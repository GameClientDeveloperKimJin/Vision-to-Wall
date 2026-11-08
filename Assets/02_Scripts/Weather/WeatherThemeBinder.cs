using System;
using VisionToWall.Core;

namespace VisionToWall.Weather
{
    /// <summary>
    /// Pushes the current weather theme to the wall and the boss attack look: once on <see cref="Bind"/>, then on
    /// every <see cref="IWeatherSource.Changed"/>. Nothing runs per frame. AppBootstrap usage:
    /// <code>
    /// weatherService.Initialize(config);
    /// _weatherBinder = new WeatherThemeBinder(weatherTheme);
    /// _weatherBinder.Bind(weatherService, wall, combatPresenter);   // after wall/combatPresenter Initialize
    /// // OnDestroy: _weatherBinder.Unbind();
    /// </code>
    /// </summary>
    public sealed class WeatherThemeBinder
    {
        private readonly WeatherTheme _theme;
        private readonly Action<WeatherKind> _onChanged;
        private IWeatherSource _source;
        private IWallBaseTheme _wall;
        private IAttackLookTarget _attack;

        /// <param name="theme">Null uses the built-in palette (sprites then come from nowhere, so the wall keeps
        /// colors and veil but shows no weather particles).</param>
        public WeatherThemeBinder(WeatherTheme theme)
        {
            _theme = theme;
            _onChanged = Apply;
        }

        public WeatherKind Applied { get; private set; } = WeatherKind.Unknown;
        public int ApplyCount { get; private set; }

        /// <summary>Either target may be null (that part is skipped).</summary>
        public void Bind(IWeatherSource source, IWallBaseTheme wall, IAttackLookTarget attack)
        {
            Unbind();
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _wall = wall;
            _attack = attack;
            _source.Changed += _onChanged;
            Apply(_source.Current);
        }

        public void Unbind()
        {
            if (_source != null) _source.Changed -= _onChanged;
            _source = null;
        }

        private void Apply(WeatherKind kind)
        {
            WeatherThemeEntry entry = _theme != null ? _theme.Get(kind) : WeatherTheme.BuiltIn(kind);
            if (_wall != null)
            {
                _wall.SetBaseColors(entry.wall);
                WeatherParticleShape shape = entry.atmosphere.shape;
                var sprite = _theme != null ? _theme.GetSprite(shape) : null;
                int grid = sprite != null ? _theme.GetSpriteGrid(shape) : 1;
                _wall.SetAtmosphere(entry.atmosphere, sprite, grid);
            }
            _attack?.SetAttackLook(entry.attack);
            Applied = kind;
            ApplyCount++;
        }
    }
}
