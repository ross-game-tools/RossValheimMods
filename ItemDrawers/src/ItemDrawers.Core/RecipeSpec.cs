using System;
using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>
    /// One parsed build cost, plus the reason it failed to parse if it did.
    ///
    /// A failure carries a message rather than throwing because the caller is
    /// reading a config file a player edited: the useful response is to log
    /// what is wrong with their line and keep the default recipe, not to take
    /// the tier -- or the whole mod -- down over a typo.
    /// </summary>
    public sealed class RecipeSpec
    {
        public IReadOnlyList<(string Item, int Amount)> Requirements { get; }
        public string Error { get; }
        public bool Ok => Error == null;

        private RecipeSpec(IReadOnlyList<(string, int)> requirements, string error)
        {
            Requirements = requirements;
            Error = error;
        }

        /// <summary>
        /// Parses "Item:Count, Item:Count" -- the same shape other Valheim
        /// mods use for recipe overrides, so it is one less thing for a
        /// server admin to look up.
        ///
        /// Item names are PREFAB names (FineWood, BlackMarble), not display
        /// names, and are not validated here: Core has no access to the
        /// game's object database, and a name that does not resolve is the
        /// Game layer's problem to report at registration time. What this
        /// does guarantee is that anything it returns is structurally sound
        /// -- non-empty name, positive count, no duplicates -- so a caller
        /// never has to defend against a zero-count or repeated ingredient.
        /// </summary>
        public static RecipeSpec Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Fail("recipe is empty");

            var parsed = new List<(string, int)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawEntry in text.Split(','))
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0) continue;

                int colon = entry.IndexOf(':');
                if (colon <= 0)
                    return Fail($"'{entry}' is not Item:Count");

                string item = entry.Substring(0, colon).Trim();
                string countText = entry.Substring(colon + 1).Trim();

                if (item.Length == 0)
                    return Fail($"'{entry}' has no item name");

                if (!int.TryParse(countText, out int count))
                    return Fail($"'{entry}' has a non-numeric count");

                // Zero is rejected as well as negative. A zero-count
                // ingredient is almost certainly a mistake, and Valheim
                // renders it as a requirement you can never satisfy or one
                // that silently vanishes depending on the screen -- better
                // to say so than to build it.
                if (count <= 0)
                    return Fail($"'{entry}' must have a count above zero");

                if (!seen.Add(item))
                    return Fail($"'{item}' is listed more than once");

                parsed.Add((item, count));
            }

            if (parsed.Count == 0)
                return Fail("recipe is empty");

            return new RecipeSpec(parsed, null);
        }

        /// <summary>Renders requirements back to the config's own format.</summary>
        public static string Format(IReadOnlyList<(string Item, int Amount)> requirements)
        {
            var parts = new List<string>(requirements.Count);
            foreach (var r in requirements) parts.Add($"{r.Item}:{r.Amount}");
            return string.Join(",", parts);
        }

        private static RecipeSpec Fail(string error) =>
            new RecipeSpec(Array.Empty<(string, int)>(), error);
    }
}
