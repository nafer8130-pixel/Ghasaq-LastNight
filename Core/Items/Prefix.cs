using System;
using Ghasaq.Core.Stats;

namespace Ghasaq.Core.Items
{
    /// <summary>
    /// One rare affix a looted piece may carry (plan section 3.3, "سوابق").
    ///
    /// A prefix never exists on its own. The content pairs it with every base
    /// piece into a variant definition whose id is
    /// <c>prefix + base</c>, so a prefixed piece is an ordinary item to every
    /// system that follows it: the bag, the loadout, the forge ledger and the
    /// save all see one more id, and nothing learns a second way to describe an
    /// item. That is why a save needs no new field for this feature.
    /// </summary>
    public sealed class PrefixDefinition
    {
        public string Id = "";

        /// <summary>Latin name prepended to the piece's own, e.g. "Emberforged".</summary>
        public string DisplayName = "";

        /// <summary>One Arabic line for the documents, not drawn by the game.</summary>
        public string Gloss = "";

        /// <summary>The stats the affix adds on top of the piece's own lines.</summary>
        public StatModifier[] Modifiers = Array.Empty<StatModifier>();

        /// <summary>Relative roll weight. Higher is more common.</summary>
        public float Weight = 1f;

        /// <summary>The id of this prefix applied to a base item.</summary>
        public string VariantId(string baseItemId)
        {
            return Id + PrefixTuning.VariantSeparator + baseItemId;
        }
    }

    /// <summary>
    /// The prefix roll's draft numbers (plan section 3.3). Tuned in the economy
    /// slice, like the salvage yields and forge costs.
    /// </summary>
    public static class PrefixTuning
    {
        /// <summary>Chance that an eligible gear drop comes out prefixed.</summary>
        public const float Chance = 0.12f;

        /// <summary>What separates prefix and base item in a variant id.</summary>
        public const string VariantSeparator = "+";

        /// <summary>
        /// One tier up - never into Mythic. That tier is the story's, and a loot
        /// roll must not mint it; the top ordinary tier cannot climb further.
        /// Dismantling reads the tier, so a prefixed piece is worth more than
        /// its plain sibling by the same table as everything else.
        /// </summary>
        public static ItemRarity Bumped(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common: return ItemRarity.Uncommon;
                case ItemRarity.Uncommon: return ItemRarity.Rare;
                case ItemRarity.Rare: return ItemRarity.Eclipse;
                default: return rarity;
            }
        }
    }
}
