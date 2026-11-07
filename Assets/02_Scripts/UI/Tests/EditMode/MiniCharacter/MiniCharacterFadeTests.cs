using NUnit.Framework;
using VisionToWall.Core;

namespace VisionToWall.UI.Tests
{
    public sealed class MiniCharacterFadeTests
    {
        private const float Dt = 1f / 60f;
        private const float AwakenSec = 2f;
        private const float FadeInSec = 0.6f;

        private static float Run(GamePhase phase, float alpha, float seconds)
        {
            int steps = (int)(seconds / Dt + 0.5f);
            for (int i = 0; i < steps; i++) alpha = MiniCharacterFade.Step(phase, alpha, Dt, AwakenSec, FadeInSec);
            return alpha;
        }

        [Test]
        public void InitialAlpha_OnlyMediaWall()
        {
            Assert.AreEqual(1f, MiniCharacterFade.InitialAlpha(GamePhase.MediaWall));
            Assert.AreEqual(0f, MiniCharacterFade.InitialAlpha(GamePhase.Awaken));
            Assert.AreEqual(0f, MiniCharacterFade.InitialAlpha(GamePhase.Combat));
            Assert.AreEqual(0f, MiniCharacterFade.InitialAlpha(GamePhase.Result));
        }

        [Test]
        public void Awaken_FadesOverTransitionTime()
        {
            float a = Run(GamePhase.Awaken, 1f, AwakenSec * 0.5f);
            Assert.AreEqual(0.5f, a, 0.02f);
            a = Run(GamePhase.Awaken, a, AwakenSec * 0.5f);
            Assert.AreEqual(0f, a, 1e-3f);
        }

        [Test]
        public void CombatAndResult_Hidden()
        {
            Assert.AreEqual(0f, MiniCharacterFade.Step(GamePhase.Combat, 0.7f, Dt, AwakenSec, FadeInSec));
            Assert.AreEqual(0f, MiniCharacterFade.Step(GamePhase.Result, 1f, Dt, AwakenSec, FadeInSec));
        }

        [Test]
        public void ReturnToMediaWall_FadesIn()
        {
            float a = Run(GamePhase.MediaWall, 0f, FadeInSec * 0.5f);
            Assert.AreEqual(0.5f, a, 0.03f);
            a = Run(GamePhase.MediaWall, a, FadeInSec);
            Assert.AreEqual(1f, a);
        }

        [Test]
        public void GlowSmoothStep_IsZeroOutsideEdges()
        {
            Assert.AreEqual(0f, NeonFigureGraphic.SmoothStep01(0.45f, 0.75f, 0f));
            Assert.AreEqual(0.5f, NeonFigureGraphic.SmoothStep01(0.45f, 0.75f, 0.6f), 1e-4f);
            Assert.AreEqual(1f, NeonFigureGraphic.SmoothStep01(0.45f, 0.75f, 1f));
        }

        [Test]
        public void FullCycle()
        {
            float a = MiniCharacterFade.InitialAlpha(GamePhase.MediaWall);
            a = Run(GamePhase.MediaWall, a, 1f);
            Assert.AreEqual(1f, a);
            a = Run(GamePhase.Awaken, a, 1f);
            Assert.That(a, Is.InRange(0.01f, 0.99f));
            a = Run(GamePhase.Awaken, a, 1.1f);
            Assert.AreEqual(0f, a);
            a = Run(GamePhase.Combat, a, 5f);
            Assert.AreEqual(0f, a);
            a = Run(GamePhase.Result, a, 5f);
            Assert.AreEqual(0f, a);
            a = Run(GamePhase.MediaWall, a, 1f);
            Assert.AreEqual(1f, a);
        }
    }
}
