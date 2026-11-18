using Deadlock.Combat;

namespace Deadlock.View
{
    /// <summary>The three images of a gun, each delivered, approved and registered on its own (Stage 32).</summary>
    public enum WeaponArtView
    {
        /// <summary>Side view of the whole gun: equipment screen and HUD (<see cref="WeaponSkinSet.sideView"/>).</summary>
        Side,
        /// <summary>First person, from the hip (<see cref="WeaponViewProfile.hipSprite"/>).</summary>
        Hip,
        /// <summary>First person, aimed down the sights (<see cref="WeaponViewProfile.adsSprite"/>).</summary>
        Ads,
    }

    public enum WeaponArtState
    {
        /// <summary>
        /// An image is registered. Only images that passed AssetPipeline's checks and the user's approval are ever
        /// registered (the skin catalog's side views, the view profiles written by the weapon art import).
        /// </summary>
        Approved,
        /// <summary>
        /// No image yet: the game shows a stand-in made at runtime (the HUD's name label marked "（仮）"; no weapon
        /// image in first person). No file is made or registered for it.
        /// </summary>
        Placeholder,
    }

    /// <summary>
    /// Which of a gun's images are the approved art and which are still stand-ins. Read from what is registered, so a
    /// newly approved and registered image counts at once, without a list to keep up.
    /// </summary>
    public static class WeaponArtStatus
    {
        public const string PlaceholderMark = "（仮）";

        public static WeaponArtState Of(WeaponDefinition gun, SkinCatalog skins, WeaponArtView view)
        {
            if (gun == null)
                return WeaponArtState.Placeholder;
            bool registered = view switch
            {
                WeaponArtView.Side => skins != null && skins.WeaponSet(gun.weaponId)?.sideView != null,
                WeaponArtView.Hip => gun.viewProfile != null && gun.viewProfile.hipSprite != null,
                _ => gun.viewProfile != null && gun.viewProfile.adsSprite != null,
            };
            return registered ? WeaponArtState.Approved : WeaponArtState.Placeholder;
        }

        /// <summary>
        /// Approval state of a catalog weapon's image (Stage 33). The knife has one first-person image, used for both
        /// first-person views.
        /// </summary>
        public static WeaponArtState Of(WeaponCatalogEntry entry, SkinCatalog skins, WeaponArtView view)
        {
            if (entry == null)
                return WeaponArtState.Placeholder;
            if (!entry.IsKnife)
                return Of(entry.gun, skins, view);
            bool registered = view == WeaponArtView.Side
                ? skins != null && skins.WeaponSet(entry.weaponId)?.sideView != null
                : entry.knife != null && entry.knife.viewProfile != null && entry.knife.viewProfile.sprite != null;
            return registered ? WeaponArtState.Approved : WeaponArtState.Placeholder;
        }

        /// <summary>The gun's name without a trailing "(...)" note (e.g. "HANDGUN (PROVISIONAL)" → "HANDGUN").</summary>
        public static string ShortName(WeaponDefinition gun)
        {
            if (gun == null)
                return "";
            string name = string.IsNullOrEmpty(gun.displayName) ? gun.weaponId : gun.displayName;
            int note = name.IndexOf(" (", System.StringComparison.Ordinal);
            return note > 0 ? name.Substring(0, note) : name;
        }

        /// <summary>The stand-in for a missing side view: the gun's short name, marked as a placeholder.</summary>
        public static string PlaceholderLabel(WeaponDefinition gun) => ShortName(gun) + PlaceholderMark;
    }
}
