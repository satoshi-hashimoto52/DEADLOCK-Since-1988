using UnityEngine;

namespace Deadlock.UI
{
    /// <summary>
    /// The control icons and brand images used by the UI, in one asset (Assets/Game/Data/UI_ControlIcons.asset).
    /// The scene builder fills it from Assets/Game/Art/UI and hands it to the touch controls, the menu and the online
    /// lobby, so rebuilding scenes keeps every reference. To swap art, replace the PNG (keep its .meta) and rebuild.
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Control Icon Set", fileName = "UI_ControlIcons")]
    public sealed class ControlIconSet : ScriptableObject
    {
        [Header("Touch controls")]
        public Sprite fire;
        public Sprite melee;
        public Sprite ads;
        public Sprite reload;
        public Sprite jump;
        [Tooltip("Posture button icons show the CURRENT posture (not the action the press will do).")]
        public Sprite stand;
        public Sprite crouch;
        public Sprite prone;
        public Sprite slide;
        public Sprite weaponSwap;

        [Header("Menu")]
        public Sprite menu;
        public Sprite settings;

        [Header("Branding")]
        public Sprite titleLogo;
    }
}
