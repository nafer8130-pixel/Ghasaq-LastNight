using System;
using System.Collections.Generic;
using Ghasaq.Core.Stats;

namespace Ghasaq.Core.Items
{
    /// <summary>
    /// The Hearth's forging numbers (plan section 3.3, "طرق في الموقد").
    ///
    /// A level adds one flat bonus to a stat chosen by the piece's kind - the
    /// weapon sharpens, the armour thickens, the relic burns hotter. The bonus
    /// is deliberately additive per level rather than a percentage of the
    /// piece's own modifiers: those include Trade-offs (the Ashen Plate's move
    /// penalty), and scaling the whole array would quietly deepen the penalty
    /// as a reward. Numbers are draft until the economy slice tunes them.
    /// </summary>
    public static class ForgeTuning
    {
        /// <summary>Highest level a piece can reach (0 = unforged, 5 = five levels).</summary>
        public const int MaxLevel = 5;

        /// <summary>Cost in Soot to go from the current level to the next. Index is the current level.</summary>
        private static readonly int[] Costs = { 12, 24, 40, 60, 90 };

        /// <summary>
        /// Soot the next level costs. Zero means "nothing to buy": the caller
        /// must have refused a piece already at <see cref="MaxLevel"/>.
        /// </summary>
        public static int CostForLevel(int currentLevel)
        {
            if (currentLevel < 0 || currentLevel >= Costs.Length)
            {
                return 0;
            }

            return Costs[currentLevel];
        }

        /// <summary>The stat a level feeds, by the item's kind.</summary>
        public static StatId BonusStat(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.Weapon: return StatId.AttackPower;
                case ItemKind.Armor: return StatId.Armor;
                case ItemKind.Relic: return StatId.GhasaqPower;
                default: return StatId.AttackPower;
            }
        }

        /// <summary>The flat bonus one level adds, by the item's kind.</summary>
        public static float BonusPerLevel(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.Weapon: return 6f;
                case ItemKind.Armor: return 8f;
                case ItemKind.Relic: return 5f;
                default: return 0f;
            }
        }

        /// <summary>The total bonus a piece at this level contributes, or zero.</summary>
        public static float BonusAt(ItemDefinition item, int level)
        {
            if (item == null || level <= 0)
            {
                return 0f;
            }

            return BonusPerLevel(item.Kind) * level;
        }
    }

    /// <summary>Why a piece could not be forged, for the message the player sees.</summary>
    public enum ForgeFailure
    {
        None = 0,

        UnknownItem = 1,

        /// <summary>Not gear: materials, consumables and story pieces stay whole.</summary>
        NotForgeable = 2,

        /// <summary>Bound to the story.</summary>
        Bound = 3,

        /// <summary>Neither carried nor worn; the hammer works on what is yours.</summary>
        NotOwned = 4,

        /// <summary>The Hearth's hammer is in the camp, not out in the field.</summary>
        NotAtHearth = 5,

        /// <summary>Refused while hostiles stand; the hammer is not a mid-fight act.</summary>
        InCombat = 6,

        /// <summary>The bank cannot pay the next level's price.</summary>
        InsufficientSoot = 7,

        /// <summary>Already at <see cref="ForgeTuning.MaxLevel"/>.</summary>
        MaxLevel = 8
    }

    /// <summary>
    /// The forged level of each piece the player owns, keyed by item id.
    ///
    /// Keyed by id rather than tracked per copy because this build's inventory
    /// holds at most one copy of any id: one slot per type, and gear is
    /// MaxStack 1, so a second copy of the same id can never be owned. Under
    /// that model an id IS a copy, and the ledger stays as small as the bag.
    /// If the inventory ever learns to hold two copies, this is the class that
    /// has to learn to tell them apart.
    ///
    /// A destroyed piece's levels are destroyed with it: dismantling clears the
    /// entry (see <see cref="Ghasaq.Core.Simulation.GameSession.TrySalvage"/>),
    /// so a fresh drop of the same id never inherits an old investment.
    /// </summary>
    public sealed class ItemForge
    {
        private readonly Dictionary<string, int> _levels;

        public ItemForge()
        {
            _levels = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        /// <summary>The piece's level, or zero when it has never been forged.</summary>
        public int LevelOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            return _levels.TryGetValue(itemId, out int level) ? level : 0;
        }

        /// <summary>
        /// Sets a piece's level. Zero clears the entry; values clamp into
        /// 0..<see cref="ForgeTuning.MaxLevel"/>, so a hand-edited save cannot
        /// make a piece stronger than the table allows.
        /// </summary>
        public void SetLevel(string itemId, int level)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return;
            }

            if (level < 0)
            {
                level = 0;
            }
            else if (level > ForgeTuning.MaxLevel)
            {
                level = ForgeTuning.MaxLevel;
            }

            if (level == 0)
            {
                _levels.Remove(itemId);
                return;
            }

            _levels[itemId] = level;
        }

        /// <summary>Snapshot for saving: one entry per forged piece, the level in Quantity.</summary>
        public List<ItemStack> ToStacks()
        {
            var stacks = new List<ItemStack>(_levels.Count);

            foreach (KeyValuePair<string, int> entry in _levels)
            {
                stacks.Add(new ItemStack(entry.Key, entry.Value));
            }

            return stacks;
        }

        /// <summary>
        /// Restores a saved ledger. Levels for ids this build no longer knows
        /// stay in the ledger and are simply never read - the same "do not lose
        /// the save over removed content" rule the inventory follows.
        /// </summary>
        public void LoadFrom(IEnumerable<ItemStack> stacks)
        {
            Clear();

            if (stacks == null)
            {
                return;
            }

            foreach (ItemStack stack in stacks)
            {
                if (stack.IsEmpty)
                {
                    continue;
                }

                SetLevel(stack.ItemId, stack.Quantity);
            }
        }

        public void Clear()
        {
            _levels.Clear();
        }
    }
}
