using System.Collections.Generic;
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
    /// السوابق / the rare prefixes of plan section 3.3: each one pairs with
    /// every piece of non-bound gear into a variant definition, looted gear
    /// rolls them through the shared stream, and a prefixed piece is an
    /// ordinary item everywhere after that - bag, forge ledger, save.
    /// </summary>
    public class PrefixTests
    {
        private static readonly ItemDatabase Items = GameContent.BuildItems();

        private const string GearTableId = "test-gear";

        private static GameSession NewSession(ulong seed)
        {
            var session = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(seed),
                WorldBounds.Square(40f));

            GameContent.Populate(session);
            return session;
        }

        private static void RegisterGearTable(GameSession session)
        {
            session.RegisterLootTable(new LootTable
            {
                Id = GearTableId,
                Guaranteed = new[] { new LootEntry(GameContent.ItemSigilbearersBlade, 1f, 1, 1) },
                MinRolls = 0,
                MaxRolls = 0
            });
        }

        private static Combatant GearVictim()
        {
            EnemyArchetype archetype = GameContent.BuildHollowWalker();
            Combatant victim = archetype.Create("victim", new Float3(0f, 0f, 5f));
            victim.LootTableId = GearTableId;
            return victim;
        }

        private static List<string> GrantedIds(GameSession session, int grants)
        {
            RegisterGearTable(session);
            Combatant victim = GearVictim();

            var ids = new List<string>();
            session.LootGranted += stack => ids.Add(stack.ItemId);

            for (int i = 0; i < grants; i++)
            {
                session.GrantLoot(victim);
            }

            return ids;
        }

        // ---------------------------- the numbers, pinned --------------------------

        [Fact]
        public void TheFourPrefixes_MatchTheSpec()
        {
            List<PrefixDefinition> prefixes = GameContent.BuildPrefixes();

            Assert.Equal(4, prefixes.Count);
            Assert.Equal(0.12f, PrefixTuning.Chance, 3);
            Assert.Equal("+", PrefixTuning.VariantSeparator);

            Assert.Equal(GameContent.PrefixEmberforged, prefixes[0].Id);
            Assert.Equal("Emberforged", prefixes[0].DisplayName);
            Assert.Equal(4f, prefixes[0].Weight, 3);
            Assert.Equal(GameContent.PrefixStaunch, prefixes[1].Id);
            Assert.Equal(3f, prefixes[1].Weight, 3);
            Assert.Equal(GameContent.PrefixVigilant, prefixes[2].Id);
            Assert.Equal(2f, prefixes[2].Weight, 3);
            Assert.Equal(GameContent.PrefixVeiltouched, prefixes[3].Id);
            Assert.Equal(1f, prefixes[3].Weight, 3);

            foreach (PrefixDefinition prefix in prefixes)
            {
                Assert.False(string.IsNullOrWhiteSpace(prefix.DisplayName));
                Assert.True(prefix.Modifiers.Length > 0, prefix.Id + " adds nothing.");
                Assert.True(prefix.Weight > 0f);
            }

            // One tier up, never into Mythic; the top ordinary tier holds.
            Assert.Equal(ItemRarity.Uncommon, PrefixTuning.Bumped(ItemRarity.Common));
            Assert.Equal(ItemRarity.Rare, PrefixTuning.Bumped(ItemRarity.Uncommon));
            Assert.Equal(ItemRarity.Eclipse, PrefixTuning.Bumped(ItemRarity.Rare));
            Assert.Equal(ItemRarity.Eclipse, PrefixTuning.Bumped(ItemRarity.Eclipse));
            Assert.Equal(ItemRarity.Mythic, PrefixTuning.Bumped(ItemRarity.Mythic));
        }

        // ------------------------------ content variants ---------------------------

        [Fact]
        public void EveryPieceOfGear_GetsAVariantForEveryPrefix()
        {
            var bases = new List<ItemDefinition>();

            foreach (ItemDefinition item in Items.All)
            {
                if (item.IsEquippable && !item.IsBound && !item.Id.Contains("+"))
                {
                    bases.Add(item);
                }
            }

            Assert.Equal(4, bases.Count);

            foreach (PrefixDefinition prefix in GameContent.BuildPrefixes())
            {
                foreach (ItemDefinition baseItem in bases)
                {
                    string variantId = prefix.VariantId(baseItem.Id);

                    Assert.True(Items.TryGet(variantId, out ItemDefinition variant), variantId + " is not registered.");
                    Assert.Equal(prefix.DisplayName + " " + baseItem.DisplayName, variant.DisplayName);
                    Assert.Equal(baseItem.Kind, variant.Kind);
                    Assert.Equal(baseItem.Slot, variant.Slot);
                    Assert.Equal(baseItem.RequiredLevel, variant.RequiredLevel);
                    Assert.Equal(PrefixTuning.Bumped(baseItem.Rarity), variant.Rarity);
                    Assert.Equal(
                        baseItem.Modifiers.Length + prefix.Modifiers.Length,
                        variant.Modifiers.Length);
                }
            }

            // The affix never touches materials or story pieces.
            PrefixDefinition emberforged = GameContent.BuildPrefixes()[0];
            Assert.False(Items.Contains(emberforged.VariantId(GameContent.ItemAsh)));
            Assert.False(Items.Contains(emberforged.VariantId(GameContent.ItemSentinelsCore)));
        }

        // --------------------------------- the rolls -------------------------------

        [Fact]
        public void Drops_RollPrefixesThroughTheSharedStream()
        {
            // The seed was chosen so the first gear drop comes out prefixed;
            // only the pinned value lives here, not luck.
            const ulong seed = 13;

            List<string> first = GrantedIds(NewSession(seed), 12);
            List<string> second = GrantedIds(NewSession(seed), 12);

            Assert.Equal("staunch+sigilbearers-blade", first[0]);
            Assert.Equal(first, second);

            bool prefixed = false;
            for (int i = 0; i < first.Count; i++)
            {
                if (first[i].Contains("+"))
                {
                    prefixed = true;
                }
            }

            Assert.True(prefixed, "the run must have met at least one affix in twelve drops.");
        }

        [Fact]
        public void DirectGrants_NeverCarryAPrefix()
        {
            // Quest rewards and starting kits go through GrantItem, not loot:
            // a story handout must stay exactly what it says.
            GameSession session = NewSession(17);

            for (int i = 0; i < 4; i++)
            {
                session.GrantItem(GameContent.ItemSigilbearersBlade, 1);
            }

            foreach (KeyValuePair<string, int> entry in session.Inventory.Entries)
            {
                Assert.False(entry.Key.Contains("+"), "GrantItem rolled a prefix: " + entry.Key);
            }
        }

        [Fact]
        public void WithoutRegisteredPrefixes_DropsStayPlain()
        {
            var session = new GameSession(
                GameContent.CreatePlayer(),
                GameContent.BuildItems(),
                new ExperienceCurve(),
                GameContent.BuildPlayerGrowth(),
                new DeterministicRng(19),
                WorldBounds.Square(40f));

            // No Populate: no prefixes registered.
            RegisterGearTable(session);

            List<string> ids = GrantedIds(session, 12);

            Assert.NotEmpty(ids);
            foreach (string id in ids)
            {
                Assert.Equal(GameContent.ItemSigilbearersBlade, id);
            }
        }

        // --------------------------- how a prefixed piece lives --------------------

        [Fact]
        public void APrefixedDrop_DismantlesForItsBumpedTier()
        {
            GameSession session = NewSession(23);
            session.GrantItem("emberforged+ember-relic", 1);

            Assert.True(session.TrySalvage("emberforged+ember-relic", out SalvageFailure failure, out int soot));
            Assert.Equal(SalvageFailure.None, failure);
            Assert.Equal(40, soot);
            Assert.Equal(40, session.SootBank.Balance);
        }

        [Fact]
        public void APrefixedPiece_ForgesUnderItsOwnId()
        {
            GameSession session = NewSession(29);
            session.GrantItem(GameContent.ItemSigilbearersBlade, 1);
            session.GrantItem("emberforged+sigilbearers-blade", 1);
            session.SootBank.Deposit(100);

            Assert.True(session.TryForge("emberforged+sigilbearers-blade", out ForgeFailure failure, out _));
            Assert.Equal(ForgeFailure.None, failure);
            Assert.Equal(1, session.Forge.LevelOf("emberforged+sigilbearers-blade"));
            Assert.Equal(0, session.Forge.LevelOf(GameContent.ItemSigilbearersBlade));
            Assert.Equal(88, session.SootBank.Balance);
        }

        [Fact]
        public void APrefixedDrop_RoundTripsThroughASave()
        {
            GameSession session = NewSession(31);
            session.GrantItem("veiltouched+ghasaq-edge", 1);
            session.SootBank.Deposit(100);
            Assert.True(session.TryForge("veiltouched+ghasaq-edge", out _, out _));

            string json = SaveSerializer.Serialize(session.CreateSave());
            Assert.True(SaveSerializer.TryDeserialize(json, out SaveGame save, out string error), error);

            GameSession restored = NewSession(31);
            restored.ApplySave(save);

            Assert.Equal(1, restored.Inventory.Count("veiltouched+ghasaq-edge"));
            Assert.Equal(1, restored.Forge.LevelOf("veiltouched+ghasaq-edge"));
            Assert.Equal("Veiltouched Ghasaq Edge", restored.Items.Get("veiltouched+ghasaq-edge").DisplayName);
        }
    }
}
