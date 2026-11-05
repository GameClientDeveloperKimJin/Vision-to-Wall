using System;
using UnityEngine;

namespace VisionToWall.Sensor
{
    /// <summary>
    /// FR-02 GPU downsampling. Webcam -> preview RT (mirror/flip applied) -> mid RT -> grid RT.
    /// The preview RT doubles as the debug source view; the extra stages reduce aliasing.
    /// All render textures are allocated once in the constructor.
    /// </summary>
    public sealed class GridDownsampler : IDisposable
    {
        /// <summary>Intermediate size between preview and grid (anti-aliasing stage).</summary>
        public const int MidWidth = 128;
        public const int MidHeight = 72;

        private static readonly Vector2 ScaleIdentity = new Vector2(1f, 1f);
        private static readonly Vector2 OffsetZero = new Vector2(0f, 0f);

        private RenderTexture _preview;
        private RenderTexture _mid;
        private RenderTexture _grid;

        /// <summary>Webcam image with mirror applied (debug source view).</summary>
        public RenderTexture Preview => _preview;

        /// <summary>Grid-resolution result, read back by AsyncGPUReadback.</summary>
        public RenderTexture Grid => _grid;

        public int GridWidth { get; }
        public int GridHeight { get; }

        public GridDownsampler(int previewWidth, int previewHeight, int gridWidth, int gridHeight)
        {
            GridWidth = gridWidth;
            GridHeight = gridHeight;

            _preview = Create("Sensor.Preview", previewWidth, previewHeight, FilterMode.Bilinear);
            _mid = Create("Sensor.Mid",
                Mathf.Clamp(MidWidth, gridWidth, Mathf.Max(gridWidth, previewWidth)),
                Mathf.Clamp(MidHeight, gridHeight, Mathf.Max(gridHeight, previewHeight)),
                FilterMode.Bilinear);
            _grid = Create("Sensor.Grid", gridWidth, gridHeight, FilterMode.Point);
        }

        /// <summary>Runs the blit chain. Mirror flips horizontally; flipVertical compensates videoVerticallyMirrored.</summary>
        public void Blit(Texture source, bool mirror, bool flipVertical)
        {
            if (source == null || _preview == null) return;

            var scale = new Vector2(mirror ? -1f : 1f, flipVertical ? -1f : 1f);
            var offset = new Vector2(mirror ? 1f : 0f, flipVertical ? 1f : 0f);

            Graphics.Blit(source, _preview, scale, offset);
            Graphics.Blit(_preview, _mid, ScaleIdentity, OffsetZero);
            Graphics.Blit(_mid, _grid, ScaleIdentity, OffsetZero);
        }

        public void Dispose()
        {
            Release(ref _preview);
            Release(ref _mid);
            Release(ref _grid);
        }

        private static RenderTexture Create(string name, int width, int height, FilterMode filter)
        {
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = name,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt.Create();
            return rt;
        }

        private static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(rt);
            else UnityEngine.Object.DestroyImmediate(rt);
            rt = null;
        }
    }
}
