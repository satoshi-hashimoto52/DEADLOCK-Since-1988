using UnityEngine;

namespace Deadlock.Input
{
    /// <summary>
    /// Applies the "control transparency" setting to the on-screen controls (attack, ADS, reload, jump, posture, weapon
    /// buttons and the move stick: icons, plates, rings and state colours alike) through one CanvasGroup on their root:
    /// every element keeps its own alpha and is multiplied by (1 − transparency / 100), so the existing semi-transparent
    /// looks and the disabled / active differences stay, and re-applying never compounds. Touches still work at 100 %
    /// (the CanvasGroup keeps blocking raycasts; nothing is deactivated). The menu button, settings, HUD and logo are not
    /// under this root and are unaffected. While the layout editor is open the controls are shown at full strength.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class TouchControlsOpacity : MonoBehaviour
    {
        CanvasGroup group;
        bool editing;

        /// <summary>The multiplier currently applied (1 = normal look).</summary>
        public float Alpha => group != null ? group.alpha : 1f;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.interactable = true;
            group.blocksRaycasts = true;
            group.ignoreParentGroups = false;
            Apply();
        }

        void OnEnable()
        {
            ControlSettings.Changed += Apply;
            Apply();
        }

        void OnDisable() => ControlSettings.Changed -= Apply;

        /// <summary>Layout editor: show at full strength while editing (the saved value is not changed).</summary>
        public void SetEditing(bool on)
        {
            editing = on;
            Apply();
        }

        void Apply()
        {
            if (group == null)
                return;
            group.alpha = editing ? 1f : 1f - ControlSettings.TouchControlTransparency / 100f;
        }
    }
}
