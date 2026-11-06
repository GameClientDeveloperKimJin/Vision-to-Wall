using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// What the combat HUD shows: fade, displayed HP and combo. Holds no Unity objects, so the rules can be tested
    /// without a scene. Allocation free.
    /// </summary>
    public sealed class GameHudModel
    {
        /// <summary>Longest time one frame can count for. Same limit as the phase machine, which UI cannot reference.</summary>
        public const float MaxFrameDeltaSec = 0.1f;

        /// <summary>The combo panel stays hidden below this count.</summary>
        public const int MinVisibleCombo = 2;

        private readonly float _fadeInSec;
        private readonly float _fadeOutSec;
        private readonly float _hpFollowSec;

        public float Alpha { get; private set; }
        public float BossHp { get; private set; } = 1f;
        public float PlayerHp { get; private set; } = 1f;

        /// <summary>Follows the real value, not the moving bar: TMP allocates on every text change, so one change per hit.</summary>
        public int BossPercent { get; private set; } = 100;
        public int PlayerPercent { get; private set; } = 100;

        public int Combo { get; private set; }
        public bool ComboVisible => Combo >= MinVisibleCombo;

        /// <param name="hpFollowSec">Time for a bar to cross its whole length.</param>
        public GameHudModel(float fadeInSec, float fadeOutSec, float hpFollowSec)
        {
            _fadeInSec = Mathf.Max(0.01f, fadeInSec);
            _fadeOutSec = Mathf.Max(0.01f, fadeOutSec);
            _hpFollowSec = Mathf.Max(0.01f, hpFollowSec);
        }

        /// <summary>
        /// Combat state keeps the last fight's values until the next Begin, so the bars are filled here and do not
        /// read it before Combat.
        /// </summary>
        public void OnPhaseChanged(GamePhase phase)
        {
            if (phase != GamePhase.Awaken && phase != GamePhase.Combat) return;

            BossHp = 1f;
            PlayerHp = 1f;
            BossPercent = 100;
            PlayerPercent = 100;
            Combo = 0;
        }

        public void OnCombatEvent(CombatEventType type)
        {
            if (type == CombatEventType.Parried) Combo++;
            else if (type == CombatEventType.PlayerHit) Combo = 0;
        }

        public void Tick(float deltaTime, GamePhase phase, float bossHp01, float playerHp01)
        {
            deltaTime = Mathf.Min(deltaTime, MaxFrameDeltaSec);

            bool visible = phase != GamePhase.MediaWall;
            Alpha = Mathf.MoveTowards(Alpha, visible ? 1f : 0f, deltaTime / (visible ? _fadeInSec : _fadeOutSec));

            if (phase != GamePhase.Combat && phase != GamePhase.Result) return;

            float step = deltaTime / _hpFollowSec;
            BossHp = Mathf.MoveTowards(BossHp, Mathf.Clamp01(bossHp01), step);
            PlayerHp = Mathf.MoveTowards(PlayerHp, Mathf.Clamp01(playerHp01), step);
            BossPercent = Mathf.RoundToInt(Mathf.Clamp01(bossHp01) * 100f);
            PlayerPercent = Mathf.RoundToInt(Mathf.Clamp01(playerHp01) * 100f);
        }
    }
}
