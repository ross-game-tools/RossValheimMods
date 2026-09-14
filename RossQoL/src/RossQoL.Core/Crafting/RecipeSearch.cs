using System;
using System.Text;

namespace RossQoL.Core.Crafting
{
    /// <summary>
    /// Whether a recipe's displayed name matches the crafting search box.
    /// Case- and space-insensitive, the same as vanilla's build menu search,
    /// so "bronzeaxe" and "Bronze Axe" both find the Bronze axe.
    /// </summary>
    public static class RecipeSearch
    {
        public static bool IsActive(string term) => Normalize(term).Length > 0;

        /// <param name="displayName">The localized name as shown in the list.</param>
        public static bool Matches(string displayName, string term)
        {
            string needle = Normalize(term);
            if (needle.Length == 0) return true;
            if (displayName == null) return false;

            return Normalize(displayName).IndexOf(needle, StringComparison.Ordinal) >= 0;
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                if (!char.IsWhiteSpace(c))
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }
    }
}
