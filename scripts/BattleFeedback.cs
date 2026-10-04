using Godot;
using Ghasaq.Core.Combat;

namespace Ghasaq.Game
{
    /// <summary>
    /// The rules behind the hit feedback: where a blow falls inside the plan's
    /// hit-stop band, how hard the camera kicks, and how an enemy's wind-up
    /// ramps toward its warning colour.
    ///
    /// These are engine-layer rules, but they are arithmetic rather than
    /// drawing, so the numbers can be asserted by the smoke test the way the
    /// core's are asserted by the unit suite. The budgets themselves - 400 ms
    /// of telegraph, 40-80 ms of hit-stop - stay in <see cref="CombatTuning"/>,
    /// where the plan put them; this class only decides placement inside them.
    /// </summary>
    public static class BattleFeedback
    {
        /// <summary>How much of a victim's health a blow must take to earn the full stop.</summary>
        public const float HeavyBlowFraction = 0.15f;

        /// <summary>A critical blow never stops less than this much of the full weight.</summary>
        public const float CriticalWeightFloor = 0.6f;

        /// <summary>
        /// The hit-stop a landed blow earns, inside the plan's band: a chip gets
        /// the minimum, a blow worth 15% of the victim's health (or a crit) gets
        /// the maximum, and everything scales between.
        /// </summary>
        public static float HitStopSeconds(float applied, float victimMaxHealth, bool critical)
        {
            float min = CombatTuning.HitStopMinMilliseconds / 1000f;
            float max = CombatTuning.HitStopMaxMilliseconds / 1000f;

            float fraction = victimMaxHealth > 0f ? applied / victimMaxHealth : 1f;
            float weight = Mathf.Clamp(fraction / HeavyBlowFraction, 0f, 1f);

            if (critical)
            {
                weight = Mathf.Max(weight, CriticalWeightFloor);
            }

            return min + ((max - min) * weight);
        }

        /// <summary>
        /// How hard the camera kicks for a landed blow: hardest when the player
        /// is the one hit (that is the hit they must not miss), heavier for a
        /// boss taking one, light otherwise.
        /// </summary>
        public static float ShakeFor(bool victimIsPlayer, bool victimIsBoss)
        {
            if (victimIsPlayer)
            {
                return 0.32f;
            }

            return victimIsBoss ? 0.5f : 0.12f;
        }

        /// <summary>
        /// How far through its wind-up a caster is: 0 when the blow has just
        /// begun (or nothing is being wound up), 1 at the moment it lands. The
        /// view blends its body toward the warning colour by this, so the
        /// telegraph the plan requires is read rather than merely enforced.
        /// </summary>
        public static float TelegraphStrength(CastPhase phase, float windupSeconds, float phaseRemaining)
        {
            if (phase != CastPhase.Windup || windupSeconds <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp(1f - (phaseRemaining / windupSeconds), 0f, 1f);
        }
    }
}
