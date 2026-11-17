#!/usr/bin/env python3
"""Regression tests of Tools/precommit/audit_commit_units.py (dependency check: no false positives, no misses).

Each test builds a small throwaway Git repository with Unity-like files (asmdef, .cs, .meta), a commit_units.json and
a copy of the audit, runs the audit there and checks its result. Nothing in the real repository is read or changed.
Run (from the project root):  python3 -I -m unittest discover -s Tools/precommit -p 'test_*.py' -v
"""
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
# AUDIT_UNDER_TEST: another copy of the audit to run the repository tests against (e.g. the version before a change).
AUDIT = os.environ.get("AUDIT_UNDER_TEST") or os.path.join(HERE, "audit_commit_units.py")
sys.path.insert(0, HERE)
import audit_commit_units as audit  # noqa: E402

GIT_ENV = {"GIT_AUTHOR_NAME": "audit-test", "GIT_COMMITTER_NAME": "audit-test",
           "GIT_AUTHOR_EMAIL": "audit-test" + "@" + "example.invalid",
           "GIT_COMMITTER_EMAIL": "audit-test" + "@" + "example.invalid"}


def asmdef(name, refs=()):
    return json.dumps({"name": name, "references": list(refs)})


class Repo:
    """A throwaway repository. `units` maps unit id -> (depends_on, {path: content}); metas are added automatically."""

    def __init__(self, units, extra_untracked=None, duplicate=None):
        self.dir = tempfile.mkdtemp(prefix="audit-test-")
        run = lambda *a: subprocess.run(["git", "-C", self.dir, *a], check=True, capture_output=True,
                                        env={**os.environ, **GIT_ENV})
        run("init", "-q")
        os.makedirs(os.path.join(self.dir, "Tools", "precommit"))
        shutil.copy(AUDIT, os.path.join(self.dir, "Tools", "precommit", "audit_commit_units.py"))
        plan_units = []
        for uid, (depends_on, files) in units.items():
            paths = []
            for path, content in files.items():
                self._write(path, content)
                paths.append(path)
                if path.startswith("Assets/"):
                    self._write(path + ".meta", "fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\n")
                    paths.append(path + ".meta")
                    folder = os.path.dirname(path)
                    while folder != "Assets":
                        meta = folder + ".meta"
                        if not os.path.exists(os.path.join(self.dir, meta)):
                            self._write(meta, "fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\nfolderAsset: yes\n")
                            paths.append(meta)
                        folder = os.path.dirname(folder)
            plan_units.append({"id": uid, "title": uid, "depends_on": depends_on, "paths": paths})
        if duplicate:
            src, dst = duplicate
            plan_units[[u["id"] for u in plan_units].index(dst)]["paths"].extend(
                [p for p in plan_units[[u["id"] for u in plan_units].index(src)]["paths"] if p.endswith(".cs")][:1])
        for path, content in (extra_untracked or {}).items():
            self._write(path, content)
        self._write("Tools/precommit/commit_units.json",
                    json.dumps({"_note": "test", "exclude": {"paths": []}, "units": plan_units}, indent=2))
        # The audit and the plan are committed (clean), so only the fixture files are changes.
        run("add", "--", "Tools/precommit")
        run("commit", "-q", "-m", "audit fixture")

    def _write(self, path, content):
        full = os.path.join(self.dir, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", encoding="utf-8") as f:
            f.write(content)

    def audit(self):
        env = {**os.environ, "DEADLOCK_PRECOMMIT_DENYLIST": os.path.join(self.dir, "no-denylist"),
               "DEADLOCK_ASSETPIPE": os.path.join(self.dir, "no-assetpipe")}
        r = subprocess.run([sys.executable, "-I", os.path.join(self.dir, "Tools", "precommit", "audit_commit_units.py"),
                            "--json"], capture_output=True, text=True, env=env)
        result = json.loads(r.stdout)
        result["exit"] = r.returncode
        return result

    def close(self):
        shutil.rmtree(self.dir, ignore_errors=True)


WIDGET = """namespace Game.Parts
{
    public class Widget { }
    public sealed class Panel
    {
        public sealed class Body { }
        public enum Kind { A, B }
    }
}
"""


class AuditDependencyTests(unittest.TestCase):
    def check(self, units, expect_ok, contains=(), **kw):
        repo = Repo(units, **kw)
        try:
            r = repo.audit()
        finally:
            repo.close()
        text = "\n".join(r["problems"])
        self.assertEqual(expect_ok, r["ok"], text)
        self.assertEqual(0 if expect_ok else 1, r["exit"])
        for c in contains:
            self.assertIn(c, text)
        return r

    # ------------------------------------------------------------------ PASS: no false positives

    def test_pass_type_name_only_in_comments_and_xml_documentation(self):
        early = """namespace Game.Core
{
    /// <summary>Uses no <see cref="Game.Parts.Widget"/>; a Widget is drawn later. Panel.Body too.</summary>
    public static class Core
    {
        // new Widget() would be a dependency, but this is a comment: Widget, Panel.Body
        /* Widget */ public static int Count = 1;
    }
}
"""
        self.check({"1-core": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Core.cs": early}),
                    "2-parts": (["1-core"], {"Assets/Code/A/Widget.cs": WIDGET})}, True)

    def test_pass_type_name_only_in_tooltip_and_strings(self):
        early = '''using UnityEngine;
namespace Game.Core
{
    public class Core
    {
        [Tooltip("Widget size and Kind of gun (Panel.Body).")]
        public float size;
        public string A => @"Widget ""Panel.Body""";
        public string B(int n) => $"{n} Widget {{Panel.Body}}";
        public char C = 'W';
    }
}
'''
        self.check({"1-core": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Core.cs": early}),
                    "2-parts": (["1-core"], {"Assets/Code/A/Widget.cs": WIDGET})}, True)

    def test_pass_identifiers_that_are_members_namespaces_or_nested_types_out_of_scope(self):
        early = """using Game.Body;
namespace Game.Kind
{
    public class Core
    {
        public int Kind;                  // a field named like the nested enum Panel.Kind
        public int State => other.Kind;   // member access of another object
        void Body() { }                   // a method named like the nested class Panel.Body
        void Run() { Body(); this.Kind = 1; }
        Core other;
    }
}
"""
        self.check({"1-core": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Core.cs": early}),
                    "2-parts": (["1-core"], {"Assets/Code/A/Widget.cs": WIDGET})}, True)

    def test_pass_assembly_that_cannot_see_the_later_assembly(self):
        # Game.A does not reference Game.B: the compiler cannot resolve Widget from A, so it is not a dependency.
        early = "namespace Game.Core { public class Core { object Widget; } }\n"
        self.check({"1-core": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Core.cs": early}),
                    "2-parts": (["1-core"], {"Assets/Code/B/B.asmdef": asmdef("Game.B", ["Game.A"]),
                                             "Assets/Code/B/Widget.cs": WIDGET})}, True)

    def test_pass_correct_asmdef_dependency_on_an_earlier_unit(self):
        user = "using Game.Parts;\nnamespace Game.Use { public class User { Widget w = new Widget(); Panel.Body b; } }\n"
        r = self.check({"1-parts": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Widget.cs": WIDGET}),
                        "2-user": (["1-parts"], {"Assets/Code/B/B.asmdef": asmdef("Game.B", ["Game.A"]),
                                                 "Assets/Code/B/User.cs": user})}, True)
        self.assertEqual(["1-parts"], [u for u in r["units"] if u["id"] == "2-user"][0]["uses_types_of"])

    # ------------------------------------------------------------------ FAIL: real problems are still found

    def test_fail_real_reference_to_a_type_of_a_later_unit(self):
        early = "using Game.Parts;\nnamespace Game.Core { public class Core { Widget w = new Widget(); } }\n"
        self.check({"1-core": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Core.cs": early}),
                    "2-parts": (["1-core"], {"Assets/Code/A/Widget.cs": WIDGET})}, False,
                   contains=["1-core は後の単位 2-parts", "Game.Parts.Widget"])

    def test_fail_qualified_nested_type_and_interpolation_hole(self):
        early = ("namespace Game.Core { public class Core { string S => $\"{typeof(Game.Parts.Panel.Body)}\"; } }\n")
        self.check({"1-core": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Core.cs": early}),
                    "2-parts": (["1-core"], {"Assets/Code/A/Widget.cs": WIDGET})}, False,
                   contains=["1-core は後の単位 2-parts", "Game.Parts.Panel"])

    def test_fail_asmdef_reference_to_an_assembly_of_a_later_unit(self):
        self.check({"1-user": ([], {"Assets/Code/B/B.asmdef": asmdef("Game.B", ["Game.A"]),
                                    "Assets/Code/B/User.cs": "namespace Game.Use { public class User { } }\n"}),
                    "2-parts": (["1-user"], {"Assets/Code/A/A.asmdef": asmdef("Game.A"),
                                             "Assets/Code/A/Widget.cs": WIDGET})}, False,
                   contains=["1-user は後の単位 2-parts", "Game.A のアセンブリ"])

    def test_fail_code_in_an_assembly_whose_asmdef_is_in_a_later_unit(self):
        self.check({"1-code": ([], {"Assets/Code/A/Widget.cs": WIDGET}),
                    "2-asm": (["1-code"], {"Assets/Code/A/A.asmdef": asmdef("Game.A")})}, False,
                   contains=["1-code は後の単位 2-asm"])

    def test_fail_missing_depends_on_for_an_earlier_unit(self):
        user = "using Game.Parts;\nnamespace Game.Use { public class User { Widget w; } }\n"
        self.check({"1-parts": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Widget.cs": WIDGET}),
                    "2-user": ([], {"Assets/Code/A/User.cs": user})}, False,
                   contains=["2-user は単位 1-parts"])

    def test_fail_cycle_between_units(self):
        a = "namespace Game.One { public class Alpha { Game.Two.Beta b; } }\n"
        b = "namespace Game.Two { public class Beta { Game.One.Alpha a; } }\n"
        self.check({"1-alpha": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Alpha.cs": a}),
                    "2-beta": (["1-alpha"], {"Assets/Code/A/Beta.cs": b})}, False,
                   contains=["循環", "1-alpha"])

    def test_fail_unassigned_file(self):
        self.check({"1-parts": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Widget.cs": WIDGET})},
                   False, contains=["notes.md がどの単位にも除外にも入っていません"],
                   extra_untracked={"notes.md": "not planned\n"})

    def test_fail_file_in_two_units(self):
        self.check({"1-parts": ([], {"Assets/Code/A/A.asmdef": asmdef("Game.A"), "Assets/Code/A/Widget.cs": WIDGET}),
                    "2-more": (["1-parts"], {"Assets/Code/A/More.cs": "namespace Game.M { class More { } }\n"})},
                   False, contains=["の両方にあります"], duplicate=("1-parts", "2-more"))


class StripAndDeclarationTests(unittest.TestCase):
    def test_strip_keeps_code_and_line_numbers(self):
        src = 'int a = 1; // Widget\n/* Widget\n Widget */ var s = "Widget"; var v = @"x ""Widget"" y";\n' \
              'var i = $"{new Widget()} Widget {{Widget}}"; char c = \'"\';\nWidget w;\n'
        out = audit.strip_csharp(src)
        self.assertEqual(src.count("\n"), out.count("\n"))
        # Only the interpolation hole and the code line remain: comments and literals are blank.
        self.assertEqual(2, len(re.findall(r"\bWidget\b", out)))
        self.assertIn("new Widget()", out)
        self.assertTrue(out.rstrip().endswith("Widget w;"))

    def test_declarations_with_nesting_and_namespaces(self):
        decl = audit.csharp_declarations(audit.strip_csharp(WIDGET))
        self.assertIn(("Widget", (), "Game.Parts"), decl)
        self.assertIn(("Body", ("Panel",), "Game.Parts"), decl)
        self.assertIn(("Kind", ("Panel",), "Game.Parts"), decl)
        self.assertEqual([("Thing", (), "File.Scoped")],
                         audit.csharp_declarations(audit.strip_csharp("namespace File.Scoped;\nclass Thing { }\n")))


if __name__ == "__main__":
    unittest.main()
