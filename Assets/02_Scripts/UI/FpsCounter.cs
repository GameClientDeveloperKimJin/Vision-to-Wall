using UnityEngine;

namespace VisionToWall.UI
{
    /// <summary>Exponentially smoothed FPS from <c>Time.unscaledDeltaTime</c>. Frame-rate independent smoothing.</summary>
    public sealed class FpsCounter
    {
        private readonly float _timeConstant;
        private bool _hasValue;

        /// <param name="timeConstantSec">Smoothing time constant; ~63% of a step change after this long.</param>
        public FpsCounter(float timeConstantSec = 0.5f)
        {
            _timeConstant = Mathf.Max(0.01f, timeConstantSec);
        }

        public float Value { get; private set; }

        public void Reset()
        {
            _hasValue = false;
            Value = 0f;
        }

        public void Sample(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            float instant = 1f / deltaTime;
            if (!_hasValue)
            {
                Value = instant;
                _hasValue = true;
                return;
            }

            float alpha = 1f - Mathf.Exp(-deltaTime / _timeConstant);
            Value += (instant - Value) * alpha;
        }
    }
}
