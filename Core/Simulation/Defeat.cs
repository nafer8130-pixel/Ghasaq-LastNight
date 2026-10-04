namespace Ghasaq.Core.Simulation
{
    /// <summary>
    /// Why a fallen Sigilbearer could not rise, for the message the player sees.
    ///
    /// Falling is not a loss state here. The world's premise is that Sigilbearers
    /// go out and come back with less, and the plan's loop is built on retrying
    /// the same region rather than on ending the run (Soot.md already relies on
    /// it: a revival washes the Soot meter off, "so a retry begins at the
    /// meter's zero"). This enum is only about the one way the rise can be
    /// refused.
    /// </summary>
    public enum DefeatFailure
    {
        None = 0,

        /// <summary>The bearer is still standing, so there is nothing to rise from.</summary>
        NotFallen = 1
    }
}
