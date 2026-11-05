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

        /// <summary>Help text under the controls (also baked into the prefab by the builder).</summary>
        public const string RestartNoticeText =
            "감도: 낮을수록 민감 (노이즈도 잡힘), 높을수록 둔감\n" +
            "  → 아무도 없을 때 그리드가 꺼진 채 유지되는 가장 낮은 값\n" +
            "좌우 반전: 켜면 거울처럼 보임\n" +
            "* 감도·좌우 반전은 즉시 적용, [설정 저장] 시 다음 실행에도 유지\n" +
            "* 카메라·그리드·UDP 설정은 config.json 수정 후 재실행";

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
                    RestartNoticeText);
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
