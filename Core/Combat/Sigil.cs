namespace Ghasaq.Core.Combat
{
    /// <summary>
    /// The five الوَسْم / Sigils of the slice, plus "none carried".
    ///
    /// Values are stable and index nothing; they are saved by name. Append new
    /// members at the end.
    /// </summary>
    public enum SigilId
    {
        /// <summary>No Sigil equipped. The base kit, no Price.</summary>
        None = 0,

        /// <summary>The blink-step, whose light marks the bearer.</summary>
        Lantern = 1,

        /// <summary>Kills burst, and the overkill bites back.</summary>
        Ash = 2,

        /// <summary>Executions from behind, paid for with the loudest ability.</summary>
        Silence = 3,

        /// <summary>Every hit feeds, and silence from the fight starves.</summary>
        Hunger = 4,

        /// <summary>A shield that shatters and leaves the bearer exposed.</summary>
        Glass = 5
    }

    /// <summary>Why equipping a Sigil was refused. The HUD tells the player which.</summary>
    public enum SigilEquipFailure
    {
        None = 0,

        /// <summary>No Sigil with that id is registered.</summary>
        UnknownSigil = 1,

        /// <summary>A Sigil is chosen at the Hearth, never mid-fight (plan section 3.1).</summary>
        InCombat = 2,

        /// <summary>
        /// The Hearth stands in a camp (plan section 3.7), and a Sigil is taken
        /// up there. Standing in a camp is the rule; the Hearth station itself is
        /// still a scene to build.
        /// </summary>
        NotAtHearth = 3
    }

    /// <summary>
    /// One الوَسْم / Sigil: the combat style a run carries, and the الثمن /
    /// Price it charges for carrying it.
    ///
    /// Authored content, not state: the same definition is shared by every run
    /// that carries it. The behaviours themselves are keyed on
    /// <see cref="Kind"/> in <see cref="SigilLoadout"/> and in the combat hooks,
    /// deliberately as explicit rules rather than a data-driven effect list: a
    /// Price that is data can be authored into silence, and the plan says every
    /// Price must be shown and felt (section 3.2).
    /// </summary>
    public sealed class SigilDefinition
    {
        /// <summary>Stable content id, e.g. "lantern". Saved and matched by id.</summary>
        public string Id = "";

        /// <summary>Which behaviour this Sigil turns on.</summary>
        public SigilId Kind = SigilId.None;

        /// <summary>Arabic-first label, which is how the player meets it.</summary>
        public string DisplayName = "";

        /// <summary>English name, for code and tooling.</summary>
        public string EnglishName = "";

        /// <summary>The new verb in one line, written to be readable on the HUD.</summary>
        public string VerbLine = "";

        /// <summary>
        /// The Price in one line, written to be shown on the HUD at all times.
        /// A Sigil without a price line fails content validation: a cost the
        /// player never sees is not a Price.
        /// </summary>
        public string PriceLine = "";

        public override string ToString()
        {
            return string.IsNullOrEmpty(EnglishName) ? Id : EnglishName;
        }
    }

    /// <summary>
    /// Answers whether a combatant is currently unaware of being hunted.
    ///
    /// The Silence Price's execution only lands on a target that has not noticed
    /// the fight, and that fact lives in the enemy's brain, not in its body.
    /// Kept behind an interface so attack resolution stays independent of the AI
    /// layer, and so tests can state awareness directly instead of arranging a
    /// whole perception scene.
    /// </summary>
    public interface IAwarenessProbe
    {
        bool IsUnaware(Combatant target);
    }

    /// <summary>
    /// The five Sigils' numbers, in one place the code and the tests agree on.
    ///
    /// These are the draft values of Documentation/Sigils.md, fixed by feel in
    /// the slice. They carry no formula: each one exists to make a verb or a
    /// Price readable, and the plan moves them to Remote Config in a later
    /// phase. Until then, changing a Price means changing it here, where the
    /// tests will notice.
    /// </summary>
    public static class SigilTuning
    {
        // ------------------------------- Lantern ---------------------------------

        /// <summary>Distance of the blink-step, in metres. Matches the kit's dash reach.</summary>
        public const float LanternBlinkDistance = 5.5f;

        /// <summary>Length of the i-frame window the blink opens.</summary>
        public const float LanternInvulnerabilitySeconds = 0.2f;

        /// <summary>Every hostile inside this radius acquires the bearer after each blink.</summary>
        public const float LanternPriceAcquireRadius = 15f;

        /// <summary>How long the acquisition lasts. It ignores cover for the whole window.</summary>
        public const float LanternPriceAcquireSeconds = 2f;

        // --------------------------------- Ash -----------------------------------

        /// <summary>Radius of the burst around a corpse.</summary>
        public const float AshBurstRadius = 2.5f;

        /// <summary>Burst damage as a fraction of the bearer's AttackPower.</summary>
        public const float AshBurstDamageFraction = 0.6f;

        /// <summary>Fraction of the overkill that bites back into the bearer.</summary>
        public const float AshOverkillBiteFraction = 0.25f;

        // ------------------------------- Silence ----------------------------------

        /// <summary>Damage multiplier on a blow from behind an unaware target.</summary>
        public const float SilenceExecutionMultiplier = 2.5f;

        /// <summary>Below this health fraction, a silent blow executes outright.</summary>
        public const float SilenceExecutionHealthFraction = 0.2f;

        // -------------------------------- Hunger ----------------------------------

        /// <summary>Fraction of a landed hit's damage that heals the bearer.</summary>
        public const float HungerHealFraction = 0.08f;

        /// <summary>Seconds without landing a hit before the famine starts.</summary>
        public const float HungerFamineSeconds = 8f;

        /// <summary>Famine damage per second, as a fraction of the bearer's maximum health.</summary>
        public const float HungerFamineHealthFractionPerSecond = 0.02f;

        /// <summary>Seconds between famine damage ticks.</summary>
        public const float HungerFamineTickSeconds = 1f;

        /// <summary>
        /// How long one famine application lasts. The loadout keeps it alive by
        /// re-applying while the famine runs, and removes it the moment a hit
        /// lands, so this is only a ceiling for the applied status.
        /// </summary>
        public const float HungerFamineStatusSeconds = 5f;

        // --------------------------------- Glass ----------------------------------

        /// <summary>Shield capacity as a fraction of the bearer's maximum health.</summary>
        public const float GlassShieldHealthFraction = 0.35f;

        /// <summary>Fraction of each incoming blow the shield takes instead of health.</summary>
        public const float GlassShieldAbsorbFraction = 0.5f;

        /// <summary>Radius of the shatter burst when the shield breaks.</summary>
        public const float GlassShatterRadius = 2.5f;

        /// <summary>Shatter damage as a fraction of the bearer's AttackPower.</summary>
        public const float GlassShatterDamageFraction = 0.75f;

        /// <summary>How long the bearer stays exposed after a shatter.</summary>
        public const float GlassExposedSeconds = 2f;

        /// <summary>Extra fraction of damage taken while exposed, in the Marked shape.</summary>
        public const float GlassExposedDamageTakenBonus = 0.25f;

        /// <summary>Seconds before a broken shield reforms. The verb must come back within a fight.</summary>
        public const float GlassReformSeconds = 12f;
    }
}
