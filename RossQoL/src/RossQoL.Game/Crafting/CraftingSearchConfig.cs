using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// RecipeSearch's settings beyond its toggle. Read live, so no restart.
    /// </summary>
    public static class CraftingSearchConfig
    {
        public static ConfigEntry<bool> SearchAutoFocus;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            SearchAutoFocus = config.Bind(section, "SearchAutoFocus", true,
                ConfigText.Description(
                    "Put the cursor in the search box when you open a crafting station, so you can type straight away. "
                    + "While the box has the cursor, E and Tab type letters instead of closing the panel: "
                    + "press Enter or click elsewhere first, or close with Esc. "
                    + "Never applies to the plain inventory or when playing with a gamepad.",
                    scope, requiresRestart: false));
        }
    }
}
