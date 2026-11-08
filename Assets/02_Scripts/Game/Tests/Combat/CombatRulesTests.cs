using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game.Tests
{
    public sealed class CombatRulesTests
    {
        private const float Dt = 0.01f;
        private const float Tolerance = 0.015f; // one step plus float rounding

        private AppConfig _config;
        private CombatRules _rules;
        private List<CombatEvent> _events;
        private List<float> _eventTimes;
        private float _now;

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        [SetUp]
        public void SetUp()
        {
            _config = new AppConfig
            {
                aimSec = 1.5f,
                aimLockSec = 0.3f,
                projectileFlightSec = 0.5f,
                attackRecoverSec = 1.5f,
                hitRadius = 0.25f,
                shieldThreshold = 0.8f,
                shieldDurationSec = 1.5f,
                shieldCooldownSec = 1.5f,
                bossMaxHp = 5,
                playerMaxHp = 5,
                combatDurationSec = 60f,
                combatAbandonSec = 10f,
            };
            CreateRules();
        }

        private void CreateRules()
        {
            _rules = new CombatRules(_config);
            _events = new List<CombatEvent>();
            _eventTimes = new List<float>();
            _now = 0f;
            _rules.EventRaised += e =>
            {
                _events.Add(e);
                _eventTimes.Add(_now);
            };
        }

        private static PlayerSample At(Vector2 position, float motion = 0f) =>
            new PlayerSample { position = position, hasPosition = true, motion = motion };

        private static readonly PlayerSample NoInput = new PlayerSample { hasPosition = false };

        /// <summary>Arm raised at the given position.</summary>
        private static PlayerSample Guard(Vector2 position) =>
            new PlayerSample { position = position, hasPosition = true, guardPose = true };

        private void Run(float seconds, PlayerSample sample, float aspect = 1f)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                _now += Dt;
                _rules.Tick(Dt, sample, aspect);
            }
        }

        private int Count(CombatEventType type)
        {
            int count = 0;
            foreach (CombatEvent e in _events)
            {
                if (e.type == type) count++;
            }
            return count;
        }

        private float TimeOf(CombatEventType type, int occurrence = 0)
        {
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i].type == type && occurrence-- == 0) return _eventTimes[i];
            }
            Assert.Fail($"{type} was not raised");
            return 0f;
        }

        /// <summary>Plays one attack with the player at the aim point until locked, then at the given offset.</summary>
        private void PlayToArrival(Vector2 aimAt, Vector2 standAt, float aspect = 1f, float motion = 0f)
        {
            Run(1.25f, At(aimAt), aspect); // 0.05 s past the lock
            Run(0.75f + Dt, At(standAt, motion), aspect);
        }

        // Attack timing

        [Test]
        public void Begin_RaisesAimStartedAtOnce()
        {
            _rules.Begin(At(Center));

            Assert.AreEqual(CombatEventType.AimStarted, _events[0].type);
            Assert.AreEqual(AttackStage.Tracking, _rules.Stage);
            Assert.IsTrue(_rules.IsAiming);
        }

        [Test]
        public void AttackCycle_FollowsConfiguredTimes()
        {
            _config.hitRadius = 0.01f;
            _rules.Begin(At(Center));
            Run(7.1f, At(Center));

            Assert.AreEqual(1.2f, TimeOf(CombatEventType.AimLocked), Tolerance);
            Assert.AreEqual(1.5f, TimeOf(CombatEventType.Fired), Tolerance);
            Assert.AreEqual(2.0f, TimeOf(CombatEventType.PlayerHit), Tolerance);
            Assert.AreEqual(3.5f, TimeOf(CombatEventType.AimStarted, 1), Tolerance);
            Assert.AreEqual(5.5f, TimeOf(CombatEventType.PlayerHit, 1), Tolerance);
            Assert.AreEqual(7.0f, TimeOf(CombatEventType.AimStarted, 2), Tolerance);
        }

        [Test]
        public void AttackCycle_DoesNotDriftWithFrameLength()
        {
            _config.playerMaxHp = 100;
            _rules.Begin(At(Center));
            for (int i = 0; i < 500; i++)
            {
                _now += 0.1f;
                _rules.Tick(0.1f, At(new Vector2(0.5f, 0.5f + (i % 2) * 0.9f)), 1f); // alternating dodge
                if (!_rules.IsRunning) break;
            }

            // Thirteenth start would be at 42 s; a stage that dropped its remainder would be up to 0.1 s late each.
            Assert.AreEqual(42f, TimeOf(CombatEventType.AimStarted, 12), 0.11f);
        }

        [Test]
        public void Tick_LimitsFrameDelta()
        {
            _rules.Begin(At(Center));

            _rules.Tick(3.8f, At(Center), 1f);

            Assert.AreEqual(59.9f, _rules.RemainingSec, 0.0001f);
            Assert.AreEqual(AttackStage.Tracking, _rules.Stage);
        }

        [Test]
        public void AimProgress_RunsAcrossTrackingAndLock()
        {
            _rules.Begin(At(Center));
            Assert.AreEqual(0f, _rules.AimProgress, 0.001f);

            Run(0.75f, At(Center));
            Assert.AreEqual(0.5f, _rules.AimProgress, 0.02f);

            Run(0.6f, At(Center));
            Assert.AreEqual(AttackStage.Locked, _rules.Stage);
            Assert.AreEqual(0.9f, _rules.AimProgress, 0.02f);

            Run(0.2f, At(Center));
            Assert.AreEqual(AttackStage.Flight, _rules.Stage);
            Assert.AreEqual(0f, _rules.AimProgress);
            Assert.IsFalse(_rules.IsAiming);
        }

        [Test]
        public void Aim_FollowsPlayerThenLocks()
        {
            var first = new Vector2(0.2f, 0.3f);
            var second = new Vector2(0.8f, 0.7f);
            _rules.Begin(At(first));
            Run(1.0f, At(second));
            Assert.AreEqual(second, _rules.AimPosition);

            Run(0.25f, At(second)); // locked at 1.2
            Run(0.2f, At(first));

            Assert.AreEqual(AttackStage.Locked, _rules.Stage);
            Assert.AreEqual(second, _rules.AimPosition);
        }

        [Test]
        public void AimStarted_ReportsCurrentPlayerNotPreviousShot()
        {
            _config.hitRadius = 0.01f;
            var far = new Vector2(0.9f, 0.9f);
            _rules.Begin(At(Center));
            Run(2.5f, At(Center));

            Run(1.1f, At(far));

            int last = _events.FindLastIndex(e => e.type == CombatEventType.AimStarted);
            Assert.AreEqual(far, _events[last].position);
        }

        // Arrival check

        [Test]
        public void Dodge_FartherThanRadius_NoDamage()
        {
            _rules.Begin(At(Center));
            PlayToArrival(Center, new Vector2(0.5f, 0.76f));

            Assert.AreEqual(1, Count(CombatEventType.Dodged));
            Assert.AreEqual(0, Count(CombatEventType.PlayerHit));
            Assert.AreEqual(1f, _rules.PlayerHp01);
        }

        [Test]
        public void Dodge_ExactlyAtRadius_CountsAsHit()
        {
            _rules.Begin(At(Center));
            PlayToArrival(Center, new Vector2(0.5f, 0.75f)); // 0.25 exactly in binary

            Assert.AreEqual(0, Count(CombatEventType.Dodged));
            Assert.AreEqual(1, Count(CombatEventType.PlayerHit));
        }

        [Test]
        public void Distance_AppliesAspectToXOnly()
        {
            var a = new Vector2(0.25f, 0.5f);
            Assert.AreEqual(0.5f, CombatRules.Distance(a, new Vector2(0.5f, 0.5f), 2f), 1e-5f);
            Assert.AreEqual(0.25f, CombatRules.Distance(a, new Vector2(0.25f, 0.75f), 2f), 1e-5f);
        }

        [Test]
        public void Aspect_SameXOffsetDodgesOnlyOnWideScreen()
        {
            var side = new Vector2(0.7f, 0.5f); // 0.2 of the width

            _rules.Begin(At(Center));
            PlayToArrival(Center, side, aspect: 1f);
            Assert.AreEqual(1, Count(CombatEventType.PlayerHit));

            CreateRules();
            _rules.Begin(At(Center));
            PlayToArrival(Center, side, aspect: 16f / 9f);
            Assert.AreEqual(1, Count(CombatEventType.Dodged));
        }

        [Test]
        public void Hit_WithoutShield_ReducesPlayerHp()
        {
            _rules.Begin(At(Center));
            PlayToArrival(Center, Center);

            Assert.AreEqual(1, Count(CombatEventType.PlayerHit));
            Assert.AreEqual(0.8f, _rules.PlayerHp01, 1e-5f);
            Assert.AreEqual(1f, _rules.BossHp01);
        }

        [Test]
        public void Parry_WithShield_ReducesBossHp()
        {
            _rules.Begin(At(Center));
            PlayToArrival(Center, Center, motion: 1f);

            Assert.AreEqual(1, Count(CombatEventType.Parried));
            Assert.AreEqual(0.8f, _rules.BossHp01, 1e-5f);
            Assert.AreEqual(1f, _rules.PlayerHp01);
        }

        [Test]
        public void Dodge_WithShield_DoesNotParry()
        {
            _rules.Begin(At(Center));
            PlayToArrival(Center, new Vector2(0.5f, 0.9f), motion: 1f);

            Assert.AreEqual(1, Count(CombatEventType.Dodged));
            Assert.AreEqual(0, Count(CombatEventType.Parried));
            Assert.AreEqual(1f, _rules.BossHp01);
        }

        [Test]
        public void ShieldRaisedOnArrivalFrame_Parries()
        {
            _rules.Begin(At(Center));
            Run(1.99f, At(Center));
            Assert.IsFalse(_rules.ShieldActive);

            Run(0.03f, At(Center, 1f));

            Assert.AreEqual(1, Count(CombatEventType.Parried));
        }

        [Test]
        public void NoPosition_UsesLastKnown()
        {
            var known = new Vector2(0.3f, 0.3f);
            _rules.Begin(At(known));
            Run(0.5f, At(known));

            Run(1.6f, NoInput);

            Assert.AreEqual(known, _rules.AimPosition);
            Assert.AreEqual(known, _rules.PlayerPosition);
            Assert.AreEqual(1, Count(CombatEventType.PlayerHit));
        }

        [Test]
        public void NoPositionEver_AimsAtCenter()
        {
            _rules.Begin(NoInput);
            Run(0.5f, NoInput);

            Assert.AreEqual(Center, _rules.AimPosition);
        }

        // Shield

        [Test]
        public void Shield_BelowThreshold_StaysDown()
        {
            _rules.Begin(At(Center));
            Run(0.5f, At(Center, 0.79f));

            Assert.IsFalse(_rules.ShieldActive);
            Assert.AreEqual(0, Count(CombatEventType.ShieldRaised));
        }

        [Test]
        public void Shield_AtThreshold_Raises()
        {
            _rules.Begin(At(Center));
            Run(Dt, At(Center, 0.8f));

            Assert.IsTrue(_rules.ShieldActive);
            Assert.AreEqual(1, Count(CombatEventType.ShieldRaised));
        }

        [Test]
        public void Shield_LowersAfterDurationEvenWhileWaving()
        {
            _rules.Begin(At(Center));
            Run(1.45f, At(Center, 1f));
            Assert.IsTrue(_rules.ShieldActive);

            Run(0.1f, At(Center, 1f));

            Assert.IsFalse(_rules.ShieldActive);
            Assert.AreEqual(1, Count(CombatEventType.ShieldLowered));
            Assert.AreEqual(1.5f, TimeOf(CombatEventType.ShieldLowered) - TimeOf(CombatEventType.ShieldRaised), Tolerance);
        }

        [Test]
        public void Shield_CooldownBlocksUntilItEnds()
        {
            _rules.Begin(At(Center));
            Run(1.55f, At(Center, 1f));
            Assert.IsFalse(_rules.ShieldActive);

            Run(1.4f, At(Center, 1f)); // cooldown has 1.45 elapsed at the end of this
            Assert.AreEqual(1, Count(CombatEventType.ShieldRaised));

            Run(0.2f, At(Center, 1f));
            Assert.AreEqual(2, Count(CombatEventType.ShieldRaised));
            Assert.AreEqual(1.5f, TimeOf(CombatEventType.ShieldRaised, 1) - TimeOf(CombatEventType.ShieldLowered), Tolerance);
        }

        // Shield: guard pose (arm raised)

        [Test]
        public void Guard_Start_RaisesShield()
        {
            _rules.Begin(At(Center));
            Run(0.1f, At(Center));
            Run(Dt, Guard(Center));

            Assert.IsTrue(_rules.ShieldActive);
            Assert.AreEqual(1, Count(CombatEventType.ShieldRaised));
        }

        [Test]
        public void Guard_Held_DoesNotRetrigger()
        {
            _rules.Begin(At(Center));
            Run(Dt, At(Center));
            Run(6f, Guard(Center)); // two full shield + cooldown cycles

            Assert.AreEqual(1, Count(CombatEventType.ShieldRaised));
            Assert.IsFalse(_rules.ShieldActive);
        }

        [Test]
        public void Guard_LowerAndRaiseAfterCooldown_RaisesAgain()
        {
            _rules.Begin(At(Center));
            Run(Dt, Guard(Center));
            Run(1f, Guard(Center));
            Run(2.1f, At(Center)); // shield down at 1.51, cooldown over at 3.01

            Run(Dt, Guard(Center));

            Assert.AreEqual(2, Count(CombatEventType.ShieldRaised));
            Assert.IsTrue(_rules.ShieldActive);
        }

        [Test]
        public void Guard_DuringCooldown_HeldFiresWhenCooldownEnds()
        {
            _rules.Begin(At(Center));
            Run(Dt, Guard(Center));
            Run(1.99f, At(Center)); // t = 2.0: cooldown until 3.01

            Run(1.2f, Guard(Center));

            Assert.AreEqual(2, Count(CombatEventType.ShieldRaised));
            Assert.AreEqual(1.5f, TimeOf(CombatEventType.ShieldRaised, 1) - TimeOf(CombatEventType.ShieldLowered), Tolerance);
        }

        [Test]
        public void Guard_DuringCooldown_LoweredBeforeEnd_IsDropped()
        {
            _rules.Begin(At(Center));
            Run(Dt, Guard(Center));
            Run(1.99f, At(Center));
            Run(0.5f, Guard(Center)); // t = 2.5, still in the cooldown
            Run(2f, At(Center));

            Assert.AreEqual(1, Count(CombatEventType.ShieldRaised));
        }

        [Test]
        public void Guard_WhileShieldUp_IsUsedUp()
        {
            _rules.Begin(At(Center));
            Run(Dt, At(Center, 1f)); // waving raises it
            Run(0.5f, At(Center));
            Run(4f, Guard(Center)); // raised during the shield, held past the cooldown

            Assert.AreEqual(1, Count(CombatEventType.ShieldRaised));
        }

        [Test]
        public void Guard_HeldAtBegin_DoesNotRaise()
        {
            _rules.Begin(Guard(Center));
            Run(3f, Guard(Center));

            Assert.AreEqual(0, Count(CombatEventType.ShieldRaised));
        }

        [Test]
        public void Guard_PoseDisabled_IsIgnored()
        {
            _config.shieldPoseEnabled = false;
            _rules.Begin(At(Center));
            Run(Dt, At(Center));
            Run(1f, Guard(Center));
            Assert.AreEqual(0, Count(CombatEventType.ShieldRaised));

            _config.shieldPoseEnabled = true; // turned on mid-hold: not a new raise
            Run(1f, Guard(Center));
            Assert.AreEqual(0, Count(CombatEventType.ShieldRaised));
        }

        [Test]
        public void Guard_RaisedAfterLock_Parries()
        {
            _rules.Begin(At(Center));
            Run(1.25f, At(Center)); // locked at 1.2
            Run(0.75f + Dt, Guard(Center));

            Assert.AreEqual(1, Count(CombatEventType.Parried));
            Assert.AreEqual(0, Count(CombatEventType.PlayerHit));
        }

        [Test]
        public void Guard_Waving_StillRaisesWithArmsDown()
        {
            _rules.Begin(At(Center));
            Run(Dt, new PlayerSample { position = Center, hasPosition = true, motion = 1f, guardPose = false });

            Assert.IsTrue(_rules.ShieldActive);
        }

        // End conditions

        [Test]
        public void End_BossHpZero_Victory()
        {
            _rules.Begin(At(Center));
            Run(30f, At(Center, 0f)); // no shield: not this path
            Assert.AreEqual(GameResult.GameOver, _rules.Result);

            CreateRules();
            _config.shieldDurationSec = 10f;
            _config.shieldCooldownSec = 0f;
            _rules.Begin(At(Center));
            Run(30f, At(Center, 1f));

            Assert.AreEqual(GameResult.Victory, _rules.Result);
            Assert.AreEqual(5, Count(CombatEventType.Parried));
            Assert.AreEqual(1, Count(CombatEventType.BossDefeated));
            Assert.AreEqual(0f, _rules.BossHp01);
            Assert.AreEqual(16f, TimeOf(CombatEventType.BossDefeated), 0.05f);
            Assert.IsFalse(_rules.IsRunning);
        }

        [Test]
        public void End_BossDefeated_ReportsBossPosition()
        {
            _config.bossMaxHp = 1;
            _config.shieldDurationSec = 10f;
            _rules = new CombatRules(_config) { BossPosition = new Vector2(0.5f, 0.7f) };
            _rules.EventRaised += _events.Add;
            _rules.Begin(At(Center));
            Run(2.1f, At(Center, 1f));

            Assert.AreEqual(new Vector2(0.5f, 0.7f), _events.Find(e => e.type == CombatEventType.BossDefeated).position);
        }

        [Test]
        public void End_PlayerHpZero_GameOver()
        {
            _rules.Begin(At(Center));
            Run(30f, At(Center));

            Assert.AreEqual(GameResult.GameOver, _rules.Result);
            Assert.AreEqual(5, Count(CombatEventType.PlayerHit));
            Assert.AreEqual(0f, _rules.PlayerHp01);
            Assert.AreEqual(1f, _rules.BossHp01);
            Assert.AreEqual(0, Count(CombatEventType.BossDefeated));
        }

        [Test]
        public void End_DurationExceeded_GameOver()
        {
            _config.playerMaxHp = 100;
            _rules.Begin(At(Center));
            Run(59.8f, At(Center));
            Assert.IsTrue(_rules.IsRunning);

            Run(0.4f, At(Center));

            Assert.IsFalse(_rules.IsRunning);
            Assert.AreEqual(GameResult.GameOver, _rules.Result);
            Assert.AreEqual(0f, _rules.RemainingSec);
        }

        [Test]
        public void End_NoInputForAbandonTime_GameOver()
        {
            _config.playerMaxHp = 100;
            _rules.Begin(At(Center));
            Run(9.9f, NoInput);
            Assert.IsTrue(_rules.IsRunning);

            Run(0.2f, NoInput);

            Assert.IsFalse(_rules.IsRunning);
            Assert.AreEqual(GameResult.GameOver, _rules.Result);
        }

        [Test]
        public void End_InputReturning_RestartsAbandonTime()
        {
            _config.playerMaxHp = 100;
            _rules.Begin(At(Center));
            Run(9f, NoInput);
            Run(Dt, At(Center));
            Run(9f, NoInput);

            Assert.IsTrue(_rules.IsRunning);
        }

        [Test]
        public void End_ActiveShield_IsLowered()
        {
            _config.shieldDurationSec = 10f;
            _config.bossMaxHp = 1;
            _rules.Begin(At(Center));
            Run(2.1f, At(Center, 1f));

            Assert.IsFalse(_rules.ShieldActive);
            Assert.AreEqual(1, Count(CombatEventType.ShieldLowered));
        }

        [Test]
        public void End_TickAfterFinish_ChangesNothing()
        {
            _config.playerMaxHp = 1;
            _rules.Begin(At(Center));
            Run(2.1f, At(Center));
            Assert.AreEqual(GameResult.GameOver, _rules.Result);
            int events = _events.Count;

            Run(5f, At(Center, 1f));

            Assert.AreEqual(events, _events.Count);
            Assert.AreEqual(GameResult.GameOver, _rules.Result);
        }

        // Both HP at zero

        [Test]
        public void BothHpZero_ImpossibleAtLastHp()
        {
            _config.bossMaxHp = 1;
            _config.playerMaxHp = 1;
            _rules.Begin(At(Center));

            // Arrival in a single long frame, with everything at its most lethal.
            for (int i = 0; i < 25; i++) _rules.Tick(0.1f, At(Center, i >= 15 ? 1f : 0f), 1f);

            Assert.AreEqual(GameResult.Victory, _rules.Result);
            Assert.AreEqual(1f, _rules.PlayerHp01);
            Assert.AreEqual(0f, _rules.BossHp01);
        }

        [Test]
        public void BothHpZero_NeverOverAnyShieldTiming()
        {
            // Every shield start with the fast timings, so several stages can fall into one 0.1 s frame.
            _config.aimSec = 0.2f;
            _config.aimLockSec = 0f;
            _config.projectileFlightSec = 0.05f;
            _config.attackRecoverSec = 0f;
            _config.shieldDurationSec = 0.1f;
            _config.shieldCooldownSec = 0f;
            _config.bossMaxHp = 3;
            _config.playerMaxHp = 3;

            for (int offset = 0; offset < 40; offset++)
            {
                CreateRules();
                _rules.Begin(At(Center));
                for (int i = 0; i < 2000 && _rules.IsRunning; i++)
                {
                    bool wave = (i + offset) % 7 < 3;
                    _rules.Tick(0.1f, At(Center, wave ? 1f : 0f), 1f);
                }

                Assert.AreNotEqual(GameResult.None, _rules.Result);
                Assert.IsFalse(_rules.BossHp01 <= 0f && _rules.PlayerHp01 <= 0f, $"offset {offset}");
                Assert.AreEqual(_rules.Result == GameResult.Victory, _rules.BossHp01 <= 0f, $"offset {offset}");
            }
        }

        [Test]
        public void LastArrival_BeatsTimeoutOnSameFrame()
        {
            // Both fall inside the frame ending at 2.0 s, well away from any float rounding.
            _config.projectileFlightSec = 0.45f;
            _config.combatDurationSec = 1.95f;
            _config.bossMaxHp = 1;
            _config.shieldDurationSec = 10f;
            _rules.Begin(At(Center));

            for (int i = 0; i < 19; i++) _rules.Tick(0.1f, At(Center, 1f), 1f);
            Assert.IsTrue(_rules.IsRunning);

            _rules.Tick(0.1f, At(Center, 1f), 1f);

            Assert.AreEqual(GameResult.Victory, _rules.Result);
        }

        // Reset

        [Test]
        public void Reset_ClearsWithoutEvents()
        {
            _rules.Begin(At(Center));
            Run(2.1f, At(Center, 1f));
            int events = _events.Count;

            _rules.Reset();

            Assert.AreEqual(events, _events.Count);
            Assert.IsFalse(_rules.IsRunning);
            Assert.IsFalse(_rules.ShieldActive);
            Assert.IsFalse(_rules.IsAiming);
            Assert.AreEqual(GameResult.None, _rules.Result);
            Assert.AreEqual(1f, _rules.BossHp01);
            Assert.AreEqual(1f, _rules.PlayerHp01);
        }

        [Test]
        public void Begin_AfterFinish_StartsFresh()
        {
            _config.playerMaxHp = 1;
            _rules.Begin(At(Center));
            Run(2.1f, At(Center));

            _rules.Begin(At(Center));

            Assert.IsTrue(_rules.IsRunning);
            Assert.AreEqual(GameResult.None, _rules.Result);
            Assert.AreEqual(1f, _rules.PlayerHp01);
            Assert.AreEqual(60f, _rules.RemainingSec);
        }

        [Test]
        public void HpFraction_NotChangedByConfigEditDuringCombat()
        {
            _rules.Begin(At(Center));
            PlayToArrival(Center, Center);
            _config.playerMaxHp = 10;

            Assert.AreEqual(0.8f, _rules.PlayerHp01, 1e-5f);
        }

        /// <summary>Waving nonstop must not win by itself: the shield is up half the time and drifts off the shots.</summary>
        [Test]
        public void Balance_WavingNonstopAtDefaults_DoesNotWin()
        {
            _config.hitRadius = 0.15f;
            _rules.Begin(At(Center));
            Run(60f, At(Center, 1f));

            Assert.AreEqual(GameResult.GameOver, _rules.Result);
            Assert.Greater(Count(CombatEventType.Parried), 0);
        }
    }
}
