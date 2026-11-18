using System.Collections;
using System.Globalization;
using System.Text;
using Deadlock.Gacha;
using Deadlock.Game;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deadlock.Match
{
    /// <summary>
    /// The normal match from the home to the result, for the offline check (Stage 35): MATCHMAKING (humans x/8, time
    /// left, cancel until confirmed), the loading of the map (the real progress of the scene load and the match set-up,
    /// players ready x/8, time left), the 5 s countdown, the match (time left, survivors), the waiting display after
    /// death (spectating is Stage 36), leaving with its confirmation, and the result of all eight with "もう一度対戦" and
    /// "ホームへ戻る". The rules are the <see cref="MatchSession"/>'s; this only shows them and reports the device's
    /// events. Offline the only human is this device's player: the seven other places are NPCs. Kept across the
    /// scene load; gone when the player is back home.
    /// </summary>
    public sealed class MatchFlow : MonoBehaviour
    {
        public const string LeaveMessage = "退出すると、この試合の獲得ポイントは0ptとなり、同じ試合には復帰できません。";
        public const string RepeatedFailureWarning = "ロードの失敗が3回続いています。端末の空き容量・ほかのアプリの動作・通信環境を確認してください。再マッチングは何回でもできます。";
        public const string SpectateTitle = "観戦中（10秒前の試合）";

        public enum View
        {
            None,
            Matchmaking,
            Loading,
            LoadFailed,
            Countdown,
            Playing,
            Spectating,
            Disconnected,
            Result,
            Cancelled,
        }

        public static MatchFlow Instance { get; private set; }
        /// <summary>Speed of the match clock (tests only; 1 = real time).</summary>
        public static float Speed = 1f;
        public static string MapScene = GameScenes.Ruins;
        static bool rematchOnHome;

        float clock;
        bool loadStarted, setupStarted, loadLogged;
        LoadTrace trace;
        int seed;
        RectTransform safe;
        GameObject matchmaking, loading, failed, countdown, hud, dead, disconnected, result, cancelled, leaveConfirm;
        Text mmHumans, mmTime, mmStatus, ldMine, ldReady, ldTime, failedText, countdownText, hudText, resultTitle, resultReward, cancelledText;
        RectTransform resultTable;
        string resultTableText = "";
        Button mmCancel, rewardRetry;
        Text spTarget, spState, spWaiting;

        public MatchSession Session { get; private set; }
        public MatchSlot LocalSlot { get; private set; }
        public MatchArena Arena { get; private set; }
        public string SetupError { get; private set; }
        public GachaResult Reward { get; private set; }
        public View Current { get; private set; }
        public bool LeaveConfirmOpen => leaveConfirm.activeSelf;
        public float Now => clock;
        /// <summary>This load's step-by-step times (Stage 36B).</summary>
        public LoadTrace Trace => trace;
        /// <summary>The result table as lines of tab-separated cells (header first), as shown.</summary>
        public string ResultTable => resultTableText;
        public string RewardText => resultReward.text;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnAnySceneLoaded;
            SceneManager.sceneLoaded += OnAnySceneLoaded;
        }

        static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (rematchOnHome && scene.name == GameScenes.Home)
            {
                rematchOnHome = false;
                Begin();
            }
        }

        /// <summary>Starts matchmaking (the home's Deadline entry, "もう一度対戦", "再マッチング").</summary>
        public static MatchFlow Begin()
        {
            if (Instance != null)
                Destroy(Instance.gameObject);
            var go = new GameObject("MatchFlow", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 800;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var flow = go.AddComponent<MatchFlow>();
            Instance = flow;
            flow.Build();
            flow.seed = System.Environment.TickCount;
            flow.Session = new MatchSession(MatchSession.NewMatchId(), 0f);
            flow.LocalSlot = flow.Session.JoinHuman("プレイヤー1", true);
            flow.Session.PhaseChanged += flow.OnPhase;
            flow.Show(View.Matchmaking);
            return flow;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (Session != null)
                Session.PhaseChanged -= OnPhase;
            SceneManager.sceneLoaded -= OnMapLoaded;
        }

        void Update()
        {
            clock += Time.unscaledDeltaTime * Speed;
            if (Session.Phase != MatchPhase.Finished && Session.Phase != MatchPhase.Cancelled)
                Session.Tick(clock);
            if (Session.Phase == MatchPhase.Loading && !loadStarted)
                StartLoad();
            Refresh();
        }

        void OnPhase(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Loading:
                    StartLoad();
                    break;
                case MatchPhase.Countdown:
                    LogLoadEnd();
                    break;
                case MatchPhase.Playing:
                    if (!LocalSlot.Departed)
                        LoadDiagnosticsAccount.Instance.ParticipatedNormally();
                    break;
                case MatchPhase.Finished:
                    OnFinished();
                    break;
                case MatchPhase.Cancelled:
                    cancelledText.text = Session.CancelReason;
                    Show(View.Cancelled);
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------- loading

        void StartLoad()
        {
            if (loadStarted)
                return;
            loadStarted = true;
            trace = new LoadTrace(clock);
            trace.Begin(LoadStage.SceneLoad, clock);
            Show(View.Loading);
            Time.timeScale = 1f;
            SceneManager.sceneLoaded += OnMapLoaded;
            StartCoroutine(Load());
        }

        IEnumerator Load()
        {
            var op = SceneManager.LoadSceneAsync(MapScene);
            while (op != null && !op.isDone)
            {
                Session.ReportLoad(LocalSlot, op.progress / 0.9f * 0.9f); // the scene: 90 %, the set-up: the rest
                yield return null;
            }
        }

        void OnMapLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != MapScene || setupStarted)
                return;
            setupStarted = true;
            SceneManager.sceneLoaded -= OnMapLoaded;
            trace.End(LoadStage.SceneLoad, clock);
            trace.Begin(LoadStage.AssetInit, clock);
            StartCoroutine(SetUp());
        }

        IEnumerator SetUp()
        {
            yield return null; // the scene's own Start first (NavMesh, player, menu)
            Session.ReportLoad(LocalSlot, 0.95f);
            if (LocalSlot.Departed || Session.Phase != MatchPhase.Loading)
                yield break; // out already (load timeout): nothing is set up
            if (MatchArena.TrySetup(Session, seed, () => clock, out var arena, out string error))
            {
                Arena = arena;
                trace.End(LoadStage.AssetInit, clock);
                trace.MarkNotSupported(LoadStage.NetworkSync, "offline check: no network sync (Stage 37)");
                trace.Begin(LoadStage.ParticipantReady, clock);
                Session.ReportReady(LocalSlot, clock);
            }
            else
            {
                SetupError = error;
                loadLogged = true;
                LoadDiagnosticsAccount.Instance.Failure(trace, clock, "SETUP_FAILED", false);
                Session.FailLoad(LocalSlot, clock);
            }
        }

        /// <summary>The load ended (the countdown began): success, or this player's timeout.</summary>
        void LogLoadEnd()
        {
            if (loadLogged || trace == null)
                return;
            loadLogged = true;
            var diagnostics = LoadDiagnosticsAccount.Instance;
            if (LocalSlot.Departed && LocalSlot.TakeoverReason == SlotConnection.LoadFailed)
                diagnostics.Failure(trace, clock, "TIMEOUT", true);
            else if (!LocalSlot.Departed)
            {
                trace.End(LoadStage.ParticipantReady, clock);
                diagnostics.Success(trace, clock);
            }
        }

        // ------------------------------------------------------------------------------------------- the end

        void OnFinished()
        {
            Arena?.EndSpectate(); // the delay ends with the match: straight to the result
            if (LocalSlot.Departed)
                return; // a human who left or lost the connection sees no result of this match
            var row = Session.Result.Row(LocalSlot.Id);
            Reward = null;
            if (row.Points > 0)
            {
                // Written down first, so a failed save or a stop before the grant is recovered at the next start.
                MatchRewardAccount.Journal.RecordPending(Session.MatchId, row, out _);
                GiveReward(row);
            }
            ShowResult();
        }

        void GiveReward(MatchResultRow row)
        {
            Reward = GachaAccount.Service.GrantMatchReward(Session.MatchId, LocalSlot.Id, row.Points);
            if (Reward.Ok)
                MatchRewardAccount.Journal.Mark(Session.MatchId, LocalSlot.Id, PendingReward.Completed, Reward.Outcome.ToString());
        }

        /// <summary>Saving the points failed: try again (the same match and place, so never twice).</summary>
        public void RetryReward()
        {
            var row = Session.Result?.Row(LocalSlot.Id);
            if (row == null || row.Points <= 0)
                return;
            GiveReward(row);
            ShowResult();
        }

        void ShowResult()
        {
            var r = Session.Result;
            resultTitle.text = "試合結果　" + (r.Reason == MatchEndReason.TimeUp ? "時間切れ（5分）" : r.Reason == MatchEndReason.LastSurvivor ? "生存者が1人になりました" : "生存者がいません");
            foreach (Transform c in resultTable)
                Destroy(c.gameObject);
            var sb = new StringBuilder();
            void Line(int index, bool highlight, params string[] cells)
            {
                float y = -index * 58f;
                if (highlight)
                    UiBuild.Image(UiBuild.Place(UiBuild.Rect("Mine", resultTable), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1320f, 54f)),
                        new Color(1f, 0.74f, 0.36f, 0.18f), false);
                float x = 0f;
                for (int i = 0; i < cells.Length; i++)
                {
                    var t = UiBuild.Text(UiBuild.Place(UiBuild.Rect($"Cell_{index}_{i}", resultTable), new Vector2(0f, 1f), new Vector2(x + 10f, y), new Vector2(Columns[i] - 12f, 54f)),
                        cells[i], index == 0 ? 22 : 24, i == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, index == 0 ? new Color(1f, 1f, 1f, 0.6f) : (Color?)null);
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                    x += Columns[i];
                }
                sb.Append(string.Join("\t", cells)).Append('\n');
            }
            Line(0, false, "順位", "名前", "状態", "キル", "与ダメージ", "残りHP", "獲得pt");
            int n = 1;
            foreach (var row in r.Rows)
            {
                string name = row.Name + (row.IsLocal ? "（あなた）" : "") + (row.TakenOverByNpc ? "（NPC引き継ぎ）" : row.StartedAsHuman ? "" : "（NPC）");
                Line(n++, row.IsLocal, row.Rank + "位", name, row.StateLabel, row.Kills.ToString(CultureInfo.InvariantCulture),
                    Mathf.RoundToInt(row.DamageDealt).ToString(CultureInfo.InvariantCulture), Mathf.CeilToInt(row.Health).ToString(CultureInfo.InvariantCulture), row.Points + "pt");
            }
            resultTableText = sb.ToString();
            var mine = r.Row(LocalSlot.Id);
            string reward = mine.Points > 0 ? $"獲得ポイント：{mine.Points}pt" : "獲得ポイント：0pt（生存していないため）";
            bool failedSave = Reward != null && !Reward.Ok;
            if (Reward != null)
                reward += "　" + Reward.Message + (failedSave ? "（記録してあるので、次の起動時にも付与を再試行します）" : "");
            resultReward.text = reward;
            resultReward.color = failedSave ? new Color(1f, 0.55f, 0.4f) : new Color(1f, 0.74f, 0.36f);
            rewardRetry.gameObject.SetActive(failedSave);
            Show(View.Result);
        }

        static readonly float[] Columns = { 120f, 470f, 150f, 110f, 170f, 140f, 160f };

        // ------------------------------------------------------------------------------------------- player actions

        /// <summary>Cancel the matchmaking (only before it is confirmed; no confirmation, no penalty).</summary>
        public bool CancelMatchmaking()
        {
            if (!Session.Cancel(LocalSlot, out string reason))
            {
                mmStatus.text = reason;
                return false;
            }
            Destroy(gameObject);
            return true;
        }

        public void AskLeave()
        {
            if (Session.Phase != MatchPhase.Countdown && Session.Phase != MatchPhase.Playing)
                return;
            leaveConfirm.SetActive(true);
            leaveConfirm.transform.SetAsLastSibling();
        }

        /// <summary>Leaving confirmed: an NPC takes the character over, this player gets 0 pt and goes home.</summary>
        public void ConfirmLeave()
        {
            leaveConfirm.SetActive(false);
            Arena?.EndSpectate();
            Session.Leave(LocalSlot, clock);
            Arena?.TakeOverLocal();
            GoHome();
        }

        /// <summary>Development only: the connection is lost (offline there is none to lose). Taken over at once, no way back.</summary>
        public void SimulateDisconnect()
        {
            if (!Debug.isDebugBuild || LocalSlot.Departed)
                return;
            Session.Disconnect(LocalSlot, clock);
            Arena?.TakeOverLocal();
            Show(View.Disconnected);
        }

        /// <summary>Coming back into the same match: never after leaving, a lost connection or a failed load.</summary>
        public bool TryRejoin() => Session.CanRejoin(LocalSlot);

        public void Rematch()
        {
            rematchOnHome = true;
            GoHome();
        }

        public void GoHome()
        {
            Destroy(gameObject);
            GameScenes.Load(GameScenes.Home);
        }

        // ------------------------------------------------------------------------------------------- display

        void Show(View v)
        {
            Current = v;
            matchmaking.SetActive(v == View.Matchmaking);
            loading.SetActive(v == View.Loading);
            failed.SetActive(v == View.LoadFailed);
            countdown.SetActive(v == View.Countdown);
            hud.SetActive(v == View.Countdown || v == View.Playing || v == View.Spectating);
            dead.SetActive(v == View.Spectating);
            disconnected.SetActive(v == View.Disconnected);
            result.SetActive(v == View.Result);
            cancelled.SetActive(v == View.Cancelled);
        }

        void Refresh()
        {
            switch (Session.Phase)
            {
                case MatchPhase.Matchmaking:
                    mmHumans.text = $"人間プレイヤー　{Session.HumanCount}/{MatchRules.Slots}";
                    mmTime.text = $"残り {Mathf.CeilToInt(Session.MatchmakingRemaining(clock))} 秒";
                    mmCancel.interactable = true;
                    break;
                case MatchPhase.Loading:
                    if (LocalSlot.Departed && Current != View.LoadFailed)
                        ShowLoadFailed();
                    ldMine.text = $"あなたのロード　{Mathf.FloorToInt(LocalSlot.LoadProgress * 100f)}%";
                    ldReady.text = $"準備完了　{Session.ReadyCount}/{MatchRules.Slots}";
                    ldTime.text = $"残り {Mathf.CeilToInt(Session.LoadingRemaining(clock))} 秒";
                    break;
                case MatchPhase.Countdown:
                    if (LocalSlot.Departed)
                    {
                        if (Current != View.LoadFailed && Current != View.Disconnected)
                            ShowLoadFailed();
                        break;
                    }
                    if (Current != View.Countdown)
                        Show(View.Countdown);
                    countdownText.text = Mathf.CeilToInt(Session.CountdownRemaining(clock)).ToString(CultureInfo.InvariantCulture);
                    Hud();
                    break;
                case MatchPhase.Playing:
                    if (LocalSlot.Departed)
                        break;
                    if (!LocalSlot.Alive && Current != View.Spectating)
                    {
                        Arena?.BeginSpectate(); // at once: no kill camera, no wait
                        Show(View.Spectating);
                    }
                    else if (LocalSlot.Alive && Current != View.Playing)
                        Show(View.Playing);
                    Hud();
                    if (Current == View.Spectating)
                        SpectateHud();
                    break;
            }
        }

        void ShowLoadFailed()
        {
            LogLoadEnd();
            failedText.text = "ロードが完了しなかったため、この試合から外れました（あなたの枠は NPC が引き継ぎます）。獲得ポイントは 0pt です。"
                              + (string.IsNullOrEmpty(SetupError) ? "" : "\n" + SetupError)
                              + (LoadDiagnosticsAccount.Instance.ShouldWarn ? "\n" + RepeatedFailureWarning : "");
            Show(View.LoadFailed);
        }

        void Hud()
        {
            float left = Session.Phase == MatchPhase.Playing ? Session.MatchRemaining(clock) : MatchRules.MatchSeconds;
            int s = Mathf.CeilToInt(left);
            // While spectating only the real time left (no live count of the living: the view is 10 s old).
            hudText.text = Current == View.Spectating
                ? $"試合の残り時間 {s / 60}:{s % 60:00}"
                : $"通常対戦（オフライン検証）　残り {s / 60}:{s % 60:00}　生存 {Session.AliveCount}/{MatchRules.Slots}";
        }

        void SpectateHud()
        {
            var sp = Arena != null ? Arena.Spectator : null;
            if (sp == null || sp.Waiting || sp.TargetSlot < 0)
            {
                float need = Arena != null && !float.IsNaN(Arena.Replay.RecordingStart)
                    ? Arena.Replay.RecordingStart + MatchReplay.DelaySeconds - clock : MatchReplay.DelaySeconds;
                spWaiting.text = sp != null && !sp.Waiting
                    ? "10秒前の時点で生存している参加者がいません"
                    : $"10秒前の記録がまだありません。記録がたまるまで待機しています（あと {Mathf.Max(1, Mathf.CeilToInt(need))} 秒）";
                spTarget.text = "";
                spState.text = "";
                return;
            }
            spWaiting.text = "";
            var st = sp.TargetState;
            var slot = Session.Slot(sp.TargetSlot);
            spTarget.text = "観戦対象：" + (slot != null ? slot.Name : "?");
            spState.text = $"HP {Mathf.CeilToInt(st.Health)}　武器 {WeaponName(st.WeaponId)}　キル {st.Kills}";
        }

        static string WeaponName(string weaponId)
        {
            var catalog = Deadlock.Combat.WeaponCatalog.Load();
            var e = catalog != null ? catalog.Find(weaponId) : null;
            return e != null ? e.displayName : weaponId ?? "—";
        }

        public string SpectateTargetText => spTarget.text;
        public string SpectateStateText => spState.text;
        public string SpectateWaitingText => spWaiting.text;

        GameObject Panel(string name, Vector2 size, bool dim, out RectTransform box)
        {
            var root = UiBuild.Stretch(UiBuild.Rect(name, safe.parent));
            if (dim)
                UiBuild.Image(root, new Color(0f, 0f, 0f, 0.75f), true);
            var area = UiBuild.Stretch(UiBuild.Rect("SafeArea", root));
            area.gameObject.AddComponent<SafeAreaFitter>();
            box = UiBuild.Place(UiBuild.Rect("Box", area), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            UiBuild.Image(box, new Color(0.07f, 0.065f, 0.06f, 0.96f), true);
            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        static Text Label(RectTransform box, string name, string text, float y, int size, Color? color = null, float height = 50f)
        {
            var t = UiBuild.Text(UiBuild.Place(UiBuild.Rect(name, box), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(box.sizeDelta.x - 60f, height)),
                text, size, TextAnchor.MiddleCenter, color);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        static Button Btn(RectTransform box, string name, string label, Vector2 pos, System.Action onClick) =>
            UiBuild.Button(box, name, label, new Vector2(0.5f, 0f), pos, new Vector2(320f, 80f), onClick, 28);

        void Build()
        {
            var root = (RectTransform)transform;
            safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", root));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var amber = new Color(1f, 0.74f, 0.36f);
            var dimText = new Color(1f, 1f, 1f, 0.65f);

            matchmaking = Panel("Matchmaking", new Vector2(980f, 560f), true, out var box);
            Label(box, "Title", "MATCHMAKING", -40f, 56, amber, 70f).fontStyle = FontStyle.Bold;
            mmHumans = Label(box, "Humans", "", -140f, 34);
            mmTime = Label(box, "Time", "", -200f, 30);
            mmStatus = Label(box, "Status", "", -260f, 22, dimText);
            Label(box, "Note", "通常対戦（8枠・最大5分）。オフライン検証のため、ほかの人間プレイヤーは参加しません。\n15秒たつと NPC で8人にそろえて開始します。確定するまではキャンセルできます。", -320f, 20, dimText, 70f);
            mmCancel = Btn(box, "MatchCancel", "キャンセル", new Vector2(0f, 40f), () => CancelMatchmaking());

            loading = Panel("Loading", new Vector2(980f, 440f), true, out box);
            Label(box, "Title", "ロード中", -40f, 44, amber, 60f);
            ldMine = Label(box, "Mine", "", -130f, 32);
            ldReady = Label(box, "Ready", "", -190f, 30);
            ldTime = Label(box, "Time", "", -250f, 28, dimText);
            Label(box, "Note", "30秒以内に準備が終わらない場合は、NPC が枠を引き継ぎます（その場合のポイントは 0pt）。", -320f, 20, dimText, 60f);

            failed = Panel("LoadFailed", new Vector2(980f, 460f), true, out box);
            Label(box, "Title", "ロードに失敗しました", -40f, 40, amber, 60f);
            failedText = Label(box, "Body", "", -120f, 24, null, 150f);
            Btn(box, "Rematch", "再マッチング", new Vector2(-180f, 40f), Rematch);
            Btn(box, "Home", "ホームへ", new Vector2(180f, 40f), GoHome);

            countdown = UiBuild.Stretch(UiBuild.Rect("Countdown", root)).gameObject;
            countdownText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Number", (RectTransform)countdown.transform), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(400f, 240f)),
                "5", 200, TextAnchor.MiddleCenter, amber);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Note", (RectTransform)countdown.transform), new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(900f, 50f)),
                "戦闘開始まで（移動・射撃はできません）", 30, TextAnchor.MiddleCenter);
            countdown.SetActive(false);

            hud = UiBuild.Place(UiBuild.Rect("MatchHud", safe), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(760f, 40f)).gameObject;
            UiBuild.Image((RectTransform)hud.transform, new Color(0f, 0f, 0f, 0.45f), false);
            hudText = UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("Text", (RectTransform)hud.transform)), "", 22, TextAnchor.MiddleCenter);
            hud.SetActive(false);

            // Spectating: who and their state 10 s ago (top left), previous / next / leave (bottom). Nothing else.
            var spRoot = UiBuild.Stretch(UiBuild.Rect("Spectate", safe));
            var info = UiBuild.Place(UiBuild.Rect("Info", spRoot), new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(560f, 150f));
            UiBuild.Image(info, new Color(0f, 0f, 0f, 0.55f), false);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", info), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(540f, 40f)), SpectateTitle, 24,
                TextAnchor.MiddleLeft, amber);
            spTarget = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Target", info), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(540f, 40f)), "", 26, TextAnchor.MiddleLeft);
            spState = UiBuild.Text(UiBuild.Place(UiBuild.Rect("State", info), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(540f, 40f)), "", 24, TextAnchor.MiddleLeft);
            spWaiting = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Waiting", spRoot), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 80f)), "", 28,
                TextAnchor.MiddleCenter);
            UiBuild.Button(spRoot, "SpectatePrev", "前の対象", new Vector2(0.5f, 0f), new Vector2(-300f, 30f), new Vector2(260f, 76f), () => Arena?.Spectator?.Previous(), 26);
            UiBuild.Button(spRoot, "SpectateNext", "次の対象", new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(260f, 76f), () => Arena?.Spectator?.Next(), 26);
            UiBuild.Button(spRoot, "SpectateLeave", "退出", new Vector2(0.5f, 0f), new Vector2(300f, 30f), new Vector2(260f, 76f), AskLeave, 26);
            dead = spRoot.gameObject;
            dead.SetActive(false);

            disconnected = Panel("Disconnected", new Vector2(980f, 380f), true, out box);
            Label(box, "Title", "通信が切断されました", -40f, 40, amber, 60f);
            Label(box, "Body", "あなたのキャラクターは NPC が引き継ぎました。この試合には復帰できません。獲得ポイントは 0pt です。", -130f, 24, null, 100f);
            Btn(box, "Home", "ホームへ", new Vector2(0f, 40f), GoHome);

            result = Panel("Result", new Vector2(1400f, 900f), true, out box);
            resultTitle = Label(box, "Title", "", -30f, 36, amber, 56f);
            resultTable = UiBuild.Place(UiBuild.Rect("Table", box), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(1320f, 530f));
            resultReward = Label(box, "Reward", "", -630f, 26, amber, 50f);
            rewardRetry = UiBuild.Button(box, "RewardRetry", "ポイントの保存を再試行", new Vector2(0.5f, 0f), new Vector2(0f, 136f), new Vector2(420f, 60f), RetryReward, 24);
            rewardRetry.gameObject.SetActive(false);
            Btn(box, "Again", "もう一度対戦", new Vector2(-190f, 40f), Rematch);
            Btn(box, "ResultHome", "ホームへ戻る", new Vector2(190f, 40f), GoHome);

            cancelled = Panel("Cancelled", new Vector2(900f, 320f), true, out box);
            cancelledText = Label(box, "Body", "", -80f, 28, null, 80f);
            Btn(box, "Close", "閉じる", new Vector2(0f, 40f), () => Destroy(gameObject));

            leaveConfirm = Panel("LeaveConfirm", new Vector2(980f, 380f), true, out box);
            Label(box, "Title", "試合から退出しますか？", -40f, 36, amber, 56f);
            Label(box, "Body", LeaveMessage, -130f, 26, null, 90f);
            Btn(box, "LeaveYes", "退出する", new Vector2(-180f, 40f), ConfirmLeave);
            Btn(box, "LeaveNo", "戻る", new Vector2(180f, 40f), () => leaveConfirm.SetActive(false));
        }
    }
}
