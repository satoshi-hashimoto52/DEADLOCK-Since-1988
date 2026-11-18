using System.Collections.Generic;

namespace Deadlock.Audio
{
    /// <summary>How one sound is played (Stage 39). The values are provisional for the prototype.</summary>
    public struct SoundSpec
    {
        public SoundGroup Group;
        /// <summary>Placed in the world (3D: distance, left / right, walls and floors) or heard flat (2D).</summary>
        public bool Spatial;
        /// <summary>Copies of this sound heard at once; a new one then takes the place of the oldest.</summary>
        public int MaxVoices;
        /// <summary>Random pitch change of each play, ± this fraction (rapid fire does not sound like one loop).</summary>
        public float PitchJitter;
        /// <summary>Level of the sound file used (0–1, never above 1).</summary>
        public float Volume;
        /// <summary>3D: full level up to this distance (m) …</summary>
        public float MinDistance;
        /// <summary>… and silent from this distance (m), falling linearly in between.</summary>
        public float MaxDistance;
        /// <summary>When every voice is busy, a sound only takes the place of one of lower or equal importance.</summary>
        public int Importance;
    }

    /// <summary>
    /// The game's sounds by id and where their files go (Stage 39). A sound file is looked for in a Resources folder
    /// at <c>Sound/SE/&lt;id&gt;</c> (BGM: <c>Sound/BGM/&lt;state&gt;</c>, lower case); a missing file is silence, never an
    /// error. No sound file exists yet (none is approved): every id here is the contract for the files to be made
    /// (docs/AUDIO_PRODUCTION_SPEC_STAGE39.md).
    /// </summary>
    public static class SoundCatalog
    {
        public const string SeFolder = "Sound/SE/";
        public const string BgmFolder = "Sound/BGM/";

        // Weapons (the gun's own shot: "weapon_fire_" + its weapon id).
        public const string FirePrefix = "weapon_fire_";
        public const string KnifeSwing = "weapon_knife_swing";
        public const string ReloadStart = "weapon_reload_start";
        public const string ReloadMagazine = "weapon_reload_magazine";
        public const string ReloadEnd = "weapon_reload_end";
        public const string DryFire = "weapon_dry_fire";
        public const string Switch = "weapon_switch";

        // The player.
        public const string Footstep = "player_footstep";
        public const string Jump = "player_jump";
        public const string Land = "player_land";
        public const string Dash = "player_dash";
        public const string Slide = "player_slide";
        public const string Crouch = "player_crouch";
        public const string Prone = "player_prone";
        public const string Hurt = "player_hurt";
        public const string Death = "player_death";

        // The match and the screens.
        public const string UiSubmit = "ui_submit";
        public const string MenuOpen = "ui_menu_open";
        public const string MenuClose = "ui_menu_close";
        public const string MatchStart = "match_start";
        public const string CountdownTick = "match_countdown_tick";
        public const string Victory = "match_victory";
        public const string Defeat = "match_defeat";
        public const string Reward = "ui_reward";
        public const string GachaStandard = "ui_gacha_standard";
        public const string GachaEmphasis = "ui_gacha_emphasis";
        public const string GachaLegendary = "ui_gacha_legendary";

        /// <summary>The guns of the catalog whose shot has its own sound (the knife has <see cref="KnifeSwing"/>).</summary>
        public static readonly string[] GunIds = { "proto_rifle", "proto_smg", "proto_shotgun", "proto_lmg", "proto_epic_smg", "proto_handgun" };

        public static string Fire(string weaponId) => FirePrefix + (string.IsNullOrEmpty(weaponId) ? "unknown" : weaponId);

        static SoundSpec Shot => new SoundSpec { Group = SoundGroup.Se, Spatial = true, MaxVoices = 4, PitchJitter = 0.04f, Volume = 1f, MinDistance = 3f, MaxDistance = 120f, Importance = 3 };
        static SoundSpec Handling => new SoundSpec { Group = SoundGroup.Se, Spatial = true, MaxVoices = 2, PitchJitter = 0.02f, Volume = 0.8f, MinDistance = 1f, MaxDistance = 20f, Importance = 2 };
        /// <summary>
        /// Footsteps are heard as far as the game's hearing of footsteps (<see cref="Map.EnemyDetector"/>: 15 m, halved by
        /// each wall or floor between), so what is heard matches what the floor map shows.
        /// </summary>
        static SoundSpec Step => new SoundSpec { Group = SoundGroup.Se, Spatial = true, MaxVoices = 4, PitchJitter = 0.06f, Volume = 0.7f, MinDistance = 1f, MaxDistance = 15f, Importance = 2 };
        static SoundSpec Body => new SoundSpec { Group = SoundGroup.Se, Spatial = true, MaxVoices = 2, PitchJitter = 0.03f, Volume = 0.8f, MinDistance = 1f, MaxDistance = 20f, Importance = 2 };
        static SoundSpec Self => new SoundSpec { Group = SoundGroup.Se, Spatial = false, MaxVoices = 1, PitchJitter = 0.03f, Volume = 0.9f, Importance = 4 };
        static SoundSpec Cue => new SoundSpec { Group = SoundGroup.Se, Spatial = false, MaxVoices = 1, PitchJitter = 0f, Volume = 0.9f, Importance = 5 };
        static SoundSpec Screen => new SoundSpec { Group = SoundGroup.Ui, Spatial = false, MaxVoices = 2, PitchJitter = 0f, Volume = 0.8f, Importance = 1 };

        static readonly Dictionary<string, SoundSpec> specs = Build();

        static Dictionary<string, SoundSpec> Build()
        {
            var d = new Dictionary<string, SoundSpec>();
            foreach (var id in GunIds)
                d[Fire(id)] = Shot;
            var knife = Handling;
            knife.MaxVoices = 1;
            d[KnifeSwing] = knife;
            d[ReloadStart] = Handling;
            d[ReloadMagazine] = Handling;
            d[ReloadEnd] = Handling;
            var dry = Handling;
            dry.MaxVoices = 1;
            d[DryFire] = dry;
            d[Switch] = Handling;
            d[Footstep] = Step;
            d[Jump] = Body;
            d[Land] = Body;
            d[Dash] = Body;
            d[Slide] = Body;
            d[Crouch] = Body;
            d[Prone] = Body;
            d[Hurt] = Self;
            var death = Body;
            death.MaxDistance = 40f;
            death.Importance = 4;
            d[Death] = death;
            d[UiSubmit] = Screen;
            d[MenuOpen] = Screen;
            d[MenuClose] = Screen;
            d[MatchStart] = Cue;
            var tick = Cue;
            tick.Volume = 0.7f;
            d[CountdownTick] = tick;
            d[Victory] = Cue;
            d[Defeat] = Cue;
            d[Reward] = Screen;
            d[GachaStandard] = Screen;
            d[GachaEmphasis] = Screen;
            d[GachaLegendary] = Screen;
            return d;
        }

        public static IEnumerable<string> Ids => specs.Keys;

        /// <summary>The spec of a sound; an id not in the list (a gun added later) plays like a gun shot if it is one.</summary>
        public static bool TryGet(string id, out SoundSpec spec)
        {
            if (specs.TryGetValue(id, out spec))
                return true;
            if (id != null && id.StartsWith(FirePrefix))
            {
                spec = Shot;
                return true;
            }
            return false;
        }

        public static string SePath(string id) => SeFolder + id;

        public static string BgmPath(BgmState state) => BgmFolder + state.ToString().ToLowerInvariant();
    }
}
