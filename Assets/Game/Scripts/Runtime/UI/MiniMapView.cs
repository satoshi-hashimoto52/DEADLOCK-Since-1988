using System.Collections.Generic;
using Deadlock.Input;
using Deadlock.Map;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>
    /// Small floor map on the HUD, a 3:2 rectangle (wider than tall, like the screen; was 4:3, widened at the same height
    /// and scale so more is seen to the sides). Heading-up: the map turns around
    /// the player's arrow in the middle, at the same scale across and down (the ground is never stretched).
    /// Shows the floor the player is on (switched with hysteresis on the stairs, <see cref="FloorMapData.FloorFor"/>):
    /// walkable floor, walls, stairs (step lines, and a boundary line at the end that leads to the other floor),
    /// the atrium drop (hatched) and, outside the building, an opaque near-black background; the frame has a light
    /// blue edge. The colours keep clear of the enemy markers' red and amber.
    /// Enemies the <see cref="EnemyDetector"/> currently reports are shown at their detected positions:
    /// an enemy in sight within the map's range is a red isosceles triangle whose sharp tip points where it faces now
    /// (so it reads whom / where it is after); an enemy lost from sight is a soft red glow around where it was last
    /// seen (no kept direction: it read as "reversed" on the device, 2026-10-23); a heard enemy on this floor is an
    /// orange footprint at the step, toes where it faced. On another floor (seen only) an up / down chevron beside it,
    /// so it never looks like an enemy on this floor. Markers fade out before they go.
    /// An enemy beyond the map's range that has a clear sight line to the player (it is watching you) turns a part of
    /// the frame red toward where it is, whether or not the player sees it.
    /// An enemy on another floor that is not seen never shows at a position (not even straight above / below): its
    /// footsteps (<see cref="FootstepCue"/>) show for a moment as an orange arrow on the map's edge in a rough
    /// direction (8 ways), pointing where the enemy faced (8 ways), with an up / down chevron, or on a dark plate
    /// below the floor label when straight above / below; no
    /// position, no distance, never following the enemy.
    /// Two kinds of markers are not at the enemy's exact place, and look different so they are not read as one:
    /// - beyond the map's range: a soft glow (red = lost, orange = heard) on the map's edge toward the last detected
    ///   position, with a small up / down chevron beside it for another floor (an enemy in sight there shows only as
    ///   the red frame part while it watches the player);
    /// - another floor, (nearly) straight above / below the player: a box with an up / down chevron in a slot beside
    ///   the player's arrow, instead of a marker hidden under the arrow.
    /// Place and size are part of Settings → Control layout (<see cref="TouchLayoutEditor"/>), saved on the device
    /// (<see cref="MapSettings.SavePlacement"/>) only when the player applies them. Until the player places it, it goes
    /// once to the top-left corner, or along the top edge to the first place clear of the controls
    /// (<see cref="ChoosePosition"/>). Where it is shown is worked out from that place each time the screen size or the
    /// touch layout changes (never every frame): kept inside the Safe Area, and moved the shortest way off the HUD text
    /// (ammo / melee name, posture line, status line, dev frame-time line, HP bar), the menu button and the touch
    /// controls (<see cref="AvoidCentre"/>). The saved place is never rewritten by this, so going back to a screen where
    /// it fits shows it at the saved place again. Its size is never reduced and no HUD is hidden: if no clear place
    /// exists, it stays at the saved place and <see cref="AvoidanceFailed"/> says so.
    /// The map image comes from the baked <see cref="FloorMapData"/> (no extra camera). "Show minimap" off
    /// (<see cref="MapSettings"/>) hides this display only. Display only: never changes the game.
    /// </summary>
    public sealed class MiniMapView : MonoBehaviour
    {
        [SerializeField] FloorMapData map;
        [SerializeField] EnemyDetector detector;
        [Tooltip("The player's feet (position and floor).")]
        [SerializeField] Transform player;
        [Tooltip("Optional: the HUD, whose Editor help text moves right of the map.")]
        [SerializeField] HudController hud;
        [Tooltip("Optional: the touch layout, whose controls the first automatic placement keeps clear of.")]
        [SerializeField] TouchLayoutApplier touchLayout;
        [Tooltip("Metres shown across 188 canvas units (the scale of the first, square map; unchanged).")]
        [SerializeField, Min(4f)] float metresAcross = 22f;

        /// <summary>Id of the map in the control layout editor.</summary>
        public const string LayoutId = "minimap";
        public const string DisplayName = "ミニマップ";
        /// <summary>Map size at scale 1 (canvas units, reference height 1080): 3:2 (was 224 x 168, 4:3; same height).</summary>
        public static readonly Vector2 BaseSize = new Vector2(252f, 168f);
        /// <summary>Distance of the automatic placement from the Safe Area's top-left corner.</summary>
        public static readonly Vector2 Margin = new Vector2(14f, 14f);
        public const float MinScale = 0.7f;
        public const float MaxScale = 1.6f;
        const float ReferenceSide = 188f;
        const float Border = 3f;
        const float MarkerSize = 20f;
        /// <summary>Facing triangle and footprint size (canvas units): larger than the round marker was, so the direction reads.</summary>
        const float FacingSize = 24f;
        /// <summary>Edge markers sit this far inside the map's edge (canvas units).</summary>
        const float EdgeGap = 12f;
        /// <summary>Another floor's marker closer than this to the centre would hide under the player's arrow.</summary>
        const float SelfRadius = 16f;

        static readonly Color SeenColor = new Color(1f, 0.27f, 0.22f, 1f);
        static readonly Color HeardColor = new Color(1f, 0.78f, 0.25f, 1f);
        /// <summary>Heard enemies and footsteps (orange).</summary>
        static readonly Color HeardGlowColor = new Color(1f, 0.55f, 0.15f, 1f);
        /// <summary>Red part of the frame toward an enemy beyond the range that watches the player (canvas units).</summary>
        const float WatchBarLength = 46f;
        const float WatchBarWidth = 6f;
        /// <summary>The frame part stays this long after the enemy stopped watching (s).</summary>
        const float WatchHold = 0.4f;
        /// <summary>Soft glow size (canvas units): about 4 m across, "around here", not a point.</summary>
        const float GlowSize = 40f;
        const float EdgeGlowSize = 34f;
        static readonly Color FrameColor = new Color(0.45f, 0.78f, 0.95f, 0.95f);
        static readonly Color32 OutsideColor = new Color32(6, 7, 11, 255);

        RectTransform safeRect;
        RectTransform frame;
        RectTransform rotor;
        RawImage mapImage;
        RectTransform markerLayer;
        RectTransform north;
        Text floorLabel;
        GameObject editPreview;
        Texture2D[] floorTextures;
        readonly List<Image> markers = new List<Image>();
        readonly List<Image> badges = new List<Image>();
        readonly List<MapEnemy> markerEnemies = new List<MapEnemy>();
        readonly List<string> markerShapes = new List<string>();
        int shownMarkers;
        readonly List<Image> cueWaves = new List<Image>();
        readonly List<Image> cueChevrons = new List<Image>();
        readonly List<Image> cueBacks = new List<Image>();
        readonly List<string> cueShapes = new List<string>();
        readonly List<Vector2> cueKeys = new List<Vector2>();
        int shownCues;
        float yaw;
        Sprite triangle, facing, footprint, arrow, glow, box, chevron, plate;
        readonly List<Image> watchBars = new List<Image>();
        int shownWatchBars;
        MiniMapPlacement placement;
        bool placed;
        Vector2 placedFor;
        int placedForLayout = -1;
        bool editing;
        Vector2 shownCentre;
        readonly List<Rect> avoidBoxes = new List<Rect>();
        readonly List<Vector3> avoidCircles = new List<Vector3>();
        /// <summary>Space kept between the map and the HUD / controls (canvas units).</summary>
        public const float AvoidGap = 8f;

        public int DisplayedFloor { get; private set; } = -1;
        public bool IsShown => frame != null && frame.gameObject.activeSelf;
        public RectTransform Frame => frame;
        public float UnitsPerMetre => ReferenceSide / metresAcross;
        /// <summary>Rotation of the map (degrees, counter-clockwise on screen) = the player's heading.</summary>
        public float MapRotation => yaw;
        public int ShownMarkers => shownMarkers;
        /// <summary>Footstep cues (another floor) shown now.</summary>
        public int ShownCues => shownCues;
        /// <summary>Red frame parts shown now (enemies beyond the range watching the player).</summary>
        public int ShownWatchBars => shownWatchBars;
        /// <summary>Test / capture access: where frame part i is (frame units from the centre, before the frame's scale).</summary>
        public Vector2 WatchBarPosition(int index) => watchBars[index].rectTransform.anchoredPosition;
        /// <summary>The place / size asked for (saved, or being edited), kept inside the Safe Area.</summary>
        public MiniMapPlacement Placement => placement;
        /// <summary>Where the map is actually shown (Safe Area canvas units, origin bottom-left), after keeping clear of the HUD.</summary>
        public Rect ShownRect => new Rect(shownCentre - BaseSize * placement.scale * 0.5f, BaseSize * placement.scale);
        /// <summary>No clear place was found: shown at the asked place, over some HUD or control.</summary>
        public bool AvoidanceFailed { get; private set; }
        /// <summary>The areas kept clear of in the last placement (Safe Area canvas units): boxes, and circles (x, y, radius).</summary>
        public IReadOnlyList<Rect> AvoidedBoxes => avoidBoxes;
        public IReadOnlyList<Vector3> AvoidedCircles => avoidCircles;
        /// <summary>The player placed the map (saved): the game does not move it.</summary>
        public bool PlacementSaved { get; private set; }
        public Vector2 SafeSize => safeRect.rect.size;
        public bool IsEditing => editing;

        /// <summary>The automatic first place inside a Safe Area of this size (canvas units, origin bottom-left, y up).</summary>
        public static Rect LayoutRect(Vector2 safeSize) => LayoutRect(safeSize, Margin.x);

        static Rect LayoutRect(Vector2 safeSize, float left) => new Rect(left, safeSize.y - Margin.y - BaseSize.y, BaseSize.x, BaseSize.y);

        /// <summary>
        /// Left edge of the first placement (canvas units from the Safe Area's left) for a touch layout: the top-left
        /// corner, or the first place to the right along the top edge that is clear of every control (8 units apart),
        /// the menu button and its margin, the top-centre status line and the top-right ammo / posture text.
        /// None clear: the corner.
        /// </summary>
        public static float ChoosePosition(Vector2 safeSize, TouchLayoutData layout)
        {
            const float gap = 8f;
            var fixedAreas = new[]
            {
                TouchLayout.MenuGuard(safeSize),
                new Rect(safeSize.x * 0.5f - 300f, safeSize.y - 124f, 600f, 124f),              // status line
                new Rect(safeSize.x - 48f - 420f, safeSize.y - 206f, 468f, 206f),              // ammo / posture
            };
            for (float left = Margin.x; left + BaseSize.x + Margin.x <= safeSize.x; left += gap)
            {
                Rect map = LayoutRect(safeSize, left);
                bool clear = true;
                foreach (var area in fixedAreas)
                    clear &= !map.Overlaps(area);
                if (layout != null)
                    foreach (var item in layout.items)
                    {
                        if (!clear || item.id == TouchLayout.MoveZone)
                            continue;
                        float r = TouchLayout.Diameter(item) * 0.5f + gap;
                        Vector2 c = TouchLayout.ClampCircle(item.center, TouchLayout.Diameter(item), safeSize) * safeSize;
                        Vector2 nearest = new Vector2(Mathf.Clamp(c.x, map.xMin, map.xMax), Mathf.Clamp(c.y, map.yMin, map.yMax));
                        clear &= Vector2.Distance(nearest, c) > r;
                    }
                if (clear)
                    return left;
            }
            return Margin.x;
        }

        /// <summary>The automatic first placement as a placement (scale 1).</summary>
        public static MiniMapPlacement AutoPlacement(Vector2 safeSize, TouchLayoutData layout)
        {
            float left = ChoosePosition(safeSize, layout);
            var centre = new Vector2(left + BaseSize.x * 0.5f, safeSize.y - Margin.y - BaseSize.y * 0.5f);
            return new MiniMapPlacement { center = new Vector2(centre.x / safeSize.x, centre.y / safeSize.y), scale = 1f };
        }

        /// <summary>Scale within limits, and the whole map inside the Safe Area.</summary>
        public static MiniMapPlacement Clamp(MiniMapPlacement p, Vector2 safeSize)
        {
            float scale = Mathf.Clamp(p.IsValid ? p.scale : 1f, MinScale, MaxScale);
            Vector2 half = BaseSize * scale * 0.5f;
            Vector2 c = p.center * safeSize;
            c.x = safeSize.x > half.x * 2f ? Mathf.Clamp(c.x, half.x, safeSize.x - half.x) : safeSize.x * 0.5f;
            c.y = safeSize.y > half.y * 2f ? Mathf.Clamp(c.y, half.y, safeSize.y - half.y) : safeSize.y * 0.5f;
            return new MiniMapPlacement { center = new Vector2(c.x / Mathf.Max(1f, safeSize.x), c.y / Mathf.Max(1f, safeSize.y)), scale = scale };
        }

        /// <summary>The map's rectangle for a placement (Safe Area canvas units, origin bottom-left).</summary>
        public static Rect PlacementRect(MiniMapPlacement p, Vector2 safeSize)
        {
            Vector2 size = BaseSize * p.scale;
            Vector2 c = p.center * safeSize;
            return new Rect(c - size * 0.5f, size);
        }

        void Awake()
        {
            BuildSprites();
            var layer = UiBuild.Stretch(UiBuild.Rect("MiniMapLayer", transform));
            safeRect = UiBuild.Stretch(UiBuild.Rect("SafeArea", layer));
            safeRect.gameObject.AddComponent<SafeAreaFitter>();
            // The frame's own image is its light blue edge; the map sits on an opaque "outside" background inside it.
            frame = UiBuild.Place(UiBuild.Rect("MiniMap", safeRect), new Vector2(0f, 0f), Vector2.zero, BaseSize);
            frame.pivot = new Vector2(0.5f, 0.5f);
            UiBuild.Image(frame, FrameColor, false);
            var background = UiBuild.Stretch(UiBuild.Rect("Background", frame));
            background.offsetMin = new Vector2(Border, Border);
            background.offsetMax = new Vector2(-Border, -Border);
            UiBuild.Image(background, OutsideColor, false);
            var viewport = UiBuild.Stretch(UiBuild.Rect("Viewport", background));
            viewport.gameObject.AddComponent<RectMask2D>();
            rotor = UiBuild.Place(UiBuild.Rect("Rotor", viewport), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            mapImage = UiBuild.Rect("FloorImage", rotor).gameObject.AddComponent<RawImage>();
            mapImage.raycastTarget = false;
            var mapRect = mapImage.rectTransform;
            mapRect.anchorMin = mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            mapRect.pivot = Vector2.zero;
            // Markers do not turn with the map: their places are computed in the frame (so triangles stay upright).
            markerLayer = UiBuild.Place(UiBuild.Rect("Markers", viewport), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            // The player: an arrow in the middle, always pointing up (heading-up map). (Not named "Player": that is
            // the player object's name, looked up by name elsewhere.)
            var arrowOutline = UiBuild.Place(UiBuild.Rect("PlayerArrowOutline", viewport), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 28f));
            Graphic(arrowOutline, triangle, Color.black);
            var arrow = UiBuild.Place(UiBuild.Rect("PlayerArrow", viewport), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 20f));
            Graphic(arrow, triangle, Color.white);
            north = UiBuild.Place(UiBuild.Rect("North", viewport), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            UiBuild.Text(north, "N", 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));
            floorLabel = UiBuild.Text(UiBuild.Place(UiBuild.Rect("FloorLabel", frame), new Vector2(0f, 1f), new Vector2(8f, -4f), new Vector2(80f, 30f)),
                "", 24, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.9f));
            floorLabel.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            BuildEditPreview();

            floorTextures = new Texture2D[map != null ? map.floors.Length : 0];
            for (int f = 0; f < floorTextures.Length; f++)
                floorTextures[f] = BuildTexture(f);
            if (map != null)
                mapRect.sizeDelta = new Vector2(map.width, map.height) * map.cellSize * UnitsPerMetre;
        }

        /// <summary>Sample markers and a label, shown only while the control layout is being edited.</summary>
        void BuildEditPreview()
        {
            var root = UiBuild.Stretch(UiBuild.Rect("EditPreview", frame));
            editPreview = root.gameObject;
            var seen = UiBuild.Place(UiBuild.Rect("SampleSeen", root), new Vector2(0.5f, 0.5f), new Vector2(-46f, 26f), new Vector2(FacingSize, FacingSize));
            Graphic(seen, facing, SeenColor);
            seen.localRotation = Quaternion.Euler(0f, 0f, -45f);
            var heard = UiBuild.Place(UiBuild.Rect("SampleHeard", root), new Vector2(0.5f, 0.5f), new Vector2(44f, -24f), new Vector2(FacingSize, FacingSize));
            Graphic(heard, footprint, HeardGlowColor);
            heard.localRotation = Quaternion.Euler(0f, 0f, 60f);
            var edge = UiBuild.Place(UiBuild.Rect("SampleWatch", root), new Vector2(0.5f, 1f), new Vector2(60f, -Border * 0.5f), new Vector2(WatchBarLength, WatchBarWidth));
            UiBuild.Image(edge, SeenColor, false);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Label", root), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(BaseSize.x, 28f)),
                "ミニマップ（プレビュー）", 20, TextAnchor.LowerCenter, new Color(1f, 1f, 1f, 0.9f));
            editPreview.SetActive(false);
        }

        void OnEnable()
        {
            MapSettings.Changed += ApplySetting;
            ApplySetting();
        }

        void OnDisable() => MapSettings.Changed -= ApplySetting;

        void OnDestroy()
        {
            if (floorTextures != null)
                foreach (var t in floorTextures)
                    Destroy(t);
        }

        void ApplySetting()
        {
            frame.gameObject.SetActive(editing || MapSettings.MinimapVisible);
            ReserveHelp();
        }

        // The Editor / desktop help text (top-left) moves right of the map while the map is at the top-left.
        void ReserveHelp()
        {
            if (hud == null || !placed)
                return;
            Rect r = ShownRect;
            bool topLeft = IsShown && r.yMax > SafeSize.y - 140f && r.xMin < SafeSize.x * 0.25f;
            hud.ReserveTopLeft(topLeft ? r.xMax + 16f : 0f);
        }

        // ------------------------------------------------------------------ placement (also used by the layout editor)

        /// <summary>Shows a place / size without saving it (layout editor preview).</summary>
        public void Preview(MiniMapPlacement p)
        {
            placement = Clamp(p, SafeSize);
            placed = true;
            placedFor = SafeSize;
            ApplyPlacement();
        }

        /// <summary>Shows and saves a place / size: from now on the game does not move the map.</summary>
        public void Commit(MiniMapPlacement p)
        {
            Preview(p);
            MapSettings.SavePlacement(placement);
            PlacementSaved = true;
        }

        /// <summary>Back to the saved place (or the automatic first place when none is saved).</summary>
        public void Revert()
        {
            placed = false;
            Place();
        }

        /// <summary>The automatic first place for a touch layout (the layout editor's "reset").</summary>
        public MiniMapPlacement AutoPlacementFor(TouchLayoutData layout) => AutoPlacement(SafeSize, layout);

        /// <summary>While the control layout is edited: shown even if switched off, with sample markers.</summary>
        public void SetEditing(bool on)
        {
            editing = on;
            editPreview.SetActive(on);
            ApplySetting();
        }

        int LayoutVersion => touchLayout != null ? touchLayout.Version : 0;

        void Place()
        {
            Vector2 safe = SafeSize;
            if (safe.x <= 1f || safe.y <= 1f)
                return;
            if (placed && safe == placedFor)
            {
                // The controls moved (layout applied / edited): only the avoidance is worked out again.
                if (LayoutVersion != placedForLayout)
                    ApplyPlacement();
                return;
            }
            if (MapSettings.TryLoadPlacement(out var saved))
            {
                PlacementSaved = true;
                placement = Clamp(saved, safe);   // the player's place: only kept inside the Safe Area
            }
            else
            {
                PlacementSaved = false;
                placement = AutoPlacement(safe, touchLayout != null ? touchLayout.Saved : null);
            }
            placed = true;
            placedFor = safe;
            ApplyPlacement();
        }

        void ApplyPlacement()
        {
            Vector2 safe = SafeSize;
            Vector2 size = BaseSize * placement.scale;
            CollectAvoidAreas();
            shownCentre = AvoidCentre(safe, size, placement.center * safe, avoidBoxes, avoidCircles, AvoidGap, out bool found);
            if (!found && !AvoidanceFailed)
                Debug.LogWarning($"[DEADLOCK][minimap] no clear place for the map ({size.x:0}x{size.y:0} in a {safe.x:0}x{safe.y:0} Safe Area): shown at the asked place");
            AvoidanceFailed = !found;
            placedForLayout = LayoutVersion;
            frame.anchoredPosition = shownCentre;
            frame.localScale = Vector3.one * placement.scale;
            ReserveHelp();
        }

        /// <summary>
        /// Centre to show the map at (Safe Area units, origin bottom-left): the asked centre if the map there is clear
        /// of every box and circle (with the gap), otherwise the nearest clear centre (4-unit grid) with the whole map
        /// inside the Safe Area. found = false: none clear, the asked centre is returned unchanged (size is never reduced).
        /// </summary>
        public static Vector2 AvoidCentre(Vector2 safe, Vector2 size, Vector2 wanted, IList<Rect> boxes, IList<Vector3> circles, float gap, out bool found)
        {
            Vector2 half = size * 0.5f;
            bool Clear(Vector2 c)
            {
                var r = new Rect(c - half - Vector2.one * gap, size + Vector2.one * (2f * gap));
                for (int i = 0; i < boxes.Count; i++)
                    if (r.Overlaps(boxes[i]))
                        return false;
                for (int i = 0; i < circles.Count; i++)
                {
                    var k = circles[i];
                    float nx = Mathf.Clamp(k.x, r.xMin, r.xMax), ny = Mathf.Clamp(k.y, r.yMin, r.yMax);
                    if ((nx - k.x) * (nx - k.x) + (ny - k.y) * (ny - k.y) < k.z * k.z)
                        return false;
                }
                return true;
            }
            found = true;
            if (Clear(wanted))
                return wanted;
            const float step = 4f;
            float bestD = float.MaxValue;
            Vector2 best = wanted;
            for (float x = half.x; x <= safe.x - half.x + 0.01f; x += step)
                for (float y = half.y; y <= safe.y - half.y + 0.01f; y += step)
                {
                    float d = (x - wanted.x) * (x - wanted.x) + (y - wanted.y) * (y - wanted.y);
                    if (d < bestD && Clear(new Vector2(x, y)))
                    {
                        bestD = d;
                        best = new Vector2(x, y);
                    }
                }
            found = bestD < float.MaxValue;
            return best;
        }

        /// <summary>HUD text (sized for its widest content), the HP bar, the menu button and the touch controls, in Safe Area units.</summary>
        void CollectAvoidAreas()
        {
            avoidBoxes.Clear();
            avoidCircles.Clear();
            if (hud != null)
            {
                AddText(hud.AmmoText, "888 / 888", hud.MeleeName);
                AddText(hud.StanceText, "スライディング　ダッシュ\n頭上が塞がっています");
                if (hud.HealthArea != null)
                {
                    var bar = WorldRect(hud.HealthArea);
                    avoidBoxes.Add(new Rect(bar.x, bar.y, bar.width, bar.height + 34f)); // the HP number above it
                }
            }
            var training = FindAnyObjectByType<TrainingHud>();
            if (training != null)
                AddText(training.StatusText, training.WidestStatus);
            var monitor = FrameRateMonitor.Instance;
            if (monitor != null && monitor.Label != null)
                AddText(monitor.Label, FrameRateMonitor.LabelTemplate);
            var menu = FindAnyObjectByType<GameMenu>();
            if (menu != null && menu.MenuButton != null)
            {
                var b = WorldRect((RectTransform)menu.MenuButton.transform);
                avoidBoxes.Add(new Rect(b.x - TouchLayout.MenuGuardMargin, b.y - TouchLayout.MenuGuardMargin,
                    b.width + 2f * TouchLayout.MenuGuardMargin, b.height + 2f * TouchLayout.MenuGuardMargin));
            }
            if (touchLayout != null)
                foreach (string id in TouchLayout.Circles)
                {
                    var w = WorldRect(touchLayout.Widget(id));
                    avoidCircles.Add(new Vector3(w.center.x, w.center.y, Mathf.Max(w.width, w.height) * 0.5f));
                }
        }

        /// <summary>A RectTransform's screen rectangle in this map's Safe Area units (origin bottom-left).</summary>
        Rect WorldRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 a = safeRect.InverseTransformPoint(corners[0]), b = safeRect.InverseTransformPoint(corners[2]);
            Vector2 offset = safeRect.rect.size * safeRect.pivot;
            return Rect.MinMaxRect(a.x + offset.x, a.y + offset.y, b.x + offset.x, b.y + offset.y);
        }

        /// <summary>The area a text takes with its widest content (lines split by '\n'), placed by its alignment in its rect.</summary>
        void AddText(Text text, params string[] widest)
        {
            if (text == null || !text.isActiveAndEnabled)
                return;
            var settings = text.GetGenerationSettings(Vector2.zero);
            var generator = text.cachedTextGeneratorForLayout;
            float w = 0f, h = 0f;
            foreach (string s in widest)
            {
                if (string.IsNullOrEmpty(s))
                    continue;
                w = Mathf.Max(w, generator.GetPreferredWidth(s, settings) / text.pixelsPerUnit);
                h = Mathf.Max(h, generator.GetPreferredHeight(s, settings) / text.pixelsPerUnit);
            }
            w = Mathf.Max(w, generator.GetPreferredWidth(text.text ?? "", settings) / text.pixelsPerUnit);
            h = Mathf.Max(h, generator.GetPreferredHeight(text.text ?? "", settings) / text.pixelsPerUnit);
            var box = WorldRect(text.rectTransform);
            // Screen units → Safe Area units (the canvases share one scale; the box is already in Safe Area units).
            float scale = box.width / Mathf.Max(1f, text.rectTransform.rect.width);
            w *= scale;
            h *= scale;
            var anchor = text.alignment;
            float x = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.LowerLeft ? box.xMin
                : anchor == TextAnchor.UpperRight || anchor == TextAnchor.MiddleRight || anchor == TextAnchor.LowerRight ? box.xMax - w
                : box.center.x - w * 0.5f;
            float y = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.UpperCenter || anchor == TextAnchor.UpperRight ? box.yMax - h
                : anchor == TextAnchor.LowerLeft || anchor == TextAnchor.LowerCenter || anchor == TextAnchor.LowerRight ? box.yMin
                : box.center.y - h * 0.5f;
            avoidBoxes.Add(new Rect(x, y, w, h));
        }

        void LateUpdate()
        {
            if (!editing)
                Place();
            else if (placed && LayoutVersion != placedForLayout)
                ApplyPlacement(); // buttons moved while editing: the preview keeps clear of them as the game will
            if (!IsShown || map == null || player == null)
                return;
            Vector3 p = player.position;
            int floor = map.FloorFor(p.y, DisplayedFloor);
            if (floor != DisplayedFloor)
            {
                DisplayedFloor = floor;
                mapImage.texture = floorTextures[floor];
                floorLabel.text = string.IsNullOrEmpty(map.floors[floor].label) ? $"{floor + 1}F" : map.floors[floor].label;
            }
            Vector3 forward = detector != null && detector.Eye != null ? detector.Eye.transform.forward : player.forward;
            if (new Vector2(forward.x, forward.z).sqrMagnitude > 1e-6f)
                yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            rotor.localRotation = Quaternion.Euler(0f, 0f, yaw);
            mapImage.rectTransform.anchoredPosition = -map.ToCells(p) * map.cellSize * UnitsPerMetre;
            float a = yaw * Mathf.Deg2Rad;
            north.anchoredPosition = ToEdge(new Vector2(-Mathf.Sin(a), Mathf.Cos(a)), 2f);
            UpdateMarkers(p);
            UpdateCues();
            UpdateWatchBars();
        }

        /// <summary>Half size of the area inside the frame where edge markers sit (rectangle, before the frame's scale).</summary>
        public static Vector2 EdgeHalf => (BaseSize - Vector2.one * (2f * Border)) * 0.5f - Vector2.one * EdgeGap;

        /// <summary>Beyond the edge rectangle?</summary>
        static bool BeyondEdge(Vector2 at) => Mathf.Abs(at.x) > EdgeHalf.x || Mathf.Abs(at.y) > EdgeHalf.y;

        /// <summary>The point on the edge rectangle in this direction (from the centre), moved in by extra.</summary>
        static Vector2 ToEdge(Vector2 direction, float extra = 0f)
        {
            Vector2 half = EdgeHalf - Vector2.one * extra;
            float fx = Mathf.Abs(direction.x) > 1e-5f ? half.x / Mathf.Abs(direction.x) : float.MaxValue;
            float fy = Mathf.Abs(direction.y) > 1e-5f ? half.y / Mathf.Abs(direction.y) : float.MaxValue;
            return direction * Mathf.Min(fx, fy);
        }

        void UpdateMarkers(Vector3 playerPosition)
        {
            shownMarkers = 0;
            int selfSlots = 0;
            if (detector != null && !editing)
            {
                var contacts = detector.Contacts;
                for (int i = 0; i < contacts.Count; i++)
                {
                    var c = contacts[i];
                    if (c.Enemy == null || c.Enemy.IsDead)
                        continue;
                    // Heard on another floor (also after the player changed floor): never a position on the map.
                    if (c.Kind == ContactKind.Heard && c.Floor != DisplayedFloor)
                        continue;
                    bool seen = c.Kind == ContactKind.Seen;
                    bool inSight = detector.InSight(c);
                    // In sight beyond the range: only the red frame part, while it watches the player (UpdateWatchBars).
                    if (inSight && BeyondEdge(WorldToFrame(c.Position)))
                        continue;
                    var image = Marker(shownMarkers);
                    var badge = badges[shownMarkers];
                    int dFloor = c.Floor - DisplayedFloor;
                    string kind = inSight ? "seen" : seen ? "lost" : "heard";
                    string floor = dFloor > 0 ? "_up" : dFloor < 0 ? "_down" : "";
                    var color = seen ? SeenColor : HeardGlowColor;
                    float age = (Time.time - c.Time) / detector.DisplayTime(c.Kind);
                    color.a = 1f - Mathf.InverseLerp(0.6f, 1f, age);
                    // Glows breathe slightly (a light, not a point); the triangle stays steady.
                    float glowAlpha = color.a * (0.75f + 0.25f * Mathf.Sin(Time.time * 6f));
                    // Always from the stored (last detected) position, never from where the enemy is now.
                    Vector2 at = WorldToFrame(c.Position);
                    var rect = image.rectTransform;
                    string shape;
                    badge.enabled = false;
                    if (BeyondEdge(at))
                    {
                        // Beyond the range: a glow on the edge toward the last detected position (no direction mark).
                        Vector2 edge = ToEdge(at.normalized);
                        image.sprite = glow;
                        rect.sizeDelta = new Vector2(EdgeGlowSize, EdgeGlowSize);
                        rect.anchoredPosition = edge;
                        rect.localRotation = Quaternion.identity;
                        color.a = glowAlpha;
                        shape = "edge_" + kind + floor;
                        if (dFloor != 0)
                            Badge(badge, edge - edge.normalized * 15f, dFloor, color);
                    }
                    else if (dFloor != 0 && at.magnitude < SelfRadius)
                    {
                        // Straight above / below: a box with a chevron in a slot beside the arrow (not the enemy's place).
                        Vector2 slot = new Vector2(30f, 8f - 24f * selfSlots++);
                        image.sprite = box;
                        rect.sizeDelta = new Vector2(MarkerSize, MarkerSize) * 1.15f;
                        rect.anchoredPosition = slot;
                        rect.localRotation = Quaternion.identity;
                        Badge(badge, slot, dFloor, color);
                        shape = "self_" + kind + floor;
                    }
                    else if (inSight)
                    {
                        // In sight: a triangle whose tip points where the enemy faces now.
                        image.sprite = facing;
                        rect.sizeDelta = new Vector2(FacingSize, FacingSize);
                        rect.anchoredPosition = at;
                        rect.localRotation = Quaternion.Euler(0f, 0f, FrameAngle(c.Facing));
                        if (dFloor != 0)
                            Badge(badge, at + new Vector2(17f, 9f), dFloor, color);
                        shape = kind + floor;
                    }
                    else if (!seen)
                    {
                        // Heard on this floor: an orange footprint at the step, toes where the enemy faced then.
                        image.sprite = footprint;
                        rect.sizeDelta = new Vector2(FacingSize, FacingSize);
                        rect.anchoredPosition = at;
                        rect.localRotation = Quaternion.Euler(0f, 0f, FrameAngle(c.Facing));
                        shape = kind + floor;
                    }
                    else
                    {
                        // Lost from sight: a soft red glow around where it was last seen, no direction.
                        image.sprite = glow;
                        rect.sizeDelta = new Vector2(GlowSize, GlowSize);
                        rect.anchoredPosition = at;
                        rect.localRotation = Quaternion.identity;
                        color.a = glowAlpha;
                        if (dFloor != 0)
                            Badge(badge, at + new Vector2(17f, 9f), dFloor, color);
                        shape = kind + floor;
                    }
                    image.color = color;
                    image.enabled = true;
                    markerEnemies[shownMarkers] = c.Enemy;
                    markerShapes[shownMarkers] = shape;
                    shownMarkers++;
                }
            }
            for (int i = shownMarkers; i < markers.Count; i++)
            {
                markers[i].enabled = false;
                badges[i].enabled = false;
                markerEnemies[i] = null;
            }
        }

        /// <summary>Footsteps from another floor: one footprint per rough direction and above / below, fading like the markers.</summary>
        void UpdateCues()
        {
            shownCues = 0;
            cueKeys.Clear();
            if (detector != null && !editing)
            {
                var cues = detector.Cues;
                for (int i = 0; i < cues.Count; i++)
                {
                    var c = cues[i];
                    if (c.Enemy == null || c.Enemy.IsDead)
                        continue;
                    var key = c.Direction * 10f + Vector2.one * (c.Vertical * 100f);
                    if (cueKeys.Contains(key))
                        continue; // one footprint per direction: the number of enemies is not told either
                    cueKeys.Add(key);
                    var color = HeardGlowColor;
                    color.a = 1f - Mathf.InverseLerp(0.6f, 1f, (Time.time - c.Time) / detector.CueDisplayTime);
                    var waveImage = Cue(shownCues);
                    var chevronImage = cueChevrons[shownCues];
                    var back = cueBacks[shownCues];
                    var w = waveImage.rectTransform;
                    string shape = c.Vertical > 0 ? "step_up" : "step_down";
                    if (c.Direction == Vector2.zero)
                    {
                        // Straight above / below: beside the floor label, not on the map.
                        Vector2 slot = new Vector2(-EdgeHalf.x + 12f, EdgeHalf.y - 34f);
                        w.anchoredPosition = slot;
                        chevronImage.rectTransform.anchoredPosition = slot + new Vector2(20f, 0f);
                        // On a dark plate: an indicator, not a place on the map.
                        back.rectTransform.anchoredPosition = slot + new Vector2(10f, 0f);
                        back.enabled = true;
                        shape += "_overhead";
                    }
                    else
                    {
                        back.enabled = false;
                        Vector2 dir = Quaternion.Euler(0f, 0f, yaw) * c.Direction;
                        Vector2 edge = ToEdge(dir.normalized);
                        w.anchoredPosition = edge;
                        chevronImage.rectTransform.anchoredPosition = edge - dir.normalized * 18f;
                    }
                    // The arrow points where the enemy faced at the step (8 ways), not toward the player.
                    w.localRotation = Quaternion.Euler(0f, 0f, FrameAngle(c.Heading));
                    chevronImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, c.Vertical < 0 ? 180f : 0f);
                    waveImage.color = chevronImage.color = color;
                    waveImage.enabled = chevronImage.enabled = true;
                    cueShapes[shownCues] = shape;
                    shownCues++;
                }
            }
            for (int i = shownCues; i < cueWaves.Count; i++)
                cueWaves[i].enabled = cueChevrons[i].enabled = cueBacks[i].enabled = false;
        }

        Image Cue(int index)
        {
            while (cueWaves.Count <= index)
            {
                var br = UiBuild.Place(UiBuild.Rect($"CueBack_{cueBacks.Count}", markerLayer), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52f, 30f));
                var b = Graphic(br, plate, new Color(0f, 0f, 0f, 0.8f));
                b.enabled = false;
                cueBacks.Add(b);
                var wr = UiBuild.Place(UiBuild.Rect($"Cue_{cueWaves.Count}", markerLayer), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(FacingSize, FacingSize));
                cueWaves.Add(Graphic(wr, arrow, HeardGlowColor));
                var cr = UiBuild.Place(UiBuild.Rect($"CueChevron_{cueChevrons.Count}", markerLayer), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));
                cueChevrons.Add(Graphic(cr, chevron, HeardColor));
                cueShapes.Add(null);
            }
            return cueWaves[index];
        }

        /// <summary>Test / capture access: the shapes of the footstep cues shown now ("step_up", "step_down_overhead").</summary>
        public List<string> CueShapesShown() => cueShapes.GetRange(0, shownCues);

        /// <summary>Test / capture access: where cue i is shown (map frame, centre = player).</summary>
        public Vector2 CueFramePosition(int index) => cueWaves[index].rectTransform.anchoredPosition;

        /// <summary>Test / capture access: cue i's arrow rotation (degrees, counter-clockwise; 0 = pointing up).</summary>
        public float CueAngle(int index) => cueWaves[index].rectTransform.localEulerAngles.z;

        /// <summary>
        /// Enemies beyond the range that watch the player: the frame turns red over a short part toward each one (on
        /// the side the direction meets), breathing slightly; gone shortly after it stops watching.
        /// </summary>
        void UpdateWatchBars()
        {
            shownWatchBars = 0;
            if (detector != null && !editing)
            {
                Vector2 half = BaseSize * 0.5f - Vector2.one * (Border * 0.5f);
                var watchers = detector.Watchers;
                for (int i = 0; i < watchers.Count; i++)
                {
                    var w = watchers[i];
                    float age = Time.time - w.Time;
                    if (w.Enemy == null || w.Enemy.IsDead || age > detector.VisionInterval * 1.5f + WatchHold)
                        continue;
                    Vector2 at = WorldToFrame(w.Position);
                    if (!BeyondEdge(at))
                        continue; // within the range: the map shows it (or it is hidden: no place given away here)
                    Vector2 d = at.normalized;
                    float tx = Mathf.Abs(d.x) > 1e-5f ? half.x / Mathf.Abs(d.x) : float.MaxValue;
                    float ty = Mathf.Abs(d.y) > 1e-5f ? half.y / Mathf.Abs(d.y) : float.MaxValue;
                    bool topOrBottom = ty <= tx;
                    Vector2 p = d * Mathf.Min(tx, ty);
                    var bar = WatchBar(shownWatchBars++);
                    var r = bar.rectTransform;
                    if (topOrBottom)
                    {
                        p.x = Mathf.Clamp(p.x, -half.x + WatchBarLength * 0.5f, half.x - WatchBarLength * 0.5f);
                        r.sizeDelta = new Vector2(WatchBarLength, WatchBarWidth);
                    }
                    else
                    {
                        p.y = Mathf.Clamp(p.y, -half.y + WatchBarLength * 0.5f, half.y - WatchBarLength * 0.5f);
                        r.sizeDelta = new Vector2(WatchBarWidth, WatchBarLength);
                    }
                    r.anchoredPosition = p;
                    var color = SeenColor;
                    color.a = (0.8f + 0.2f * Mathf.Sin(Time.time * 6f)) * (1f - Mathf.InverseLerp(detector.VisionInterval * 1.5f, detector.VisionInterval * 1.5f + WatchHold, age));
                    bar.color = color;
                    bar.enabled = true;
                }
            }
            for (int i = shownWatchBars; i < watchBars.Count; i++)
                watchBars[i].enabled = false;
        }

        Image WatchBar(int index)
        {
            while (watchBars.Count <= index)
            {
                // On the frame itself (not inside the masked map), over its light blue edge.
                var rect = UiBuild.Place(UiBuild.Rect($"WatchBar_{watchBars.Count}", frame), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WatchBarLength, WatchBarWidth));
                var image = UiBuild.Image(rect, SeenColor, false);
                image.enabled = false;
                watchBars.Add(image);
            }
            return watchBars[index];
        }

        /// <summary>
        /// Rotation (degrees, counter-clockwise from up) of a mark pointing along a world direction (x, z) on this
        /// heading-up map.
        /// </summary>
        public float FrameAngle(Vector2 worldDirection)
        {
            Vector2 v = Quaternion.Euler(0f, 0f, yaw) * worldDirection;
            return Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f;
        }

        /// <summary>Up / down chevron beside a marker on another floor (not a triangle: triangles are enemies).</summary>
        void Badge(Image badge, Vector2 position, int dFloor, Color color)
        {
            badge.sprite = chevron;
            badge.rectTransform.anchoredPosition = position;
            badge.rectTransform.localRotation = Quaternion.Euler(0f, 0f, dFloor < 0 ? 180f : 0f);
            badge.color = color;
            badge.enabled = true;
        }

        Image Marker(int index)
        {
            while (markers.Count <= index)
            {
                var rect = UiBuild.Place(UiBuild.Rect($"Marker_{markers.Count}", markerLayer), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MarkerSize, MarkerSize));
                var image = Graphic(rect, facing, SeenColor);
                image.enabled = false;
                markers.Add(image);
                var badgeRect = UiBuild.Place(UiBuild.Rect($"Badge_{badges.Count}", markerLayer), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));
                var badge = Graphic(badgeRect, chevron, SeenColor);
                badge.enabled = false;
                badges.Add(badge);
                markerEnemies.Add(null);
                markerShapes.Add(null);
            }
            return markers[index];
        }

        /// <summary>Test / capture access: the marker of an enemy (its shape and position in the map frame, centre = player).</summary>
        public bool TryGetMarker(MapEnemy enemy, out string shape, out Vector2 framePosition)
        {
            for (int i = 0; i < shownMarkers; i++)
                if (markerEnemies[i] == enemy)
                {
                    shape = markerShapes[i];
                    framePosition = markers[i].rectTransform.anchoredPosition;
                    return true;
                }
            shape = null;
            framePosition = default;
            return false;
        }

        /// <summary>Test / capture access: an enemy marker's rotation (degrees, counter-clockwise; 0 = tip / toes up).</summary>
        public bool TryGetMarkerAngle(MapEnemy enemy, out float angle)
        {
            for (int i = 0; i < shownMarkers; i++)
                if (markerEnemies[i] == enemy)
                {
                    angle = markers[i].rectTransform.localEulerAngles.z;
                    return true;
                }
            angle = 0f;
            return false;
        }

        /// <summary>Where a world position appears in the map frame (canvas units from the centre = the player, before the frame's scale).</summary>
        public Vector2 WorldToFrame(Vector3 world)
        {
            Vector3 d = world - player.position;
            return Quaternion.Euler(0f, 0f, yaw) * (new Vector2(d.x, d.z) * UnitsPerMetre);
        }

        static Image Graphic(RectTransform rect, Sprite sprite, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        // ------------------------------------------------------------------ images

        // Floor slate blue, walls light grey, the drop deep violet with a hatch, stairs teal with darker step lines and
        // a bright cyan boundary toward the other floor, outside near black: apart from each other and from the
        // markers' red / amber, and readable on a dark screen.
        public static readonly Color32 FloorColor = new Color32(58, 76, 102, 240);
        public static readonly Color32 WallColor = new Color32(222, 226, 232, 255);
        public static readonly Color32 VoidColor = new Color32(26, 16, 44, 255);
        public static readonly Color32 VoidHatch = new Color32(84, 62, 120, 255);
        public static readonly Color32 StairColor = new Color32(48, 122, 108, 250);
        public static readonly Color32 StairLine = new Color32(28, 78, 70, 250);
        public static readonly Color32 StairEndColor = new Color32(120, 240, 230, 255);

        public static Color32 CellColor(MapCell cell, int x, int y) => cell switch
        {
            MapCell.Floor => FloorColor,
            MapCell.Wall => WallColor,
            // Hatched: an open drop, clearly not floor.
            MapCell.Void => (x + y) % 6 < 2 ? VoidHatch : VoidColor,
            MapCell.Stairs => StairColor,
            MapCell.StairsStep => StairLine,
            MapCell.StairsEnd => StairEndColor,
            _ => OutsideColor,
        };

        /// <summary>One texel per cell, point filtered (the map keeps the game's pixel look).</summary>
        Texture2D BuildTexture(int floor)
        {
            var tex = new Texture2D(map.width, map.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = $"MiniMap_{floor}",
            };
            var pixels = new Color32[map.width * map.height];
            for (int y = 0; y < map.height; y++)
                for (int x = 0; x < map.width; x++)
                    pixels[y * map.width + x] = CellColor(map.Cell(floor, x, y), x, y);
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        void BuildSprites()
        {
            const int n = 32;
            Vector2 a = new Vector2(15.5f, 30f), b = new Vector2(1.5f, 3f), c = new Vector2(29.5f, 3f);
            // The player's arrow.
            triangle = MakeSprite("MarkerTriangle", (x, y) => Inside(new Vector2(x, y), a, b, c));
            // Soft glow: bright in the middle, fading to nothing at the rim (no edge, no point).
            glow = MakeSpriteColours("MarkerGlow", (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f;
                float t = Mathf.Clamp01(1f - d);
                return new Color32(255, 255, 255, (byte)(255f * 0.85f * t * t * (3f - 2f * t)));
            });
            // Seen enemy: a tall isosceles triangle (about 36 degrees at the tip), tip up before rotation, white inside a
            // black rim so the tip reads small and on any floor colour.
            Vector2 tip = new Vector2(15.5f, 29.5f), left = new Vector2(7f, 3f), right = new Vector2(24f, 3f);
            facing = MakeOutlined("MarkerFacing", (x, y) => Inside(new Vector2(x, y), tip, left, right));
            // Heard on this floor: one footprint, toes up before rotation (sole, heel and five toes, big toe on the
            // left), white inside a black rim.
            footprint = MakeOutlined("MarkerFootprint", (x, y) =>
            {
                bool Ellipse(float cx, float cy, float rx, float ry) => (x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry) <= 1f;
                bool Disc(float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
                return Ellipse(15.5f, 15f, 6.2f, 6.5f) || Ellipse(15f, 5.5f, 4.3f, 3.6f)
                    || Disc(11.5f, 24.5f, 2.9f) || Disc(16f, 26.2f, 2.1f) || Disc(19.6f, 25.4f, 1.9f) || Disc(22.4f, 23.4f, 1.7f) || Disc(23.9f, 20.6f, 1.5f);
            });
            // Footstep from another floor: an arrow (head and shaft), pointing up before rotation, black rim.
            arrow = MakeOutlined("MarkerArrow", (x, y) =>
                Inside(new Vector2(x, y), new Vector2(15.5f, 29f), new Vector2(6f, 17f), new Vector2(25f, 17f))
                || (Mathf.Abs(x - 15.5f) <= 2.6f && y >= 3f && y <= 18f));
            // Up / down chevron (an open "^": not the enemy triangles).
            chevron = MakeSprite("MarkerChevron", (x, y) =>
            {
                float dx = Mathf.Abs(x - 15.5f), top = 26f - dx * 1.1f;
                return y <= top && y >= top - 7f && dx < 14f;
            });
            // Dark plate under the straight-above / below cue (an indicator, not a place on the map).
            plate = MakeSprite("MarkerPlate", (x, y) => x > 0 && x < 31 && y > 0 && y < 31);
            // Box around the chevron in the slot beside the arrow (an indicator, not a place on the map).
            box = MakeSprite("MarkerBox", (x, y) => (x < 3 || x > 28 || y < 3 || y > 28) && x > 0 && x < 31 && y > 0 && y < 31);

            // White inside, a black rim about 1.5 texels wide outside, transparent elsewhere (the colour tints the inside).
            Sprite MakeOutlined(string name, System.Func<int, int, bool> inside)
            {
                return MakeSpriteColours(name, (x, y) =>
                {
                    if (inside(x, y))
                        return new Color32(255, 255, 255, 255);
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            if (dx * dx + dy * dy <= 4 && x + dx >= 0 && y + dy >= 0 && x + dx < n && y + dy < n && inside(x + dx, y + dy))
                                return new Color32(0, 0, 0, 255);
                    return new Color32(0, 0, 0, 0);
                });
            }

            Sprite MakeSprite(string name, System.Func<int, int, bool> on) =>
                MakeSpriteColours(name, (x, y) => on(x, y) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0));

            Sprite MakeSpriteColours(string name, System.Func<int, int, Color32> colour)
            {
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = name };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        px[y * n + x] = colour(x, y);
                tex.SetPixels32(px);
                tex.Apply(false, true);
                return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float Cross(Vector2 u, Vector2 v, Vector2 w) => (v.x - u.x) * (w.y - u.y) - (v.y - u.y) * (w.x - u.x);
            float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
