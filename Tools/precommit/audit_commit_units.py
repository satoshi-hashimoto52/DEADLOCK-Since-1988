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
    must name earlier units;
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

    # Dependencies: types first declared in a unit's new files.
    order = {u["id"]: i for i, u in enumerate(units)}
    declared = {}
    for u in units:
        for p in u["paths"]:
            if p.endswith(".cs") and status_of.get(p, "").strip() == "??":
                with open(os.path.join(ROOT, p), encoding="utf-8", errors="replace") as f:
                    for name in TYPE_DECL.findall(f.read()):
                        declared.setdefault(name, u["id"])
    deps = {}
    for u in units:
        for d in u["depends_on"]:
            if d not in order or order[d] >= order[u["id"]]:
                problems.append(f"{u['id']} の depends_on {d} は、前の単位ではありません")
        needed = set()
        for p in u["paths"]:
            if not p.endswith(".cs") or not os.path.isfile(os.path.join(ROOT, p)):
                continue
            with open(os.path.join(ROOT, p), encoding="utf-8", errors="replace") as f:
                text = f.read()
            for name, home in declared.items():
                if home != u["id"] and re.search(r"\b" + re.escape(name) + r"\b", text):
                    needed.add(home)
        deps[u["id"]] = sorted(needed)
        for d in needed - set(u["depends_on"]):
            problems.append(f"{u['id']} は {d} の新しい型を使っていますが、depends_on にありません")

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
                         "depends_on": u["depends_on"], "uses_types_of": deps[u["id"]]} for u in units],
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
