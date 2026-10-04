using Ghasaq.Core.Combat;
using Ghasaq.Core.Content;
using Xunit;

namespace Ghasaq.Core.Tests.Combat
{
    /// <summary>
    /// Pins the slice's combat budgets (plan section 7) and proves the shipped
    /// enemy content respects them.
    ///
    /// The plan fixes these numbers by feel, not by formula — a lethal move that
    /// cannot be read is the failure they exist to prevent — so they are asserted
    /// here rather than left to review.
    /// </summary>
    public class CombatTuningTests
    {
        [Fact]
        public void TheBudgets_MatchThePlan()
        {
            Assert.Equal(0.4f, CombatTuning.TelegraphMinSeconds, 3);
            Assert.Equal(40, CombatTuning.HitStopMinMilliseconds);
            Assert.Equal(80, CombatTuning.HitStopMaxMilliseconds);
            Assert.Equal(3, CombatTuning.TargetHostilesOnScreen);

            Assert.True(
                CombatTuning.HitStopMinMilliseconds < CombatTuning.HitStopMaxMilliseconds,
                "The hit-stop band is inverted.");
        }

        [Fact]
        public void EveryDamagingEnemyAbility_TelegraphsAtLeastTheFloor()
        {
            foreach (EnemyArchetype archetype in GameContent.BuildEnemyArchetypes())
            {
                foreach (AbilityDefinition ability in archetype.Abilities)
                {
                    if (!ability.DealsDamage)
                    {
                        continue;
                    }

                    Assert.True(
                        ability.WindupSeconds >= CombatTuning.TelegraphMinSeconds,
                        archetype.Id + "'s '" + ability.Id + "' telegraphs " + ability.WindupSeconds +
                        "s, under the " + CombatTuning.TelegraphMinSeconds + "s floor. A lethal move " +
                        "must be readable (plan section 7).");
                }
            }
        }
    }
}
