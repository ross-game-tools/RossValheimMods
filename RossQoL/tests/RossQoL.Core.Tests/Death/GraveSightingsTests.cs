using RossQoL.Core.Death;
using Xunit;

namespace RossQoL.Core.Tests.Death
{
    public class GraveSightingsTests
    {
        [Fact]
        public void A_tombstone_is_unseen_until_it_is_observed()
        {
            var sightings = new GraveSightings();
            sightings.EnterWorld(7L);

            Assert.False(sightings.HasSeen(42u));

            sightings.Observe(42u);

            Assert.True(sightings.HasSeen(42u));
            Assert.False(sightings.HasSeen(43u));
        }

        [Fact]
        public void Staying_in_one_world_keeps_every_sighting()
        {
            var sightings = new GraveSightings();
            sightings.EnterWorld(7L);
            sightings.Observe(42u);

            Assert.False(sightings.EnterWorld(7L));
            Assert.True(sightings.HasSeen(42u));
        }

        [Fact]
        public void Changing_world_drops_them()
        {
            var sightings = new GraveSightings();
            sightings.EnterWorld(7L);
            sightings.Observe(42u);

            Assert.True(sightings.EnterWorld(8L));
            Assert.False(sightings.HasSeen(42u));
            Assert.Equal(0, sightings.Count);
        }

        [Fact]
        public void Leaving_to_the_menu_and_rejoining_the_same_world_re_arms_the_rule()
        {
            // The regression this type exists to stop: the watcher lives on a
            // DontDestroyOnLoad object, so without the trip through "no world"
            // a rejoin would keep last session's sightings and read a
            // not-yet-streamed ZDO as a looted grave.
            var sightings = new GraveSightings();
            sightings.EnterWorld(7L);
            sightings.Observe(42u);

            Assert.True(sightings.EnterWorld(0L));   // back to the main menu
            Assert.True(sightings.EnterWorld(7L));   // same world, new session

            Assert.False(sightings.HasSeen(42u));
        }

        [Fact]
        public void A_fresh_set_holds_nothing_even_before_a_world_is_entered()
        {
            var sightings = new GraveSightings();

            Assert.False(sightings.HasSeen(42u));
            Assert.Equal(0, sightings.Count);
            Assert.False(sightings.EnterWorld(0L)); // already "no world"
        }
    }
}
