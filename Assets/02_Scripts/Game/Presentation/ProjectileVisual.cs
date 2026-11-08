using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// Light bolt on a pooled projectile. The pool moves the object before activating it, so clearing here starts
    /// the trail at the muzzle instead of drawing a streak from the previous shot.
    /// <see cref="ApplyLook"/> changes only how the bolt looks (weather, v2.2): the projectile has no collider and
    /// hits are decided by the combat rules, so none of these values reach the judgement.
    /// </summary>
    public sealed class ProjectileVisual : MonoBehaviour
    {
        [SerializeField] private TrailRenderer trail;
        [SerializeField, Tooltip("Glow billboard. Found among the children when empty.")]
        private Renderer glow;

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _block;
        private bool _cached;
        private float _baseTrailTime;
        private float _baseTrailWidth;
        private Vector3 _baseGlowScale;
        private Color _baseTrailColor;
        private Color _baseGlowColor;

        private void OnEnable()
        {
            if (trail != null) trail.Clear();
        }

        /// <summary>Applies a weather look. Call on weather change only (never per frame).</summary>
        public void ApplyLook(in WeatherAttackLook look)
        {
            CacheBase();
            _block ??= new MaterialPropertyBlock();

            if (trail != null)
            {
                trail.time = _baseTrailTime * Positive(look.trailTimeScale);
                trail.widthMultiplier = _baseTrailWidth * Positive(look.trailWidthScale);
                trail.GetPropertyBlock(_block);
                _block.SetColor(ColorId, IsSet(look.trailColor) ? look.trailColor : _baseTrailColor);
                trail.SetPropertyBlock(_block);
            }

            if (glow != null)
            {
                glow.transform.localScale = _baseGlowScale * Positive(look.glowScale);
                glow.GetPropertyBlock(_block);
                _block.SetColor(ColorId, IsSet(look.glowColor) ? look.glowColor : _baseGlowColor);
                glow.SetPropertyBlock(_block);
            }
        }

        private void CacheBase()
        {
            if (_cached) return;
            _cached = true;
            if (glow == null)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r is TrailRenderer) continue;
                    glow = r;
                    break;
                }
            }
            if (trail != null)
            {
                _baseTrailTime = trail.time;
                _baseTrailWidth = trail.widthMultiplier;
                Material m = trail.sharedMaterial;
                _baseTrailColor = m != null && m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
            }
            if (glow != null)
            {
                _baseGlowScale = glow.transform.localScale;
                Material m = glow.sharedMaterial;
                _baseGlowColor = m != null && m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
            }
        }

        private static float Positive(float scale) => scale > 0f ? scale : 1f;

        // An all-zero color means "not set" (default struct): keep the prefab color.
        private static bool IsSet(Color c) => c.r > 0f || c.g > 0f || c.b > 0f;
    }
}
