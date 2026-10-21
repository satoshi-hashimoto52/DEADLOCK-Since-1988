using System;
using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// Colour-change regions of a first-person image. A region map (Tools/pixelart/make_skin_regions.py) stores
    /// index × 32 per pixel; 0 = keep the drawn colour (line black, steel, everything unmasked).
    /// </summary>
    public enum SkinRegion
    {
        None = 0,
        WeaponBody = 1,
        GloveBody = 2,
        GloveReinforcement = 3,
        WristBand = 4,
        SleeveBase = 5,
        SleeveCamo = 6,
    }

    /// <summary>One region's colour: hue / saturation replace the drawn ones, the drawn lightness (shading) stays.</summary>
    [Serializable]
    public sealed class SkinColour
    {
        public SkinRegion region;
        [Range(0f, 360f)] public float hue;
        [Range(0f, 1f)] public float saturation;
    }

    /// <summary>A weapon skin: colours of the weapon's own regions only (never the hands).</summary>
    [Serializable]
    public sealed class WeaponSkin
    {
        public string skinId;
        public string label;
        public SkinColour[] colours = Array.Empty<SkinColour>();
    }

    /// <summary>The skins one weapon (by <c>weaponId</c>) offers besides its standard look, and its equipment-screen image.</summary>
    [Serializable]
    public sealed class WeaponSkinSet
    {
        public string weaponId;
        public string label;
        public WeaponSkin[] skins = Array.Empty<WeaponSkin>();
        [Tooltip("Equipment screen: side view of the whole weapon (written by the scene builder; empty = none yet).")]
        public Sprite sideView;
        [Tooltip("Region map of the side view (same canvas). Empty = shown in its drawn colours.")]
        public Texture2D sideViewRegions;
    }

    /// <summary>A player (hands / arms) skin: colours of the glove, wrist band and sleeve regions only; the same on every weapon.</summary>
    [Serializable]
    public sealed class PlayerSkin
    {
        public string skinId;
        public string label;
        public SkinColour[] colours = Array.Empty<SkinColour>();
    }

    /// <summary>
    /// Skins the game knows (looks only: no combat value, hit volume or input depends on them). Weapon skins and the
    /// player's hand / arm skin are chosen independently (<see cref="SkinSelection"/>); "standard" (the drawn colours)
    /// is always available and is not listed. Every skinned image has a region map from the same masks, so one choice
    /// reaches hip, ADS and the knife alike; images without a region map simply keep their drawn colours.
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Skin Catalog", fileName = "SkinCatalog")]
    public sealed class SkinCatalog : ScriptableObject
    {
        public const string StandardId = "standard";
        public const string StandardLabel = "標準";
        /// <summary>Length of the colour table the skin shader reads (region index → colour).</summary>
        public const int TableSize = 8;

        [Tooltip("Sprite material with the skin shader (Deadlock/Skinned Sprite).")]
        public Material material;
        [Tooltip("uGUI material with the skin shader (Deadlock/Skinned UI); each equipment-screen image uses its own instance.")]
        public Material uiMaterial;
        [Tooltip("Equipment screen: the standard player, full body, front (written by the scene builder; empty = none yet).")]
        public Sprite playerFront;
        [Tooltip("Region map of the player front view (same canvas): only the hand / arm regions the player skin uses.")]
        public Texture2D playerFrontRegions;
        public WeaponSkinSet[] weapons = Array.Empty<WeaponSkinSet>();
        public PlayerSkin[] playerSkins = Array.Empty<PlayerSkin>();

        public static bool IsWeaponRegion(SkinRegion region) => region == SkinRegion.WeaponBody;

        public static bool IsPlayerRegion(SkinRegion region) => region >= SkinRegion.GloveBody && region <= SkinRegion.SleeveCamo;

        public WeaponSkinSet WeaponSet(string weaponId)
        {
            foreach (var set in weapons)
                if (set != null && set.weaponId == weaponId)
                    return set;
            return null;
        }

        /// <summary>The weapon skin, or null for the standard look (also for an id that no longer exists).</summary>
        public WeaponSkin FindWeaponSkin(string weaponId, string skinId)
        {
            var set = WeaponSet(weaponId);
            if (set == null || string.IsNullOrEmpty(skinId) || skinId == StandardId)
                return null;
            foreach (var skin in set.skins)
                if (skin != null && skin.skinId == skinId)
                    return skin;
            return null;
        }

        /// <summary>The player skin, or null for the standard look (also for an id that no longer exists).</summary>
        public PlayerSkin FindPlayerSkin(string skinId)
        {
            if (string.IsNullOrEmpty(skinId) || skinId == StandardId)
                return null;
            foreach (var skin in playerSkins)
                if (skin != null && skin.skinId == skinId)
                    return skin;
            return null;
        }

        /// <summary>
        /// Shader colour table for one weapon's images with the current selection: x = hue (0..1), y = saturation,
        /// z = 1 when the region is recoloured. A weapon skin only reaches weapon regions and the player skin only hand
        /// regions, so neither colour can leak onto the other.
        /// </summary>
        public void FillColours(string weaponId, Vector4[] table) => FillColours(weaponId, true, table);

        /// <param name="weaponId">The weapon whose skin applies, or null for none (the player front view).</param>
        /// <param name="player">False: leave the player skin out (the weapon's equipment-screen image has no hands).</param>
        public void FillColours(string weaponId, bool player, Vector4[] table)
        {
            Array.Clear(table, 0, table.Length);
            var weaponSkin = FindWeaponSkin(weaponId, SkinSelection.GetWeaponSkinId(weaponId));
            if (weaponSkin != null)
                foreach (var c in weaponSkin.colours)
                    if (c != null && IsWeaponRegion(c.region))
                        table[(int)c.region] = new Vector4(c.hue / 360f, c.saturation, 1f, 0f);
            var playerSkin = player ? FindPlayerSkin(SkinSelection.PlayerSkinId) : null;
            if (playerSkin != null)
                foreach (var c in playerSkin.colours)
                    if (c != null && IsPlayerRegion(c.region))
                        table[(int)c.region] = new Vector4(c.hue / 360f, c.saturation, 1f, 0f);
        }

        public string WeaponSkinLabel(string weaponId, string skinId) => FindWeaponSkin(weaponId, skinId)?.label ?? StandardLabel;

        public string PlayerSkinLabel(string skinId) => FindPlayerSkin(skinId)?.label ?? StandardLabel;
    }
}
