using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI.Dev
{
    /// <summary>Dev-only <see cref="IExhibitionStateSource"/>: Active on points, Idle after <c>idleTimeoutSec</c> without points.</summary>
    public sealed class MockExhibitionState : MonoBehaviour, IExhibitionStateSource
    {
        [SerializeField] private MockPointSource source;

        private float _lastMotion = float.NegativeInfinity;

        public ExhibitionState Current { get; private set; } = ExhibitionState.Idle;
        public event Action<ExhibitionState> StateChanged;

        private void Awake()
        {
            if (source == null) source = GetComponent<MockPointSource>();
        }

        private void OnEnable()
        {
            if (source != null) source.FrameReceived += OnFrame;
        }

        private void OnDisable()
        {
            if (source != null) source.FrameReceived -= OnFrame;
        }

        private void OnFrame(MotionFrame frame)
        {
            if (frame.Count > 0) _lastMotion = Time.unscaledTime;
        }

        private void Update()
        {
            bool active = Time.unscaledTime - _lastMotion < ConfigService.Current.idleTimeoutSec;
            ExhibitionState next = active ? ExhibitionState.Active : ExhibitionState.Idle;
            if (next == Current) return;
            Current = next;
            StateChanged?.Invoke(next);
        }
    }
}
