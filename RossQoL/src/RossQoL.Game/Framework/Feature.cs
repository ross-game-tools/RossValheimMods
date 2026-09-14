using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using UnityEngine;

namespace RossQoL.Game.Framework
{
    /// <summary>
    /// One switchable tweak. Owns its config entries, its patch classes and
    /// the list of Valheim members it depends on.
    /// </summary>
    internal abstract class Feature
    {
        /// <summary>Config key of the feature's own toggle, e.g. "TamesFollow".</summary>
        public abstract string Key { get; }

        public abstract FeatureScope Scope { get; }

        public abstract string Description { get; }

        /// <summary>[HarmonyPatch] classes applied when the feature is patched.</summary>
        public abstract IEnumerable<Type> PatchClasses { get; }

        public virtual IEnumerable<CompatMember> RequiredMembers => Array.Empty<CompatMember>();

        /// <summary>Entries beyond the toggle, bound in the category's section.</summary>
        public virtual void BindSettings(ConfigFile config, string section)
        {
        }

        /// <summary>Called once after patching, e.g. to add a component to the shared host.</summary>
        public virtual void OnActivated(GameObject host)
        {
        }

        internal Category Category { get; set; }

        internal ConfigEntry<bool> Toggle { get; set; }

        public string Name => $"{Category?.Section}/{Key}";

        /// <summary>Read by patches on every call; cheap.</summary>
        public bool IsActive =>
            Category?.Enabled != null && Toggle != null
            && FeatureRules.IsActive(Category.Enabled.Value, Toggle.Value);
    }
}
