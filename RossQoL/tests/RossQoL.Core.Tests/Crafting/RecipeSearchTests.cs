using RossQoL.Core.Crafting;
using Xunit;

namespace RossQoL.Core.Tests.Crafting
{
    public class RecipeSearchTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Empty_search_matches_everything(string term)
        {
            Assert.True(RecipeSearch.Matches("Bronze axe", term));
            Assert.True(RecipeSearch.Matches(null, term));
        }

        [Fact]
        public void Matches_a_substring_anywhere_in_the_name()
        {
            Assert.True(RecipeSearch.Matches("Bronze axe", "axe"));
            Assert.True(RecipeSearch.Matches("Bronze axe", "onz"));
        }

        [Fact]
        public void Ignores_case()
        {
            Assert.True(RecipeSearch.Matches("Bronze axe", "BRONZE"));
            Assert.True(RecipeSearch.Matches("BRONZE AXE", "bronze"));
        }

        [Fact]
        public void Ignores_spaces_like_the_build_menu_search()
        {
            Assert.True(RecipeSearch.Matches("Bronze axe", "bronzeaxe"));
            Assert.True(RecipeSearch.Matches("Bronze axe", " bronze  axe "));
        }

        [Fact]
        public void Rejects_a_name_that_does_not_contain_the_term()
        {
            Assert.False(RecipeSearch.Matches("Bronze axe", "iron"));
        }

        [Fact]
        public void A_missing_name_matches_only_an_empty_search()
        {
            Assert.False(RecipeSearch.Matches(null, "axe"));
        }

        [Fact]
        public void Matches_non_latin_names()
        {
            Assert.True(RecipeSearch.Matches("Бронзовый топор", "ТОПОР"));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("  ", false)]
        [InlineData("a", true)]
        public void IsActive_only_for_a_term_with_content(string term, bool expected)
        {
            Assert.Equal(expected, RecipeSearch.IsActive(term));
        }
    }
}
