using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game.Tests
{
    public sealed class GamePhaseMachineTests
    {
        private const float Dt = 0.01f;

        private sealed class FakeInput : IPlayerInputSource
        {
            public float Motion;
            public PlayerSample Current => new PlayerSample { motion = Motion };
        }

        private sealed class FakeCombat : ICombatSession
        {
            public int BeginCount;
            public int AbortCount;
            public bool FinishInsideAbort;
            public GamePhaseMachine PhaseProbe;
            public GamePhase PhaseAtBegin;

            public event Action<GameResult> Finished;

            public void Begin()
            {
                BeginCount++;
                if (PhaseProbe != null) PhaseAtBegin = PhaseProbe.Phase;
            }

            public void Abort()
            {
                AbortCount++;
                if (FinishInsideAbort) Finish(GameResult.Victory);
            }

            public void Finish(GameResult result) => Finished?.Invoke(result);

            public int SubscriberCount => Finished == null ? 0 : Finished.GetInvocationList().Length;
        }

        private GameObject _go;
        private GamePhaseMachine _machine;
        private FakeInput _input;
        private FakeCombat _combat;
        private List<GamePhase> _changes;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("GamePhaseMachineTest");
            _machine = _go.AddComponent<GamePhaseMachine>();
            _input = new FakeInput();
            _combat = new FakeCombat();
            _changes = new List<GamePhase>();
            _machine.PhaseChanged += _changes.Add;
            _machine.Initialize(Config(), _input, _combat);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_go);

        private static AppConfig Config() => new AppConfig
        {
            awakenChargeSec = 3f,
            awakenMinMotion = 0.15f,
            awakenGraceSec = 0.5f,
            awakenTransitionSec = 2f,
            resultDurationSec = 5f,
            rearmCooldownSec = 5f
        };

        private void Run(float seconds)
        {
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++) _machine.Tick(Dt);
        }

        private void GoToAwaken()
        {
            _input.Motion = 1f;
            Run(3.1f);
            Assert.AreEqual(GamePhase.Awaken, _machine.Phase);
        }

        private void GoToCombat()
        {
            GoToAwaken();
            Run(2.1f);
            Assert.AreEqual(GamePhase.Combat, _machine.Phase);
        }

        private void GoToResult(GameResult result = GameResult.Victory)
        {
            GoToCombat();
            _combat.Finish(result);
            Assert.AreEqual(GamePhase.Result, _machine.Phase);
        }

        // ---------- cycle ----------

        [Test]
        public void StartsInMediaWall_WithoutNotification()
        {
            Assert.AreEqual(GamePhase.MediaWall, _machine.Phase);
            Assert.AreEqual(GameResult.None, _machine.Result);
            Assert.AreEqual(0f, _machine.AwakenCharge);
            Assert.IsEmpty(_changes);
        }

        [Test]
        public void MediaWall_EntersAwakenWhenGaugeIsFull()
        {
            _input.Motion = 1f;

            Run(2.9f);
            Assert.AreEqual(GamePhase.MediaWall, _machine.Phase);

            Run(0.2f);
            Assert.AreEqual(GamePhase.Awaken, _machine.Phase);
            Assert.AreEqual(1f, _machine.AwakenCharge);
        }

        [Test]
        public void LongFrame_CountsOnlyUpToTheCap()
        {
            _input.Motion = 1f;

            _machine.Tick(10f);

            Assert.AreEqual(GamePhase.MediaWall, _machine.Phase);
            Assert.AreEqual(GamePhaseMachine.MaxFrameDeltaSec, _machine.PhaseElapsedSec, 1e-5f);
            Assert.AreEqual(GamePhaseMachine.MaxFrameDeltaSec / 3f, _machine.AwakenCharge, 1e-5f);
        }

        [Test]
        public void MediaWall_StoppingDrainsGauge()
        {
            _input.Motion = 1f;
            Run(2f);
            float charged = _machine.AwakenCharge;

            _input.Motion = 0f;
            Run(1f);

            Assert.Less(_machine.AwakenCharge, charged);
            Assert.AreEqual(GamePhase.MediaWall, _machine.Phase);
        }

        [Test]
        public void Awaken_EntersCombatAfterTransitionAndBeginsOnce()
        {
            GoToAwaken();

            Run(1.9f);
            Assert.AreEqual(GamePhase.Awaken, _machine.Phase);
            Assert.AreEqual(0, _combat.BeginCount);

            Run(0.2f);
            Assert.AreEqual(GamePhase.Combat, _machine.Phase);
            Assert.AreEqual(1, _combat.BeginCount);
        }

        [Test]
        public void Combat_NeverEndsWithoutFinished()
        {
            GoToCombat();
            Run(120f);
            Assert.AreEqual(GamePhase.Combat, _machine.Phase);
        }

        [TestCase(GameResult.Victory)]
        [TestCase(GameResult.GameOver)]
        public void Finished_EntersResultWithOutcome(GameResult result)
        {
            GoToResult(result);
            Assert.AreEqual(result, _machine.Result);
        }

        [Test]
        public void Result_ReturnsToMediaWallEmptyWithCooldown()
        {
            GoToResult();

            Run(4.9f);
            Assert.AreEqual(GamePhase.Result, _machine.Phase);

            Run(0.2f);
            Assert.AreEqual(GamePhase.MediaWall, _machine.Phase);
            Assert.AreEqual(GameResult.None, _machine.Result);
            Assert.AreEqual(0f, _machine.AwakenCharge);
            Assert.Greater(_machine.RearmCooldownLeftSec, 4.8f);
        }

        [Test]
        public void AfterReturn_ChargesOnlyAfterCooldown()
        {
            GoToResult();
            Run(5.1f);

            Run(4.9f);
            Assert.AreEqual(0f, _machine.AwakenCharge);

            Run(0.2f + 3.1f);
            Assert.AreEqual(GamePhase.Awaken, _machine.Phase);
        }

        [Test]
        public void FullCycle_NotifiesEachPhaseInOrder()
        {
            GoToResult();
            Run(5.1f);

            CollectionAssert.AreEqual(
                new[] { GamePhase.Awaken, GamePhase.Combat, GamePhase.Result, GamePhase.MediaWall }, _changes);
        }

        [Test]
        public void PhaseElapsed_RestartsOnEveryTransition()
        {
            GoToAwaken();
            Assert.Less(_machine.PhaseElapsedSec, 0.2f);

            Run(1f);
            Assert.AreEqual(1f, _machine.PhaseElapsedSec, 0.2f);

            Run(1.1f);
            Assert.Less(_machine.PhaseElapsedSec, 0.2f);
        }

        // ---------- ordering ----------

        [Test]
        public void Begin_RunsBeforeCombatNotification()
        {
            int beginCountAtNotify = -1;
            _machine.PhaseChanged += phase =>
            {
                if (phase == GamePhase.Combat) beginCountAtNotify = _combat.BeginCount;
            };

            GoToCombat();

            Assert.AreEqual(1, beginCountAtNotify);
        }

        [Test]
        public void Begin_SeesCombatPhase()
        {
            _combat.PhaseProbe = _machine;
            GoToCombat();
            Assert.AreEqual(GamePhase.Combat, _combat.PhaseAtBegin);
        }

        // ---------- invalid Finished ----------

        [Test]
        public void Finished_IgnoredInMediaWall()
        {
            _combat.Finish(GameResult.Victory);

            Assert.AreEqual(GamePhase.MediaWall, _machine.Phase);
            Assert.AreEqual(GameResult.None, _machine.Result);
            Assert.IsEmpty(_changes);
        }

        [Test]
        public void Finished_IgnoredInAwaken()
        {
            GoToAwaken();
            _combat.Finish(GameResult.Victory);
            Assert.AreEqual(GamePhase.Awaken, _machine.Phase);
        }

        [Test]
        public void Finished_SecondCallInResultIsIgnored()
        {
            GoToResult(GameResult.Victory);
            Run(1f);
            float elapsed = _machine.PhaseElapsedSec;

            _combat.Finish(GameResult.GameOver);

            Assert.AreEqual(GameResult.Victory, _machine.Result);
            Assert.AreEqual(elapsed, _machine.PhaseElapsedSec);
        }

        // ---------- shutdown ----------

        [Test]
        public void Shutdown_InCombat_AbortsAndReturnsToMediaWall()
        {
            GoToCombat();

            _machine.Shutdown();

            Assert.AreEqual(1, _combat.AbortCount);
            Assert.AreEqual(GamePhase.MediaWall, _machine.Phase);
            Assert.AreEqual(GamePhase.MediaWall, _changes[_changes.Count - 1]);
            Assert.AreEqual(0f, _machine.AwakenCharge);
        }

        [Test]
        public void Shutdown_FinishedRaisedFromAbortIsIgnored()
        {
            GoToCombat();
            _combat.FinishInsideAbort = true;

            _machine.Shutdown();

            Assert.AreEqual(GamePhase.MediaWall, _machine.Phase);
            Assert.AreEqual(GameResult.None, _machine.Result);
            Assert.IsFalse(_changes.Contains(GamePhase.Result));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void Shutdown_OutsideCombat_DoesNotAbort(int phaseIndex)
        {
            if (phaseIndex == 1) GoToAwaken();

            _machine.Shutdown();

            Assert.AreEqual(0, _combat.AbortCount);
            Assert.AreEqual(GamePhase.MediaWall, _machine.Phase);
        }

        [Test]
        public void Shutdown_InMediaWall_DoesNotNotify()
        {
            _machine.Shutdown();
            Assert.IsEmpty(_changes);
        }

        [Test]
        public void Initialize_Again_AbortsOldSessionAndSwitchesSubscription()
        {
            GoToCombat();
            var replacement = new FakeCombat();

            _machine.Initialize(Config(), _input, replacement);

            Assert.AreEqual(1, _combat.AbortCount);
            Assert.AreEqual(0, _combat.SubscriberCount);
            Assert.AreEqual(1, replacement.SubscriberCount);
            Assert.AreEqual(GamePhase.MediaWall, _machine.Phase);
        }

        [Test]
        public void Tick_BeforeInitialize_IsHarmless()
        {
            var go = new GameObject("Uninitialized");
            try
            {
                var machine = go.AddComponent<GamePhaseMachine>();
                machine.Tick(Dt);
                machine.Shutdown();
                Assert.AreEqual(GamePhase.MediaWall, machine.Phase);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
