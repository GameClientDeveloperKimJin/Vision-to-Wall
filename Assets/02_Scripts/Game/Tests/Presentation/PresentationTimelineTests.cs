using NUnit.Framework;
using VisionToWall.Core;

namespace VisionToWall.Game.Tests
{
    public sealed class PresentationTimelineTests
    {
        private const float AwakenSec = 2f;
        private const float Frame = 0.02f;

        private PresentationTuning _tuning;
        private PresentationTimeline _timeline;

        [SetUp]
        public void SetUp()
        {
            _tuning = new PresentationTuning();
            _timeline = new PresentationTimeline(_tuning, AwakenSec);
        }

        private PresentationLook Run(float seconds, float charge = 0f)
        {
            PresentationLook look = _timeline.Current;
            int frames = UnityEngine.Mathf.CeilToInt(seconds / Frame);
            for (int i = 0; i < frames; i++) look = _timeline.Tick(Frame, charge);
            return look;
        }

        private void RunThroughCombat()
        {
            _timeline.SetPhase(GamePhase.Awaken, GameResult.None);
            Run(AwakenSec);
            _timeline.SetPhase(GamePhase.Combat, GameResult.None);
            Run(1f);
        }

        [Test]
        public void Starts_Calm()
        {
            PresentationLook look = _timeline.Current;

            Assert.That(look.crimson, Is.EqualTo(0f));
            Assert.That(look.absorb, Is.EqualTo(0f));
            Assert.That(look.irisOpen, Is.EqualTo(0f));
            Assert.That(look.irisGlow, Is.EqualTo(0f));
            Assert.That(look.backdrop, Is.EqualTo(0f));
            Assert.That(look.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void MediaWall_AbsorbFollowsGauge_EyeStaysClosed()
        {
            PresentationLook look = Run(0.1f, 0.6f);

            Assert.That(look.absorb, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(look.irisOpen, Is.EqualTo(0f));
            Assert.That(look.crimson, Is.EqualTo(0f));
        }

        [Test]
        public void Awaken_ReachesCrimsonAtEnd_NotBefore()
        {
            _timeline.SetPhase(GamePhase.Awaken, GameResult.None);

            Assert.That(Run(AwakenSec * 0.5f).crimson, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(Run(AwakenSec * 0.5f).crimson, Is.EqualTo(1f).Within(0.02f));
        }

        [Test]
        public void Awaken_ReleasesAbsorb()
        {
            Run(0.1f, 1f);
            _timeline.SetPhase(GamePhase.Awaken, GameResult.None);

            Assert.That(Run(_tuning.absorbReleaseSec + Frame).absorb, Is.EqualTo(0f));
        }

        [Test]
        public void Awaken_FlashesAboveCombatGlow_ThenSettles()
        {
            _timeline.SetPhase(GamePhase.Awaken, GameResult.None);

            float peak = 0f;
            int frames = UnityEngine.Mathf.CeilToInt(AwakenSec / Frame);
            for (int i = 0; i < frames; i++)
                peak = UnityEngine.Mathf.Max(peak, _timeline.Tick(Frame, 1f).irisGlow);

            Assert.That(peak, Is.GreaterThan(_tuning.awakenFlashPeak * 0.95f));
            Assert.That(_timeline.Current.irisGlow, Is.EqualTo(1f).Within(0.02f));
            Assert.That(_timeline.Current.irisOpen, Is.EqualTo(1f));
        }

        [Test]
        public void Awaken_ShowsArenaOnlyInLaterPart()
        {
            _timeline.SetPhase(GamePhase.Awaken, GameResult.None);

            Assert.That(Run(AwakenSec * _tuning.backdropStartAt * 0.9f).backdrop, Is.EqualTo(0f));
            Assert.That(Run(AwakenSec).backdrop, Is.EqualTo(1f));
        }

        [Test]
        public void Combat_HoldsFullLook_AtNormalSpeed()
        {
            RunThroughCombat();
            PresentationLook look = _timeline.Current;

            Assert.That(look.crimson, Is.EqualTo(1f));
            Assert.That(look.backdrop, Is.EqualTo(1f));
            Assert.That(look.irisOpen, Is.EqualTo(1f));
            Assert.That(look.irisGlow, Is.EqualTo(1f));
            Assert.That(look.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void Victory_SlowsThenEasesBack()
        {
            RunThroughCombat();
            _timeline.SetPhase(GamePhase.Result, GameResult.Victory);

            Assert.That(Run(_tuning.slowMotionHoldSec * 0.5f).timeScale, Is.EqualTo(_tuning.slowMotionScale));
            Assert.That(Run(_tuning.slowMotionHoldSec + _tuning.slowMotionEaseSec).timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void GameOver_ClosesEye_WithoutSlowMotion()
        {
            RunThroughCombat();
            _timeline.SetPhase(GamePhase.Result, GameResult.GameOver);

            PresentationLook early = _timeline.Tick(Frame, 1f);
            Assert.That(early.timeScale, Is.EqualTo(1f));

            PresentationLook look = Run(_tuning.irisCloseSec + Frame);
            Assert.That(look.irisOpen, Is.EqualTo(0f));
            Assert.That(look.irisGlow, Is.EqualTo(0f));
            Assert.That(look.crimson, Is.EqualTo(1f), "The red light goes back only after the result.");
        }

        [TestCase(GameResult.Victory)]
        [TestCase(GameResult.GameOver)]
        public void FullCycle_ReturnsToInitialLook_OverReturnTime(GameResult result)
        {
            RunThroughCombat();
            _timeline.SetPhase(GamePhase.Result, result);
            Run(5f);
            _timeline.SetPhase(GamePhase.MediaWall, GameResult.None);

            Assert.That(Run(_tuning.returnSec * 0.5f).crimson, Is.EqualTo(0.5f).Within(0.02f));

            PresentationLook look = Run(_tuning.returnSec);
            Assert.That(look.crimson, Is.EqualTo(0f));
            Assert.That(look.irisOpen, Is.EqualTo(0f));
            Assert.That(look.irisGlow, Is.EqualTo(0f));
            Assert.That(look.backdrop, Is.EqualTo(0f));
            Assert.That(look.absorb, Is.EqualTo(0f));
            Assert.That(look.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void AbortFromVictory_RestoresNormalSpeedOnFirstFrame()
        {
            RunThroughCombat();
            _timeline.SetPhase(GamePhase.Result, GameResult.Victory);
            Run(0.1f);
            _timeline.SetPhase(GamePhase.MediaWall, GameResult.None);

            Assert.That(_timeline.Tick(Frame, 0f).timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void Reset_IsImmediate()
        {
            RunThroughCombat();
            _timeline.Reset();
            PresentationLook look = _timeline.Current;

            Assert.That(look.crimson, Is.EqualTo(0f));
            Assert.That(look.backdrop, Is.EqualTo(0f));
            Assert.That(look.timeScale, Is.EqualTo(1f));
        }
    }
}
