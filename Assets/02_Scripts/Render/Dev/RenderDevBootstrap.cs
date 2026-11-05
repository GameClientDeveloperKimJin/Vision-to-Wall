using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Render.Dev
{
    /// <summary>Dev scene driver: wires <see cref="MockPointSource"/> into the state machine and the wall.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class RenderDevBootstrap : MonoBehaviour
    {
        [SerializeField] private MockPointSource source;
        [SerializeField] private ExhibitionStateMachine stateMachine;
        [SerializeField] private MediaWallController wall;
        [SerializeField] private bool logTransitions = true;

        private void Awake()
        {
            if (source == null || stateMachine == null || wall == null)
            {
                Debug.LogError("[RenderDev] Assign source, stateMachine and wall.", this);
                return;
            }

            AppConfig config = ConfigService.Load();
            stateMachine.Initialize(config, source);
            wall.Initialize(source, stateMachine);
            stateMachine.StateChanged += OnStateChanged;
        }

        private void OnDestroy()
        {
            if (stateMachine != null) stateMachine.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(ExhibitionState state)
        {
            if (logTransitions) Debug.Log($"[RenderDev] State -> {state} at {Time.unscaledTime:F2}s");
        }
    }
}
