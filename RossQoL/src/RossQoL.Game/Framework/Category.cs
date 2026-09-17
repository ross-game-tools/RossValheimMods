using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using RossQoL.Core.Framework;

namespace RossQoL.Game.Framework
{
    /// <summary>
    /// A group of features by what they modify. Names one config section and
    /// its master switch.
    /// </summary>
    internal sealed class Category
    {
        public string Section { get; }
        public string Description { get; }
        public IReadOnlyList<Feature> Features { get; }
        public ConfigEntry<bool> Enabled { get; private set; }

        public Category(string section, string description, params Feature[] features)
        {
            Section = section;
            Description = description;
            Features = features;
            foreach (var feature in features) feature.Category = this;
        }

        public FeatureScope Scope => FeatureRules.CategoryScope(Features.Select(f => f.Scope));

        public void Bind(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                ConfigText.Description(Description, Scope,
                    requiresRestart: false, turningOnRequiresRestart: Scope == FeatureScope.Client));

            foreach (var feature in Features)
            {
                feature.Toggle = config.Bind(Section, feature.Key, true,
                    ConfigText.Description(feature.Description, feature.Scope,
                        requiresRestart: false, turningOnRequiresRestart: feature.Scope == FeatureScope.Client));

                // One feature's settings must not take the mod down with them.
                // Binding a setting whose name is already taken in this section
                // -- the feature's own toggle, most easily -- throws a cast
                // error, and without this that error escaped Awake and no
                // feature loaded at all.
                try
                {
                    feature.BindSettings(config, Section);
                }
                catch (System.Exception ex)
                {
                    RossQoLPlugin.Log.LogError(
                        $"{feature.Name}: its settings could not be bound, so it stays off and everything "
                        + $"else loads as normal. This is a bug in the feature, not in your config: {ex}");
                    feature.Toggle.Value = false;
                }
            }
        }
    }
}
