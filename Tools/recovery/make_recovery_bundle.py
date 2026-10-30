#!/usr/bin/env python3
"""Builds the development-only recovery data of the 2026-10-28 iOS incident.

Reads the Mac backups (read only), checks every source against its SHA256SUMS.txt and against the
second copy, takes only the whitelisted, confirmed values (docs/recovery/IOS_DATA_INCIDENT_20261028.md)
and writes them as a C# file compiled into the Editor and development builds only:

    Assets/Game/Verification/Recovery/IosIncident20261028Bundle.cs   (untracked, never committed)

Stops without writing when anything does not match. An existing output is never replaced by different
content. Usage (from the project root):

    python3 -I Tools/recovery/make_recovery_bundle.py [--check]

--check only verifies and prints what would be written.
"""
import base64
import hashlib
import json
import os
import re
import sys

# The Mac backup next to the project folder (outside the repository); DEADLOCK_DEVICE_BACKUP overrides it.
BACKUP = os.environ.get("DEADLOCK_DEVICE_BACKUP") or os.path.abspath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "_device_backup"))
SHOTS = os.path.join(BACKUP, "2026-10-28_iphone_screenshots")
RECORDS = os.path.join(BACKUP, "2026-10-28_incident_records")
OUT = "Assets/Game/Verification/Recovery/IosIncident20261028Bundle.cs"

WEAPONS = [  # (item, weaponId, expected sha256) — the same values as IosIncident20261028 in the Dev code
    ("weapon-proto_rifle", "proto_rifle", "c8323045c1162c7461ceca3ee56fda60a88b7e2f4cadc9e1663c824f4de21456"),
    ("weapon-proto_smg", "proto_smg", "a355bbfe5e6cd51a51f24bcbfbdff09e8478b28db04e8d60397ea0d4fb79d6fd"),
]
TARGETS = ["MainButton", "Points", "Settings", "Crate", "Window", "Row"]
LAYOUT_LOGS = ["evidence_logs/final50_cap_log.txt", "evidence_logs/recover_cap_log.txt"]
PANEL_LOG = "evidence_logs/integ_dev_log.txt"


def die(msg):
    print("STOP: " + msg, file=sys.stderr)
    sys.exit(1)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read_sums(path):
    sums = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            parts = line.split()
            if len(parts) >= 2:
                sums[parts[-1]] = parts[0]
    return sums


def checked(base, rel, sums, key):
    with open(os.path.join(base, rel), "rb") as f:
        data = f.read()
    if sums.get(key) != sha(data):
        die(f"SHA-256 mismatch: {os.path.join(base, rel)}")
    return data


def main():
    check_only = "--check" in sys.argv[1:]
    shot_sums = read_sums(os.path.join(SHOTS, "SHA256SUMS.txt"))
    rec_sums = read_sums(os.path.join(RECORDS, "SHA256SUMS.txt"))

    files = []
    for item, wid, expected in WEAPONS:
        key = f"Documents/WeaponBalanceLab/{wid}.json"
        a = checked(SHOTS, f"run1/WeaponBalanceLab/{wid}.json", shot_sums, key)
        b = checked(SHOTS, f"run2/WeaponBalanceLab/{wid}.json", shot_sums, key)
        if a != b or sha(a) != expected:
            die(f"{wid}: copies differ or not the approved content")
        profile = json.loads(a.decode("utf-8"))
        if profile.get("weaponId") != wid or profile.get("profileVersion") != 1:
            die(f"{wid}: weaponId / profileVersion")
        files.append({"item": item, "weaponId": wid, "contentBase64": base64.b64encode(a).decode("ascii"), "sha256": expected})

    layouts = set()
    for rel in LAYOUT_LOGS:
        text = checked(RECORDS, rel, rec_sums, rel).decode("utf-8")
        for m in re.finditer(r"^saved layout at (start|end): (\{.*\})\s*$", text, re.M):
            layouts.add(m.group(2))
    if len(layouts) != 1:
        die(f"home layout: expected one value in the records, found {len(layouts)}")
    layout = layouts.pop()
    data = json.loads(layout)
    if data.get("layoutVersion") != 2 or len(data.get("items", [])) != 8:
        die("home layout: version / items")
    logo = [i for i in data["items"] if i["id"] == "logo"][0]
    if abs(logo["scale"] - 1.1) > 1e-5:
        die("home layout: logo scale is not 1.1")

    panel_text = checked(RECORDS, PANEL_LOG, rec_sums, PANEL_LOG).decode("utf-8")
    expected_line = "overrides before: " + " ".join(f"{t}=50" for t in TARGETS) + " compareLegacy=False"
    if expected_line not in panel_text:
        die("panel transparency / compare: record not found")

    prefs = [{"item": "home-layout", "key": "deadlock.uiLayout.home", "kind": "string", "stringValue": layout, "intValue": 0,
              "sha256": sha(layout.encode("utf-8"))}]
    for t in TARGETS:
        prefs.append({"item": "panel-transparency", "key": "deadlock.dev.home.panelTransparency." + t, "kind": "int",
                      "stringValue": "", "intValue": 50, "sha256": ""})
    prefs.append({"item": "panel-compare", "key": "deadlock.dev.home.panelCompareLegacy", "kind": "int", "stringValue": "",
                  "intValue": 0, "sha256": ""})

    bundle = {"bundleId": "ios-incident-20261028", "formatVersion": 1,
              "source": "Mac backup 2026-10-28 (run1=run2, SHA256SUMS) and device logs of 2026-10-27",
              "prefs": prefs, "files": files}
    bundle_json = json.dumps(bundle, ensure_ascii=True, separators=(",", ":"))

    cs = (
        "#if UNITY_EDITOR || DEVELOPMENT_BUILD\n"
        "// GENERATED by Tools/recovery/make_recovery_bundle.py from the checked Mac backup. VERIFICATION ONLY: never\n"
        "// committed, compiled into the Editor and development builds only. Inert unless the app is launched with\n"
        "// DEADLOCK_RECOVERY (see DeviceDataRecoveryBoot).\n"
        "namespace Deadlock.Verification\n"
        "{\n"
        "    public static class IosIncident20261028Bundle\n"
        "    {\n"
        f"        public const string BundleSha256 = \"{sha(bundle_json.encode('utf-8'))}\";\n"
        "        public const string Json = @\"" + bundle_json.replace('"', '""') + "\";\n"
        "\n"
        "        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]\n"
        "        static void Register() => Deadlock.Dev.DeviceDataRecoveryBoot.RegisterBundle(Json);\n"
        "    }\n"
        "}\n"
        "#endif\n"
    )

    print("home layout sha256:", sha(layout.encode("utf-8")))
    print("bundle sha256:", sha(bundle_json.encode("utf-8")), "bytes:", len(bundle_json))
    for f in files:
        print(f["weaponId"], f["sha256"])
    if check_only:
        return
    if os.path.exists(OUT):
        with open(OUT, encoding="utf-8") as f:
            if f.read() == cs:
                print("unchanged:", OUT)
                return
        die(f"{OUT} exists with other content (not replaced)")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(cs)
    print("written:", OUT)


if __name__ == "__main__":
    main()
