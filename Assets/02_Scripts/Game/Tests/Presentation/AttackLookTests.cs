using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game.Tests
{
    /// <summary>IAttackLookTarget (R5b): weather changes only how the bolt looks and sounds.</summary>
    public sealed class AttackLookTests
    {
        private const string ProjectilePath = "Assets/03_Prefabs/Game/Projectile.prefab";
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private GameObject _object;

        [TearDown]
        public void TearDown()
        {
            if (_object != null) Object.DestroyImmediate(_object);
        }

        private static WeatherAttackLook Rain => new WeatherAttackLook
        {
            trailColor = new Color(4f, 0.06f, 0.25f), glowColor = new Color(5f, 0.1f, 0.35f),
            trailTimeScale = 1.8f, trailWidthScale = 0.6f, glowScale = 0.85f, sfxPitch = 1.15f
        };

        private static WeatherAttackLook Default => new WeatherAttackLook
        {
            trailColor = new Color(4f, 0.04f, 0.03f), glowColor = new Color(6f, 0.1f, 0.06f),
            trailTimeScale = 1f, trailWidthScale = 1f, glowScale = 1f, sfxPitch = 1f
        };

        private (ProjectileVisual visual, TrailRenderer trail, Renderer glow) SpawnBolt()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath);
            Assume.That(prefab, Is.Not.Null);
            _object = Object.Instantiate(prefab);
            var visual = _object.GetComponent<ProjectileVisual>();
            var trail = _object.GetComponent<TrailRenderer>();
            Renderer glow = null;
            foreach (Renderer r in _object.GetComponentsInChildren<Renderer>(true))
                if (!(r is TrailRenderer)) glow = r;
            return (visual, trail, glow);
        }

        private static void AssertClose(Color expected, Color actual)
        {
            // MaterialPropertyBlock stores HDR colors with float rounding.
            Assert.AreEqual(expected.r, actual.r, 1e-4f);
            Assert.AreEqual(expected.g, actual.g, 1e-4f);
            Assert.AreEqual(expected.b, actual.b, 1e-4f);
        }

        private static Color BlockColor(Renderer r)
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            return block.GetColor(ColorId);
        }

        [Test]
        public void Bolt_TakesWeatherLook_ThenDefaultRestoresPrefab()
        {
            var (visual, trail, glow) = SpawnBolt();
            float time = trail.time, width = trail.widthMultiplier;
            Vector3 scale = glow.transform.localScale;
            Color glowColor = glow.sharedMaterial.GetColor(ColorId);

            visual.ApplyLook(Rain);
            Assert.AreEqual(time * 1.8f, trail.time, 1e-5f);
            Assert.AreEqual(width * 0.6f, trail.widthMultiplier, 1e-5f);
            Assert.AreEqual(scale.x * 0.85f, glow.transform.localScale.x, 1e-5f);
            AssertClose(Rain.glowColor, BlockColor(glow));
            AssertClose(Rain.trailColor, BlockColor(trail));
            Assert.AreEqual(glowColor, glow.sharedMaterial.GetColor(ColorId), "shared material untouched");

            visual.ApplyLook(Default);
            Assert.AreEqual(time, trail.time, 1e-6f);
            Assert.AreEqual(width, trail.widthMultiplier, 1e-6f);
            Assert.AreEqual(scale, glow.transform.localScale);
            AssertClose(glowColor, BlockColor(glow));
        }

        [Test]
        public void Bolt_HasNoCollider_SoLookCannotChangeHits()
        {
            var (visual, _, _) = SpawnBolt();
            visual.ApplyLook(Rain);
            Assert.IsEmpty(_object.GetComponentsInChildren<Collider>(true));
        }

        [Test]
        public void EmptyLook_KeepsPrefab()
        {
            var (visual, trail, glow) = SpawnBolt();
            float time = trail.time;
            Vector3 scale = glow.transform.localScale;
            Color glowColor = glow.sharedMaterial.GetColor(ColorId);

            visual.ApplyLook(default);
            Assert.AreEqual(time, trail.time, 1e-6f);
            Assert.AreEqual(scale, glow.transform.localScale);
            AssertClose(glowColor, BlockColor(glow));
        }

        [Test]
        public void LaserPitch_IsClamped_AndDefaultsToOne()
        {
            _object = new GameObject("Audio");
            var audio = _object.AddComponent<AudioDirector>();
            Assert.AreEqual(1f, audio.LaserPitch);
            audio.LaserPitch = 1.25f;
            Assert.AreEqual(1.25f, audio.LaserPitch);
            audio.LaserPitch = 9f;
            Assert.AreEqual(2f, audio.LaserPitch);
            audio.LaserPitch = 0f;
            Assert.AreEqual(1f, audio.LaserPitch);
        }

        [Test]
        public void CombatPresenter_SetAttackLook_BeforeInitialize_SetsPitch()
        {
            _object = new GameObject("Presenter");
            var audio = _object.AddComponent<AudioDirector>();
            var presenter = _object.AddComponent<CombatPresenter>();
            var so = new SerializedObject(presenter);
            so.FindProperty("audioDirector").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();

            ((IAttackLookTarget)presenter).SetAttackLook(Rain);
            Assert.AreEqual(1.15f, audio.LaserPitch, 1e-6f);
        }
    }
}
