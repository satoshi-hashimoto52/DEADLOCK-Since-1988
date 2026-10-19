using System.Collections.Generic;
using System.IO;
using Deadlock.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Deadlock.Editor
{
    /// <summary>
    /// Pre-renders the placeholder first-person rifle (hip and ADS) from a simple 3D model built out of primitives,
    /// then turns the renders into crisp sprites: hard light bands, dark silhouette lines between parts and darker
    /// crease lines on hard edges. Hip and ADS come from the same model and light, so they read as the same gun and
    /// keep correct perspective (stock near and large, muzzle far and small).
    ///
    /// The renders use the same vertical FOV (50°) as the in-game view-model camera, so a sprite placed at z = 1 with
    /// its pivot at the projected muzzle / front sight reproduces the rendered perspective.
    /// Output (generated, overwritten on every bake): Assets/Game/Art/Generated/Spr_WeaponHip.png, Spr_WeaponAds.png
    /// and the baked fields of Assets/Game/Data/WeaponView_ProtoRifle.asset.
    /// </summary>
    public static class WeaponSpriteBaker
    {
        public const string ProfilePath = "Assets/Game/Data/WeaponView_ProtoRifle.asset";
        const string WeaponPath = "Assets/Game/Data/Weapon_ProtoRifle.asset";
        const string ShaderName = "Hidden/Deadlock/WeaponBake";
        const float Fov = 50f;
        const int FrameHeight = 1080; // ~1:1 on a 1080p screen
        const int FrameWidth = 1920;

        // Gun-local reference points (metres): +Z forward, +Y up, bore on y = 0.
        static readonly Vector3 MuzzleTip = new Vector3(0f, 0f, 0.735f);
        static readonly Vector3 FrontSightTip = new Vector3(0f, 0.090f, 0.42f);
        static readonly Vector3 RearSightCentre = new Vector3(0f, 0.090f, -0.12f);

        // Hip hold relative to the camera, and ADS eye relief behind the rear aperture.
        static readonly Vector3 HipGunPosition = new Vector3(0.17f, -0.19f, 0.35f);
        static readonly Vector3 HipGunEuler = new Vector3(-8f, -5f, -4f);
        const float AdsEyeRelief = 0.15f;

        // Toward the light: upper right, behind the player. Same light for hip and ADS.
        static readonly Vector3 TowardLight = new Vector3(0.3f, 0.9f, -0.3f);

        static readonly Color Metal = new Color(0.46f, 0.48f, 0.52f);
        static readonly Color MetalDark = new Color(0.27f, 0.28f, 0.31f);
        static readonly Color Polymer = new Color(0.36f, 0.32f, 0.26f);
        static readonly Color Glove = new Color(0.44f, 0.33f, 0.23f);
        static readonly Color GlovePad = new Color(0.26f, 0.20f, 0.15f);
        static readonly Color Stitch = new Color(0.78f, 0.66f, 0.46f);
        static readonly Color Sleeve = new Color(0.38f, 0.42f, 0.26f);
        static readonly Color Outline = new Color(0.035f, 0.03f, 0.03f);

        [MenuItem("DEADLOCK/Prototype/Bake Weapon Sprites")]
        public static void BakeFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var previous = EditorSceneManager.GetActiveScene().path;
            Bake();
            if (!string.IsNullOrEmpty(previous))
                EditorSceneManager.OpenScene(previous);
        }

        /// <summary>Batch: -batchmode -quit -executeMethod Deadlock.Editor.WeaponSpriteBaker.BakeFromCommandLine</summary>
        public static void BakeFromCommandLine() => Bake();

        /// <summary>Bakes into a fresh empty scene (the caller is responsible for the scene that was open).</summary>
        public static WeaponViewProfile Bake()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new System.InvalidOperationException($"Shader '{ShaderName}' not found.");
            var material = new Material(shader);

            var parts = new List<(Renderer renderer, Color color, float spec)>();
            Transform gun = BuildRifle(material, parts);

            var cameraGo = new GameObject("BakeCamera");
            var cam = cameraGo.AddComponent<Camera>();
            cam.fieldOfView = Fov;
            cam.aspect = (float)FrameWidth / FrameHeight; // fixed: projections are computed after the target texture is released
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            Shader.SetGlobalVector("_BakeLightDir", TowardLight.normalized);

            // Hip: camera at the origin, gun held lower right, angled toward the centre of the screen.
            cameraGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            gun.SetPositionAndRotation(HipGunPosition, Quaternion.Euler(HipGunEuler));
            var hip = RenderSprite(cam, parts, gun.TransformPoint(MuzzleTip), out _);

            // ADS: eye on the sight line behind the rear aperture, looking at the front sight tip.
            // Parts behind the aperture ("Rear*") would fill the frame right in front of the eye and are hidden.
            gun.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var part in parts)
                if (part.renderer.name.StartsWith("Rear") && part.renderer.name != "RearSightBase" && part.renderer.name != "RearSightPost")
                    part.renderer.enabled = false;
            Vector3 eye = RearSightCentre - Vector3.forward * AdsEyeRelief;
            cameraGo.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(FrontSightTip - eye, Vector3.up));
            var ads = RenderSprite(cam, parts, FrontSightTip, out Vector2 adsMuzzleOffset, MuzzleTip);

            Object.DestroyImmediate(material);
            Directory.CreateDirectory(PlaceholderArtGenerator.Folder);
            Sprite hipSprite = SaveSprite(PlaceholderArtGenerator.WeaponHip, hip);
            Sprite adsSprite = SaveSprite(PlaceholderArtGenerator.WeaponAds, ads);

            var profile = AssetDatabase.LoadAssetAtPath<WeaponViewProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<WeaponViewProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.hipSprite = hipSprite;
            profile.adsSprite = adsSprite;
            profile.hipMuzzlePosition = hip.PivotView;
            profile.referenceAspect = (float)FrameWidth / FrameHeight;
            profile.adsMuzzleOffset = adsMuzzleOffset;
            EditorUtility.SetDirty(profile);

            var weapon = AssetDatabase.LoadAssetAtPath<Deadlock.Combat.WeaponDefinition>(WeaponPath);
            if (weapon != null && weapon.viewProfile != profile)
            {
                weapon.viewProfile = profile; // only the presentation link; combat values are left untouched
                EditorUtility.SetDirty(weapon);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[DEADLOCK] Weapon sprites baked: hip {hip.Width}x{hip.Height} muzzle@{hip.PivotView}, " +
                      $"ads {ads.Width}x{ads.Height} adsMuzzleOffset={adsMuzzleOffset}");
            return profile;
        }

        // ------------------------------------------------------------------ model

        static Transform BuildRifle(Material material, List<(Renderer, Color, float)> parts)
        {
            var root = new GameObject("Rifle").transform;

            void Box(string name, Vector3 pos, Vector3 size, Color color, float spec = 0f, Vector3 euler = default)
                => Add(PrimitiveType.Cube, name, pos, euler, size, color, spec);
            void Cyl(string name, Vector3 pos, float radius, float length, Color color, float spec, Vector3 euler)
                => Add(PrimitiveType.Cylinder, name, pos, euler, new Vector3(radius * 2f, length * 0.5f, radius * 2f), color, spec);
            void Capsule(string name, Vector3 from, Vector3 to, float radius, Color color, float spec = 0.15f)
            {
                Vector3 dir = to - from;
                var go = Add(PrimitiveType.Capsule, name, (from + to) * 0.5f, Vector3.zero,
                    new Vector3(radius * 2f, (dir.magnitude + radius * 2f) * 0.5f, radius * 2f), color, spec);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            }
            void Limb(string name, Vector3 from, Vector3 to, float radius, Color color)
            {
                Vector3 dir = to - from;
                var go = Add(PrimitiveType.Cylinder, name, (from + to) * 0.5f, Vector3.zero,
                    new Vector3(radius * 2f, dir.magnitude * 0.5f, radius * 2f), color, 0f);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            }
            GameObject Add(PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Color color, float spec)
            {
                var go = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.name = name;
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(euler);
                go.transform.localScale = scale;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                parts.Add((renderer, color, spec));
                return go;
            }

            Vector3 xr = new Vector3(90f, 0f, 0f); // cylinder along Z

            // Receiver (upper / lower), rail with ridges, charging handle
            Box("UpperReceiver", new Vector3(0f, 0.006f, 0.055f), new Vector3(0.058f, 0.07f, 0.27f), Metal, 1f);
            Box("RearReceiver", new Vector3(0f, 0.006f, -0.125f), new Vector3(0.056f, 0.066f, 0.09f), Metal, 1f);
            Box("SideRidgeR", new Vector3(0.0295f, 0.024f, 0.05f), new Vector3(0.003f, 0.006f, 0.26f), MetalDark, 0.6f);
            Box("SideRidgeL", new Vector3(-0.0295f, 0.024f, 0.05f), new Vector3(0.003f, 0.006f, 0.26f), MetalDark, 0.6f);
            Box("BoltRelease", new Vector3(-0.030f, -0.03f, 0.03f), new Vector3(0.004f, 0.018f, 0.028f), MetalDark, 0.6f);
            Box("DeflectorR", new Vector3(0.031f, 0.03f, -0.035f), new Vector3(0.006f, 0.018f, 0.018f), Metal, 1f);
            Box("LowerReceiver", new Vector3(0f, -0.045f, -0.01f), new Vector3(0.052f, 0.035f, 0.22f), MetalDark, 0.6f);
            Box("TopRail", new Vector3(0f, 0.046f, 0.02f), new Vector3(0.030f, 0.012f, 0.32f), MetalDark, 0.8f);
            for (int i = 0; i < 12; i++)
                Box($"RailRidge{i}", new Vector3(0f, 0.054f, -0.115f + i * 0.024f), new Vector3(0.034f, 0.006f, 0.010f), Metal, 1f);
            Box("RearChargingHandle", new Vector3(0f, 0.041f, -0.175f), new Vector3(0.05f, 0.010f, 0.02f), MetalDark, 0.6f);
            Box("EjectionPort", new Vector3(0.029f, 0.012f, 0.03f), new Vector3(0.004f, 0.022f, 0.055f), new Color(0.06f, 0.06f, 0.07f));
            Box("PortCover", new Vector3(0.0305f, -0.006f, 0.03f), new Vector3(0.003f, 0.010f, 0.06f), Metal, 1f);
            Box("ForwardAssist", new Vector3(0.026f, 0.020f, -0.07f), new Vector3(0.016f, 0.016f, 0.03f), MetalDark, 0.6f);
            for (int i = 0; i < 3; i++)
                Cyl($"Pin{i}", new Vector3(0.027f, -0.035f, -0.08f + i * 0.07f), 0.004f, 0.006f, Metal, 1f, new Vector3(0f, 0f, 90f));

            // Rear sight (aperture ring on a base) — the ring is what the ADS view looks through.
            Box("RearSightBase", new Vector3(0f, 0.062f, RearSightCentre.z), new Vector3(0.018f, 0.020f, 0.024f), MetalDark, 0.6f);
            Box("ChargingHandleLatch", new Vector3(0.012f, 0.044f, -0.078f), new Vector3(0.012f, 0.008f, 0.012f), MetalDark, 0.6f);
            var aperture = Add(PrimitiveType.Cube, "Aperture", RearSightCentre, Vector3.zero, Vector3.one, MetalDark, 0.6f);
            aperture.GetComponent<MeshFilter>().sharedMesh = RingMesh(0.0145f, 0.0026f, 0.004f, 40);
            Box("RearSightPost", new Vector3(0f, 0.0705f, RearSightCentre.z), new Vector3(0.006f, 0.008f, 0.008f), MetalDark, 0.6f);

            // Handguard with vents, front sight tower (post tip on the sight line)
            Box("Handguard", new Vector3(0f, 0.0f, 0.32f), new Vector3(0.066f, 0.070f, 0.26f), Polymer, 0.3f);
            Box("HandguardTop", new Vector3(0f, 0.039f, 0.32f), new Vector3(0.040f, 0.010f, 0.25f), MetalDark, 0.5f);
            for (int i = 0; i < 5; i++)
            {
                float z = 0.225f + i * 0.045f;
                Box($"VentR{i}", new Vector3(0.0335f, 0.010f, z), new Vector3(0.003f, 0.016f, 0.028f), new Color(0.08f, 0.075f, 0.07f));
                Box($"VentL{i}", new Vector3(-0.0335f, 0.010f, z), new Vector3(0.003f, 0.016f, 0.028f), new Color(0.08f, 0.075f, 0.07f));
            }
            Box("FrontSightBase", new Vector3(0f, 0.052f, FrontSightTip.z), new Vector3(0.024f, 0.022f, 0.026f), MetalDark, 0.6f);
            Box("FrontSightPost", new Vector3(0f, 0.0775f, FrontSightTip.z), new Vector3(0.004f, 0.025f, 0.004f), new Color(0.07f, 0.07f, 0.08f));
            Box("FrontSightEarL", new Vector3(-0.0105f, 0.076f, FrontSightTip.z), new Vector3(0.004f, 0.030f, 0.014f), MetalDark, 0.6f);
            Box("FrontSightEarR", new Vector3(0.0105f, 0.076f, FrontSightTip.z), new Vector3(0.004f, 0.030f, 0.014f), MetalDark, 0.6f);

            // Barrel and muzzle device
            Cyl("Barrel", new Vector3(0f, 0f, 0.565f), 0.0095f, 0.25f, Metal, 1f, xr);
            Cyl("GasBlock", new Vector3(0f, 0.004f, 0.47f), 0.014f, 0.025f, MetalDark, 0.6f, xr);
            Cyl("MuzzleDevice", new Vector3(0f, 0f, 0.71f), 0.0145f, 0.05f, MetalDark, 0.7f, xr);
            Box("MuzzleSlotL", new Vector3(-0.0135f, 0.004f, 0.715f), new Vector3(0.004f, 0.006f, 0.028f), new Color(0.05f, 0.05f, 0.06f));
            Box("MuzzleSlotR", new Vector3(0.0135f, 0.004f, 0.715f), new Vector3(0.004f, 0.006f, 0.028f), new Color(0.05f, 0.05f, 0.06f));

            // Magazine (with ribs), trigger group, grip, stock
            Box("MagWell", new Vector3(0f, -0.072f, 0.07f), new Vector3(0.042f, 0.035f, 0.08f), MetalDark, 0.5f);
            Box("Magazine", new Vector3(0f, -0.15f, 0.085f), new Vector3(0.032f, 0.14f, 0.07f), MetalDark, 0.4f, new Vector3(-14f, 0f, 0f));
            for (int i = 0; i < 3; i++)
                Box($"MagRib{i}", new Vector3(0.0165f, -0.12f - i * 0.03f, 0.08f + i * 0.007f), new Vector3(0.002f, 0.012f, 0.06f), Metal, 0.6f, new Vector3(-14f, 0f, 0f));
            Box("TriggerGuard", new Vector3(0f, -0.083f, -0.04f), new Vector3(0.008f, 0.006f, 0.075f), MetalDark, 0.5f);
            Box("Trigger", new Vector3(0f, -0.072f, -0.035f), new Vector3(0.005f, 0.022f, 0.006f), Metal, 1f, new Vector3(10f, 0f, 0f));
            Box("PistolGrip", new Vector3(0f, -0.105f, -0.105f), new Vector3(0.032f, 0.11f, 0.042f), Polymer, 0.2f, new Vector3(20f, 0f, 0f));
            Box("RearStockTube", new Vector3(0f, 0.004f, -0.24f), new Vector3(0.030f, 0.030f, 0.14f), MetalDark, 0.6f);
            Box("RearStock", new Vector3(0f, -0.012f, -0.32f), new Vector3(0.044f, 0.07f, 0.16f), Polymer, 0.25f);
            Box("RearButtPad", new Vector3(0f, -0.015f, -0.405f), new Vector3(0.048f, 0.085f, 0.018f), GlovePad);

            // Right hand on the pistol grip: palm behind, fingers wrapped round the front, index finger along the guard.
            Box("RightPalm", new Vector3(0.010f, -0.098f, -0.132f), new Vector3(0.052f, 0.085f, 0.045f), Glove, 0.15f, new Vector3(20f, 0f, 0f));
            Box("RightKnucklePad", new Vector3(0.033f, -0.097f, -0.110f), new Vector3(0.010f, 0.07f, 0.035f), GlovePad, 0f, new Vector3(20f, 0f, 0f));
            for (int i = 0; i < 3; i++)
            {
                float y = -0.098f - i * 0.022f;
                float z = -0.078f - i * 0.008f;
                Capsule($"RightFinger{i}A", new Vector3(0.03f, y, z - 0.012f), new Vector3(0.012f, y, z + 0.004f), 0.0095f, Glove);
                Capsule($"RightFinger{i}B", new Vector3(0.012f, y, z + 0.004f), new Vector3(-0.018f, y + 0.002f, z - 0.002f), 0.009f, Glove);
            }
            Capsule("RightIndexA", new Vector3(0.03f, -0.074f, -0.085f), new Vector3(0.024f, -0.074f, -0.055f), 0.009f, Glove);
            Capsule("RightIndexB", new Vector3(0.024f, -0.074f, -0.055f), new Vector3(0.008f, -0.072f, -0.034f), 0.0085f, Glove);
            Capsule("RightThumb", new Vector3(-0.022f, -0.075f, -0.14f), new Vector3(-0.024f, -0.058f, -0.095f), 0.0095f, Glove);
            Box("RightStitch", new Vector3(0.037f, -0.098f, -0.135f), new Vector3(0.002f, 0.06f, 0.003f), Stitch, 0f, new Vector3(20f, 0f, 0f));
            Limb("RightCuff", new Vector3(0.016f, -0.135f, -0.175f), new Vector3(0.03f, -0.16f, -0.215f), 0.033f, GlovePad);
            Limb("RightForearm", new Vector3(0.03f, -0.16f, -0.215f), new Vector3(0.13f, -0.39f, -0.52f), 0.042f, Sleeve);

            // Left hand under the handguard: palm below, fingers curling up the left side, thumb over the right side.
            Box("LeftPalm", new Vector3(0.002f, -0.048f, 0.315f), new Vector3(0.062f, 0.026f, 0.085f), Glove, 0.15f);
            for (int i = 0; i < 4; i++)
            {
                float z = 0.285f + i * 0.021f;
                Capsule($"LeftFinger{i}A", new Vector3(-0.012f, -0.048f, z), new Vector3(-0.036f, -0.036f, z), 0.0088f, Glove);
                Capsule($"LeftFinger{i}B", new Vector3(-0.036f, -0.036f, z), new Vector3(-0.040f, -0.006f, z + 0.002f), 0.0082f, Glove);
            }
            Capsule("LeftThumb", new Vector3(0.026f, -0.042f, 0.285f), new Vector3(0.037f, -0.005f, 0.33f), 0.0095f, Glove);
            Box("LeftKnucklePad", new Vector3(-0.039f, -0.022f, 0.317f), new Vector3(0.008f, 0.018f, 0.075f), GlovePad);
            Box("LeftStitch", new Vector3(0.002f, -0.0615f, 0.315f), new Vector3(0.05f, 0.002f, 0.003f), Stitch);
            Limb("LeftCuff", new Vector3(0.004f, -0.065f, 0.27f), new Vector3(-0.004f, -0.09f, 0.235f), 0.032f, GlovePad);
            Limb("LeftForearm", new Vector3(-0.004f, -0.09f, 0.235f), new Vector3(-0.09f, -0.33f, -0.08f), 0.04f, Sleeve);
            return root;
        }

        /// <summary>Flat-sided ring (aperture) in the XY plane: radius to the tube centre, tube half-size, depth.</summary>
        static Mesh RingMesh(float radius, float halfWidth, float depth, int segments)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                int i = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                normals.AddRange(new[] { n, n, n, n });
                triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 }); // clockwise = front face in Unity
            }
            float ri = radius - halfWidth, ro = radius + halfWidth, hz = depth * 0.5f;
            for (int k = 0; k < segments; k++)
            {
                float a0 = k * Mathf.PI * 2f / segments, a1 = (k + 1) * Mathf.PI * 2f / segments;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                Vector3 back = Vector3.back * hz, front = Vector3.forward * hz;
                Quad(d0 * ro + back, d1 * ro + back, d1 * ri + back, d0 * ri + back, Vector3.back);       // face toward the eye
                Quad(d0 * ri + front, d1 * ri + front, d1 * ro + front, d0 * ro + front, Vector3.forward);
                Quad(d0 * ro + front, d1 * ro + front, d1 * ro + back, d0 * ro + back, (d0 + d1).normalized); // outer rim
                Quad(d0 * ri + back, d1 * ri + back, d1 * ri + front, d0 * ri + front, -(d0 + d1).normalized); // inner rim
            }
            var mesh = new Mesh { name = "ApertureRing" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ rendering

        sealed class Baked
        {
            public Texture2D Texture;
            public int Width, Height;
            public Vector2 PivotPixel;   // in cropped texture, bottom-left origin
            public Vector2 PivotView;    // pivot position in view units at z = 1
        }

        static Baked RenderSprite(Camera cam, List<(Renderer renderer, Color color, float spec)> parts, Vector3 pivotWorld,
            out Vector2 secondaryOffset, Vector3? secondaryWorld = null)
        {
            Shader.SetGlobalVector("_BakeCameraPos", cam.transform.position);
            Color[] color = RenderPass(cam, parts, 0f, false);
            Color[] ids = RenderPass(cam, parts, 1f, true);
            Color[] normals = RenderPass(cam, parts, 2f, true);
            int w = FrameWidth, h = FrameHeight;

            int Id(int x, int y) => Mathf.RoundToInt(ids[y * w + x].r * 255f);
            bool Inside(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && ids[y * w + x].a > 0.5f && Id(x, y) > 0;
            Vector3 N(int x, int y) { Color c = normals[y * w + x]; return new Vector3(c.r * 2f - 1f, c.g * 2f - 1f, c.b * 2f - 1f); }

            var output = new Color[w * h];
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Inside(x, y))
                {
                    // 1 px dark rim just outside the silhouette.
                    bool rim = Inside(x - 1, y) || Inside(x + 1, y) || Inside(x, y - 1) || Inside(x, y + 1);
                    output[y * w + x] = rim ? Outline : Color.clear;
                    if (rim) Grow(x, y);
                    continue;
                }
                Grow(x, y);
                Color c = color[y * w + x];
                int id = Id(x, y);
                // Part boundary: dark line on the pixel whose right/upper neighbour belongs to another part.
                bool boundary = (Inside(x + 1, y) && Id(x + 1, y) != id) || (Inside(x, y + 1) && Id(x, y + 1) != id);
                if (boundary)
                    c = Color.Lerp(c, Outline, 0.85f);
                else
                {
                    // Hard edge inside a part (box corners): the side facing the light gets a bright edge line,
                    // the other side a dark crease, so metal edges and bevels read.
                    Vector3 n = N(x, y);
                    Vector3 other = Inside(x + 1, y) && Vector3.Dot(n, N(x + 1, y)) < 0.75f ? N(x + 1, y)
                        : Inside(x, y + 1) && Vector3.Dot(n, N(x, y + 1)) < 0.75f ? N(x, y + 1)
                        : Vector3.zero;
                    if (other != Vector3.zero)
                    {
                        bool lit = Vector3.Dot(n, TowardLight.normalized) > Vector3.Dot(other, TowardLight.normalized);
                        c = lit ? Color.Lerp(c, Color.white, 0.35f) : Color.Lerp(c, Outline, 0.5f);
                    }
                }
                c.a = 1f;
                output[y * w + x] = c;
            }

            void Grow(int x, int y)
            {
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }

            // Crop to content (keeping the pivot inside the texture bounds is not required for sprites).
            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            var cropped = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            var pixels = new Color[cw * ch];
            for (int y = 0; y < ch; y++)
                System.Array.Copy(output, (y + minY) * w + minX, pixels, y * cw, cw);
            cropped.SetPixels(pixels);
            cropped.Apply();

            Vector3 vp = cam.WorldToViewportPoint(pivotWorld);
            var baked = new Baked
            {
                Texture = cropped,
                Width = cw,
                Height = ch,
                PivotPixel = new Vector2(vp.x * w - minX, vp.y * h - minY),
                PivotView = ViewportToView(vp, cam.aspect),
            };

            secondaryOffset = Vector2.zero;
            if (secondaryWorld.HasValue)
                secondaryOffset = ViewportToView(cam.WorldToViewportPoint(secondaryWorld.Value), cam.aspect) - baked.PivotView;
            return baked;
        }

        static Vector2 ViewportToView(Vector3 viewport, float aspect)
        {
            float halfH = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            return new Vector2((viewport.x * 2f - 1f) * halfH * aspect, (viewport.y * 2f - 1f) * halfH);
        }

        static Color[] RenderPass(Camera cam, List<(Renderer renderer, Color color, float spec)> parts, float mode, bool linear)
        {
            Shader.SetGlobalFloat("_BakeMode", mode);
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < parts.Count; i++)
            {
                block.Clear();
                block.SetColor("_BaseColor", parts[i].color);
                block.SetVector("_PartId", new Vector4((i + 1) / 255f, 0f, 0f, 1f));
                block.SetFloat("_Spec", parts[i].spec);
                parts[i].renderer.SetPropertyBlock(block);
            }

            var rt = new RenderTexture(FrameWidth, FrameHeight, 24, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGBA32, false, linear);
            tex.ReadPixels(new Rect(0, 0, FrameWidth, FrameHeight), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Color[] result = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return result;
        }

        static Sprite SaveSprite(string path, Baked baked)
        {
            File.WriteAllBytes(path, baked.Texture.EncodeToPNG());
            Object.DestroyImmediate(baked.Texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = FrameHeight / (2f * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad));
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(baked.PivotPixel.x / baked.Width, baked.PivotPixel.y / baked.Height);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.filterMode = FilterMode.Point; // crisp sprite pixels, rendered ~1:1 at 1080p
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
