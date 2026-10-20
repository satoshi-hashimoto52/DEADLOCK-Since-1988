using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.Player;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deadlock.Network
{
    /// <summary>
    /// Automated two-process / two-device check of the online prototype. Inactive unless the app is launched with
    /// <c>-netCheck host</c> or <c>-netCheck client -netHost &lt;address&gt;</c>. iOS apps do not receive launch
    /// arguments, so the same values are also read from the environment variables DEADLOCK_NETCHECK,
    /// DEADLOCK_NETHOST and DEADLOCK_NETCHECK_TIMELIMIT (e.g. <c>xcrun devicectl device process launch -e ...</c>).
    /// The host drives a fixed scenario: it plays its own player through injected input and tells the client what to
    /// do over the network (<see cref="AvatarCheckChannel"/>); both sides check what they actually see and log
    /// "NETCHECK PASS|FAIL name detail" lines and a final "NETCHECK RESULT" line. Players act through the same paths
    /// as a person (input → local player → requests to the host); only the "unchecked fire" steps bypass the local
    /// checks on purpose, to prove the host refuses them.
    /// </summary>
    public sealed class NetworkCheckRunner : MonoBehaviour
    {
        [Flags]
        enum Press
        {
            None = 0, Jump = 1, Fire = 2, Reload = 4, Crouch = 8, Prone = 16, Gun = 32, Melee = 64,
        }

        public static NetworkCheckRunner Instance { get; private set; }

        string role;
        string hostAddress = "127.0.0.1";
        float timeLimit = 240f;
        int passed, failed;
        NetworkSession session;
        PlayerRig rig;

        Vector2 move;
        bool sprint, fireHeld, ads;
        Press pending, active;

        readonly Dictionary<int, string> replies = new Dictionary<int, string>();
        int nextId = 1;
        string hello;
        bool expectHostEnd;

        // Observation of the other player while it acts (max / seen values).
        bool watching;
        readonly HashSet<string> seen = new HashSet<string>();
        float watchMaxY, watchStartZ, watchMaxDz;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            string mode = Arg(args, "-netCheck") ?? Environment.GetEnvironmentVariable("DEADLOCK_NETCHECK");
            if (mode != "host" && mode != "client")
                return;
            var go = new GameObject("NetworkCheckRunner");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<NetworkCheckRunner>();
            runner.role = mode;
            runner.hostAddress = Arg(args, "-netHost") ?? Environment.GetEnvironmentVariable("DEADLOCK_NETHOST") ?? runner.hostAddress;
            string limitText = Arg(args, "-netCheckTimeLimit") ?? Environment.GetEnvironmentVariable("DEADLOCK_NETCHECK_TIMELIMIT");
            if (float.TryParse(limitText, NumberStyles.Float, CultureInfo.InvariantCulture, out float limit))
                runner.timeLimit = limit;
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        void Awake() => Instance = this;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Log($"start role={role} host={hostAddress}");
            if (SceneManager.GetActiveScene().name != GameScenes.Online)
                GameScenes.Load(GameScenes.Online);
            while (NetworkSession.Instance == null)
                yield return null;
            yield return null;
            session = NetworkSession.Instance;
            rig = session.LocalRig;
            rig.Input.ExternalSource = Frame;
            StartCoroutine(Watchdog());
            if (role == "host")
                yield return HostScenario();
            else
                yield return ClientLoop();
        }

        IEnumerator Watchdog()
        {
            float end = Time.realtimeSinceStartup + timeLimit;
            while (Time.realtimeSinceStartup < end)
                yield return null;
            Check("time limit", false, $"scenario did not finish in {timeLimit} s");
            Finish();
        }

        // ---------------------------------------------------------------- injected input

        PlayerInputFrame Frame() => new PlayerInputFrame
        {
            Move = move,
            Sprint = sprint,
            FireHeld = fireHeld || (active & Press.Fire) != 0,
            FirePressed = (active & Press.Fire) != 0,
            AdsActive = ads,
            JumpPressed = (active & Press.Jump) != 0,
            ReloadPressed = (active & Press.Reload) != 0,
            CrouchPressed = (active & Press.Crouch) != 0,
            PronePressed = (active & Press.Prone) != 0,
            SelectGunPressed = (active & Press.Gun) != 0,
            SelectMeleePressed = (active & Press.Melee) != 0,
        };

        // A press queued by a step is visible to the player for exactly the next frame.
        void LateUpdate()
        {
            active = pending;
            pending = Press.None;
            if (watching)
                Observe();
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        // ---------------------------------------------------------------- shared helpers

        NetworkAvatar Mine => NetworkAvatar.All.FirstOrDefault(a => a.IsLocalPlayerAvatar);
        NetworkAvatar Other => NetworkAvatar.All.FirstOrDefault(a => !a.IsLocalPlayerAvatar);

        void Log(string text) => Debug.Log($"NETCHECK [{role}] {text}");

        void Check(string name, bool ok, string detail = "")
        {
            if (ok) passed++;
            else failed++;
            Debug.Log($"NETCHECK {(ok ? "PASS" : "FAIL")} [{role}] {name} {detail}");
        }

        void Finish()
        {
            Debug.Log($"NETCHECK RESULT [{role}] pass={passed} fail={failed}");
            rig.Input.ExternalSource = null;
            Application.Quit(failed > 0 ? 1 : 0);
        }

        static float Chest(Posture posture) => posture switch
        {
            Posture.Crouching => 0.75f,
            Posture.Prone => 0.25f,
            Posture.Sliding => 0.5f,
            _ => 1.15f,
        };

        void AimAtOther()
        {
            var other = Other;
            if (other == null)
                return;
            Vector3 target = other.transform.position + Vector3.up * Chest(other.State.Value.PostureValue);
            Vector3 d = target - rig.Aim.position;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            rig.Motor.SetYawPitch(yaw, pitch);
        }

        string Query()
        {
            var mine = Mine;
            var other = Other;
            var s = other != null ? other.State.Value : default;
            int listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l => l.enabled);
            int cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c => c.enabled && c.CompareTag("MainCamera"));
            return string.Join(";", new[]
            {
                $"avatars={NetworkAvatar.All.Count}",
                $"bindings={NetworkAvatar.LocalBindings}",
                $"hp_self={rig.Health.Health:0}",
                $"dead_self={rig.Health.IsDead}",
                $"blocked={rig.Input.IsBlocked}",
                $"loaded={rig.Gun.Ammo.Loaded}",
                $"hp_other={(other != null ? other.Health.Value : -1f):0}",
                $"dead_other={(other != null && other.Dead.Value)}",
                $"posture_other={s.PostureValue}",
                $"slot_other={s.SlotValue}",
                $"other_visible={(other != null && other.View.IsVisible)}",
                $"self_visible={(mine != null && mine.View.IsVisible)}",
                $"listeners={listeners}",
                $"maincams={cameras}",
                $"requested={(mine != null ? mine.ShotsRequested : -1)}",
                $"rejected={(mine != null ? mine.ShotsRejected : -1)}",
                $"lastrej={(mine != null ? mine.LastRejection : ShotRejection.None)}",
                $"timescale={Time.timeScale:0.##}",
            });
        }

        static Dictionary<string, string> Parse(string reply)
        {
            var map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(reply))
                return map;
            foreach (var part in reply.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0)
                    map[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            return map;
        }

        static float F(Dictionary<string, string> map, string key) =>
            map.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : float.NaN;

        static string S(Dictionary<string, string> map, string key) => map.TryGetValue(key, out var v) ? v : "";

        void StartWatch()
        {
            seen.Clear();
            watchMaxY = float.MinValue;
            watchMaxDz = 0f;
            watchStartZ = Other != null ? Other.transform.position.z : 0f;
            watching = true;
        }

        void Observe()
        {
            var other = Other;
            if (other == null)
                return;
            var s = other.State.Value;
            seen.Add(s.PostureValue.ToString());
            seen.Add(s.SlotValue.ToString());
            if (s.Has(AvatarState.Flag.Sprinting)) seen.Add("Sprinting");
            if (s.Has(AvatarState.Flag.Switching)) seen.Add("Switching");
            seen.Add("swing" + s.SwingSeq);
            watchMaxY = Mathf.Max(watchMaxY, other.transform.position.y);
            watchMaxDz = Mathf.Max(watchMaxDz, Mathf.Abs(other.transform.position.z - watchStartZ));
        }

        string WatchReport()
        {
            watching = false;
            return $"seen={string.Join(",", seen)};maxy={watchMaxY:0.00};maxdz={watchMaxDz:0.00}";
        }

        // ---------------------------------------------------------------- actions (run on whichever side is told)

        IEnumerator Run(string command, Action<string> reply)
        {
            string[] p = command.Split(' ');
            float Num(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
            switch (p[0])
            {
                case "q":
                    break;
                case "tp":
                    rig.Motor.Teleport(new Vector3(Num(1), 0.05f, Num(2)), Num(3));
                    yield return Frames(3);
                    break;
                case "move":
                    move = Vector2.up;
                    yield return new WaitForSeconds(Num(1));
                    move = Vector2.zero;
                    break;
                case "sprint":
                    move = Vector2.up;
                    sprint = true;
                    yield return new WaitForSeconds(Num(1));
                    move = Vector2.zero;
                    sprint = false;
                    yield return new WaitForSeconds(0.3f);
                    break;
                case "crouch":
                    pending |= Press.Crouch;
                    yield return new WaitForSeconds(0.5f);
                    break;
                case "prone":
                    pending |= Press.Prone;
                    yield return new WaitForSeconds(0.7f);
                    break;
                case "stand":
                    if (rig.Posture.Current != Posture.Standing)
                        pending |= rig.Posture.Current == Posture.Prone ? Press.Prone : Press.Crouch;
                    yield return new WaitForSeconds(0.7f);
                    break;
                case "jump":
                    pending |= Press.Jump;
                    yield return new WaitForSeconds(0.8f);
                    break;
                case "slide":
                    move = Vector2.up;
                    sprint = true;
                    yield return new WaitForSeconds(0.5f);
                    pending |= Press.Crouch;
                    yield return Frames(2);
                    move = Vector2.zero;
                    sprint = false;
                    yield return new WaitForSeconds(1.2f);
                    break;
                case "weapon":
                    pending |= p[1] == "melee" ? Press.Melee : Press.Gun;
                    yield return new WaitForSeconds(rig.Weapons.SwitchTime + 0.2f);
                    break;
                case "swing":
                    AimAtOther();
                    pending |= Press.Fire;
                    yield return new WaitForSeconds(rig.Melee.Definition.TotalTime + 0.2f);
                    break;
                case "shoot":
                    ads = true;
                    yield return new WaitForSeconds(0.3f);
                    for (int i = 0; i < (int)Num(1); i++)
                    {
                        AimAtOther();
                        yield return null;
                        pending |= Press.Fire;
                        yield return new WaitForSeconds(0.25f);
                    }
                    ads = false;
                    yield return new WaitForSeconds(0.4f);
                    break;
                case "rawfire":
                    for (int i = 0; i < (int)Num(1); i++)
                        RawFire();
                    yield return new WaitForSeconds(0.5f);
                    break;
                case "reloadraw":
                    pending |= Press.Reload;
                    yield return Frames(3);
                    RawFire();
                    yield return new WaitForSeconds(0.5f);
                    break;
                case "menu":
                    var menu = FindAnyObjectByType<GameMenu>();
                    if (p[1] == "open") menu.Open();
                    else menu.Close();
                    yield return Frames(2);
                    break;
                case "respawn":
                    Mine.RespawnRpc();
                    yield return new WaitForSeconds(0.6f);
                    break;
                case "watch":
                    StartWatch();
                    break;
                case "watchreport":
                    reply(WatchReport());
                    yield break;
                case "leave_rejoin":
                    reply("ok");
                    yield return LeaveAndRejoin();
                    yield break;
                case "expect_host_end":
                    expectHostEnd = true;
                    break;
                default:
                    reply("unknown " + command);
                    yield break;
            }
            reply(Query());
        }

        void RawFire()
        {
            var mine = Mine;
            var other = Other;
            if (mine == null || other == null)
                return;
            Vector3 origin = rig.Aim.position;
            Vector3 target = other.transform.position + Vector3.up * Chest(other.State.Value.PostureValue);
            mine.SendUncheckedFireRequest(origin, (target - origin).normalized);
        }

        // ---------------------------------------------------------------- client

        IEnumerator ClientLoop()
        {
            yield return Connect();
            while (true)
            {
                if (expectHostEnd && !session.IsActive)
                {
                    yield return new WaitForSeconds(0.5f);
                    Check("client returns to the lobby when the host ends", FindAnyObjectByType<OnlineSessionUi>().Lobby.activeSelf, session.LastMessage);
                    Check("client is told why (host ended / connection lost)", session.LastMessage.Contains("ホスト"), session.LastMessage);
                    Check("no avatars left after the host ended", NetworkAvatar.All.Count == 0 && NetworkAvatar.LocalBindings == 0,
                        $"avatars={NetworkAvatar.All.Count} bindings={NetworkAvatar.LocalBindings}");
                    Finish();
                    yield break;
                }
                yield return null;
            }
        }

        IEnumerator Connect()
        {
            float end = Time.realtimeSinceStartup + 40f;
            while (Time.realtimeSinceStartup < end)
            {
                if (!session.IsActive && !session.IsBusy)
                {
                    Log($"joining {hostAddress}");
                    session.Join(hostAddress);
                }
                float wait = Time.realtimeSinceStartup + 10f;
                while (session.State == SessionState.Connecting && Time.realtimeSinceStartup < wait)
                    yield return null;
                if (session.State == SessionState.Connected)
                    break;
                Log($"not connected yet: {session.LastMessage}");
                yield return new WaitForSeconds(1f);
            }
            while (Mine == null)
                yield return null;
            yield return new WaitForSeconds(0.5f);
            Mine.GetComponent<AvatarCheckChannel>().ReplyRpc(0, Query());
        }

        IEnumerator LeaveAndRejoin()
        {
            session.Leave();
            float end = Time.realtimeSinceStartup + 5f;
            while ((NetworkAvatar.All.Count > 0 || session.IsBusy) && Time.realtimeSinceStartup < end)
                yield return null;
            yield return new WaitForSeconds(0.3f);
            var ui = FindAnyObjectByType<OnlineSessionUi>();
            Check("after leaving: back in the lobby", ui.Lobby.activeSelf && !session.IsActive, session.LastMessage);
            Check("after leaving: no avatars, local player unbound", NetworkAvatar.All.Count == 0 && NetworkAvatar.LocalBindings == 0,
                $"avatars={NetworkAvatar.All.Count} bindings={NetworkAvatar.LocalBindings}");
            Check("after leaving: input blocked only by the lobby, player alive", rig.Input.IsBlocked && !rig.Health.IsDead);
            yield return new WaitForSeconds(1f);
            yield return Connect();
        }

        public void OnCommand(AvatarCheckChannel channel, int id, string command)
        {
            StartCoroutine(Run(command, r => channel.ReplyRpc(id, r)));
        }

        // ---------------------------------------------------------------- host

        public void OnReply(int id, string reply)
        {
            if (id == 0)
                hello = reply;
            else
                replies[id] = reply;
        }

        AvatarCheckChannel ClientChannel => Other != null ? Other.GetComponent<AvatarCheckChannel>() : null;

        int Send(string command)
        {
            int id = nextId++;
            ClientChannel.CommandRpc(id, command);
            return id;
        }

        string lastReply;

        IEnumerator Await(int id, float timeout = 15f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!replies.ContainsKey(id) && Time.realtimeSinceStartup < end)
                yield return null;
            replies.TryGetValue(id, out lastReply);
            if (lastReply == null)
                Check($"reply to command {id}", false, "timeout");
        }

        IEnumerator Remote(string command)
        {
            yield return Await(Send(command));
        }

        IEnumerator Local(string command)
        {
            lastReply = null;
            yield return Run(command, r => lastReply = r);
        }

        IEnumerator WaitForHello()
        {
            hello = null;
            float end = Time.realtimeSinceStartup + 60f;
            while (hello == null && Time.realtimeSinceStartup < end)
                yield return null;
        }

        float Hp(NetworkAvatar avatar) => avatar != null ? avatar.Health.Value : float.NaN;

        IEnumerator HostScenario()
        {
            session.StartHost();
            Log($"hosting on port {session.Port}");
            yield return WaitForHello();
            Check("client connected", hello != null, hello ?? "no hello");
            if (hello == null)
            {
                Finish();
                yield break;
            }
            var h = Parse(hello);
            yield return new WaitForSeconds(0.5f);

            // --- Separation of local things
            Check("host: 2 avatars", NetworkAvatar.All.Count == 2);
            Check("host: own model hidden, other visible", Mine != null && !Mine.View.IsVisible && Other != null && Other.View.IsVisible);
            var mine = Parse(Query());
            Check("host: one main camera, one audio listener", S(mine, "maincams") == "1" && S(mine, "listeners") == "1", $"cams={S(mine, "maincams")} listeners={S(mine, "listeners")}");
            Check("client: 2 avatars, 1 binding, own hidden, other visible", S(h, "avatars") == "2" && S(h, "bindings") == "1"
                && S(h, "self_visible") == "False" && S(h, "other_visible") == "True", hello);
            Check("client: one main camera, one audio listener", S(h, "maincams") == "1" && S(h, "listeners") == "1", hello);

            // --- Client actions seen by the host (client lane x = 10, host lane x = -4: nothing in the way)
            yield return Remote("tp 10 -10 0");
            StartWatch();
            yield return Remote("move 1.0");
            Check("host sees the client move", watchMaxDz > 2f, $"dz={watchMaxDz:0.00}");
            yield return Remote("crouch");
            Check("host sees the client crouch", Other.State.Value.PostureValue == Posture.Crouching);
            yield return Remote("prone");
            Check("host sees the client prone", Other.State.Value.PostureValue == Posture.Prone);
            yield return Remote("stand");
            Check("host sees the client stand", Other.State.Value.PostureValue == Posture.Standing, Other.State.Value.PostureValue.ToString());
            yield return Remote("tp 10 -10 0");
            StartWatch();
            yield return Remote("sprint 0.8");
            yield return Remote("tp 10 -10 0");
            float groundY = Other.transform.position.y;
            yield return Remote("jump");
            yield return Remote("tp 10 -10 0");
            yield return Remote("slide");
            yield return Remote("weapon melee");
            yield return Remote("swing");
            yield return Remote("weapon gun");
            watching = false;
            Check("host sees the client sprint", seen.Contains("Sprinting"));
            Check("host sees the client jump", watchMaxY > groundY + 0.3f, $"maxY={watchMaxY:0.00} ground={groundY:0.00}");
            Check("host sees the client slide", seen.Contains(nameof(Posture.Sliding)));
            Check("host sees the client switch to the knife and back", seen.Contains(nameof(WeaponSlot.Melee)) && seen.Contains("Switching")
                && Other.State.Value.SlotValue == WeaponSlot.Gun);
            Check("host sees the client swing", seen.Count(x => x.StartsWith("swing")) >= 2);

            // --- Host actions seen by the client
            yield return Local("tp -4 10 180");
            yield return Remote("watch");
            yield return Local("move 1.0");
            yield return Local("crouch");
            yield return Local("stand");
            yield return Local("prone");
            yield return Local("stand");
            yield return Local("tp -4 10 180");
            yield return Local("sprint 0.8");
            yield return Local("tp -4 10 180");
            yield return Local("jump");
            yield return Local("tp -4 10 180");
            yield return Local("slide");
            yield return Local("stand");
            yield return Local("weapon melee");
            yield return Local("swing");
            yield return Local("weapon gun");
            yield return Remote("watchreport");
            var w = Parse(lastReply);
            string seenByClient = S(w, "seen");
            Check("client sees the host move", F(w, "maxdz") > 2f, lastReply);
            Check("client sees the host crouch / prone / slide", seenByClient.Contains("Crouching") && seenByClient.Contains("Prone") && seenByClient.Contains("Sliding"), seenByClient);
            Check("client sees the host sprint and jump", seenByClient.Contains("Sprinting") && F(w, "maxy") > 0.4f, lastReply);
            Check("client sees the host switch weapons and swing", seenByClient.Contains("Melee") && seenByClient.Contains("swing1"), seenByClient);

            // --- Damage, decided by the host, same on both sides
            yield return Local("tp 4 -6 0");
            yield return Remote("tp 4 6 180");
            yield return new WaitForSeconds(0.6f);
            yield return Remote("shoot 2");
            yield return new WaitForSeconds(0.5f);
            yield return Remote("q");
            var c = Parse(lastReply);
            Check("client → host: 2 hits = 50", Mathf.Approximately(rig.Health.Health, 50f), $"host hp={rig.Health.Health}");
            Check("client → host: HP equal on both sides", Mathf.Approximately(F(c, "hp_other"), rig.Health.Health) && Mathf.Approximately(Hp(Mine), rig.Health.Health),
                $"client sees {S(c, "hp_other")}, host avatar {Hp(Mine)}");
            yield return Local("shoot 1");
            yield return new WaitForSeconds(0.5f);
            yield return Remote("q");
            c = Parse(lastReply);
            Check("host → client: 1 hit = 75, equal on both sides", Mathf.Approximately(Hp(Other), 75f) && Mathf.Approximately(F(c, "hp_self"), 75f),
                $"host sees {Hp(Other)}, client has {S(c, "hp_self")}");

            // Knife (host): step up to the client.
            yield return Local("tp 4 4.6 0");
            yield return Local("weapon melee");
            yield return Local("swing");
            yield return new WaitForSeconds(0.4f);
            yield return Remote("q");
            c = Parse(lastReply);
            Check("host knife → client: 50 damage, equal on both sides", Mathf.Approximately(Hp(Other), 25f) && Mathf.Approximately(F(c, "hp_self"), 25f),
                $"host sees {Hp(Other)}, client has {S(c, "hp_self")}");
            yield return Local("weapon gun");

            // --- Walls block
            yield return Local("tp 0 -4 0");
            yield return Remote("tp 0 4 180");
            yield return new WaitForSeconds(0.6f);
            float hostHp = rig.Health.Health;
            yield return Remote("shoot 2");
            yield return Local("shoot 1");
            yield return new WaitForSeconds(0.5f);
            Check("wall blocks client → host", Mathf.Approximately(rig.Health.Health, hostHp), $"{hostHp} → {rig.Health.Health}");
            Check("wall blocks host → client", Mathf.Approximately(Hp(Other), 25f), $"client hp {Hp(Other)}");

            // --- Refused requests (sent unchecked, as a modified client could)
            int accepted = Other.ServerShotsAccepted;
            yield return Remote("rawfire 5");
            Check("host refuses shots faster than the fire interval", Other.ServerShotsAccepted - accepted == 1, $"accepted {Other.ServerShotsAccepted - accepted} of 5");
            c = Parse(lastReply);
            Check("client is told: too fast", S(c, "lastrej") == nameof(ShotRejection.TooFast), lastReply);
            yield return new WaitForSeconds(0.3f);
            accepted = Other.ServerShotsAccepted;
            yield return Remote("reloadraw");
            c = Parse(lastReply);
            Check("host refuses a shot during reload", Other.ServerShotsAccepted == accepted && S(c, "lastrej") == nameof(ShotRejection.Reloading), lastReply);
            yield return new WaitForSeconds(2f);

            // Kill the client, then it tries to shoot.
            yield return Local("tp 4 -6 0");
            yield return Remote("tp 4 6 180");
            yield return new WaitForSeconds(0.6f);
            yield return Local("shoot 1");
            yield return new WaitForSeconds(0.5f);
            Check("client dies at 0 HP (host)", Other.Dead.Value && Hp(Other) == 0f, $"hp={Hp(Other)}");
            yield return Remote("q");
            c = Parse(lastReply);
            Check("client sees itself dead, input blocked", S(c, "dead_self") == "True" && S(c, "blocked") == "True", lastReply);
            hostHp = rig.Health.Health;
            accepted = Other.ServerShotsAccepted;
            yield return Remote("rawfire 1");
            c = Parse(lastReply);
            yield return new WaitForSeconds(0.3f);
            Check("host refuses a dead player's shot", Other.ServerShotsAccepted == accepted && S(c, "lastrej") == nameof(ShotRejection.Dead)
                && Mathf.Approximately(rig.Health.Health, hostHp), lastReply);
            yield return Local("shoot 1");
            yield return new WaitForSeconds(0.4f);
            Check("no damage to an already dead player", Hp(Other) == 0f);

            // --- Respawn (requested by the client, done by the host)
            yield return Remote("respawn");
            yield return new WaitForSeconds(0.4f);
            yield return Remote("q");
            c = Parse(lastReply);
            Check("respawn: host sees the client alive at full HP", !Other.Dead.Value && Mathf.Approximately(Hp(Other), 100f));
            Check("respawn: client alive, input back, full magazine", S(c, "dead_self") == "False" && S(c, "blocked") == "False"
                && S(c, "hp_self") == "100" && S(c, "loaded") == rig.Gun.Definition.magazineSize.ToString(), lastReply);

            // --- Menu online: the match goes on, the one with the menu open still takes hits
            yield return Local("tp 4 -6 0");
            yield return Remote("tp 4 6 180");
            yield return new WaitForSeconds(0.6f);
            yield return Local("menu open");
            hostHp = rig.Health.Health;
            StartWatch();
            yield return Remote("move 0.5");
            Check("menu open on host: client still moves", watchMaxDz > 0.5f, $"dz={watchMaxDz:0.00}");
            yield return Remote("tp 4 6 180");
            yield return new WaitForSeconds(0.4f);
            yield return Remote("shoot 1");
            yield return new WaitForSeconds(0.5f);
            Check("menu open on host: host still takes hits", rig.Health.Health < hostHp, $"{hostHp} → {rig.Health.Health}");
            Check("menu open on host: no pause, own input blocked", Mathf.Approximately(Time.timeScale, 1f) && rig.Input.IsBlocked);
            yield return Local("menu close");
            yield return Remote("menu open");
            float clientHp = Hp(Other);
            yield return Local("shoot 1");
            yield return new WaitForSeconds(0.5f);
            yield return Remote("q");
            c = Parse(lastReply);
            Check("menu open on client: client still takes hits, no pause", Hp(Other) < clientHp && S(c, "timescale") == "1" && S(c, "blocked") == "True", lastReply);
            yield return Remote("menu close");

            // --- Leave and rejoin: no duplicated players or subscriptions
            int requestedBefore;
            yield return Remote("leave_rejoin");
            float end = Time.realtimeSinceStartup + 5f;
            while (NetworkAvatar.All.Count > 1 && Time.realtimeSinceStartup < end)
                yield return null;
            Check("host: client left, 1 avatar", NetworkAvatar.All.Count == 1);
            yield return WaitForHello();
            h = Parse(hello);
            Check("rejoined: 2 avatars on both, 1 binding on the client", NetworkAvatar.All.Count == 2 && S(h, "avatars") == "2" && S(h, "bindings") == "1", hello);
            Check("rejoined: host still has 1 binding", NetworkAvatar.LocalBindings == 1);
            yield return Local("tp 4 -6 0");
            yield return Remote("tp 4 6 180");
            yield return new WaitForSeconds(0.6f);
            yield return Remote("q");
            requestedBefore = (int)F(Parse(lastReply), "requested");
            accepted = Other.ServerShotsAccepted;
            yield return Remote("shoot 1");
            c = Parse(lastReply);
            Check("rejoined: one shot = one request = one accepted shot", (int)F(c, "requested") - requestedBefore == 1 && Other.ServerShotsAccepted - accepted == 1,
                $"requested +{(int)F(c, "requested") - requestedBefore}, accepted +{Other.ServerShotsAccepted - accepted}");

            // --- Host ends: the client is not stranded
            yield return Remote("expect_host_end");
            yield return new WaitForSeconds(0.3f);
            session.Leave();
            yield return new WaitForSeconds(1f);
            Check("host back in the lobby after ending", FindAnyObjectByType<OnlineSessionUi>().Lobby.activeSelf && !session.IsActive);
            Finish();
        }
    }
}
