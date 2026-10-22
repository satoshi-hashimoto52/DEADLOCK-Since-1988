using System.Collections.Generic;
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
        [Tooltip("Metres shown across the map.")]
        [SerializeField, Min(4f)] float metresAcross = 22f;

        /// <summary>Map size and its distance from the Safe Area's top-left corner (canvas units, reference height 1080).</summary>
        public const float Size = 188f;
        public static readonly Vector2 Margin = new Vector2(14f, 14f);
        const float MarkerSize = 20f;

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
        readonly List<MapEnemy> markerEnemies = new List<MapEnemy>();
        readonly List<string> markerShapes = new List<string>();
        int shownMarkers;
        float yaw;
        Sprite circle, ring, triangle, triangleHollow;

        public int DisplayedFloor { get; private set; } = -1;
        public bool IsShown => frame != null && frame.gameObject.activeSelf;
        public RectTransform Frame => frame;
        public float UnitsPerMetre => Size / metresAcross;
        /// <summary>Rotation of the map (degrees, counter-clockwise on screen) = the player's heading.</summary>
        public float MapRotation => yaw;
        public int ShownMarkers => shownMarkers;

        /// <summary>The map's rectangle inside a Safe Area of this size (canvas units, origin bottom-left, y up).</summary>
        public static Rect LayoutRect(Vector2 safeSize) => new Rect(Margin.x, safeSize.y - Margin.y - Size, Size, Size);

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
            markerLayer = UiBuild.Place(UiBuild.Rect("Markers", rotor), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

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
            // The Editor / desktop help text (top-left) moves right of the map while it is shown.
            if (hud != null)
                hud.ReserveTopLeft(visible ? Margin.x + Size + 16f : 0f);
        }

        void LateUpdate()
        {
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
            float r = Size * 0.5f - 14f;
            float a = yaw * Mathf.Deg2Rad;
            north.anchoredPosition = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * r;
            UpdateMarkers(p);
        }

        void UpdateMarkers(Vector3 playerPosition)
        {
            shownMarkers = 0;
            if (detector != null)
            {
                var contacts = detector.Contacts;
                for (int i = 0; i < contacts.Count; i++)
                {
                    var c = contacts[i];
                    if (c.Enemy == null || c.Enemy.IsDead)
                        continue;
                    var image = Marker(shownMarkers);
                    bool seen = c.Kind == ContactKind.Seen;
                    int dFloor = c.Floor - DisplayedFloor;
                    string shape = (seen ? "seen" : "heard") + (dFloor > 0 ? "_up" : dFloor < 0 ? "_down" : "");
                    image.sprite = dFloor == 0 ? (seen ? circle : ring) : (seen ? triangle : triangleHollow);
                    var rect = image.rectTransform;
                    // Triangles stay upright on the turning map: up = above, down = below.
                    rect.localRotation = Quaternion.Euler(0f, 0f, -yaw + (dFloor < 0 ? 180f : 0f));
                    rect.sizeDelta = dFloor == 0 ? new Vector2(MarkerSize, MarkerSize) : new Vector2(MarkerSize, MarkerSize) * 1.1f;
                    Vector3 d = c.Position - playerPosition;
                    rect.anchoredPosition = new Vector2(d.x, d.z) * UnitsPerMetre;
                    float age = (Time.time - c.Time) / detector.DisplayTime(c.Kind);
                    var color = seen ? SeenColor : HeardColor;
                    color.a = 1f - Mathf.InverseLerp(0.6f, 1f, age);
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
                markerEnemies[i] = null;
            }
        }

        Image Marker(int index)
        {
            while (markers.Count <= index)
            {
                var rect = UiBuild.Place(UiBuild.Rect($"Marker_{markers.Count}", markerLayer), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MarkerSize, MarkerSize));
                var image = Graphic(rect, circle, SeenColor);
                image.enabled = false;
                markers.Add(image);
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
                    framePosition = Quaternion.Euler(0f, 0f, yaw) * markers[i].rectTransform.anchoredPosition;
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
