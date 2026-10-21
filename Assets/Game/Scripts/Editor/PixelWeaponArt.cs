using System;
using System.Collections.Generic;
using System.IO;
using Deadlock.View;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Imports the first-person weapon art. Two paths, both separate from <see cref="WeaponSpriteBaker"/> (the baker and
    /// the scene builder never write into Assets/Game/Art/Weapons, so re-baking cannot overwrite the art):
    /// - Per weapon: <see cref="WeaponArtDefinition"/>s (guns: hip / ADS) and <see cref="MeleeArtDefinition"/>s (melee:
    ///   one image) listed in <see cref="WeaponArtRegistry"/>, with hand-written placement files. <see cref="ImportRegistered"/> validates every definition first and
    ///   throws one <see cref="WeaponArtException"/> naming the weapon id, asset and field of each problem (missing art,
    ///   duplicate ids...), never filling a gap with another weapon's art. Only the image's import settings and the
    ///   weapon's view profile are written; sources and placement files are only read.
    /// - The provisional pixel set (pixel_weapons.json, Tools/pixelart/draw_weapons.py): the knife (and the shared
    ///   muzzle flash sprite); <see cref="PrepareKnife"/>.
    /// Sprites: Point filter (crisp pixels), uncompressed, no mip maps, clamped, never downscaled.
    /// Optional skin region maps (one per image, same canvas) are imported as linear 8-bit (R8) data textures, point
    /// sampled and never resized, and written into the view profile next to their image.
    /// </summary>
    public static class PixelWeaponArt
    {
        public const string Folder = "Assets/Game/Art/Weapons";
        public const string RegistryPath = "Assets/Game/Data/WeaponArt/WeaponArtRegistry.asset";
        public const string KnifeProfilePath = "Assets/Game/Data/MeleeView_ProtoKnife_Pixel.asset";
        const string KnifeName = "Spr_PixelKnife";
        const string FlashName = "Spr_PixelMuzzleFlash";
        static readonly float HalfHeight = Mathf.Tan(25f * Mathf.Deg2Rad); // view-model camera, z = 1

        [Serializable]
        class SpriteInfo
        {
            public string name;
            public float[] size;
            public float[] pivot;
            [Tooltip("Optional per-sprite pixels-per-unit (art drawn at another resolution); 0 = the shared value.")]
            public float pixelsPerUnit;
        }

        [Serializable]
        class ArtInfo
        {
            public SpriteInfo[] sprites;
            public float pixelsPerUnit;
            public float flashPixelsPerUnit;
        }

        /// <summary>Imports the provisional pixel set and returns the knife's pixel profile (the baked one when the art is missing).</summary>
        public static MeleeViewProfile PrepareKnife(MeleeViewProfile bakedMelee)
        {
            string jsonPath = $"{Folder}/pixel_weapons.json";
            if (!File.Exists(jsonPath))
                return bakedMelee;
            var info = JsonUtility.FromJson<ArtInfo>(File.ReadAllText(jsonPath));
            foreach (var sprite in info.sprites)
            {
                float ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit
                    : sprite.name == FlashName ? info.flashPixelsPerUnit : info.pixelsPerUnit;
                Configure($"{Folder}/{sprite.name}.png", new Vector2(sprite.pivot[0], sprite.pivot[1]), ppu);
            }
            var knife = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{KnifeName}.png");
            if (knife == null)
            {
                Debug.LogWarning($"[DEADLOCK] Pixel knife art missing: {Folder}/{KnifeName}.png (baked sprite is used instead)");
                return bakedMelee;
            }
            var melee = Derive(bakedMelee, KnifeProfilePath);
            melee.sprite = knife;
            EditorUtility.SetDirty(melee);
            AssetDatabase.SaveAssets();
            return melee;
        }

        // ---- per-weapon art -----------------------------------------------------------------------------------------

        [Serializable]
        class HipPlacement
        {
            public float[] muzzle;            // muzzle tip, pixels from the top-left (pivot; the flash sits here)
            public float[] hold;              // hold point (kick / reload turn around it), pixels from the top-left
            public float screenHeightPixels;  // how many image pixels make the full screen height (sets the size)
            public float anchorAspect;        // screen aspect at which the image's bottom-right = the screen's (0 = reference)
            public float[] offset;            // extra shift of the whole image in units (e.g. down: arms run further off the bottom)
            public float hipRightEdgeFollow;
            public float hipRightEdgeFollowNarrow;
            public float hipScaleAt4x3;
            public float hipMuzzleFlashScale;
            public float[] hipToAds;          // optional: hip move while raising the sights, offset in units (relative)
        }

        [Serializable]
        class AdsPlacement
        {
            public float[] aim;               // front-sight tip = pivot = screen centre, pixels from the top-left
            public float[] muzzle;            // flash reference (the flash is drawn in front of the sprite)
            public float[] hold;              // optional: turn point for kick / reload, pixels from the top-left
            public float screenHeightPixels;  // image pixels per full screen height
            public float adsMuzzleFlashScale;
        }

        /// <summary>Imports every definition in the registry (Assets/Game/Data/WeaponArt/WeaponArtRegistry.asset). No registry = nothing to do.</summary>
        public static void ImportRegistered()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponArtRegistry>(RegistryPath);
            if (registry == null)
                return;
            Import(registry.weapons, registry.melee ?? Array.Empty<MeleeArtDefinition>(), RegistryPath);
        }

        public static void Import(IList<WeaponArtDefinition> definitions, string context) =>
            Import(definitions, Array.Empty<MeleeArtDefinition>(), context);

        /// <summary>
        /// Validates all definitions, then imports each: the images' import settings (pivot, size, Point, uncompressed),
        /// the view profile (base profile values + placement), and the weapon's viewProfile reference.
        /// Throws <see cref="WeaponArtException"/> (nothing is written) when any definition has a problem.
        /// </summary>
        public static void Import(IList<WeaponArtDefinition> definitions, IList<MeleeArtDefinition> meleeDefinitions, string context)
        {
            var problems = new List<string>();
            var hips = new Dictionary<WeaponArtDefinition, HipPlacement>();
            var adss = new Dictionary<WeaponArtDefinition, AdsPlacement>();
            var ids = new Dictionary<string, WeaponArtDefinition>();
            var meleeIds = new Dictionary<string, MeleeArtDefinition>();
            for (int i = 0; i < definitions.Count; i++)
            {
                var d = definitions[i];
                if (d == null)
                {
                    problems.Add($"{context}: entry {i} is empty");
                    continue;
                }
                string where = $"{AssetDatabase.GetAssetPath(d)}";
                string id = d.WeaponId;
                if (d.weapon == null)
                    problems.Add($"{where}: 'weapon' is not set (the weapon id comes from it)");
                else if (string.IsNullOrWhiteSpace(id))
                    problems.Add($"{where}: weapon {AssetDatabase.GetAssetPath(d.weapon)} has no weaponId");
                else if (ids.TryGetValue(id, out var other))
                    problems.Add($"weapon id '{id}' is used twice: {AssetDatabase.GetAssetPath(other)} and {where}");
                else
                    ids[id] = d;
                string label = $"[{id ?? "?"}] {where}";
                if (d.baseProfile == null)
                    problems.Add($"{label}: 'baseProfile' is not set");
                var hip = CheckView(d.hip, "hip", label, problems);
                var ads = CheckView(d.ads, "ads", label, problems);
                if (hip != null)
                {
                    var place = Parse<HipPlacement>(d.hip.placement, "hip", label, problems);
                    if (place != null)
                    {
                        Require(place.muzzle, 2, "hip.placement", "muzzle", label, problems);
                        Require(place.hold, 2, "hip.placement", "hold", label, problems);
                        if (place.screenHeightPixels <= 0f)
                            problems.Add($"{label}: hip.placement ({AssetDatabase.GetAssetPath(d.hip.placement)}) needs 'screenHeightPixels' > 0");
                        hips[d] = place;
                    }
                }
                if (ads != null)
                {
                    var place = Parse<AdsPlacement>(d.ads.placement, "ads", label, problems);
                    if (place != null)
                    {
                        Require(place.aim, 2, "ads.placement", "aim", label, problems);
                        Require(place.muzzle, 2, "ads.placement", "muzzle", label, problems);
                        if (place.screenHeightPixels <= 0f)
                            problems.Add($"{label}: ads.placement ({AssetDatabase.GetAssetPath(d.ads.placement)}) needs 'screenHeightPixels' > 0");
                        adss[d] = place;
                    }
                }
            }
            var meleePlaces = new Dictionary<MeleeArtDefinition, MeleePlacement>();
            for (int i = 0; i < meleeDefinitions.Count; i++)
            {
                var d = meleeDefinitions[i];
                if (d == null)
                {
                    problems.Add($"{context}: melee entry {i} is empty");
                    continue;
                }
                string where = AssetDatabase.GetAssetPath(d);
                string id = d.WeaponId;
                if (d.melee == null)
                    problems.Add($"{where}: 'melee' is not set (the weapon id comes from it)");
                else if (string.IsNullOrWhiteSpace(id))
                    problems.Add($"{where}: melee weapon {AssetDatabase.GetAssetPath(d.melee)} has no weaponId");
                else if (ids.TryGetValue(id, out var otherGun))
                    problems.Add($"weapon id '{id}' is used twice: {AssetDatabase.GetAssetPath(otherGun)} and {where}");
                else if (meleeIds.TryGetValue(id, out var otherMelee))
                    problems.Add($"weapon id '{id}' is used twice: {AssetDatabase.GetAssetPath(otherMelee)} and {where}");
                else
                    meleeIds[id] = d;
                string label = $"[{id ?? "?"}] {where}";
                if (d.baseProfile == null)
                    problems.Add($"{label}: 'baseProfile' is not set");
                if (CheckView(d.art, "art", label, problems) != null)
                {
                    var place = Parse<MeleePlacement>(d.art.placement, "art", label, problems);
                    if (place != null)
                    {
                        Require(place.grip, 2, "art.placement", "grip", label, problems);
                        if (place.screenHeightPixels <= 0f)
                            problems.Add($"{label}: art.placement ({AssetDatabase.GetAssetPath(d.art.placement)}) needs 'screenHeightPixels' > 0");
                        if (d.baseProfile != null)
                            foreach (var o in place.motionOverrides ?? Array.Empty<MotionOverride>())
                                if (new SerializedObject(d.baseProfile).FindProperty(o.field) == null)
                                    problems.Add($"{label}: art.placement motion override '{o.field}' is not a field of MeleeViewProfile");
                        meleePlaces[d] = place;
                    }
                }
            }
            if (problems.Count > 0)
                throw new WeaponArtException(problems);

            foreach (var d in definitions)
            {
                var view = d.viewProfile;
                if (view == null)
                {
                    string path = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(d)).Replace('\\', '/')}/WeaponView_{d.WeaponId}_Art.asset";
                    view = Derive(d.baseProfile, path);
                    d.viewProfile = view;
                    EditorUtility.SetDirty(d);
                }
                else
                {
                    EditorUtility.CopySerialized(d.baseProfile, view);
                    view.name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(view));
                }
                if (d.muzzleFlash != null)
                    view.muzzleFlashSprite = d.muzzleFlash;
                ApplyHip(view, d.hip.image, hips[d]);
                ApplyAds(view, d.ads.image, adss[d]);
                view.hipSkinRegions = ConfigureSkinRegions(d.hip.skinRegions);
                view.adsSkinRegions = ConfigureSkinRegions(d.ads.skinRegions);
                EditorUtility.SetDirty(view);
                d.weapon.viewProfile = view;
                EditorUtility.SetDirty(d.weapon);
            }
            foreach (var d in meleeDefinitions)
            {
                var view = d.viewProfile;
                if (view == null)
                {
                    string path = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(d)).Replace('\\', '/')}/MeleeView_{d.WeaponId}_Art.asset";
                    view = Derive(d.baseProfile, path);
                    d.viewProfile = view;
                    EditorUtility.SetDirty(d);
                }
                else
                {
                    EditorUtility.CopySerialized(d.baseProfile, view);
                    view.name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(view));
                }
                ApplyMelee(view, d.art.image, meleePlaces[d]);
                view.skinRegions = ConfigureSkinRegions(d.art.skinRegions);
                EditorUtility.SetDirty(view);
                d.melee.viewProfile = view;
                EditorUtility.SetDirty(d.melee);
            }
            AssetDatabase.SaveAssets();
        }

        [Serializable]
        class MotionOverride
        {
            public string field;    // MeleeViewProfile field name (presentation only)
            public float[] value;   // 1 number (float) or 2 numbers (Vector2)
        }

        [Serializable]
        class MeleePlacement
        {
            public float[] grip;                    // grip centre = pivot, pixels from the top-left
            public float screenHeightPixels;        // image pixels per full screen height (sets the size)
            public MotionOverride[] motionOverrides; // optional: presentation values tuned for this art
        }

        /// <summary>Melee: pivot = grip; size from screenHeightPixels; optional motion overrides for this art (presentation only).</summary>
        static void ApplyMelee(MeleeViewProfile view, Texture2D image, MeleePlacement place)
        {
            string path = AssetDatabase.GetAssetPath(image);
            Size(image, out int width, out int height);
            float ppu = place.screenHeightPixels / (2f * HalfHeight);
            Configure(path, new Vector2(place.grip[0] / width, 1f - place.grip[1] / height), ppu, MaxSize(width, height));
            view.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (place.motionOverrides == null || place.motionOverrides.Length == 0)
                return;
            var so = new SerializedObject(view);
            foreach (var o in place.motionOverrides)
            {
                var prop = so.FindProperty(o.field);
                if (prop.propertyType == SerializedPropertyType.Vector2)
                    prop.vector2Value = new Vector2(o.value[0], o.value[1]);
                else if (prop.propertyType == SerializedPropertyType.Boolean)
                    prop.boolValue = o.value[0] > 0.5f;
                else
                    prop.floatValue = o.value[0];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Texture2D CheckView(WeaponArtDefinition.ViewArt art, string slot, string label, List<string> problems)
        {
            if (art == null || art.image == null)
            {
                problems.Add($"{label}: '{slot}.image' is not set");
                return null;
            }
            if (art.source == null)
                problems.Add($"{label}: '{slot}.source' (the approved original) is not set");
            if (art.placement == null)
                problems.Add($"{label}: '{slot}.placement' is not set");
            if (art.source != null)
            {
                Size(art.source, out int sw, out int sh);
                Size(art.image, out int iw, out int ih);
                if (sw != iw || sh != ih)
                    problems.Add($"{label}: '{slot}.image' {AssetDatabase.GetAssetPath(art.image)} is {iw}x{ih} but its source {AssetDatabase.GetAssetPath(art.source)} is {sw}x{sh}");
            }
            if (art.skinRegions != null)
            {
                Size(art.skinRegions, out int rw, out int rh);
                Size(art.image, out int iw, out int ih);
                if (rw != iw || rh != ih)
                    problems.Add($"{label}: '{slot}.skinRegions' {AssetDatabase.GetAssetPath(art.skinRegions)} is {rw}x{rh} but the image is {iw}x{ih}");
            }
            return art.placement != null && art.source != null ? art.image : null;
        }

        static T Parse<T>(TextAsset file, string slot, string label, List<string> problems) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(file.text);
            }
            catch (Exception e)
            {
                problems.Add($"{label}: {slot}.placement ({AssetDatabase.GetAssetPath(file)}) is not valid JSON: {e.Message}");
                return null;
            }
        }

        static void Require(float[] value, int length, string file, string field, string label, List<string> problems)
        {
            if (value == null || value.Length != length)
                problems.Add($"{label}: {file} needs '{field}' ({length} numbers)");
        }

        internal static void Size(Texture2D texture, out int width, out int height)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            importer.GetSourceTextureWidthAndHeight(out width, out height);
        }

        /// <summary>
        /// Hip: pivot = muzzle (the flash follows it). At the anchor aspect the image's bottom-right corner sits on the
        /// screen's bottom-right corner (arms / stock continue off the bottom and right edges); on narrower screens the
        /// right side runs off the screen instead of the gun moving toward the centre.
        /// </summary>
        static void ApplyHip(WeaponViewProfile view, Texture2D image, HipPlacement place)
        {
            string path = AssetDatabase.GetAssetPath(image);
            Size(image, out int width, out int height);
            float ppu = place.screenHeightPixels / (2f * HalfHeight);
            Configure(path, new Vector2(place.muzzle[0] / width, 1f - place.muzzle[1] / height), ppu, MaxSize(width, height));
            view.hipSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            // hipMuzzlePosition is the 16:9 place; with no edge follow it is the place on every screen.
            float halfWidth = HalfHeight * (place.anchorAspect > 0f ? place.anchorAspect : view.referenceAspect);
            view.hipMuzzlePosition = new Vector2(halfWidth - (width - place.muzzle[0]) / ppu, -HalfHeight + (height - place.muzzle[1]) / ppu);
            if (place.offset != null && place.offset.Length == 2)
                view.hipMuzzlePosition += new Vector2(place.offset[0], place.offset[1]);
            view.hipHoldOffset = new Vector2((place.hold[0] - place.muzzle[0]) / ppu, (place.muzzle[1] - place.hold[1]) / ppu);
            view.hipRightEdgeFollow = place.hipRightEdgeFollow;
            view.hipRightEdgeFollowNarrow = place.hipRightEdgeFollowNarrow;
            view.hipScaleAt4x3 = place.hipScaleAt4x3;
            if (place.hipMuzzleFlashScale > 0f)
                view.hipMuzzleFlashScale = place.hipMuzzleFlashScale;
            if (place.hipToAds != null && place.hipToAds.Length == 2)
            {
                view.hipToAdsMuzzlePosition = new Vector2(place.hipToAds[0], place.hipToAds[1]);
                view.hipToAdsRelative = true;
            }
        }

        /// <summary>ADS: pivot = aim point (front-sight tip), which sits on the screen centre when aimed.</summary>
        static void ApplyAds(WeaponViewProfile view, Texture2D image, AdsPlacement place)
        {
            string path = AssetDatabase.GetAssetPath(image);
            Size(image, out int width, out int height);
            float ppu = place.screenHeightPixels / (2f * HalfHeight);
            Configure(path, new Vector2(place.aim[0] / width, 1f - place.aim[1] / height), ppu, MaxSize(width, height));
            view.adsSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            view.adsMuzzleOffset = new Vector2((place.muzzle[0] - place.aim[0]) / ppu, (place.aim[1] - place.muzzle[1]) / ppu);
            if (place.hold != null && place.hold.Length == 2)
                view.adsHoldOffset = new Vector2((place.hold[0] - place.aim[0]) / ppu, (place.aim[1] - place.hold[1]) / ppu);
            if (place.adsMuzzleFlashScale > 0f)
                view.adsMuzzleFlashScale = place.adsMuzzleFlashScale;
        }

        /// <summary>Smallest import size limit that keeps the image at its own size (no downscaling).</summary>
        internal static int MaxSize(int width, int height)
        {
            int size = 32;
            while (size < Mathf.Max(width, height))
                size *= 2;
            return size;
        }

        static T Derive<T>(T source, string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = UnityEngine.Object.Instantiate(source);
                AssetDatabase.CreateAsset(asset, path);
            }
            else
                EditorUtility.CopySerialized(source, asset);
            asset.name = Path.GetFileNameWithoutExtension(path);
            return asset;
        }

        /// <summary>Region map import: data (not colour), R8, point, no mip maps, clamped, never resized.</summary>
        internal static Texture2D ConfigureSkinRegions(Texture2D regions)
        {
            if (regions == null)
                return null;
            string path = AssetDatabase.GetAssetPath(regions);
            Size(regions, out int width, out int height);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.SingleChannel; // R8
            var channel = new TextureImporterSettings();
            importer.ReadTextureSettings(channel);
            channel.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            importer.SetTextureSettings(channel);
            importer.sRGBTexture = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, MaxSize(width, height));
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        internal static void Configure(string path, Vector2 pivot, float pixelsPerUnit, int maxSize = 1024)
        {
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
                return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spritePixelsPerUnit = pixelsPerUnit;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, maxSize); // never lowered: no downscaling, existing .meta kept
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }
    }

    /// <summary>Problems found in weapon art definitions (all of them, one per line, each naming the weapon id / asset / field).</summary>
    public sealed class WeaponArtException : Exception
    {
        public IReadOnlyList<string> Problems { get; }

        public WeaponArtException(IReadOnlyList<string> problems)
            : base("[DEADLOCK] Weapon art definitions have problems (nothing was imported):\n- " + string.Join("\n- ", problems))
        {
            Problems = problems;
        }
    }
}
