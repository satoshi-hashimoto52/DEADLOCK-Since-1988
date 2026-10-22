using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Map;
using Deadlock.UI;
using Deadlock.View;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Deadlock.Editor
{
    /// <summary>
    /// Ruins test field (prototype, for future dungeons): a closed two-storey stone building with a central atrium hall,
    /// an upper gallery with a parapet, a ring aisle, side wings with rooms, corridors that loop, and two walled and
    /// roofed stair tunnels. Same player rig, HUD, menu and zombies as the training; the round is a
    /// <see cref="TrainingSession"/> (all zombies dead = cleared, retry reloads the scene).
    ///
    /// Plan (metres; x east, z north; ground floor y 0, upper floor y 5, roof 9): building x -HalfX..HalfX,
    /// z -HalfZ..HalfZ; atrium x, z -8..8 open to the roof; ring aisle / gallery to the ring walls at |x|, |z| = RingHalf;
    /// north / south corridors between the ring walls and the outer walls; east / west wings with a corridor beside the
    /// stair tunnel along the outer wall (east: up toward +z from z -7; west: up toward -z from z 7).
    /// Stairs are 30 degree ramps for collision (the crouch / prone / slide step heights are 0.05-0.1 m, so real steps
    /// would block them) with step treads drawn on top; the tunnels have walls and a sloped ceiling 3.6 m above.
    /// Way-finding is by lighter dressed stone (door frames, stair openings, step nosings) and small dim lamps, not by
    /// strong glow.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        public const string RuinsScenePath = "Assets/Game/Scenes/Proto_Ruins.unity";
        const string RuinsNavMeshPath = "Assets/Game/Generated/NavMesh_Ruins.asset";
        const string RuinsFloorMapPath = "Assets/Game/Generated/RuinsFloorMap.asset";

        const float UpperY = 5f;          // upper floor walking surface
        const float SlabBottom = 4.6f;    // underside of the upper floor slab = ground floor ceiling
        const float RoofY = 9f;           // underside of the roof
        const float StairRise = UpperY;
        static readonly float StairRun = UpperY / Mathf.Tan(30f * Mathf.Deg2Rad); // 8.66 m
        const float StairCeiling = 3.6f;  // vertical clearance above the ramp
        const float PieceMax = 8f;        // floors / walls are split into pieces of at most 8 m (lights per object: 4)

        // Plan dimensions. Widened after the first device check (2026-10-22, "a little narrow"); previous values noted.
        const float AtriumHalf = 8f;       // unchanged
        const float RingHalf = 12.6f;      // ring wall centre lines (was 12): aisle / gallery clear width 3.2 -> 3.8 m
        const float HalfZ = 17.3f;         // outer north / south wall faces (was 16): corridors 3.8 -> 4.5 m clear
        const float HalfX = 21.1f;         // outer east / west wall faces (was 20): keeps the wing corridors at 4.5 m
        const float TunnelWidth = 3.5f;    // stair tunnels (was 3.0)
        const float DoorWide = 3.5f;       // main ring doors (was 3.0)
        const float DoorNarrow = 2.8f;     // other doors (was 2.4)
        const float DoorHeight = 3f;
        const float WallThickness = 0.4f;
        static float TunnelInner => HalfX - TunnelWidth;                 // 17.6: tunnel interior from here to HalfX
        static float InnerWallEdge => TunnelInner - 0.3f;                // 17.3: wing corridor ends at the tunnel wall
        static float WingCorridorX => (RingHalf + WallThickness / 2f + InnerWallEdge) / 2f; // 15.05
        static float TunnelCentreX => HalfX - TunnelWidth / 2f;          // 19.35
        static float CorridorZ => (RingHalf + WallThickness / 2f + HalfZ) / 2f;           // 15.15
        const float WingDoorX = 16.8f;     // doors from the wing rooms to the north / south corridors (was 16)
        const float StairBottomZ = 7f;     // east tunnel starts at z -7 (west at +7)

        /// <summary>Where the player starts in the ruins (south corridor, west end, facing east).</summary>
        public static Vector3 RuinsSpawn => new Vector3(-18f, 0.05f, -CorridorZ);
        public const float RuinsSpawnYaw = 90f;

        sealed class RuinMaterials
        {
            public Material Wall, Floor, Pillar, Frame, GlowPurple, GlowBlue, Lamp;
        }

        [MenuItem("DEADLOCK/Prototype/Rebuild Ruins Test Field")]
        public static void BuildRuinsFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            BuildRuinsOnly();
        }

        [MenuItem("DEADLOCK/Prototype/Open Ruins Test Field")]
        public static void OpenRuinsScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(RuinsScenePath);
        }

        /// <summary>Builds only the ruins scene (the other scenes are left as they are) and lists it in the build.</summary>
        public static void BuildRuinsOnly()
        {
            int playerLayer = EnsureLayer(PlayerLayerName, 6);
            int viewModelLayer = EnsureLayer(ViewModelLayerName, 7);
            int enemyLayer = EnsureLayer(EnemyLayerName, 8);
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(MeshFolder);
            uiIcons = UiArtSetup.Prepare();
            BuildRuinsScene(playerLayer, viewModelLayer, enemyLayer);
            AddToBuildScenes(RuinsScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DEADLOCK] Ruins test field built: {RuinsScenePath}");
        }

        static void AddToBuildScenes(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path))
                return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void BuildRuinsScene(int playerLayer, int viewModelLayer, int enemyLayer)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var mats = CreateMaterials();
            var rm = CreateRuinMaterials();
            var (weapon, melee) = LoadDefinitions();

            SetupRuinsLighting();
            BuildRuins(rm);
            // Floor map from the geometry just built (no hand-drawn image: changing a corridor re-bakes it).
            var floorMap = FloorMapBaker.Bake(RuinsFloorMapPath, new Rect(-HalfX - 0.5f, -HalfZ - 0.5f, 2f * HalfX + 1f, 2f * HalfZ + 1f), 0.25f,
                new[] { 0f, UpperY }, new[] { "1F", "2F" }, RoofY - 0.05f, 1 << LayerMask.NameToLayer("Default"));
            var projectiles = BuildProjectiles(mats, playerLayer, viewModelLayer);
            var player = BuildPlayer(weapon, melee, projectiles, playerLayer, viewModelLayer, RuinsSpawn);
            player.Root.transform.rotation = Quaternion.Euler(0f, RuinsSpawnYaw, 0f);

            var zombies = BuildRuinZombies(player, viewModelLayer, enemyLayer);
            var session = new GameObject("TrainingSession").AddComponent<TrainingSession>();
            Set(session, "player", player.Health);
            Set(session, "input", player.Input);
            SetArray(session, "zombies", zombies);
            BuildPickups(mats, new Vector3(-18f, 0f, -(RingHalf + 1f)), new Vector3(15f, UpperY, -10.5f));

            var canvas = BuildUi(player, projectiles);
            var menu = canvas.AddComponent<GameMenu>();
            Set(menu, "input", player.Input);
            Set(menu, "icons", uiIcons);
            Set(menu, "skins", skinCatalog);
            Set(menu, "session", session);
            var hud = canvas.AddComponent<TrainingHud>();
            Set(hud, "session", session);

            // Floor map (development prototype): detection on its own object, the display in the HUD canvas.
            var detector = new GameObject("MapDetection").AddComponent<EnemyDetector>();
            Set(detector, "eye", player.WorldCamera);
            Set(detector, "map", floorMap);
            SetMask(detector, "occluders", 1 << LayerMask.NameToLayer("Default"));
            var mapRect = Rect("MiniMap", canvas.transform);
            Stretch(mapRect);
            var miniMap = mapRect.gameObject.AddComponent<MiniMapView>();
            Set(miniMap, "map", floorMap);
            Set(miniMap, "detector", detector);
            Set(miniMap, "player", player.Root.transform);
            Set(miniMap, "hud", canvas.GetComponentInChildren<HudController>());
            Set(miniMap, "touchLayout", canvas.GetComponentInChildren<Deadlock.Input.TouchLayoutApplier>());
            var pause = new GameObject("OfflineSessionPause").AddComponent<OfflineSessionPause>();
            Set(pause, "menu", menu);

            EditorSceneManager.SaveScene(scene, RuinsScenePath);
        }

        static RuinMaterials CreateRuinMaterials()
        {
            RuinsArtGenerator.GenerateAll();
            return new RuinMaterials
            {
                Wall = LitMaterial("Mat_RuinWall", RuinsArtGenerator.Wall),
                Floor = LitMaterial("Mat_RuinFloor", RuinsArtGenerator.Floor),
                Pillar = LitMaterial("Mat_RuinPillar", RuinsArtGenerator.Pillar),
                // Lighter dressed stone: door frames, stair openings and step nosings read against the dark walls.
                Frame = LitMaterial("Mat_RuinFrame", RuinsArtGenerator.Frame),
                // Weak glow: unlit, so it reads in the dark without adding lights. Kept small (pillar runes only).
                GlowPurple = UnlitMaterial("Mat_RuinGlowPurple", null, new Color(0.5f, 0.32f, 0.82f)),
                GlowBlue = UnlitMaterial("Mat_RuinGlowBlue", null, new Color(0.3f, 0.5f, 0.82f)),
                // Small dim lamps beside doors and stair openings (warm, low).
                Lamp = UnlitMaterial("Mat_RuinLamp", null, new Color(0.86f, 0.62f, 0.36f)),
            };
        }

        /// <summary>
        /// Closed space, darker than the outdoor scenes but readable: blue-grey ambient, a weak shadowless fill, dark fog
        /// that starts beyond the hall, purple / blue point lights. Raised after the first device check ("a little too
        /// dark"): ambient 0.40 -> 0.52, fill 0.45 -> 0.6, fog 16-60 m -> 24-80 m, lights +15 %.
        /// </summary>
        static void SetupRuinsLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.51f, 0.60f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.07f, 0.07f, 0.10f);
            RenderSettings.fogStartDistance = 24f;
            RenderSettings.fogEndDistance = 80f;
            RenderSettings.skybox = null;

            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.66f, 0.69f, 0.86f);
            fill.intensity = 0.6f;
            fill.shadows = LightShadows.None; // the roof closes the building: no sunlight, only shading of the faces
            fill.transform.rotation = Quaternion.Euler(62f, -30f, 0f);

            var root = new GameObject("RuinLights").transform;
            var purple = new Color(0.62f, 0.36f, 1f);
            var blue = new Color(0.38f, 0.58f, 1f);
            void Point(string name, Vector3 p, Color c, float range, float intensity)
            {
                var l = new GameObject(name).AddComponent<Light>();
                l.transform.SetParent(root, false);
                l.transform.position = p;
                l.type = LightType.Point;
                l.color = c;
                l.range = range;
                l.intensity = intensity * 1.15f;
                l.shadows = LightShadows.None;
            }
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f, sz = i < 2 ? -1f : 1f;
                Point($"Hall_{i + 1}", new Vector3(6f * sx, 3.2f, 6f * sz), purple, 11f, 2.2f);
            }
            float gallery = (AtriumHalf + RingHalf) / 2f;
            Point("Gallery_N", new Vector3(0f, UpperY + 2.8f, gallery), blue, 10f, 1.8f);
            Point("Gallery_S", new Vector3(0f, UpperY + 2.8f, -gallery), blue, 10f, 1.8f);
            Point("StairE_Bottom", new Vector3(TunnelCentreX, 2.8f, -StairBottomZ - 1.6f), blue, 6f, 1.8f);
            Point("StairE_Top", new Vector3(TunnelCentreX, UpperY + 2.8f, -StairBottomZ + StairRun + 1.4f), blue, 6f, 1.8f);
            Point("StairW_Bottom", new Vector3(-TunnelCentreX, 2.8f, StairBottomZ + 1.6f), blue, 6f, 1.8f);
            Point("StairW_Top", new Vector3(-TunnelCentreX, UpperY + 2.8f, StairBottomZ - StairRun - 1.4f), blue, 6f, 1.8f);
            // Long corridors (42 m): a dim light every 20 m so enemies and doorways stay readable.
            foreach (float x in new[] { -10f, 10f })
            {
                Point($"CorridorN_{x}", new Vector3(x, 2.8f, CorridorZ), x < 0f ? purple : blue, 9f, 1.4f);
                Point($"CorridorS_{x}", new Vector3(x, 2.8f, -CorridorZ), x < 0f ? blue : purple, 9f, 1.4f);
            }
            Point("UpperCorridorN", new Vector3(0f, UpperY + 2.8f, CorridorZ), purple, 9f, 1.4f);
            Point("UpperCorridorS", new Vector3(0f, UpperY + 2.8f, -CorridorZ), purple, 9f, 1.4f);
        }

        // ------------------------------------------------------------------ structure

        /// <summary>Axis-aligned stone block given by its min / max corners, split into pieces of at most 8 m.</summary>
        static void Block(Transform root, string name, Vector3 min, Vector3 max, Material mat)
        {
            int nx = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / PieceMax - 0.001f));
            int nz = Mathf.Max(1, Mathf.CeilToInt((max.z - min.z) / PieceMax - 0.001f));
            float sx = (max.x - min.x) / nx, sz = (max.z - min.z) / nz;
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    var a = new Vector3(min.x + ix * sx, min.y, min.z + iz * sz);
                    var size = new Vector3(sx, max.y - min.y, sz);
                    string n = nx * nz == 1 ? name : $"{name}_{ix}_{iz}";
                    Box(root, n, a + size * 0.5f, size, mat);
                }
        }

        /// <summary>Visual-only box (no collider): trims, frames, lamps, glow.</summary>
        static void Trim(Transform root, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = Box(root, name, center, size, mat);
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void Glow(Transform root, string name, Vector3 center, Vector3 size, Material mat) => Trim(root, name, center, size, mat);

        struct Door
        {
            public float Center, Width, Height;
            public bool Lamps;
            public Door(float center, float width, float height, bool lamps = false) { Center = center; Width = width; Height = height; Lamps = lamps; }
        }

        /// <summary>
        /// Frame of lighter dressed stone around a door (jambs inside the opening's edges and a lintel band, standing
        /// slightly proud of both wall faces; the opening keeps its width) and optional small lamps beside it.
        /// <paramref name="along"/> is the wall direction, <paramref name="across"/> its normal.
        /// </summary>
        static void DoorFrame(Transform root, string name, Vector3 doorBottomCentre, Vector3 along, Vector3 across, Door d, RuinMaterials m)
        {
            const float jamb = 0.25f, band = 0.25f;
            float depth = WallThickness + 0.12f;
            Vector3 S(float a, float h, float c) => new Vector3(Mathf.Abs(along.x) * a + Mathf.Abs(across.x) * c, h, Mathf.Abs(along.z) * a + Mathf.Abs(across.z) * c);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 c = doorBottomCentre + along * side * (d.Width / 2f + jamb / 2f) + Vector3.up * (d.Height / 2f);
                Trim(root, $"{name}_Jamb{(side < 0 ? "L" : "R")}", c, S(jamb, d.Height, depth), m.Frame);
                if (d.Lamps)
                    foreach (float face in new[] { -1f, 1f })
                        Trim(root, $"{name}_Lamp{(side < 0 ? "L" : "R")}{(face < 0 ? "a" : "b")}",
                            doorBottomCentre + along * side * (d.Width / 2f + jamb + 0.25f) + Vector3.up * 2.3f + across * face * (WallThickness / 2f + 0.06f),
                            S(0.16f, 0.26f, 0.1f), m.Lamp);
            }
            Trim(root, $"{name}_Lintel", doorBottomCentre + Vector3.up * (d.Height + band / 2f), S(d.Width + 2f * jamb, band, depth), m.Frame);
        }

        /// <summary>Wall along x (at z), from x0 to x1, between y0 and y1, with framed door openings and lintels.</summary>
        static void WallX(Transform root, string name, float z, float x0, float x1, float y0, float y1, RuinMaterials m, params Door[] doors)
        {
            float t = WallThickness;
            float cursor = x0;
            int i = 0;
            foreach (var d in doors.OrderBy(d => d.Center))
            {
                float a = d.Center - d.Width / 2f, b = d.Center + d.Width / 2f;
                if (a > cursor)
                    Block(root, $"{name}_{i++}", new Vector3(cursor, y0, z - t / 2f), new Vector3(a, y1, z + t / 2f), m.Wall);
                if (y0 + d.Height < y1)
                    Block(root, $"{name}_Lintel_{i++}", new Vector3(a, y0 + d.Height, z - t / 2f), new Vector3(b, y1, z + t / 2f), m.Wall);
                DoorFrame(root, $"{name}_Door{i}", new Vector3(d.Center, y0, z), Vector3.right, Vector3.forward, d, m);
                cursor = b;
            }
            if (x1 > cursor)
                Block(root, $"{name}_{i}", new Vector3(cursor, y0, z - t / 2f), new Vector3(x1, y1, z + t / 2f), m.Wall);
        }

        /// <summary>Wall along z (at x), from z0 to z1, between y0 and y1, with framed door openings and lintels.</summary>
        static void WallZ(Transform root, string name, float x, float z0, float z1, float y0, float y1, RuinMaterials m, params Door[] doors)
        {
            float t = WallThickness;
            float cursor = z0;
            int i = 0;
            foreach (var d in doors.OrderBy(d => d.Center))
            {
                float a = d.Center - d.Width / 2f, b = d.Center + d.Width / 2f;
                if (a > cursor)
                    Block(root, $"{name}_{i++}", new Vector3(x - t / 2f, y0, cursor), new Vector3(x + t / 2f, y1, a), m.Wall);
                if (y0 + d.Height < y1)
                    Block(root, $"{name}_Lintel_{i++}", new Vector3(x - t / 2f, y0 + d.Height, a), new Vector3(x + t / 2f, y1, b), m.Wall);
                DoorFrame(root, $"{name}_Door{i}", new Vector3(x, y0, d.Center), Vector3.forward, Vector3.right, d, m);
                cursor = b;
            }
            if (z1 > cursor)
                Block(root, $"{name}_{i}", new Vector3(x - t / 2f, y0, cursor), new Vector3(x + t / 2f, y1, z1), m.Wall);
        }

        static void BuildRuins(RuinMaterials m)
        {
            var root = new GameObject("Ruins").transform;
            var shell = new GameObject("Shell").transform;
            shell.SetParent(root, false);

            // Floor, roof and outer walls: a closed box (no sky anywhere, the atrium included).
            Block(shell, "GroundFloor", new Vector3(-HalfX - 0.5f, -0.5f, -HalfZ - 0.5f), new Vector3(HalfX + 0.5f, 0f, HalfZ + 0.5f), m.Floor);
            Block(shell, "Roof", new Vector3(-HalfX - 0.5f, RoofY, -HalfZ - 0.5f), new Vector3(HalfX + 0.5f, RoofY + 0.5f, HalfZ + 0.5f), m.Wall);
            Block(shell, "OuterWall_E", new Vector3(HalfX, -0.5f, -HalfZ - 0.5f), new Vector3(HalfX + 0.5f, RoofY + 0.5f, HalfZ + 0.5f), m.Wall);
            Block(shell, "OuterWall_W", new Vector3(-HalfX - 0.5f, -0.5f, -HalfZ - 0.5f), new Vector3(-HalfX, RoofY + 0.5f, HalfZ + 0.5f), m.Wall);
            Block(shell, "OuterWall_N", new Vector3(-HalfX, -0.5f, HalfZ), new Vector3(HalfX, RoofY + 0.5f, HalfZ + 0.5f), m.Wall);
            Block(shell, "OuterWall_S", new Vector3(-HalfX, -0.5f, -HalfZ - 0.5f), new Vector3(HalfX, RoofY + 0.5f, -HalfZ), m.Wall);

            // Upper floor slab around the atrium, with openings over the two stair tunnels.
            var upper = new GameObject("UpperFloor").transform;
            upper.SetParent(root, false);
            void Slab(string n, float x0, float x1, float z0, float z1) =>
                Block(upper, n, new Vector3(x0, SlabBottom, z0), new Vector3(x1, UpperY, z1), m.Floor);
            float top = -StairBottomZ + StairRun; // 1.66
            Slab("Slab_N", -HalfX, HalfX, AtriumHalf, HalfZ);
            Slab("Slab_S", -HalfX, HalfX, -HalfZ, -AtriumHalf);
            Slab("Slab_E", AtriumHalf, InnerWallEdge, -AtriumHalf, AtriumHalf);
            Slab("Slab_E_SouthOfStair", InnerWallEdge, HalfX, -AtriumHalf, -StairBottomZ - 0.3f);
            Slab("Slab_E_NorthOfStair", InnerWallEdge, HalfX, top, AtriumHalf);
            Slab("Slab_W", -InnerWallEdge, -AtriumHalf, -AtriumHalf, AtriumHalf);
            Slab("Slab_W_NorthOfStair", -HalfX, -InnerWallEdge, StairBottomZ + 0.3f, AtriumHalf);
            Slab("Slab_W_SouthOfStair", -HalfX, -InnerWallEdge, -AtriumHalf, -top);

            // Walls: the same plan on both floors (ground y 0..4.6 under the slab, upper y 5..9 under the roof).
            var walls = new GameObject("Walls").transform;
            walls.SetParent(root, false);
            foreach (var (tag, y0, y1) in new[] { ("G", 0f, SlabBottom), ("U", UpperY, RoofY) })
            {
                var main = new Door(0f, DoorWide, DoorHeight, lamps: true);
                Door Narrow(float c) => new Door(c, DoorNarrow, DoorHeight);
                WallX(walls, $"Ring_N_{tag}", RingHalf, -HalfX, HalfX, y0, y1, m, main, Narrow(-WingDoorX), Narrow(WingDoorX));
                WallX(walls, $"Ring_S_{tag}", -RingHalf, -HalfX, HalfX, y0, y1, m, main, Narrow(-WingDoorX), Narrow(WingDoorX));
                WallZ(walls, $"Ring_E_{tag}", RingHalf, -RingHalf, RingHalf, y0, y1, m, main, Narrow(8f), Narrow(-9.5f));
                WallZ(walls, $"Ring_W_{tag}", -RingHalf, -RingHalf, RingHalf, y0, y1, m, main, Narrow(-8f), Narrow(9.5f));
                // East wing: room north (z 4..ring) and room south (ring..-7) off the corridor beside the stair tunnel.
                WallX(walls, $"EastRoomN_{tag}", 4f, RingHalf, HalfX, y0, y1, m, Narrow(WingCorridorX), Narrow(TunnelCentreX));
                WallX(walls, $"EastRoomS_{tag}", -StairBottomZ, RingHalf, InnerWallEdge, y0, y1, m, Narrow(WingCorridorX));
                // West wing: mirrored.
                WallX(walls, $"WestRoomS_{tag}", -4f, -HalfX, -RingHalf, y0, y1, m, Narrow(-WingCorridorX), Narrow(-TunnelCentreX));
                WallX(walls, $"WestRoomN_{tag}", StairBottomZ, -InnerWallEdge, -RingHalf, y0, y1, m, Narrow(-WingCorridorX));
            }

            // Atrium: worn pillars at its edge (ground colonnade, gallery posts), a parapet on the gallery edge.
            var hall = new GameObject("Atrium").transform;
            hall.SetParent(root, false);
            var pillars = new List<Vector2>();
            foreach (float a in new[] { -8f, -4f, 0f, 4f, 8f })
            {
                pillars.Add(new Vector2(a, AtriumHalf));
                pillars.Add(new Vector2(a, -AtriumHalf));
                if (Mathf.Abs(a) < AtriumHalf)
                {
                    pillars.Add(new Vector2(AtriumHalf, a));
                    pillars.Add(new Vector2(-AtriumHalf, a));
                }
            }
            for (int i = 0; i < pillars.Count; i++)
            {
                var p = pillars[i];
                Box(hall, $"Pillar_{i + 1}", new Vector3(p.x, RoofY / 2f, p.y), new Vector3(1.2f, RoofY, 1.2f), m.Pillar);
                // Lighter plinth at the foot (reads the pillar base and the floor edge), faint rune above it.
                Trim(hall, $"PillarBase_{i + 1}", new Vector3(p.x, 0.2f, p.y), new Vector3(1.4f, 0.4f, 1.4f), m.Frame);
                Glow(hall, $"PillarRune_{i + 1}", new Vector3(p.x, 0.75f, p.y), new Vector3(1.24f, 0.08f, 1.24f), i % 2 == 0 ? m.GlowPurple : m.GlowBlue);
            }
            // Parapet 1.3 m high on the gallery edge (a standing jump lifts the feet 1.1 m: it cannot be cleared). Unchanged.
            const float parapet = 1.3f;
            Block(hall, "Parapet_N", new Vector3(-AtriumHalf, UpperY, AtriumHalf), new Vector3(AtriumHalf, UpperY + parapet, AtriumHalf + 0.3f), m.Wall);
            Block(hall, "Parapet_S", new Vector3(-AtriumHalf, UpperY, -AtriumHalf - 0.3f), new Vector3(AtriumHalf, UpperY + parapet, -AtriumHalf), m.Wall);
            Block(hall, "Parapet_E", new Vector3(AtriumHalf, UpperY, -AtriumHalf), new Vector3(AtriumHalf + 0.3f, UpperY + parapet, AtriumHalf), m.Wall);
            Block(hall, "Parapet_W", new Vector3(-AtriumHalf - 0.3f, UpperY, -AtriumHalf), new Vector3(-AtriumHalf, UpperY + parapet, AtriumHalf), m.Wall);
            // Lighter coping on the parapet top (the gallery edge reads from below and above).
            Trim(hall, "ParapetCap_N", new Vector3(0f, UpperY + parapet + 0.04f, AtriumHalf + 0.15f), new Vector3(2f * AtriumHalf + 0.6f, 0.08f, 0.42f), m.Frame);
            Trim(hall, "ParapetCap_S", new Vector3(0f, UpperY + parapet + 0.04f, -AtriumHalf - 0.15f), new Vector3(2f * AtriumHalf + 0.6f, 0.08f, 0.42f), m.Frame);
            Trim(hall, "ParapetCap_E", new Vector3(AtriumHalf + 0.15f, UpperY + parapet + 0.04f, 0f), new Vector3(0.42f, 0.08f, 2f * AtriumHalf), m.Frame);
            Trim(hall, "ParapetCap_W", new Vector3(-AtriumHalf - 0.15f, UpperY + parapet + 0.04f, 0f), new Vector3(0.42f, 0.08f, 2f * AtriumHalf), m.Frame);
            // Hall: a low dais (lighter edge: a 0.3 m step) with an altar (cover) and a faint crystal, fallen pillar drums.
            Box(hall, "Dais", new Vector3(0f, 0.15f, 0f), new Vector3(5f, 0.3f, 5f), m.Floor);
            Trim(hall, "DaisEdge", new Vector3(0f, 0.27f, 0f), new Vector3(5.06f, 0.06f, 5.06f), m.Frame);
            Box(hall, "Altar", new Vector3(0f, 0.9f, 0f), new Vector3(1.4f, 1.2f, 1f), m.Pillar);
            Glow(hall, "Crystal", new Vector3(0f, 1.8f, 0f), new Vector3(0.35f, 0.6f, 0.35f), m.GlowPurple);
            Box(hall, "FallenDrum_A", new Vector3(-4.5f, 0.5f, 2.5f), new Vector3(2.4f, 1f, 1f), m.Pillar);
            Box(hall, "FallenDrum_B", new Vector3(4f, 0.5f, -3.5f), new Vector3(1f, 1f, 2.4f), m.Pillar);
            // Cover in the rooms and corridors (both floors).
            Box(hall, "Block_GroundN", new Vector3(6f, 0.5f, CorridorZ), new Vector3(1.2f, 1f, 1.2f), m.Pillar);
            Box(hall, "Block_GroundWestRoom", new Vector3(-17f, 0.5f, 9.8f), new Vector3(1.2f, 1f, 1.2f), m.Pillar);
            Box(hall, "Block_UpperS", new Vector3(-5f, UpperY + 0.5f, -CorridorZ), new Vector3(1.2f, 1f, 1.2f), m.Pillar);
            Box(hall, "Block_UpperEastRoom", new Vector3(16.5f, UpperY + 0.5f, 8.8f), new Vector3(1.2f, 1f, 1.2f), m.Pillar);
            Box(hall, "Block_UpperWestRoom", new Vector3(-18f, UpperY + 0.5f, -10.8f), new Vector3(1.2f, 1f, 1.2f), m.Pillar);

            BuildStairTunnel(root, m, "StairE", TunnelInner, HalfX, -StairBottomZ, +1f);
            BuildStairTunnel(root, m, "StairW", -HalfX, -TunnelInner, StairBottomZ, -1f);

            var surface = root.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << LayerMask.NameToLayer("Default");
            surface.BuildNavMesh();
            AssetDatabase.DeleteAsset(RuinsNavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, RuinsNavMeshPath);
        }

        /// <summary>
        /// A walled, roofed stair tunnel between x0 and x1 (one side is the outer wall): a 30 degree ramp from bottomZ
        /// (ground) rising toward dir (+1 = +z, -1 = -z) to the upper floor; treads with lighter nosings drawn on the ramp
        /// (no collision), an inner wall full height, end walls that close the space under the ramp and above the
        /// openings, a ceiling parallel to the ramp, and lighter stone frames with small lamps at both openings.
        /// </summary>
        static void BuildStairTunnel(Transform parent, RuinMaterials m, string name, float x0, float x1, float bottomZ, float dir)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            float width = x1 - x0, cx = (x0 + x1) / 2f;
            float topZ = bottomZ + dir * StairRun;
            float length = Mathf.Sqrt(StairRun * StairRun + StairRise * StairRise);
            var up = new Vector3(0f, StairRise, dir * StairRun).normalized;      // along the ramp, uphill
            var normal = Vector3.Cross(up, dir > 0f ? Vector3.right : Vector3.left).normalized;
            if (normal.y < 0f)
                normal = -normal;
            var rot = Quaternion.LookRotation(up, normal);
            var midSurface = new Vector3(cx, StairRise / 2f, (bottomZ + topZ) / 2f);

            // Ramp: collision only (the treads below are the visible stairs).
            const float rampThickness = 0.4f;
            var ramp = new GameObject("Ramp");
            ramp.transform.SetParent(root, false);
            ramp.transform.SetPositionAndRotation(midSurface - normal * (rampThickness / 2f), rot);
            ramp.isStatic = true;
            ramp.AddComponent<BoxCollider>().size = new Vector3(width, rampThickness, length);

            // Treads: 25 steps of 0.2 m; the ramp line passes through the middle of each tread (feet within 0.1 m of it).
            // Each step has a lighter nosing on its front edge so the steps read in the dark.
            const int steps = 25;
            float rise = StairRise / steps, run = StairRun / steps;
            for (int i = 0; i < steps; i++)
            {
                float top = rise * (i + 0.5f);
                float zc = bottomZ + dir * run * (i + 0.5f);
                Trim(root, $"Step_{i + 1:00}", new Vector3(cx, top - 0.225f, zc), new Vector3(width, 0.45f, run), m.Floor);
                Trim(root, $"Nosing_{i + 1:00}", new Vector3(cx, top - 0.025f, zc - dir * (run / 2f - 0.04f)), new Vector3(width, 0.06f, 0.09f), m.Frame);
            }

            // Ceiling parallel to the ramp.
            var ceiling = Box(root, "Ceiling", midSurface + Vector3.up * StairCeiling + normal * 0.2f, new Vector3(width, 0.4f, length + 0.6f), m.Wall);
            ceiling.transform.rotation = rot;

            // Inner wall full height (the other side is the outer wall).
            float innerX = x0 > 0f ? x0 - 0.15f : x1 + 0.15f;
            float zMin = Mathf.Min(bottomZ, topZ), zMax = Mathf.Max(bottomZ, topZ);
            Block(root, "InnerWall", new Vector3(innerX - 0.15f, 0f, zMin), new Vector3(innerX + 0.15f, RoofY, zMax), m.Wall);
            float wx0 = Mathf.Min(innerX - 0.15f, x0), wx1 = Mathf.Max(innerX + 0.15f, x1);
            // Bottom end: closed above the entrance (the upper floor must not see into the shaft).
            float bz = bottomZ - dir * 0.3f;
            Block(root, "EndWall_Bottom", new Vector3(wx0, StairCeiling, Mathf.Min(bz, bottomZ)), new Vector3(wx1, RoofY, Mathf.Max(bz, bottomZ)), m.Wall);
            // Top end: closed under the ramp (no space below the stairs) and above the exit.
            float tz = topZ + dir * 0.3f;
            Block(root, "EndWall_Top", new Vector3(wx0, 0f, Mathf.Min(tz, topZ)), new Vector3(wx1, UpperY - 0.01f, Mathf.Max(tz, topZ)), m.Wall);
            Block(root, "Lintel_Top", new Vector3(wx0, UpperY + StairCeiling, Mathf.Min(tz, topZ)), new Vector3(wx1, RoofY, Mathf.Max(tz, topZ)), m.Wall);

            // Lighter stone frames around both openings and small lamps beside them (the way up / down reads from afar).
            void OpeningFrame(string n, float y, float z, float outward)
            {
                // Jambs over the end of the inner wall and against the outer wall, so the opening keeps its full width.
                const float jamb = 0.3f;
                float innerJambX = x0 > 0f ? x0 - jamb / 2f : x1 + jamb / 2f;
                float outerJambX = x0 > 0f ? x1 - jamb / 2f : x0 + jamb / 2f;
                Trim(root, $"{n}_JambInner", new Vector3(innerJambX, y + StairCeiling / 2f, z + outward * 0.06f), new Vector3(jamb, StairCeiling, 0.14f), m.Frame);
                Trim(root, $"{n}_JambOuter", new Vector3(outerJambX, y + StairCeiling / 2f, z + outward * 0.06f), new Vector3(jamb, StairCeiling, 0.14f), m.Frame);
                Trim(root, $"{n}_Lintel", new Vector3(cx, y + StairCeiling - 0.15f, z + outward * 0.06f), new Vector3(width, 0.3f, 0.14f), m.Frame);
                Trim(root, $"{n}_Lamp", new Vector3(innerJambX, y + 2.3f, z + outward * 0.18f), new Vector3(0.16f, 0.26f, 0.1f), m.Lamp);
            }
            OpeningFrame("Entrance", 0f, bottomZ - dir * 0.31f, -dir);
            OpeningFrame("Exit", UpperY, topZ + dir * 0.31f, dir);
        }

        static Zombie[] BuildRuinZombies(PlayerParts player, int viewModelLayer, int enemyLayer)
        {
            var root = new GameObject("Zombies").transform;
            int sight = ~((1 << enemyLayer) | (1 << viewModelLayer) | (1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << LayerMask.NameToLayer("UI")));
            // Away from the start (south-west corridor): hall, east room, north corridor on the ground; gallery and the
            // west upper room above (they come down the stairs).
            float gallery = (AtriumHalf + RingHalf) / 2f;
            var spawns = new[]
            {
                new Vector3(3f, 0f, 5f), new Vector3(17f, 0f, 9.5f), new Vector3(-8f, 0f, CorridorZ),
                new Vector3(-3f, UpperY, gallery), new Vector3(-16f, UpperY, -9.5f),
            };
            var zombies = new Zombie[spawns.Length];
            for (int i = 0; i < spawns.Length; i++)
            {
                var (go, zombie, view) = CreateZombieObject($"Zombie_{i + 1}", enemyLayer, sight);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(spawns[i], Quaternion.Euler(0f, 180f, 0f));
                Set(zombie, "target", player.Health);
                var mapEnemy = go.AddComponent<MapEnemy>(); // seen / heard on the floor map
                Set(mapEnemy, "zombie", zombie);
                Set(mapEnemy, "view", view);
                zombies[i] = zombie;
            }
            return zombies;
        }
    }
}
