using Ghasaq.Core.Combat;
using Ghasaq.Core.Content;
using Ghasaq.Core.Items;
using Ghasaq.Core.Progression;
using Ghasaq.Core.Randomness;
using Ghasaq.Core.Serialization;
using Ghasaq.Core.Simulation;
using Xunit;

namespace Ghasaq.Core.Tests.Simulation
{
    /// <summary>
    /// Tests the run's fall-and-rise loop: the bearer falls, the run holds, and
    /// rising restores them without ending the run.
    ///
    /// These exist because of a real gap. Everything in the game could die
    /// except the one thing whose death the loop needs: the player was skipped
    /// by targeting, by movement and by the corpse cleanup, and no layer
    /// anywhere recorded that the run had stopped. A player who fell simply lay
    /// there - the fight had no living target, the HUD kept drawing, and the
    /// only way on was to quit and load a save.
    ///
    /// The tests below pin the transition rather than the staging: where the
    /// bearer wakes and which region receives them is the host's decision, so
    /// the smoke test covers that end.
    /// </summary>
    public class DefeatTests
    {
        private static GameSession MakeSession(out Combatant player)
        {
            player = GameContent.CreatePlayer();
            player.Soot = new SootMeter();

            var session = new GameSession(
                player,
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(20250925),
                WorldBounds.Square(40f));

            GameContent.Populate(session);

            // The fall is reported through the encounter's death event, so the
            // bearer is attached the way the running game attaches them.
            session.Encounter.AddDriven(player, GameContent.BuildPlayerAbilities(), null, isPlayer: true);

            return session;
        }

        /// <summary>Fells the bearer the way the simulation does: damage, then the tick that announces it.</summary>
        private static void Fall(Combatant player)
        {
            player.Vitals.ApplyDamage(player.Vitals.MaxHealth * 2f, null);

            // The combatant announces its death on the tick that first sees a
            // corpse; that is what the encounter - and through it the session -
            // hears.
            player.Tick(1f / 60f);
        }

        // ------------------------------ the fall itself ---------------------------

        [Fact]
        public void FallingAnnouncesOnceAndHoldsTheRun()
        {
            GameSession session = MakeSession(out Combatant player);
            int falls = 0;
            session.PlayerFallen += () => falls++;

            Assert.False(session.IsPlayerFallen);
            Assert.True(player.IsAlive);

            Fall(player);

            Assert.False(player.IsAlive);
            Assert.True(session.IsPlayerFallen);
            Assert.Equal(1, falls);

            // The fight keeps stepping - there is no living target, but the
            // world is not torn down - and the fall is not re-announced.
            session.Update(0.05f);

            Assert.Equal(1, falls);
            Assert.True(session.IsPlayerFallen);
        }

        [Fact]
        public void FallingIsNotARewardPath()
        {
            // The bearer's fall must not travel the enemy-defeat plumbing:
            // no experience, no loot, no kill credit.
            GameSession session = MakeSession(out Combatant player);
            int experienceBefore = session.Progression.TotalExperience;

            Fall(player);

            Assert.True(session.IsPlayerFallen);
            Assert.Equal(experienceBefore, session.Progression.TotalExperience);
            Assert.Equal(0, session.Inventory.Count(GameContent.ItemEmberRelic));
        }

        // ------------------------------ the rise ----------------------------------

        [Fact]
        public void RisingWithoutFallingIsRefused()
        {
            GameSession session = MakeSession(out Combatant player);

            Assert.False(session.TryRiseFromDefeat(out DefeatFailure failure));
            Assert.Equal(DefeatFailure.NotFallen, failure);
            Assert.True(session.Player.IsAlive);
        }

        [Fact]
        public void RisingRestoresTheBearerAndWashesTheSoot()
        {
            GameSession session = MakeSession(out Combatant player);
            Fall(player);

            // The meter a lost fight had built up.
            player.Soot.NotifyGhasaqUsed();
            player.Soot.NotifyGhasaqUsed();
            Assert.True(player.Soot.Soot > 0f);

            Assert.True(session.TryRiseFromDefeat(out DefeatFailure failure));

            Assert.Equal(DefeatFailure.None, failure);
            Assert.False(session.IsPlayerFallen);
            Assert.True(player.IsAlive);
            Assert.Equal(player.Vitals.MaxHealth, player.Vitals.Health, 3);
            Assert.Equal(0f, player.Soot.Soot, 3);
        }

        [Fact]
        public void RisingTwiceIsRefused()
        {
            GameSession session = MakeSession(out Combatant player);
            Fall(player);

            Assert.True(session.TryRiseFromDefeat(out _));

            Assert.False(session.TryRiseFromDefeat(out DefeatFailure failure));
            Assert.Equal(DefeatFailure.NotFallen, failure);
            Assert.True(player.IsAlive);
        }

        [Fact]
        public void ALoadedRunIsAStandingOne()
        {
            GameSession session = MakeSession(out Combatant player);
            SaveGame save = session.CreateSave();

            Fall(player);
            Assert.True(session.IsPlayerFallen);

            session.ApplySave(save);

            Assert.False(session.IsPlayerFallen);
            Assert.True(session.Player.IsAlive);

            // The load re-armed the death announcement too: a second fall after
            // a load must still reach the session, or the defeat screen would
            // never open again for that run.
            int falls = 0;
            session.PlayerFallen += () => falls++;

            Fall(player);

            Assert.True(session.IsPlayerFallen);
            Assert.Equal(1, falls);
        }
    }
}
