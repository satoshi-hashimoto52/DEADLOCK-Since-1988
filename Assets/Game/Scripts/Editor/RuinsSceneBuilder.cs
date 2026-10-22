using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.UI;
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
    /// Plan (metres; x east, z north; ground floor y 0, upper floor y 5, roof 9):
    /// building x -20..20, z -16..16; atrium x -8..8, z -8..8 open to the roof; ring aisle / gallery to |x|, |z| = 12;
    /// north / south corridors z 12..16 / -16..-12; east / west wings |x| 12..20 with the stair tunnels along the outer
    /// walls (east: x 17..20, up toward +z from z -7 to 1.66; west: x -20..-17, up toward -z from z 7 to -1.66).
    /// Stairs are 30 degree ramps for collision (the crouch / prone / slide step heights are 0.05-0.1 m, so real steps
    /// would block them) with step treads drawn on top; the tunnels have walls and a sloped ceiling 3.6 m above.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        public const string RuinsScenePath = "Assets/Game/Scenes/Proto_Ruins.unity";
        const string RuinsNavMeshPath = "Assets/Game/Generated/NavMesh_Ruins.asset";

        const float UpperY = 5f;          // upper floor walking surface
        const float SlabBottom = 4.6f;    // underside of the upper floor slab = ground floor ceiling
        const float RoofY = 9f;           // underside of the roof
        const float StairRise = UpperY;
        static readonly float StairRun = UpperY / Mathf.Tan(30f * Mathf.Deg2Rad); // 8.66 m
        const float StairCeiling = 3.6f;  // vertical clearance above the ramp
        const float PieceMax = 8f;        // floors / walls are split into pieces of at most 8 m (lights per object: 4)

        /// <summary>Where the player starts in the ruins (south corridor, west end, facing east).</summary>
        public static readonly Vector3 RuinsSpawn = new Vector3(-17f, 0.05f, -14f);
        public const float RuinsSpawnYaw = 90f;

        sealed class RuinMaterials
        {
            public Material Wall, Floor, Pillar, GlowPurple, GlowBlue;
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
            var projectiles = BuildProjectiles(mats, playerLayer, viewModelLayer);
            var player = BuildPlayer(weapon, melee, projectiles, playerLayer, viewModelLayer, RuinsSpawn);
            player.Root.transform.rotation = Quaternion.Euler(0f, RuinsSpawnYaw, 0f);

            var zombies = BuildRuinZombies(player, viewModelLayer, enemyLayer);
            var session = new GameObject("TrainingSession").AddComponent<TrainingSession>();
            Set(session, "player", player.Health);
            Set(session, "input", player.Input);
            SetArray(session, "zombies", zombies);
            BuildPickups(mats, new Vector3(-17f, 0f, -12.9f), new Vector3(14f, UpperY, -10f));

            var canvas = BuildUi(player, projectiles);
            var menu = canvas.AddComponent<GameMenu>();
            Set(menu, "input", player.Input);
            Set(menu, "icons", uiIcons);
            Set(menu, "skins", skinCatalog);
            Set(menu, "session", session);
            var hud = canvas.AddComponent<TrainingHud>();
            Set(hud, "session", session);
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
                // Weak glow: unlit, so it reads in the dark without adding lights.
                GlowPurple = UnlitMaterial("Mat_RuinGlowPurple", null, new Color(0.62f, 0.38f, 1f)),
                GlowBlue = UnlitMaterial("Mat_RuinGlowBlue", null, new Color(0.36f, 0.62f, 1f)),
            };
        }

        /// <summary>Dark, closed space: low blue-grey ambient, a weak shadowless fill, dark fog, purple / blue point lights.</summary>
        static void SetupRuinsLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.40f, 0.39f, 0.48f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.04f, 0.04f, 0.07f);
            RenderSettings.fogStartDistance = 16f;
            RenderSettings.fogEndDistance = 60f;
            RenderSettings.skybox = null;

            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.62f, 0.66f, 0.85f);
            fill.intensity = 0.45f;
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
                l.intensity = intensity;
                l.shadows = LightShadows.None;
            }
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f, sz = i < 2 ? -1f : 1f;
                Point($"Hall_{i + 1}", new Vector3(6f * sx, 3.2f, 6f * sz), purple, 11f, 2.2f);
            }
            Point("Gallery_N", new Vector3(0f, UpperY + 2.8f, 10f), blue, 10f, 1.8f);
            Point("Gallery_S", new Vector3(0f, UpperY + 2.8f, -10f), blue, 10f, 1.8f);
            Point("StairE_Bottom", new Vector3(18.5f, 2.8f, -8.6f), blue, 6f, 1.8f);
            Point("StairE_Top", new Vector3(18.5f, UpperY + 2.8f, 3f), blue, 6f, 1.8f);
            Point("StairW_Bottom", new Vector3(-18.5f, 2.8f, 8.6f), blue, 6f, 1.8f);
            Point("StairW_Top", new Vector3(-18.5f, UpperY + 2.8f, -3f), blue, 6f, 1.8f);
            // Long corridors (40 m): a dim light every 20 m so enemies and doorways stay readable.
            foreach (float x in new[] { -10f, 10f })
            {
                Point($"CorridorN_{x}", new Vector3(x, 2.8f, 14f), x < 0f ? purple : blue, 9f, 1.4f);
                Point($"CorridorS_{x}", new Vector3(x, 2.8f, -14f), x < 0f ? blue : purple, 9f, 1.4f);
            }
            Point("UpperCorridorN", new Vector3(0f, UpperY + 2.8f, 14f), purple, 9f, 1.4f);
            Point("UpperCorridorS", new Vector3(0f, UpperY + 2.8f, -14f), purple, 9f, 1.4f);
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

        struct Door
        {
            public float Center, Width, Height;
            public Door(float center, float width, float height) { Center = center; Width = width; Height = height; }
        }

        /// <summary>Wall along x (at z), from x0 to x1, between y0 and y1, with door openings and lintels.</summary>
        static void WallX(Transform root, string name, float z, float x0, float x1, float y0, float y1, Material mat, params Door[] doors)
        {
            float t = 0.4f;
            float cursor = x0;
            int i = 0;
            foreach (var d in doors.OrderBy(d => d.Center))
            {
                float a = d.Center - d.Width / 2f, b = d.Center + d.Width / 2f;
                if (a > cursor)
                    Block(root, $"{name}_{i++}", new Vector3(cursor, y0, z - t / 2f), new Vector3(a, y1, z + t / 2f), mat);
                if (y0 + d.Height < y1)
                    Block(root, $"{name}_Lintel_{i++}", new Vector3(a, y0 + d.Height, z - t / 2f), new Vector3(b, y1, z + t / 2f), mat);
                cursor = b;
            }
            if (x1 > cursor)
                Block(root, $"{name}_{i}", new Vector3(cursor, y0, z - t / 2f), new Vector3(x1, y1, z + t / 2f), mat);
        }

        /// <summary>Wall along z (at x), from z0 to z1, between y0 and y1, with door openings and lintels.</summary>
        static void WallZ(Transform root, string name, float x, float z0, float z1, float y0, float y1, Material mat, params Door[] doors)
        {
            float t = 0.4f;
            float cursor = z0;
            int i = 0;
            foreach (var d in doors.OrderBy(d => d.Center))
            {
                float a = d.Center - d.Width / 2f, b = d.Center + d.Width / 2f;
                if (a > cursor)
                    Block(root, $"{name}_{i++}", new Vector3(x - t / 2f, y0, cursor), new Vector3(x + t / 2f, y1, a), mat);
                if (y0 + d.Height < y1)
                    Block(root, $"{name}_Lintel_{i++}", new Vector3(x - t / 2f, y0 + d.Height, a), new Vector3(x + t / 2f, y1, b), mat);
                cursor = b;
            }
            if (z1 > cursor)
                Block(root, $"{name}_{i}", new Vector3(x - t / 2f, y0, cursor), new Vector3(x + t / 2f, y1, z1), mat);
        }

        /// <summary>Thin unlit strip (weak glow); no collider, so it never blocks movement or shots.</summary>
        static void Glow(Transform root, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = Box(root, name, center, size, mat);
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void BuildRuins(RuinMaterials m)
        {
            var root = new GameObject("Ruins").transform;
            var shell = new GameObject("Shell").transform;
            shell.SetParent(root, false);

            // Floor, roof and outer walls: a closed box (no sky anywhere, the atrium included).
            Block(shell, "GroundFloor", new Vector3(-20.5f, -0.5f, -16.5f), new Vector3(20.5f, 0f, 16.5f), m.Floor);
            Block(shell, "Roof", new Vector3(-20.5f, RoofY, -16.5f), new Vector3(20.5f, RoofY + 0.5f, 16.5f), m.Wall);
            Block(shell, "OuterWall_E", new Vector3(20f, -0.5f, -16.5f), new Vector3(20.5f, RoofY + 0.5f, 16.5f), m.Wall);
            Block(shell, "OuterWall_W", new Vector3(-20.5f, -0.5f, -16.5f), new Vector3(-20f, RoofY + 0.5f, 16.5f), m.Wall);
            Block(shell, "OuterWall_N", new Vector3(-20f, -0.5f, 16f), new Vector3(20f, RoofY + 0.5f, 16.5f), m.Wall);
            Block(shell, "OuterWall_S", new Vector3(-20f, -0.5f, -16.5f), new Vector3(20f, RoofY + 0.5f, -16f), m.Wall);

            // Upper floor slab around the atrium, with openings over the two stair tunnels.
            var upper = new GameObject("UpperFloor").transform;
            upper.SetParent(root, false);
            void Slab(string n, float x0, float x1, float z0, float z1) =>
                Block(upper, n, new Vector3(x0, SlabBottom, z0), new Vector3(x1, UpperY, z1), m.Floor);
            Slab("Slab_N", -20f, 20f, 8f, 16f);
            Slab("Slab_S", -20f, 20f, -16f, -8f);
            Slab("Slab_E", 8f, 16.7f, -8f, 8f);
            Slab("Slab_E_SouthOfStair", 16.7f, 20f, -8f, -7.3f);
            Slab("Slab_E_NorthOfStair", 16.7f, 20f, 1.66f, 8f);
            Slab("Slab_W", -16.7f, -8f, -8f, 8f);
            Slab("Slab_W_NorthOfStair", -20f, -16.7f, 7.3f, 8f);
            Slab("Slab_W_SouthOfStair", -20f, -16.7f, -8f, -1.66f);

            // Walls: the same plan on both floors (ground y 0..4.6 under the slab, upper y 5..9 under the roof).
            var walls = new GameObject("Walls").transform;
            walls.SetParent(root, false);
            foreach (var (tag, y0, y1, dh) in new[] { ("G", 0f, SlabBottom, 3.0f), ("U", UpperY, RoofY, 3.0f) })
            {
                WallX(walls, $"Ring_N_{tag}", 12f, -20f, 20f, y0, y1, m.Wall, new Door(0f, 3f, dh), new Door(-16f, 2.4f, dh), new Door(16f, 2.4f, dh));
                WallX(walls, $"Ring_S_{tag}", -12f, -20f, 20f, y0, y1, m.Wall, new Door(0f, 3f, dh), new Door(-16f, 2.4f, dh), new Door(16f, 2.4f, dh));
                WallZ(walls, $"Ring_E_{tag}", 12f, -12f, 12f, y0, y1, m.Wall, new Door(0f, 3f, dh), new Door(8f, 2.4f, dh), new Door(-9.5f, 2.4f, dh));
                WallZ(walls, $"Ring_W_{tag}", -12f, -12f, 12f, y0, y1, m.Wall, new Door(0f, 3f, dh), new Door(-8f, 2.4f, dh), new Door(9.5f, 2.4f, dh));
                // East wing: room north (z 4..12) and room south (z -12..-7) off the corridor beside the stair tunnel.
                WallX(walls, $"EastRoomN_{tag}", 4f, 12f, 20f, y0, y1, m.Wall, new Door(14.4f, 2.4f, dh), new Door(18.5f, 2.4f, dh));
                WallX(walls, $"EastRoomS_{tag}", -7f, 12f, 16.7f, y0, y1, m.Wall, new Door(14.4f, 2.4f, dh));
                // West wing: mirrored.
                WallX(walls, $"WestRoomS_{tag}", -4f, -20f, -12f, y0, y1, m.Wall, new Door(-14.4f, 2.4f, dh), new Door(-18.5f, 2.4f, dh));
                WallX(walls, $"WestRoomN_{tag}", 7f, -16.7f, -12f, y0, y1, m.Wall, new Door(-14.4f, 2.4f, dh));
            }

            // Atrium: worn pillars at its edge (ground colonnade, gallery posts), a parapet on the gallery edge.
            var hall = new GameObject("Atrium").transform;
            hall.SetParent(root, false);
            var pillars = new List<Vector2>();
            foreach (float a in new[] { -8f, -4f, 0f, 4f, 8f })
            {
                pillars.Add(new Vector2(a, 8f));
                pillars.Add(new Vector2(a, -8f));
                if (Mathf.Abs(a) < 8f)
                {
                    pillars.Add(new Vector2(8f, a));
                    pillars.Add(new Vector2(-8f, a));
                }
            }
            for (int i = 0; i < pillars.Count; i++)
            {
                var p = pillars[i];
                Box(hall, $"Pillar_{i + 1}", new Vector3(p.x, RoofY / 2f, p.y), new Vector3(1.2f, RoofY, 1.2f), m.Pillar);
                Glow(hall, $"PillarRune_{i + 1}", new Vector3(p.x, 0.7f, p.y), new Vector3(1.26f, 0.12f, 1.26f), i % 2 == 0 ? m.GlowPurple : m.GlowBlue);
            }
            // Parapet 1.3 m high on the gallery edge (a standing jump lifts the feet 1.1 m: it cannot be cleared).
            const float parapet = 1.3f;
            Block(hall, "Parapet_N", new Vector3(-8f, UpperY, 8f), new Vector3(8f, UpperY + parapet, 8.3f), m.Wall);
            Block(hall, "Parapet_S", new Vector3(-8f, UpperY, -8.3f), new Vector3(8f, UpperY + parapet, -8f), m.Wall);
            Block(hall, "Parapet_E", new Vector3(8f, UpperY, -8f), new Vector3(8.3f, UpperY + parapet, 8f), m.Wall);
            Block(hall, "Parapet_W", new Vector3(-8.3f, UpperY, -8f), new Vector3(-8f, UpperY + parapet, 8f), m.Wall);
            // Hall: a low dais with an altar (cover) and a faint crystal, fallen pillar drums as cover.
            Box(hall, "Dais", new Vector3(0f, 0.15f, 0f), new Vector3(5f, 0.3f, 5f), m.Floor);
            Box(hall, "Altar", new Vector3(0f, 0.9f, 0f), new Vector3(1.4f, 1.2f, 1f), m.Pillar);
            Glow(hall, "Crystal", new Vector3(0f, 1.8f, 0f), new Vector3(0.35f, 0.6f, 0.35f), m.GlowPurple);
            Box(hall, "FallenDrum_A", new Vector3(-4.5f, 0.5f, 2.5f), new Vector3(2.4f, 1f, 1f), m.Pillar);
            Box(hall, "FallenDrum_B", new Vector3(4f, 0.5f, -3.5f), new Vector3(1f, 1f, 2.4f), m.Pillar);
            // Cover in the rooms and corridors (both floors).
            Box(hall, "Block_GroundN", new Vector3(6f, 0.5f, 14f), new Vector3(1.2f, 1f, 1.2f), m.Pillar);
            Box(hall, "Block_GroundWestRoom", new Vector3(-16f, 0.5f, 9.5f), new Vector3(1.2f, 1f, 1.2f), m.Pillar);
            Box(hall, "Block_UpperS", new Vector3(-5f, UpperY + 0.5f, -14f), new Vector3(1.2f, 1f, 1.2f), m.Pillar);
            Box(hall, "Block_UpperEastRoom", new Vector3(15f, UpperY + 0.5f, 8.5f), new Vector3(1.2f, 1f, 1.2f), m.Pillar);
            Box(hall, "Block_UpperWestRoom", new Vector3(-16.5f, UpperY + 0.5f, -10.5f), new Vector3(1.2f, 1f, 1.2f), m.Pillar);
            // Faint runes on the walls next to the main doors.
            foreach (float s in new[] { -1f, 1f })
            {
                Glow(hall, $"Rune_RingN_{s}", new Vector3(2.2f * s, 2.4f, 11.78f), new Vector3(0.5f, 0.5f, 0.04f), m.GlowBlue);
                Glow(hall, $"Rune_RingS_{s}", new Vector3(2.2f * s, 2.4f, -11.78f), new Vector3(0.5f, 0.5f, 0.04f), m.GlowBlue);
            }

            BuildStairTunnel(root, m, "StairE", 17f, 20f, -7f, +1f);
            BuildStairTunnel(root, m, "StairW", -20f, -17f, 7f, -1f);

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
        /// (ground) rising toward dir (+1 = +z, -1 = -z) to the upper floor; treads drawn on the ramp (no collision),
        /// an inner wall full height, end walls that close the space under the ramp and above the openings, and a
        /// ceiling parallel to the ramp.
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
            const int steps = 25;
            float rise = StairRise / steps, run = StairRun / steps;
            for (int i = 0; i < steps; i++)
            {
                float top = rise * (i + 0.5f);
                float zc = bottomZ + dir * run * (i + 0.5f);
                var tread = Box(root, $"Step_{i + 1:00}", new Vector3(cx, top - 0.225f, zc), new Vector3(width, 0.45f, run), m.Floor);
                Object.DestroyImmediate(tread.GetComponent<BoxCollider>());
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
            // Glow strips over both openings, so the exits read in the dark.
            Glow(root, "Glow_Entrance", new Vector3(cx, StairCeiling - 0.1f, bottomZ - dir * 0.34f), new Vector3(width, 0.12f, 0.04f), m.GlowBlue);
            Glow(root, "Glow_Exit", new Vector3(cx, UpperY + StairCeiling - 0.1f, topZ + dir * 0.34f), new Vector3(width, 0.12f, 0.04f), m.GlowBlue);
        }

        static Zombie[] BuildRuinZombies(PlayerParts player, int viewModelLayer, int enemyLayer)
        {
            var root = new GameObject("Zombies").transform;
            int sight = ~((1 << enemyLayer) | (1 << viewModelLayer) | (1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << LayerMask.NameToLayer("UI")));
            // Away from the start (south-west corridor): hall, east room, north corridor on the ground; gallery and the
            // west upper room above (they come down the stairs).
            var spawns = new[]
            {
                new Vector3(3f, 0f, 5f), new Vector3(16f, 0f, 9f), new Vector3(-8f, 0f, 14f),
                new Vector3(-3f, UpperY, 10f), new Vector3(-15f, UpperY, -8f),
            };
            var zombies = new Zombie[spawns.Length];
            for (int i = 0; i < spawns.Length; i++)
            {
                var (go, zombie, _) = CreateZombieObject($"Zombie_{i + 1}", enemyLayer, sight);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(spawns[i], Quaternion.Euler(0f, 180f, 0f));
                Set(zombie, "target", player.Health);
                zombies[i] = zombie;
            }
            return zombies;
        }
    }
}
