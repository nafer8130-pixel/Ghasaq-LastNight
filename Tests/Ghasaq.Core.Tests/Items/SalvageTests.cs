using Ghasaq.Core.Ai;
using Ghasaq.Core.Combat;
using Ghasaq.Core.Content;
using Ghasaq.Core.Items;
using Ghasaq.Core.Numerics;
using Ghasaq.Core.Progression;
using Ghasaq.Core.Randomness;
using Ghasaq.Core.Serialization;
using Ghasaq.Core.Simulation;
using Xunit;

namespace Ghasaq.Core.Tests.Items
{
    /// <summary>
    /// The Reliquary's dismantling half and the Hearth's bench (plan sections
    /// 3.3 and 3.7): gear held in the bag breaks down into the permanent Soot
    /// balance - at the camp, out of combat, and never from the equipped copy.
    /// The yields pinned here are the draft numbers of Documentation/Reliquary.md.
    /// </summary>
    public class SalvageTests
    {
        private static GameSession NewSession()
        {
            var session = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(2026),
                WorldBounds.Square(40f));

            // Populate leaves the run at the Hearth's camp, which is where the
            // hammer stands.
            GameContent.Populate(session);
            return session;
        }

        // ---------------------------- the numbers, pinned --------------------------

        [Fact]
        public void TheYields_MatchTheSpec()
        {
            Assert.Equal(2, SalvageTuning.SootFor(ItemRarity.Common));
            Assert.Equal(6, SalvageTuning.SootFor(ItemRarity.Uncommon));
            Assert.Equal(15, SalvageTuning.SootFor(ItemRarity.Rare));
            Assert.Equal(40, SalvageTuning.SootFor(ItemRarity.Eclipse));
            Assert.Equal(80, SalvageTuning.SootFor(ItemRarity.Mythic));
        }

        // -------------------------------- the loop --------------------------------

        [Fact]
        public void Dismantling_HeldGearAtTheHearth_BanksItsSoot()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemEmberRelic, 1);

            Assert.True(session.TrySalvage(GameContent.ItemEmberRelic, out SalvageFailure failure, out int soot));
            Assert.Equal(SalvageFailure.None, failure);
            Assert.Equal(15, soot);
            Assert.Equal(0, session.Inventory.Count(GameContent.ItemEmberRelic));
            Assert.Equal(15, session.SootBank.Balance);

            // The piece is gone, so the same dismantle cannot pay twice.
            Assert.False(session.TrySalvage(GameContent.ItemEmberRelic, out failure, out _));
            Assert.Equal(SalvageFailure.NotHeld, failure);
            Assert.Equal(15, session.SootBank.Balance);
        }

        [Fact]
        public void Dismantling_IsRefusedAwayFromTheHearth_WithoutLoss()
        {
            GameSession session = NewSession();
            session.EnterRegion(GameContent.RegionWilds);
            session.GrantItem(GameContent.ItemEmberRelic, 1);

            Assert.False(session.TrySalvage(GameContent.ItemEmberRelic, out SalvageFailure failure, out int soot));
            Assert.Equal(SalvageFailure.NotAtHearth, failure);
            Assert.Equal(0, soot);
            Assert.Equal(1, session.Inventory.Count(GameContent.ItemEmberRelic));
            Assert.Equal(0, session.SootBank.Balance);
        }

        [Fact]
        public void Dismantling_IsRefusedWhileHostilesStand()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemEmberRelic, 1);

            // A hostile on its feet means the bench is out of reach, exactly as it
            // is for the Sigil swap.
            EnemyArchetype archetype = GameContent.BuildHollowWalker();
            Combatant walker = archetype.Create("walker", new Float3(0f, 0f, 8f));
            session.Encounter.AddEnemy(walker, archetype.Abilities, archetype.Brain, archetype.AttackAbilityIndex);

            Assert.False(session.TrySalvage(GameContent.ItemEmberRelic, out SalvageFailure failure, out _));
            Assert.Equal(SalvageFailure.InCombat, failure);
            Assert.Equal(1, session.Inventory.Count(GameContent.ItemEmberRelic));
            Assert.Equal(0, session.SootBank.Balance);
        }

        // ------------------------------- the refusals -----------------------------

        [Fact]
        public void Dismantling_RefusesWhatIsNotGear()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemAsh, 3);
            session.GrantItem(GameContent.ItemEmberDraught, 2);

            Assert.False(session.TrySalvage(GameContent.ItemAsh, out SalvageFailure material, out _));
            Assert.Equal(SalvageFailure.NotSalvageable, material);

            Assert.False(session.TrySalvage(GameContent.ItemEmberDraught, out SalvageFailure draught, out _));
            Assert.Equal(SalvageFailure.NotSalvageable, draught);

            Assert.Equal(3, session.Inventory.Count(GameContent.ItemAsh));
            Assert.Equal(2, session.Inventory.Count(GameContent.ItemEmberDraught));
            Assert.Equal(0, session.SootBank.Balance);
        }

        [Fact]
        public void Dismantling_RefusesBoundGear()
        {
            GameSession session = NewSession();

            // No shipped piece of gear is bound yet, so the rule is pinned with
            // one that is: binding wins over every other property.
            session.Items.Register(new ItemDefinition
            {
                Id = "oath-blade",
                DisplayName = "Oath Blade",
                Kind = ItemKind.Weapon,
                Rarity = ItemRarity.Eclipse,
                MaxStack = 1,
                IsBound = true
            });

            session.GrantItem("oath-blade", 1);

            Assert.False(session.TrySalvage("oath-blade", out SalvageFailure failure, out _));
            Assert.Equal(SalvageFailure.Bound, failure);
            Assert.Equal(1, session.Inventory.Count("oath-blade"));
            Assert.Equal(0, session.SootBank.Balance);
        }

        [Fact]
        public void Dismantling_RefusesAStoryItem()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemSentinelsCore, 1);

            Assert.False(session.TrySalvage(GameContent.ItemSentinelsCore, out SalvageFailure failure, out _));
            Assert.Equal(SalvageFailure.Bound, failure);
            Assert.Equal(1, session.Inventory.Count(GameContent.ItemSentinelsCore));
        }

        [Fact]
        public void Dismantling_NeverTakesTheEquippedCopy()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemSigilbearersBlade, 1);

            Assert.True(session.TryEquipFromInventory(GameContent.ItemSigilbearersBlade, out _));

            // The blade is on the character now, not in the bag: there is nothing
            // to dismantle.
            Assert.False(session.TrySalvage(GameContent.ItemSigilbearersBlade, out SalvageFailure failure, out _));
            Assert.Equal(SalvageFailure.NotHeld, failure);
            Assert.Equal(GameContent.ItemSigilbearersBlade, session.Equipment.GetEquipped(EquipSlot.Weapon));
            Assert.Equal(0, session.SootBank.Balance);
        }

        [Fact]
        public void Dismantling_AnUnknownId_ReportsIt()
        {
            GameSession session = NewSession();

            Assert.False(session.TrySalvage("no-such-thing", out SalvageFailure failure, out _));
            Assert.Equal(SalvageFailure.UnknownItem, failure);
        }

        // --------------------------------- saving ---------------------------------

        [Fact]
        public void TheSootBalance_RoundTripsThroughASave()
        {
            GameSession session = NewSession();
            session.GrantItem(GameContent.ItemEmberRelic, 1);
            Assert.True(session.TrySalvage(GameContent.ItemEmberRelic, out _, out _));

            string json = SaveSerializer.Serialize(session.CreateSave());
            Assert.True(SaveSerializer.TryDeserialize(json, out SaveGame save, out string error), error);

            GameSession restored = NewSession();
            restored.ApplySave(save);

            Assert.Equal(15, restored.SootBank.Balance);
        }
    }
}
