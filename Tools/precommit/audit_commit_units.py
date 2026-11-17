#!/usr/bin/env python3
"""Read-only audit of the planned commit units (Tools/precommit/commit_units.json) before anything is committed.

Never stages, commits or changes a file: it only runs read-only git commands (status, diff, ls-files, check-ignore)
and, for a unit tied to an AssetPipeline receipt, `assetpipe commit-plan --json` (also read only).

Checks:
  - every changed or new file is in exactly one unit or in `exclude`; every unit path exists or is a deletion;
  - excluded paths are in no unit and are ignored by Git (.gitignore) unless they are tracked;
  - Unity: every file under Assets/ and its .meta are in the same unit (new folders too); no GUID is declared twice;
  - personal / device information: generic patterns (home paths, e-mail addresses, device UUIDs, app containers,
    private keys) in new files and in the added lines of changed files, plus the literal terms of a local denylist
    kept outside the repository (reported by line number only, never printed);
  - dependencies: a unit that uses a type first added in another unit must list it in depends_on, and depends_on
    must name earlier units. A use is a real reference in the code (comments, XML documentation and string literals
    removed; namespace / using directives, members of other objects (`x.Name`) and nested types out of their scope are
    not references) from an assembly that can see the type's assembly (Unity asmdef references). An asmdef that
    references, or a .cs file that is compiled into, an assembly whose asmdef is new in another unit depends on that
    unit too. Cycles between units are reported. Regression tests: Tools/precommit/test_audit_commit_units.py;
  - the index is empty (nothing staged);
  - an AssetPipeline unit: the receipt's commit plan is consistent and all its files are in that unit.

Usage (from the project root):  python3 -I Tools/precommit/audit_commit_units.py [--json]
Environment: DEADLOCK_PRECOMMIT_DENYLIST (default ../_device_backup/precommit_denylist.local.txt),
             DEADLOCK_ASSETPIPE (default ../AssetPipeline/assetpipe).
Exit code 0 when no problem was found, 1 otherwise.
"""
import json
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
UNITS_FILE = os.path.join(ROOT, "Tools", "precommit", "commit_units.json")

SENSITIVE = {
    "home_path": re.compile(r"(?:^|[^A-Za-z0-9._-])(?:/Users|/home)/[A-Za-z0-9._-]+/"),  # absolute paths only, not ".../home/..."
    "private_tmp": re.compile(r"/private/(?:tmp|var)/"),
    "email": re.compile(r"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}\b"),
    "device_uuid": re.compile(r"\b[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\b"),
    "device_udid": re.compile(r"\b[0-9A-F]{8}-[0-9A-F]{16}\b"),
    "app_container": re.compile("Containers/Data/" + "Application|/var/" + "mobile/"),  # split so this file does not match itself
    "private_key": re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----"),
}
#: Addresses that may appear in commits (attribution lines).
ALLOWED_EMAILS = {"noreply@anthropic.com"}
TEXT = (".cs", ".md", ".txt", ".json", ".asset", ".meta", ".unity", ".prefab", ".py", ".sh", ".tsv", ".csv", ".yml",
        ".yaml", ".asmdef", ".xml", ".plist", ".mat", ".gitignore")
TYPE_DECL = re.compile(r"\b(?:class|struct|enum|interface)\s+([A-Z][A-Za-z0-9_]{3,})")
TYPE_DECL_AT = re.compile(r"\b(?:class|struct|enum|interface|record)\s+([A-Za-z_][A-Za-z0-9_]*)")
NAMESPACE_DECL = re.compile(r"\bnamespace\s+([A-Za-z_][\w.]*)\s*([{;])")
# Directives that name namespaces, not types (`using X.Y;`, `namespace X.Y`); `using static` and aliases stay code.
NAMESPACE_DIRECTIVE = re.compile(r"^[ \t]*(?:using[ \t]+(?!static\b)[\w.]+[ \t]*;|namespace[ \t]+[\w.]+)", re.M)


def strip_csharp(text):
    """C# source with comments (incl. XML documentation), string and character literals blanked out.

    Interpolation holes (`$"... {code} ..."`) stay code. Newlines are kept, so line numbers do not move.
    """
    out = list(text)
    n = len(text)
    i = 0
    # Stack of string contexts we are inside: ("str"|"vstr", interpolated, brace depth of the hole or -1).
    stack = []

    def blank(a, b):
        for k in range(a, b):
            if out[k] != "\n":
                out[k] = " "

    while i < n:
        c = text[i]
        if stack and stack[-1][2] < 0:  # inside the literal part of a string
            kind, interp, _ = stack[-1]
            if kind == "str" and c == "\\":
                blank(i, min(i + 2, n))
                i += 2
                continue
            if c == '"':
                if kind == "vstr" and i + 1 < n and text[i + 1] == '"':
                    blank(i, i + 2)
                    i += 2
                    continue
                stack.pop()
                i += 1
                continue
            if interp and c == "{":
                if i + 1 < n and text[i + 1] == "{":
                    blank(i, i + 2)
                    i += 2
                    continue
                stack[-1] = (kind, interp, 0)  # into the hole: code again
                i += 1
                continue
            blank(i, i + 1)
            i += 1
            continue
        # Code (top level, or inside an interpolation hole).
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            blank(i, j)
            i = j
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            blank(i, j)
            i = j
            continue
        if c == "'":
            j = i + 1
            while j < n and text[j] != "'" and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            blank(i, min(j + 1, n))
            i = j + 1
            continue
        m = re.match(r'(\$@|@\$|\$|@)?"', text[i:i + 3])
        if m:
            prefix = m.group(1) or ""
            stack.append(("vstr" if "@" in prefix else "str", "$" in prefix, -1))
            i += len(m.group(0))
            continue
        if stack:  # inside a hole: track braces to find its end
            kind, interp, depth = stack[-1]
            if c == "{":
                stack[-1] = (kind, interp, depth + 1)
            elif c == "}":
                stack[-1] = (kind, interp, depth - 1 if depth > 0 else -1)
        i += 1
    return "".join(out)


def csharp_declarations(code):
    """Types declared in stripped C# code: (name, enclosing type names outermost first, namespace)."""
    events = []
    for m in NAMESPACE_DECL.finditer(code):
        events.append((m.start(), "ns", m.group(1), m.group(2)))
    for m in TYPE_DECL_AT.finditer(code):
        events.append((m.start(), "type", m.group(1), None))
    events.sort()
    result = []
    depth = 0
    scopes = []  # (kind, name, depth at which the scope's body opened)
    pending = None  # a declaration waiting for its opening brace
    ns_file = None  # file-scoped namespace
    ev = 0
    for pos, ch in enumerate(code):
        while ev < len(events) and events[ev][0] == pos:
            _, kind, name, term = events[ev]
            ev += 1
            if kind == "ns":
                if term == ";":
                    ns_file = name
                else:
                    pending = ("ns", name)
            else:
                outers = tuple(s[1] for s in scopes if s[0] == "type")
                nss = [s[1] for s in scopes if s[0] == "ns"] or ([ns_file] if ns_file else [])
                result.append((name, outers, ".".join(nss)))
                pending = ("type", name)
        if ch == "{":
            depth += 1
            if pending:
                scopes.append((pending[0], pending[1], depth))
                pending = None
        elif ch == "}":
            if scopes and scopes[-1][2] == depth:
                scopes.pop()
            depth -= 1
        elif ch == ";" and pending and pending[0] == "type":
            pending = None  # e.g. a forward-style declaration without a body
    return result


def load_asmdefs(root):
    """Assembly definitions under Assets/: {folder: (name, [referenced names], path)} (GUID references resolved)."""
    found, by_guid = {}, {}
    for base, _dirs, files in os.walk(os.path.join(root, "Assets")):
        for name in files:
            if not name.endswith(".asmdef"):
                continue
            full = os.path.join(base, name)
            try:
                with open(full, encoding="utf-8-sig") as f:
                    data = json.load(f)
            except (OSError, ValueError):
                continue
            rel = os.path.relpath(full, root).replace(os.sep, "/")
            found[os.path.dirname(rel)] = (data.get("name", ""), list(data.get("references", [])), rel)
            guid = read_guid(full + ".meta")
            if guid:
                by_guid["GUID:" + guid] = data.get("name", "")
    return {folder: (n, [by_guid.get(r, r) for r in refs], p) for folder, (n, refs, p) in found.items()}


def assembly_of(path, asmdefs):
    """(assembly name, asmdef path or None) of a C# file: the nearest asmdef folder above it."""
    folder = os.path.dirname(path)
    while folder:
        if folder in asmdefs:
            return asmdefs[folder][0], asmdefs[folder][2]
        folder = os.path.dirname(folder)
    return None, None


def type_reference_line(code, name, outers, namespace, user_outers):
    """Line number (1-based) of the first real reference to a type in stripped code, or None.

    Real: the name as a token that is not a member access of something else. `Outer.Name` (nested types) and
    `Ns.Name` (top-level types) count; `x.Name` (a property, method or field of another object) does not. A nested
    type counts unqualified only inside its own outer type or where `using static <Outer>` / a class deriving from
    it brings it into scope. Namespace / using directives are not references.
    """
    code = NAMESPACE_DIRECTIVE.sub(lambda m: " " * len(m.group(0)), code)
    outer = outers[-1] if outers else None
    in_scope = bool(outer) and (outer in user_outers or re.search(r"\busing\s+static\s+[\w.]*\b" + re.escape(outer) + r"\s*;", code)
                                or re.search(r":\s*(?:[\w.]+\s*,\s*)*[\w.]*\b" + re.escape(outer) + r"\b", code))
    ns_last = namespace.split(".")[-1] if namespace else None
    for m in re.finditer(r"(?<![\w@])" + re.escape(name) + r"(?![\w])", code):
        before = code[:m.start()].rstrip()
        if before.endswith(".") and not before.endswith(".."):
            q = re.search(r"([A-Za-z_][A-Za-z0-9_]*)\s*$", before[:-1])
            qualifier = q.group(1) if q else None
            if outer:
                ok = qualifier == outer
            else:
                ok = qualifier is not None and qualifier == ns_last
            if not ok:
                continue
        elif outer and not in_scope:
            continue
        return code.count("\n", 0, m.start()) + 1
    return None


def git(*args):
    return subprocess.run(["git", "-C", ROOT, *args], capture_output=True, text=True, check=True).stdout


def changed_files():
    """(path, porcelain status) of every changed, new or deleted file, untracked folders expanded."""
    out = []
    for line in git("status", "--porcelain=v1", "--untracked-files=all").splitlines():
        out.append((line[3:].strip().strip('"'), line[:2]))
    return out


def covers(pattern, path):
    return path == pattern or (pattern.endswith("/") and path.startswith(pattern))


def denylist():
    path = os.environ.get("DEADLOCK_PRECOMMIT_DENYLIST") or os.path.join(ROOT, "..", "_device_backup",
                                                                        "precommit_denylist.local.txt")
    if not os.path.isfile(path):
        return [], False
    with open(path, encoding="utf-8") as f:
        return [t.strip() for t in f if t.strip() and not t.startswith("#")], True


def lines_to_scan(path, status):
    """New files: every line. Changed files: only the added lines (what this commit would publish)."""
    full = os.path.join(ROOT, path)
    if not path.endswith(TEXT) and not os.path.basename(path).startswith(".git"):
        return []
    if status.strip() == "??":
        if not os.path.isfile(full):
            return []
        with open(full, encoding="utf-8", errors="replace") as f:
            return list(enumerate(f.read().splitlines(), 1))
    if "D" in status:
        return []
    out, n = [], 0
    for line in git("diff", "-U0", "--", path).splitlines():
        m = re.match(r"@@ -\d+(?:,\d+)? \+(\d+)", line)
        if m:
            n = int(m.group(1))
        elif line.startswith("+") and not line.startswith("+++"):
            out.append((n, line[1:]))
            n += 1
    return out


def scan(path, status, terms):
    hits = []
    for n, text in lines_to_scan(path, status):
        for kind, rx in SENSITIVE.items():
            for m in rx.finditer(text):
                if kind == "email" and m.group(0) in ALLOWED_EMAILS:
                    continue
                hits.append(f"{kind}:{n}")
                break
        if any(t in text for t in terms):
            hits.append(f"denylist:{n}")
    return hits


def read_guid(full):
    try:
        with open(full, encoding="utf-8", errors="replace") as f:
            for line in f:
                if line.startswith("guid:"):
                    return line.split(":", 1)[1].strip()
    except OSError:
        pass
    return None


def main():
    as_json = "--json" in sys.argv[1:]
    with open(UNITS_FILE, encoding="utf-8") as f:
        spec = json.load(f)
    units = spec["units"]
    exclude = spec["exclude"]["paths"]
    terms, deny_found = denylist()
    problems, notes = [], []
    if not deny_found:
        notes.append("ローカルの禁止語リストがありません（一般的なパターンだけで検査）")

    changes = changed_files()
    status_of = dict(changes)
    owner = {}
    for u in units:
        for p in u["paths"]:
            if p in owner:
                problems.append(f"{p} が {owner[p]} と {u['id']} の両方にあります")
            owner[p] = u["id"]
            if any(covers(e, p) for e in exclude):
                problems.append(f"{p}（{u['id']}）は除外対象です")

    unassigned = [p for p, _ in changes if p not in owner and not any(covers(e, p) for e in exclude)]
    for p in unassigned:
        problems.append(f"{p} がどの単位にも除外にも入っていません")
    committed_units = []
    for u in units:
        pending = [p for p in u["paths"] if p in status_of]
        if not pending:
            # Every file of the unit is clean: committed (or a wrong path, which ls-files tells apart).
            untracked = [p for p in u["paths"] if not git("ls-files", "--", p).strip()]
            if untracked:
                problems.append(f"{u['id']}: 追跡されていないパスがあります: {untracked[:3]}")
            else:
                committed_units.append(u["id"])
            continue
        if len(pending) != len(u["paths"]):
            done = [p for p in u["paths"] if p not in status_of]
            problems.append(f"{u['id']} の一部だけがコミット済みです（{done[:3]}）。単位を分けてコミットしない")
        for p in pending:
            if not os.path.exists(os.path.join(ROOT, p)) and "D" not in status_of[p]:
                problems.append(f"{p}（{u['id']}）がありません")
    for u in units:
        if u["id"] not in committed_units:
            continue
        for d in u["depends_on"]:
            if d not in committed_units:
                problems.append(f"{u['id']} はコミット済みですが、先に必要な {d} がまだです")
    if committed_units:
        notes.append("コミット済みの単位: " + ", ".join(committed_units))

    # Excluded paths: never tracked-and-changed silently, and ignored when untracked.
    for e in exclude:
        tracked = git("ls-files", "--", e).strip()
        if tracked:
            notes.append(f"除外対象 {e} は追跡済み（.gitignore では守れない。コミットに含めないことを毎回確認する）")
        elif os.path.exists(os.path.join(ROOT, e.rstrip("/"))):
            ignored = subprocess.run(["git", "-C", ROOT, "check-ignore", "-q", e.rstrip("/")]).returncode == 0
            if not ignored:
                problems.append(f"除外対象 {e} が .gitignore に入っていません")

    # Unity: an asset and its .meta in the same unit; GUIDs unique.
    for p, uid in owner.items():
        if not p.startswith("Assets/"):
            continue
        partner = p[:-5] if p.endswith(".meta") else p + ".meta"
        partner_full = os.path.join(ROOT, partner)
        if p.endswith(".meta"):
            if os.path.exists(partner_full) and partner in status_of and owner.get(partner) != uid:
                problems.append(f"{p} と {partner} が別の単位です")
        elif partner in status_of and owner.get(partner) != uid:
            problems.append(f"{p} の .meta（{partner}）が同じ単位にありません")
        elif not os.path.exists(partner_full):
            problems.append(f"{p} の .meta がありません")
    guids = {}
    for base, _dirs, files in os.walk(os.path.join(ROOT, "Assets")):
        for name in files:
            if name.endswith(".meta"):
                full = os.path.join(base, name)
                g = read_guid(full)
                if g:
                    guids.setdefault(g, []).append(os.path.relpath(full, ROOT))
    for g, metas in guids.items():
        if len(metas) > 1:
            problems.append(f"GUID {g} が複数の .meta にあります: {metas}")

    # Personal and device information.
    sensitive = {}
    for p, s in changes:
        if any(covers(e, p) for e in exclude):
            continue
        hits = scan(p, s, terms)
        if hits:
            sensitive[p] = hits
            problems.append(f"{p} に個人情報・端末情報らしきものがあります（{', '.join(hits[:6])}）")

    # Dependencies between units, from the code (comments, XML documentation and string literals removed) and the
    # Unity assembly definitions:
    #  - a type first declared in a unit's new .cs file is used by another unit only where the code really names it
    #    (not a namespace, not a member of another object, not a nested type out of its scope) AND the using file's
    #    assembly can see the declaring assembly (the same assembly, a referenced one, or no asmdef = Assembly-CSharp);
    #  - an asmdef that references an assembly whose asmdef is new in another unit depends on that unit;
    #  - a .cs file compiled into an assembly whose asmdef is new in another unit depends on that unit.
    order = {u["id"]: i for i, u in enumerate(units)}
    asmdefs = load_asmdefs(ROOT)
    asm_by_name = {n: (folder, p) for folder, (n, _r, p) in asmdefs.items()}
    asm_refs = {n: set(r) for _f, (n, r, _p) in asmdefs.items()}
    code_cache = {}

    def code_of(p):
        if p not in code_cache:
            with open(os.path.join(ROOT, p), encoding="utf-8", errors="replace") as f:
                code_cache[p] = strip_csharp(f.read())
        return code_cache[p]

    declared = []  # (name, outers, namespace, unit, file, assembly)
    for u in units:
        for p in u["paths"]:
            if p.endswith(".cs") and status_of.get(p, "").strip() == "??" and os.path.isfile(os.path.join(ROOT, p)):
                asm, _ = assembly_of(p, asmdefs)
                for name, outers, ns in csharp_declarations(code_of(p)):
                    declared.append((name, outers, ns, u["id"], p, asm))
    # A new asmdef belongs to the unit it is listed in.
    new_asm_unit = {}
    for u in units:
        for p in u["paths"]:
            if p.endswith(".asmdef") and status_of.get(p, "").strip() == "??":
                for folder, (n, _r, ap) in asmdefs.items():
                    if ap == p:
                        new_asm_unit[n] = u["id"]

    def can_see(user_asm, decl_asm):
        if decl_asm is None or user_asm == decl_asm:
            return True
        if user_asm is None:  # Assembly-CSharp sees every assembly definition (auto-referenced)
            return True
        return decl_asm in asm_refs.get(user_asm, set())

    deps, reasons = {}, {}
    for u in units:
        for d in u["depends_on"]:
            if d not in order or order[d] >= order[u["id"]]:
                problems.append(f"{u['id']} の depends_on {d} は、前の単位ではありません")
        needed = {}
        for p in u["paths"]:
            full = os.path.join(ROOT, p)
            if p.endswith(".asmdef") and os.path.isfile(full):
                for folder, (n, refs, ap) in asmdefs.items():
                    if ap != p:
                        continue
                    for r in refs:
                        home = new_asm_unit.get(r)
                        if home and home != u["id"]:
                            needed.setdefault(home, f"{p} が {r} のアセンブリ（{asm_by_name[r][1]}）を参照")
            if not p.endswith(".cs") or not os.path.isfile(full):
                continue
            user_asm, user_asmdef = assembly_of(p, asmdefs)
            if user_asm in new_asm_unit and new_asm_unit[user_asm] != u["id"]:
                needed.setdefault(new_asm_unit[user_asm], f"{p} は {user_asm} のアセンブリ（{user_asmdef}）に入る")
            code = code_of(p)
            tokens = set(re.findall(r"[A-Za-z_][A-Za-z0-9_]*", code))
            user_outers = {name for name, _o, _n in csharp_declarations(code)}
            for name, outers, ns, home, decl_file, decl_asm in declared:
                if home == u["id"] or name not in tokens or decl_file == p:
                    continue
                if not can_see(user_asm, decl_asm):
                    continue  # the compiler cannot resolve it from this assembly: not a dependency
                line = type_reference_line(code, name, outers, ns, user_outers)
                if line:
                    full_name = ".".join([*(x for x in [ns] if x), *outers, name])
                    needed.setdefault(home, f"{p}:{line} が {full_name}（{decl_file}）を使う")
        deps[u["id"]] = sorted(needed)
        reasons[u["id"]] = needed
        for d in sorted(set(needed) - set(u["depends_on"])):
            later = d in order and order[d] > order[u["id"]]
            where = "後の単位" if later else "単位"
            problems.append(f"{u['id']} は{where} {d} の新しい型・アセンブリを使っていますが、depends_on にありません（{needed[d]}）")
    # Cycles between units (each one also shows up above, here as one message per cycle).
    seen_cycles = set()

    def walk(start, node, path):
        for nxt in reasons.get(node, {}):
            if nxt == start:
                cyc = path + [start]
                key = frozenset(cyc)
                if key not in seen_cycles:
                    seen_cycles.add(key)
                    problems.append("単位の依存が循環しています: " + " → ".join(cyc))
            elif nxt not in path and nxt in reasons:
                walk(start, nxt, path + [nxt])

    for u in units:
        walk(u["id"], u["id"], [u["id"]])

    staged = [p for p in git("diff", "--cached", "--name-only").splitlines() if p]
    if staged:
        problems.append("ステージ済みのファイルがあります: " + ", ".join(staged[:5]))

    # AssetPipeline units.
    pipe = os.environ.get("DEADLOCK_ASSETPIPE") or os.path.join(ROOT, "..", "AssetPipeline", "assetpipe")
    assetpipe = {}
    for u in units:
        rid = u.get("assetpipe_receipt")
        if not rid:
            continue
        if not os.path.isfile(pipe):
            problems.append(f"{u['id']}: AssetPipeline が見つからないので {rid} を確認できません")
            continue
        r = subprocess.run([pipe, "commit-plan", rid, "--json"], capture_output=True, text=True)
        try:
            plan = json.loads(r.stdout)
        except ValueError:
            problems.append(f"{u['id']}: commit-plan {rid} が失敗しました: {r.stderr.strip()[:200]}")
            continue
        assetpipe[u["id"]] = {"receipt": rid, "state": plan["state"], "ok": plan["ok"], "guid": plan["guid"]}
        problems += [f"{u['id']}（AssetPipeline {rid}）: {x}" for x in plan["problems"]]
        missing = [p for p in plan["to_commit"] if p not in u["paths"]]
        if missing:
            problems.append(f"{u['id']}: AssetPipeline の単位にあるファイルが入っていません: {missing}")

    result = {"units": [{"id": u["id"], "title": u["title"], "files": len(u["paths"]),
                         "committed": u["id"] in committed_units,
                         "depends_on": u["depends_on"], "uses_types_of": deps[u["id"]],
                         "uses_reasons": reasons[u["id"]]} for u in units],
              "changed_files": len(changes), "assigned": len([p for p, _ in changes if p in owner]),
              "excluded": len([p for p, _ in changes if any(covers(e, p) for e in exclude)]),
              "unassigned": unassigned, "staged": staged, "sensitive": sensitive, "assetpipe": assetpipe,
              "denylist_used": deny_found, "notes": notes, "problems": problems, "ok": not problems}
    if as_json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        print(f"変更ファイル {result['changed_files']} 件：単位に割り当て {result['assigned']} 件、除外 {result['excluded']} 件、"
              f"未割り当て {len(unassigned)} 件")
        for u in result["units"]:
            print(f"  {u['id']}{'（コミット済み）' if u['committed'] else ''}: {u['files']} 件  depends_on={u['depends_on'] or '-'}  使う型の単位={u['uses_types_of'] or '-'}")
        for k, v in assetpipe.items():
            print(f"  AssetPipeline {k}: {v['receipt']} {v['state']} GUID {v['guid']} {'整合' if v['ok'] else '問題あり'}")
        for n in notes:
            print(f"メモ: {n}")
        for x in problems:
            print(f"問題: {x}")
        print("結果: " + ("問題なし（コミットはしていない）" if result["ok"] else "問題あり（コミットしない）"))
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
