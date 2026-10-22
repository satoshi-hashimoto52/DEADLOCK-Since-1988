using System.Collections.Generic;
using Deadlock.Input;
using Deadlock.Map;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>
    /// Small floor map in the top-left corner of the Safe Area (clear of the notch, the top-centre menu / status, the
    /// ammo counter and the default touch controls). Heading-up: the map turns around the player's arrow in the middle.
    /// Shows the floor the player is on (switched with hysteresis on the stairs, <see cref="FloorMapData.FloorFor"/>):
    /// floor, walls, stairs and the atrium drop (hatched, not walkable), and the enemies the
    /// <see cref="EnemyDetector"/> currently reports, at their detected positions:
    /// seen = filled circle (red), heard = ring (amber); on another floor = an up / down triangle (filled = seen,
    /// hollow = heard) instead, so it never looks like an enemy on this floor. Markers fade out before they go.
    /// Two kinds of markers are not at the enemy's exact place, and look different so they are not read as one:
    /// - beyond the map's range: a notched pointer held on the map's edge, aimed at the last detected position (filled =
    ///   seen, hollow = heard), with a small up / down triangle beside it for another floor;
    /// - another floor, (nearly) straight above / below the player: a boxed up / down triangle in a slot beside the
    ///   player's arrow, instead of a triangle hidden under the arrow.
    /// Placed at the top-left by default; when the player's own touch layout puts a control there, the map slides
    /// right along the top edge to the first place clear of the controls, the menu button, the status line and the
    /// ammo / posture text (<see cref="ChoosePosition"/>, re-checked twice a second, so layout edits are followed).
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
        [Tooltip("Optional: the touch layout, whose controls the map keeps clear of.")]
        [SerializeField] TouchLayoutApplier touchLayout;
        [Tooltip("Metres shown across the map.")]
        [SerializeField, Min(4f)] float metresAcross = 22f;

        /// <summary>Map size and its distance from the Safe Area's top-left corner (canvas units, reference height 1080).</summary>
        public const float Size = 188f;
        public static readonly Vector2 Margin = new Vector2(14f, 14f);
        const float MarkerSize = 20f;
        /// <summary>Markers farther than this from the centre (canvas units, either axis) go on the edge instead.</summary>
        const float EdgeInset = 80f;
        /// <summary>Another floor's marker closer than this to the centre would hide under the player's arrow.</summary>
        const float SelfRadius = 16f;

        static readonly Color SeenColor = new Color(1f, 0.27f, 0.22f, 1f);
        static readonly Color HeardColor = new Color(1f, 0.78f, 0.25f, 1f);

        RectTransform frame;
        RectTransform rotor;
        RawImage mapImage;
        RectTransform markerLayer;
        RectTransform north;
        Text floorLabel;
        Texture2D[] floorTextures;
        readonly List<Image> markers = new List<Image>();
        readonly List<Image> badges = new List<Image>();
        readonly List<MapEnemy> markerEnemies = new List<MapEnemy>();
        readonly List<string> markerShapes = new List<string>();
        int shownMarkers;
        float yaw;
        Sprite circle, ring, triangle, triangleHollow, pointer, pointerHollow, box;

        public int DisplayedFloor { get; private set; } = -1;
        public bool IsShown => frame != null && frame.gameObject.activeSelf;
        public RectTransform Frame => frame;
        public float UnitsPerMetre => Size / metresAcross;
        /// <summary>Rotation of the map (degrees, counter-clockwise on screen) = the player's heading.</summary>
        public float MapRotation => yaw;
        public int ShownMarkers => shownMarkers;

        /// <summary>The map's rectangle inside a Safe Area of this size (canvas units, origin bottom-left, y up).</summary>
        public static Rect LayoutRect(Vector2 safeSize) => LayoutRect(safeSize, Margin.x);

        static Rect LayoutRect(Vector2 safeSize, float left) => new Rect(left, safeSize.y - Margin.y - Size, Size, Size);

        /// <summary>
        /// Left edge of the map (canvas units from the Safe Area's left) for a touch layout: the top-left corner, or the
        /// first place to the right along the top edge that is clear of every control (8 units apart), the menu button
        /// and its margin, the top-centre status line and the top-right ammo / posture text. None clear: the corner.
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
            for (float left = Margin.x; left + Size + Margin.x <= safeSize.x; left += gap)
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

        void Awake()
        {
            BuildSprites();
            var layer = UiBuild.Stretch(UiBuild.Rect("MiniMapLayer", transform));
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", layer));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            // The frame draws a thin light border (its own image) around the dark map background.
            frame = UiBuild.Place(UiBuild.Rect("MiniMap", safe), new Vector2(0f, 1f), new Vector2(Margin.x, -Margin.y), new Vector2(Size, Size));
            UiBuild.Image(frame, new Color(1f, 1f, 1f, 0.35f), false);
            var background = UiBuild.Stretch(UiBuild.Rect("Background", frame));
            background.offsetMin = new Vector2(3f, 3f);
            background.offsetMax = new Vector2(-3f, -3f);
            UiBuild.Image(background, new Color(0.02f, 0.03f, 0.05f, 0.85f), false);
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

            // The player: an arrow in the middle, always pointing up (heading-up map).
            var arrowOutline = UiBuild.Place(UiBuild.Rect("PlayerOutline", viewport), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 28f));
            Graphic(arrowOutline, triangle, Color.black);
            var arrow = UiBuild.Place(UiBuild.Rect("Player", viewport), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 20f));
            Graphic(arrow, triangle, new Color(0.55f, 0.9f, 1f, 1f));
            north = UiBuild.Place(UiBuild.Rect("North", viewport), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            UiBuild.Text(north, "N", 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));
            floorLabel = UiBuild.Text(UiBuild.Place(UiBuild.Rect("FloorLabel", frame), new Vector2(0f, 1f), new Vector2(8f, -4f), new Vector2(80f, 30f)),
                "", 24, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.9f));
            floorLabel.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            floorTextures = new Texture2D[map != null ? map.floors.Length : 0];
            for (int f = 0; f < floorTextures.Length; f++)
                floorTextures[f] = BuildTexture(f);
            if (map != null)
                mapRect.sizeDelta = new Vector2(map.width, map.height) * map.cellSize * UnitsPerMetre;
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
            bool visible = MapSettings.MinimapVisible;
            frame.gameObject.SetActive(visible);
            ReserveHelp();
        }

        // The Editor / desktop help text (top-left) moves right of the map while it is shown.
        void ReserveHelp()
        {
            if (hud != null)
                hud.ReserveTopLeft(IsShown ? frame.anchoredPosition.x + Size + 16f : 0f);
        }

        float nextPlacement;

        void Place()
        {
            if (touchLayout == null || Time.unscaledTime < nextPlacement)
                return;
            nextPlacement = Time.unscaledTime + 0.5f;
            float left = ChoosePosition(((RectTransform)frame.parent).rect.size, touchLayout.Shown);
            if (Mathf.Approximately(left, frame.anchoredPosition.x))
                return;
            frame.anchoredPosition = new Vector2(left, -Margin.y);
            ReserveHelp();
        }

        void LateUpdate()
        {
            if (!IsShown || map == null || player == null)
                return;
            Place();
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
            float r = Size * 0.5f - 14f;
            float a = yaw * Mathf.Deg2Rad;
            north.anchoredPosition = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * r;
            UpdateMarkers(p);
        }

        void UpdateMarkers(Vector3 playerPosition)
        {
            shownMarkers = 0;
            int selfSlots = 0;
            if (detector != null)
            {
                var contacts = detector.Contacts;
                for (int i = 0; i < contacts.Count; i++)
                {
                    var c = contacts[i];
                    if (c.Enemy == null || c.Enemy.IsDead)
                        continue;
                    var image = Marker(shownMarkers);
                    var badge = badges[shownMarkers];
                    bool seen = c.Kind == ContactKind.Seen;
                    int dFloor = c.Floor - DisplayedFloor;
                    string kind = seen ? "seen" : "heard";
                    string floor = dFloor > 0 ? "_up" : dFloor < 0 ? "_down" : "";
                    var color = seen ? SeenColor : HeardColor;
                    float age = (Time.time - c.Time) / detector.DisplayTime(c.Kind);
                    color.a = 1f - Mathf.InverseLerp(0.6f, 1f, age);
                    // Always from the stored (last detected) position, never from where the enemy is now.
                    Vector2 at = WorldToFrame(c.Position);
                    var rect = image.rectTransform;
                    string shape;
                    badge.enabled = false;
                    float far = Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.y));
                    if (far > EdgeInset)
                    {
                        // Beyond the range: a pointer on the edge, toward the last detected position.
                        Vector2 edge = at * (EdgeInset / far);
                        image.sprite = seen ? pointer : pointerHollow;
                        rect.sizeDelta = new Vector2(MarkerSize, MarkerSize);
                        rect.anchoredPosition = edge;
                        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(at.y, at.x) * Mathf.Rad2Deg - 90f);
                        shape = "edge_" + kind + floor;
                        if (dFloor != 0)
                            Badge(badge, edge - edge.normalized * 15f, dFloor, seen, color);
                    }
                    else if (dFloor != 0 && at.magnitude < SelfRadius)
                    {
                        // Straight above / below: a boxed triangle in a slot beside the arrow (not the enemy's place).
                        Vector2 slot = new Vector2(30f, 8f - 24f * selfSlots++);
                        image.sprite = box;
                        rect.sizeDelta = new Vector2(MarkerSize, MarkerSize) * 1.15f;
                        rect.anchoredPosition = slot;
                        rect.localRotation = Quaternion.identity;
                        Badge(badge, slot, dFloor, seen, color);
                        shape = "self_" + kind + floor;
                    }
                    else
                    {
                        image.sprite = dFloor == 0 ? (seen ? circle : ring) : (seen ? triangle : triangleHollow);
                        rect.sizeDelta = dFloor == 0 ? new Vector2(MarkerSize, MarkerSize) : new Vector2(MarkerSize, MarkerSize) * 1.1f;
                        rect.anchoredPosition = at;
                        // Triangles stay upright: up = above, down = below.
                        rect.localRotation = Quaternion.Euler(0f, 0f, dFloor < 0 ? 180f : 0f);
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

        void Badge(Image badge, Vector2 position, int dFloor, bool seen, Color color)
        {
            badge.sprite = seen ? triangle : triangleHollow;
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
                var image = Graphic(rect, circle, SeenColor);
                image.enabled = false;
                markers.Add(image);
                var badgeRect = UiBuild.Place(UiBuild.Rect($"Badge_{badges.Count}", markerLayer), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));
                var badge = Graphic(badgeRect, triangle, SeenColor);
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

        /// <summary>Where a world position appears in the map frame (canvas units from the centre = the player).</summary>
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

        static readonly Color32 FloorColor = new Color32(78, 82, 98, 220);
        static readonly Color32 WallColor = new Color32(225, 225, 235, 255);
        static readonly Color32 VoidColor = new Color32(12, 12, 20, 230);
        static readonly Color32 VoidHatch = new Color32(60, 58, 80, 230);
        static readonly Color32 StairColor = new Color32(160, 140, 96, 240);
        static readonly Color32 StairLine = new Color32(104, 90, 62, 240);

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
                {
                    pixels[y * map.width + x] = map.Cell(floor, x, y) switch
                    {
                        MapCell.Floor => FloorColor,
                        MapCell.Wall => WallColor,
                        // Hatched: an open drop, clearly not floor.
                        MapCell.Void => (x + y) % 6 < 2 ? VoidHatch : VoidColor,
                        // Step lines across the ramp (the ramps run north-south).
                        MapCell.Stairs => y % 3 == 0 ? StairLine : StairColor,
                        _ => new Color32(0, 0, 0, 0),
                    };
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        void BuildSprites()
        {
            const int n = 32;
            circle = MakeSprite("MarkerCircle", (x, y) => Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) < 13f);
            ring = MakeSprite("MarkerRing", (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f));
                return d < 14.5f && d > 9f;
            });
            Vector2 a = new Vector2(15.5f, 30f), b = new Vector2(1.5f, 3f), c = new Vector2(29.5f, 3f);
            Vector2 centroid = (a + b + c) / 3f;
            triangle = MakeSprite("MarkerTriangle", (x, y) => Inside(new Vector2(x, y), a, b, c));
            triangleHollow = MakeSprite("MarkerTriangleHollow", (x, y) =>
                Inside(new Vector2(x, y), a, b, c) && !Inside(new Vector2(x, y), Shrink(a), Shrink(b), Shrink(c)));
            Vector2 Shrink(Vector2 v) => centroid + (v - centroid) * 0.45f;
            // Edge pointer: an arrowhead with a notch at its base (not a plain triangle), pointing up before rotation.
            Vector2 notch = new Vector2(15.5f, 13f), notchHollow = new Vector2(15.5f, 19f);
            bool Arrowhead(Vector2 p, Vector2 n) => Inside(p, a, b, c) && !Inside(p, b, n, c);
            pointer = MakeSprite("MarkerPointer", (x, y) => Arrowhead(new Vector2(x, y), notch));
            pointerHollow = MakeSprite("MarkerPointerHollow", (x, y) =>
            {
                var p = new Vector2(x, y);
                bool inner = Inside(p, Shrink(a), Shrink(b), Shrink(c)) && !Inside(p, Shrink(b), notchHollow, Shrink(c));
                return Arrowhead(p, notch) && !inner;
            });
            // Box around the triangle in the slot beside the arrow (an indicator, not a place on the map).
            box = MakeSprite("MarkerBox", (x, y) => (x < 3 || x > 28 || y < 3 || y > 28) && x > 0 && x < 31 && y > 0 && y < 31);

            Sprite MakeSprite(string name, System.Func<int, int, bool> on)
            {
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = name };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        px[y * n + x] = on(x, y) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
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
