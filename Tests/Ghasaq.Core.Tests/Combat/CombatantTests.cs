using System.Collections.Generic;
using Ghasaq.Core.Combat;
using Ghasaq.Core.Tests.Support;
using Xunit;

namespace Ghasaq.Core.Tests.Combat
{
    /// <summary>
    /// Tests the line between a blow and a health change.
    ///
    /// <see cref="Combatant.Struck"/> exists because the view layer needs to know
    /// that the combat path landed - it gives a hit its weight (hit-stop, shake,
    /// a spark), and a damage-over-time tick is none of those. These tests pin
    /// the distinction in both directions: a landed blow strikes, a tick does
    /// not, and a blow the defences refuse does not either.
    /// </summary>
    public class CombatantTests
    {
        [Fact]
        public void ALandedBlow_ReportsItselfAsStruck()
        {
            Combatant victim = CombatantFactory.Create("victim", maxHealth: 100f);

            var struck = new List<DamageResult>();
            victim.Struck += (_, _, result) => struck.Add(result);

            float applied = victim.ReceiveDamage(new DamageResult(30f, 30f, 30f, true, 0f), null);

            Assert.Equal(30f, applied, 3);
            DamageResult hit = Assert.Single(struck);
            Assert.Equal(30f, hit.Applied, 3);
            Assert.True(hit.Critical);
        }

        [Fact]
        public void ADamageOverTimeTick_IsDamagedButNotStruck()
        {
            Combatant victim = CombatantFactory.Create("victim", maxHealth: 100f);

            int struck = 0;
            int damaged = 0;
            victim.Struck += (_, _, _) => struck++;
            victim.Damaged += (_, _, _) => damaged++;

            victim.Statuses.Apply(StatusEffect.Dot(
                StatusKind.Bleeding,
                DamageType.Physical,
                damagePerTick: 5f,
                duration: 3f,
                tickInterval: 1f,
                source: null,
                maxStacks: 1));

            victim.Tick(1.01f);

            Assert.True(damaged > 0, "the tick still reaches the health pool");
            Assert.Equal(1, damaged);
            Assert.Equal(0, struck);
            Assert.Equal(95f, victim.Vitals.Health, 3);
        }

        [Fact]
        public void ABlowThatIsRefused_DoesNotStrike()
        {
            Combatant victim = CombatantFactory.Create("victim", maxHealth: 100f);

            int struck = 0;
            victim.Struck += (_, _, _) => struck++;
            victim.Vitals.GrantInvulnerability(0.2f);

            float applied = victim.ReceiveDamage(new DamageResult(30f, 30f, 30f, false, 0f), null);

            Assert.Equal(0f, applied, 3);
            Assert.Equal(0, struck);
        }
    }
}
