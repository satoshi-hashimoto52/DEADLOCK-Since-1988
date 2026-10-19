using System.Collections.Generic;
using System.IO;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using Deadlock.UI;
using Deadlock.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Deadlock.Editor
{
    /// <summary>
    /// Builds the prototype firing-range scene and its supporting assets from code, through the Unity Editor API.
    /// Re-running it regenerates the scene. Menu: DEADLOCK/Prototype/Rebuild Firing Range.
    /// Batch: -batchmode -quit -executeMethod Deadlock.Editor.PrototypeSceneBuilder.Build
    /// (use -quit rather than exiting from code so the Editor saves ProjectSettings on shutdown).
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        public const string ScenePath = "Assets/Game/Scenes/Proto_FiringRange.unity";
        public const string PlayerLayerName = "Player";
        public const string ViewModelLayerName = "ViewModel";

        const string MaterialFolder = "Assets/Game/Materials";
        const string MeshFolder = "Assets/Game/Generated/Meshes";
        const string DataFolder = "Assets/Game/Data";
        const string WeaponPath = DataFolder + "/Weapon_ProtoRifle.asset";
        const float TextureWorldSize = 2f; // one 32 px tile per 2 m => 16 px / m

        static readonly Color SkyColor = new Color(0.16f, 0.17f, 0.21f);

        [MenuItem("DEADLOCK/Prototype/Rebuild Firing Range")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Build();
        }

        public static void Build()
        {
            int playerLayer = EnsureLayer(PlayerLayerName, 6);
            int viewModelLayer = EnsureLayer(ViewModelLayerName, 7);

            PlaceholderArtGenerator.GenerateAll();
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(MeshFolder);
            Directory.CreateDirectory(DataFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

            // Open the new scene before loading assets: NewScene unloads unreferenced assets,
            // which would leave dangling references to anything loaded earlier.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var mats = new Materials
            {
                Wall = LitMaterial("Mat_WallBrick", PlaceholderArtGenerator.WallBrick),
                Floor = LitMaterial("Mat_FloorTile", PlaceholderArtGenerator.FloorTile),
                Crate = LitMaterial("Mat_Crate", PlaceholderArtGenerator.Crate),
                Concrete = LitMaterial("Mat_Concrete", PlaceholderArtGenerator.Concrete),
                Ammo = LitMaterial("Mat_AmmoBox", PlaceholderArtGenerator.AmmoBox),
                ImpactWall = UnlitMaterial("Mat_ImpactWall", PlaceholderArtGenerator.ImpactWall, Color.white),
                ImpactTarget = UnlitMaterial("Mat_ImpactTarget", PlaceholderArtGenerator.ImpactTarget, Color.white),
                Tracer = UnlitMaterial("Mat_Tracer", null, new Color(1f, 0.85f, 0.4f)),
            };
            WeaponDefinition weapon = LoadOrCreateWeapon();

            SetupLighting();

            BuildEnvironment(mats);
            var projectiles = BuildProjectiles(mats, playerLayer, viewModelLayer);
            var player = BuildPlayer(weapon, projectiles, playerLayer, viewModelLayer);
            BuildTargets();
            BuildPickups(mats);
            BuildUi(player, projectiles);

            EditorSceneManager.SaveScene(scene, ScenePath);
            ConfigureProject();
            AssetDatabase.SaveAssets();
            Debug.Log($"[DEADLOCK] Prototype scene built: {ScenePath}");
        }

        // ------------------------------------------------------------------ project / assets

        static int EnsureLayer(string name, int preferredIndex)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name)
                    return i;

            int index = string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferredIndex).stringValue) ? preferredIndex : -1;
            for (int i = 8; index < 0 && i < layers.arraySize; i++)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                    index = i;
            if (index < 0)
                throw new System.InvalidOperationException($"No free layer slot for '{name}'.");

            layers.GetArrayElementAtIndex(index).stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            return index;
        }

        static void ConfigureProject()
        {
            // Prototype: landscape only (screen orientation is still a proposal in GAME_SPEC).
            // Edited through the serialized asset: the static PlayerSettings setters are not persisted in batch mode.
            var playerSettingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0];
            var playerSettings = new SerializedObject(playerSettingsAsset);
            playerSettings.FindProperty("defaultScreenOrientation").intValue = (int)UIOrientation.AutoRotation;
            playerSettings.FindProperty("allowedAutorotateToPortrait").boolValue = false;
            playerSettings.FindProperty("allowedAutorotateToPortraitUpsideDown").boolValue = false;
            playerSettings.FindProperty("allowedAutorotateToLandscapeLeft").boolValue = true;
            playerSettings.FindProperty("allowedAutorotateToLandscapeRight").boolValue = true;
            playerSettings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(playerSettingsAsset);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            // The template sample scene is replaced by the prototype scene.
            // (Assets/Settings/SampleSceneProfile.asset stays: PC_RPAsset uses it as its volume profile.)
            AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity");
            if (AssetDatabase.IsValidFolder("Assets/Scenes") && AssetDatabase.FindAssets("", new[] { "Assets/Scenes" }).Length == 0)
                AssetDatabase.DeleteAsset("Assets/Scenes");
        }

        static WeaponDefinition LoadOrCreateWeapon()
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
            if (weapon != null)
                return weapon; // keep hand-tuned values
            weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            AssetDatabase.CreateAsset(weapon, WeaponPath);
            return weapon;
        }

        static Material LitMaterial(string name, string texturePath)
        {
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Simple Lit");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetColor("_SpecColor", Color.black);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material UnlitMaterial(string name, string texturePath, Color color)
        {
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            if (texturePath != null)
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreateMaterial(string name, string shaderName)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var shader = Shader.Find(shaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }
            return mat;
        }

        // ------------------------------------------------------------------ scene content

        sealed class Materials
        {
            public Material Wall, Floor, Crate, Concrete, Ammo, ImpactWall, ImpactTarget, Tracer;
        }

        sealed class PlayerRig
        {
            public GameObject Root;
            public PlayerInputRouter Input;
            public GunController Gun;
            public Camera WorldCamera;
        }

        static void SetupLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.46f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyColor;
            RenderSettings.fogStartDistance = 22f;
            RenderSettings.fogEndDistance = 80f;
            RenderSettings.skybox = null;

            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Hard;
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        }

        static void BuildEnvironment(Materials m)
        {
            var root = new GameObject("Environment").transform;
            // Range: x -8..8, z -6..54
            Box(root, "Floor", new Vector3(0f, -0.25f, 24f), new Vector3(16f, 0.5f, 60f), m.Floor);
            Box(root, "Wall_Left", new Vector3(-8.25f, 2f, 24f), new Vector3(0.5f, 4f, 60.5f), m.Wall);
            Box(root, "Wall_Right", new Vector3(8.25f, 2f, 24f), new Vector3(0.5f, 4f, 60.5f), m.Wall);
            Box(root, "Wall_Back", new Vector3(0f, 2f, -6.25f), new Vector3(17f, 4f, 0.5f), m.Wall);
            Box(root, "Wall_End", new Vector3(0f, 2f, 54.25f), new Vector3(17f, 4f, 0.5f), m.Wall);

            Box(root, "Crate_A", new Vector3(-3f, 0.5f, 7f), new Vector3(1f, 1f, 1f), m.Crate);
            Box(root, "Crate_B", new Vector3(-3.9f, 0.5f, 7.6f), new Vector3(1f, 1f, 1f), m.Crate);
            Box(root, "Crate_C", new Vector3(-3.4f, 1.5f, 7.3f), new Vector3(1f, 1f, 1f), m.Crate);
            Box(root, "Platform_Jump", new Vector3(5.5f, 0.4f, 5f), new Vector3(2f, 0.8f, 2f), m.Concrete);
            Box(root, "LowWall", new Vector3(0.5f, 0.55f, 16f), new Vector3(4f, 1.1f, 0.4f), m.Concrete);
            Box(root, "Pillar_A", new Vector3(-5f, 2f, 22f), new Vector3(1f, 4f, 1f), m.Concrete);
            Box(root, "Pillar_B", new Vector3(5f, 2f, 31f), new Vector3(1f, 4f, 1f), m.Concrete);
            // Fully hides Target_Hidden from the start position: shots must stop here.
            Box(root, "OccluderWall", new Vector3(-2f, 1.75f, 26f), new Vector3(4f, 3.5f, 0.5f), m.Wall);
            Box(root, "Crate_D", new Vector3(3f, 0.5f, 40f), new Vector3(1f, 1f, 1f), m.Crate);
        }

        static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = WorldUvBox(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>Box mesh whose UVs are in world units so every surface keeps the same texel density.</summary>
        static Mesh WorldUvBox(Vector3 size)
        {
            string path = $"{MeshFolder}/Box_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
                return existing;

            Vector3 h = size * 0.5f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            void Face(Vector3 normal, Vector3 uAxis, Vector3 vAxis, float uSize, float vSize)
            {
                int start = vertices.Count;
                Vector3 center = Vector3.Scale(normal, h);
                for (int i = 0; i < 4; i++)
                {
                    float su = i == 1 || i == 2 ? 1f : -1f;
                    float sv = i >= 2 ? 1f : -1f;
                    vertices.Add(center + uAxis * (su * uSize * 0.5f) + vAxis * (sv * vSize * 0.5f));
                    normals.Add(normal);
                    uvs.Add(new Vector2((su + 1f) * 0.5f * uSize, (sv + 1f) * 0.5f * vSize) / TextureWorldSize);
                }
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
            Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);

            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static ProjectileSystem BuildProjectiles(Materials m, int playerLayer, int viewModelLayer)
        {
            var go = new GameObject("Projectiles");
            var system = go.AddComponent<ProjectileSystem>();
            int ignore = (1 << playerLayer) | (1 << viewModelLayer) | (1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << LayerMask.NameToLayer("UI"));
            system.HitMask = ~ignore;

            var visuals = go.AddComponent<ProjectileVisuals>();
            Set(visuals, "projectiles", system);
            Set(visuals, "tracerMaterial", m.Tracer);
            Set(visuals, "impactMaterial", m.ImpactWall);
            Set(visuals, "targetImpactMaterial", m.ImpactTarget);
            return system;
        }

        static PlayerRig BuildPlayer(WeaponDefinition weapon, ProjectileSystem projectiles, int playerLayer, int viewModelLayer)
        {
            var root = new GameObject("Player") { layer = playerLayer };
            root.transform.position = new Vector3(0f, 0.05f, 0f);
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.35f;
            controller.skinWidth = 0.04f;
            controller.minMoveDistance = 0f; // small per-frame steps at high frame rates must not be dropped

            var router = root.AddComponent<PlayerInputRouter>();
            var motor = root.AddComponent<FirstPersonMotor>();
            var gun = root.AddComponent<GunController>();

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            // World camera (base): everything except the view model.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(pivot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = weapon.hipFov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyColor;
            cam.cullingMask = ~(1 << viewModelLayer);
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderType = CameraRenderType.Base;

            // View-model camera (overlay): only the first-person weapon, depth cleared, fixed FOV.
            var vmGo = new GameObject("ViewModelCamera");
            vmGo.transform.SetParent(camGo.transform, false);
            var vmCam = vmGo.AddComponent<Camera>();
            vmCam.fieldOfView = 50f;
            vmCam.nearClipPlane = 0.01f;
            vmCam.farClipPlane = 5f;
            vmCam.cullingMask = 1 << viewModelLayer;
            var vmData = vmCam.GetUniversalAdditionalCameraData();
            vmData.renderType = CameraRenderType.Overlay;
            vmData.renderShadows = false;
            camData.cameraStack.Add(vmCam);

            var view = BuildWeaponView(vmGo.transform, viewModelLayer);
            var viewModel = view.AddComponent<WeaponViewModel>();
            Set(viewModel, "gun", gun);
            Set(viewModel, "motor", motor);
            Set(viewModel, "worldCamera", cam);
            Set(viewModel, "hipSprite", view.transform.Find("Hip").GetComponent<SpriteRenderer>());
            Set(viewModel, "adsSprite", view.transform.Find("Ads").GetComponent<SpriteRenderer>());
            Set(viewModel, "hipMuzzleFlash", view.transform.Find("Hip/MuzzleFlash").GetComponent<SpriteRenderer>());
            Set(viewModel, "adsMuzzleFlash", view.transform.Find("Ads/MuzzleFlash").GetComponent<SpriteRenderer>());

            Set(motor, "input", router);
            Set(motor, "cameraPivot", pivot.transform);
            Set(gun, "definition", weapon);
            Set(gun, "input", router);
            Set(gun, "motor", motor);
            Set(gun, "aimOrigin", camGo.transform);
            Set(gun, "projectiles", projectiles);

            return new PlayerRig { Root = root, Input = router, Gun = gun, WorldCamera = cam };
        }

        static GameObject BuildWeaponView(Transform parent, int layer)
        {
            var view = new GameObject("WeaponView") { layer = layer };
            view.transform.SetParent(parent, false);

            var hip = SpriteChild(view.transform, "Hip", PlaceholderArtGenerator.WeaponHip, layer, 0);
            var hipFlash = SpriteChild(hip.transform, "MuzzleFlash", PlaceholderArtGenerator.MuzzleFlash, layer, 1);
            Vector2 muzzle = PlaceholderArtGenerator.HipMuzzlePixel;
            hipFlash.transform.localPosition = new Vector3((muzzle.x - 512f) / PlaceholderArtGenerator.HipPixelsPerUnit,
                (256f - muzzle.y) / PlaceholderArtGenerator.HipPixelsPerUnit, -0.01f);
            hipFlash.transform.localScale = Vector3.one * 0.6f;
            hipFlash.GetComponent<SpriteRenderer>().enabled = false;

            var ads = SpriteChild(view.transform, "Ads", PlaceholderArtGenerator.WeaponAds, layer, 0);
            var adsFlash = SpriteChild(ads.transform, "MuzzleFlash", PlaceholderArtGenerator.MuzzleFlash, layer, -1);
            adsFlash.transform.localPosition = new Vector3(0f, 0.012f, 0.01f);
            adsFlash.transform.localScale = Vector3.one * 0.5f;
            adsFlash.GetComponent<SpriteRenderer>().enabled = false;
            ads.GetComponent<SpriteRenderer>().enabled = false;
            return view;
        }

        static GameObject SpriteChild(Transform parent, string name, string spritePath, int layer, int sortingOrder)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            sr.sortingOrder = sortingOrder;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            return go;
        }

        static void BuildTargets()
        {
            var root = new GameObject("Targets").transform;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderArtGenerator.TargetSprite);
            var positions = new (string name, Vector3 pos)[]
            {
                ("Target_Near", new Vector3(-4f, 0f, 13f)),
                ("Target_Mid", new Vector3(2.5f, 0f, 21f)),
                ("Target_Hidden", new Vector3(-2f, 0f, 29f)),
                ("Target_Far", new Vector3(4f, 0f, 37f)),
                ("Target_End_A", new Vector3(0f, 0f, 48f)),
                ("Target_End_B", new Vector3(-5f, 0f, 45f)),
            };
            foreach (var (name, pos) in positions)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.position = pos;
                var col = go.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.95f, 0f);
                col.size = new Vector3(0.8f, 1.9f, 0.3f);
                var target = go.AddComponent<ShootingTarget>();

                var visual = new GameObject("Sprite");
                visual.transform.SetParent(go.transform, false);
                visual.transform.localPosition = new Vector3(0f, 1f, 0f);
                var sr = visual.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.shadowCastingMode = ShadowCastingMode.Off;
                visual.AddComponent<Billboard>();
                Set(target, "sprite", sr);
            }
        }

        static void BuildPickups(Materials m)
        {
            var root = new GameObject("Pickups").transform;
            foreach (var (name, pos) in new[] { ("AmmoPickup_A", new Vector3(2f, 0f, 4f)), ("AmmoPickup_B", new Vector3(-6f, 0f, 19f)) })
            {
                var go = new GameObject(name) { layer = LayerMask.NameToLayer("Ignore Raycast") };
                go.transform.SetParent(root, false);
                go.transform.position = pos;
                var trigger = go.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, 0.6f, 0f);
                trigger.size = new Vector3(1.2f, 1.2f, 1.2f);
                var pickup = go.AddComponent<AmmoPickup>();

                var visual = new GameObject("Visual");
                visual.transform.SetParent(go.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                visual.AddComponent<MeshFilter>().sharedMesh = WorldUvBox(new Vector3(0.6f, 0.4f, 0.4f));
                visual.AddComponent<MeshRenderer>().sharedMaterial = m.Ammo;
                Set(pickup, "visual", visual.transform);
            }
        }

        // ------------------------------------------------------------------ UI

        static void BuildUi(PlayerRig player, ProjectileSystem projectiles)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // landscape: scale by height
            canvasGo.AddComponent<GraphicRaycaster>();
            var canvasRect = (RectTransform)canvasGo.transform;

            BuildTouchControls(canvasRect, player);
            BuildHud(canvasRect, player, projectiles);
        }

        static void BuildTouchControls(RectTransform canvas, PlayerRig player)
        {
            var root = Rect("TouchControls", canvas);
            Stretch(root);
            var controls = root.gameObject.AddComponent<TouchControls>();

            var look = Rect("LookArea", root);
            Stretch(look);
            AddImage(look, null, new Color(0f, 0f, 0f, 0f), raycast: true);
            var lookArea = look.gameObject.AddComponent<TouchLookArea>();

            var safe = Rect("SafeArea", root);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // Left 40% of the screen: floating move stick.
            var zone = Rect("MoveZone", safe);
            zone.anchorMin = new Vector2(0f, 0f);
            zone.anchorMax = new Vector2(0.4f, 0.8f);
            zone.pivot = Vector2.zero;
            zone.offsetMin = zone.offsetMax = Vector2.zero;
            AddImage(zone, null, new Color(0f, 0f, 0f, 0f), raycast: true);
            var stickBase = Rect("StickBase", zone);
            stickBase.anchorMin = stickBase.anchorMax = Vector2.zero;
            stickBase.sizeDelta = new Vector2(280f, 280f);
            stickBase.localPosition = new Vector3(260f, 250f, 0f);
            AddImage(stickBase, PlaceholderArtGenerator.UiRing, new Color(1f, 1f, 1f, 0.35f), raycast: false);
            var knob = Rect("Knob", stickBase);
            knob.sizeDelta = new Vector2(130f, 130f);
            AddImage(knob, PlaceholderArtGenerator.UiCircle, new Color(1f, 1f, 1f, 0.5f), raycast: false);
            var stick = zone.gameObject.AddComponent<VirtualJoystick>();
            Set(stick, "stickBase", stickBase);
            Set(stick, "knob", knob);

            var fire = Button(safe, "FireButton", "FIRE", new Vector2(-250f, 260f), 240f, lookArea);
            var ads = Button(safe, "AdsButton", "ADS", new Vector2(-520f, 170f), 160f, null);
            var reload = Button(safe, "ReloadButton", "R", new Vector2(-170f, 540f), 140f, null);
            var jump = Button(safe, "JumpButton", "JUMP", new Vector2(-470f, 430f), 140f, null);

            Set(controls, "moveStick", stick);
            Set(controls, "lookArea", lookArea);
            Set(controls, "fireButton", fire);
            Set(controls, "adsButton", ads);
            Set(controls, "reloadButton", reload);
            Set(controls, "jumpButton", jump);
            Set(player.Input, "touchControls", controls);
        }

        static TouchButton Button(RectTransform parent, string name, string label, Vector2 fromBottomRight, float size, TouchLookArea dragLooksWith)
        {
            var rect = Rect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = fromBottomRight;
            var image = AddImage(rect, PlaceholderArtGenerator.UiCircle, new Color(1f, 1f, 1f, 0.35f), raycast: true);
            var text = AddText(Rect("Label", rect), label, Mathf.RoundToInt(size * 0.2f), TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);
            var button = rect.gameObject.AddComponent<TouchButton>();
            Set(button, "graphic", image);
            Set(button, "dragLooksWith", dragLooksWith);
            return button;
        }

        static void BuildHud(RectTransform canvas, PlayerRig player, ProjectileSystem projectiles)
        {
            var root = Rect("HUD", canvas);
            Stretch(root);
            var safe = Rect("SafeArea", root);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var crosshair = Rect("Crosshair", safe);
            crosshair.anchorMin = crosshair.anchorMax = new Vector2(0.5f, 0.5f);
            crosshair.sizeDelta = Vector2.zero;
            var group = crosshair.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var lines = new RectTransform[4];
            var lineSizes = new[] { new Vector2(4f, 18f), new Vector2(4f, 18f), new Vector2(18f, 4f), new Vector2(18f, 4f) };
            for (int i = 0; i < 4; i++)
            {
                lines[i] = Rect($"Line{i}", crosshair);
                lines[i].sizeDelta = lineSizes[i];
                // pivot on the inner end so the gap is measured from the centre
                lines[i].pivot = i switch { 0 => new Vector2(0.5f, 0f), 1 => new Vector2(0.5f, 1f), 2 => new Vector2(1f, 0.5f), _ => new Vector2(0f, 0.5f) };
                AddImage(lines[i], null, new Color(1f, 1f, 1f, 0.9f), raycast: false);
            }
            var dot = Rect("Dot", crosshair);
            dot.sizeDelta = new Vector2(4f, 4f);
            AddImage(dot, null, new Color(1f, 1f, 1f, 0.9f), raycast: false);

            var hitMarker = Rect("HitMarker", safe);
            hitMarker.anchorMin = hitMarker.anchorMax = new Vector2(0.5f, 0.5f);
            hitMarker.sizeDelta = new Vector2(56f, 56f);
            var hitImage = AddImage(hitMarker, PlaceholderArtGenerator.UiHitMarker, Color.white, raycast: false);
            hitImage.enabled = false;

            var ammo = AddText(Rect("AmmoText", safe), "30 / 90", 64, TextAnchor.UpperRight);
            var ammoRect = (RectTransform)ammo.transform;
            ammoRect.anchorMin = ammoRect.anchorMax = ammoRect.pivot = new Vector2(1f, 1f);
            ammoRect.anchoredPosition = new Vector2(-48f, -36f);
            ammoRect.sizeDelta = new Vector2(420f, 90f);

            var status = AddText(Rect("StatusText", safe), "", 40, TextAnchor.MiddleCenter);
            var statusRect = (RectTransform)status.transform;
            statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 0.5f);
            statusRect.anchoredPosition = new Vector2(0f, -120f);
            statusRect.sizeDelta = new Vector2(600f, 60f);

            var barBack = Rect("ReloadBarBack", safe);
            barBack.anchorMin = barBack.anchorMax = new Vector2(0.5f, 0.5f);
            barBack.anchoredPosition = new Vector2(0f, -165f);
            barBack.sizeDelta = new Vector2(320f, 12f);
            var bar = Rect("ReloadBar", barBack);
            Stretch(bar);
            var barImage = AddImage(bar, PlaceholderArtGenerator.UiWhite, new Color(1f, 0.85f, 0.4f, 0.9f), raycast: false);
            barImage.type = Image.Type.Filled;
            barImage.fillMethod = Image.FillMethod.Horizontal;
            barImage.enabled = false;

            var help = AddText(Rect("HelpText", safe), "", 24, TextAnchor.UpperLeft);
            var helpRect = (RectTransform)help.transform;
            helpRect.anchorMin = helpRect.anchorMax = helpRect.pivot = new Vector2(0f, 1f);
            helpRect.anchoredPosition = new Vector2(32f, -24f);
            helpRect.sizeDelta = new Vector2(1100f, 80f);
            help.color = new Color(1f, 1f, 1f, 0.6f);

            var hud = root.gameObject.AddComponent<HudController>();
            Set(hud, "gun", player.Gun);
            Set(hud, "projectiles", projectiles);
            Set(hud, "input", player.Input);
            Set(hud, "worldCamera", player.WorldCamera);
            Set(hud, "canvasRect", canvas);
            SetArray(hud, "crosshairLines", lines);
            Set(hud, "crosshairGroup", group);
            Set(hud, "ammoText", ammo);
            Set(hud, "statusText", status);
            Set(hud, "reloadBar", barImage);
            Set(hud, "hitMarker", hitImage);
            Set(hud, "helpText", help);
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static Image AddImage(RectTransform rect, string spritePath, Color color, bool raycast)
        {
            var image = rect.gameObject.AddComponent<Image>();
            if (spritePath != null)
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        static Text AddText(RectTransform rect, string content, int fontSize, TextAnchor alignment)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        // ------------------------------------------------------------------ serialized field helpers

        static void Set(Object target, string field, Object value)
        {
            if (value is not null && value == null)
                throw new System.InvalidOperationException($"{target.GetType().Name}.{field}: the assigned object was destroyed.");
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
                throw new System.ArgumentException($"{target.GetType().Name} has no serialized field '{field}'.");
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (prop.objectReferenceValue != value)
                throw new System.InvalidOperationException($"Could not assign {value} to {target.GetType().Name}.{field}.");
        }

        static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
