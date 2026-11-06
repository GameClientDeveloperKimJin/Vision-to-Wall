using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI.Dev
{
    /// <summary>Dev scene driver: wires the four mocks into <see cref="EngineerPanel"/> and logs config changes.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class UIDevBootstrap : MonoBehaviour
    {
        [SerializeField] private EngineerPanel panel;
        [SerializeField] private MockDebugViewProvider debugViews;
        [SerializeField] private MockPointSource points;
        [SerializeField] private MockNetworkStats stats;
        [SerializeField] private MockExhibitionState state;
        [SerializeField] private MockGameSources gameSources;
        [SerializeField] private GameHudView hud;
        [SerializeField] private bool logConfigChanges = true;

        private void Awake()
        {
            if (panel == null)
            {
                Debug.LogError("[UIDev] Assign the EngineerPanel.", this);
                return;
            }

            ConfigService.Load();
            panel.Initialize(debugViews, points, stats, state);
            if (debugViews != null) panel.SetCameraInfo(debugViews.DeviceName);
            if (gameSources != null)
            {
                panel.SetGameSources(gameSources, gameSources, stats);
                if (hud != null) hud.Initialize(gameSources, gameSources);
            }
            ConfigService.Changed += OnConfigChanged;
            Debug.Log("[UIDev] Ready. Press F1 to toggle the engineer panel.");
        }

        private void OnDestroy() => ConfigService.Changed -= OnConfigChanged;

        private void OnConfigChanged(AppConfig c)
        {
            if (logConfigChanges) Debug.Log($"[UIDev] Config changed: threshold={c.threshold:0.000} mirror={c.mirror}");
        }
    }
}
