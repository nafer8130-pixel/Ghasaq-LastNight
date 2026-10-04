using Ghasaq.Core.Numerics;

namespace Ghasaq.Core.Combat
{
    /// <summary>
    /// السُّخام / Soot: what the Ghasaq leaves behind in the body when it is
    /// used, and العَتْمة / Dimming, the state enough of it brings on.
    ///
    /// The numbers are the draft values of Documentation/Soot.md, fixed by feel
    /// in the slice like the Sigils' are. They carry no formula: each one exists
    /// to make the risk readable - how fast the meter climbs, how fast it
    /// fades, and what the two edges of the Dimming are worth. Changing a
    /// number means changing it here, where the tests notice.
    ///
    /// Deliberately absent: any self-damage, any death, any corruption bar. The
    /// plan is explicit that the Dimming shifts risk - stronger and frailer at
    /// once - and never takes the character away from the player (section 3.6).
    /// </summary>
    public static class SootTuning
    {
        /// <summary>Full meter. Dimming is strongest here.</summary>
        public const float Max = 100f;

        /// <summary>Soot added for committing to a Ghasaq-powered ability.</summary>
        public const float PerGhasaqUse = 20f;

        /// <summary>Soot that drifts off per second when the Ghasaq is put down.</summary>
        public const float DecayPerSecond = 2f;

        /// <summary>Where the Dimming begins. Below it, the meter is only a meter.</summary>
        public const float DimmingStart = 60f;

        /// <summary>Bonus damage dealt at a full meter, as a fraction.</summary>
        public const float DimmingDamageDealtBonus = 0.25f;

        /// <summary>Bonus damage taken at a full meter, as a fraction. The Price side.</summary>
        public const float DimmingDamageTakenBonus = 0.35f;
    }

    /// <summary>
    /// One combatant's السُّخام / Soot meter.
    ///
    /// It rises when the bearer commits to an ability that draws on the Ghasaq,
    /// drifts down when the Ghasaq is left alone, and past
    /// <see cref="SootTuning.DimmingStart"/> it begins to dim: the bearer deals
    /// more and takes more, both scaling with how deep the meter is. The
    /// meter itself never damages anyone - the only thing it changes is how a
    /// fight prices its blows.
    ///
    /// Only the player's run carries one (the session hands it over), the same
    /// rule the Sigil follows; for everyone else every hook is one null check
    /// away from neutral. It is a session state, not a saved one: a run that
    /// ends leaves the soot behind, and a revival starts clean.
    /// </summary>
    public sealed class SootMeter
    {
        private float _soot;

        /// <summary>Current soot, 0..<see cref="SootTuning.Max"/>.</summary>
        public float Soot
        {
            get { return _soot; }
        }

        /// <summary>The meter as a fraction of full, 0..1.</summary>
        public float Fraction
        {
            get
            {
                return SootTuning.Max <= 0f
                    ? 0f
                    : FMath.Clamp(_soot / SootTuning.Max, 0f, 1f);
            }
        }

        /// <summary>True while the meter is deep enough to dim.</summary>
        public bool IsDimming
        {
            get { return _soot >= SootTuning.DimmingStart; }
        }

        /// <summary>
        /// How deep into the Dimming the meter is, 0 at the threshold and 1 at
        /// full. The bonuses scale with this, so crossing the line starts the
        /// state gently and the last stretch is the dangerous one.
        /// </summary>
        public float DimmingFraction
        {
            get
            {
                float span = SootTuning.Max - SootTuning.DimmingStart;

                if (span <= 0f)
                {
                    return IsDimming ? 1f : 0f;
                }

                return FMath.Clamp((_soot - SootTuning.DimmingStart) / span, 0f, 1f);
            }
        }

        /// <summary>Damage the bearer deals, 1 when clear and up to 1 + the draft bonus at full.</summary>
        public float DamageDealtMultiplier
        {
            get { return 1f + (SootTuning.DimmingDamageDealtBonus * DimmingFraction); }
        }

        /// <summary>Damage the bearer takes, 1 when clear and up to 1 + the draft bonus at full.</summary>
        public float DamageTakenMultiplier
        {
            get { return 1f + (SootTuning.DimmingDamageTakenBonus * DimmingFraction); }
        }

        /// <summary>
        /// A Ghasaq-powered ability was committed to. The meter rises at
        /// commitment, interrupted or not: the risk begins with the decision,
        /// not with the outcome.
        /// </summary>
        public void NotifyGhasaqUsed()
        {
            _soot = FMath.Clamp(_soot + SootTuning.PerGhasaqUse, 0f, SootTuning.Max);
        }

        /// <summary>The Ghasaq was left alone this step, so the soot drifts off.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || _soot <= 0f)
            {
                return;
            }

            _soot = FMath.Clamp(_soot - (SootTuning.DecayPerSecond * deltaTime), 0f, SootTuning.Max);
        }

        /// <summary>Empties the meter. Used by a revival, which starts the fight clean.</summary>
        public void Reset()
        {
            _soot = 0f;
        }
    }
}
