using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Render
{
    /// <summary>
    /// FR-06. Idle (attract) / Active (interactive) state machine.
    /// Idle -> Active on the first frame with Count &gt; 0.
    /// Active -> Idle after <see cref="AppConfig.idleTimeoutSec"/> without such a frame
    /// (also covers a dead network: no packets at all). Timing uses Time.unscaledTime.
    /// </summary>
    public sealed class ExhibitionStateMachine : MonoBehaviour, IExhibitionStateSource
    {
        private AppConfig _config;
        private IPointSource _source;
        private bool _subscribed;
        private float _lastMotionTime;

        public ExhibitionState Current { get; private set; } = ExhibitionState.Idle;

        /// <summary>Raised on the main thread on every transition.</summary>
        public event Action<ExhibitionState> StateChanged;

        /// <summary>Time.unscaledTime of the last frame with Count &gt; 0.</summary>
        public float LastMotionTime => _lastMotionTime;

        /// <param name="config">Runtime config. idleTimeoutSec is read every frame, so panel edits apply live.</param>
        public void Initialize(AppConfig config, IPointSource source)
        {
            Unsubscribe();
            _config = config;
            _source = source;
            Current = ExhibitionState.Idle;
            if (isActiveAndEnabled) Subscribe();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed || _source == null) return;
            _source.FrameReceived += OnFrameReceived;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _source.FrameReceived -= OnFrameReceived;
            _subscribed = false;
        }

        private void OnFrameReceived(MotionFrame frame)
        {
            if (frame.Count <= 0) return;

            _lastMotionTime = Time.unscaledTime;
            if (Current == ExhibitionState.Idle) SetState(ExhibitionState.Active);
        }

        private void Update()
        {
            if (Current != ExhibitionState.Active) return;

            float timeout = _config != null ? _config.idleTimeoutSec : 5f;
            if (Time.unscaledTime - _lastMotionTime >= timeout) SetState(ExhibitionState.Idle);
        }

        private void SetState(ExhibitionState next)
        {
            if (Current == next) return;
            Current = next;
            StateChanged?.Invoke(next);
        }
    }
}
