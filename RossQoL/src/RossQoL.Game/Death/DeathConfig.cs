using BepInEx.Configuration;
using RossQoL.Core.Death;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// Settings for the Death category. Read at use, so live reloads apply
    /// without a restart.
    /// </summary>
    public static class DeathConfig
    {
        public static ConfigEntry<int> MinItemsToTrack;
        public static ConfigEntry<float> HideDistance;
        public static ConfigEntry<float> EdgeMargin;
        public static ConfigEntry<KeyboardShortcut> ClearGraveKey;
        public static ConfigEntry<float> ClearGraveHoldSeconds;
        public static ConfigEntry<KeyboardShortcut> CycleGraveKey;

        public static ConfigEntry<int> RespawnFoodCount;
        public static ConfigEntry<string> RespawnFoodsByFrontier;

        public static ConfigEntry<float> RestedMinutes;

        public static ConfigEntry<float> CorpseRunMinDistance;
        public static ConfigEntry<float> CorpseRunFullDistance;
        public static ConfigEntry<float> CorpseRunMaxRegenBonus;
        public static ConfigEntry<float> CorpseRunMaxDrainReduction;
        public static ConfigEntry<float> CorpseRunMinStrength;
        public static ConfigEntry<float> CorpseRunMinutes;

        public static ConfigEntry<float> SkillLossMultiplier;

        internal static void BindMarker(ConfigFile config, string section, FeatureScope scope)
        {
            // Synced, not this feature's Client scope: it is read on the
            // shared recording path, so a client raising it would suppress
            // recording entirely and with it the server-controlled corpse run
            // buff. Bound here because the marker is the feature it reads
            // most like, but it is the server's value.
            MinItemsToTrack = config.Bind(section, "MinItemsToTrack", 1,
                ConfigText.Description(
                    "The fewest items a grave has to hold before it is tracked at all. A death that left almost "
                    + "nothing behind is not worth a marker on your screen.",
                    FeatureScope.Synced, requiresRestart: false, range: new AcceptableValueRange<int>(0, 64)));

            HideDistance = config.Bind(section, "HideDistance", 10f,
                ConfigText.Description(
                    "How close you have to be to a grave before its on-screen marker hides, so it does not "
                    + "cover the grave itself.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 100f)));

            EdgeMargin = config.Bind(section, "EdgeMargin", 60f,
                ConfigText.Description(
                    "How far in from the edge of the screen a grave's marker sits when the grave itself is off "
                    + "screen.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 400f)));

            ClearGraveKey = config.Bind(section, "ClearGraveKey", new KeyboardShortcut(KeyCode.Delete),
                ConfigText.Description(
                    "Hold this key to stop tracking the selected grave, for one you have already looted or no "
                    + "longer care about.",
                    scope, requiresRestart: false));

            ClearGraveHoldSeconds = config.Bind(section, "ClearGraveHoldSeconds", 1.5f,
                ConfigText.Description(
                    "How long ClearGraveKey must be held before the selected grave is forgotten. Held rather "
                    + "than tapped, so it is not cleared by accident.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0.1f, 10f)));

            CycleGraveKey = config.Bind(section, "CycleGraveKey", new KeyboardShortcut(KeyCode.PageDown),
                ConfigText.Description(
                    "Press this key to switch the tracked marker to your next remembered grave, for when you "
                    + "have died more than once. Defaults to PageDown.",
                    scope, requiresRestart: false));
        }

        internal static void BindRespawnFood(ConfigFile config, string section, FeatureScope scope)
        {
            RespawnFoodCount = config.Bind(section, "RespawnFoodCount", 1,
                ConfigText.Description(
                    "How many portions of food you are handed on respawn, so a death does not also mean "
                    + "starting the walk back hungry. The food picked is always the same one, so above 1 "
                    + "vanilla usually refuses the repeat while the first helping is still fresh -- in "
                    + "practice this is almost always one meal.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<int>(0, 3)));

            RespawnFoodsByFrontier = config.Bind(section, "RespawnFoodsByFrontier", RespawnFoods.DefaultTable,
                ConfigText.Description(
                    "The food handed out on respawn, by how far the world has got. Format is "
                    + "\"Tier:Food|Fallback|Fallback,Tier:...\" -- the first food in the tier's list that the "
                    + "world actually has is used, and a tier with nothing available falls back to the tier "
                    + "below it.",
                    scope, requiresRestart: false));
        }

        internal static void BindRespawnRested(ConfigFile config, string section, FeatureScope scope)
        {
            RestedMinutes = config.Bind(section, "RestedMinutes", 10f,
                ConfigText.Description(
                    "Minutes of the Rested buff handed out on respawn, so the walk back is not also spent at "
                    + "reduced stamina regeneration.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 60f)));
        }

        internal static void BindCorpseRun(ConfigFile config, string section, FeatureScope scope)
        {
            CorpseRunMinDistance = config.Bind(section, "CorpseRunMinDistance", 50f,
                ConfigText.Description(
                    "How far your grave has to be before the corpse run buff starts climbing past "
                    + "CorpseRunMinStrength, its strength at the grave itself. Beyond this distance it keeps "
                    + "rising toward full strength as your grave gets further away.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 1000f)));

            CorpseRunFullDistance = config.Bind(section, "CorpseRunFullDistance", 1000f,
                ConfigText.Description(
                    "How much further than CorpseRunMinDistance your grave has to be for the corpse run buff to "
                    + "reach full strength. Measured beyond the minimum, not from the grave.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 10000f)));

            CorpseRunMaxRegenBonus = config.Bind(section, "CorpseRunMaxRegenBonus", 0.55f,
                ConfigText.Description(
                    "The most the corpse run buff can add to your stamina regeneration, reached at "
                    + "CorpseRunFullDistance. A modest cushion for the walk back, not a replacement for it -- "
                    + "the buff ends the moment you loot your grave, and Valheim's own, much stronger reward "
                    + "takes over from there.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 5f)));

            CorpseRunMaxDrainReduction = config.Bind(section, "CorpseRunMaxDrainReduction", 0.25f,
                ConfigText.Description(
                    "The most the corpse run buff can cut your running and jumping stamina cost by, reached "
                    + "at CorpseRunFullDistance. A modest cushion for the walk back, not a replacement for it "
                    + "-- the buff ends the moment you loot your grave, and Valheim's own, much stronger "
                    + "reward takes over from there.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 1f)));

            CorpseRunMinStrength = config.Bind(section, "CorpseRunMinStrength", 0.5f,
                ConfigText.Description(
                    "The strength of the corpse run buff standing at your own grave, as a fraction of full "
                    + "strength -- the rest of the range between here and full strength is earned back by "
                    + "distance as the grave gets further away, rather than the buff sitting flat until "
                    + "CorpseRunMinDistance kicks in. 0 lets it fade all the way to nothing at the grave; 1 "
                    + "keeps it always at full strength. It still ends the moment you loot the grave.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 1f)));

            CorpseRunMinutes = config.Bind(section, "CorpseRunMinutes", 10f,
                ConfigText.Description(
                    "How many real-time minutes the corpse run buff can run for a given grave, starting the "
                    + "moment it first applies to that grave and counting down whatever you do -- a fresh "
                    + "death starts a new clock, so an earlier one never shortens the buff you get for your "
                    + "latest. Once the limit is reached the buff ends and does not come back for that grave, "
                    + "even if you are still far from it -- it caps how long the help lasts, it does not pause "
                    + "it. 0 removes the limit, leaving the buff to last as long as the grave does. Not saved "
                    + "across a relog: the clock restarts if you log back in.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 60f)));
        }

        internal static void BindSkillLoss(ConfigFile config, string section, FeatureScope scope)
        {
            SkillLossMultiplier = config.Bind(section, "SkillLossMultiplier", 1f,
                ConfigText.Description(
                    "How much skill you lose when you die, against what Valheim would normally take. 1 is the "
                    + "usual loss, 0.5 is half of it, 0 is none at all. A soft death -- dying again within "
                    + "seconds -- already costs nothing, and that is unchanged. Even at 0, Valheim still shows "
                    + "its own \"skills lowered\" message on death; that message is not part of what this "
                    + "setting scales.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 2f)));
        }
    }
}
