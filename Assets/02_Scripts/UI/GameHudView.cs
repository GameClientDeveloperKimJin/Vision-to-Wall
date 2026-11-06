using System;
using System.Text;
using TMPro;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// Combat HUD: boss HP, player HP, shield, combo and remaining time. Hidden on the media wall, fades in during
    /// Awaken and fades out after Result. Uses unscaled time because Result runs in slow motion. Text is only set
    /// when its value changes: an idle frame allocates nothing, and TMP allocates 2 objects per text change.
    /// </summary>
    public sealed class GameHudView : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Boss")]
        [SerializeField] private RectTransform bossFill;
        [SerializeField] private TMP_Text bossPercent;
        [SerializeField] private TMP_Text remainingTime;

        [Header("Player")]
        [SerializeField] private RectTransform playerFill;
        [SerializeField] private TMP_Text playerLabel;
        [SerializeField] private GameObject shieldStatus;

        [Header("Combo")]
        [SerializeField] private GameObject comboRoot;
        [SerializeField] private TMP_Text comboValue;

        [SerializeField] private ResultBannerView resultBanner;

        [Header("Timing (unscaled seconds)")]
        [SerializeField, Min(0.01f)] private float fadeInSec = 1f;
        [SerializeField, Min(0.01f)] private float fadeOutSec = 0.6f;
        [SerializeField, Min(0.01f), Tooltip("Time for a bar to cross its whole length. Keep the last hit shorter than the boss hiding on victory.")]
        private float hpFollowSec = 0.6f;

        private readonly StringBuilder _sb = new StringBuilder(8);

        private IGamePhaseSource _phase;
        private ICombatStateSource _combat;
        private GameHudModel _model;
        private Action<GamePhase> _onPhaseChanged;
        private Action<CombatEvent> _onCombatEvent;

        private float _shownBoss = -1f;
        private float _shownPlayer = -1f;
        private int _shownBossPercent = -1;
        private int _shownPlayerPercent = -1;
        private int _shownSeconds = -1;
        private int _shownCombo = -1;
        private bool _shownCombo2Plus;
        private bool _shownShield;

        public GameHudModel Model => _model;

        /// <summary>Called once by the App.</summary>
        public void Initialize(IGamePhaseSource phase, ICombatStateSource combat)
        {
            if (phase == null) throw new ArgumentNullException(nameof(phase));
            if (combat == null) throw new ArgumentNullException(nameof(combat));

            Unsubscribe();

            _phase = phase;
            _combat = combat;
            _model = new GameHudModel(fadeInSec, fadeOutSec, hpFollowSec);
            _onPhaseChanged = OnPhaseChanged;
            _onCombatEvent = OnCombatEvent;
            _phase.PhaseChanged += _onPhaseChanged;
            _combat.CombatEventRaised += _onCombatEvent;

            _shownShield = false;
            if (shieldStatus != null) shieldStatus.SetActive(false);
            _shownCombo2Plus = false;
            if (comboRoot != null) comboRoot.SetActive(false);

            OnPhaseChanged(_phase.Phase);
            ApplyAlpha();
            ApplyBars();
        }

        private void Awake()
        {
            // Nothing may show before Initialize.
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (canvas != null) canvas.enabled = false;
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            if (_phase != null && _onPhaseChanged != null) _phase.PhaseChanged -= _onPhaseChanged;
            if (_combat != null && _onCombatEvent != null) _combat.CombatEventRaised -= _onCombatEvent;
        }

        private void OnPhaseChanged(GamePhase phase)
        {
            _model.OnPhaseChanged(phase);

            if (resultBanner == null) return;
            if (phase == GamePhase.Result) resultBanner.Show(_phase.Result);
            else resultBanner.Hide();
        }

        private void OnCombatEvent(CombatEvent e) => _model.OnCombatEvent(e.type);

        private void Update()
        {
            if (_model == null) return;

            GamePhase phase = _phase.Phase;
            _model.Tick(Time.unscaledDeltaTime, phase, _combat.BossHp01, _combat.PlayerHp01);
            ApplyAlpha();
            if (_model.Alpha <= 0f) return;

            ApplyBars();
            ApplyShield(phase);
            ApplyCombo();
            ApplyTime(phase);
        }

        private void ApplyAlpha()
        {
            if (canvasGroup != null) canvasGroup.alpha = _model.Alpha;

            bool on = _model.Alpha > 0f;
            if (canvas != null && canvas.enabled != on) canvas.enabled = on;
        }

        private void ApplyBars()
        {
            if (!Mathf.Approximately(_shownBoss, _model.BossHp))
            {
                _shownBoss = _model.BossHp;
                SetFill(bossFill, _shownBoss);
            }

            if (!Mathf.Approximately(_shownPlayer, _model.PlayerHp))
            {
                _shownPlayer = _model.PlayerHp;
                SetFill(playerFill, _shownPlayer);
            }

            int boss = _model.BossPercent;
            if (boss != _shownBossPercent)
            {
                _shownBossPercent = boss;
                if (bossPercent != null) bossPercent.SetText("{0}%", boss);
            }

            int player = _model.PlayerPercent;
            if (player != _shownPlayerPercent)
            {
                _shownPlayerPercent = player;
                if (playerLabel != null) playerLabel.SetText("PLAYER HP: {0}%", player);
            }
        }

        private static void SetFill(RectTransform fill, float value)
        {
            if (fill != null) fill.anchorMax = new Vector2(value, 1f);
        }

        private void ApplyShield(GamePhase phase)
        {
            bool on = phase == GamePhase.Combat && _combat.ShieldActive;
            if (on == _shownShield) return;

            _shownShield = on;
            if (shieldStatus != null) shieldStatus.SetActive(on);
        }

        private void ApplyCombo()
        {
            bool visible = _model.ComboVisible;
            if (visible != _shownCombo2Plus)
            {
                _shownCombo2Plus = visible;
                if (comboRoot != null) comboRoot.SetActive(visible);
            }

            if (!visible || _model.Combo == _shownCombo) return;

            _shownCombo = _model.Combo;
            if (comboValue != null) comboValue.SetText("x{0}", _shownCombo);
        }

        private void ApplyTime(GamePhase phase)
        {
            // Combat state still holds the last fight during Awaken, so show the time the next one will start with.
            float remaining;
            switch (phase)
            {
                case GamePhase.Awaken: remaining = ConfigService.Current.combatDurationSec; break;
                case GamePhase.MediaWall: return;
                default: remaining = _combat.RemainingSec; break;
            }

            int seconds = Mathf.CeilToInt(remaining);
            if (seconds == _shownSeconds) return;

            _shownSeconds = seconds;
            if (remainingTime == null) return;

            _sb.Length = 0;
            GameHudText.AppendClock(_sb, seconds);
            remainingTime.SetText(_sb);
        }
    }
}
