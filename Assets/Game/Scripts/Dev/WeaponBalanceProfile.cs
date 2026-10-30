using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Deadlock.Combat;
using UnityEngine;

namespace Deadlock.Dev
{
    /// <summary>
    /// DEVELOPMENT ONLY (Deadlock.Dev). A trial copy of one gun's performance for the balance lab: the values of a
    /// <see cref="WeaponDefinition"/> plus recoil direction and damage falloff. The weapon assets stay the source of
    /// truth and are never written: a profile is applied to a runtime clone of the definition for a test session only.
    /// Saved as JSON on the device (<see cref="WeaponBalanceStore"/>), apart from game settings.
    /// </summary>
    [Serializable]
    public sealed class WeaponBalanceProfile
    {
        public const int CurrentVersion = 1;

        public string weaponId;
        public string weaponType;
        public int profileVersion = CurrentVersion;
        public string baseCommit;
        public string savedAt;

        public float damage;
        public bool automatic;
        public float rpm;
        public int magazineSize;
        public int startReserve;
        public int reserveMax;
        public float reloadTime;
        public float projectileSpeed;
        public float maxRange;
        public float hipSpreadDegrees;
        public float adsSpreadDegrees;
        public float adsTime;
        public RecoilProfile recoil = new RecoilProfile();
        public DamageFalloffProfile falloff = new DamageFalloffProfile();

        /// <summary>Weapon type shown in the lab (from the id; future guns add their own).</summary>
        public static string TypeOf(string weaponId) => weaponId switch
        {
            "proto_rifle" => "ライフル",
            "proto_smg" => "SMG",
            _ => "未分類",
        };

        /// <summary>The definition's current values (recoil: its profile when enabled, else the earlier recoil's numbers).</summary>
        public static WeaponBalanceProfile FromDefinition(WeaponDefinition d)
        {
            var p = new WeaponBalanceProfile
            {
                weaponId = d.weaponId,
                weaponType = TypeOf(d.weaponId),
                damage = d.damage,
                automatic = d.automatic,
                rpm = 60f / d.fireInterval,
                magazineSize = d.magazineSize,
                startReserve = d.startReserve,
                reserveMax = d.reserveMax,
                reloadTime = d.reloadTime,
                projectileSpeed = d.projectileSpeed,
                maxRange = d.maxRange,
                hipSpreadDegrees = d.hipSpreadDegrees,
                adsSpreadDegrees = d.adsSpreadDegrees,
                adsTime = d.adsTime,
                falloff = d.damageFalloff != null ? d.damageFalloff.Clone() : new DamageFalloffProfile(),
            };
            if (d.recoilProfile != null && d.recoilProfile.enabled)
                p.recoil = d.recoilProfile.Clone();
            else
                p.recoil = new RecoilProfile
                {
                    enabled = false,
                    pitchDegrees = d.recoilPitchDegrees,
                    yawDegrees = d.recoilYawDegrees,
                    adsMultiplier = d.adsRecoilMultiplier,
                    recoveryFraction = Deadlock.Player.FirstPersonMotor.DefaultRecoveryFraction, // the earlier rule (the speed is the scene's)
                };
            return p;
        }

        public WeaponBalanceProfile Clone()
        {
            var c = (WeaponBalanceProfile)MemberwiseClone();
            c.recoil = recoil != null ? recoil.Clone() : new RecoilProfile();
            c.falloff = falloff != null ? falloff.Clone() : new DamageFalloffProfile();
            return c;
        }

        /// <summary>Problems that keep the profile from being applied (empty when it can be).</summary>
        public List<string> Problems()
        {
            var list = new List<string>();
            void Need(bool ok, string message)
            {
                if (!ok)
                    list.Add(message);
            }
            Need(Finite(damage) && damage >= 0f, "威力は0以上");
            Need(Finite(rpm) && rpm >= 1f && rpm <= 6000f, "RPMは1〜6000");
            Need(magazineSize >= 1, "装弾数は1以上");
            Need(startReserve >= 0 && reserveMax >= 0 && startReserve <= reserveMax, "予備弾は0以上で、開始予備弾は上限以下");
            Need(Finite(reloadTime) && reloadTime >= 0.05f, "リロード時間は0.05秒以上");
            Need(Finite(projectileSpeed) && projectileSpeed >= 1f, "弾速は1以上");
            Need(Finite(maxRange) && maxRange >= 1f, "最大射程は1以上");
            Need(Finite(hipSpreadDegrees) && hipSpreadDegrees >= 0f && Finite(adsSpreadDegrees) && adsSpreadDegrees >= 0f, "拡散は0以上");
            Need(Finite(adsTime) && adsTime >= 0.01f, "ADS移行時間は0.01秒以上");
            if (recoil != null && recoil.enabled && !recoil.IsValid(out string recoilError))
                list.Add("反動：" + recoilError);
            if (falloff != null && !falloff.IsValid(out string falloffError))
                list.Add("距離減衰：" + falloffError);
            return list;
        }

        public bool CanApply => Problems().Count == 0;

        /// <summary>Writes the values into <paramref name="target"/> (a runtime clone, never a weapon asset).</summary>
        public void ApplyTo(WeaponDefinition target)
        {
            target.damage = damage;
            target.automatic = automatic;
            target.fireInterval = 60f / rpm;
            target.magazineSize = magazineSize;
            target.startReserve = startReserve;
            target.reserveMax = reserveMax;
            target.reloadTime = reloadTime;
            target.projectileSpeed = projectileSpeed;
            target.maxRange = maxRange;
            target.hipSpreadDegrees = hipSpreadDegrees;
            target.adsSpreadDegrees = adsSpreadDegrees;
            target.adsTime = adsTime;
            target.recoilProfile = recoil.Clone();
            target.recoilPitchDegrees = recoil.pitchDegrees;
            target.recoilYawDegrees = recoil.yawDegrees;
            target.adsRecoilMultiplier = recoil.adsMultiplier;
            target.damageFalloff = falloff.Clone();
        }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        // ------------------------------------------------------------------------------------------- table columns

        /// <summary>One column of the weapon table: header and the cell text for a profile.</summary>
        public readonly struct Column
        {
            public readonly string Header;
            public readonly Func<WeaponBalanceProfile, string> Cell;

            public Column(string header, Func<WeaponBalanceProfile, string> cell)
            {
                Header = header;
                Cell = cell;
            }
        }

        static string F(float v, string format = "0.##") => v.ToString(format, CultureInfo.InvariantCulture);

        public static string RecoilDirectionText(WeaponBalanceProfile p) =>
            p.recoil == null || !p.recoil.enabled ? "従来（上・左右ランダム）" : RecoilCalculator.DirectionName(p.recoil);

        public static string RecoilStrengthText(WeaponBalanceProfile p) =>
            p.recoil == null || !p.recoil.enabled
                ? "従来"
                : p.recoil.strength switch { RecoilStrength.Weak => "弱", RecoilStrength.Strong => "強", _ => "中" };

        public static string FalloffText(WeaponBalanceProfile p)
        {
            if (p.falloff == null || p.falloff.IsEmpty)
                return "なし（1.0）";
            var parts = new List<string>();
            foreach (var pt in p.falloff.points)
                parts.Add($"{F(pt.distance, "0")}m×{F(pt.multiplier, "0.00")}");
            return string.Join(" ", parts);
        }

        /// <summary>The weapon table columns (weapons are rows).</summary>
        public static readonly Column[] Columns =
        {
            new Column("武器ID", p => p.weaponId),
            new Column("種類", p => p.weaponType),
            new Column("基本威力", p => F(p.damage)),
            new Column("射撃方式", p => p.automatic ? "フルオート" : "セミオート"),
            new Column("RPM", p => F(p.rpm, "0")),
            new Column("装弾数", p => p.magazineSize.ToString(CultureInfo.InvariantCulture)),
            new Column("開始予備弾", p => p.startReserve.ToString(CultureInfo.InvariantCulture)),
            new Column("予備弾上限", p => p.reserveMax.ToString(CultureInfo.InvariantCulture)),
            new Column("リロード(秒)", p => F(p.reloadTime)),
            new Column("弾速(m/s)", p => F(p.projectileSpeed, "0")),
            new Column("最大射程(m)", p => F(p.maxRange, "0")),
            new Column("腰だめ拡散(°)", p => F(p.hipSpreadDegrees)),
            new Column("ADS拡散(°)", p => F(p.adsSpreadDegrees)),
            new Column("ADS移行(秒)", p => F(p.adsTime)),
            new Column("反動方向", RecoilDirectionText),
            new Column("反動強度", RecoilStrengthText),
            new Column("ADS反動倍率", p => F(p.recoil.adsMultiplier)),
            new Column("回復量", p => p.recoil == null || !p.recoil.enabled ? "従来 0.5" : F(p.recoil.recoveryFraction)),
            new Column("回復速度(°/秒)", p => p.recoil == null || !p.recoil.enabled ? "従来（場面の値）" : F(p.recoil.recoverySpeed, "0.0")),
            new Column("距離減衰", FalloffText),
        };

        /// <summary>Columns whose text differs between <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static List<(string column, string from, string to)> Diff(WeaponBalanceProfile a, WeaponBalanceProfile b)
        {
            var list = new List<(string, string, string)>();
            foreach (var c in Columns)
            {
                string x = c.Cell(a), y = c.Cell(b);
                if (x != y)
                    list.Add((c.Header, x, y));
            }
            // Base angles are not table columns but are part of the recoil.
            if (Math.Abs(a.recoil.pitchDegrees - b.recoil.pitchDegrees) > 1e-4f)
                list.Add(("反動（縦の基準角）", F(a.recoil.pitchDegrees), F(b.recoil.pitchDegrees)));
            if (Math.Abs(a.recoil.yawDegrees - b.recoil.yawDegrees) > 1e-4f)
                list.Add(("反動（横の基準角）", F(a.recoil.yawDegrees), F(b.recoil.yawDegrees)));
            return list;
        }

        /// <summary>The falloff example of the lab (trial values, not a decided setting).</summary>
        public static DamageFalloffProfile ExampleFalloff() => new DamageFalloffProfile
        {
            points = new List<FalloffPoint>
            {
                new FalloffPoint(0f, 1f), new FalloffPoint(10f, 1f), new FalloffPoint(25f, 1f),
                new FalloffPoint(40f, 0.8f), new FalloffPoint(60f, 0.6f), new FalloffPoint(80f, 0.4f),
            },
        };
    }

    /// <summary>
    /// DEVELOPMENT ONLY. Saves trial profiles as JSON files, one per weapon, in a folder of their own
    /// (<c>persistentDataPath/WeaponBalanceLab</c> by default): separate from PlayerPrefs and game settings, never in the
    /// project's assets.
    /// </summary>
    public sealed class WeaponBalanceStore
    {
        public string Folder { get; }

        public WeaponBalanceStore(string folder = null)
        {
            Folder = folder ?? Path.Combine(Application.persistentDataPath, "WeaponBalanceLab");
        }

        public string PathOf(string weaponId) => Path.Combine(Folder, weaponId + ".json");

        public void Save(WeaponBalanceProfile profile, string baseCommit)
        {
            Directory.CreateDirectory(Folder);
            var copy = profile.Clone();
            copy.profileVersion = WeaponBalanceProfile.CurrentVersion;
            copy.baseCommit = baseCommit;
            copy.savedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);
            string path = PathOf(profile.weaponId);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(copy, true));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>The saved profile of this weapon (false when there is none, or it is unreadable / of another version).</summary>
        public bool TryLoad(string weaponId, out WeaponBalanceProfile profile, out string error)
        {
            profile = null;
            error = null;
            string path = PathOf(weaponId);
            if (!File.Exists(path))
            {
                error = "保存された試験データはありません";
                return false;
            }
            try
            {
                profile = JsonUtility.FromJson<WeaponBalanceProfile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                error = "読み込めません：" + e.Message;
                return false;
            }
            if (profile == null || profile.weaponId != weaponId || profile.profileVersion != WeaponBalanceProfile.CurrentVersion)
            {
                error = "形式が違うため読み込みません";
                profile = null;
                return false;
            }
            profile.recoil ??= new RecoilProfile();
            profile.falloff ??= new DamageFalloffProfile();
            return true;
        }

        /// <summary>Removes this weapon's saved trial data (the lab goes back to the current values).</summary>
        public bool Delete(string weaponId)
        {
            string path = PathOf(weaponId);
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }
    }
}
