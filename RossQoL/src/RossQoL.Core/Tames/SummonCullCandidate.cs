namespace RossQoL.Core.Tames
{
    /// <summary>
    /// One creature counted against the summon cap, reduced to the two things
    /// the culling order asks about: how hurt it is, and how long it has been
    /// alive.
    /// </summary>
    public readonly struct SummonCullCandidate
    {
        public SummonCullCandidate(float health, float maxHealth, double secondsSinceSpawned)
        {
            Health = health;
            MaxHealth = maxHealth;
            SecondsSinceSpawned = secondsSinceSpawned;
        }

        public float Health { get; }

        public float MaxHealth { get; }

        public double SecondsSinceSpawned { get; }

        /// <summary>
        /// Current health over maximum, clamped to 0..1.
        ///
        /// A fraction rather than raw health because a starred creature has a
        /// bigger pool: a starred skeleton on half health has more hit points
        /// left than an unstarred one at full, and the one worth replacing is
        /// the wounded one whatever its tier.
        ///
        /// A max of zero or less is a creature whose health the game has not
        /// told us about, not a creature on its last legs. Reading it as 0
        /// would make it the first thing culled every time, so it reads as
        /// full: nothing singles it out, and the tie-break below falls back to
        /// vanilla's own oldest-first rule for it.
        /// </summary>
        public float HealthFraction
        {
            get
            {
                if (MaxHealth <= 0f) return 1f;

                float fraction = Health / MaxHealth;
                if (fraction < 0f) return 0f;
                if (fraction > 1f) return 1f;
                return fraction;
            }
        }
    }
}
