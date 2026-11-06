using System;
using System.Text;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using VisionToWall.Core;
using Object = UnityEngine.Object;

namespace VisionToWall.UI.Tests
{
    public class GameHudModelTests
    {
        private const float Dt = 0.05f;

        private static GameHudModel NewModel() => new GameHudModel(1f, 0.5f, 0.6f);

        private static void Run(GameHudModel m, GamePhase phase, float seconds, float boss = 1f, float player = 1f)
        {
            for (float t = 0f; t < seconds; t += Dt) m.Tick(Dt, phase, boss, player);
        }

        [Test]
        public void MediaWall_StaysFullyHidden()
        {
            GameHudModel m = NewModel();
            Run(m, GamePhase.MediaWall, 2f);
            Assert.AreEqual(0f, m.Alpha);
        }

        [Test]
        public void Awaken_FadesIn()
        {
            GameHudModel m = NewModel();
            m.Tick(0.1f, GamePhase.Awaken, 1f, 1f);
            Assert.That(m.Alpha, Is.GreaterThan(0f).And.LessThan(1f));
            Run(m, GamePhase.Awaken, 1.1f);
            Assert.AreEqual(1f, m.Alpha);
        }

        [Test]
        public void Result_StaysVisible_ThenMediaWallFadesOut()
        {
            GameHudModel m = NewModel();
            Run(m, GamePhase.Combat, 1.2f);
            Run(m, GamePhase.Result, 5f);
            Assert.AreEqual(1f, m.Alpha);

            m.Tick(0.1f, GamePhase.MediaWall, 1f, 1f);
            Assert.That(m.Alpha, Is.GreaterThan(0f).And.LessThan(1f));
            Run(m, GamePhase.MediaWall, 0.6f);
            Assert.AreEqual(0f, m.Alpha);
        }

        [Test]
        public void LongFrame_CountsOnlyMaxDelta()
        {
            GameHudModel m = NewModel();
            m.Tick(3.8f, GamePhase.Awaken, 1f, 1f);
            Assert.AreEqual(GameHudModel.MaxFrameDeltaSec, m.Alpha, 0.0001f);
        }

        [Test]
        public void Hp_MovesGradually_ToTarget()
        {
            GameHudModel m = NewModel();
            m.Tick(Dt, GamePhase.Combat, 0.2f, 0.4f);
            Assert.That(m.BossHp, Is.GreaterThan(0.2f).And.LessThan(1f));
            Assert.That(m.PlayerHp, Is.GreaterThan(0.4f).And.LessThan(1f));

            Run(m, GamePhase.Combat, 1f, 0.2f, 0.4f);
            Assert.AreEqual(0.2f, m.BossHp, 0.0001f);
            Assert.AreEqual(0.4f, m.PlayerHp, 0.0001f);
        }

        [Test]
        public void Percent_JumpsToRealValue_WhileBarMoves()
        {
            GameHudModel m = NewModel();
            m.Tick(Dt, GamePhase.Combat, 0.6f, 0.8f);

            Assert.AreEqual(60, m.BossPercent);
            Assert.AreEqual(80, m.PlayerPercent);
            Assert.That(m.BossHp, Is.GreaterThan(0.6f));

            m.OnPhaseChanged(GamePhase.Awaken);
            Assert.AreEqual(100, m.BossPercent);
            Assert.AreEqual(100, m.PlayerPercent);
        }

        [Test]
        public void Awaken_IgnoresLastFightsHp()
        {
            GameHudModel m = NewModel();
            Run(m, GamePhase.Result, 1f, 0f, 0.4f);
            Assert.AreEqual(0f, m.BossHp);

            m.OnPhaseChanged(GamePhase.Awaken);
            Run(m, GamePhase.Awaken, 1f, 0f, 0.4f); // combat state is stale until Begin

            Assert.AreEqual(1f, m.BossHp);
            Assert.AreEqual(1f, m.PlayerHp);
        }

        [Test]
        public void LastBossHit_EmptiesBarBeforeBossHides()
        {
            // bossMaxHp 5: last parry goes 0.2 -> 0. The boss hides 0.6 s after the victory (G5).
            GameHudModel m = NewModel();
            Run(m, GamePhase.Combat, 1f, 0.2f);
            Run(m, GamePhase.Result, 0.15f, 0f);
            Assert.AreEqual(0f, m.BossHp);
        }

        [Test]
        public void Combo_CountsParries_PlayerHitClears()
        {
            GameHudModel m = NewModel();
            m.OnCombatEvent(CombatEventType.Parried);
            Assert.AreEqual(1, m.Combo);
            Assert.IsFalse(m.ComboVisible);

            m.OnCombatEvent(CombatEventType.Parried);
            Assert.AreEqual(2, m.Combo);
            Assert.IsTrue(m.ComboVisible);

            m.OnCombatEvent(CombatEventType.PlayerHit);
            Assert.AreEqual(0, m.Combo);
            Assert.IsFalse(m.ComboVisible);
        }

        [TestCase(CombatEventType.AimStarted)]
        [TestCase(CombatEventType.AimLocked)]
        [TestCase(CombatEventType.Fired)]
        [TestCase(CombatEventType.Dodged)]
        [TestCase(CombatEventType.ShieldRaised)]
        [TestCase(CombatEventType.ShieldLowered)]
        [TestCase(CombatEventType.BossDefeated)]
        public void Combo_OtherEventsKeepIt(CombatEventType type)
        {
            GameHudModel m = NewModel();
            m.OnCombatEvent(CombatEventType.Parried);
            m.OnCombatEvent(type);
            Assert.AreEqual(1, m.Combo);
        }

        [Test]
        public void Combo_ClearedByCombatStart_KeptThroughResult()
        {
            GameHudModel m = NewModel();
            m.OnCombatEvent(CombatEventType.Parried);
            m.OnCombatEvent(CombatEventType.Parried);

            m.OnPhaseChanged(GamePhase.Result);
            Assert.AreEqual(2, m.Combo);

            m.OnPhaseChanged(GamePhase.Combat);
            Assert.AreEqual(0, m.Combo);
        }
    }

    public class GameHudTextTests
    {
        private sealed class FakePhase : IGamePhaseSource
        {
            public GamePhase Phase { get; set; }
            public GameResult Result => GameResult.None;
            public float PhaseElapsedSec => 0f;
            public float AwakenCharge { get; set; }
#pragma warning disable 67
            public event Action<GamePhase> PhaseChanged;
#pragma warning restore 67
        }

        private sealed class FakeInput : IPlayerInputSource
        {
            public PlayerSample Current { get; set; }
        }

        private sealed class FakeStats : INetworkStats
        {
            public int PacketsReceived { get; set; }
            public int PacketsDropped { get; set; }
            public uint LastSeq => 0;
            public float LastPacketTime => -1f;
        }

        [TestCase(0, "00:00")]
        [TestCase(-3, "00:00")]
        [TestCase(9, "00:09")]
        [TestCase(60, "01:00")]
        [TestCase(125, "02:05")]
        public void Clock(int seconds, string expected)
        {
            var sb = new StringBuilder();
            GameHudText.AppendClock(sb, seconds);
            Assert.AreEqual(expected, sb.ToString());
        }

        [Test]
        public void GameStatus_External_HasAllThreeLines()
        {
            var sb = new StringBuilder();
            GameHudText.AppendGameStatus(sb,
                new FakePhase { Phase = GamePhase.Combat, AwakenCharge = 1f },
                new FakeInput { Current = new PlayerSample { source = PlayerPositionSource.Body, motion = 0.42f } },
                new FakeStats { PacketsReceived = 123, PacketsDropped = 4 },
                9001);

            Assert.AreEqual("단계: 전투  |  각성 게이지 1.00\n입력: 몸  |  강도 0.42\n몸 스트림 (9001)  수신 123  버림 4", sb.ToString());
        }

        [Test]
        public void GameStatus_Webcam_LeavesOutBodyStream()
        {
            var sb = new StringBuilder();
            GameHudText.AppendGameStatus(sb,
                new FakePhase { Phase = GamePhase.MediaWall, AwakenCharge = 0.5f },
                new FakeInput { Current = new PlayerSample { source = PlayerPositionSource.MotionCentroid, motion = 0.1f } },
                null,
                9001);

            Assert.AreEqual("단계: 미디어 월  |  각성 게이지 0.50\n입력: 모션  |  강도 0.10", sb.ToString());
        }

        [Test]
        public void GameStatus_NoSources_DoesNotThrow()
        {
            var sb = new StringBuilder();
            GameHudText.AppendGameStatus(sb, null, null, null, 9001);
            Assert.AreEqual("단계: 연결 없음\n입력: 연결 없음", sb.ToString());
        }

        [TestCase(GamePhase.MediaWall, "미디어 월")]
        [TestCase(GamePhase.Awaken, "각성")]
        [TestCase(GamePhase.Combat, "전투")]
        [TestCase(GamePhase.Result, "결과")]
        public void PhaseNames(GamePhase phase, string expected)
        {
            Assert.AreEqual(expected, GameHudText.PhaseName(new FakePhase { Phase = phase }));
        }

        [TestCase(PlayerPositionSource.Body, "몸")]
        [TestCase(PlayerPositionSource.MotionCentroid, "모션")]
        [TestCase(PlayerPositionSource.None, "없음")]
        public void SourceNames(PlayerPositionSource source, string expected)
        {
            Assert.AreEqual(expected, GameHudText.SourceName(source));
        }
    }

    public class GameUiPrefabTests
    {
        private const string HudPath = "Assets/03_Prefabs/UI/GameHud.prefab";
        private const string PanelPath = "Assets/03_Prefabs/UI/EngineerPanel.prefab";
        private const string FontName = "GmarketSansTTFMedium SDF";

        private static void AssertNoEmptyReferences(GameObject root)
        {
            foreach (MonoBehaviour b in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!b.GetType().Namespace.StartsWith("VisionToWall", StringComparison.Ordinal)) continue;

                SerializedProperty p = new SerializedObject(b).GetIterator();
                while (p.NextVisible(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference || p.name == "m_Script") continue;
                    Assert.IsNotNull(p.objectReferenceValue, $"{b.GetType().Name}.{p.name} is not assigned");
                }
            }
        }

        private static void AssertFont(GameObject root)
        {
            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
                Assert.AreEqual(FontName, t.font.name, t.name);
        }

        [TestCase(HudPath)]
        [TestCase(PanelPath)]
        public void Prefab_ReferencesAssigned_FontCorrect(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            AssertNoEmptyReferences(prefab);
            AssertFont(prefab);
        }

        [Test]
        public void Hud_NeverBlocksTheF1PanelSliders()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            Assert.That(prefab.GetComponent<Canvas>().sortingOrder, Is.LessThan(
                AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath).GetComponent<Canvas>().sortingOrder));
            foreach (UnityEngine.UI.Graphic g in prefab.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                Assert.IsFalse(g.raycastTarget, g.name);
        }
    }

    public class GameHudViewTests
    {
        private sealed class FakeSources : IGamePhaseSource, ICombatStateSource
        {
            public GamePhase Phase { get; set; }
            public GameResult Result { get; set; }
            public float PhaseElapsedSec => 0f;
            public float AwakenCharge => 0f;
            public event Action<GamePhase> PhaseChanged;
            public float BossHp01 => 1f;
            public float PlayerHp01 => 1f;
            public float RemainingSec => 0f;
            public bool ShieldActive => false;
            public bool IsAiming => false;
            public Vector2 AimPosition => default;
            public float AimProgress => 0f;
            public float BossPanDeg => 0f;
            public float BossTiltDeg => 0f;
            public event Action<CombatEvent> CombatEventRaised;

            public int Listeners => (PhaseChanged?.GetInvocationList().Length ?? 0) + (CombatEventRaised?.GetInvocationList().Length ?? 0);

            public void RaisePhase(GamePhase phase) { Phase = phase; PhaseChanged?.Invoke(phase); }
            public void RaiseEvent(CombatEventType type) => CombatEventRaised?.Invoke(new CombatEvent { type = type });
        }

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void Initialize_RejectsNull()
        {
            _go = new GameObject("hud");
            var view = _go.AddComponent<GameHudView>();
            Assert.Throws<ArgumentNullException>(() => view.Initialize(null, new FakeSources()));
            Assert.Throws<ArgumentNullException>(() => view.Initialize(new FakeSources(), null));
        }

        [Test]
        public void Events_ReachModel_NoDoubleSubscribe_OnReinitialize()
        {
            _go = new GameObject("hud");
            var view = _go.AddComponent<GameHudView>();
            var src = new FakeSources();

            view.Initialize(src, src);
            view.Initialize(src, src);
            Assert.AreEqual(2, src.Listeners);

            src.RaiseEvent(CombatEventType.Parried);
            Assert.AreEqual(1, view.Model.Combo);

            src.RaisePhase(GamePhase.Combat);
            Assert.AreEqual(0, view.Model.Combo);
        }
    }
}
