namespace Ghasaq.Core.Items
{
    /// <summary>
    /// The Hearth's dismantling numbers (plan section 3.3).
    ///
    /// One table keyed by rarity, rather than a value authored per item: every
    /// piece of gear the content ships is dismantlable the day it is authored,
    /// and a new rarity cannot silently yield nothing. The numbers are draft
    /// until the economy slice tunes them, like the Sigil and Soot numbers.
    /// </summary>
    public static class SalvageTuning
    {
        /// <summary>Soot paid for breaking down one piece of gear of this rarity.</summary>
        public static int SootFor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common: return 2;
                case ItemRarity.Uncommon: return 6;
                case ItemRarity.Rare: return 15;
                case ItemRarity.Eclipse: return 40;
                case ItemRarity.Mythic: return 80;
                default: return 2;
            }
        }
    }

    /// <summary>Why a piece of gear could not be dismantled, for the message the player sees.</summary>
    public enum SalvageFailure
    {
        None = 0,

        UnknownItem = 1,

        /// <summary>Not gear: materials and consumables stay whole.</summary>
        NotSalvageable = 2,

        /// <summary>Bound to the story by the same rule that keeps it out of a discard.</summary>
        Bound = 3,

        /// <summary>Nothing of it is in the bag; the equipped copy is not a bag copy.</summary>
        NotHeld = 4,

        /// <summary>The Hammer stands in the Hearth's camp, not out in the field.</summary>
        NotAtHearth = 5,

        /// <summary>Refused while hostiles stand; the bench is not a mid-fight act.</summary>
        InCombat = 6
    }
}
