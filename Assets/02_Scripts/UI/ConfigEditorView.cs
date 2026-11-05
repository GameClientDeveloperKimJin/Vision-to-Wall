using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// FR-08/09 runtime config editor: threshold slider and mirror toggle (applied immediately via
    /// <see cref="ConfigService.NotifyChanged"/>), Save button, and the list of fields that need a restart.
    /// </summary>
    public sealed class ConfigEditorView : MonoBehaviour
    {
        public const float MinThreshold = 0.01f;
        public const float MaxThreshold = 0.50f;

        [SerializeField] private Slider thresholdSlider;
        [SerializeField] private TMP_Text thresholdValue;
        [SerializeField] private Toggle mirrorToggle;
        [SerializeField] private Button saveButton;
        [SerializeField] private TMP_Text saveStatus;
        [SerializeField] private TMP_Text restartNotice;
        [SerializeField, Min(0.1f)] private float savedMessageSec = 2f;

        private float _clearStatusAt = -1f;

        private void Awake()
        {
            if (thresholdSlider != null)
            {
                thresholdSlider.minValue = MinThreshold;
                thresholdSlider.maxValue = MaxThreshold;
                thresholdSlider.wholeNumbers = false;
                thresholdSlider.onValueChanged.AddListener(OnThresholdChanged);
            }
            if (mirrorToggle != null) mirrorToggle.onValueChanged.AddListener(OnMirrorChanged);
            if (saveButton != null) saveButton.onClick.AddListener(OnSaveClicked);
            if (saveStatus != null) saveStatus.SetText(string.Empty);

            if (restartNotice != null)
            {
                restartNotice.SetText(
                    "* 재시작 필요: cameraIndex, cameraWidth/Height, cameraFps, gridWidth/Height, " +
                    "udpIp, udpPort (config.json 수정 후 재실행)\n" +
                    "* 즉시 반영: threshold, mirror");
            }
        }

        private void OnDestroy()
        {
            if (thresholdSlider != null) thresholdSlider.onValueChanged.RemoveListener(OnThresholdChanged);
            if (mirrorToggle != null) mirrorToggle.onValueChanged.RemoveListener(OnMirrorChanged);
            if (saveButton != null) saveButton.onClick.RemoveListener(OnSaveClicked);
        }

        /// <summary>Pulls current values into the controls without raising change events.</summary>
        public void SyncFromConfig()
        {
            AppConfig c = ConfigService.Current;
            if (thresholdSlider != null) thresholdSlider.SetValueWithoutNotify(c.threshold);
            if (mirrorToggle != null) mirrorToggle.SetIsOnWithoutNotify(c.mirror);
            UpdateThresholdLabel(c.threshold);
        }

        private void Update()
        {
            if (_clearStatusAt < 0f || Time.unscaledTime < _clearStatusAt) return;
            _clearStatusAt = -1f;
            if (saveStatus != null) saveStatus.SetText(string.Empty);
        }

        private void OnThresholdChanged(float value)
        {
            ConfigService.Current.threshold = Mathf.Clamp(value, MinThreshold, MaxThreshold);
            ConfigService.NotifyChanged();
            UpdateThresholdLabel(ConfigService.Current.threshold);
        }

        private void OnMirrorChanged(bool value)
        {
            ConfigService.Current.mirror = value;
            ConfigService.NotifyChanged();
        }

        private void OnSaveClicked()
        {
            bool ok = ConfigService.Save();
            if (ok)
            {
                Debug.Log($"[UI] Saved {ConfigService.FilePath} (threshold={ConfigService.Current.threshold:0.###}, " +
                          $"mirror={ConfigService.Current.mirror})");
            }
            if (saveStatus != null) saveStatus.SetText(ok ? "저장됨" : "저장 실패 (로그 확인)");
            _clearStatusAt = Time.unscaledTime + savedMessageSec;
        }

        private void UpdateThresholdLabel(float value)
        {
            if (thresholdValue != null) thresholdValue.SetText("{0:2}", value);
        }
    }
}
