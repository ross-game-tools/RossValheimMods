using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Brewing inputs stack, so a run of mead does not cost a chest.
    ///
    /// Vanilla gives every mead base -- and the barley wine base, which is
    /// the same thing under another name -- a maximum stack of one, which is
    /// what makes brewing tedious to store; nothing else about them changes.
    ///
    /// Synced scope: a stack size is written into saved items, so every
    /// player in a world must agree on it. On a server without the mod, or
    /// with the feature off, a player who joins with stacked bases in a chest
    /// would hold more than that world thinks is possible -- so the server
    /// decides, and the setting is admin-only.
    /// </summary>
    internal sealed class StackableMeadBasesFeature : Feature
    {
        public const string FeatureName = "Items/StackableMeadBases";

        public static StackableMeadBasesFeature Instance { get; private set; }

        public StackableMeadBasesFeature() => Instance = this;

        public override string Key => "StackableMeadBases";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Mead bases and barley wine bases stack up to MeadBaseStackSize instead of taking a slot each. "
            + "Finished drinks are unchanged. Unstack them before turning this off, or a stack larger than "
            + "vanilla allows is left in your chest.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(StackableMeadBasesPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("ObjectDB", "Awake", "the moment item definitions are ready to change"),
            new CompatMember("ObjectDB", "CopyOtherDB", "item definitions replaced when a world loads"),
            new CompatMember("ObjectDB", "m_items", "finding the mead bases"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            ItemsConfig.BindStacking(config, section, Scope);
    }

    /// <summary>
    /// Stack size lives on an item's SharedData, which every copy of that
    /// item points at, so setting it once on the prefab changes every mead
    /// base in the world, in chests and on the ground alike.
    ///
    /// Applied on Awake and again on CopyOtherDB: the main menu builds one
    /// ObjectDB and loading a world copies another over it, and an item
    /// whose size was set only on the first would be back to vanilla in game.
    ///
    /// The vanilla size is remembered per item, so switching the feature off
    /// puts it back rather than leaving the raised size behind.
    /// </summary>
    [HarmonyPatch]
    internal static class StackableMeadBasesPatch
    {
        /// <summary>Vanilla's own stack size, by item name, from the first time each was seen.</summary>
        private static readonly Dictionary<string, int> Original = new Dictionary<string, int>(StringComparer.Ordinal);

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ObjectDB), nameof(ObjectDB.Awake), StackableMeadBasesFeature.FeatureName)
            & ValheimCompat.RequireMethod(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB), StackableMeadBasesFeature.FeatureName);

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ObjectDB), nameof(ObjectDB.Awake));
            yield return AccessTools.Method(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB));
        }

        private static void Postfix(ObjectDB __instance)
        {
            // An exception escaping here would leave the game with no items.
            try
            {
                Apply(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"StackableMeadBases: leaving mead bases as they were: {ex}");
            }
        }

        private static void Apply(ObjectDB db)
        {
            if (db?.m_items == null) return;

            bool active = StackableMeadBasesFeature.Instance?.IsActive == true;
            int size = Math.Max(1, ItemsConfig.MeadBaseStackSize?.Value ?? 20);
            int changed = 0;

            foreach (var prefab in db.m_items)
            {
                if (prefab == null) continue;
                if (!IsMeadBase(prefab.name)) continue;

                var drop = prefab.GetComponent<ItemDrop>();
                var shared = drop != null ? drop.m_itemData?.m_shared : null;
                if (shared == null) continue;

                if (!Original.ContainsKey(prefab.name)) Original[prefab.name] = shared.m_maxStackSize;

                int wanted = active ? size : Original[prefab.name];
                if (shared.m_maxStackSize == wanted) continue;

                shared.m_maxStackSize = wanted;
                changed++;
            }

            if (changed > 0)
                RossQoLPlugin.Log.LogInfo(
                    $"StackableMeadBases: {changed} brewing bases now stack to {(active ? size : 1)}.");
        }

        /// <summary>
        /// The brewing inputs, by the prefab naming vanilla uses:
        /// MeadBaseTasty, MeadBaseHealthMinor and the rest, plus
        /// BarleyWineBase, which is a fermenter input under another name.
        /// A finished drink is "MeadTasty" or "BarleyWine", without Base, and
        /// is left alone.
        /// </summary>
        private static bool IsMeadBase(string prefabName) =>
            prefabName != null
            && (prefabName.StartsWith("MeadBase", StringComparison.Ordinal)
                || prefabName.StartsWith("BarleyWineBase", StringComparison.Ordinal));
    }
}
