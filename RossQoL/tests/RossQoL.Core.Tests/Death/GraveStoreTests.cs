using System.Linq;
using RossQoL.Core.Death;
using Xunit;

namespace RossQoL.Core.Tests.Death
{
    public class GraveStoreTests
    {
        private static GraveRecord At(long world, float x, uint id) =>
            new GraveRecord(world, x, 0f, 0f, 1, 1, 1L, id);

        private static string Store(params GraveRecord[] records)
        {
            string stored = "";
            foreach (var record in records)
                stored = GraveStore.Add(stored, record, GraveStore.DefaultPerWorld, GraveStore.DefaultTotal);
            return stored;
        }

        [Fact]
        public void The_newest_grave_comes_first()
        {
            string stored = Store(At(1, 10f, 1), At(1, 20f, 2), At(1, 30f, 3));

            Assert.Equal(new uint[] { 3, 2, 1 }, GraveStore.Parse(stored).Select(g => g.ZdoId));
        }

        [Fact]
        public void Only_this_worlds_graves_come_back()
        {
            string stored = Store(At(1, 10f, 1), At(2, 20f, 2), At(1, 30f, 3));

            Assert.Equal(new uint[] { 3, 1 }, GraveStore.ForWorld(stored, 1).Select(g => g.ZdoId));
            Assert.Equal(new uint[] { 2 }, GraveStore.ForWorld(stored, 2).Select(g => g.ZdoId));
            Assert.Empty(GraveStore.ForWorld(stored, 3));
        }

        [Fact]
        public void A_world_keeps_only_its_newest_graves()
        {
            string stored = "";
            for (uint i = 1; i <= 8; i++)
                stored = GraveStore.Add(stored, At(1, i, i), perWorld: 5, total: 20);

            Assert.Equal(new uint[] { 8, 7, 6, 5, 4 }, GraveStore.ForWorld(stored, 1).Select(g => g.ZdoId));
        }

        [Fact]
        public void Evicting_one_world_leaves_the_others_alone()
        {
            string stored = GraveStore.Add("", At(2, 1f, 99), perWorld: 2, total: 20);
            for (uint i = 1; i <= 4; i++)
                stored = GraveStore.Add(stored, At(1, i, i), perWorld: 2, total: 20);

            Assert.Equal(new uint[] { 4, 3 }, GraveStore.ForWorld(stored, 1).Select(g => g.ZdoId));
            Assert.Equal(new uint[] { 99 }, GraveStore.ForWorld(stored, 2).Select(g => g.ZdoId));
        }

        [Fact]
        public void The_total_cap_drops_the_oldest_wherever_it_is()
        {
            string stored = "";
            for (uint i = 1; i <= 4; i++)
                stored = GraveStore.Add(stored, At(i, i, i), perWorld: 5, total: 3);

            Assert.Equal(new uint[] { 4, 3, 2 }, GraveStore.Parse(stored).Select(g => g.ZdoId));
        }

        [Fact]
        public void A_grave_can_be_removed_by_its_tombstone()
        {
            string stored = Store(At(1, 10f, 1), At(1, 20f, 2));

            string after = GraveStore.Remove(stored, worldId: 1, zdoId: 1);

            Assert.Equal(new uint[] { 2 }, GraveStore.Parse(after).Select(g => g.ZdoId));
            Assert.Equal(after, GraveStore.Remove(after, worldId: 1, zdoId: 404));
        }

        [Fact]
        public void A_corrupt_line_is_dropped_and_the_rest_survive()
        {
            string stored = Store(At(1, 10f, 1), At(1, 20f, 2));
            string damaged = "utter nonsense\n" + stored;

            Assert.Equal(new uint[] { 2, 1 }, GraveStore.Parse(damaged).Select(g => g.ZdoId));
        }

        [Fact]
        public void An_empty_store_is_empty_rather_than_broken()
        {
            Assert.Empty(GraveStore.Parse(null));
            Assert.Empty(GraveStore.Parse(""));
            Assert.Empty(GraveStore.ForWorld(null, 1));
            Assert.Equal("", GraveStore.Remove(null, 1, 1));
        }
    }
}
