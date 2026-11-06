using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Light bolt on a pooled projectile. The pool moves the object before activating it, so clearing here starts
    /// the trail at the muzzle instead of drawing a streak from the previous shot.
    /// </summary>
    public sealed class ProjectileVisual : MonoBehaviour
    {
        [SerializeField] private TrailRenderer trail;

        private void OnEnable()
        {
            if (trail != null) trail.Clear();
        }
    }
}
