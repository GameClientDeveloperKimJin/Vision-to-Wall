using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// Upper-right neon joint figure that mirrors the player's pose on the media wall (spec 53).
    /// Shown only in <see cref="GamePhase.MediaWall"/>, fades out over <c>awakenTransitionSec</c> on Awaken, hidden in
    /// Combat and Result, fades back in on return. Pose smoothing keeps running while hidden so the figure does not
    /// jump when it reappears; drawing stops (canvas disabled) while the alpha is 0.
    /// Without a pose source (webcam mode, <c>udpPosePort</c> 0) the whole panel stays hidden.
    /// </summary>
    public sealed class MiniCharacterView : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private NeonFigureGraphic figure;
        [SerializeField, Min(0f)] private float fadeInSec = 0.6f;
        [SerializeField, Min(0f), Tooltip("Used when Initialize gets no AppConfig.")]
        private float defaultAwakenSec = 2f;

        private readonly PoseSmoother _smoother = new PoseSmoother();
        private IPoseSource _pose;
        private IGamePhaseSource _phase;
        private float _awakenSec;
        private float _alpha;
        private bool _initialized;

        public PoseSmoother Smoother => _smoother;
        /// <summary>Phase alpha (CanvasGroup), 0..1.</summary>
        public float PanelAlpha => _alpha;
        /// <summary>Figure alpha inside the panel: 0 when nobody is tracked.</summary>
        public float FigurePresence => _smoother.Presence;
        public bool HasPoseSource => _pose != null;

        public void Initialize(IPoseSource pose, IGamePhaseSource phase) => Initialize(pose, phase, null);

        /// <summary>
        /// <paramref name="config"/> (optional) supplies <c>awakenTransitionSec</c> and the camera aspect.
        /// Pass <paramref name="pose"/> null to keep the panel hidden (webcam mode).
        /// </summary>
        public void Initialize(IPoseSource pose, IGamePhaseSource phase, AppConfig config)
        {
            Unsubscribe();
            _pose = pose;
            _phase = phase;
            _awakenSec = config != null ? config.awakenTransitionSec : defaultAwakenSec;
            if (config != null && config.cameraHeight > 0) _smoother.Aspect = (float)config.cameraWidth / config.cameraHeight;

            _smoother.Reset();
            if (figure != null) figure.SetSource(_smoother);
            if (_pose != null)
            {
                _pose.PoseReceived += OnPose;
                if (_pose.LastPoseTime >= 0f) _smoother.Push(_pose.Latest, _pose.LastPoseTime);
            }

            _alpha = _pose != null ? MiniCharacterFade.InitialAlpha(CurrentPhase) : 0f;
            _initialized = true;
            ApplyAlpha();
        }

        private GamePhase CurrentPhase => _phase != null ? _phase.Phase : GamePhase.MediaWall;

        private void Awake()
        {
            if (!_initialized)
            {
                _alpha = 0f;
                ApplyAlpha();
            }
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            if (_pose != null) _pose.PoseReceived -= OnPose;
        }

        private void OnPose(PoseFrame frame) => _smoother.Push(frame, Time.unscaledTime);

        private void Update()
        {
            if (_pose == null) return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, PoseSmoother.MaxFrameDeltaSec);
            _smoother.Tick(Time.unscaledTime, dt);

            float alpha = MiniCharacterFade.Step(CurrentPhase, _alpha, dt, _awakenSec, fadeInSec);
            if (alpha != _alpha)
            {
                _alpha = alpha;
                ApplyAlpha();
            }

            if (_alpha > 0f && figure != null) figure.Refresh();
        }

        private void ApplyAlpha()
        {
            if (group != null) group.alpha = _alpha;
            if (canvas != null) canvas.enabled = _alpha > 0f;
        }
    }
}
