namespace VisionToWall.Core
{
    public enum ExhibitionState
    {
        /// <summary>Attract mode: no visitor motion for idleTimeoutSec.</summary>
        Idle,
        /// <summary>Interactive mode: motion received.</summary>
        Active
    }
}
