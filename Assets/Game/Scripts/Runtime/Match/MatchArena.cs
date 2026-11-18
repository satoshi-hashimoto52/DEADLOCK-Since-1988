using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Player;
using Deadlock.UI;
using Deadlock.View;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace Deadlock.Match
{
    /// <summary>
    /// The offline normal match in a loaded map scene (Stage 35; the ruins): made only when a match is started from
    /// the home's Deadline entry, so the scene's own mode (the ruins test field with its zombies) is unchanged when
    /// entered normally. It switches that mode's parts off (zombies, training round and its HUD, the pause of the menu,
    /// the menu's scene / retry / development buttons), places the eight on planned start places, puts an NPC body in
    /// every place but the local player's, and sends every hit through the session (records, health, kills). The local
    /// player's shots and knife hits are theirs (nothing else in this scene damages a body). A takeover turns the
    /// player's character into an NPC body with the same place, facing, health, weapon, skin and rounds.
    /// </summary>
    public sealed class MatchArena : MonoBehaviour
    {
        public const string BlockCountdown = "matchCountdown";
        public const string BlockResult = "matchResult";
        public const string LeaveButtonName = "LeaveMatchButton";
        public const string DevDisconnectButtonName = "DevDisconnectButton";
        /// <summary>Grid of candidate start points (metres, provisional) and the map's walkable box (the ruins).</summary>
        public static float CandidateStep = 2.5f;
        static readonly Rect MapArea = new Rect(-20.5f, -16.7f, 41f, 33.4f);
        static readonly float[] Floors = { 0f, 5f };

        MatchSession session;
        PlayerRig rig;
        int localSlot = -1;
        int defaultMask, playerLayer, enemyLayer;
        readonly Dictionary<int, Transform> bodies = new Dictionary<int, Transform>();
        readonly Dictionary<Collider, int> owners = new Dictionary<Collider, int>();
        readonly Dictionary<int, MatchBot> bots = new Dictionary<int, MatchBot>();
        readonly Dictionary<int, Vector3> lastStep = new Dictionary<int, Vector3>();
        readonly Dictionary<int, bool> lastAlive = new Dictionary<int, bool>();
        System.Func<float> clock;
        /// <summary>Metres walked between two footstep sound events (provisional).</summary>
        public static float StepLength = 1.6f;

        /// <summary>The recent past of every participant and the sound events (for the delayed spectating, Stage 36).</summary>
        public MatchReplay Replay { get; } = new MatchReplay();
        public MatchSpectator Spectator { get; private set; }

        public int ShotMask { get; private set; }
        public PlayerRig Rig => rig;
        public IReadOnlyDictionary<int, MatchBot> Bots => bots;
        public List<Vector3> Spawns { get; private set; }
        public List<string> PlanLog { get; } = new List<string>();
        public bool CombatAllowed => session != null && session.CombatAllowed;
        public bool LocalInControl => rig != null && rig.gameObject.activeSelf;
        public GameMenu Menu { get; private set; }

        /// <summary>Sets the match up in the active scene. False (with the reason) when it cannot be placed: nothing is forced.</summary>
        public static bool TrySetup(MatchSession session, int seed, System.Func<float> clock, out MatchArena arena, out string error)
        {
            arena = new GameObject("MatchArena").AddComponent<MatchArena>();
            arena.session = session;
            arena.clock = clock;
            if (!arena.Setup(seed, out error))
            {
                Destroy(arena.gameObject);
                arena = null;
                return false;
            }
            return true;
        }

        bool Setup(int seed, out string error)
        {
            error = null;
            rig = FindAnyObjectByType<PlayerRig>();
            if (rig == null)
            {
                error = "プレイヤーが見つかりません";
                return false;
            }
            defaultMask = 1 << LayerMask.NameToLayer("Default");
            playerLayer = rig.gameObject.layer;
            enemyLayer = LayerMask.NameToLayer("Enemy");
            ShotMask = defaultMask | (1 << playerLayer) | (1 << enemyLayer);

            // The scene's own mode stays out of the match.
            foreach (var z in FindObjectsByType<Zombie>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                z.gameObject.SetActive(false);
            foreach (var s in FindObjectsByType<TrainingSession>(FindObjectsSortMode.None))
                s.enabled = false;
            foreach (var h in FindObjectsByType<TrainingHud>(FindObjectsSortMode.None))
            {
                if (h.StatusText != null)
                    h.StatusText.gameObject.SetActive(false);
                if (h.ResultPanel != null)
                    h.ResultPanel.SetActive(false);
                h.enabled = false;
            }
            foreach (var p in FindObjectsByType<OfflineSessionPause>(FindObjectsSortMode.None))
                p.enabled = false; // a normal match never pauses (the others keep playing)
            Menu = FindAnyObjectByType<GameMenu>();

            var candidates = Candidates();
            var planner = new SpawnPlanner
            {
                LineOfSight = (a, b) => !Physics.Linecast(a + Vector3.up * 1.5f, b + Vector3.up * 1.5f, defaultMask, QueryTriggerInteraction.Ignore),
                Shelter = Shelter,
            };
            bool planned = planner.TryPlan(candidates, MatchRules.Slots, seed, out var spawns);
            PlanLog.AddRange(planner.Log);
            if (!planned)
            {
                error = "出現位置を決められません：" + string.Join("／", planner.Log);
                Debug.LogWarning("[DEADLOCK][match] " + error);
                return false;
            }
            Spawns = spawns;
            foreach (var line in planner.Log)
                Debug.Log("[DEADLOCK][match] spawn: " + line);

            var npcWeapon = NpcWeapon(rig.Gun.Definition);
            for (int i = 0; i < session.Slots.Count; i++)
            {
                var slot = session.Slots[i];
                float yaw = Yaw(spawns[i]);
                if (slot.IsLocal)
                {
                    localSlot = slot.Id;
                    rig.ResetAt(spawns[i], yaw);
                    bodies[slot.Id] = rig.transform;
                    foreach (var c in rig.GetComponentsInChildren<Collider>())
                        owners[c] = slot.Id;
                    continue;
                }
                // Other humans would be other devices (Stage 37); offline every other place is an NPC body.
                var bot = MatchBot.Create(this, slot.Id, slot.Name, spawns[i], yaw, npcWeapon, null, SkinCatalog.StandardId, enemyLayer, NpcColor(slot.Id), false);
                Register(slot.Id, bot);
            }
            AdaptMenu();
            rig.Input.Block(BlockCountdown); // no move, no shot until the countdown ends
            rig.Gun.Fired += OnLocalFired;
            rig.Gun.ReloadStarted += OnLocalReload;
            session.PhaseChanged += OnPhase;
            return true;
        }

        void Update()
        {
            // The development equipment's menu entry is added a frame after the scene loads: kept out of a match.
            var dev = Menu != null ? Menu.transform.Find("MenuLayer/MenuPanel/Window/DevLoadoutButton") : null;
            if (dev != null && dev.gameObject.activeSelf)
                dev.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (session != null)
                session.PhaseChanged -= OnPhase;
            if (rig != null)
            {
                rig.Gun.Fired -= OnLocalFired;
                rig.Gun.ReloadStarted -= OnLocalReload;
            }
        }

        void OnPhase(MatchPhase phase)
        {
            if (rig == null)
                return;
            if (phase == MatchPhase.Playing)
                rig.Input.Unblock(BlockCountdown);
            else if (phase == MatchPhase.Finished)
                rig.Input.Block(BlockResult);
        }

        static WeaponDefinition NpcWeapon(WeaponDefinition fallback)
        {
            var catalog = WeaponCatalog.Load();
            var rifle = catalog != null ? catalog.Find("proto_rifle")?.gun : null;
            return rifle != null ? rifle : fallback;
        }

        public static Color NpcColor(int id) => Color.HSVToRGB((id * 0.13f) % 1f, 0.55f, 0.75f);

        static float Yaw(Vector3 p)
        {
            Vector3 toCentre = -p;
            toCentre.y = 0f;
            return toCentre.sqrMagnitude < 0.01f ? 0f : Quaternion.LookRotation(toCentre).eulerAngles.y;
        }

        void Register(int slotId, MatchBot bot)
        {
            if (bots.TryGetValue(slotId, out var old) && old != null)
            {
                old.Fired -= OnBotFired;
                old.ReloadStarted -= OnBotReload;
            }
            bot.Fired += OnBotFired;
            bot.ReloadStarted += OnBotReload;
            bots[slotId] = bot;
            bodies[slotId] = bot.transform;
            foreach (var c in bot.GetComponents<Collider>())
                owners[c] = slotId;
        }

        // ------------------------------------------------------------------------------------------- the record of the past

        float Now => clock != null ? clock() : Time.time;
        bool Recording => session.Phase == MatchPhase.Countdown || session.Phase == MatchPhase.Playing;

        void LateUpdate()
        {
            if (!Recording)
                return;
            float now = Now;
            foreach (var slot in session.Slots)
            {
                if (!bodies.TryGetValue(slot.Id, out var body) || body == null)
                    continue;
                var sample = new ReplaySample
                {
                    Time = now,
                    Position = body.position,
                    Health = slot.Health,
                    Kills = slot.Kills,
                    Alive = slot.Alive,
                };
                if (slot.Id == localSlot && LocalInControl)
                {
                    sample.Yaw = rig.Motor.Yaw;
                    sample.Pitch = rig.Motor.Pitch;
                    sample.EyeHeight = rig.Aim.position.y - rig.transform.position.y;
                    sample.WeaponId = rig.Weapons.Current == WeaponSlot.Melee ? rig.Melee.Definition.weaponId : rig.Gun.Definition.weaponId;
                }
                else if (bots.TryGetValue(slot.Id, out var bot) && bot != null)
                {
                    sample.Yaw = bot.transform.eulerAngles.y;
                    sample.Pitch = slot.Alive ? bot.AimPitch : 0f;
                    sample.EyeHeight = MatchBot.EyeHeight;
                    sample.WeaponId = bot.Weapon.weaponId;
                }
                Replay.Record(slot.Id, sample);
                // Footsteps from the walked distance; a death once.
                Vector3 flat = new Vector3(body.position.x, 0f, body.position.z);
                if (!lastStep.TryGetValue(slot.Id, out var step))
                    lastStep[slot.Id] = flat;
                else if (slot.Alive && (flat - step).magnitude >= StepLength)
                {
                    lastStep[slot.Id] = flat;
                    Replay.AddEvent(new ReplayEvent { Time = now, SlotId = slot.Id, Kind = ReplayEventKind.Footstep, Position = body.position });
                }
                if (lastAlive.TryGetValue(slot.Id, out bool wasAlive) && wasAlive && !slot.Alive)
                    Replay.AddEvent(new ReplayEvent { Time = now, SlotId = slot.Id, Kind = ReplayEventKind.Death, Position = body.position });
                lastAlive[slot.Id] = slot.Alive;
            }
        }

        void OnLocalFired()
        {
            if (Recording && LocalInControl)
                Replay.AddEvent(new ReplayEvent { Time = Now, SlotId = localSlot, Kind = ReplayEventKind.Gunshot, Position = rig.Aim.position,
                    Direction = rig.Aim.forward, WeaponId = rig.Gun.Definition.weaponId });
        }

        void OnLocalReload()
        {
            if (Recording && LocalInControl)
                Replay.AddEvent(new ReplayEvent { Time = Now, SlotId = localSlot, Kind = ReplayEventKind.Reload, Position = rig.transform.position,
                    WeaponId = rig.Gun.Definition.weaponId });
        }

        void OnBotFired(MatchBot bot, Vector3 origin, Vector3 direction)
        {
            if (Recording)
                Replay.AddEvent(new ReplayEvent { Time = Now, SlotId = bot.SlotId, Kind = ReplayEventKind.Gunshot, Position = origin, Direction = direction,
                    WeaponId = bot.Weapon.weaponId });
        }

        void OnBotReload(MatchBot bot)
        {
            if (Recording)
                Replay.AddEvent(new ReplayEvent { Time = Now, SlotId = bot.SlotId, Kind = ReplayEventKind.Reload, Position = bot.transform.position,
                    WeaponId = bot.Weapon.weaponId });
        }

        /// <summary>The local player died: the delayed spectating starts at once.</summary>
        public MatchSpectator BeginSpectate()
        {
            if (Spectator == null)
                Spectator = MatchSpectator.Begin(this, Replay, () => Now, localSlot);
            return Spectator;
        }

        public void EndSpectate()
        {
            if (Spectator != null)
                Spectator.Stop();
            Spectator = null;
        }

        // ------------------------------------------------------------------------------------------- start places

        /// <summary>Walkable, clear points of the map on a grid (both floors).</summary>
        List<Vector3> Candidates()
        {
            var list = new List<Vector3>();
            foreach (float floor in Floors)
                for (float x = MapArea.xMin; x <= MapArea.xMax; x += CandidateStep)
                    for (float z = MapArea.yMin; z <= MapArea.yMax; z += CandidateStep)
                    {
                        var probe = new Vector3(x, floor + 0.3f, z);
                        if (!NavMesh.SamplePosition(probe, out var hit, 0.6f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - floor) > 0.35f)
                            continue;
                        var p = hit.position;
                        if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.5f, 0.45f, defaultMask, QueryTriggerInteraction.Ignore))
                            continue;
                        list.Add(p);
                    }
            return list;
        }

        /// <summary>Share of eight directions with a wall within 3 m (cover around the point).</summary>
        float Shelter(Vector3 p)
        {
            int blocked = 0;
            for (int i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                if (Physics.Raycast(p + Vector3.up * 1.2f, dir, 3f, defaultMask, QueryTriggerInteraction.Ignore))
                    blocked++;
            }
            return blocked / 8f;
        }

        // ------------------------------------------------------------------------------------------- combat

        public bool IsAlive(int slotId) => session.Slot(slotId)?.Alive ?? false;

        public bool Sees(Vector3 eye, Vector3 point, Transform target) =>
            !Physics.Linecast(eye, point, defaultMask, QueryTriggerInteraction.Ignore);

        /// <summary>The nearest living participant other than this bot.</summary>
        public Transform NearestOpponent(MatchBot bot, out int slotId)
        {
            slotId = -1;
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (var kv in bodies)
            {
                if (kv.Key == bot.SlotId || kv.Value == null || !kv.Value.gameObject.activeInHierarchy || !IsAlive(kv.Key))
                    continue;
                float d = (kv.Value.position - bot.transform.position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = kv.Value;
                    slotId = kv.Key;
                }
            }
            return best;
        }

        public void ShotHit(MatchBot shooter, Collider collider, float damage)
        {
            if (collider != null && owners.TryGetValue(collider, out int victim))
                ApplyHit(shooter.SlotId, victim, damage, shooter.Eye);
        }

        /// <summary>A hit on a bot from the scene's own damage (the local player's projectiles and knife).</summary>
        public DamageResult DamageFromLocalPlayer(MatchBot bot, float amount)
        {
            if (!LocalInControl || localSlot < 0 || !IsAlive(localSlot) || !CombatAllowed)
                return DamageResult.Ignored;
            bool killed = ApplyHit(localSlot, bot.SlotId, amount, rig.transform.position);
            return killed ? DamageResult.Destroyed : DamageResult.Damaged;
        }

        /// <summary>Any hit between two places: recorded by the session, then shown on the bodies. True when it killed.</summary>
        public bool ApplyHit(int attacker, int victim, float amount, Vector3 source)
        {
            float applied = session.RecordHit(attacker, victim, amount, out bool killed);
            if (applied <= 0f)
                return false;
            if (bots.TryGetValue(victim, out var bot))
            {
                if (killed)
                    bot.Die();
            }
            else if (victim == localSlot && LocalInControl)
            {
                rig.Health.TakeDamage(applied, source);
                if (killed && !rig.Health.IsDead)
                    rig.Health.TakeDamage(rig.Health.Health, source);
            }
            return killed;
        }

        // ------------------------------------------------------------------------------------------- takeover

        /// <summary>
        /// The local human left or lost the connection: an NPC takes the character over where it is, with the same
        /// facing, health, weapon (and its skin) and rounds; the place's record stays in the session.
        /// </summary>
        public MatchBot TakeOverLocal()
        {
            if (!LocalInControl || localSlot < 0)
                return null;
            var slot = session.Slot(localSlot);
            var gun = rig.Gun;
            var ammo = new AmmoState(gun.Ammo.MagazineSize, gun.Ammo.ReserveMax, gun.Ammo.Loaded, gun.Ammo.Reserve);
            string skin = SkinSelection.GetWeaponSkinId(gun.Definition.weaponId);
            var bot = MatchBot.Create(this, localSlot, slot.Name, rig.transform.position, rig.Motor.Yaw, gun.Definition, ammo, skin, enemyLayer,
                new Color(0.3f, 0.6f, 1f), true);
            foreach (var c in rig.GetComponentsInChildren<Collider>())
                owners.Remove(c);
            rig.gameObject.SetActive(false); // the human's control ends here
            Register(localSlot, bot);
            if (!slot.Alive)
                bot.Die();
            return bot;
        }

        // ------------------------------------------------------------------------------------------- menu

        void AdaptMenu()
        {
            if (Menu == null)
                return;
            var window = Menu.transform.Find("MenuLayer/MenuPanel/Window") as RectTransform;
            if (window == null)
                return;
            // Leaving goes through the match's own confirmation; nothing else may leave or change the loadout mid-match.
            foreach (var name in new[] { "RetryButton", "SceneButton", "TrainingButton", "RuinsButton", "OnlineButton", "HomeButton", "DevGunButton", "DevLoadoutButton" })
            {
                var t = window.Find(name);
                if (t != null)
                    t.gameObject.SetActive(false);
            }
            var retry = window.Find("RetryButton") as RectTransform;
            Vector2 pos = retry != null ? retry.anchoredPosition : new Vector2(0f, -212f);
            UiBuild.Button(window, LeaveButtonName, "試合から退出", new Vector2(0.5f, 1f), pos, new Vector2(480f, 76f), () =>
            {
                Menu.Close();
                MatchFlow.Instance?.AskLeave();
            });
            if (Debug.isDebugBuild)
            {
                var scene = window.Find("SceneButton") as RectTransform;
                Vector2 p2 = scene != null ? scene.anchoredPosition : pos + new Vector2(0f, -92f);
                var b = UiBuild.Button(window, DevDisconnectButtonName, "通信切断を模擬（開発用）", new Vector2(0.5f, 1f), p2, new Vector2(480f, 76f), () =>
                {
                    Menu.Close();
                    MatchFlow.Instance?.SimulateDisconnect();
                }, 26);
                ((Image)b.targetGraphic).color = new Color(0.4f, 0.1f, 0.1f, 0.95f);
            }
        }
    }
}
