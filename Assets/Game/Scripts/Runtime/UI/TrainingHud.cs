using System.Collections.Generic;
using Deadlock.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadlock.UI
{
    /// <summary>
    /// Training round display: defeated zombies and time, and the cleared / failed panel with retry.
    /// With an escape point (ruins test field, test-only provisional rule): the status line has no total, a
    /// "脱出する" button shows while the player stands on the point (F on desktop), and the result panel says
    /// 生還 / 死亡 with the time and the defeated count, with Retry and the menu. The button is placed clear of the HUD
    /// text, the controls and the floor map (<see cref="MiniMapView.AvoidCentre"/>), worked out again only when the
    /// screen, the touch layout or the map's place changes.
    /// </summary>
    public sealed class TrainingHud : MonoBehaviour
    {
        [SerializeField] TrainingSession session;
        [Tooltip("Optional (ruins test field): the floor map, which the escape button keeps clear of with the HUD and controls.")]
        [SerializeField] MiniMapView miniMap;
        [Tooltip("Optional: the menu, opened from the result panel (escape point only).")]
        [SerializeField] GameMenu menu;

        /// <summary>Escape button size (canvas units) and where it goes when that place is clear (fraction of the Safe Area).</summary>
        public static readonly Vector2 EscapeSize = new Vector2(340f, 92f);
        public static readonly Vector2 EscapeWanted = new Vector2(0.5f, 0.3f);

        UnityEngine.UI.Text status;
        RectTransform safe;
        GameObject resultPanel;
        UnityEngine.UI.Text resultTitle;
        UnityEngine.UI.Text resultDetail;
        UnityEngine.UI.Button escapeButton;
        Vector2 escapeFor;
        int escapeForMap = -1;
        readonly List<Rect> escapeBoxes = new List<Rect>();
        readonly List<Vector3> escapeCircles = new List<Vector3>();

        bool HasEscape => session.Extraction != null;
        public GameObject ResultPanel => resultPanel;
        public string ResultTitle => resultTitle.text;
        public string ResultDetail => resultDetail.text;
        /// <summary>The status line (top centre); the floor map keeps clear of it.</summary>
        public UnityEngine.UI.Text StatusText => status;
        /// <summary>The widest this round's status line gets (all defeated, up to 99:59), for layout checks.</summary>
        public string WidestStatus => HasEscape
            ? $"遺跡（試作）　脱出地点へ　討伐 {session.Total}　99:59"
            : $"トレーニング（試作）　討伐 {session.Total} / {session.Total}　99:59";
        public UnityEngine.UI.Button EscapeButton => escapeButton;
        /// <summary>Where the escape button is drawn (Safe Area canvas units, origin bottom-left), from its corners on screen.</summary>
        public Rect EscapeRect
        {
            get
            {
                var corners = new Vector3[4];
                ((RectTransform)escapeButton.transform).GetWorldCorners(corners);
                Vector2 a = safe.InverseTransformPoint(corners[0]), b = safe.InverseTransformPoint(corners[2]);
                Vector2 offset = safe.rect.size * safe.pivot;
                return Rect.MinMaxRect(a.x + offset.x, a.y + offset.y, b.x + offset.x, b.y + offset.y);
            }
        }

        void Awake()
        {
            var layer = UiBuild.Stretch(UiBuild.Rect("TrainingLayer", transform));
            safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", layer));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            status = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Status", safe), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(600f, 40f)),
                "", 28, TextAnchor.UpperCenter);
            if (HasEscape)
            {
                escapeButton = UiBuild.Button(safe, "EscapeButton", "脱出する", Vector2.zero, Vector2.zero, EscapeSize, () => session.RequestEscape(), 36);
                escapeButton.GetComponent<UnityEngine.UI.Image>().color = new Color(0.1f, 0.45f, 0.2f, 0.92f);
                escapeButton.gameObject.SetActive(false);
            }

            resultPanel = UiBuild.Stretch(UiBuild.Rect("ResultPanel", layer)).gameObject;
            UiBuild.Image((RectTransform)resultPanel.transform, new Color(0f, 0f, 0f, 0.55f), true);
            var window = UiBuild.Place(UiBuild.Rect("Window", resultPanel.transform), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, HasEscape ? 460f : 420f));
            UiBuild.Image(window, new Color(0.08f, 0.09f, 0.12f, 0.95f), true);
            resultTitle = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(560f, 70f)),
                "", 56, TextAnchor.MiddleCenter);
            resultDetail = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Detail", window), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(560f, HasEscape ? 90f : 40f)),
                "", 28, HasEscape ? TextAnchor.UpperCenter : TextAnchor.MiddleCenter);
            UiBuild.Button(window, "RetryButton", "再挑戦", new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(420f, 76f), session.Retry);
            if (HasEscape)
                UiBuild.Button(window, "MenuButton", "メニュー", new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(420f, 76f), OpenMenu);
            else
                UiBuild.Button(window, "FiringRangeButton", "射撃場へ", new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(420f, 76f),
                    () => GameScenes.Load(GameScenes.FiringRange));
            resultPanel.SetActive(false);
        }

        void OnEnable()
        {
            session.Finished += OnFinished;
            if (menu != null)
                menu.Closed += OnMenuClosed;
        }

        void OnDisable()
        {
            session.Finished -= OnFinished;
            if (menu != null)
                menu.Closed -= OnMenuClosed;
        }

        // The menu draws under this layer: the result panel steps aside while it is open and comes back after.
        void OpenMenu()
        {
            if (menu == null)
                return;
            resultPanel.SetActive(false);
            menu.Open();
        }

        void OnMenuClosed()
        {
            if (session.Result != TrainingResult.Playing)
                resultPanel.SetActive(true);
        }

        void LateUpdate()
        {
            int seconds = Mathf.FloorToInt(session.ElapsedTime);
            status.text = HasEscape
                ? $"遺跡（試作）　脱出地点へ　討伐 {session.Defeated}　{seconds / 60}:{seconds % 60:00}"
                : $"トレーニング（試作）　討伐 {session.Defeated} / {session.Total}　{seconds / 60}:{seconds % 60:00}";
            if (!HasEscape)
                return;
            bool can = session.CanEscape;
            if (can && (!escapeButton.gameObject.activeSelf || safe.rect.size != escapeFor || (miniMap != null && miniMap.PlacementVersion != escapeForMap)))
                PlaceEscape();
            escapeButton.gameObject.SetActive(can);
            if (can && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
                session.RequestEscape();
        }

        /// <summary>The wanted place if clear, otherwise the nearest place clear of the HUD, the controls and the map.</summary>
        void PlaceEscape()
        {
            Vector2 size = safe.rect.size;
            escapeFor = size;
            escapeBoxes.Clear();
            escapeCircles.Clear();
            if (miniMap != null)
            {
                escapeForMap = miniMap.PlacementVersion;
                escapeBoxes.AddRange(miniMap.AvoidedBoxes);
                escapeCircles.AddRange(miniMap.AvoidedCircles);
                if (miniMap.IsShown)
                    escapeBoxes.Add(miniMap.ShownRect);
            }
            escapeBoxes.Add(new Rect(size * 0.5f - new Vector2(60f, 60f), new Vector2(120f, 120f))); // the crosshair
            Vector2 centre = MiniMapView.AvoidCentre(size, EscapeSize, Vector2.Scale(EscapeWanted, size), escapeBoxes, escapeCircles, MiniMapView.AvoidGap, out bool found);
            if (!found)
                Debug.LogWarning($"[DEADLOCK][escape] no clear place for the escape button in a {size.x:0}x{size.y:0} Safe Area: shown at the wanted place");
            var rt = escapeButton.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f); // the centre is placed (UiBuild pivots on the anchor)
            rt.anchoredPosition = centre;
        }

        void OnFinished(TrainingResult result)
        {
            if (escapeButton != null)
                escapeButton.gameObject.SetActive(false);
            resultPanel.SetActive(true);
            int seconds = Mathf.FloorToInt(session.ElapsedTime);
            string time = $"{seconds / 60}:{seconds % 60:00}";
            if (HasEscape)
            {
                bool survived = result == TrainingResult.Escaped;
                resultTitle.text = survived ? "生還" : "死亡";
                resultTitle.color = survived ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.4f, 0.35f);
                resultDetail.text = $"経過時間 {time}\n討伐数 {session.Defeated}";
                return;
            }
            resultTitle.text = result == TrainingResult.Cleared ? "クリア" : "失敗";
            resultTitle.color = result == TrainingResult.Cleared ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.4f, 0.35f);
            resultDetail.text = result == TrainingResult.Cleared
                ? $"ゾンビを全滅させました（討伐 {session.Defeated} / {session.Total}、{time}）"
                : $"倒されました（討伐 {session.Defeated} / {session.Total}、{time}）";
        }
    }
}
