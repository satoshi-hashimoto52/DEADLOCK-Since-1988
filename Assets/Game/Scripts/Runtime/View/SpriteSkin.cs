using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// Puts a skin on one first-person sprite: the catalog's skin material plus a per-renderer property block with the
    /// image's region map and the colour table (no texture is copied or re-coloured on the CPU). Any sprite with a region
    /// map of the same canvas can be skinned this way (e.g. the knife's attack poses once they are drawn); a sprite
    /// without one keeps its material and drawn colours.
    /// </summary>
    public static class SpriteSkin
    {
        public static readonly int RegionsId = Shader.PropertyToID("_SkinRegions");
        public static readonly int ColoursId = Shader.PropertyToID("_SkinColours");

        static MaterialPropertyBlock block;

        /// <returns>True when the renderer now draws with the skin shader.</returns>
        public static bool Apply(SpriteRenderer renderer, Texture2D regions, SkinCatalog catalog, Vector4[] colours)
        {
            if (renderer == null || regions == null || catalog == null || catalog.material == null)
                return false;
            if (renderer.sharedMaterial != catalog.material)
                renderer.sharedMaterial = catalog.material;
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetTexture(RegionsId, regions);
            block.SetVectorArray(ColoursId, colours);
            renderer.SetPropertyBlock(block);
            return true;
        }

        /// <summary>The colour table a renderer currently uses (tests / checks); null when it has none.</summary>
        public static Vector4[] CurrentColours(SpriteRenderer renderer)
        {
            var b = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(b);
            if (b.isEmpty)
                return null;
            var list = new System.Collections.Generic.List<Vector4>();
            b.GetVectorArray(ColoursId, list);
            return list.Count > 0 ? list.ToArray() : null;
        }
    }
}
