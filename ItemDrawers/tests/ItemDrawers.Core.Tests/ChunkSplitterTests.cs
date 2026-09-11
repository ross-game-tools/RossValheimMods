using System.Linq;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class ChunkSplitterTests
    {
        private const int Stack = 50;

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(Stack - 1)]
        [InlineData(Stack)]
        [InlineData(Stack + 1)]
        [InlineData(2 * Stack)]
        [InlineData(2 * Stack + 1)]
        public void Chunks_sum_to_the_amount_and_none_exceeds_the_stack_size(int amount)
        {
            var chunks = ChunkSplitter.Chunks(amount, Stack);

            Assert.Equal(amount, chunks.Sum());
            Assert.All(chunks, c => Assert.InRange(c, 1, Stack));
        }

        [Fact]
        public void Zero_amount_yields_no_chunks()
        {
            Assert.Empty(ChunkSplitter.Chunks(0, Stack));
        }

        [Fact]
        public void Negative_amount_yields_no_chunks()
        {
            Assert.Empty(ChunkSplitter.Chunks(-5, Stack));
        }

        [Fact]
        public void Nonsensical_stack_size_is_treated_as_one()
        {
            var chunks = ChunkSplitter.Chunks(3, 0);

            Assert.Equal(new[] { 1, 1, 1 }, chunks);
        }
    }
}
