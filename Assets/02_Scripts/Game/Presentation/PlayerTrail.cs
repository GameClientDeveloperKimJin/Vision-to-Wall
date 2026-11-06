using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>G-FR-05 neon trail along the judged player position, in the overlay camera's view.</summary>
    [RequireComponent(typeof(TrailRenderer))]
    public sealed class PlayerTrail : MonoBehaviour
    {
        private BossCombatController _combat;
        private TrailRenderer _trail;
        private bool _active;

        private void Awake()
        {
            _trail = GetComponent<TrailRenderer>();
            _trail.emitting = false;
        }

        public void Bind(BossCombatController combat) => _combat = combat;

        public void SetActive(bool active)
        {
            if (_trail == null || active == _active) return;

            _active = active;
            if (active && _combat != null)
            {
                Follow(); // start here, not with a streak from the last position
                _trail.Clear();
            }
            _trail.emitting = active;
        }

        private void LateUpdate()
        {
            if (_active && _combat != null) Follow();
        }

        private void Follow()
        {
            transform.position = WallSpace.ToWorld(_combat.OverlayCamera, _combat.PlayerPosition,
                _combat.PlayerPlaneDistance);
        }
    }
}
