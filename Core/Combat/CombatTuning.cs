namespace Ghasaq.Core.Combat
{
    /// <summary>
    /// The slice's combat budgets, in one place the code and the tests agree on.
    ///
    /// These numbers come from the production plan (section 7) and are fixed by
    /// feel, not by formula: an enemy's lethal move must be readable for at least
    /// 400 ms, and a landed hit gets a short stop for weight. The content
    /// validator enforces the telegraph floor on every authored enemy ability, so
    /// a move that cannot be read fails the build instead of quietly shipping.
    /// The view layer reads the hit-stop band when it animates a hit.
    /// </summary>
    public static class CombatTuning
    {
        /// <summary>
        /// Minimum wind-up for an enemy's damaging move, in seconds — the plan's
        /// "400 ms telegraph" floor.
        /// </summary>
        public const float TelegraphMinSeconds = 0.4f;

        /// <summary>Shortest stop the view may apply on a landed hit, in milliseconds.</summary>
        public const int HitStopMinMilliseconds = 40;

        /// <summary>Longest stop the view may apply on a landed hit, in milliseconds.</summary>
        public const int HitStopMaxMilliseconds = 80;

        /// <summary>
        /// Hostiles the slice is designed to keep readable on screen at once.
        /// Above this, the plan says use LOD or reduce.
        /// </summary>
        public const int TargetHostilesOnScreen = 3;
    }
}
