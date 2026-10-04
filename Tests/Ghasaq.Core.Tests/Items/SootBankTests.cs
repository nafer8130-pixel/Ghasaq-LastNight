using Ghasaq.Core.Items;
using Xunit;

namespace Ghasaq.Core.Tests.Items
{
    /// <summary>
    /// The bank itself: accumulation, refusal of non-positive deposits, and the
    /// saturation that stops an overflow from turning savings negative.
    /// </summary>
    public class SootBankTests
    {
        [Fact]
        public void Deposits_Accumulate()
        {
            var bank = new SootBank();
            Assert.Equal(0, bank.Balance);

            Assert.Equal(15, bank.Deposit(15));
            Assert.Equal(40, bank.Deposit(40));
            Assert.Equal(55, bank.Balance);
        }

        [Fact]
        public void NonPositiveDeposits_AreIgnored()
        {
            var bank = new SootBank();
            bank.LoadFrom(20);

            Assert.Equal(0, bank.Deposit(0));
            Assert.Equal(0, bank.Deposit(-5));
            Assert.Equal(20, bank.Balance);
        }

        [Fact]
        public void TheBalance_SaturatesInsteadOfWrapping()
        {
            var bank = new SootBank();
            bank.LoadFrom(int.MaxValue - 3);

            Assert.Equal(3, bank.Deposit(100));
            Assert.Equal(int.MaxValue, bank.Balance);
            Assert.Equal(0, bank.Deposit(1));
        }

        [Fact]
        public void LoadFrom_ClampsANegativeValue()
        {
            var bank = new SootBank();
            bank.LoadFrom(30);
            bank.LoadFrom(-1);

            Assert.Equal(0, bank.Balance);
        }
    }
}
