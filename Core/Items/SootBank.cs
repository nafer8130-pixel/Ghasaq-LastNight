namespace Ghasaq.Core.Items
{
    /// <summary>
    /// The banked السُّخام / Soot balance: the upgrade currency that dismantling
    /// gear at the Hearth leaves behind (plan sections 3.3 and 3.7).
    ///
    /// Deliberately separate from the in-run meter in Core/Combat/Soot.cs. The
    /// meter prices one fight and dies with it; this balance never decays, rides
    /// a save, and is what the Hearth's forge will spend in a later slice.
    /// Merging the two would make a fight's residue and a character's savings the
    /// same number, and neither could be tuned without moving the other.
    /// </summary>
    public sealed class SootBank
    {
        /// <summary>Soot currently banked. Never negative.</summary>
        public int Balance { get; private set; }

        /// <summary>
        /// Banks an amount and returns how much was accepted.
        ///
        /// Non-positive deposits are ignored, and the balance saturates instead
        /// of wrapping: an add that overflowed int would otherwise turn a long
        /// game's savings into a negative number.
        /// </summary>
        public int Deposit(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int room = int.MaxValue - Balance;
            int accepted = amount < room ? amount : room;

            if (accepted <= 0)
            {
                return 0;
            }

            Balance += accepted;
            return accepted;
        }

        /// <summary>
        /// Pays a price all or nothing: the balance moves only when it can
        /// cover the whole cost, so a refused purchase never leaves the bank
        /// short and the piece un-bought.
        /// </summary>
        public bool TrySpend(int amount)
        {
            if (amount <= 0 || Balance < amount)
            {
                return false;
            }

            Balance -= amount;
            return true;
        }

        /// <summary>Restores a saved balance. A hand-edited negative value clamps to zero.</summary>
        public void LoadFrom(int balance)
        {
            Balance = balance < 0 ? 0 : balance;
        }
    }
}
