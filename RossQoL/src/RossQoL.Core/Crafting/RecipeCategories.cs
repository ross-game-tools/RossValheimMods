using System;

namespace RossQoL.Core.Crafting
{
    /// <summary>
    /// Category words a recipe can also be found by in the crafting search,
    /// from the crafted item's type (ItemDrop.ItemData.ItemType, passed by
    /// name so Core needs no game reference). English only; the weapon skill
    /// name is added separately by the caller, in the game's language.
    /// </summary>
    public static class RecipeCategories
    {
        private static readonly string[] None = Array.Empty<string>();

        public static string[] WordsFor(string itemType)
        {
            switch (itemType)
            {
                case "Helmet": return new[] { "helmet", "armor" };
                case "Chest": return new[] { "chest", "armor" };
                case "Legs": return new[] { "legs", "armor" };
                case "Shoulder": return new[] { "cape", "armor" };
                case "Shield": return new[] { "shield" };
                case "Utility": return new[] { "utility" };
                case "Tool": return new[] { "tool" };
                case "Torch": return new[] { "torch" };
                case "Ammo":
                case "AmmoNonEquipable": return new[] { "ammo" };
                case "Consumable": return new[] { "food" };
                case "Material": return new[] { "material" };
                case "Trinket": return new[] { "trinket" };
                case "OneHandedWeapon":
                case "TwoHandedWeapon":
                case "TwoHandedWeaponLeft":
                case "Bow": return new[] { "weapon" };
                default: return None;
            }
        }

        /// <summary>Whether items of this type have a weapon skill worth searching by (weapons, bows, tools).</summary>
        public static bool HasSkillWord(string itemType)
        {
            switch (itemType)
            {
                case "OneHandedWeapon":
                case "TwoHandedWeapon":
                case "TwoHandedWeaponLeft":
                case "Bow":
                case "Tool":
                    return true;
                default:
                    return false;
            }
        }
    }
}
