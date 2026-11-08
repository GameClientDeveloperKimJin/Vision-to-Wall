using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Weather.Tests
{
    public sealed class WeatherThemeBinderTests
    {
        private sealed class FakeSource : IWeatherSource
        {
            public WeatherKind Current { get; set; }
            public bool IsOverride => false;
            public event Action<WeatherKind> Changed;
            public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
            public void Raise(WeatherKind k) { Current = k; Changed?.Invoke(k); }
        }

        private sealed class FakeWall : IWallBaseTheme
        {
            public readonly List<WeatherWallColors> Colors = new List<WeatherWallColors>();
            public readonly List<(WeatherAtmosphere atmo, Texture2D sprite, int grid)> Atmos =
                new List<(WeatherAtmosphere, Texture2D, int)>();
            public void SetBaseColors(in WeatherWallColors colors) => Colors.Add(colors);
            public void SetAtmosphere(in WeatherAtmosphere atmosphere, Texture2D sprite, int spriteGrid) =>
                Atmos.Add((atmosphere, sprite, spriteGrid));
        }

        private sealed class FakeAttack : IAttackLookTarget
        {
            public readonly List<WeatherAttackLook> Looks = new List<WeatherAttackLook>();
            public void SetAttackLook(in WeatherAttackLook look) => Looks.Add(look);
        }

        private WeatherTheme _theme;

        [SetUp]
        public void SetUp()
        {
            _theme = UnityEditor.AssetDatabase.LoadAssetAtPath<WeatherTheme>("Assets/02_Scripts/Weather/WeatherTheme.asset");
            Assume.That(_theme, Is.Not.Null);
        }

        [Test]
        public void Bind_AppliesCurrentOnce_ThenEveryChange()
        {
            var source = new FakeSource { Current = WeatherKind.Cloudy };
            var wall = new FakeWall();
            var attack = new FakeAttack();
            var binder = new WeatherThemeBinder(_theme);

            binder.Bind(source, wall, attack);
            Assert.AreEqual(1, wall.Colors.Count);
            Assert.AreEqual(WeatherParticleShape.Puff, wall.Atmos[0].atmo.shape);
            Assert.IsNotNull(wall.Atmos[0].sprite, "cloud sprite from the asset");
            Assert.AreEqual(1, attack.Looks.Count);

            source.Raise(WeatherKind.Rain);
            Assert.AreEqual(2, wall.Atmos.Count);
            Assert.AreEqual(WeatherParticleShape.Streak, wall.Atmos[1].atmo.shape);
            Assert.AreEqual(2, wall.Atmos[1].grid, "rain sheet is 2x2");
            Assert.AreEqual(1.15f, attack.Looks[1].sfxPitch, 1e-6f);
            Assert.AreEqual(WeatherKind.Rain, binder.Applied);
        }

        [Test]
        public void Unknown_SendsDefaultAtmosphereWithoutSprite()
        {
            var source = new FakeSource { Current = WeatherKind.Unknown };
            var wall = new FakeWall();
            new WeatherThemeBinder(_theme).Bind(source, wall, null);
            Assert.AreEqual(WeatherParticleShape.Dot, wall.Atmos[0].atmo.shape);
            Assert.IsNull(wall.Atmos[0].sprite, "no dot sprite: the wall keeps its own ambient dots");
            Assert.AreEqual(new Color(0.03f, 0.06f, 0.16f), wall.Colors[0].backgroundTop);
        }

        [Test]
        public void Unbind_StopsUpdates()
        {
            var source = new FakeSource { Current = WeatherKind.Clear };
            var wall = new FakeWall();
            var binder = new WeatherThemeBinder(_theme);
            binder.Bind(source, wall, null);
            binder.Unbind();
            source.Raise(WeatherKind.Snow);
            Assert.AreEqual(1, wall.Colors.Count);
            Assert.AreEqual(0, source.Subscribers);
        }

        [Test]
        public void AllKinds_HaveSpritesExceptDefault()
        {
            foreach (WeatherKind k in new[] { WeatherKind.Clear, WeatherKind.Cloudy, WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Fog })
                Assert.IsNotNull(_theme.GetSprite(_theme.Get(k).atmosphere.shape), k.ToString());
        }
    }
}
