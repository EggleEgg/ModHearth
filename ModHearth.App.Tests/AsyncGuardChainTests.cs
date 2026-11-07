using ModHearth.UI.Helpers;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Pins the ordering/veto behaviour of the chain that gates application restarts. Adding a new
    /// restart precondition is just another <c>Register</c> call, so the chain must short-circuit
    /// deterministically and never run a later guard after one vetoes.
    /// </summary>
    public class AsyncGuardChainTests
    {
        [Fact]
        public async Task RunsGuardsInRegistrationOrderAndAllowsWhenEveryGuardPasses()
        {
            AsyncGuardChain chain = new();
            List<int> order = [];

            chain.Register(() => Record(order, 1, allow: true));
            chain.Register(() => Record(order, 2, allow: true));
            chain.Register(() => Record(order, 3, allow: true));

            Assert.True(await chain.RunAsync());
            Assert.Equal([1, 2, 3], order);
        }

        [Fact]
        public async Task StopsAtFirstVeto()
        {
            AsyncGuardChain chain = new();
            List<int> order = [];

            chain.Register(() => Record(order, 1, allow: true));
            chain.Register(() => Record(order, 2, allow: false));
            chain.Register(() => Record(order, 3, allow: true));

            Assert.False(await chain.RunAsync());
            Assert.Equal([1, 2], order);
        }

        [Fact]
        public async Task EmptyChainAllows()
        {
            AsyncGuardChain chain = new();

            Assert.True(await chain.RunAsync());
        }

        [Fact]
        public void RegisterRejectsNull()
        {
            AsyncGuardChain chain = new();

            _ = Assert.Throws<ArgumentNullException>(() => chain.Register(null!));
        }

        private static Task<bool> Record(List<int> order, int id, bool allow)
        {
            order.Add(id);
            return Task.FromResult(allow);
        }
    }
}
