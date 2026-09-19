using System.Collections.Generic;
using RossQoL.Core.Tames;
using Xunit;

namespace RossQoL.Core.Tests.Tames
{
    public class SummonCullOrderTests
    {
        private static SummonCullCandidate Candidate(float health, float maxHealth, double age) =>
            new SummonCullCandidate(health, maxHealth, age);

        [Fact]
        public void The_worst_wounded_is_despawned_first()
        {
            var candidates = new List<SummonCullCandidate>
            {
                Candidate(100f, 100f, age: 300d),
                Candidate(20f, 100f, age: 10d),
                Candidate(60f, 100f, age: 200d),
            };

            Assert.Equal(new[] { 1, 2, 0 }, SummonCullOrder.Order(candidates));
        }

        [Fact]
        public void Equal_wounds_fall_to_the_oldest()
        {
            var candidates = new List<SummonCullCandidate>
            {
                Candidate(50f, 100f, age: 5d),
                Candidate(50f, 100f, age: 500d),
                Candidate(50f, 100f, age: 50d),
            };

            Assert.Equal(new[] { 1, 2, 0 }, SummonCullOrder.Order(candidates));
        }

        [Fact]
        public void A_starred_creature_with_more_health_left_still_outlives_a_weaker_one()
        {
            // 200 hit points out of 400 is more absolute health than 90 out of
            // 100, and it is still the one kept: the rule is the fraction, so
            // the nearly-untouched creature survives whatever its tier.
            var starredAndHalfDead = Candidate(200f, 400f, age: 10d);
            var plainAndBarelyScratched = Candidate(90f, 100f, age: 10d);

            var candidates = new List<SummonCullCandidate> { plainAndBarelyScratched, starredAndHalfDead };

            Assert.Equal(new[] { 1, 0 }, SummonCullOrder.Order(candidates));
        }

        [Fact]
        public void A_creature_at_full_health_is_despawned_last()
        {
            var candidates = new List<SummonCullCandidate>
            {
                Candidate(100f, 100f, age: 1d),
                Candidate(99f, 100f, age: 1000d),
            };

            Assert.Equal(new[] { 1, 0 }, SummonCullOrder.Order(candidates));
        }

        [Fact]
        public void All_at_full_health_is_vanillas_own_oldest_first_order()
        {
            var candidates = new List<SummonCullCandidate>
            {
                Candidate(100f, 100f, age: 10d),
                Candidate(400f, 400f, age: 30d),
                Candidate(100f, 100f, age: 20d),
            };

            Assert.Equal(new[] { 1, 2, 0 }, SummonCullOrder.Order(candidates));
        }

        [Fact]
        public void A_creature_with_no_max_health_reads_as_unwounded()
        {
            // Zero max health means the game has not told us how hurt it is,
            // not that it is nearly dead. Reading it as empty would make it
            // the first thing culled every single time.
            var unknown = Candidate(0f, 0f, age: 1d);
            var wounded = Candidate(10f, 100f, age: 1000d);

            Assert.Equal(1f, unknown.HealthFraction);
            Assert.Equal(new[] { 1, 0 }, SummonCullOrder.Order(new List<SummonCullCandidate> { unknown, wounded }));
        }

        [Fact]
        public void A_negative_max_health_is_treated_the_same_way()
        {
            Assert.Equal(1f, Candidate(5f, -100f, age: 1d).HealthFraction);
        }

        [Fact]
        public void Health_above_or_below_the_maximum_is_clamped()
        {
            Assert.Equal(1f, Candidate(150f, 100f, age: 0d).HealthFraction);
            Assert.Equal(0f, Candidate(-5f, 100f, age: 0d).HealthFraction);
        }

        [Fact]
        public void An_empty_list_orders_nothing()
        {
            Assert.Empty(SummonCullOrder.Order(new List<SummonCullCandidate>()));
        }

        [Fact]
        public void A_null_list_orders_nothing()
        {
            Assert.Empty(SummonCullOrder.Order(null));
        }

        [Fact]
        public void A_single_candidate_is_its_own_order()
        {
            Assert.Equal(new[] { 0 }, SummonCullOrder.Order(new List<SummonCullCandidate> { Candidate(1f, 100f, age: 1d) }));
        }

        [Fact]
        public void Comparing_a_candidate_with_itself_is_a_tie()
        {
            var candidate = Candidate(37f, 100f, age: 42d);

            Assert.Equal(0, SummonCullOrder.Compare(candidate, candidate));
        }

        [Fact]
        public void The_comparison_is_antisymmetric()
        {
            // The sort is only safe to run if the order is total: whichever way
            // round two creatures arrive, the same one is despawned.
            var wounded = Candidate(10f, 100f, age: 5d);
            var healthy = Candidate(90f, 100f, age: 500d);

            Assert.True(SummonCullOrder.Compare(wounded, healthy) < 0);
            Assert.True(SummonCullOrder.Compare(healthy, wounded) > 0);
        }

        [Fact]
        public void Creatures_identical_in_wound_and_age_keep_the_order_they_arrived_in()
        {
            // Two skeletons raised by the same cast are indistinguishable on
            // both counts. The order must still be the same every call, or the
            // cap would pick a different one each time it ran.
            var twin = Candidate(100f, 100f, age: 7d);
            var candidates = new List<SummonCullCandidate> { twin, twin, twin };

            Assert.Equal(new[] { 0, 1, 2 }, SummonCullOrder.Order(candidates));
            Assert.Equal(SummonCullOrder.Order(candidates), SummonCullOrder.Order(candidates));
        }
    }
}
