using Deadlock.Combat;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.Network
{
    /// <summary>
    /// The other player as seen by you: an original blocky placeholder (body, head, gun / knife) posed by the synced
    /// state so posture, facing and aim are easy to read: crouched (shorter), prone (lying along the facing), sliding
    /// (leaning back), sprinting (leaning forward), dead (on its side, grey). Muzzle flash on accepted shots and a
    /// knife swing when the owner swings. Smoothed toward the latest state. Hidden on its owner's own machine.
    /// The final retro look is not decided here.
    /// </summary>
    public sealed class AvatarView : MonoBehaviour
    {
        [SerializeField] Transform model;
        [Tooltip("Pivot at the feet; posed (rotated / squashed) per posture.")]
        [SerializeField] Transform body;
        [SerializeField] Transform head;
        [Tooltip("Arms + weapons; pitched with the aim.")]
        [SerializeField] Transform arms;
        [SerializeField] GameObject gun;
        [SerializeField] GameObject knife;
        [SerializeField] GameObject muzzleFlash;
        [SerializeField] Renderer[] tinted;
        [SerializeField] Color hostColor = new Color(0.25f, 0.45f, 0.95f);
        [SerializeField] Color clientColor = new Color(0.95f, 0.55f, 0.15f);
        [SerializeField] Color deadColor = new Color(0.4f, 0.4f, 0.4f);
        [SerializeField, Min(1f)] float smoothing = 18f;

        NetworkAvatar avatar;
        Renderer[] allRenderers;
        Color baseColor;
        bool deadShown;
        float flashUntil;
        float swingStart = -10f;
        byte lastSwingSeq;
        bool initialised;
        Vector3 armsRest;

        public bool IsVisible { get; private set; }
        public bool MuzzleFlashVisible => muzzleFlash.activeSelf;

        public void Init(NetworkAvatar owner, bool ownedLocally)
        {
            avatar = owner;
            armsRest = arms.localPosition;
            allRenderers = model.GetComponentsInChildren<Renderer>(true);
            // Your own body is not drawn (you see the first-person view instead).
            IsVisible = !ownedLocally;
            foreach (var r in allRenderers)
                r.enabled = IsVisible;
            baseColor = owner.OwnerClientId == 0 ? hostColor : clientColor;
            SetColor(baseColor);
            muzzleFlash.SetActive(false);
            lastSwingSeq = owner.State.Value.SwingSeq;
            initialised = false;
        }

        public void MuzzleFlash() => flashUntil = Time.time + 0.06f;

        void SetColor(Color color)
        {
            foreach (var r in tinted)
                r.material.color = color;
        }

        void LateUpdate()
        {
            if (avatar == null || !avatar.IsSpawned)
                return;
            var s = avatar.State.Value;
            bool dead = avatar.Dead.Value;

            // Follow the root (already at the latest state) smoothly in world space.
            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            Vector3 targetPos = avatar.transform.position;
            Quaternion targetRot = avatar.transform.rotation;
            if (!initialised || (model.position - targetPos).sqrMagnitude > 9f)
            {
                model.SetPositionAndRotation(targetPos, targetRot);
                initialised = true;
            }
            else
            {
                model.SetPositionAndRotation(Vector3.Lerp(model.position, targetPos, k), Quaternion.Slerp(model.rotation, targetRot, k));
            }

            // Pose.
            Vector3 pos = Vector3.zero;
            Vector3 euler = Vector3.zero;
            Vector3 scale = Vector3.one;
            if (dead)
            {
                euler = new Vector3(0f, 0f, 90f);
                pos = new Vector3(0.9f, 0.2f, 0f);
            }
            else
            {
                switch (s.PostureValue)
                {
                    case Posture.Crouching:
                        scale = new Vector3(1f, 0.62f, 1f);
                        break;
                    case Posture.Prone:
                        euler = new Vector3(90f, 0f, 0f); // lying forward, head toward the facing
                        pos = new Vector3(0f, 0.2f, -1.45f);
                        break;
                    case Posture.Sliding:
                        euler = new Vector3(-35f, 0f, 0f);
                        scale = new Vector3(1f, 0.6f, 1f);
                        pos = new Vector3(0f, 0f, 0.35f);
                        break;
                    default:
                        if (s.Has(AvatarState.Flag.Sprinting))
                            euler = new Vector3(14f, 0f, 0f);
                        break;
                }
            }
            body.localPosition = Vector3.Lerp(body.localPosition, pos, k);
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(euler), k);
            body.localScale = Vector3.Lerp(body.localScale, scale, k);

            // Aim: head and arms follow the pitch (relative to the body pose).
            float pitch = dead ? 0f : s.Pitch;
            if (!dead && s.PostureValue == Posture.Prone)
                pitch -= 90f;
            Quaternion aim = Quaternion.Euler(-pitch, 0f, 0f);
            head.localRotation = aim;
            arms.localRotation = aim;
            // Aiming down sights: the gun comes up to the face.
            bool aiming = !dead && s.Has(AvatarState.Flag.Aiming);
            arms.localPosition = Vector3.Lerp(arms.localPosition, armsRest + (aiming ? new Vector3(-0.08f, 0.14f, 0f) : Vector3.zero), k);

            // Weapon in hand.
            bool switching = s.Has(AvatarState.Flag.Switching);
            gun.SetActive(!dead && !switching && s.SlotValue == WeaponSlot.Gun);
            knife.SetActive(!dead && !switching && s.SlotValue == WeaponSlot.Melee);

            if (s.SwingSeq != lastSwingSeq)
            {
                lastSwingSeq = s.SwingSeq;
                swingStart = Time.time;
            }
            float swing = Mathf.Clamp01((Time.time - swingStart) / 0.35f);
            if (swing < 1f)
                arms.localRotation = aim * Quaternion.Euler(Mathf.Sin(swing * Mathf.PI) * 70f, Mathf.Sin(swing * Mathf.PI) * -40f, 0f);

            muzzleFlash.SetActive(IsVisible && Time.time < flashUntil && gun.activeSelf);

            if (dead != deadShown)
            {
                deadShown = dead;
                SetColor(dead ? deadColor : baseColor);
            }
        }
    }
}
