using Deadlock.View;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>
    /// An equipment-screen image (uGUI) that shows the chosen skin: the same catalog, choice (<see cref="SkinSelection"/>)
    /// and colour table (<see cref="SkinCatalog.FillColours(string, bool, Vector4[])"/>) as the first-person sprites,
    /// drawn with the UI skin shader (Deadlock/Skinned UI). uGUI graphics cannot take material property blocks, so the
    /// image owns ONE material instance, made once and only updated on a change (no new material per change or per
    /// opening), and destroyed with the image.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class SkinnedImage : MonoBehaviour
    {
        SkinCatalog catalog;
        Texture2D regions;
        string weaponId;
        bool player;
        Image image;
        Material instance;
        readonly Vector4[] colours = new Vector4[SkinCatalog.TableSize];

        /// <summary>The colour table the image draws with now (tests / checks).</summary>
        public Vector4[] Colours => colours;
        public Material Instance => instance;

        /// <param name="weaponId">Weapon whose skin applies (side view), or null (player front view).</param>
        /// <param name="player">Whether the player (hand / arm) skin applies.</param>
        public void Init(SkinCatalog catalog, Sprite sprite, Texture2D regions, string weaponId, bool player)
        {
            this.catalog = catalog;
            this.regions = regions;
            this.weaponId = weaponId;
            this.player = player;
            image = GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (catalog != null && catalog.uiMaterial != null && regions != null && instance == null)
            {
                instance = new Material(catalog.uiMaterial) { name = catalog.uiMaterial.name + " (" + name + ")" };
                instance.SetTexture(SpriteSkin.RegionsId, regions);
                image.material = instance;
            }
            Refresh();
        }

        void OnEnable()
        {
            SkinSelection.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => SkinSelection.Changed -= Refresh;

        void OnDestroy()
        {
            if (instance != null)
                Destroy(instance);
        }

        void Refresh()
        {
            if (catalog == null || instance == null)
                return;
            catalog.FillColours(weaponId, player, colours);
            instance.SetVectorArray(SpriteSkin.ColoursId, colours);
        }
    }
}
