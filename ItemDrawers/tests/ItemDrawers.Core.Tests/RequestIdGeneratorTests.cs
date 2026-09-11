using System;
using System.Collections.Generic;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class RequestIdGeneratorTests
    {
        [Fact]
        public void Next_returns_strictly_increasing_values()
        {
            var gen = new RequestIdGenerator(seed: 0);
            long a = gen.Next();
            long b = gen.Next();
            long c = gen.Next();

            Assert.True(a < b);
            Assert.True(b < c);
        }

        [Fact]
        public void Next_never_returns_the_seed_itself()
        {
            // The first id issued is seed+1, not the seed -- a seed of 0
            // (the implicit default for an unseeded counter) must not
            // produce 0 as a live request id, since 0 is easy to confuse
            // with "no request" in calling code.
            var gen = new RequestIdGenerator(seed: 0);
            Assert.Equal(1, gen.Next());
        }

        /// <summary>
        /// The actual invariant C3's fix depends on: DrawerManager holds
        /// exactly ONE RequestIdGenerator for the life of a client session,
        /// asked for ids by many different DrawerComponent instances over
        /// that session -- including many that are destroyed and recreated
        /// (a drawer walking out of render range and back) while this ONE
        /// generator persists, untouched, across every one of those
        /// rebuilds. Nothing here re-seeds the generator between simulated
        /// rebuilds, which is the fix itself, not merely the seed choice:
        /// a generator that WAS re-created (and therefore re-seeded) on
        /// every rebuild is exactly the pre-fix bug, and is deliberately
        /// not what this test models -- a test that constructed two fresh
        /// generators and showed they collide would be proving the bug
        /// existed, not that the fix holds.
        /// </summary>
        [Fact]
        public void A_single_generator_reused_across_many_simulated_component_rebuilds_never_repeats_an_id()
        {
            var sessionGenerator = new RequestIdGenerator(seed: DateTimeOffset.UtcNow.Ticks);
            var seen = new HashSet<long>();

            for (int rebuild = 0; rebuild < 50; rebuild++)
            {
                // Each simulated "rebuild" issues a few requests, as a real
                // DrawerComponent would across its lifetime before being
                // destroyed and replaced by a fresh instance that shares
                // this same DrawerManager-owned generator.
                for (int request = 0; request < 5; request++)
                {
                    long id = sessionGenerator.Next();
                    Assert.True(seen.Add(id), $"id {id} was reused after {rebuild} simulated component rebuilds");
                }
            }
        }

        [Fact]
        public void Generators_seeded_from_different_wall_clock_ticks_do_not_collide()
        {
            // Documents WHY seeding from DateTimeOffset.UtcNow.Ticks (as
            // DrawerManager does, once, at construction) is a safe choice
            // across separate client sessions -- distinct from the
            // per-session reuse invariant above, which is about NOT
            // re-seeding within one session.
            var sessionA = new RequestIdGenerator(seed: 1_000_000L);
            var sessionB = new RequestIdGenerator(seed: 1_000_500L);

            for (int i = 0; i < 500; i++)
                Assert.NotEqual(sessionA.Next(), sessionB.Next());
        }
    }
}
