using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Turns the boss toward a world point with <see cref="Quaternion.Slerp"/>. The model front is +Z.
    /// The rest rotation is the one at <see cref="SetRest"/>; pan and tilt are measured from it.
    /// </summary>
    public sealed class BossLookAt : MonoBehaviour
    {
        [SerializeField, Min(0.01f), Tooltip("Time constant of the turn. Larger is slower.")]
        private float turnTauSec = 0.25f;

        private Quaternion _rest = Quaternion.identity;
        private Vector3 _target;
        private bool _hasTarget;

        public float PanDeg { get; private set; }
        public float TiltDeg { get; private set; }

        /// <summary>Pan: + = model turns to its right. Tilt: + = model looks up. Both in -180~180.</summary>
        public static void ComputePanTilt(Quaternion rest, Quaternion current, out float panDeg, out float tiltDeg)
        {
            Vector3 forward = Quaternion.Inverse(rest) * current * Vector3.forward;
            panDeg = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            tiltDeg = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>Sets the rest pose and turns to it immediately.</summary>
        public void SetRest(Quaternion rotation)
        {
            _rest = rotation;
            _hasTarget = false;
            transform.rotation = rotation;
            PanDeg = 0f;
            TiltDeg = 0f;
        }

        public void SetTarget(Vector3 worldPoint)
        {
            _target = worldPoint;
            _hasTarget = true;
        }

        /// <summary>Stops following; <see cref="Tick"/> turns back to the rest rotation.</summary>
        public void ClearTarget() => _hasTarget = false;

        /// <param name="deltaTime">Unscaled and limited by the caller.</param>
        public void Tick(float deltaTime)
        {
            Quaternion goal = _rest;
            if (_hasTarget)
            {
                Vector3 direction = _target - transform.position;
                if (direction.sqrMagnitude > 1e-6f) goal = Quaternion.LookRotation(direction, _rest * Vector3.up);
            }

            transform.rotation = Quaternion.Slerp(transform.rotation, goal, 1f - Mathf.Exp(-deltaTime / turnTauSec));
            ComputePanTilt(_rest, transform.rotation, out float pan, out float tilt);
            PanDeg = pan;
            TiltDeg = tilt;
        }
    }
}
