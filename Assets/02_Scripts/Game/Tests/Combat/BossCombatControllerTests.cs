using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game.Tests
{
    /// <summary>Scene-independent pieces of the combat presentation: look math, world mapping and the pool.</summary>
    public sealed class CombatSceneMathTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        private Camera MakeCamera()
        {
            var go = Track(new GameObject("Cam"));
            go.transform.position = new Vector3(0f, 0f, -10f);
            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = 53.13f;
            camera.aspect = 16f / 9f;
            return camera;
        }

        [Test]
        public void WallSpace_RoundTrips()
        {
            Camera camera = MakeCamera();
            var p = new Vector2(0.2f, 0.8f);

            Vector3 world = WallSpace.ToWorld(camera, p, 5f);
            Vector2 back = WallSpace.ToNormalized(camera, world);

            Assert.AreEqual(p.x, back.x, 1e-3f);
            Assert.AreEqual(p.y, back.y, 1e-3f);
            Assert.AreEqual(-5f, world.z, 1e-3f);
        }

        [Test]
        public void WallSpace_ScreenHeightMatchesWorldAtDistance()
        {
            Camera camera = MakeCamera();

            float top = WallSpace.ToWorld(camera, new Vector2(0.5f, 1f), 10f).y;
            float bottom = WallSpace.ToWorld(camera, new Vector2(0.5f, 0f), 10f).y;

            // 53.13 degrees at 10 units: the same 10 units as the orthographic wall camera (size 5).
            Assert.AreEqual(10f, top - bottom, 0.01f);
            Assert.AreEqual(10f, WallSpace.ScreenHeightAt(camera, 10f), 0.01f);
        }

        [Test]
        public void PanTilt_ZeroAtRest()
        {
            Quaternion rest = Quaternion.LookRotation(Vector3.back, Vector3.up);

            BossLookAt.ComputePanTilt(rest, rest, out float pan, out float tilt);

            Assert.AreEqual(0f, pan, 1e-3f);
            Assert.AreEqual(0f, tilt, 1e-3f);
        }

        [Test]
        public void PanTilt_SignsAreInTheBossFrame()
        {
            // Boss looks at the camera (-Z), so its own right is world -X: the viewer's right is the boss's left.
            Quaternion rest = Quaternion.LookRotation(Vector3.back, Vector3.up);

            BossLookAt.ComputePanTilt(rest, Quaternion.LookRotation(new Vector3(1f, 0f, -1f), Vector3.up),
                out float pan, out float tilt);
            Assert.AreEqual(-45f, pan, 0.01f);
            Assert.AreEqual(0f, tilt, 0.01f);

            BossLookAt.ComputePanTilt(rest, Quaternion.LookRotation(new Vector3(0f, 1f, -1f), Vector3.up),
                out pan, out tilt);
            Assert.AreEqual(0f, pan, 0.01f);
            Assert.AreEqual(45f, tilt, 0.01f);
        }

        [Test]
        public void BossLookAt_ApproachesTargetSmoothlyAndReturnsToRest()
        {
            var go = Track(new GameObject("Boss"));
            var look = go.AddComponent<BossLookAt>();
            Quaternion rest = Quaternion.LookRotation(Vector3.back, Vector3.up);
            look.SetRest(rest);

            Vector3 target = new Vector3(5f, 0f, -5f); // 45 degrees away
            look.SetTarget(target);

            look.Tick(0.016f);
            float firstStep = Mathf.Abs(look.PanDeg);
            Assert.Greater(firstStep, 0f);
            Assert.Less(firstStep, 10f, "one frame must not snap");

            for (int i = 0; i < 300; i++) look.Tick(0.016f);
            Assert.AreEqual(-45f, look.PanDeg, 0.1f);

            look.ClearTarget();
            for (int i = 0; i < 300; i++) look.Tick(0.016f);
            Assert.AreEqual(0f, look.PanDeg, 0.1f);
        }

        private ProjectilePool MakePool(GameObject prefab, int layer = CombatLayers.Overlay)
        {
            var go = Track(new GameObject("Pool"));
            var pool = go.AddComponent<ProjectilePool>();
            var so = new UnityEditor.SerializedObject(pool);
            so.FindProperty("projectilePrefab").objectReferenceValue = prefab;
            so.FindProperty("size").intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
            pool.Prepare(layer);
            return pool;
        }

        [Test]
        public void Pool_LaunchMovesAndReleasesAtEnd()
        {
            var prefab = Track(new GameObject("Shot"));
            ProjectilePool pool = MakePool(prefab);

            int shot = pool.Launch(Vector3.zero, Vector3.right, 1f);
            Assert.GreaterOrEqual(shot, 0);
            Assert.AreEqual(1, pool.ActiveCount);

            pool.Tick(0.5f);
            Transform instance = pool.transform.GetChild(shot);
            Assert.AreEqual(0.5f, instance.position.x, 1e-4f);

            pool.Tick(0.6f);
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.IsFalse(instance.gameObject.activeSelf);
        }

        [Test]
        public void Pool_Exhausted_ReturnsMinusOneAndReuses()
        {
            ProjectilePool pool = MakePool(Track(new GameObject("Shot")));

            int a = pool.Launch(Vector3.zero, Vector3.one, 1f);
            int b = pool.Launch(Vector3.zero, Vector3.one, 1f);
            Assert.AreEqual(-1, pool.Launch(Vector3.zero, Vector3.one, 1f));

            pool.Release(a);
            Assert.AreEqual(a, pool.Launch(Vector3.zero, Vector3.one, 1f));
            pool.ReleaseAll();
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.GreaterOrEqual(b, 0);
        }

        [Test]
        public void Pool_AppliesLayerToChildren()
        {
            var prefab = Track(new GameObject("Shot"));
            var visual = new GameObject("Visual");
            visual.transform.SetParent(prefab.transform);
            ProjectilePool pool = MakePool(prefab);

            Assert.AreEqual(CombatLayers.Overlay, pool.transform.GetChild(0).gameObject.layer);
            Assert.AreEqual(CombatLayers.Overlay, pool.transform.GetChild(0).GetChild(0).gameObject.layer);
        }
    }

    public sealed class BossCombatControllerTests
    {
        private sealed class FakeInput : IPlayerInputSource
        {
            public PlayerSample Current { get; set; }
        }

        private readonly List<Object> _created = new List<Object>();
        private BossCombatController _controller;
        private List<GameResult> _results;

        [SetUp]
        public void SetUp()
        {
            var cameraGo = new GameObject("Cam");
            _created.Add(cameraGo);
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraGo.AddComponent<Camera>();

            var bossGo = new GameObject("Boss");
            _created.Add(bossGo);
            var boss = bossGo.AddComponent<BossLookAt>();

            var prefab = new GameObject("Shot");
            _created.Add(prefab);
            var poolGo = new GameObject("Pool");
            _created.Add(poolGo);
            var pool = poolGo.AddComponent<ProjectilePool>();
            var poolSo = new UnityEditor.SerializedObject(pool);
            poolSo.FindProperty("projectilePrefab").objectReferenceValue = prefab;
            poolSo.ApplyModifiedPropertiesWithoutUndo();

            var lineGo = new GameObject("Line");
            _created.Add(lineGo);
            var line = lineGo.AddComponent<LineRenderer>();

            var go = new GameObject("Combat");
            _created.Add(go);
            _controller = go.AddComponent<BossCombatController>();
            var so = new UnityEditor.SerializedObject(_controller);
            so.FindProperty("overlayCamera").objectReferenceValue = camera;
            so.FindProperty("boss").objectReferenceValue = boss;
            so.FindProperty("projectiles").objectReferenceValue = pool;
            so.FindProperty("aimLine").objectReferenceValue = line;
            so.ApplyModifiedPropertiesWithoutUndo();

            var input = new FakeInput
            {
                Current = new PlayerSample { position = new Vector2(0.5f, 0.5f), hasPosition = true },
            };
            _controller.Initialize(new AppConfig(), input);
            _results = new List<GameResult>();
            _controller.Finished += _results.Add;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void BeforeBegin_ReportsFullHpAndIdle()
        {
            Assert.AreEqual(1f, _controller.BossHp01);
            Assert.AreEqual(1f, _controller.PlayerHp01);
            Assert.IsFalse(_controller.IsAiming);
            Assert.IsFalse(_controller.IsRunning);
        }

        [Test]
        public void Begin_RaisesAimStartedThroughController()
        {
            var events = new List<CombatEventType>();
            _controller.CombatEventRaised += e => events.Add(e.type);

            _controller.Begin();

            CollectionAssert.AreEqual(new[] { CombatEventType.AimStarted }, events);
            Assert.IsTrue(_controller.IsAiming);
        }

        [Test]
        public void Abort_RestoresStateWithoutFinished()
        {
            _controller.Begin();

            _controller.Abort();

            Assert.IsEmpty(_results);
            Assert.IsFalse(_controller.IsRunning);
            Assert.IsFalse(_controller.IsAiming);
            Assert.AreEqual(1f, _controller.PlayerHp01);
        }

        [Test]
        public void Abort_BeforeBegin_DoesNothing()
        {
            Assert.DoesNotThrow(() => _controller.Abort());
            Assert.IsEmpty(_results);
        }

        [Test]
        public void Initialize_MissingReferences_Throws()
        {
            var go = new GameObject("Empty");
            _created.Add(go);
            var empty = go.AddComponent<BossCombatController>();

            Assert.Throws<System.InvalidOperationException>(() => empty.Initialize(new AppConfig(), new FakeInput()));
        }
    }
}
