using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game.Tests
{
    public sealed class MockCombatSessionTests
    {
        private GameObject _go;
        private MockCombatSession _mock;
        private List<GameResult> _results;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("MockCombatSessionTest");
            _mock = _go.AddComponent<MockCombatSession>();
            _results = new List<GameResult>();
            _mock.Finished += _results.Add;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void Default_FinishesWithVictoryOnceAfterTenSeconds()
        {
            _mock.Begin();

            _mock.Tick(9.9f);
            Assert.IsEmpty(_results);

            _mock.Tick(0.2f);
            _mock.Tick(20f);
            CollectionAssert.AreEqual(new[] { GameResult.Victory }, _results);
        }

        [Test]
        public void Configure_CanFinishWithGameOver()
        {
            _mock.Configure(1f, GameResult.GameOver);
            _mock.Begin();
            _mock.Tick(1f);
            CollectionAssert.AreEqual(new[] { GameResult.GameOver }, _results);
        }

        [Test]
        public void WithoutBegin_NeverFinishes()
        {
            _mock.Tick(60f);
            Assert.IsEmpty(_results);
        }

        [Test]
        public void Abort_PreventsFinish()
        {
            _mock.Begin();
            _mock.Tick(5f);
            _mock.Abort();
            _mock.Tick(60f);
            Assert.IsEmpty(_results);
        }

        [Test]
        public void Begin_RestartsTimer()
        {
            _mock.Begin();
            _mock.Tick(9f);
            _mock.Begin();
            _mock.Tick(9f);
            Assert.IsEmpty(_results);
        }
    }
}
