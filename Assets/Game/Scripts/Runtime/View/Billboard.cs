using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// Turns a world sprite to face the camera around the Y axis only (Doom / Project Warlock style).
    /// Hit colliders live on the parent and are not rotated.
    /// </summary>
    public sealed class Billboard : MonoBehaviour
    {
        Camera target;

        void LateUpdate()
        {
            if (target == null)
            {
                target = Camera.main;
                if (target == null)
                    return;
            }
            Vector3 toCamera = target.transform.position - transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-toCamera);
        }
    }
}
