using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// Panel alpha per game phase (user decision 2026-11-08): MediaWall shown (fade in), Awaken fades out over
    /// <c>awakenTransitionSec</c>, Combat and Result hidden.
    /// </summary>
    public static class MiniCharacterFade
    {
        public static float InitialAlpha(GamePhase phase) => phase == GamePhase.MediaWall ? 1f : 0f;

        public static float Step(GamePhase phase, float alpha, float dt, float awakenSec, float fadeInSec)
        {
            switch (phase)
            {
                case GamePhase.MediaWall:
                    return Mathf.MoveTowards(alpha, 1f, fadeInSec <= 0f ? 1f : dt / fadeInSec);
                case GamePhase.Awaken:
                    return Mathf.MoveTowards(alpha, 0f, awakenSec <= 0f ? 1f : dt / awakenSec);
                default:
                    return 0f;
            }
        }
    }
}
