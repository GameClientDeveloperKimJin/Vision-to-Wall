using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// Single input source for the game: player position and motion intensity, independent of the sensor type.
    /// Allocation free per frame.
    /// </summary>
    public sealed class PlayerInputTracker : MonoBehaviour, IPlayerInputSource
    {
        private PlayerInputFilter _filter;
        private IPointSource _motion;
        private IPointSource _body;
        private bool _subscribed;

        public PlayerSample Current => _filter != null ? _filter.Current : default;

        /// <param name="motion">Motion stream (udpPort).</param>
        /// <param name="bodyOrNull">Body stream (udpBodyPort), or null to use the motion centroid only.</param>
        public void Initialize(AppConfig config, IPointSource motion, IPointSource bodyOrNull)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (motion == null) throw new ArgumentNullException(nameof(motion));

            Unsubscribe();
            _filter = new PlayerInputFilter(config.UseExternalSensor);
            _motion = motion;
            _body = bodyOrNull;
            if (isActiveAndEnabled) Subscribe();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Update()
        {
            _filter?.Tick(Time.unscaledTime, Time.unscaledDeltaTime);
        }

        private void Subscribe()
        {
            if (_subscribed || _motion == null) return;
            _subscribed = true;
            _motion.FrameReceived += OnMotionFrame;
            if (_body != null) _body.FrameReceived += OnBodyFrame;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            _motion.FrameReceived -= OnMotionFrame;
            if (_body != null) _body.FrameReceived -= OnBodyFrame;
        }

        private void OnMotionFrame(MotionFrame frame) => _filter.AddMotionFrame(frame, Time.unscaledTime);

        private void OnBodyFrame(MotionFrame frame) => _filter.AddBodyFrame(frame, Time.unscaledTime);
    }
}
