using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Look of the boss: iris from BossIris.shader, the shell's masked emission, how far it has appeared, and its
    /// reaction to a hit. At glow 0 the whole boss is an unlit sculpture; at reveal 0 it is not drawn at all.
    /// Only touches its own visuals, never the boss root that combat aims from. No allocation after Awake.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class BossIrisView : MonoBehaviour
    {
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int OpenId = Shader.PropertyToID("_Open");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField, Tooltip("Parent of shell and iris. Scaled by reveal, shaken by hits.")]
        private Transform visualRoot;
        [SerializeField, Tooltip("Boss shell. Its material emission is multiplied by the glow.")]
        private Renderer[] shellRenderers;
        [SerializeField, Tooltip("Multiplies the shell base color. The raw texture reads light under the scene light.")]
        private Color shellTint = new Color(0.3f, 0.32f, 0.38f, 1f);
        [SerializeField, Min(0f), Tooltip("Shell emission at glow 1, over the material's value.")]
        private float shellEmissionGain = 3f;

        [Header("Hit reaction")]
        [SerializeField, Min(0.01f)] private float hitFadeSec = 0.4f;
        [SerializeField, Min(0f), Tooltip("Iris glow added at the moment of a hit.")]
        private float hitGlow = 5f;
        [SerializeField, ColorUsage(false, true)] private Color hitEmission = new Color(5f, 0.5f, 0.3f, 1f);
        [SerializeField, Min(0f), Tooltip("Random offset at the moment of a hit, in world units before scale.")]
        private float hitShake = 0.08f;
        [SerializeField, Min(0f), Tooltip("Pushed back along its own axis at the moment of a hit.")]
        private float hitPush = 0.35f;
        [SerializeField, Range(0f, 0.5f), Tooltip("Shrinks by this fraction at the moment of a hit.")]
        private float hitSquash = 0.08f;

        private Material _material;
        private Material[] _shellMaterials;
        private Color[] _shellEmission;
        private Vector3 _visualScale = Vector3.one;
        private Vector3 _visualPosition;
        private float _open = -1f;
        private float _baseGlow;
        private float _reveal = 1f;
        private float _hit;
        private float _writtenGlow = -1f;
        private float _writtenHit = -1f;

        public float Reveal => _reveal;
        public float Hit => _hit;

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

            if (visualRoot != null)
            {
                _visualScale = visualRoot.localScale;
                _visualPosition = visualRoot.localPosition;
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

        public void Apply(float open, float glow, float reveal)
        {
            if (_material == null) return;

            if (!Mathf.Approximately(open, _open))
            {
                _open = open;
                _material.SetFloat(OpenId, open);
            }
            _baseGlow = glow;
            _reveal = reveal;
            if (reveal <= 0f) _hit = 0f; // this object's Update stops while hidden
            Refresh();
        }

        /// <summary>A shot struck the boss.</summary>
        public void PlayHit()
        {
            _hit = 1f;
            Refresh();
        }

        private void Update()
        {
            if (_hit <= 0f) return;

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, GamePhaseMachine.MaxFrameDeltaSec);
            _hit = Mathf.MoveTowards(_hit, 0f, deltaTime / hitFadeSec);
            Refresh();
        }

        private void Refresh()
        {
            if (_material == null) return;

            float glow = _baseGlow + _hit * hitGlow;
            if (!Mathf.Approximately(glow, _writtenGlow) || !Mathf.Approximately(_hit, _writtenHit))
            {
                _writtenGlow = glow;
                _writtenHit = _hit;
                _material.SetFloat(GlowId, glow);
                Color flash = hitEmission * _hit;
                for (int i = 0; i < _shellMaterials.Length; i++)
                {
                    if (_shellMaterials[i] != null) _shellMaterials[i].SetColor(EmissionId, _shellEmission[i] * _baseGlow + flash);
                }
            }

            if (visualRoot == null) return;

            bool visible = _reveal > 0f;
            if (visualRoot.gameObject.activeSelf != visible) visualRoot.gameObject.SetActive(visible);
            if (!visible) return;

            float scale = _reveal * (1f - hitSquash * _hit);
            visualRoot.localScale = _visualScale * scale;
            Vector3 offset = _hit > 0f
                ? Random.insideUnitSphere * (hitShake * _hit) + new Vector3(0f, 0f, -hitPush * _hit)
                : Vector3.zero;
            visualRoot.localPosition = _visualPosition + offset;
        }
    }
}
