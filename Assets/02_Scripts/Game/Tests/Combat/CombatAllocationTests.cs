using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace VisionToWall.Game.Tests
{
    /// <summary>Whole attack cycles, so every stage change and event path is inside the measured block.</summary>
    public sealed class CombatAllocationTests
    {
        private static void PlayCycles(CombatRules rules, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                var sample = new PlayerSample
                {
                    position = new Vector2(0.5f, (i / 180) % 2 == 0 ? 0.5f : 0.95f),
                    hasPosition = i % 400 != 0,
                    motion = i % 300 < 100 ? 1f : 0f,
                    guardPose = i % 250 < 60,
                };
                rules.Tick(0.02f, sample, 16f / 9f);
            }
        }

        [Test]
        public void Rules_FullCycles_AllocateNothing()
        {
            var config = new AppConfig { bossMaxHp = 100, playerMaxHp = 100, combatDurationSec = 600f };
            var rules = new CombatRules(config);
            int seen = 0;
            rules.EventRaised += e => seen++;
            rules.Begin(new PlayerSample { position = Vector2.one * 0.5f, hasPosition = true });
            PlayCycles(rules, 200); // JIT everything outside the measurement

            Assert.That(() => PlayCycles(rules, 1000), Is.Not.AllocatingGCMemory());
            Assert.Greater(seen, 10);
            Assert.IsTrue(rules.IsRunning);
        }

        [Test]
        public void LookAt_Tick_AllocatesNothing()
        {
            var go = new GameObject("Boss");
            try
            {
                var look = go.AddComponent<BossLookAt>();
                look.SetRest(Quaternion.identity);
                look.SetTarget(Vector3.one);
                look.Tick(0.016f);

                Assert.That(() =>
                {
                    for (int i = 0; i < 100; i++) look.Tick(0.016f);
                }, Is.Not.AllocatingGCMemory());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
