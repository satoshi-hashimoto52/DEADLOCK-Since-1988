using System;
using System.Collections;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Gacha;
using Deadlock.Game;
using Deadlock.Home;
using Deadlock.Input;
using Deadlock.Match;
using Deadlock.Player;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 35 on the device's own scenes (offline: the local player and seven NPCs in the ruins; the match clock runs
    /// faster): the home's Deadline entry, MATCHMAKING, the real loading, the countdown without combat, the start places,
    /// kills on NPCs, the end, the result with the points saved once, "もう一度対戦", leaving with its confirmation and
    /// the NPC taking the character over, a simulated disconnection, the 300 s limit, a failed set-up, the ruins test
    /// field unchanged when entered normally, and the screens inside the Safe Area. The gacha data stays in memory.
    /// The match clock runs faster in every phase but the map load (<see cref="LoadAtRealSpeed"/>): the load's timeout
    /// and its slow-load mark are measured on the match clock, so the load itself runs at the real clock as in the game.
    /// </summary>
    public class MatchFlowTests : InputTestFixture
    {
        MemoryGachaStore store;
        MemoryGachaStore journalStore;
        MemoryGachaStore diagnosticsStore;

        LoadAtRealSpeed pacer;

        /// <summary>
        /// The test's match-clock speed (<see cref="Fast"/>) in every phase except the map load, which runs at the real
        /// clock (speed 1): the game measures the load timeout (30 s) and the slow-load mark (10 s) on the match clock, so a
        /// normal load of about a second at 20x or 60x would count as 20 to 60 s. Runs before the match flow every frame.
        /// </summary>
        [DefaultExecutionOrder(-1000)]
        sealed class LoadAtRealSpeed : MonoBehaviour
        {
            public static float Fast = 20f;

            void Update()
            {
                var flow = MatchFlow.Instance;
                MatchFlow.Speed = flow != null && flow.Session != null && flow.Session.Phase == MatchPhase.Loading ? 1f : Fast;
            }
        }

        public override void Setup()
        {
            base.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            store = new MemoryGachaStore();
            GachaAccount.StoreOverride = store;
            GachaAccount.Reset();
            journalStore = new MemoryGachaStore();
            MatchRewardAccount.StoreOverride = journalStore;
            MatchRewardAccount.Reset();
            diagnosticsStore = new MemoryGachaStore();
            LoadDiagnosticsAccount.StoreOverride = diagnosticsStore;
            LoadDiagnosticsAccount.DevelopmentOverride = null;
            LoadDiagnosticsAccount.Reset();
            pacer = new GameObject("LoadAtRealSpeed").AddComponent<LoadAtRealSpeed>();
            UnityEngine.Object.DontDestroyOnLoad(pacer.gameObject);
            LoadAtRealSpeed.Fast = 20f;
            MatchFlow.Speed = LoadAtRealSpeed.Fast;
            MatchBot.HoldFire = true;
            DevGunSelection.ClearSelection();
        }

        public override void TearDown()
        {
            if (MatchFlow.Instance != null)
                UnityEngine.Object.Destroy(MatchFlow.Instance.gameObject);
            if (pacer != null)
                UnityEngine.Object.Destroy(pacer.gameObject);
            MatchFlow.Speed = 1f;
            MatchFlow.MapScene = GameScenes.Ruins;
            MatchBot.HoldFire = false;
            GachaAccount.StoreOverride = null;
            GachaAccount.Reset();
            MatchRewardAccount.StoreOverride = null;
            MatchRewardAccount.Reset();
            LoadDiagnosticsAccount.StoreOverride = null;
            LoadDiagnosticsAccount.DevelopmentOverride = null;
            LoadDiagnosticsAccount.Reset();
            DevGunSelection.ClearSelection();
            Time.timeScale = 1f;
            base.TearDown();
        }

        static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end)
                    Assert.Fail("timed out: " + what);
                yield return null;
            }
        }

        static IEnumerator LoadHome()
        {
            SceneManager.LoadScene(GameScenes.Home);
            yield return null;
            yield return null;
        }

        /// <summary>From the home to the start of play.</summary>
        static IEnumerator StartToPlay()
        {
            yield return WaitFor(() => MatchFlow.Instance != null && MatchFlow.Instance.Session.Phase == MatchPhase.Playing, 60f, "playing");
        }

        static Button FlowButton(string name) => MatchFlow.Instance.GetComponentsInChildren<Button>(true).First(b => b.name == name);

        [UnityTest]
        public IEnumerator Deadline_MatchmakingLoadingCountdown_Play_Result_PointsOnce_Rematch()
        {
            // Two failed loads before (Stage 36B): a normal start of the match resets the count.
            for (int i = 0; i < 2; i++)
            {
                var t = new LoadTrace(0f);
                t.Begin(LoadStage.SceneLoad, 0f);
                LoadDiagnosticsAccount.Instance.Failure(t, 30f, "TIMEOUT", true);
            }
            yield return LoadHome();
            var menu = UnityEngine.Object.FindAnyObjectByType<HomeMenu>();
            menu.Layout.Element(HomeMenu.Survival).GetComponent<Button>().onClick.Invoke();
            var row = menu.PanelObject("SurvivalPanel").GetComponentsInChildren<RectTransform>(true).First(r => r.name == "Deadline（デッドライン）");
            StringAssert.Contains("テスト", row.Find("Status").GetComponent<Text>().text);
            row.GetComponent<Button>().onClick.Invoke();
            var flow = MatchFlow.Instance;
            Assert.IsNotNull(flow, "matchmaking started");
            Assert.AreEqual(MatchFlow.View.Matchmaking, flow.Current);
            yield return null;
            var texts = flow.GetComponentsInChildren<Text>(true);
            Assert.IsTrue(texts.Any(t => t.text == "MATCHMAKING"));
            StringAssert.Contains("1/8", texts.First(t => t.name == "Humans").text);
            StringAssert.Contains("残り", texts.First(t => t.name == "Time").text);
            Assert.IsTrue(FlowButton("MatchCancel").interactable);

            // Confirmed after 15 s (match clock): NPCs fill, cancelling is refused, the map loads for real.
            yield return WaitFor(() => flow.Session.Phase != MatchPhase.Matchmaking, 10f, "confirmed");
            Assert.AreEqual(7, flow.Session.Slots.Count(s => s.IsNpc));
            Assert.IsFalse(flow.CancelMatchmaking(), "no cancel after the confirmation");
            float seenProgress = 0f;
            yield return WaitFor(() =>
            {
                if (flow.Session.Phase == MatchPhase.Loading)
                    seenProgress = Mathf.Max(seenProgress, flow.LocalSlot.LoadProgress);
                return flow.Session.Phase == MatchPhase.Countdown;
            }, 60f, "countdown");
            Assert.AreEqual(1f, flow.LocalSlot.LoadProgress);
            Assert.AreEqual(GameScenes.Ruins, SceneManager.GetActiveScene().name);

            // Countdown: no combat; eight bodies on planned places.
            var arena = flow.Arena;
            Assert.IsNotNull(arena);
            Assert.IsTrue(arena.Rig.Input.IsBlocked, "no move or shot during the countdown");
            Assert.AreEqual(7, arena.Bots.Count);
            Assert.AreEqual(8, arena.Spawns.Count);
            for (int i = 0; i < 8; i++)
                for (int j = i + 1; j < 8; j++)
                    Assert.Greater(Vector3.Distance(arena.Spawns[i], arena.Spawns[j]), 4.9f, $"start places {i} and {j} apart");
            Assert.AreEqual(arena.Spawns[0].x, arena.Rig.transform.position.x, 0.2f, "the local player on place 1");
            Assert.IsFalse(UnityEngine.Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None).Any(), "no zombies in a match");
            TestContext.WriteLine("spawn log: " + string.Join(" / ", arena.PlanLog));

            // The load diagnostics: every step measured, the network step marked as not there (offline).
            var success = LoadDiagnosticsAccount.Instance.Entries.Last(e => e.eventType == "LOAD_SUCCESS");
            Assert.AreEqual("complete", success.stage);
            CollectionAssert.AreEqual(new[] { "scene_load", "asset_init", "network_sync", "participant_ready" }, success.stages.Select(x => x.stage).ToArray());
            Assert.AreEqual(LoadTrace.Done, success.stages[0].status);
            Assert.Greater(success.stages[0].seconds, 0f);
            Assert.AreEqual(LoadTrace.Done, success.stages[1].status);
            Assert.AreEqual(LoadTrace.NotSupported, success.stages[2].status);
            Assert.AreEqual(-1f, success.stages[2].seconds);
            Assert.AreEqual(LoadTrace.Done, success.stages[3].status);
            Assert.GreaterOrEqual(success.totalSeconds, success.stages[0].seconds);

            yield return StartToPlay();
            Assert.AreEqual(0, LoadDiagnosticsAccount.Instance.ConsecutiveFailures, "taking part resets the failures in a row");
            Assert.IsFalse(arena.Rig.Input.IsBlocked, "combat open");
            Assert.AreEqual(MatchFlow.View.Playing, flow.Current);
            // The local player's own hit (as a projectile or knife reaches a bot) counts for place 1.
            var firstBot = arena.Bots.Values.First();
            Assert.AreEqual(DamageResult.Damaged, firstBot.ApplyDamage(30f, firstBot.transform.position, Vector3.forward));
            Assert.AreEqual(30f, flow.LocalSlot.DamageDealt);
            foreach (var bot in arena.Bots.Values.ToList())
                arena.ApplyHit(flow.LocalSlot.Id, bot.SlotId, 1000f, Vector3.zero);
            yield return WaitFor(() => flow.Current == MatchFlow.View.Result, 5f, "result");
            Assert.AreEqual(MatchEndReason.LastSurvivor, flow.Session.Result.Reason);
            Assert.AreEqual(7, flow.LocalSlot.Kills);
            Assert.AreEqual(10, GachaAccount.Service.State.points, "1st: 10 pt");
            StringAssert.StartsWith("1位\tプレイヤー1（あなた）\t生存\t7\t", flow.ResultTable.Split('\n')[1]);
            StringAssert.Contains("10pt", flow.RewardText);
            Assert.AreEqual(9, flow.ResultTable.Trim().Split('\n').Length, "header and all eight");
            Assert.IsTrue(arena.Rig.Input.IsBlocked, "no combat after the end");
            flow.RetryReward();
            Assert.AreEqual(10, GachaAccount.Service.State.points, "never twice");

            yield return CheckInsideSafeArea(flow, "Result");

            // もう一度対戦: back to a new matchmaking, nothing carried over.
            string oldId = flow.Session.MatchId;
            FlowButton("Again").onClick.Invoke();
            yield return WaitFor(() => MatchFlow.Instance != null && MatchFlow.Instance != flow, 10f, "new matchmaking");
            var next = MatchFlow.Instance;
            Assert.AreEqual(GameScenes.Home, SceneManager.GetActiveScene().name);
            Assert.AreEqual(MatchPhase.Matchmaking, next.Session.Phase);
            Assert.AreNotEqual(oldId, next.Session.MatchId);
            Assert.IsTrue(next.CancelMatchmaking());
            yield return null;
            Assert.IsNull(MatchFlow.Instance);
            Assert.AreEqual(10, GachaAccount.Service.State.points);
        }

        IEnumerator CheckInsideSafeArea(MatchFlow flow, string panel)
        {
            var canvas = flow.GetComponent<Canvas>();
            var camGo = new GameObject("AspectCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 0;
            var root = (RectTransform)flow.transform.Find(panel);
            var area = (RectTransform)root.Find("SafeArea");
            var box = (RectTransform)area.Find("Box");
            var fitter = area.GetComponent<SafeAreaFitter>();
            Rect Pixels(RectTransform r)
            {
                var w = new Vector3[4];
                r.GetWorldCorners(w);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, w[0]), b = RectTransformUtility.WorldToScreenPoint(cam, w[2]);
                return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            }
            bool Inside(Rect o, Rect i) => i.xMin >= o.xMin - 0.5f && i.yMin >= o.yMin - 0.5f && i.xMax <= o.xMax + 0.5f && i.yMax <= o.yMax + 0.5f;
            try
            {
                foreach (var (size, sides, bottom) in new[] { (new Vector2Int(2622, 1206), 186f, 63f), (new Vector2Int(2048, 1536), 0f, 0f) })
                {
                    var rt = new RenderTexture(size.x, size.y, 16);
                    cam.targetTexture = rt;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                    fitter.enabled = false;
                    area.anchorMin = new Vector2(sides / size.x, bottom / size.y);
                    area.anchorMax = new Vector2(1f - sides / size.x, 1f);
                    area.offsetMin = area.offsetMax = Vector2.zero;
                    for (int i = 0; i < 3; i++)
                        yield return null;
                    Canvas.ForceUpdateCanvases();
                    Rect safe = Pixels(area), b = Pixels(box);
                    Assert.IsTrue(Inside(safe, b), $"{size}: {panel} inside the Safe Area");
                    foreach (var button in box.GetComponentsInChildren<Button>())
                        Assert.IsTrue(Inside(b, Pixels((RectTransform)button.transform)), $"{size}: {button.name} inside");
                    cam.targetTexture = null;
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                }
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                fitter.enabled = true;
                UnityEngine.Object.Destroy(camGo);
            }
        }

        [UnityTest]
        public IEnumerator Leave_Confirmed_NpcTakesTheCharacterOver_ZeroPoints_Home()
        {
            yield return LoadHome();
            var flow = MatchFlow.Begin();
            yield return StartToPlay();
            var arena = flow.Arena;
            int me = flow.LocalSlot.Id;
            var victim = arena.Bots.Values.First();
            arena.ApplyHit(me, victim.SlotId, 1000f, Vector3.zero);
            var shooter = arena.Bots.Values.First(b => b != victim);
            arena.ApplyHit(shooter.SlotId, me, 30f, shooter.Eye);
            Assert.AreEqual(70f, arena.Rig.Health.Health, "the body shows the session's health");
            arena.Rig.Gun.Ammo.TryConsume(); // a round fired: the NPC takes over the same count

            // The menu has the match's own leave button; scene and retry buttons are hidden.
            var window = arena.Menu.transform.Find("MenuLayer/MenuPanel/Window");
            Assert.IsNotNull(window.Find(MatchArena.LeaveButtonName));
            foreach (var hidden in new[] { "RetryButton", "SceneButton", "TrainingButton", "OnlineButton", "HomeButton", "DevGunButton" })
                Assert.IsFalse(window.Find(hidden)?.gameObject.activeSelf ?? false, hidden);
            window.Find(MatchArena.LeaveButtonName).GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(flow.LeaveConfirmOpen);
            Assert.IsTrue(flow.GetComponentsInChildren<Text>(true).Any(t => t.text == "退出すると、この試合の獲得ポイントは0ptとなり、同じ試合には復帰できません。"));
            FlowButton("LeaveNo").onClick.Invoke();
            Assert.IsFalse(flow.LeaveConfirmOpen);
            Assert.IsFalse(flow.LocalSlot.Departed, "going back changes nothing");

            Vector3 pos = arena.Rig.transform.position;
            float yaw = arena.Rig.Motor.Yaw;
            var weapon = arena.Rig.Gun.Definition;
            int loaded = arena.Rig.Gun.Ammo.Loaded, reserve = arena.Rig.Gun.Ammo.Reserve;
            var session = flow.Session;
            flow.AskLeave();
            FlowButton("LeaveYes").onClick.Invoke();
            // Same frame, before the home loads: the NPC has the character.
            var bot = arena.Bots[me];
            Assert.IsTrue(bot.TookOverHuman);
            Assert.AreEqual(pos, bot.transform.position);
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, bot.transform.eulerAngles.y), 0.1f);
            Assert.AreSame(weapon, bot.Weapon);
            Assert.AreEqual(loaded, bot.Ammo.Loaded);
            Assert.AreEqual(reserve, bot.Ammo.Reserve);
            Assert.IsFalse(string.IsNullOrEmpty(bot.SkinId));
            var slot = session.Slot(me);
            Assert.AreEqual(70f, slot.Health);
            Assert.AreEqual(1, slot.Kills);
            Assert.AreEqual(SlotController.Npc, slot.Controller);
            Assert.AreEqual(SlotConnection.Left, slot.Connection);
            Assert.IsFalse(session.CanRejoin(slot));
            Assert.IsFalse(arena.Rig.gameObject.activeSelf, "the human's control ended");
            yield return WaitFor(() => SceneManager.GetActiveScene().name == GameScenes.Home, 10f, "home");
            Assert.IsNull(MatchFlow.Instance);
            Assert.AreEqual(0, store.Writes, "0 pt: nothing saved");
        }

        /// <summary>Stage 35 completion: the points' save fails at the result; the next start of the home gives them, once.</summary>
        [UnityTest]
        public IEnumerator RewardSaveFails_RecoveredAtTheNextStart_Once()
        {
            yield return LoadHome();
            var flow = MatchFlow.Begin();
            yield return StartToPlay();
            var arena = flow.Arena;
            store.FailNextWrites = 1;
            foreach (var bot in arena.Bots.Values.ToList())
                arena.ApplyHit(flow.LocalSlot.Id, bot.SlotId, 1000f, Vector3.zero);
            yield return WaitFor(() => flow.Current == MatchFlow.View.Result, 5f, "result");
            Assert.AreEqual(GachaOutcome.SaveFailed, flow.Reward.Outcome);
            Assert.AreEqual(0, GachaAccount.Service.State.points, "nothing given when the save failed");
            Assert.IsTrue(FlowButton("RewardRetry").gameObject.activeSelf);
            StringAssert.Contains("次の起動時", flow.RewardText);
            Assert.AreEqual(1, MatchRewardAccount.Journal.Pending.Count, "written down before the grant");
            string matchId = flow.Session.MatchId;

            // The app is closed here: everything in memory is gone; the files stay.
            GachaAccount.Reset();
            MatchRewardAccount.Reset();
            FlowButton("ResultHome").onClick.Invoke();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == GameScenes.Home, 10f, "home");
            yield return null;
            var menu = UnityEngine.Object.FindAnyObjectByType<HomeMenu>();
            Assert.AreEqual(10, GachaAccount.Service.State.points, "recovered at the start");
            StringAssert.Contains("10pt", menu.Message);
            Assert.IsEmpty(MatchRewardAccount.Journal.Pending);
            Assert.IsNotNull(GachaAccount.Service.State.FindRequest(GachaService.MatchRewardRequestId(matchId, 1)));

            // Again: nothing more.
            GachaAccount.Reset();
            MatchRewardAccount.Reset();
            yield return LoadHome();
            Assert.AreEqual(10, GachaAccount.Service.State.points);
        }

        [UnityTest]
        public IEnumerator SimulatedDisconnect_NpcTakesOver_NoRejoin_NoResultNoPoints()
        {
            yield return LoadHome();
            var flow = MatchFlow.Begin();
            yield return StartToPlay();
            var arena = flow.Arena;
            flow.SimulateDisconnect();
            Assert.AreEqual(MatchFlow.View.Disconnected, flow.Current);
            Assert.IsFalse(flow.TryRejoin());
            Assert.AreEqual(8, arena.Bots.Count, "seven NPCs and the taken-over character");
            Assert.AreEqual(SlotConnection.Disconnected, flow.LocalSlot.Connection);
            // The others play on to the end without the human.
            int survivor = arena.Bots.Keys.First(k => k != flow.LocalSlot.Id);
            foreach (var other in arena.Bots.Keys.ToList())
                if (other != survivor)
                    arena.ApplyHit(survivor, other, 1000f, Vector3.zero);
            yield return WaitFor(() => flow.Session.Phase == MatchPhase.Finished, 5f, "finished");
            Assert.AreEqual(MatchFlow.View.Disconnected, flow.Current, "no result for a human who is out");
            Assert.AreEqual(0, flow.Session.Result.Row(flow.LocalSlot.Id).Points);
            Assert.AreEqual(0, store.Writes);
        }

        [UnityTest]
        public IEnumerator TimeUp_300s_EverySurvivorRanked_LowRankOnePoint()
        {
            LoadAtRealSpeed.Fast = 60f; // 300 s in 5 s (the map load at the real clock)
            yield return LoadHome();
            var flow = MatchFlow.Begin();
            yield return StartToPlay();
            var arena = flow.Arena;
            var shooter = arena.Bots.Values.First();
            arena.ApplyHit(shooter.SlotId, flow.LocalSlot.Id, 10f, shooter.Eye); // less health than the others: last of the survivors
            yield return WaitFor(() => flow.Current == MatchFlow.View.Result, 15f, "time up");
            var result = flow.Session.Result;
            Assert.AreEqual(MatchEndReason.TimeUp, result.Reason);
            Assert.AreEqual(300f, result.Duration, 1f);
            Assert.IsTrue(result.Rows.All(r => r.Alive));
            Assert.AreEqual(8, result.Row(flow.LocalSlot.Id).Rank);
            Assert.AreEqual(1, result.Row(flow.LocalSlot.Id).Points, "5th to 8th: 1 pt");
            Assert.AreEqual(1, GachaAccount.Service.State.points);
            Assert.AreEqual(0, result.Rows.Where(r => !r.StartedAsHuman).Sum(r => r.Points), "no NPC gets points");
        }

        [UnityTest]
        public IEnumerator FailedSetUp_LoadFailedScreen_ZeroPoints()
        {
            MatchFlow.MapScene = GameScenes.Home; // a scene without a player: the match cannot be set up
            for (int i = 0; i < 2; i++)
            {
                var t = new LoadTrace(0f);
                t.Begin(LoadStage.SceneLoad, 0f);
                LoadDiagnosticsAccount.Instance.Failure(t, 30f, "TIMEOUT", true);
            }
            yield return LoadHome();
            var flow = MatchFlow.Begin();
            yield return WaitFor(() => flow.Current == MatchFlow.View.LoadFailed, 30f, "load failed");
            Assert.AreEqual(SlotConnection.LoadFailed, flow.LocalSlot.Connection);
            Assert.IsTrue(flow.LocalSlot.TakenOverByNpc);
            var failure = LoadDiagnosticsAccount.Instance.Entries.Last();
            Assert.AreEqual("LOAD_FAILED", failure.eventType);
            Assert.AreEqual("SETUP_FAILED", failure.errorCategory);
            Assert.AreEqual("asset_init", failure.stage);
            Assert.AreEqual(3, LoadDiagnosticsAccount.Instance.ConsecutiveFailures);
            StringAssert.Contains("3回続いています", flow.GetComponentsInChildren<Text>(true).First(t => t.name == "Body" && t.transform.parent.parent.parent.name == "LoadFailed").text,
                "the third failure in a row asks to check the device and network");
            Assert.IsTrue(FlowButton("Rematch").interactable, "rematching is never limited");
            StringAssert.Contains("0pt", flow.GetComponentsInChildren<Text>(true).First(t => t.name == "Body" && t.transform.parent.parent.parent.name == "LoadFailed").text);
            Assert.IsNotNull(FlowButton("Rematch"));
            FlowButton("Home").onClick.Invoke();
            yield return WaitFor(() => MatchFlow.Instance == null, 5f, "closed");
            Assert.AreEqual(0, store.Writes);
        }

        static IEnumerator WaitMatchSeconds(MatchFlow flow, float seconds)
        {
            float until = flow.Now + seconds;
            yield return WaitFor(() => flow.Now >= until, 30f, $"{seconds} s of match time");
        }

        /// <summary>
        /// Stage 36A: dying starts the spectating at once; the picture is 10 s old (the watched one's past health and
        /// kills, the camera at their past eyes), previous / next, the automatic switch when the watched one dies at
        /// the shown time, the sound events at their past time, the game's HUD hidden, no combat, and the result as
        /// soon as the real match ends.
        /// </summary>
        [UnityTest]
        public IEnumerator Spectate_TenSecondsAgo_HudHidden_Switch_AutoSwitch_Sounds_EndsToResult()
        {
            yield return LoadHome();
            var flow = MatchFlow.Begin();
            yield return StartToPlay();
            var arena = flow.Arena;
            int me = flow.LocalSlot.Id;
            var router = arena.Rig.Input;
            router.RequireCursorLock = false;
            router.SetMode(InputMode.Desktop);
            var mouse = InputSystem.GetDevice<Mouse>();
            yield return WaitMatchSeconds(flow, 11f); // more than 10 s recorded

            // A shot of the local player (a sound event) and a hit on the bot to be watched, at known match times.
            Press(mouse.leftButton);
            yield return null;
            yield return null;
            Release(mouse.leftButton);
            yield return null;
            var shot = arena.Replay.EventsBetween(-1f, flow.Now).LastOrDefault(e => e.Kind == ReplayEventKind.Gunshot && e.SlotId == me);
            Assert.AreEqual(ReplayEventKind.Gunshot, shot.Kind, "the shot is recorded with its time and place");
            var ids = arena.Bots.Keys.OrderBy(k => k).ToList();
            int watched = ids[0], other = ids[1], victim = ids[2];
            float hitTime = flow.Now;
            arena.ApplyHit(other, watched, 40f, Vector3.zero);
            arena.ApplyHit(watched, victim, 1000f, Vector3.zero);
            yield return WaitMatchSeconds(flow, 1f);

            // Death: spectating at once.
            arena.ApplyHit(other, me, 1000f, Vector3.zero);
            yield return null;
            // The spectator takes its shown time in LateUpdate, after this point of the frame; the match clock moves only
            // in MatchFlow.Update. So the clock read here is the one the spectator shows 10 s before, and after the next
            // frame's Update the clock has moved on by one frame (up to 0.7 s at 20x on a slow frame).
            float shownFrom = flow.Now;
            yield return null;
            Assert.AreEqual(MatchFlow.View.Spectating, flow.Current);
            var sp = arena.Spectator;
            Assert.IsNotNull(sp, "no kill camera, no wait");
            Assert.IsFalse(sp.Waiting);
            Assert.AreEqual(shownFrom - MatchReplay.DelaySeconds, sp.PlaybackTime, 0.001f, "10 s ago");
            Assert.IsTrue(sp.Camera.enabled);
            Assert.IsTrue(router.IsBlocked, "no combat while spectating");
            Assert.IsFalse(arena.Rig.GetComponentsInChildren<Camera>().Any(c => c.enabled), "the player's own cameras are off");
            Assert.IsFalse(UnityEngine.Object.FindAnyObjectByType<HudController>().GetComponentInParent<Canvas>().rootCanvas.enabled,
                "no map, markers, footsteps, ammo panel or controls");
            Assert.IsFalse(UnityEngine.Object.FindObjectsByType<Deadlock.Map.EnemyDetector>(FindObjectsSortMode.None).Any(d => d.enabled));
            StringAssert.DoesNotContain("生存", flow.GetComponentsInChildren<Text>(true).First(t => t.transform.parent.name == "MatchHud").text, "no live count");
            StringAssert.Contains("試合の残り時間", flow.GetComponentsInChildren<Text>(true).First(t => t.transform.parent.name == "MatchHud").text);

            // Watch the hit bot: at the shown time (before the hit) its past health and kills, the camera at its past eyes.
            while (sp.TargetSlot != watched)
            {
                sp.Next();
                yield return null;
            }
            yield return null;
            Assert.Less(sp.PlaybackTime, hitTime);
            Assert.AreEqual(100f, sp.TargetState.Health, "health 10 s ago (it is 60 now)");
            Assert.AreEqual(0, sp.TargetState.Kills, "kills 10 s ago (1 now)");
            Assert.AreEqual(60f, flow.Session.Slot(watched).Health);
            StringAssert.Contains("HP 100", flow.SpectateStateText);
            StringAssert.Contains("キル 0", flow.SpectateStateText);
            StringAssert.Contains("ライフル", flow.SpectateStateText, "the weapon 10 s ago");
            Assert.IsTrue(arena.Replay.TryState(watched, sp.PlaybackTime, out var past));
            Assert.AreEqual(past.Position + Vector3.up * past.EyeHeight, sp.Camera.transform.position);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(-past.Pitch, past.Yaw, 0f), sp.Camera.transform.rotation), 0.5f, "its past look");
            StringAssert.Contains(flow.Session.Slot(watched).Name, flow.SpectateTargetText);
            // The local player's shot is heard only when the shown time reaches it.
            Assert.IsFalse(sp.PlayedSounds.Any(e => e.Kind == ReplayEventKind.Gunshot && e.SlotId == me && Mathf.Approximately(e.Time, shot.Time)), "not before its time");
            yield return WaitFor(() => sp.PlaybackTime >= shot.Time + 0.01f, 10f, "shown time passes the shot");
            yield return null;
            Assert.IsTrue(sp.PlayedSounds.Any(e => e.Kind == ReplayEventKind.Gunshot && e.SlotId == me && Mathf.Approximately(e.Time, shot.Time)), "heard at its past time");
            Assert.AreEqual(shot.Position, sp.PlayedSounds.First(e => e.Kind == ReplayEventKind.Gunshot && e.SlotId == me && Mathf.Approximately(e.Time, shot.Time)).Position,
                "at its place");
            yield return WaitFor(() => sp.PlaybackTime > hitTime + 0.1f, 10f, "shown time passes the hit");
            yield return null;
            Assert.AreEqual(60f, sp.TargetState.Health);
            Assert.AreEqual(1, sp.TargetState.Kills);

            // Previous / next.
            var candidates = sp.CurrentCandidates();
            Assert.IsFalse(candidates.Contains(me), "never the spectator");
            Assert.IsFalse(candidates.Contains(victim), "dead at the shown time");
            sp.Next();
            Assert.AreNotEqual(watched, sp.TargetSlot);
            sp.Previous();
            Assert.AreEqual(watched, sp.TargetSlot);

            // The watched one dies now: still watched until the shown time reaches the death, then the next living one.
            float deathTime = flow.Now;
            arena.ApplyHit(other, watched, 1000f, Vector3.zero);
            yield return null;
            Assert.AreEqual(watched, sp.TargetSlot, "alive in the past");
            yield return WaitFor(() => sp.PlaybackTime > deathTime + 0.1f, 10f, "shown time reaches the death");
            yield return null;
            Assert.AreNotEqual(watched, sp.TargetSlot, "switched automatically");
            Assert.IsTrue(sp.CurrentCandidates().Contains(sp.TargetSlot));

            // The real match ends: the result at once (the delay is dropped).
            foreach (var id in arena.Bots.Keys.Where(k => k != other).ToList())
                arena.ApplyHit(other, id, 1000f, Vector3.zero);
            yield return WaitFor(() => flow.Current == MatchFlow.View.Result, 3f, "result");
            yield return null;
            Assert.IsNull(arena.Spectator);
            Assert.AreEqual(0, GachaAccount.Service.State.points, "dead: 0 pt");
        }

        [UnityTest]
        public IEnumerator Spectate_LessThanTenSecondsRecorded_Waits_NothingMadeUp()
        {
            yield return LoadHome();
            var flow = MatchFlow.Begin();
            yield return StartToPlay();
            var arena = flow.Arena;
            arena.ApplyHit(arena.Bots.Keys.First(), flow.LocalSlot.Id, 1000f, Vector3.zero);
            yield return null;
            yield return null;
            var sp = arena.Spectator;
            Assert.IsNotNull(sp);
            Assert.IsTrue(sp.Waiting, "10 s of past do not exist yet");
            Assert.IsFalse(sp.Camera.enabled, "nothing shown");
            StringAssert.Contains("まだありません", flow.SpectateWaitingText);
            yield return WaitFor(() => !sp.Waiting, 10f, "the record reaches 10 s");
            Assert.GreaterOrEqual(sp.PlaybackTime, arena.Replay.RecordingStart);
            Assert.IsTrue(sp.Camera.enabled);
            // Leaving from the spectating: confirmation, then home.
            FlowButton("SpectateLeave").onClick.Invoke();
            Assert.IsTrue(flow.LeaveConfirmOpen);
            FlowButton("LeaveYes").onClick.Invoke();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == GameScenes.Home, 10f, "home");
        }

        [UnityTest]
        public IEnumerator LoadDiagnostics_DevelopmentExport_InSettings_OffInARelease()
        {
            yield return LoadHome();
            var menu = UnityEngine.Object.FindAnyObjectByType<HomeMenu>();
            var window = menu.PanelObject("SettingsPanel").transform.Find("SafeArea/Window");
            Assert.IsNotNull(window.Find(Deadlock.Dev.LoadDiagnosticsExport.ButtonName), "development: the export button");
            Assert.IsNotNull(LoadDiagnosticsAccount.Instance.ExportJson());
            LoadDiagnosticsAccount.DevelopmentOverride = false;
            LoadDiagnosticsAccount.Reset();
            Assert.IsNull(LoadDiagnosticsAccount.Instance.ExportJson(), "release: no export");
            StringAssert.Contains("無効", Deadlock.Dev.LoadDiagnosticsExport.Export(out string path));
            Assert.IsNull(path, "nothing written");
        }

        [UnityTest]
        public IEnumerator RuinsEnteredNormally_ModeUnchanged()
        {
            SceneManager.LoadScene(GameScenes.Ruins);
            yield return null;
            yield return null;
            Assert.IsNull(UnityEngine.Object.FindAnyObjectByType<MatchArena>());
            Assert.IsNull(MatchFlow.Instance);
            Assert.IsTrue(UnityEngine.Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None).Length > 0, "the zombies of the test field");
            Assert.IsTrue(UnityEngine.Object.FindAnyObjectByType<TrainingSession>().enabled);
            var menu = UnityEngine.Object.FindAnyObjectByType<GameMenu>();
            var window = menu.transform.Find("MenuLayer/MenuPanel/Window");
            Assert.IsTrue(window.Find("RetryButton").gameObject.activeSelf);
            Assert.IsNull(window.Find(MatchArena.LeaveButtonName));
            Assert.IsTrue(UnityEngine.Object.FindAnyObjectByType<OfflineSessionPause>().enabled, "the menu still pauses the test field");
        }
    }
}
