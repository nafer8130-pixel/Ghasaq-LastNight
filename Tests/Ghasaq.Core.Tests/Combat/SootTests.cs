using System.Collections.Generic;
using Ghasaq.Core.Combat;
using Ghasaq.Core.Numerics;
using Ghasaq.Core.Randomness;
using Ghasaq.Core.Tests.Support;
using Xunit;

namespace Ghasaq.Core.Tests.Combat
{
    /// <summary>
    /// Tests السُّخام / Soot and العَتْمة / Dimming (plan section 3.6): the
    /// meter a bearer of the Ghasaq fills by committing to it, the state it
    /// brings on past its threshold, and the two edges that state sharpens.
    ///
    /// Two couplings make the mechanic real, and neither can be tested on the
    /// meter alone: <see cref="AbilityController"/> has to charge the meter on
    /// commitment, and <see cref="AttackResolver"/> has to price blows with
    /// it. Both are pinned here, so a later refactor cannot quietly disconnect
    /// the meter from the fight. What the plan forbids is pinned too: the
    /// meter is never lethal on its own, and a refused activation leaves no
    /// soot, because nothing was burned.
    /// </summary>
    public class SootTests
    {
        private static Combatant Bearer(string id = "bearer", float ghasaqPower = 0f)
        {
            Combatant bearer = CombatantFactory.Create(
                id,
                Faction.Player,
                ghasaqPower: ghasaqPower);

            bearer.Soot = new SootMeter();
            return bearer;
        }

        /// <summary>The same ability, drawing on the Ghasaq instead of the arm.</summary>
        private static AbilityDefinition GhasaqAbility()
        {
            AbilityDefinition ability = CombatantFactory.Strike();
            ability.UsesGhasaqPower = true;
            return ability;
        }

        /// <summary>Fills a meter the only way it fills in play, one burn at a time.</summary>
        private static SootMeter Full()
        {
            var meter = new SootMeter();

            while (meter.Soot < SootTuning.Max)
            {
                meter.NotifyGhasaqUsed();
            }

            return meter;
        }

        // ------------------------------- the meter -------------------------------

        [Fact]
        public void AMeterStartsCleanAndNeutral()
        {
            var meter = new SootMeter();

            Assert.Equal(0f, meter.Soot, 3);
            Assert.Equal(0f, meter.Fraction, 3);
            Assert.False(meter.IsDimming);
            Assert.Equal(0f, meter.DimmingFraction, 3);
            Assert.Equal(1f, meter.DamageDealtMultiplier, 3);
            Assert.Equal(1f, meter.DamageTakenMultiplier, 3);
        }

        [Fact]
        public void EachCommitmentToTheGhasaqAddsItsTuningAmount()
        {
            var meter = new SootMeter();

            meter.NotifyGhasaqUsed();
            Assert.Equal(SootTuning.PerGhasaqUse, meter.Soot, 3);
            Assert.Equal(SootTuning.PerGhasaqUse / SootTuning.Max, meter.Fraction, 3);

            meter.NotifyGhasaqUsed();
            Assert.Equal(SootTuning.PerGhasaqUse * 2f, meter.Soot, 3);
        }

        [Fact]
        public void TheMeterIsCappedAtFullHoweverOftenTheGhasaqIsBurned()
        {
            var meter = new SootMeter();

            for (int i = 0; i < 20; i++)
            {
                meter.NotifyGhasaqUsed();
            }

            Assert.Equal(SootTuning.Max, meter.Soot, 3);
            Assert.Equal(1f, meter.Fraction, 3);
        }

        [Fact]
        public void TheSootFadesWhenTheGhasaqIsPutDown()
        {
            var meter = Full();

            meter.Tick(10f);
            Assert.Equal(SootTuning.Max - (SootTuning.DecayPerSecond * 10f), meter.Soot, 3);

            meter.Tick(10000f);
            Assert.Equal(0f, meter.Soot, 3);
            Assert.False(meter.IsDimming);
        }

        // ----------------------------- the Dimming -------------------------------

        [Fact]
        public void TheDimmingBeginsAtItsThresholdAndNotBefore()
        {
            var meter = new SootMeter();

            meter.NotifyGhasaqUsed();
            meter.NotifyGhasaqUsed();
            Assert.Equal(2f * SootTuning.PerGhasaqUse, meter.Soot, 3);
            Assert.False(meter.IsDimming, "two burns do not reach the threshold");
            Assert.Equal(1f, meter.DamageDealtMultiplier, 3);

            // The third burn lands exactly on the threshold: the state begins,
            // at zero depth, so stepping over the line costs nothing yet.
            meter.NotifyGhasaqUsed();
            Assert.Equal(SootTuning.DimmingStart, meter.Soot, 3);
            Assert.True(meter.IsDimming);
            Assert.Equal(0f, meter.DimmingFraction, 3);
            Assert.Equal(1f, meter.DamageDealtMultiplier, 3);
            Assert.Equal(1f, meter.DamageTakenMultiplier, 3);
        }

        [Fact]
        public void TheDimmingScalesBothEdgesWithHowDeepTheMeterIs()
        {
            var meter = new SootMeter();

            for (int i = 0; i < 4; i++)
            {
                meter.NotifyGhasaqUsed();
            }

            // Halfway up: halfway through both bonuses.
            Assert.Equal(0.5f, meter.DimmingFraction, 3);
            Assert.Equal(1f + (SootTuning.DimmingDamageDealtBonus * 0.5f), meter.DamageDealtMultiplier, 3);
            Assert.Equal(1f + (SootTuning.DimmingDamageTakenBonus * 0.5f), meter.DamageTakenMultiplier, 3);

            meter.NotifyGhasaqUsed();
            Assert.Equal(1f, meter.DimmingFraction, 3);
            Assert.Equal(1f + SootTuning.DimmingDamageDealtBonus, meter.DamageDealtMultiplier, 3);
            Assert.Equal(1f + SootTuning.DimmingDamageTakenBonus, meter.DamageTakenMultiplier, 3);
        }

        [Fact]
        public void TheFadeCarriesTheDimmingOut()
        {
            var meter = new SootMeter();

            for (int i = 0; i < 3; i++)
            {
                meter.NotifyGhasaqUsed();
            }

            Assert.True(meter.IsDimming);

            meter.Tick(1f);

            Assert.False(meter.IsDimming, "one second of rest is enough to fall back below the line");
            Assert.Equal(1f, meter.DamageDealtMultiplier, 3);
        }

        // -------------------------- the body it hangs on --------------------------

        [Fact]
        public void ACombatantWithoutAMeterIsNeutral()
        {
            Combatant plain = CombatantFactory.Create("plain", Faction.Hostile);

            Assert.Null(plain.Soot);
            Assert.Equal(1f, plain.OutgoingDamageMultiplier, 3);
            Assert.Equal(1f, plain.IncomingDamageMultiplier, 3);
        }

        [Fact]
        public void TheMeterShowsThroughTheCombatantsMultipliers()
        {
            Combatant bearer = Bearer("bearer");
            bearer.Soot = Full();

            Assert.Equal(bearer.Soot.DamageDealtMultiplier, bearer.OutgoingDamageMultiplier, 3);
            Assert.Equal(bearer.Soot.DamageTakenMultiplier, bearer.IncomingDamageMultiplier, 3);
        }

        [Fact]
        public void TheBearersOwnTickAdvancesTheFade()
        {
            Combatant bearer = Bearer("bearer");
            bearer.Soot = Full();

            bearer.Tick(10f);

            Assert.Equal(SootTuning.Max - (SootTuning.DecayPerSecond * 10f), bearer.Soot.Soot, 3);
        }

        [Fact]
        public void RevivingWashesTheSootOff()
        {
            Combatant bearer = Bearer("bearer");
            bearer.Soot = Full();

            Assert.Equal(SootTuning.Max, bearer.Soot.Soot, 3);

            bearer.Revive(Float3.Zero, 0f);

            Assert.Equal(0f, bearer.Soot.Soot, 3);
            Assert.False(bearer.Soot.IsDimming);
        }

        // ----------------------------- the couplings ------------------------------

        [Fact]
        public void CommittingToAGhasaqAbilityLeavesSoot()
        {
            Combatant bearer = Bearer("caster");
            var controller = new AbilityController(bearer, new List<AbilityDefinition> { GhasaqAbility() });

            bool activated = controller.TryActivate(0, out AbilityFailure failure);

            Assert.True(activated);
            Assert.Equal(AbilityFailure.None, failure);
            Assert.Equal(SootTuning.PerGhasaqUse, bearer.Soot.Soot, 3);
        }

        [Fact]
        public void AnOrdinaryAbilityLeavesNone()
        {
            Combatant bearer = Bearer("caster");
            var controller = new AbilityController(bearer, new List<AbilityDefinition> { CombatantFactory.Strike() });

            Assert.True(controller.TryActivate(0, out _));
            Assert.Equal(0f, bearer.Soot.Soot, 3);
        }

        [Fact]
        public void ARefusedCommitmentLeavesNone()
        {
            Combatant bearer = Bearer("caster");
            AbilityDefinition ability = GhasaqAbility();
            ability.StaminaCost = 500f;
            var controller = new AbilityController(bearer, new List<AbilityDefinition> { ability });

            bool activated = controller.TryActivate(0, out AbilityFailure failure);

            Assert.False(activated);
            Assert.Equal(AbilityFailure.NotEnoughStamina, failure);
            Assert.Equal(0f, bearer.Soot.Soot, 3);
        }

        [Fact]
        public void TheDimmingPricesBothEdgesOfAResolvedBlow()
        {
            AbilityDefinition ability = GhasaqAbility();
            var forward = new Float3(0f, 0f, 1f);

            // A blow from a clear bearer against a clear target is the baseline
            // both edges are measured against.
            Combatant clearAttacker = Bearer("clear-attacker", ghasaqPower: 100f);
            clearAttacker.FaceImmediately(forward);
            Combatant clearTarget = CombatantFactory.Create("clear-target", Faction.Hostile, maxHealth: 5000f, position: forward);

            float plain = ResolveOnce(clearAttacker, ability, clearTarget);

            // The same blow from a dimmed bearer is heavier...
            Combatant dimmedAttacker = Bearer("dimmed-attacker", ghasaqPower: 100f);
            dimmedAttacker.Soot = Full();
            dimmedAttacker.FaceImmediately(forward);
            Combatant toldTarget = CombatantFactory.Create("heavier-target", Faction.Hostile, maxHealth: 5000f, position: forward);

            float heavier = ResolveOnce(dimmedAttacker, ability, toldTarget);

            Assert.True(plain > 0f, "the baseline blow landed");
            Assert.Equal(plain * (1f + SootTuning.DimmingDamageDealtBonus), heavier, 1);

            // ...and the same blow against a dimmed target lands harder still.
            Combatant plainAttacker = Bearer("plain-attacker", ghasaqPower: 100f);
            plainAttacker.FaceImmediately(forward);
            Combatant dimmedTarget = CombatantFactory.Create("dimmed-target", Faction.Hostile, maxHealth: 5000f, position: forward);
            dimmedTarget.Soot = Full();

            float frailer = ResolveOnce(plainAttacker, ability, dimmedTarget);

            Assert.Equal(plain * (1f + SootTuning.DimmingDamageTakenBonus), frailer, 1);
        }

        private static float ResolveOnce(Combatant attacker, AbilityDefinition ability, Combatant target)
        {
            float before = target.Vitals.Health;

            AttackResolver.Resolve(
                attacker,
                ability,
                new List<Combatant> { target },
                new DeterministicRng(11),
                new List<Combatant>());

            return before - target.Vitals.Health;
        }
    }
}
