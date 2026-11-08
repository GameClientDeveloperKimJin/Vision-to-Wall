using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VisionToWall.Core;

namespace VisionToWall.Render.Tests
{
    /// <summary>IWallBaseTheme on the real MediaWall prefab (R5b).</summary>
    public sealed class MediaWallWeatherTests
    {
        private const string PrefabPath = "Assets/03_Prefabs/Render/MediaWall.prefab";
        private static readonly int ColorTopId = Shader.PropertyToID("_ColorTop");
        private static readonly int ColorBottomId = Shader.PropertyToID("_ColorBottom");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");

        private GameObject _root;
        private MediaWallController _wall;
        private Material _wallMat;
        private ParticleSystem _ambient;

        // Same values as the prefab and WeatherTheme's Unknown entry.
        private static WeatherWallColors DefaultColors => new WeatherWallColors
        {
            backgroundTop = new Color(0.03f, 0.06f, 0.16f),
            backgroundBottom = new Color(0f, 0f, 0.03f),
            glow = new Color(0.35f, 0.85f, 1f),
            particleLow = new Color(0.2f, 0.45f, 1f, 1f),
            particleHigh = new Color(0.75f, 1f, 1f, 1f),
            ambient = new Color(0.4f, 0.7f, 1f, 0.35f)
        };

        private static WeatherAtmosphere DefaultAtmosphere => new WeatherAtmosphere
        {
            shape = WeatherParticleShape.Dot, velocity = new Vector2(0f, 0.01f), speedJitter = 1f, sway = 0.01f,
            size = 1f, stretch = 1f, density = 1f
        };

        private static WeatherAtmosphere Fog => new WeatherAtmosphere
        {
            shape = WeatherParticleShape.Haze, velocity = new Vector2(0.03f, 0.002f), speedJitter = 0.5f, sway = 0.02f,
            size = 70f, stretch = 1f, density = 0.45f, veil = 0.2f
        };

        [SetUp]
        public void SetUp()
        {
            // renderer.material in edit mode logs a leak notice; the instance is destroyed in TearDown.
            LogAssert.ignoreFailingMessages = true;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assume.That(prefab, Is.Not.Null);
            _root = Object.Instantiate(prefab);
            _wall = _root.GetComponentInChildren<MediaWallController>(true);
            _wall.SetTheme(0f); // runs setup
            var so = new SerializedObject(_wall);
            _wallMat = ((MeshRenderer)so.FindProperty("wallRenderer").objectReferenceValue).sharedMaterial;
            _ambient = (ParticleSystem)so.FindProperty("ambientParticles").objectReferenceValue;
        }

        [TearDown]
        public void TearDown()
        {
            // OnDestroy does not run in edit mode: free the runtime instances the controller created.
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (string field in new[] { "_material", "_weatherMat", "_sunMat", "_sunTex" })
            {
                var obj = typeof(MediaWallController).GetField(field, bf)?.GetValue(_wall) as Object;
                if (obj != null) Object.DestroyImmediate(obj);
            }
            if (_root != null) Object.DestroyImmediate(_root);
            LogAssert.ignoreFailingMessages = false;
        }

        private (Color top, Color bottom, Color glow, Color ambient) Snapshot() =>
            (_wallMat.GetColor(ColorTopId), _wallMat.GetColor(ColorBottomId), _wallMat.GetColor(GlowColorId),
                _ambient != null ? _ambient.main.startColor.color : Color.clear);

        private static Texture2D FakeSprite() => new Texture2D(4, 4);

        private static void AssertClose(Color expected, Color actual, string message)
        {
            const float Eps = 1e-5f;
            Assert.AreEqual(expected.r, actual.r, Eps, message);
            Assert.AreEqual(expected.g, actual.g, Eps, message);
            Assert.AreEqual(expected.b, actual.b, Eps, message);
            Assert.AreEqual(expected.a, actual.a, Eps, message);
        }

        [Test]
        public void DefaultTheme_LeavesWallExactlyAsBefore()
        {
            var before = Snapshot();
            int children = _wall.transform.childCount;

            _wall.SetBaseColors(DefaultColors);
            _wall.SetAtmosphere(DefaultAtmosphere, null, 1);

            var after = Snapshot();
            Assert.AreEqual(before.top, after.top);
            Assert.AreEqual(before.bottom, after.bottom);
            Assert.AreEqual(before.glow, after.glow);
            Assert.AreEqual(before.ambient, after.ambient);
            Assert.IsFalse(_wall.WeatherLayerOn);
            Assert.AreEqual(children, _wall.transform.childCount, "no weather objects created");
        }

        [Test]
        public void Weather_ThenDefault_RestoresWall()
        {
            var before = Snapshot();
            Texture2D sprite = FakeSprite();
            try
            {
                _wall.SetAtmosphere(Fog, sprite, 1);
                Assert.IsTrue(_wall.WeatherLayerOn);
                Assert.AreNotEqual(before.top, Snapshot().top, "fog veil tints the background");

                _wall.SetBaseColors(DefaultColors);
                _wall.SetAtmosphere(DefaultAtmosphere, null, 1);
                Assert.IsFalse(_wall.WeatherLayerOn);
                var after = Snapshot();
                Assert.AreEqual(before.top, after.top);
                Assert.AreEqual(before.bottom, after.bottom);
                Assert.AreEqual(before.glow, after.glow);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void Combat_IsTheSameInEveryWeather()
        {
            _wall.SetTheme(1f);
            var crimsonDefault = Snapshot();

            Texture2D sprite = FakeSprite();
            try
            {
                WeatherWallColors snow = DefaultColors;
                snow.backgroundTop = new Color(0.1f, 0.11f, 0.18f);
                snow.glow = new Color(0.8f, 0.9f, 1f);
                _wall.SetBaseColors(snow);
                _wall.SetAtmosphere(Fog, sprite, 1);

                var crimsonWeather = Snapshot();
                // Lerp(a, b, 1) = a + (b - a): equal up to float rounding, invisible on screen.
                AssertClose(crimsonDefault.top, crimsonWeather.top, "veil and base colors vanish at full crimson");
                AssertClose(crimsonDefault.bottom, crimsonWeather.bottom, "bottom");
                AssertClose(crimsonDefault.glow, crimsonWeather.glow, "glow");
            }
            finally
            {
                Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void WeatherFade_DropsInCombat()
        {
            Texture2D sprite = FakeSprite();
            try
            {
                _wall.SetAtmosphere(Fog, sprite, 1);
                float calm = _wall.WeatherFade;
                _wall.SetTheme(1f);
                // The fade is refreshed in Update; call it the same way the frame loop does.
                typeof(MediaWallController).GetMethod("UpdateWeatherFade",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(_wall, null);
                Assert.AreEqual(1f, calm, 1e-4f);
                Assert.Less(_wall.WeatherFade, 0.5f);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void SpriteMissing_KeepsLayerOff()
        {
            _wall.SetAtmosphere(Fog, null, 1);
            Assert.IsFalse(_wall.WeatherLayerOn);
        }
    }
}
