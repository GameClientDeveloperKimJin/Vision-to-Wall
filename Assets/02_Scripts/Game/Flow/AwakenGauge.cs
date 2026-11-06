using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// Pure awaken gauge logic. Motion at or above <see cref="AppConfig.awakenMinMotion"/> charges it over
    /// <see cref="AppConfig.awakenChargeSec"/>. A gap keeps the charge for <see cref="AppConfig.awakenGraceSec"/>
    /// (hand reversal), then it decays at the charge rate. Config is read every tick, so panel edits apply live.
    /// </summary>
    public sealed class AwakenGauge
    {
        private readonly AppConfig _config;
        private float _graceLeft;

        public AwakenGauge(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>0..1</summary>
        public float Charge { get; private set; }

        public bool IsFull => Charge >= 1f;

        public float CooldownLeftSec { get; private set; }

        /// <summary>Empties the gauge and clears the cooldown.</summary>
        public void Reset()
        {
            Charge = 0f;
            _graceLeft = 0f;
            CooldownLeftSec = 0f;
        }

        /// <summary>Blocks charging for <see cref="AppConfig.rearmCooldownSec"/>.</summary>
        public void StartCooldown()
        {
            CooldownLeftSec = _config.rearmCooldownSec;
        }

        public void Tick(float motion, float deltaTime)
        {
            if (CooldownLeftSec > 0f)
            {
                CooldownLeftSec = Mathf.Max(0f, CooldownLeftSec - deltaTime);
                return;
            }

            if (motion >= _config.awakenMinMotion)
            {
                _graceLeft = _config.awakenGraceSec;
                Charge = Mathf.Clamp01(Charge + deltaTime / _config.awakenChargeSec);
                return;
            }

            // Only the part of deltaTime past the grace period decays, so a long frame stays exact.
            float decayTime = Mathf.Max(0f, deltaTime - _graceLeft);
            _graceLeft = Mathf.Max(0f, _graceLeft - deltaTime);
            Charge = Mathf.Clamp01(Charge - decayTime / _config.awakenChargeSec);
        }
    }
}
