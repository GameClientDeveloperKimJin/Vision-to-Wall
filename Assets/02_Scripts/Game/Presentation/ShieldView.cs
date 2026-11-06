using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Energy shield sphere at the position combat judges with. Particles gather first, then the sphere fades in.
    /// A parry marks the struck point on the shader. No allocation after Awake.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class ShieldView : MonoBehaviour
    {
        private static readonly int RevealId = Shader.PropertyToID("_Reveal");
        private static readonly int HitDirId = Shader.PropertyToID("_HitDir");
        private static readonly int HitId = Shader.PropertyToID("_Hit");

        [SerializeField] private ParticleSystem gather;
        [SerializeField, Min(0f), Tooltip("Sphere radius over the hit radius. Slightly larger reads as covering the body.")]
        private float radiusOverHit = 1.15f;
        [SerializeField, Min(0.01f)] private float riseSec = 0.2f;
        [SerializeField, Min(0.01f)] private float fallSec = 0.3f;
        [SerializeField, Min(0.01f)] private float hitFadeSec = 0.5f;
        [SerializeField, Range(0, 512)] private int gatherCount = 180;
        [SerializeField, Min(0.01f)] private float gatherStartOverRadius = 2.2f;
        [SerializeField, Min(0.01f)] private float gatherSec = 0.25f;
        [SerializeField] private Color gatherColor = new Color(0.4f, 0.85f, 1f, 1f);

        private BossCombatController _combat;
        private MeshRenderer _renderer;
        private Material _material;
        private ParticleSystem.EmitParams _emit;
        private float _radius;
        private float _reveal;
        private float _hit;
        private bool _raised;

        public float Reveal => _reveal;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _material = _renderer.material; // per-instance copy, destroyed in OnDestroy
            _renderer.enabled = false;
            _emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public void Bind(BossCombatController combat, float hitRadius)
        {
            _combat = combat;
            float screenHeight = WallSpace.ScreenHeightAt(combat.OverlayCamera, combat.PlayerPlaneDistance);
            _radius = hitRadius * screenHeight * radiusOverHit;
            transform.localScale = Vector3.one * (_radius * 2f); // the built-in sphere has radius 0.5
        }

        public void Raise()
        {
            _raised = true;
            if (_combat == null) return;

            Follow();
            EmitGather();
        }

        public void Lower() => _raised = false;

        public void Clear()
        {
            _raised = false;
            _reveal = 0f;
            _hit = 0f;
            if (_material != null)
            {
                _material.SetFloat(RevealId, 0f);
                _material.SetFloat(HitId, 0f);
            }
            if (_renderer != null) _renderer.enabled = false;
        }

        public void Strike(Vector3 worldPoint)
        {
            if (_material == null) return;

            Vector3 direction = worldPoint - transform.position;
            if (direction.sqrMagnitude < 1e-6f) direction = -Vector3.forward;
            _material.SetVector(HitDirId, direction.normalized);
            _hit = 1f;
        }

        private void Update()
        {
            if (_combat == null || _material == null) return;

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, GamePhaseMachine.MaxFrameDeltaSec);
            float nextReveal = Mathf.MoveTowards(_reveal, _raised ? 1f : 0f, deltaTime / (_raised ? riseSec : fallSec));
            float nextHit = Mathf.MoveTowards(_hit, 0f, deltaTime / hitFadeSec);

            if (!Mathf.Approximately(nextReveal, _reveal))
            {
                _reveal = nextReveal;
                _material.SetFloat(RevealId, _reveal);
                _renderer.enabled = _reveal > 0f;
            }
            if (!Mathf.Approximately(nextHit, _hit) || nextHit > 0f)
            {
                _hit = nextHit;
                _material.SetFloat(HitId, _hit);
            }

            if (_reveal > 0f) Follow();
        }

        private void Follow()
        {
            transform.position = WallSpace.ToWorld(_combat.OverlayCamera, _combat.PlayerPosition,
                _combat.PlayerPlaneDistance);
        }

        private void EmitGather()
        {
            if (gather == null) return;

            Vector3 center = transform.position;
            for (int i = 0; i < gatherCount; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                float distance = _radius * gatherStartOverRadius * Random.Range(0.8f, 1.2f);
                _emit.position = center + direction * distance;
                _emit.velocity = -direction * ((distance - _radius * 0.6f) / gatherSec);
                _emit.startLifetime = gatherSec;
                _emit.startSize = Random.Range(0.02f, 0.06f);
                _emit.startColor = gatherColor;
                gather.Emit(_emit, 1);
            }
        }
    }
}
