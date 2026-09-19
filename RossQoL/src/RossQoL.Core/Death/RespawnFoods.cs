using System;
using System.Collections.Generic;
using RossQoL.Core.Progression;

namespace RossQoL.Core.Death
{
    /// <summary>
    /// Which food a respawn hands out, by how far the world has got.
    ///
    /// Two fallbacks matter. Within a tier, the first food the game actually
    /// has is used -- the best food of a tier is often one the player cannot
    /// cook yet, and a handout nobody has the station for is no handout. And a
    /// tier with nothing available drops to the tier below, so a world that
    /// has run ahead of its cooking still eats.
    /// </summary>
    public static class RespawnFoods
    {
        /// <summary>The default food table in format Tier:Food|Fallback|Fallback,Tier:... where names are prefab names.</summary>
        public const string DefaultTable =
            "Meadows:Honey|Raspberry|MushroomYellow,"
            + "BlackForest:CarrotSoup|QueensJam|Honey,"
            + "Swamp:TurnipStew|Sausages|CarrotSoup,"
            + "Mountain:Eyescream|OnionSoup|TurnipStew,"
            + "Plains:BloodPudding|Bread|Eyescream,"
            + "Mistlands:FishAndBread|MushroomOmelette|Salad,"
            + "Ashlands:RoastedCrustPie|ScorchingMedley|SpicyMarmalade,"
            + "DeepNorth:OatmealLingonberryJam|OatMilk|KaleChips";

        /// <summary>Which food a respawn hands out: the first entry of the tier the game actually has, falling back down the ladder when a tier has nothing available.</summary>
        /// <param name="prefabExists">Does the game know this item? ObjectDB in play, a stub in tests.</param>
        /// <returns>A prefab name, or null when nothing in or below the tier is available.</returns>
        public static string Pick(string table, string tier, Func<string, bool> prefabExists)
        {
            if (prefabExists == null) return null;

            var parsed = Parse(table);
            if (parsed.Count == 0) return null;

            var ladder = WorldFrontier.Tiers;
            int start = IndexOf(ladder, tier);

            // An unknown tier is treated as the bottom of the ladder: handing
            // out the meadows' berries is wrong in the safe direction.
            for (int i = start; i >= 0; i--)
            {
                if (!parsed.TryGetValue(ladder[i], out var foods)) continue;

                foreach (string food in foods)
                    if (prefabExists(food)) return food;
            }

            return null;
        }

        private static int IndexOf(IReadOnlyList<string> tiers, string tier)
        {
            for (int i = 0; i < tiers.Count; i++)
                if (string.Equals(tiers[i], tier, StringComparison.OrdinalIgnoreCase))
                    return i;

            return 0;
        }

        private static Dictionary<string, List<string>> Parse(string table)
        {
            var parsed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(table)) return parsed;

            foreach (string entry in table.Split(','))
            {
                string[] halves = entry.Split(':');
                if (halves.Length != 2) continue;

                string tier = halves[0].Trim();
                if (tier.Length == 0) continue;

                var foods = new List<string>();
                foreach (string food in halves[1].Split('|'))
                {
                    string trimmed = food.Trim();
                    if (trimmed.Length > 0) foods.Add(trimmed);
                }

                if (foods.Count > 0) parsed[tier] = foods;
            }

            return parsed;
        }
    }
}
