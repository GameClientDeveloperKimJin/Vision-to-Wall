using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Ring at the aimed spot, sized to the hit radius, filling with aim progress. Readable through the decorative
    /// rays, and it shows the exact area to step out of.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class AimReticle : MonoBehaviour
    {
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

        private BossCombatController _combat;
        private MeshRenderer _renderer;
        private Material _material;
        private float _progress = -1f;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _material = _renderer.material; // per-instance copy, destroyed in OnDestroy
            _renderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public void Bind(BossCombatController combat, float hitRadius)
        {
            _combat = combat;
            float screenHeight = WallSpace.ScreenHeightAt(combat.OverlayCamera, combat.PlayerPlaneDistance);
            transform.localScale = Vector3.one * (hitRadius * screenHeight * 2f);
            transform.rotation = combat.OverlayCamera.transform.rotation;
        }

        private void LateUpdate()
        {
            if (_combat == null || _renderer == null) return;

            bool aiming = _combat.IsAiming;
            if (_renderer.enabled != aiming) _renderer.enabled = aiming;
            if (!aiming) return;

            transform.position = WallSpace.ToWorld(_combat.OverlayCamera, _combat.AimPosition,
                _combat.PlayerPlaneDistance);

            float progress = _combat.AimProgress;
            if (!Mathf.Approximately(progress, _progress))
            {
                _progress = progress;
                _material.SetFloat(ProgressId, progress);
            }
        }
    }
}
