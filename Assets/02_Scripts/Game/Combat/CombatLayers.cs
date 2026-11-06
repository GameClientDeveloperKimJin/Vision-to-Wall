using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>Layer of everything the overlay camera draws. A built-in layer, so project settings stay untouched.</summary>
    public static class CombatLayers
    {
        public const int Overlay = 1; // TransparentFX

        /// <summary>One-time setup only (it walks the hierarchy). Replaced models keep working on any layer.</summary>
        public static void SetRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetRecursively(root.GetChild(i), layer);
        }
    }
}
