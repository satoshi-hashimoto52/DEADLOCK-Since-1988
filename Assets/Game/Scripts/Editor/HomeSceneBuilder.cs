using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deadlock.Home;
using Deadlock.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.Editor
{
    /// <summary>
    /// Builds the home scene (Assets/Game/Scenes/Home.unity) from Assets/Game/Art/Home/home_layout.json and the
    /// registered home images, and makes it the first scene of the build (the other scenes keep their order).
    /// Each image is a sprite at 1 px per unit with its pivot at the top left, placed at its reference position
    /// (y flipped), unscaled; one camera frames them all (<see cref="HomeBackgroundView"/>). The images are only
    /// imported (sprite settings), never changed. Re-running it regenerates the scene; the lighting settings asset
    /// keeps its values once created.
    /// Menu: DEADLOCK/Home/Rebuild Home Scene. Batch: -batchmode -quit -executeMethod Deadlock.Editor.HomeSceneBuilder.Build
    /// </summary>
    public static class HomeSceneBuilder
    {
        public const string ScenePath = "Assets/Game/Scenes/Home.unity";
        public const string LayoutPath = "Assets/Game/Art/Home/home_layout.json";
        public const string LightingPath = "Assets/Game/Data/HomeLighting.asset";
        const string MaterialPath = "Assets/Game/Materials/HomeSpriteUnlit.mat";

        [System.Serializable]
        class Layout
        {
            public int[] reference;
            public Layer[] layers;
        }

        [System.Serializable]
        class Layer
        {
            public string id, path, parent, filter, role;
            public string[] frames;
            public int[] position;
            public int sortingOrder;
        }

        [MenuItem("DEADLOCK/Home/Rebuild Home Scene")]
        public static void Build()
        {
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            foreach (var layer in layout.layers)
                foreach (var path in Paths(layer))
                    ImportSprite(path, layer.filter == "bilinear");

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                // Unlit sprites: the home art is drawn exactly as painted (no scene lights).
                material = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            var lighting = AssetDatabase.LoadAssetAtPath<HomeLightingSettings>(LightingPath);
            if (lighting == null)
            {
                lighting = ScriptableObject.CreateInstance<HomeLightingSettings>();
                AssetDatabase.CreateAsset(lighting, LightingPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var home = new GameObject("Home").AddComponent<HomeScreen>();

            var camGo = new GameObject("HomeCamera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black; // Fit: the area outside the reference
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;
            camGo.transform.position = HomeFraming.CameraPosition(-10f);
            camGo.AddComponent<HomeBackgroundView>();

            var root = new GameObject("HomeBackground").transform;
            var objects = new Dictionary<string, Transform>();
            HomeCandleAnimator candle = null;
            foreach (var layer in layout.layers)
            {
                var go = new GameObject(layer.id);
                var parent = layer.parent != null && objects.TryGetValue(layer.parent, out var p) ? p : root;
                go.transform.SetParent(parent, false);
                go.transform.position = HomeFraming.ToWorld(new Vector2(layer.position[0], layer.position[1]));
                objects[layer.id] = go.transform;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sharedMaterial = material;
                sr.sortingOrder = layer.sortingOrder;
                var sprites = Paths(layer).Select(AssetDatabase.LoadAssetAtPath<Sprite>).ToArray();
                sr.sprite = layer.role == "glow" ? sprites[1] : sprites[0];
                if (string.IsNullOrEmpty(layer.role))
                    continue;
                if (candle == null)
                {
                    candle = objects["home.prop.candle"].gameObject.AddComponent<HomeCandleAnimator>();
                    candle.settings = lighting;
                }
                switch (layer.role)
                {
                    case "flame": candle.flameRenderer = sr; candle.flameFrames = sprites; break;
                    case "light": candle.lightRenderer = sr; candle.lightFrames = sprites; break;
                    case "glow":
                        candle.glowRenderer = sr;
                        sprites[0] = null; // glow 00 is never shown (01 -> 01 -> 02 -> 03)
                        candle.glowFrames = sprites;
                        break;
                }
            }

            // Screen-space UI: only the safe-area root for the future home menu, and the interim hint.
            var canvasGo = new GameObject("HomeUI", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(HomeFraming.RefWidth, HomeFraming.RefHeight);
            scaler.matchWidthOrHeight = 1f;
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", canvasGo.transform));
            safe.gameObject.AddComponent<HomeSafeAreaPanel>();
            UiBuild.Stretch(UiBuild.Rect("MenuRoot", safe)); // home menu UI goes here (next step)
            var hint = UiBuild.Text(UiBuild.Place(UiBuild.Rect("InterimContinueHint", safe), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(700f, 40f)),
                "画面をタップして射撃場へ", 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f));
            hint.raycastTarget = false;

            EditorSceneManager.SaveScene(scene, ScenePath);
            ListFirstInBuild();
            AssetDatabase.SaveAssets();
            Debug.Log($"[DEADLOCK] Home scene built: {ScenePath}");
        }

        static IEnumerable<string> Paths(Layer layer) => layer.frames != null && layer.frames.Length > 0 ? layer.frames : new[] { layer.path };

        /// <summary>Home first, the other scenes after it in their current order.</summary>
        public static void ListFirstInBuild()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void ImportSprite(string path, bool bilinear)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null)
                throw new FileNotFoundException("home image missing (register it through AssetPipeline first)", path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 1f;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.TopLeft;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(settings);
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 2048;
            ti.alphaIsTransparency = true;
            ti.sRGBTexture = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = bilinear ? FilterMode.Bilinear : FilterMode.Point;
            ti.SaveAndReimport();
        }
    }
}
