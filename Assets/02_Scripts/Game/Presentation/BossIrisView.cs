using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Front iris of the boss, drawn by BossIris.shader, plus the shell's masked emission. At glow 0 the whole boss
    /// is an unlit sculpture. Writes to the materials only when a value changes.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class BossIrisView : MonoBehaviour
    {
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int OpenId = Shader.PropertyToID("_Open");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField, Tooltip("Boss shell. Its material emission is multiplied by the glow.")]
        private Renderer[] shellRenderers;
        [SerializeField, Tooltip("Multiplies the shell base color. The raw texture reads light under the scene light.")]
        private Color shellTint = new Color(0.3f, 0.32f, 0.38f, 1f);
        [SerializeField, Min(0f), Tooltip("Shell emission at glow 1, over the material's value.")]
        private float shellEmissionGain = 3f;

        private Material _material;
        private Material[] _shellMaterials;
        private Color[] _shellEmission;
        private float _glow = -1f;
        private float _open = -1f;

        private void Awake()
        {
            _material = GetComponent<MeshRenderer>().material; // per-instance copy, destroyed in OnDestroy

            int count = shellRenderers != null ? shellRenderers.Length : 0;
            _shellMaterials = new Material[count];
            _shellEmission = new Color[count];
            for (int i = 0; i < count; i++)
            {
                if (shellRenderers[i] == null) continue;
                _shellMaterials[i] = shellRenderers[i].material; // per-instance copy, destroyed in OnDestroy
                _shellEmission[i] = _shellMaterials[i].GetColor(EmissionId) * shellEmissionGain;
                _shellMaterials[i].SetColor(BaseColorId, _shellMaterials[i].GetColor(BaseColorId) * shellTint);
            }
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_shellMaterials == null) return;
            for (int i = 0; i < _shellMaterials.Length; i++)
            {
                if (_shellMaterials[i] != null) Destroy(_shellMaterials[i]);
            }
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
                for (int i = 0; i < _shellMaterials.Length; i++)
                {
                    if (_shellMaterials[i] != null) _shellMaterials[i].SetColor(EmissionId, _shellEmission[i] * glow);
                }
            }
        }
    }
}
