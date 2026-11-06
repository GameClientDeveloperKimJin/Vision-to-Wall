using TMPro;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// VICTORY / GAME OVER. Waits for <see cref="showDelaySec"/> so the boss explosion is seen first. Driven by
    /// <see cref="GameHudView"/>. Uses unscaled time because Result runs in slow motion.
    /// </summary>
    public sealed class ResultBannerView : MonoBehaviour
    {
        public const string VictoryText = "VICTORY";
        public const string GameOverText = "GAME OVER";

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Color victoryColor = new Color(0.45f, 0.95f, 1f, 1f);
        [SerializeField] private Color gameOverColor = new Color(1f, 0.3f, 0.28f, 1f);
        [SerializeField, Min(0f), Tooltip("Matches the boss hiding on victory (PresentationTuning.revealVictoryHoldSec).")]
        private float showDelaySec = 0.6f;
        [SerializeField, Min(0.01f)] private float fadeSec = 0.4f;

        private float _delayLeft;
        private float _targetAlpha;

        public void Show(GameResult result)
        {
            if (result == GameResult.None)
            {
                Hide();
                return;
            }

            if (label != null)
            {
                bool victory = result == GameResult.Victory;
                label.SetText(victory ? VictoryText : GameOverText);
                label.color = victory ? victoryColor : gameOverColor;
            }

            _delayLeft = showDelaySec;
            _targetAlpha = 1f;
        }

        public void Hide()
        {
            _delayLeft = 0f;
            _targetAlpha = 0f;
        }

        private void Awake()
        {
            if (canvasGroup != null) canvasGroup.alpha = 0f;
        }

        private void Update()
        {
            if (canvasGroup == null) return;

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, GameHudModel.MaxFrameDeltaSec);
            if (_delayLeft > 0f)
            {
                _delayLeft -= deltaTime;
                return;
            }

            if (Mathf.Approximately(canvasGroup.alpha, _targetAlpha)) return;
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, _targetAlpha, deltaTime / fadeSec);
        }
    }
}
