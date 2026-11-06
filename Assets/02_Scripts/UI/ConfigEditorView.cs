using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// FR-08/09 runtime config editor.
    /// Webcam mode: threshold slider and mirror toggle (applied immediately via <see cref="ConfigService.NotifyChanged"/>).
    /// External mode: hand sensor sliders/toggle instead of threshold; the external process re-reads config.json,
    /// so hand values are auto-saved shortly after the last change.
    /// Save button and help text in both modes.
    /// </summary>
    public sealed class ConfigEditorView : MonoBehaviour
    {
        public const float MinThreshold = 0.01f;
        public const float MaxThreshold = 0.50f;
        public const float MinHandSpeedRef = 0.3f;
        public const float MaxHandSpeedRef = 5f;
        public const float MinHandMinSpeed = 0f;
        public const float MaxHandMinSpeed = 0.5f;
        public const float MinShieldThreshold = 0.3f;
        public const float MaxShieldThreshold = 1f;
        public const float MinAwakenChargeSec = 1f;
        public const float MaxAwakenChargeSec = 10f;

        private const string GameNoticeText =
            "실드 발동 감도: 높일수록 크게 흔들어야 발동\n" +
            "각성 요구 시간: 초 단위. 게임 값은 즉시 적용, [설정 저장] 시 유지\n";

        /// <summary>Help text in webcam mode (also baked into the prefab by the builder).</summary>
        public const string RestartNoticeText =
            "감지 기준값: 낮을수록 민감 (노이즈도 잡힘), 높을수록 둔감\n" +
            "  → 아무도 없을 때 그리드가 꺼진 채 유지되는 가장 낮은 값\n" +
            "좌우 반전: 켜면 거울처럼 보임\n" +
            GameNoticeText +
            "* 감지 기준값·좌우 반전은 즉시 적용, [설정 저장] 시 다음 실행에도 유지\n" +
            "* 카메라·그리드·UDP 설정은 config.json 수정 후 재실행";

        /// <summary>Help text in external (hand sensor) mode.</summary>
        public const string ExternalNoticeText =
            "손 속도 기준: 낮출수록 살살 흔들어도 강하게 반응\n" +
            "정지 판정 속도: 몸만 움직여도 반응하면 올리기\n" +
            "정지한 손도 표시: 가만히 둔 손 위치도 약하게 표시\n" +
            GameNoticeText +
            "* 손 센서 값은 자동 저장, 0.5초 안에 반영\n" +
            "* 좌우 반전·카메라·UDP는 손 센서 재실행";

        [Header("Webcam mode")]
        [SerializeField] private GameObject thresholdRow;
        [SerializeField] private Slider thresholdSlider;
        [SerializeField] private TMP_Text thresholdValue;
        [SerializeField] private Toggle mirrorToggle;

        [Header("External (hand sensor) mode")]
        [SerializeField] private GameObject handSection;
        [SerializeField] private Slider handSpeedRefSlider;
        [SerializeField] private TMP_Text handSpeedRefValue;
        [SerializeField] private Slider handMinSpeedSlider;
        [SerializeField] private TMP_Text handMinSpeedValue;
        [SerializeField] private Toggle handEmitStillToggle;
        [SerializeField, Min(0.1f)] private float handAutoSaveDelaySec = 0.5f;

        [Header("Game")]
        [SerializeField] private Slider shieldThresholdSlider;
        [SerializeField] private TMP_Text shieldThresholdValue;
        [SerializeField] private Slider awakenChargeSlider;
        [SerializeField] private TMP_Text awakenChargeValue;

        [Header("Common")]
        [SerializeField] private Button saveButton;
        [SerializeField] private TMP_Text saveStatus;
        [SerializeField] private TMP_Text restartNotice;
        [SerializeField, Min(0.1f)] private float savedMessageSec = 2f;

        private float _clearStatusAt = -1f;
        private float _autoSaveAt = -1f;
        private bool _external;

        private void Awake()
        {
            SetupSlider(thresholdSlider, MinThreshold, MaxThreshold, OnThresholdChanged);
            SetupSlider(handSpeedRefSlider, MinHandSpeedRef, MaxHandSpeedRef, OnHandSpeedRefChanged);
            SetupSlider(handMinSpeedSlider, MinHandMinSpeed, MaxHandMinSpeed, OnHandMinSpeedChanged);
            SetupSlider(shieldThresholdSlider, MinShieldThreshold, MaxShieldThreshold, OnShieldThresholdChanged);
            SetupSlider(awakenChargeSlider, MinAwakenChargeSec, MaxAwakenChargeSec, OnAwakenChargeChanged);
            if (mirrorToggle != null) mirrorToggle.onValueChanged.AddListener(OnMirrorChanged);
            if (handEmitStillToggle != null) handEmitStillToggle.onValueChanged.AddListener(OnHandEmitStillChanged);
            if (saveButton != null) saveButton.onClick.AddListener(OnSaveClicked);
            if (saveStatus != null) saveStatus.SetText(string.Empty);
            ApplyMode();
        }

        private void OnDestroy()
        {
            if (thresholdSlider != null) thresholdSlider.onValueChanged.RemoveListener(OnThresholdChanged);
            if (handSpeedRefSlider != null) handSpeedRefSlider.onValueChanged.RemoveListener(OnHandSpeedRefChanged);
            if (handMinSpeedSlider != null) handMinSpeedSlider.onValueChanged.RemoveListener(OnHandMinSpeedChanged);
            if (shieldThresholdSlider != null) shieldThresholdSlider.onValueChanged.RemoveListener(OnShieldThresholdChanged);
            if (awakenChargeSlider != null) awakenChargeSlider.onValueChanged.RemoveListener(OnAwakenChargeChanged);
            if (mirrorToggle != null) mirrorToggle.onValueChanged.RemoveListener(OnMirrorChanged);
            if (handEmitStillToggle != null) handEmitStillToggle.onValueChanged.RemoveListener(OnHandEmitStillChanged);
            if (saveButton != null) saveButton.onClick.RemoveListener(OnSaveClicked);
        }

        /// <summary>Pulls current values into the controls without raising change events.</summary>
        public void SyncFromConfig()
        {
            ApplyMode();
            AppConfig c = ConfigService.Current;
            if (thresholdSlider != null) thresholdSlider.SetValueWithoutNotify(c.threshold);
            if (mirrorToggle != null) mirrorToggle.SetIsOnWithoutNotify(c.mirror);
            if (handSpeedRefSlider != null) handSpeedRefSlider.SetValueWithoutNotify(c.handSpeedRef);
            if (handMinSpeedSlider != null) handMinSpeedSlider.SetValueWithoutNotify(c.handMinSpeed);
            if (handEmitStillToggle != null) handEmitStillToggle.SetIsOnWithoutNotify(c.handEmitStill);
            if (shieldThresholdSlider != null) shieldThresholdSlider.SetValueWithoutNotify(c.shieldThreshold);
            if (awakenChargeSlider != null) awakenChargeSlider.SetValueWithoutNotify(c.awakenChargeSec);
            SetValueLabel(thresholdValue, c.threshold);
            SetValueLabel(handSpeedRefValue, c.handSpeedRef);
            SetValueLabel(handMinSpeedValue, c.handMinSpeed);
            SetValueLabel(shieldThresholdValue, c.shieldThreshold);
            SetValueLabel(awakenChargeValue, c.awakenChargeSec);
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            if (_autoSaveAt >= 0f && now >= _autoSaveAt)
            {
                _autoSaveAt = -1f;
                Save("손 센서에 반영됨");
            }

            if (_clearStatusAt >= 0f && now >= _clearStatusAt)
            {
                _clearStatusAt = -1f;
                if (saveStatus != null) saveStatus.SetText(string.Empty);
            }
        }

        private void ApplyMode()
        {
            _external = ConfigService.Current.UseExternalSensor;
            if (thresholdRow != null) thresholdRow.SetActive(!_external);
            if (handSection != null) handSection.SetActive(_external);
            if (restartNotice != null) restartNotice.SetText(_external ? ExternalNoticeText : RestartNoticeText);
        }

        private void OnThresholdChanged(float value)
        {
            ConfigService.Current.threshold = Mathf.Clamp(value, MinThreshold, MaxThreshold);
            ConfigService.NotifyChanged();
            SetValueLabel(thresholdValue, ConfigService.Current.threshold);
        }

        private void OnMirrorChanged(bool value)
        {
            ConfigService.Current.mirror = value;
            ConfigService.NotifyChanged();
        }

        private void OnHandSpeedRefChanged(float value)
        {
            ConfigService.Current.handSpeedRef = Mathf.Clamp(value, MinHandSpeedRef, MaxHandSpeedRef);
            SetValueLabel(handSpeedRefValue, ConfigService.Current.handSpeedRef);
            ScheduleHandAutoSave();
        }

        private void OnHandMinSpeedChanged(float value)
        {
            ConfigService.Current.handMinSpeed = Mathf.Clamp(value, MinHandMinSpeed, MaxHandMinSpeed);
            SetValueLabel(handMinSpeedValue, ConfigService.Current.handMinSpeed);
            ScheduleHandAutoSave();
        }

        private void OnHandEmitStillChanged(bool value)
        {
            ConfigService.Current.handEmitStill = value;
            ScheduleHandAutoSave();
        }

        // CombatRules and AwakenGauge read the shared config every tick, so these apply at once.
        private void OnShieldThresholdChanged(float value)
        {
            ConfigService.Current.shieldThreshold = Mathf.Clamp(value, MinShieldThreshold, MaxShieldThreshold);
            ConfigService.NotifyChanged();
            SetValueLabel(shieldThresholdValue, ConfigService.Current.shieldThreshold);
        }

        private void OnAwakenChargeChanged(float value)
        {
            ConfigService.Current.awakenChargeSec = Mathf.Clamp(value, MinAwakenChargeSec, MaxAwakenChargeSec);
            ConfigService.NotifyChanged();
            SetValueLabel(awakenChargeValue, ConfigService.Current.awakenChargeSec);
        }

        private void ScheduleHandAutoSave()
        {
            ConfigService.NotifyChanged();
            _autoSaveAt = Time.unscaledTime + handAutoSaveDelaySec;
        }

        private void OnSaveClicked()
        {
            _autoSaveAt = -1f;
            Save("저장됨");
        }

        private void Save(string okMessage)
        {
            bool ok = ConfigService.Save();
            if (ok)
            {
                AppConfig c = ConfigService.Current;
                Debug.Log($"[UI] Saved {ConfigService.FilePath} (threshold={c.threshold:0.###}, mirror={c.mirror}, " +
                          $"handSpeedRef={c.handSpeedRef:0.##}, handMinSpeed={c.handMinSpeed:0.###}, handEmitStill={c.handEmitStill}, " +
                          $"shieldThreshold={c.shieldThreshold:0.##}, awakenChargeSec={c.awakenChargeSec:0.##})");
            }
            if (saveStatus != null) saveStatus.SetText(ok ? okMessage : "저장 실패 (로그 확인)");
            _clearStatusAt = Time.unscaledTime + savedMessageSec;
        }

        private static void SetupSlider(Slider slider, float min, float max, UnityEngine.Events.UnityAction<float> onChanged)
        {
            if (slider == null) return;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.onValueChanged.AddListener(onChanged);
        }

        private static void SetValueLabel(TMP_Text label, float value)
        {
            if (label != null) label.SetText("{0:2}", value);
        }
    }
}
