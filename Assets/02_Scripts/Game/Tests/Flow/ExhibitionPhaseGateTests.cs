using System;
using System.Collections.Generic;
using NUnit.Framework;
using VisionToWall.Core;

namespace VisionToWall.Game.Tests
{
    public sealed class ExhibitionPhaseGateTests
    {
        private sealed class FakeState : IExhibitionStateSource
        {
            public ExhibitionState Current { get; private set; } = ExhibitionState.Idle;
            public event Action<ExhibitionState> StateChanged;

            public void Set(ExhibitionState state)
            {
                Current = state;
                StateChanged?.Invoke(state);
            }

            public bool HasSubscribers => StateChanged != null;
        }

        private sealed class FakePhases : IGamePhaseSource
        {
            public GamePhase Phase { get; private set; } = GamePhase.MediaWall;
            public GameResult Result => GameResult.None;
            public float PhaseElapsedSec => 0f;
            public float AwakenCharge => 0f;
            public event Action<GamePhase> PhaseChanged;

            public void Set(GamePhase phase)
            {
                Phase = phase;
                PhaseChanged?.Invoke(phase);
            }

            public bool HasSubscribers => PhaseChanged != null;
        }

        private FakeState _inner;
        private FakePhases _phases;
        private ExhibitionPhaseGate _gate;
        private List<ExhibitionState> _events;

        [SetUp]
        public void SetUp()
        {
            _inner = new FakeState();
            _phases = new FakePhases();
            _gate = new ExhibitionPhaseGate(_inner, _phases);
            _events = new List<ExhibitionState>();
            _gate.StateChanged += _events.Add;
        }

        [Test]
        public void MediaWall_PassesInnerThrough()
        {
            Assert.AreEqual(ExhibitionState.Idle, _gate.Current);

            _inner.Set(ExhibitionState.Active);
            Assert.AreEqual(ExhibitionState.Active, _gate.Current);

            _inner.Set(ExhibitionState.Idle);
            Assert.AreEqual(ExhibitionState.Idle, _gate.Current);

            CollectionAssert.AreEqual(new[] { ExhibitionState.Active, ExhibitionState.Idle }, _events);
        }

        [TestCase(GamePhase.Awaken)]
        [TestCase(GamePhase.Combat)]
        [TestCase(GamePhase.Result)]
        public void OtherPhases_AreActiveEvenWhenInnerIsIdle(GamePhase phase)
        {
            _phases.Set(phase);
            Assert.AreEqual(ExhibitionState.Active, _gate.Current);
            CollectionAssert.AreEqual(new[] { ExhibitionState.Active }, _events);
        }

        [Test]
        public void InnerGoesIdleDuringCombat_NoEvent()
        {
            _inner.Set(ExhibitionState.Active);
            _phases.Set(GamePhase.Combat);
            _events.Clear();

            _inner.Set(ExhibitionState.Idle);

            Assert.AreEqual(ExhibitionState.Active, _gate.Current);
            Assert.IsEmpty(_events);
        }

        [Test]
        public void PhaseChangeWithSameValue_NoEvent()
        {
            _inner.Set(ExhibitionState.Active);
            _events.Clear();

            _phases.Set(GamePhase.Awaken);
            _phases.Set(GamePhase.Combat);
            _phases.Set(GamePhase.Result);
            _phases.Set(GamePhase.MediaWall);

            Assert.IsEmpty(_events);
        }

        [Test]
        public void ReturnToMediaWall_RestoresInnerIdle()
        {
            _phases.Set(GamePhase.Result);
            _events.Clear();

            _phases.Set(GamePhase.MediaWall);

            Assert.AreEqual(ExhibitionState.Idle, _gate.Current);
            CollectionAssert.AreEqual(new[] { ExhibitionState.Idle }, _events);
        }

        [Test]
        public void CreatedDuringCombat_StartsActive()
        {
            var phases = new FakePhases();
            phases.Set(GamePhase.Combat);
            Assert.AreEqual(ExhibitionState.Active, new ExhibitionPhaseGate(new FakeState(), phases).Current);
        }

        [Test]
        public void Dispose_Unsubscribes()
        {
            _gate.Dispose();
            Assert.IsFalse(_inner.HasSubscribers);
            Assert.IsFalse(_phases.HasSubscribers);
        }
    }
}
