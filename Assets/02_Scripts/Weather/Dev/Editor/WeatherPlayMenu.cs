using System;
using UnityEditor;
using UnityEngine;
using VisionToWall.Core;
using VisionToWall.Game;
using VisionToWall.Render;

namespace VisionToWall.Weather.Dev.Editor
{
    /// <summary>
    /// Human check for R5b before AppBootstrap wires the weather: during Play in Main, pushes a weather theme to the
    /// real MediaWall and CombatPresenter through <see cref="WeatherThemeBinder"/>. Editor only; nothing is saved.
    /// Menu: VisionToWall/Weather (Play)/...
    /// </summary>
    internal static class WeatherPlayMenu
    {
        private const string Root = "VisionToWall/Weather (Play)/";
        private const string ThemePath = "Assets/02_Scripts/Weather/WeatherTheme.asset";

        private sealed class ManualSource : IWeatherSource
        {
            public WeatherKind Current { get; private set; }
            public bool IsOverride => true;
            public event Action<WeatherKind> Changed;

            public void Set(WeatherKind kind)
            {
                Current = kind;
                Changed?.Invoke(kind);
            }
        }

        private static ManualSource _source;
        private static WeatherThemeBinder _binder;

        [MenuItem(Root + "Default (off)")] private static void Off() => Apply(WeatherKind.Unknown);
        [MenuItem(Root + "Clear")] private static void Clear() => Apply(WeatherKind.Clear);
        [MenuItem(Root + "Cloudy")] private static void Cloudy() => Apply(WeatherKind.Cloudy);
        [MenuItem(Root + "Rain")] private static void Rain() => Apply(WeatherKind.Rain);
        [MenuItem(Root + "Snow")] private static void Snow() => Apply(WeatherKind.Snow);
        [MenuItem(Root + "Fog")] private static void Fog() => Apply(WeatherKind.Fog);

        [MenuItem(Root + "Default (off)", true)]
        [MenuItem(Root + "Clear", true)]
        [MenuItem(Root + "Cloudy", true)]
        [MenuItem(Root + "Rain", true)]
        [MenuItem(Root + "Snow", true)]
        [MenuItem(Root + "Fog", true)]
        private static bool CanApply() => EditorApplication.isPlaying;

        private static void Apply(WeatherKind kind)
        {
            var wall = UnityEngine.Object.FindObjectOfType<MediaWallController>();
            var combat = UnityEngine.Object.FindObjectOfType<CombatPresenter>();
            if (wall == null)
            {
                Debug.LogWarning("[Weather] Play menu: no MediaWallController in the scene.");
                return;
            }

            if (_binder == null || _source == null)
            {
                _source = new ManualSource();
                _binder = new WeatherThemeBinder(AssetDatabase.LoadAssetAtPath<WeatherTheme>(ThemePath));
                _source.Set(kind);
                _binder.Bind(_source, wall, combat);
                EditorApplication.playModeStateChanged += Reset;
            }
            else
            {
                _source.Set(kind);
            }
            Debug.Log("[Weather] Play menu: " + kind);
        }

        private static void Reset(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode) return;
            _binder?.Unbind();
            _binder = null;
            _source = null;
            EditorApplication.playModeStateChanged -= Reset;
        }
    }
}
