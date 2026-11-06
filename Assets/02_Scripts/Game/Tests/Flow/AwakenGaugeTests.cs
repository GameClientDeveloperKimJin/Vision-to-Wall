using NUnit.Framework;
using VisionToWall.Core;

namespace VisionToWall.Game.Tests
{
    public sealed class AwakenGaugeTests
    {
        private const float Tolerance = 1e-3f;
        private const float Dt = 0.01f;
        private const float Moving = 1f;
        private const float Still = 0f;

        private static AppConfig Config() => new AppConfig
        {
            awakenChargeSec = 3f,
            awakenMinMotion = 0.15f,
            awakenGraceSec = 0.5f,
            rearmCooldownSec = 5f
        };

        private static void Run(AwakenGauge gauge, float motion, float seconds)
        {
            int steps = (int)System.Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++) gauge.Tick(motion, Dt);
        }

        [Test]
        public void ContinuousMotion_FillsInChargeSec()
        {
            var gauge = new AwakenGauge(Config());

            Run(gauge, Moving, 2.9f);
            Assert.IsFalse(gauge.IsFull);
            Assert.AreEqual(2.9f / 3f, gauge.Charge, Tolerance);

            Run(gauge, Moving, 0.2f);
            Assert.IsTrue(gauge.IsFull);
            Assert.AreEqual(1f, gauge.Charge);
        }

        [Test]
        public void MotionBelowMinimum_DoesNotCharge()
        {
            var gauge = new AwakenGauge(Config());
            Run(gauge, 0.14f, 1f);
            Assert.AreEqual(0f, gauge.Charge);
        }

        [Test]
        public void MotionAtMinimum_Charges()
        {
            var gauge = new AwakenGauge(Config());
            Run(gauge, 0.15f, 1f);
            Assert.AreEqual(1f / 3f, gauge.Charge, Tolerance);
        }

        [Test]
        public void GapShorterThanGrace_KeepsCharge()
        {
            var gauge = new AwakenGauge(Config());
            Run(gauge, Moving, 1f);
            float charged = gauge.Charge;

            Run(gauge, Still, 0.45f);

            Assert.AreEqual(charged, gauge.Charge);
        }

        [Test]
        public void MotionAfterGap_RestoresFullGrace()
        {
            var gauge = new AwakenGauge(Config());
            Run(gauge, Moving, 1f);
            Run(gauge, Still, 0.45f);
            Run(gauge, Moving, 0.01f);
            float charged = gauge.Charge;

            Run(gauge, Still, 0.45f);

            Assert.AreEqual(charged, gauge.Charge);
        }

        [Test]
        public void AfterGrace_DecaysAtChargeRate()
        {
            var gauge = new AwakenGauge(Config());
            Run(gauge, Moving, 1.5f);

            Run(gauge, Still, 0.5f + 0.75f);

            Assert.AreEqual(0.25f, gauge.Charge, Tolerance);
        }

        [Test]
        public void LongFrame_DecaysOnlyPastGrace()
        {
            var gauge = new AwakenGauge(Config());
            Run(gauge, Moving, 1.5f);

            gauge.Tick(Still, 1.25f);

            Assert.AreEqual(0.25f, gauge.Charge, Tolerance);
        }

        [Test]
        public void Decay_StopsAtZero()
        {
            var gauge = new AwakenGauge(Config());
            Run(gauge, Moving, 0.5f);
            Run(gauge, Still, 5f);
            Assert.AreEqual(0f, gauge.Charge);
        }

        [Test]
        public void Cooldown_BlocksChargingUntilItEnds()
        {
            var gauge = new AwakenGauge(Config());
            gauge.StartCooldown();

            Run(gauge, Moving, 4.9f);
            Assert.AreEqual(0f, gauge.Charge);
            Assert.Greater(gauge.CooldownLeftSec, 0f);

            Run(gauge, Moving, 0.2f);
            Assert.AreEqual(0f, gauge.CooldownLeftSec);

            Run(gauge, Moving, 1f);
            Assert.Greater(gauge.Charge, 0.3f);
        }

        [Test]
        public void Reset_ClearsChargeGraceAndCooldown()
        {
            var gauge = new AwakenGauge(Config());
            Run(gauge, Moving, 1f);
            gauge.StartCooldown();

            gauge.Reset();

            Assert.AreEqual(0f, gauge.Charge);
            Assert.AreEqual(0f, gauge.CooldownLeftSec);
        }

        [Test]
        public void ConfigChange_AppliesLive()
        {
            AppConfig config = Config();
            var gauge = new AwakenGauge(config);
            config.awakenChargeSec = 1f;

            Run(gauge, Moving, 0.5f);

            Assert.AreEqual(0.5f, gauge.Charge, Tolerance);
        }
    }
}
