using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Front iris of the boss, drawn by BossIris.shader. Its glow and eyelid are values rather than baked textures.
    /// Writes to the material only when a value changes.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class BossIrisView : MonoBehaviour
    {
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int OpenId = Shader.PropertyToID("_Open");

        private Material _material;
        private float _glow = -1f;
        private float _open = -1f;

        private void Awake()
        {
            _material = GetComponent<MeshRenderer>().material; // per-instance copy, destroyed in OnDestroy
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public void Apply(float open, float glow)
        {
            if (_material == null) return;

            if (!Mathf.Approximately(open, _open))
            {
                _open = open;
                _material.SetFloat(OpenId, open);
            }
            if (!Mathf.Approximately(glow, _glow))
            {
                _glow = glow;
                _material.SetFloat(GlowId, glow);
            }
        }
    }
}
