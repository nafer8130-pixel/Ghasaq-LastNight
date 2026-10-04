using Ghasaq.Core.Ai;
using Ghasaq.Core.Combat;
using Ghasaq.Core.Content;
using Ghasaq.Core.Items;
using Ghasaq.Core.Numerics;
using Ghasaq.Core.Progression;
using Ghasaq.Core.Randomness;
using Ghasaq.Core.Serialization;
using Ghasaq.Core.Simulation;
using Ghasaq.Core.Stats;
using Xunit;

namespace Ghasaq.Core.Tests.Items
{
    /// <summary>
    /// الطَّرْق at the Hearth (plan section 3.3): the Soot bank pays for levels,
    /// a wrought piece gets a flat bonus by kind, a worn piece shows it at once,
    /// and a destroyed piece forgets its levels. The numbers pinned here are the
    /// draft values of Documentation/Reliquary.md.
    /// </summary>
    public class ForgeTests
    {
        private static GameSession NewSession()
        {
            var session = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(2027),
                WorldBounds.Square(40f));

            // Populate leaves the run at the Hearth's camp, where the hammer is.
            GameContent.Populate(session);
            return session;
        }

        // ---------------------------- the numbers, pinned --------------------------

        [Fact]
        public void TheNumbers_MatchTheSpec()
        {
            Assert.Equal(5, ForgeTuning.MaxLevel);
            Assert.Equal(12, ForgeTuning.CostForLevel(0));
            Assert.Equal(24, ForgeTuning.CostForLevel(1));
            Assert.Equal(40, ForgeTuning.CostForLevel(2));
            Assert.Equal(60, ForgeTuning.CostForLevel(3));
            Assert.Equal(90, ForgeTuning.CostForLevel(4));
            Assert.Equal(0, ForgeTuning.CostForLevel(ForgeTuning.MaxLevel));

            Assert.Equal(6f, ForgeTuning.BonusPerLevel(ItemKind.Weapon), 3);
            Assert.Equal(8f, ForgeTuning.BonusPerLevel(ItemKind.Armor), 3);
            Assert.Equal(5f, ForgeTuning.BonusPerLevel(ItemKind.Relic), 3);

            Assert.Equal(StatId.AttackPower, ForgeTuning.BonusStat(ItemKind.Weapon));
            Assert.Equal(StatId.Armor, ForgeTuning.BonusStat(ItemKind.Armor));
            Assert.Equal(StatId.GhasaqPower, ForgeTuning.BonusStat(ItemKind.Relic));
        }

        // -------------------------------- the loop --------------------------------

        [Fact]
        public void Forging_HeldGearAtTheHearth_SpendsTheSootAndAddsALevel()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemSigilbearersBlade, 1);
            session.SootBank.Deposit(100);

            Assert.True(session.TryForge(GameContent.ItemSigilbearersBlade, out ForgeFailure failure, out int cost));
            Assert.Equal(ForgeFailure.None, failure);
            Assert.Equal(12, cost);
            Assert.Equal(1, session.Forge.LevelOf(GameContent.ItemSigilbearersBlade));
            Assert.Equal(88, session.SootBank.Balance);

            // The next level costs more, and the ledger follows.
            Assert.True(session.TryForge(GameContent.ItemSigilbearersBlade, out _, out cost));
            Assert.Equal(24, cost);
            Assert.Equal(2, session.Forge.LevelOf(GameContent.ItemSigilbearersBlade));
            Assert.Equal(64, session.SootBank.Balance);
        }

        [Fact]
        public void Forging_WornGear_RaisesItsStatOnTheSpot()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemSigilbearersBlade, 1);

            float bare = session.Player.Stats.Get(StatId.AttackPower);

            Assert.True(session.TryEquipFromInventory(GameContent.ItemSigilbearersBlade, out _));
            Assert.Equal(bare + 22f, session.Player.Stats.Get(StatId.AttackPower), 3);

            session.SootBank.Deposit(100);
            Assert.True(session.TryForge(GameContent.ItemSigilbearersBlade, out _, out _));

            // 22 from the blade itself, 6 from its first level.
            Assert.Equal(bare + 28f, session.Player.Stats.Get(StatId.AttackPower), 3);

            // Taking it off removes the whole contribution, level included.
            Assert.True(session.TryUnequipToInventory(EquipSlot.Weapon));
            Assert.Equal(bare, session.Player.Stats.Get(StatId.AttackPower), 3);
        }

        [Fact]
        public void Forging_StopsAtTheMaximumLevel()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemSigilbearersBlade, 1);
            session.SootBank.Deposit(300);

            for (int i = 0; i < ForgeTuning.MaxLevel; i++)
            {
                Assert.True(session.TryForge(GameContent.ItemSigilbearersBlade, out _, out _));
            }

            // 12 + 24 + 40 + 60 + 90.
            Assert.Equal(ForgeTuning.MaxLevel, session.Forge.LevelOf(GameContent.ItemSigilbearersBlade));
            Assert.Equal(300 - 226, session.SootBank.Balance);

            Assert.False(session.TryForge(GameContent.ItemSigilbearersBlade, out ForgeFailure failure, out int cost));
            Assert.Equal(ForgeFailure.MaxLevel, failure);
            Assert.Equal(0, cost);
            Assert.Equal(300 - 226, session.SootBank.Balance);
        }

        [Fact]
        public void Dismantling_ClearsTheForgedLevelsOfThePiece()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemEmberRelic, 1);
            session.SootBank.Deposit(100);

            Assert.True(session.TryForge(GameContent.ItemEmberRelic, out _, out _));
            Assert.Equal(88, session.SootBank.Balance);

            Assert.True(session.TrySalvage(GameContent.ItemEmberRelic, out _, out _));
            Assert.Equal(103, session.SootBank.Balance);

            // The piece is gone, and so is its investment - no refund, no ghost.
            Assert.Equal(0, session.Forge.LevelOf(GameContent.ItemEmberRelic));

            // A fresh drop of the same id starts at level zero and full price.
            session.GrantItem(GameContent.ItemEmberRelic, 1);
            Assert.True(session.TryForge(GameContent.ItemEmberRelic, out _, out int cost));
            Assert.Equal(12, cost);
        }

        // ------------------------------- the refusals -----------------------------

        [Fact]
        public void Forging_IsRefusedAwayFromTheHearth_WithoutSpending()
        {
            GameSession session = NewSession();
            session.EnterRegion(GameContent.RegionWilds);
            session.GrantItem(GameContent.ItemSigilbearersBlade, 1);
            session.SootBank.Deposit(100);

            Assert.False(session.TryForge(GameContent.ItemSigilbearersBlade, out ForgeFailure failure, out _));
            Assert.Equal(ForgeFailure.NotAtHearth, failure);
            Assert.Equal(0, session.Forge.LevelOf(GameContent.ItemSigilbearersBlade));
            Assert.Equal(100, session.SootBank.Balance);
        }

        [Fact]
        public void Forging_IsRefusedWhileHostilesStand()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemSigilbearersBlade, 1);
            session.SootBank.Deposit(100);

            EnemyArchetype archetype = GameContent.BuildHollowWalker();
            Combatant walker = archetype.Create("walker", new Float3(0f, 0f, 8f));
            session.Encounter.AddEnemy(walker, archetype.Abilities, archetype.Brain, archetype.AttackAbilityIndex);

            Assert.False(session.TryForge(GameContent.ItemSigilbearersBlade, out ForgeFailure failure, out _));
            Assert.Equal(ForgeFailure.InCombat, failure);
            Assert.Equal(100, session.SootBank.Balance);
        }

        [Fact]
        public void Forging_RefusesWhatIsNotGearAndWhatIsBound()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemAsh, 3);
            session.GrantItem(GameContent.ItemSentinelsCore, 1);
            session.SootBank.Deposit(100);

            Assert.False(session.TryForge(GameContent.ItemAsh, out ForgeFailure material, out _));
            Assert.Equal(ForgeFailure.NotForgeable, material);

            Assert.False(session.TryForge(GameContent.ItemSentinelsCore, out ForgeFailure bound, out _));
            Assert.Equal(ForgeFailure.Bound, bound);

            Assert.Equal(100, session.SootBank.Balance);
        }

        [Fact]
        public void Forging_RefusesWhatThePlayerDoesNotOwn()
        {
            GameSession session = NewSession();
            session.SootBank.Deposit(100);

            Assert.False(session.TryForge(GameContent.ItemEmberRelic, out ForgeFailure failure, out _));
            Assert.Equal(ForgeFailure.NotOwned, failure);
            Assert.Equal(100, session.SootBank.Balance);
        }

        [Fact]
        public void Forging_RefusesAnUnknownId()
        {
            GameSession session = NewSession();

            Assert.False(session.TryForge("no-such-thing", out ForgeFailure failure, out _));
            Assert.Equal(ForgeFailure.UnknownItem, failure);
        }

        [Fact]
        public void Forging_WithoutEnoughSoot_NamesThePriceAndTakesNothing()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemSigilbearersBlade, 1);
            session.SootBank.Deposit(5);

            Assert.False(session.TryForge(GameContent.ItemSigilbearersBlade, out ForgeFailure failure, out int cost));
            Assert.Equal(ForgeFailure.InsufficientSoot, failure);
            Assert.Equal(12, cost);
            Assert.Equal(5, session.SootBank.Balance);
            Assert.Equal(0, session.Forge.LevelOf(GameContent.ItemSigilbearersBlade));
        }

        // --------------------------------- saving ---------------------------------

        [Fact]
        public void TheForge_RoundTripsThroughASave_AndTheWornBonusComesBack()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemSigilbearersBlade, 1);
            Assert.True(session.TryEquipFromInventory(GameContent.ItemSigilbearersBlade, out _));
            session.SootBank.Deposit(100);

            Assert.True(session.TryForge(GameContent.ItemSigilbearersBlade, out _, out _));

            float attack = session.Player.Stats.Get(StatId.AttackPower);
            int balance = session.SootBank.Balance;

            string json = SaveSerializer.Serialize(session.CreateSave());
            Assert.True(SaveSerializer.TryDeserialize(json, out SaveGame save, out string error), error);

            GameSession restored = NewSession();
            restored.ApplySave(save);

            Assert.Equal(1, restored.Forge.LevelOf(GameContent.ItemSigilbearersBlade));
            Assert.Equal(balance, restored.SootBank.Balance);
            Assert.Equal(GameContent.ItemSigilbearersBlade, restored.Equipment.GetEquipped(EquipSlot.Weapon));
            Assert.Equal(attack, restored.Player.Stats.Get(StatId.AttackPower), 3);
        }
    }
}
