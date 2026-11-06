using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Combat background: grid, rays and data pillars from ArenaBackdrop.shader. The shader writes clip space
    /// directly and the quad only has to stay inside the camera frustum. The renderer is off while hidden, so the
    /// media wall costs and looks the same as before.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class ArenaBackdrop : MonoBehaviour
    {
        private static readonly int RevealId = Shader.PropertyToID("_Reveal");
        private static readonly int VanishId = Shader.PropertyToID("_VanishPoint");

        [SerializeField, Min(0.01f), Tooltip("Distance in front of the camera. Only keeps the quad from being culled.")]
        private float cameraDistance = 15f;

        private MeshRenderer _renderer;
        private Material _material;
        private float _reveal = -1f;

        private void Awake() => EnsureSetup();

        private void EnsureSetup()
        {
            if (_material != null) return;

            _renderer = GetComponent<MeshRenderer>();
            _material = _renderer.material; // per-instance copy, destroyed in OnDestroy
            _renderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        /// <summary>One-time setup. The rays spread from the boss's spot on screen.</summary>
        public void Bind(Camera camera, Vector2 vanishPoint)
        {
            EnsureSetup(); // the caller's Awake may run first
            Transform cam = camera.transform;
            transform.SetPositionAndRotation(cam.position + cam.forward * cameraDistance, cam.rotation);
            _material.SetVector(VanishId, vanishPoint);
        }

        public void SetReveal(float reveal)
        {
            if (_material == null || Mathf.Approximately(reveal, _reveal)) return;

            _reveal = reveal;
            _renderer.enabled = reveal > 0.001f;
            _material.SetFloat(RevealId, reveal);
        }
    }
}
