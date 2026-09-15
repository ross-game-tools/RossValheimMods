using RossQoL.Core.Crafting;
using Xunit;

namespace RossQoL.Core.Tests.Crafting
{
    public class RecipeCategoriesTests
    {
        [Theory]
        [InlineData("Helmet", "helmet")]
        [InlineData("Chest", "chest")]
        [InlineData("Legs", "legs")]
        [InlineData("Shoulder", "cape")]
        [InlineData("Shield", "shield")]
        [InlineData("Utility", "utility")]
        [InlineData("Tool", "tool")]
        [InlineData("Torch", "torch")]
        [InlineData("Ammo", "ammo")]
        [InlineData("AmmoNonEquipable", "ammo")]
        [InlineData("Consumable", "food")]
        [InlineData("Material", "material")]
        [InlineData("Trinket", "trinket")]
        [InlineData("OneHandedWeapon", "weapon")]
        [InlineData("TwoHandedWeapon", "weapon")]
        [InlineData("TwoHandedWeaponLeft", "weapon")]
        [InlineData("Bow", "weapon")]
        public void Each_item_type_has_its_word(string itemType, string word)
        {
            Assert.Contains(word, RecipeCategories.WordsFor(itemType));
        }

        [Theory]
        [InlineData("Helmet")]
        [InlineData("Chest")]
        [InlineData("Legs")]
        [InlineData("Shoulder")]
        public void Armor_slots_are_armor(string itemType)
        {
            Assert.Contains("armor", RecipeCategories.WordsFor(itemType));
        }

        [Theory]
        [InlineData("Trophy")]
        [InlineData("Misc")]
        [InlineData("SomethingNew")]
        [InlineData(null)]
        public void Other_types_have_no_words(string itemType)
        {
            Assert.Empty(RecipeCategories.WordsFor(itemType));
        }

        [Theory]
        [InlineData("OneHandedWeapon", true)]
        [InlineData("Bow", true)]
        [InlineData("Tool", true)]
        [InlineData("Helmet", false)]
        [InlineData("Shield", false)]
        public void Skill_words_only_for_weapons_bows_and_tools(string itemType, bool expected)
        {
            Assert.Equal(expected, RecipeCategories.HasSkillWord(itemType));
        }

        [Fact]
        public void Search_matches_a_category_word_when_the_name_does_not()
        {
            Assert.True(RecipeSearch.Matches("Troll leather helmet", new[] { "helmet", "armor" }, "armor"));
            Assert.True(RecipeSearch.Matches("Bronze axe", new[] { "weapon", "Axes" }, "axes"));
        }

        [Fact]
        public void Search_still_matches_by_name_with_category_words()
        {
            Assert.True(RecipeSearch.Matches("Bronze axe", new[] { "weapon" }, "bronze"));
        }

        [Fact]
        public void Search_rejects_when_neither_name_nor_words_match()
        {
            Assert.False(RecipeSearch.Matches("Bronze axe", new[] { "weapon", "Axes" }, "armor"));
            Assert.False(RecipeSearch.Matches("Bronze axe", null, "armor"));
        }

        [Fact]
        public void Category_words_ignore_case_and_spaces()
        {
            Assert.True(RecipeSearch.Matches("Staff of embers", new[] { "Elemental magic" }, "elementalMAGIC"));
        }
    }
}
