using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// Phase changes to wall theme, particle pull, iris, arena, BGM and result slow motion. Only listens to game
    /// state. It is the one place that writes <see cref="Time.timeScale"/>, and it restores it on disable.
    /// </summary>
    public sealed class PhasePresenter : MonoBehaviour
    {
        [SerializeField] private PresentationTuning tuning = new PresentationTuning();
        [SerializeField] private ArenaBackdrop backdrop;
        [SerializeField] private AudioDirector audioDirector;

        private IGamePhaseSource _phase;
        private IWallPresentation _wall;
        private Camera _wallCamera;
        private Camera _overlayCamera;
        private Transform _boss;
        private BossIrisView _iris;
        private PresentationTimeline _timeline;
        private Action<GamePhase> _onPhaseChanged;
        private float _wallCameraDistance;
        private bool _slowMotionWritten;

        public PresentationLook Look => _timeline != null ? _timeline.Current : default;

        /// <param name="wallCamera">Camera that draws the wall; the pull point is projected into its view.</param>
        /// <param name="awakenSec"><see cref="AppConfig.awakenTransitionSec"/>.</param>
        public void Initialize(IGamePhaseSource phase, BossCombatController combat, IWallPresentation wall,
            Camera wallCamera, float awakenSec)
        {
            if (phase == null) throw new ArgumentNullException(nameof(phase));
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (wall == null) throw new ArgumentNullException(nameof(wall));
            if (wallCamera == null) throw new ArgumentNullException(nameof(wallCamera));

            Unsubscribe();

            _phase = phase;
            _wall = wall;
            _wallCamera = wallCamera;
            _overlayCamera = combat.OverlayCamera;
            var look = combat.GetComponentInChildren<BossLookAt>(true);
            _boss = look != null ? look.transform : combat.transform;
            _iris = _boss.GetComponentInChildren<BossIrisView>(true);
            _wallCameraDistance = Mathf.Max(_wallCamera.nearClipPlane + 1f, 1f);
            _timeline = new PresentationTimeline(tuning, awakenSec);
            _timeline.SetPhase(phase.Phase, phase.Result);

            if (backdrop != null)
                backdrop.Bind(_overlayCamera, WallSpace.ToNormalized(_overlayCamera, _boss.position));

            Subscribe();
            Apply(_timeline.Current);
        }

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            Unsubscribe();
            RestoreTimeScale();
        }

        private void OnDestroy() => RestoreTimeScale();

        private void Subscribe()
        {
            if (_phase == null || _onPhaseChanged != null) return;

            _onPhaseChanged = OnPhaseChanged;
            _phase.PhaseChanged += _onPhaseChanged;
        }

        private void Unsubscribe()
        {
            if (_phase == null || _onPhaseChanged == null) return;

            _phase.PhaseChanged -= _onPhaseChanged;
            _onPhaseChanged = null;
        }

        private void OnPhaseChanged(GamePhase phase)
        {
            _timeline.SetPhase(phase, _phase.Result);

            switch (phase)
            {
                case GamePhase.Awaken:
                    if (audioDirector != null)
                    {
                        audioDirector.Play(AudioCue.Awaken);
                        audioDirector.PlayBgm();
                    }
                    break;
                case GamePhase.MediaWall:
                    RestoreTimeScale();
                    if (audioDirector != null) audioDirector.StopBgm();
                    break;
            }
        }

        private void Update()
        {
            if (_timeline == null) return;

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, GamePhaseMachine.MaxFrameDeltaSec);
            Apply(_timeline.Tick(deltaTime, _phase.AwakenCharge));
        }

        private void Apply(PresentationLook look)
        {
            _wall.SetTheme(look.crimson);
            _wall.SetAbsorb(look.absorb, look.absorb > 0f ? PullPoint() : Vector3.zero);

            if (_iris != null) _iris.Apply(look.irisOpen, look.irisGlow, look.bossReveal);
            if (backdrop != null) backdrop.SetReveal(look.backdrop);

            // Only the result phase may leave normal speed, and only a value we wrote is ever put back.
            if (_phase.Phase == GamePhase.Result && look.timeScale < 1f)
            {
                Time.timeScale = look.timeScale;
                _slowMotionWritten = true;
            }
            else
            {
                RestoreTimeScale();
            }
        }

        /// <summary>The boss is drawn by the perspective overlay camera, the particles by the wall camera.</summary>
        private Vector3 PullPoint()
        {
            Vector2 onScreen = WallSpace.ToNormalized(_overlayCamera, _boss.position);
            return _wallCamera.ViewportToWorldPoint(new Vector3(onScreen.x, onScreen.y, _wallCameraDistance));
        }

        private void RestoreTimeScale()
        {
            if (!_slowMotionWritten) return;

            Time.timeScale = 1f;
            _slowMotionWritten = false;
        }
    }
}
