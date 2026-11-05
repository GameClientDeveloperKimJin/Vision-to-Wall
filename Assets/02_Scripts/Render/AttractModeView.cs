using TMPro;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Render
{
    /// <summary>
    /// FR-06 Idle presentation: guide text with a slow breathing pulse plus a calm ambient particle layer.
    /// Idle -> Active: text fades out over 0.3 s. Active -> Idle: text fades in over 1 s.
    /// The text is set once at startup; nothing allocates per frame.
    /// </summary>
    public sealed class AttractModeView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text label;
        [SerializeField] private string message = "손을 흔들어 보세요";

        [Tooltip("Font with Hangul glyphs. If empty and the label font lacks the glyphs, a dynamic font is built from the OS font below.")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private string systemFontFamily = "Malgun Gothic";
        [SerializeField] private string systemFontStyle = "Regular";

        [Header("Fade")]
        [SerializeField, Min(0.01f)] private float fadeOutSec = 0.3f;
        [SerializeField, Min(0.01f)] private float fadeInSec = 1f;

        [Header("Idle pulse")]
        [SerializeField, Range(0f, 1f)] private float pulseDepth = 0.25f;
        [SerializeField, Min(0f)] private float pulseHz = 0.4f;

        [Header("Ambient particles (always on, calmer when Active)")]
        [SerializeField] private ParticleSystem ambientParticles;
        [SerializeField, Min(0f)] private float ambientRateIdle = 14f;
        [SerializeField, Min(0f)] private float ambientRateActive = 5f;

        private IExhibitionStateSource _state;
        private float _visibility = 1f; // 1 = Idle, 0 = Active
        private float _lastRate = -1f;

        public void Bind(IExhibitionStateSource state)
        {
            _state = state;
            _visibility = state == null || state.Current == ExhibitionState.Idle ? 1f : 0f;
        }

        private void Awake()
        {
            if (label == null) return;
            EnsureFont();
            label.text = message;
        }

        private void EnsureFont()
        {
            if (font != null)
            {
                label.font = font;
                return;
            }

            if (label.font != null && label.font.HasCharacters(message)) return;

            TMP_FontAsset created = TMP_FontAsset.CreateFontAsset(systemFontFamily, systemFontStyle);
            if (created != null)
            {
                created.name = $"{systemFontFamily} (runtime)";
                label.font = created;
            }
            else
            {
                Debug.LogWarning($"[Render] AttractModeView: no Hangul font. Assign one or install '{systemFontFamily}'.", this);
            }
        }

        private void Update()
        {
            bool idle = _state == null || _state.Current == ExhibitionState.Idle;
            float dt = Time.unscaledDeltaTime;
            float target = idle ? 1f : 0f;
            float duration = target > _visibility ? fadeInSec : fadeOutSec;
            _visibility = Mathf.MoveTowards(_visibility, target, dt / duration);

            if (group != null)
            {
                float pulse = 1f - pulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseHz * Mathf.PI * 2f));
                group.alpha = _visibility * pulse;
            }

            if (ambientParticles != null)
            {
                float rate = Mathf.Lerp(ambientRateActive, ambientRateIdle, _visibility);
                if (Mathf.Abs(rate - _lastRate) > 0.01f)
                {
                    ParticleSystem.EmissionModule emission = ambientParticles.emission;
                    emission.rateOverTimeMultiplier = rate;
                    _lastRate = rate;
                }
            }
        }
    }
}
