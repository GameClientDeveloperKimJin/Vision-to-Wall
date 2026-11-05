namespace VisionToWall.Core
{
    /// <summary>One detected motion cell. Origin is bottom-left; mirror already applied.</summary>
    [System.Serializable]
    public struct MotionPoint
    {
        /// <summary>Normalized horizontal position, 0 (left) to 1 (right).</summary>
        public float x;
        /// <summary>Normalized vertical position, 0 (bottom) to 1 (top).</summary>
        public float y;
        /// <summary>Motion intensity, 0 to 1.</summary>
        public float v;

        public MotionPoint(float x, float y, float v)
        {
            this.x = x;
            this.y = y;
            this.v = v;
        }
    }
}
