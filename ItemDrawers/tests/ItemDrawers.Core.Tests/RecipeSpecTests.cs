using ItemDrawers.Core;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class RecipeSpecTests
    {
        [Fact]
        public void Parses_a_single_requirement()
        {
            var spec = RecipeSpec.Parse("FineWood:10");
            Assert.True(spec.Ok);
            Assert.Equal(new[] { ("FineWood", 10) }, spec.Requirements);
        }

        [Fact]
        public void Parses_several_requirements_in_order()
        {
            var spec = RecipeSpec.Parse("FineWood:5,Stone:10");
            Assert.True(spec.Ok);
            Assert.Equal(new[] { ("FineWood", 5), ("Stone", 10) }, spec.Requirements);
        }

        [Fact]
        public void Tolerates_the_spacing_a_person_would_actually_type()
        {
            var spec = RecipeSpec.Parse("  FineWood : 5 ,  BlackMarble:10  ");
            Assert.True(spec.Ok);
            Assert.Equal(new[] { ("FineWood", 5), ("BlackMarble", 10) }, spec.Requirements);
        }

        [Fact]
        public void Ignores_a_trailing_comma()
        {
            var spec = RecipeSpec.Parse("FineWood:10,");
            Assert.True(spec.Ok);
            Assert.Single(spec.Requirements);
        }

        // Each of these is a real thing a config file can contain, and each
        // must be REJECTED rather than silently reinterpreted -- a recipe
        // that quietly becomes something other than what was typed is worse
        // than one that refuses and says why.
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("FineWood")]            // no count
        [InlineData("FineWood:")]           // empty count
        [InlineData(":10")]                 // no item
        [InlineData("FineWood:abc")]        // non-numeric
        [InlineData("FineWood:0")]          // zero
        [InlineData("FineWood:-5")]         // negative
        [InlineData("FineWood:5,FineWood:3")] // duplicate ingredient
        public void Rejects_malformed_input(string text)
        {
            var spec = RecipeSpec.Parse(text);
            Assert.False(spec.Ok);
            Assert.False(string.IsNullOrWhiteSpace(spec.Error));
        }

        [Fact]
        public void Rejects_a_duplicate_regardless_of_case()
        {
            // Valheim prefab lookups are case-sensitive, but a config author
            // writing "finewood" twice meant one ingredient either way, and
            // accepting it would produce a recipe with two entries that the
            // game then resolves inconsistently.
            var spec = RecipeSpec.Parse("FineWood:5,finewood:3");
            Assert.False(spec.Ok);
        }

        [Fact]
        public void Format_round_trips_through_Parse()
        {
            var original = new[] { ("FineWood", 5), ("BlackMarble", 10) };
            var spec = RecipeSpec.Parse(RecipeSpec.Format(original));
            Assert.True(spec.Ok);
            Assert.Equal(original, spec.Requirements);
        }

        [Fact]
        public void Every_shipped_default_recipe_parses()
        {
            // The defaults are written into the config file as text, so a
            // default that did not survive its own parser would hand every
            // new install a broken recipe.
            foreach (var tier in new[] { "FineWood:10", "FineWood:5,Stone:10", "FineWood:5,BlackMarble:10" })
                Assert.True(RecipeSpec.Parse(tier).Ok, tier);
        }

        [Fact]
        public void A_failed_parse_yields_no_requirements()
        {
            // Callers fall back to the default recipe on failure; a partial
            // list here would let half a parsed recipe reach the game.
            var spec = RecipeSpec.Parse("FineWood:5,Stone:bad");
            Assert.False(spec.Ok);
            Assert.Empty(spec.Requirements);
        }
    }
}
