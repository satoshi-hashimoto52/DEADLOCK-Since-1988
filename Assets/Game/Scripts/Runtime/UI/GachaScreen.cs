using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Deadlock.Combat;
using Deadlock.Gacha;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>Opening effect of one result (D-058): standard for COMMON to RARE, emphasised for EPIC, its own for LEGENDARY.</summary>
    public enum GachaEffect
    {
        Standard,
        Emphasis,
        Legendary,
    }

    /// <summary>Lengths and looks of the opening. Provisional, adjustable (not approved, Q-030).</summary>
    public static class GachaPresentation
    {
        public static float StandardSeconds = 0.35f;
        public static float EmphasisSeconds = 0.9f;
        public static float LegendarySeconds = 1.6f;

        public static GachaEffect EffectOf(WeaponRarity r) =>
            r == WeaponRarity.Legendary ? GachaEffect.Legendary : r == WeaponRarity.Epic ? GachaEffect.Emphasis : GachaEffect.Standard;

        public static float SecondsOf(GachaEffect e) =>
            e == GachaEffect.Legendary ? LegendarySeconds : e == GachaEffect.Emphasis ? EmphasisSeconds : StandardSeconds;

        public static Color ColorOf(WeaponRarity r) => r switch
        {
            WeaponRarity.Common => new Color(0.72f, 0.72f, 0.72f),
            WeaponRarity.Uncommon => new Color(0.45f, 0.85f, 0.45f),
            WeaponRarity.Rare => new Color(0.4f, 0.65f, 1f),
            WeaponRarity.Epic => new Color(0.78f, 0.45f, 1f),
            WeaponRarity.Legendary => new Color(1f, 0.68f, 0.2f),
            _ => Color.white,
        };
    }

    /// <summary>
    /// The gacha screen (Stage 34; opened from the home's Crate): the gacha (single 100 pt / 11-draw 1,000 pt with the
    /// confirmation of current points, cost and points after; the opening one by one with skip to the list; the results
    /// with "もう一度引く" and "閉じる"), the exchange shop and the history of the last 100 draws. Built at runtime on its
    /// own canvas, inside the Safe Area. Every draw and exchange is decided and saved by <see cref="GachaService"/> before
    /// anything is shown, so skipping or closing never changes a result; a draw saved but not shown (the app was closed)
    /// is shown when the screen opens again. Images and sounds: none (only the existing font and plain shapes).
    /// Stage 37E: the figures and actions go through an <see cref="IGachaSource"/> — the local data as before, or, in a
    /// development build only, the account and economy service (<see cref="SourceProvider"/>, set by Deadlock.Dev), whose
    /// confirmed figures alone are shown then; an action waiting for the service shows "通信中" and cannot be repeated.
    /// </summary>
    public sealed class GachaScreen : MonoBehaviour
    {
        public enum Page
        {
            Gacha,
            Shop,
            History,
        }

        public const string RootName = "GachaScreen";
        static readonly Color WindowColor = new Color(0.07f, 0.065f, 0.06f, 0.97f);
        static readonly Color Cream = new Color(0.94f, 0.88f, 0.74f, 1f);
        static readonly Color Amber = new Color(1f, 0.74f, 0.36f, 1f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

        /// <summary>Development builds add their tools (points, test items, event, export / import) to this area.</summary>
        public static event Action<GachaScreen, RectTransform> DevToolsBuilt;

        GachaService service;
        IGachaSource source;
        RectTransform window, pageGacha, pageShop, pageHistory, shopContent, historyContent, devArea;
        RectTransform confirm, opening, results, resultGrid;
        Text wallet, status, rates, message, confirmTitle, confirmBody, openCounter, openRarity, openName, openDetail, resultSummary;
        Image openCard, openFlash;
        Button drawSingle, drawTen, confirmYes;
        Action pendingYes;
        HistoryRecord shown;
        int openIndex;
        float openStarted;
        GachaKind lastKind;
        bool busy;

        public Page CurrentPage { get; private set; }
        public bool IsConfirmOpen => confirm.gameObject.activeSelf;
        public bool IsOpening => opening.gameObject.activeSelf;
        public bool IsResultsOpen => results.gameObject.activeSelf;
        public int OpeningIndex => openIndex;
        public HistoryRecord ShownRecord => shown;
        public GachaEffect CurrentEffect { get; private set; }
        public string Message => message.text;
        public string ConfirmBody => confirmBody.text;
        public bool ConfirmEnabled => confirmYes.interactable;
        public RectTransform Window => window;
        public GachaService Service => service;
        public IGachaSource Source => source;
        /// <summary>The last action's answer (null while waiting for the service).</summary>
        public GachaResult LastResult { get; private set; }

        /// <summary>
        /// Development only (Deadlock.Dev): the source to use instead of the local data — the economy service. Null in a
        /// release build (nothing sets it), so the screen shows the local data.
        /// </summary>
        public static Func<IGachaSource> SourceProvider;

        // ------------------------------------------------------------------------------------------- open / close

        /// <summary>Opens the screen (made on first use in the active scene) on a page.</summary>
        public static GachaScreen Open(Page page)
        {
            var screen = FindAnyObjectByType<GachaScreen>(FindObjectsInactive.Include);
            if (screen == null)
            {
                var go = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 700; // above the home and its sub-screens
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;
                screen = go.AddComponent<GachaScreen>();
                screen.Build();
            }
            screen.gameObject.SetActive(true);
            screen.service = GachaAccount.Service;
            screen.source = SourceProvider?.Invoke() ?? new LocalGachaSource(screen.service);
            screen.ShowPage(page);
            if (screen.source is LocalGachaSource)
                screen.ShowUnrevealed();
            else
            {
                // The service's figures (and an action of before whose answer was lost) are read first.
                var opened = screen.source;
                screen.message.text = "サーバーに問い合わせています…";
                opened.Refresh(() =>
                {
                    if (screen.source != opened)
                        return;
                    screen.message.text = opened.Error ?? "";
                    screen.Refresh();
                    screen.ShowUnrevealed();
                });
            }
            return screen;
        }

        void ShowUnrevealed()
        {
            var pending = source.Unrevealed();
            if (pending == null || IsOpening)
                return;
            lastKind = pending.kind == "ten" ? GachaKind.Ten : GachaKind.Single;
            StartOpening(pending);
            message.text = "前回確定した結果（未表示）を表示します";
        }

        public void Close()
        {
            if (IsOpening && shown != null)
                source.MarkRevealed(shown.requestId);
            confirm.gameObject.SetActive(false);
            opening.gameObject.SetActive(false);
            results.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }

        public void ShowPage(Page page)
        {
            CurrentPage = page;
            pageGacha.gameObject.SetActive(page == Page.Gacha);
            pageShop.gameObject.SetActive(page == Page.Shop);
            pageHistory.gameObject.SetActive(page == Page.History);
            message.text = "";
            Refresh();
        }

        public void ShowMessage(string text) => message.text = text;

        // ------------------------------------------------------------------------------------------- refresh

        static string Pt(int v) => v.ToString("N0", CultureInfo.InvariantCulture);

        public void Refresh()
        {
            if (source == null)
                return;
            wallet.text = source.Ready ? $"ポイント {Pt(source.Points)} pt　　交換メダル {Pt(source.Medals)} 枚" : "ポイント ---　　交換メダル ---";
            var ev = source.ActiveEvent;
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(source.Label))
                sb.Append(source.Label).Append('\n');
            sb.Append(ev != null ? $"LEGENDARY イベント開催中（{ev.eventId}{(ev.rerun ? "・復刻" : "")}）" : "通常期間（LEGENDARY は排出されません）");
            if (source.Error != null)
                sb.Append('\n').Append(source.Error);
            else if (!source.PoolOpen)
                sb.Append("\nガチャの対象が揃っていないため、引けません（ポイントは消費しません）：").Append(string.Join("／", source.PoolProblems));
            if (source.UsesTestItems)
                sb.Append("\n開発用：テスト用アイテムで抽選しています（正式な排出対象ではありません）");
            status.text = sb.ToString();
            bool usable = source.Ready && source.PoolOpen && source.Error == null;
            status.color = usable ? Dim : new Color(1f, 0.55f, 0.4f);

            var r = new StringBuilder("レアリティ　　通常枠　　　11連の確定枠（RARE 以上）\n");
            foreach (var rarity in GachaRates.Rarities)
                r.Append($"{ItemCatalog.RarityLabel(rarity),-10}　{GachaRates.RarityChance(rarity, false, ev != null).Percent,8}　　{GachaRates.RarityChance(rarity, true, ev != null).Percent,8}\n");
            r.Append("カテゴリ：武器 20%・スキン 50%・スキル 30%（対象の無いカテゴリは残りで按分）。同じレアリティ・カテゴリ内は均等。天井なし。");
            rates.text = r.ToString();

            bool canDraw = usable && !busy && !source.Busy;
            drawSingle.interactable = canDraw;
            drawTen.interactable = canDraw;
            if (pageShop.gameObject.activeSelf)
                BuildShop();
            if (pageHistory.gameObject.activeSelf)
                BuildHistory();
        }

        // ------------------------------------------------------------------------------------------- confirm / draw

        /// <summary>The confirmation before a draw (current points, cost, points after). Every draw asks, also "again".</summary>
        public void AskDraw(GachaKind kind)
        {
            int cost = GachaService.Cost(kind);
            int now = source.Points;
            bool enough = now >= cost;
            string name = kind == GachaKind.Single ? "単発" : "11連";
            string body = $"現在のポイント　{Pt(now)} pt\n消費ポイント　　{Pt(cost)} pt\n実行後の残高　　{(enough ? Pt(now - cost) + " pt" : "不足（" + Pt(cost - now) + " pt 足りません）")}";
            string requestId = GachaService.NewRequestId(); // one per confirmation: a second tap cannot draw twice
            ShowConfirm($"{name}（{Pt(cost)}pt）を引きますか？", body, "引く", enough && source.Ready && source.PoolOpen && source.Error == null, () => Execute(kind, requestId));
        }

        public void ShowConfirm(string title, string body, string yesLabel, bool yesEnabled, Action onYes)
        {
            confirmTitle.text = title;
            confirmBody.text = body;
            confirmYes.GetComponentInChildren<Text>().text = yesLabel;
            confirmYes.interactable = yesEnabled;
            pendingYes = onYes;
            confirm.gameObject.SetActive(true);
            confirm.SetAsLastSibling();
        }

        public void PressConfirmYes()
        {
            if (!confirmYes.interactable)
                return;
            pendingYes?.Invoke();
        }

        public void PressConfirmNo()
        {
            pendingYes = null;
            confirm.gameObject.SetActive(false);
        }

        /// <summary>
        /// Draws with this request id. Called again with the same id (a double tap) it changes nothing. Returns the
        /// result, or null while the service has not answered yet (the result then comes in <see cref="LastResult"/>).
        /// </summary>
        public GachaResult Execute(GachaKind kind, string requestId)
        {
            if (busy || source.Busy)
                return new GachaResult { Outcome = GachaOutcome.AlreadyApplied, Message = "処理中です" };
            busy = true;
            LastResult = null;
            confirm.gameObject.SetActive(false);
            pendingYes = null;
            lastKind = kind;
            GachaResult answered = null;
            try
            {
                source.Draw(kind, requestId, result =>
                {
                    busy = false;
                    answered = LastResult = result;
                    // A draw the service had already confirmed (a resend of the same request) is shown as well.
                    if (result.Outcome == GachaOutcome.Done || result.Ok && result.Record != null && !result.Record.revealed && !(source is LocalGachaSource))
                        StartOpening(result.Record);
                    else
                        message.text = result.Message;
                    Refresh();
                });
            }
            catch
            {
                busy = false;
                throw;
            }
            if (answered == null)
            {
                message.text = "通信中です…（結果はサーバーが確定します）";
                Refresh();
            }
            return answered;
        }

        // ------------------------------------------------------------------------------------------- opening

        void StartOpening(HistoryRecord record)
        {
            shown = record;
            openIndex = 0;
            results.gameObject.SetActive(false);
            opening.gameObject.SetActive(true);
            opening.SetAsLastSibling();
            ShowCard();
        }

        void ShowCard()
        {
            var r = shown.results[openIndex];
            openStarted = Time.unscaledTime;
            CurrentEffect = GachaPresentation.EffectOf(r.Rarity);
            Audio.GameAudio.Ui(CurrentEffect == GachaEffect.Legendary ? Audio.SoundCatalog.GachaLegendary
                : CurrentEffect == GachaEffect.Emphasis ? Audio.SoundCatalog.GachaEmphasis : Audio.SoundCatalog.GachaStandard);
            openCounter.text = $"{openIndex + 1} / {shown.results.Count}" + (r.guaranteed ? "　（RARE 以上確定枠）" : "");
            openRarity.text = ItemCatalog.RarityLabel(r.Rarity);
            openRarity.color = GachaPresentation.ColorOf(r.Rarity);
            openName.text = r.displayName;
            openDetail.text = $"{ItemCatalog.CategoryLabel(r.Category)}　" + (r.firstTime ? "初回入手" : $"重複：交換メダル +{r.medals} 枚");
            openCard.color = Color.Lerp(new Color(0.12f, 0.11f, 0.1f), GachaPresentation.ColorOf(r.Rarity), 0.25f);
        }

        void Update()
        {
            source?.Pump();
            if (!IsOpening || shown == null)
                return;
            float t = Mathf.Clamp01((Time.unscaledTime - openStarted) / GachaPresentation.SecondsOf(CurrentEffect));
            var rarity = shown.results[openIndex].Rarity;
            openCard.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0f, 1f, t));
            Color flash = CurrentEffect == GachaEffect.Legendary
                ? Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * 0.6f, 1f), 0.6f, 1f)
                : GachaPresentation.ColorOf(rarity);
            float strength = CurrentEffect == GachaEffect.Standard ? 0.15f : CurrentEffect == GachaEffect.Emphasis ? 0.45f : 0.6f;
            if (CurrentEffect != GachaEffect.Standard)
                strength *= 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 8f);
            flash.a = strength * (1f - t * 0.6f);
            openFlash.color = flash;
        }

        /// <summary>Next result; after the last, the list of all.</summary>
        public void OpenNext()
        {
            if (!IsOpening)
                return;
            if (openIndex + 1 < shown.results.Count)
            {
                openIndex++;
                ShowCard();
            }
            else
                ShowResults();
        }

        /// <summary>Skip: straight to the list of all results (the results do not change).</summary>
        public void SkipOpening()
        {
            if (IsOpening)
                ShowResults();
        }

        void ShowResults()
        {
            opening.gameObject.SetActive(false);
            source.MarkRevealed(shown.requestId);
            foreach (Transform c in resultGrid)
                Destroy(c.gameObject);
            int n = shown.results.Count;
            int perRow = n == 1 ? 1 : 6;
            const float w = 196f, h = 170f, gap = 10f;
            for (int i = 0; i < n; i++)
            {
                var r = shown.results[i];
                int row = i / perRow, col = i % perRow;
                int inRow = Mathf.Min(perRow, n - row * perRow);
                float x = (col - (inRow - 1) * 0.5f) * (w + gap);
                var cell = UiBuild.Place(UiBuild.Rect("Result_" + i, resultGrid), new Vector2(0.5f, 1f), new Vector2(x, -row * (h + gap)), new Vector2(w, h));
                UiBuild.Image(cell, Color.Lerp(new Color(0.12f, 0.11f, 0.1f), GachaPresentation.ColorOf(r.Rarity), 0.3f), false);
                UiBuild.Text(UiBuild.Place(UiBuild.Rect("Rarity", cell), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(w - 10f, 30f)),
                    ItemCatalog.RarityLabel(r.Rarity) + (r.guaranteed ? "（確定枠）" : ""), 20, TextAnchor.MiddleCenter, GachaPresentation.ColorOf(r.Rarity));
                var name = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Name", cell), new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(w - 12f, 76f)),
                    r.displayName, 18, TextAnchor.MiddleCenter, Cream);
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiBuild.Text(UiBuild.Place(UiBuild.Rect("State", cell), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(w - 10f, 30f)),
                    ItemCatalog.CategoryLabel(r.Category) + "・" + (r.firstTime ? "初回" : $"重複 +{r.medals}枚"), 17, TextAnchor.MiddleCenter, Dim);
            }
            int first = 0;
            foreach (var r in shown.results)
                if (r.firstTime)
                    first++;
            resultSummary.text = $"{(shown.kind == "ten" ? "11連" : "単発")}の結果：初回 {first} 件・重複 {n - first} 件・交換メダル +{shown.MedalsGained} 枚";
            results.gameObject.SetActive(true);
            results.SetAsLastSibling();
            Refresh();
        }

        public IReadOnlyList<Transform> ResultCells
        {
            get
            {
                var list = new List<Transform>();
                foreach (Transform c in resultGrid)
                    list.Add(c);
                return list;
            }
        }

        /// <summary>"もう一度引く": the same kind again, as a new draw, after its own confirmation.</summary>
        public void DrawAgain()
        {
            results.gameObject.SetActive(false);
            AskDraw(lastKind);
        }

        public void CloseResults() => results.gameObject.SetActive(false);

        // ------------------------------------------------------------------------------------------- shop / history

        void BuildShop()
        {
            foreach (Transform c in shopContent)
                Destroy(c.gameObject);
            var lineup = source.ShopLineup();
            if (lineup.Count == 0)
                UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("Text", Row(shopContent, "Empty", 40f))), "交換できるアイテムはありません。", 20, TextAnchor.MiddleLeft, Dim);
            foreach (var item in lineup)
            {
                var row = Row(shopContent, "Shop_" + item.itemId, 72f);
                UiBuild.Image(row, new Color(1f, 1f, 1f, 0.05f), false);
                UiBuild.Text(UiBuild.Place(UiBuild.Rect("Name", row), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(560f, 60f)),
                    item.displayName, 22, TextAnchor.MiddleLeft, Cream).rectTransform.pivot = new Vector2(0f, 0.5f);
                UiBuild.Text(UiBuild.Place(UiBuild.Rect("Kind", row), new Vector2(0f, 0.5f), new Vector2(600f, 0f), new Vector2(260f, 60f)),
                    $"{ItemCatalog.RarityLabel(item.rarity)}・{ItemCatalog.CategoryLabel(item.category)}", 20, TextAnchor.MiddleLeft, GachaPresentation.ColorOf(item.rarity))
                    .rectTransform.pivot = new Vector2(0f, 0.5f);
                int price = GachaRates.ExchangePrice(item.rarity);
                bool owned = source.Owns(item.itemId);
                bool enough = source.Medals >= price;
                var it = item;
                var b = UiBuild.Button(row, "Exchange", owned ? "所持済み" : $"{Pt(price)} 枚で交換", new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(260f, 58f),
                    () => AskExchange(it), 20);
                ((RectTransform)b.transform).pivot = new Vector2(1f, 0.5f);
                b.interactable = !owned && enough && source.Ready && source.Error == null && !source.Busy;
            }
        }

        public void AskExchange(ItemDefinition item)
        {
            int price = GachaRates.ExchangePrice(item.rarity);
            int now = source.Medals;
            bool ok = now >= price && !source.Owns(item.itemId) && source.Ready && !source.Busy;
            string requestId = GachaService.NewRequestId();
            ShowConfirm($"{item.displayName} を交換しますか？",
                $"所持メダル　{Pt(now)} 枚\n交換価格　　{Pt(price)} 枚\n交換後　　　{(now >= price ? Pt(now - price) + " 枚" : "不足")}", "交換する", ok, () =>
                {
                    confirm.gameObject.SetActive(false);
                    LastResult = null;
                    bool answered = false;
                    source.Exchange(item.itemId, requestId, result =>
                    {
                        answered = true;
                        LastResult = result;
                        message.text = result.Message;
                        Refresh();
                    });
                    if (!answered)
                        message.text = "通信中です…（結果はサーバーが確定します）";
                });
        }

        void BuildHistory()
        {
            foreach (Transform c in historyContent)
                Destroy(c.gameObject);
            var list = source.History;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var h = list[i];
                var sb = new StringBuilder();
                DateTime.TryParse(h.timeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t);
                sb.Append($"{t.ToLocalTime():yyyy/MM/dd HH:mm}　{(h.kind == "ten" ? "11連" : "単発")}　-{Pt(h.cost)}pt　交換メダル +{h.MedalsGained} 枚");
                if (!string.IsNullOrEmpty(h.eventId))
                    sb.Append($"　（{h.eventId}）");
                foreach (var r in h.results)
                    sb.Append($"\n　{r.slot + 1}. {ItemCatalog.RarityLabel(r.Rarity)}・{ItemCatalog.CategoryLabel(r.Category)}　{r.displayName}　{(r.firstTime ? "初回" : $"重複 +{r.medals}枚")}{(r.guaranteed ? "　確定枠" : "")}");
                int lines = 1 + h.results.Count;
                var row = Row(historyContent, "History_" + i, 10f + lines * 23f);
                var text = UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("Text", row)), sb.ToString(), 18, TextAnchor.UpperLeft, Cream);
                text.rectTransform.offsetMin = new Vector2(12f, 0f);
            }
            if (list.Count == 0)
                UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("Text", Row(historyContent, "Empty", 40f))), "履歴はまだありません。", 20, TextAnchor.MiddleLeft, Dim);
        }

        public int HistoryRows => historyContent.childCount;
        public int ShopRows => shopContent.childCount;
        public Button ShopButton(string itemId) => shopContent.Find("Shop_" + itemId)?.Find("Exchange")?.GetComponent<Button>();

        static RectTransform Row(RectTransform content, string name, float height)
        {
            var row = UiBuild.Rect(name, content);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            return row;
        }

        // ------------------------------------------------------------------------------------------- build

        Button Btn(RectTransform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick, int font = 24)
        {
            var b = UiBuild.Button(parent, name, label, anchor, pos, size, onClick, font);
            ((Image)b.targetGraphic).color = new Color(0.16f, 0.14f, 0.12f, 0.98f);
            b.gameObject.AddComponent<Outline>().effectColor = new Color(0.78f, 0.6f, 0.3f, 1f);
            b.GetComponentInChildren<Text>().color = Cream;
            return b;
        }

        static RectTransform Scroll(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var view = UiBuild.Place(UiBuild.Rect(name, parent), new Vector2(0.5f, 1f), pos, size);
            UiBuild.Image(view, new Color(0f, 0f, 0f, 0.25f), true);
            view.gameObject.AddComponent<RectMask2D>();
            var content = UiBuild.Rect("Content", view);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        void Build()
        {
            var root = (RectTransform)transform;
            var dim = UiBuild.Stretch(UiBuild.Rect("Backdrop", root));
            UiBuild.Image(dim, new Color(0f, 0f, 0f, 0.7f), true);
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", root));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            window = UiBuild.Place(UiBuild.Rect("Window", safe), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1360f, 920f));
            UiBuild.Image(window, WindowColor, true);
            window.gameObject.AddComponent<Outline>().effectColor = new Color(0.78f, 0.6f, 0.3f, 0.8f);
            Vector2 top = new Vector2(0.5f, 1f);

            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), top, new Vector2(-300f, -14f), new Vector2(700f, 52f)), "Crate", 38, TextAnchor.MiddleLeft, Cream)
                .fontStyle = FontStyle.Bold;
            wallet = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Wallet", window), top, new Vector2(120f, -18f), new Vector2(700f, 44f)), "", 26, TextAnchor.MiddleRight, Amber);
            var close = Btn(window, "GachaClose", "閉じる", new Vector2(1f, 1f), new Vector2(-14f, -12f), new Vector2(150f, 56f), Close);
            ((RectTransform)close.transform).pivot = new Vector2(1f, 1f);
            Btn(window, "TabGacha", "ガチャ", top, new Vector2(-420f, -80f), new Vector2(260f, 56f), () => ShowPage(Page.Gacha));
            Btn(window, "TabShop", "交換ショップ", top, new Vector2(-150f, -80f), new Vector2(260f, 56f), () => ShowPage(Page.Shop));
            Btn(window, "TabHistory", "抽選履歴", top, new Vector2(120f, -80f), new Vector2(260f, 56f), () => ShowPage(Page.History));
            message = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Message", window), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(1300f, 36f)), "", 22,
                TextAnchor.MiddleCenter, Amber);

            // Gacha page.
            pageGacha = UiBuild.Place(UiBuild.Rect("PageGacha", window), top, new Vector2(0f, -150f), new Vector2(1320f, 710f));
            status = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Status", pageGacha), top, Vector2.zero, new Vector2(1300f, 84f)), "", 21, TextAnchor.UpperLeft);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            rates = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Rates", pageGacha), top, new Vector2(0f, -92f), new Vector2(1300f, 210f)), "", 21, TextAnchor.UpperLeft, Cream);
            rates.horizontalOverflow = HorizontalWrapMode.Wrap;
            drawSingle = Btn(pageGacha, "DrawSingle", "単発　100pt", top, new Vector2(-230f, -330f), new Vector2(400f, 110f), () => AskDraw(GachaKind.Single), 34);
            drawTen = Btn(pageGacha, "DrawTen", "11連　1,000pt", top, new Vector2(230f, -330f), new Vector2(400f, 110f), () => AskDraw(GachaKind.Ten), 34);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Note", pageGacha), top, new Vector2(0f, -452f), new Vector2(1300f, 60f)),
                "11連は通常枠10回＋RARE 以上確定枠1回。重複したアイテムは交換メダルになります。結果は引いた時点で確定・保存され、演出を飛ばしても変わりません。",
                19, TextAnchor.UpperCenter, Dim).horizontalOverflow = HorizontalWrapMode.Wrap;
            devArea = UiBuild.Place(UiBuild.Rect("DevArea", pageGacha), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(1300f, 150f));

            // Shop and history pages.
            pageShop = UiBuild.Place(UiBuild.Rect("PageShop", window), top, new Vector2(0f, -150f), new Vector2(1320f, 710f));
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Note", pageShop), top, Vector2.zero, new Vector2(1300f, 34f)),
                "交換メダルでアイテムと交換します（COMMON 30・UNCOMMON 100・RARE 350・EPIC 1,000・LEGENDARY 3,500 枚。LEGENDARY は対象イベント中だけ）。所持済みは交換できません。",
                18, TextAnchor.MiddleLeft, Dim).horizontalOverflow = HorizontalWrapMode.Wrap;
            shopContent = Scroll(pageShop, "List", new Vector2(0f, -44f), new Vector2(1300f, 610f));
            pageHistory = UiBuild.Place(UiBuild.Rect("PageHistory", window), top, new Vector2(0f, -150f), new Vector2(1320f, 710f));
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Note", pageHistory), top, Vector2.zero, new Vector2(1300f, 34f)),
                $"直近 {GachaSave.HistoryLimit} 回分（購入1回＝1件。古い履歴が消えても、所持品・メダル・ポイントは変わりません）", 18, TextAnchor.MiddleLeft, Dim);
            historyContent = Scroll(pageHistory, "List", new Vector2(0f, -44f), new Vector2(1300f, 610f));

            // Confirmation.
            confirm = UiBuild.Stretch(UiBuild.Rect("Confirm", window));
            UiBuild.Image(confirm, new Color(0f, 0f, 0f, 0.75f), true);
            var box = UiBuild.Place(UiBuild.Rect("Box", confirm), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 420f));
            UiBuild.Image(box, new Color(0.12f, 0.11f, 0.1f, 1f), true);
            confirmTitle = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", box), top, new Vector2(0f, -20f), new Vector2(780f, 50f)), "", 28, TextAnchor.MiddleCenter, Cream);
            confirmBody = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Body", box), top, new Vector2(0f, -90f), new Vector2(560f, 170f)), "", 26, TextAnchor.UpperLeft, Cream);
            confirmYes = Btn(box, "ConfirmYes", "引く", new Vector2(0.5f, 0f), new Vector2(-150f, 28f), new Vector2(260f, 76f), PressConfirmYes, 28);
            Btn(box, "ConfirmNo", "やめる", new Vector2(0.5f, 0f), new Vector2(150f, 28f), new Vector2(260f, 76f), PressConfirmNo, 28);
            confirm.gameObject.SetActive(false);

            // Opening.
            opening = UiBuild.Stretch(UiBuild.Rect("Opening", window));
            UiBuild.Image(opening, new Color(0f, 0f, 0f, 0.9f), true);
            openFlash = UiBuild.Image(UiBuild.Stretch(UiBuild.Rect("Effect", opening)), new Color(1f, 1f, 1f, 0f), false);
            var card = UiBuild.Place(UiBuild.Rect("Card", opening), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(560f, 560f));
            openCard = UiBuild.Image(card, Color.gray, false);
            openRarity = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Rarity", card), top, new Vector2(0f, -40f), new Vector2(520f, 80f)), "", 56, TextAnchor.MiddleCenter);
            openRarity.fontStyle = FontStyle.Bold;
            openName = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Name", card), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 160f)), "", 34, TextAnchor.MiddleCenter, Cream);
            openName.horizontalOverflow = HorizontalWrapMode.Wrap;
            openDetail = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Detail", card), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(520f, 60f)), "", 26, TextAnchor.MiddleCenter, Amber);
            openCounter = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Counter", opening), top, new Vector2(0f, -20f), new Vector2(900f, 44f)), "", 26, TextAnchor.MiddleCenter, Cream);
            Btn(opening, "OpenNext", "次へ", new Vector2(0.5f, 0f), new Vector2(-150f, 40f), new Vector2(260f, 76f), OpenNext, 28);
            Btn(opening, "OpenSkip", "スキップ", new Vector2(0.5f, 0f), new Vector2(150f, 40f), new Vector2(260f, 76f), SkipOpening, 28);
            opening.gameObject.SetActive(false);

            // Results.
            results = UiBuild.Stretch(UiBuild.Rect("Results", window));
            UiBuild.Image(results, new Color(0.05f, 0.045f, 0.04f, 0.98f), true);
            resultSummary = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Summary", results), top, new Vector2(0f, -24f), new Vector2(1300f, 44f)), "", 26, TextAnchor.MiddleCenter, Cream);
            resultGrid = UiBuild.Place(UiBuild.Rect("Grid", results), top, new Vector2(0f, -110f), new Vector2(1300f, 400f));
            Btn(results, "Again", "もう一度引く", new Vector2(0.5f, 0f), new Vector2(-170f, 40f), new Vector2(300f, 80f), DrawAgain, 28);
            Btn(results, "ResultsClose", "閉じる", new Vector2(0.5f, 0f), new Vector2(170f, 40f), new Vector2(300f, 80f), CloseResults, 28);
            results.gameObject.SetActive(false);

            DevToolsBuilt?.Invoke(this, devArea);
        }
    }
}
