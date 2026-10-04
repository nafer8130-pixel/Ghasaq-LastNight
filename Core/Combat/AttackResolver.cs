using System.Collections.Generic;
using Ghasaq.Core.Numerics;
using Ghasaq.Core.Randomness;
using Ghasaq.Core.Stats;

namespace Ghasaq.Core.Combat
{
    /// <summary>
    /// Turns a landed ability into actual hits: picks targets, rolls damage and
    /// applies effects.
    ///
    /// Separated from <see cref="AbilityController"/> so that "when does the blow
    /// land" and "what does the blow hit" can be tested independently. This class
    /// is stateless and does no allocation on the hot path: the caller supplies a
    /// reusable buffer for the hit list, and nearest-target selection is done by
    /// replacement rather than sorting.
    /// </summary>
    public static class AttackResolver
    {
        /// <summary>
        /// Selects targets for a landed ability and applies damage and effects.
        /// Fills <paramref name="hits"/> with the combatants actually struck, for
        /// presentation. Returns the number of targets hit.
        ///
        /// <paramref name="awareness"/> is the Silence Price's window onto the
        /// enemy's mind. Omitted, no target is treated as unaware, which is the
        /// conservative reading: a test or a tool that does not model awareness
        /// never gets a free execution.
        /// </summary>
        public static int Resolve(
            Combatant attacker,
            AbilityDefinition ability,
            IReadOnlyList<Combatant> candidates,
            DeterministicRng rng,
            List<Combatant> hits,
            IAwarenessProbe awareness = null)
        {
            if (hits == null)
            {
                return 0;
            }

            hits.Clear();

            if (attacker == null || ability == null || !attacker.IsAlive)
            {
                return 0;
            }

            // A dash is pure movement. It still resolves so that the caller has a
            // single entry point, but it damages nothing.
            if (ability.Kind == AbilityKind.Dash)
            {
                MoveAttacker(attacker, ability);
                return 0;
            }

            // Self-stagger is the cost of committing to the ability, so it is
            // paid whether or not the blow connects. Applying it only on a hit
            // would make whiffing a heavy attack free, and would remove the
            // risk that gives heavy attacks their weight.
            //
            // It is also applied once per swing rather than once per target, so
            // a cleave that hits five enemies does not stagger its own caster
            // five times.
            if (ability.SelfStaggerSeconds > 0f)
            {
                ApplySelfStagger(attacker, ability.SelfStaggerSeconds);
            }

            if (ability.Kind == AbilityKind.Self)
            {
                ApplyStatusesToSelf(attacker, ability);
                return 0;
            }

            SelectTargets(attacker, ability, candidates, hits);

            if (hits.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < hits.Count; i++)
            {
                StrikeCore(attacker, ability, hits[i], rng, awareness, out bool killed);

                // The Ash Price: the kill bursts at the corpse. The bite that
                // goes with it is handled inside the strike, which needs only
                // the attacker and the victim; the burst needs the target list,
                // and this is the one place that has it.
                if (killed)
                {
                    ApplyAshBurst(attacker, hits[i], candidates, rng);
                }
            }

            return hits.Count;
        }

        /// <summary>
        /// Fills <paramref name="hits"/> with the hostile combatants the ability
        /// reaches, nearest first, capped at the ability's target limit.
        ///
        /// Selection is by repeated replacement rather than by sorting: the
        /// candidate set is small, and this keeps the method allocation-free,
        /// which matters when a boss cleave runs against twenty enemies on a
        /// mobile CPU.
        /// </summary>
        public static void SelectTargets(
            Combatant attacker,
            AbilityDefinition ability,
            IReadOnlyList<Combatant> candidates,
            List<Combatant> hits)
        {
            hits.Clear();

            if (candidates == null)
            {
                return;
            }

            float range = ability.Range;
            int limit = ability.MaxTargets <= 0 ? int.MaxValue : ability.MaxTargets;
            Float3 forward = attacker.Forward;

            for (int i = 0; i < candidates.Count; i++)
            {
                Combatant candidate = candidates[i];
                if (candidate == null || !candidate.IsAlive || !attacker.IsHostileTo(candidate))
                {
                    continue;
                }

                float distance = Float3.DistanceXZ(attacker.Position, candidate.Position);
                if (distance > range)
                {
                    continue;
                }

                if (!FMath.WithinCone(attacker.Position, forward, candidate.Position, ability.ConeHalfAngleDegrees, range))
                {
                    continue;
                }

                InsertNearest(attacker, hits, candidate, limit);
            }
        }

        /// <summary>
        /// Maintains a nearest-first list of at most <paramref name="limit"/>
        /// entries. A new candidate replaces the current farthest entry, and only
        /// when it is actually nearer.
        /// </summary>
        private static void InsertNearest(Combatant attacker, List<Combatant> hits, Combatant candidate, int limit)
        {
            if (hits.Count < limit)
            {
                hits.Add(candidate);
                return;
            }

            int farthestIndex = -1;
            float farthestDistance = -1f;
            for (int i = 0; i < hits.Count; i++)
            {
                float distance = Float3.DistanceXZ(attacker.Position, hits[i].Position);
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthestIndex = i;
                }
            }

            float candidateDistance = Float3.DistanceXZ(attacker.Position, candidate.Position);
            if (farthestIndex >= 0 && candidateDistance < farthestDistance)
            {
                hits[farthestIndex] = candidate;
            }
        }

        /// <summary>
        /// Resolves and applies one hit against one target.
        ///
        /// The burst part of the Ash Price is deliberately not here: it needs
        /// the world's target list, which a lone strike does not have. Use
        /// <see cref="Resolve"/> for a full swing; its result includes the burst.
        /// </summary>
        public static DamageResult Strike(
            Combatant attacker,
            AbilityDefinition ability,
            Combatant target,
            DeterministicRng rng,
            IAwarenessProbe awareness = null)
        {
            return StrikeCore(attacker, ability, target, rng, awareness, out _);
        }

        /// <summary>The shared body of one hit, with the kill flag the burst needs.</summary>
        private static DamageResult StrikeCore(
            Combatant attacker,
            AbilityDefinition ability,
            Combatant target,
            DeterministicRng rng,
            IAwarenessProbe awareness,
            out bool killed)
        {
            killed = false;

            if (!ability.DealsDamage || target == null || !target.IsAlive)
            {
                return DamageResult.None;
            }

            // The Silence Price: a blow from behind an unaware target is an
            // execution. Below the health threshold it is simply lethal; above
            // it, it merely hits several times harder, which is what makes the
            // Price read as "kill them quietly" rather than "hit them harder".
            bool silent = attacker.Sigil != null
                && attacker.Sigil.Kind == SigilId.Silence
                && IsBehind(target, attacker)
                && awareness != null
                && awareness.IsUnaware(target);

            if (silent && target.Vitals.HealthFraction < SigilTuning.SilenceExecutionHealthFraction)
            {
                // An execution bypasses armour, resistance, crit and shields:
                // the target is already nearly dead, so what is left to decide
                // is only whether the blow was quiet enough to take the rest.
                float lethal = target.Vitals.Health;
                var execution = new DamageResult(lethal, lethal, lethal, false, 0f);

                float executed = target.ReceiveDamage(execution, attacker, out _);
                if (executed > 0f)
                {
                    attacker.Sigil?.NotifyHitLanded(executed);
                }

                killed = !target.IsAlive;
                return execution;
            }

            StatId powerStat = ability.UsesGhasaqPower ? StatId.GhasaqPower : StatId.AttackPower;
            float baseDamage = attacker.Stats.Get(powerStat) * ability.DamageMultiplier;

            if (silent)
            {
                baseDamage *= SigilTuning.SilenceExecutionMultiplier;
            }

            var request = new DamageRequest(
                ability.DamageType,
                baseDamage,
                armorPenetration: 0f,
                critChance: attacker.Stats.Get(StatId.CritChance) + ability.CritChanceBonus,
                critMultiplier: attacker.Stats.Get(StatId.CritMultiplier),
                attackerLevel: attacker.Level,
                variance: ability.Variance);

            DamageResult result = DamageCalculator.Resolve(
                request,
                defenderArmor: target.Stats.Get(StatId.Armor),
                defenderResistance: target.Vitals.Resistance.Get(ability.DamageType),
                attackerDamageMultiplier: attacker.OutgoingDamageMultiplier,
                defenderDamageTakenMultiplier: target.IncomingDamageMultiplier,
                rng: rng);

            if (result.IsZero)
            {
                return result;
            }

            float applied = target.ReceiveDamage(result, attacker, out float overkill);

            if (applied <= 0f)
            {
                // The blow was refused - an invulnerability window. Nothing
                // landed, so nothing follows: no knockback, no statuses, and
                // no Price is charged for a hit that did not happen.
                return result;
            }

            if (ability.KnockbackSpeed > 0f)
            {
                Float3 push = target.Position - attacker.Position;
                target.ApplyKnockback(push, ability.KnockbackSpeed);
            }

            ApplyStatuses(target, ability, rng, attacker);

            // The Hunger Price feeds on any landed hit, and the Ash Price is
            // charged on the overkill of a killing one.
            attacker.Sigil?.NotifyHitLanded(applied);

            if (!target.IsAlive)
            {
                killed = true;
                attacker.Sigil?.NotifyKill(target, overkill);
            }

            return result;
        }

        /// <summary>
        /// Rolls and applies an ability's on-hit statuses. Damage-over-time uses
        /// the school declared by the status kind, so burning always burns with
        /// Ember regardless of the ability's own damage type.
        /// </summary>
        public static void ApplyStatuses(
            Combatant target,
            AbilityDefinition ability,
            DeterministicRng rng,
            Combatant source)
        {
            StatusApplication[] applications = ability.OnHitStatuses;
            if (applications == null || applications.Length == 0)
            {
                return;
            }

            for (int i = 0; i < applications.Length; i++)
            {
                StatusApplication application = applications[i];
                if (application.Chance <= 0f)
                {
                    continue;
                }

                if (application.Chance < 1f && rng != null && !rng.Chance(application.Chance))
                {
                    continue;
                }

                target.Statuses.Apply(BuildStatus(application, ability.DamageType, source));
            }
        }

        /// <summary>Builds the runtime status effect for one application.</summary>
        public static StatusEffect BuildStatus(StatusApplication application, DamageType fallback, object source)
        {
            if (StatusEffectSystem.IsDamageOverTime(application.Kind))
            {
                return StatusEffect.Dot(
                    application.Kind,
                    application.ResolveDamageType(fallback),
                    application.Magnitude,
                    application.Duration,
                    application.TickInterval,
                    source,
                    application.MaxStacks);
            }

            return StatusEffect.Modifier(application.Kind, application.Magnitude, application.Duration, source);
        }

        private static void ApplyStatusesToSelf(Combatant attacker, AbilityDefinition ability)
        {
            StatusApplication[] applications = ability.OnHitStatuses;
            if (applications == null)
            {
                return;
            }

            for (int i = 0; i < applications.Length; i++)
            {
                attacker.Statuses.Apply(BuildStatus(applications[i], ability.DamageType, attacker));
            }
        }

        private static void ApplySelfStagger(Combatant attacker, float seconds)
        {
            attacker.Statuses.Apply(StatusEffect.Modifier(StatusKind.Staggered, 1f, seconds, null));
        }

        /// <summary>
        /// Advances a dashing attacker along its facing direction.
        ///
        /// Bounds are intentionally not clamped here: the core has no notion of
        /// a world yet, and clamping is the responsibility of whatever owns the
        /// space (the simulation, or the Unity layer against real colliders).
        /// A dash that would leave the arena is stopped there, not here.
        /// </summary>
        private static void MoveAttacker(Combatant attacker, AbilityDefinition ability)
        {
            if (ability.DashDistance <= 0f)
            {
                return;
            }

            Float3 destination = attacker.Position + (attacker.Forward * ability.DashDistance);
            attacker.SetPosition(destination);

            // A dash that actually moved is the Lantern's blink, and its Price
            // is charged on arrival: every hostile within range acquires the
            // bearer, even around cover.
            attacker.Sigil?.NotifyDashLanded();
        }

        /// <summary>
        /// The burst half of the Ash Price: everything hostile around the corpse
        /// takes a share of the bearer's power.
        ///
        /// Capped at the world's candidate list, which the caller supplies. The
        /// burst itself deals plain damage - it cannot crit, cannot apply on-hit
        /// effects, and, deliberately, cannot burst again: a chain reaction
        /// would make one swing a whole encounter, and the Price is a burst, not
        /// a plague.
        /// </summary>
        private static void ApplyAshBurst(
            Combatant attacker,
            Combatant victim,
            IReadOnlyList<Combatant> candidates,
            DeterministicRng rng)
        {
            SigilLoadout sigil = attacker.Sigil;

            if (sigil == null || sigil.Kind != SigilId.Ash || candidates == null)
            {
                return;
            }

            float damage = attacker.Stats.Get(StatId.AttackPower) * SigilTuning.AshBurstDamageFraction;

            ApplyRadialBurst(
                attacker,
                victim.Position,
                SigilTuning.AshBurstRadius,
                damage,
                DamageType.Ghasaq,
                candidates,
                rng,
                hits: null);
        }

        /// <summary>
        /// Hits every hostile combatant within <paramref name="radius"/> of a
        /// point, ignoring facing. Fills <paramref name="hits"/> when one is
        /// supplied. Returns how many were struck.
        ///
        /// The share of damage is resolved like any other blow - armour and
        /// resistance still matter - but it never crits and never triggers
        /// on-hit effects. That keeps a burst a burst, not a second ability.
        /// </summary>
        public static int ApplyRadialBurst(
            Combatant source,
            Float3 center,
            float radius,
            float baseDamage,
            DamageType damageType,
            IReadOnlyList<Combatant> candidates,
            DeterministicRng rng,
            List<Combatant> hits)
        {
            hits?.Clear();

            if (source == null || candidates == null || baseDamage <= 0f || radius <= 0f)
            {
                return 0;
            }

            int struck = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                Combatant candidate = candidates[i];

                if (candidate == null || !candidate.IsAlive || !source.IsHostileTo(candidate))
                {
                    continue;
                }

                if (Float3.DistanceXZ(center, candidate.Position) > radius)
                {
                    continue;
                }

                var request = new DamageRequest(
                    damageType,
                    baseDamage,
                    armorPenetration: 0f,
                    critChance: 0f,
                    critMultiplier: 1f,
                    attackerLevel: source.Level,
                    variance: 0f);

                DamageResult result = DamageCalculator.Resolve(
                    request,
                    defenderArmor: candidate.Stats.Get(StatId.Armor),
                    defenderResistance: candidate.Vitals.Resistance.Get(damageType),
                    attackerDamageMultiplier: source.OutgoingDamageMultiplier,
                    defenderDamageTakenMultiplier: candidate.IncomingDamageMultiplier,
                    rng: rng);

                if (result.IsZero)
                {
                    continue;
                }

                candidate.ReceiveDamage(result, source);
                struck++;
                hits?.Add(candidate);
            }

            return struck;
        }

        /// <summary>
        /// True when the attacker stands in the target's rear half-plane. This
        /// is the "from behind" half of the Silence Price; the other half is the
        /// target being unaware, which only the target's brain can answer.
        /// </summary>
        public static bool IsBehind(Combatant target, Combatant attacker)
        {
            if (target == null || attacker == null)
            {
                return false;
            }

            Float3 toAttacker = (attacker.Position - target.Position).FlattenedXZ;

            if (toAttacker == Float3.Zero)
            {
                return false;
            }

            return Float3.Dot(target.Forward, toAttacker.Normalized) < 0f;
        }
    }
}
