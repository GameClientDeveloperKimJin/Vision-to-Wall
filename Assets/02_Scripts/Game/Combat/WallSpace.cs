using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Converts normalized screen coordinates (0~1, origin bottom left) to world points seen by the overlay camera
    /// at a fixed distance, and back. Viewport based, so a point lands on its normalized position at any depth.
    /// </summary>
    public static class WallSpace
    {
        public static Vector3 ToWorld(Camera camera, Vector2 normalized, float distance)
        {
            return camera.ViewportToWorldPoint(new Vector3(normalized.x, normalized.y, distance));
        }

        public static Vector2 ToNormalized(Camera camera, Vector3 world)
        {
            Vector3 viewport = camera.WorldToViewportPoint(world);
            return new Vector2(viewport.x, viewport.y);
        }

        /// <summary>World length of one screen height at <paramref name="distance"/> from a perspective camera.</summary>
        public static float ScreenHeightAt(Camera camera, float distance)
        {
            return 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }
    }
}
