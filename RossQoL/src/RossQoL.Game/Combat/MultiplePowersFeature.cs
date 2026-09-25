using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// A second guardian-power slot. Using a guardian stone normally still sets
    /// your one vanilla power (slot 1), fired by the vanilla key; holding the
    /// assign modifier (Shift by default) while using a stone sends that power
    /// to a SECOND slot instead, which fires on its own key (H by default) and
    /// runs its own cooldown. Your first power and its key are untouched.
    ///
    /// Client scope: a guardian power is a self-buff -- vanilla tracks and fires
    /// it per player -- so a second one is each player's own business and needs
    /// no agreement with the server.
    /// </summary>
    internal sealed class MultiplePowersFeature : Feature
    {
        public const string FeatureName = "Combat/MultiplePowers";

        public static MultiplePowersFeature Instance { get; private set; }

        public MultiplePowersFeature() => Instance = this;

        public override string Key => "MultiplePowers";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "A second guardian-power slot. Hold the assign modifier (Shift by default) and use a "
            + "guardian stone to set that power as your second power; it fires on its own key (H by "
            + "default) with its own cooldown. Using a stone normally still sets your first power, "
            + "which fires on the vanilla guardian-power key as always.";

        // A held modifier at a guardian stone assigns the second power; its own
        // key fires it and ticks its cooldown, and the HUD shows its icon beside
        // vanilla's single slot.
        public override IEnumerable<Type> PatchClasses =>
            new[]
            {
                typeof(GuardianStonePatch),
                typeof(GuardianStoneHoverPatch),
                typeof(PowerActivationPatch),
                typeof(PowerHudPatch),
                typeof(PowerSavePatch),
            };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("ItemStand", "Interact", "the guardian-stone use hook where the modifier diverts a power to the second slot"),
            new CompatMember("ItemStand", "m_guardianPower", "the power a guardian stone grants"),
            new CompatMember("ItemStand", "GetHoverText", "adding the second-power option to the stone's hover text"),
            new CompatMember("ItemStand", "m_activatePowerEffects", "the stone's own power-activation burst, replayed when assigning the second power"),
            new CompatMember("ItemStand", "m_activatePowerEffectsPlayer", "the on-player half of that activation burst"),
            new CompatMember("EffectList", "Create", "playing the activation burst when the second power is assigned"),
            new CompatMember("Player", "Save", "writing the live second-power cooldown into the character save"),
            new CompatMember("Player", "Update", "the per-frame hook that fires the second power from its key"),
            new CompatMember("Player", "m_localPlayer", "acting only for the player at this client"),
            new CompatMember("Player", "GetGuardianPowerName", "the vanilla-held power, so the second power never double-fires it"),
            new CompatMember("Player", "AddAdrenaline", "granting the same guardian-power adrenaline vanilla does"),
            new CompatMember("Player", "m_adrenalineGuardianPower", "how much adrenaline a guardian power grants"),
            new CompatMember("Player", "Message", "telling the player its second power was set"),
            new CompatMember("Character", "GetSEMan", "applying the fired power's status effect"),
            new CompatMember("SEMan", "AddStatusEffect", "applying the fired power's status effect"),
            new CompatMember("ObjectDB", "instance", "resolving the power's status effect"),
            new CompatMember("ObjectDB", "GetStatusEffect", "resolving the power's status effect"),
            new CompatMember("StatusEffect", "NameHash", "the effect hash added to the player"),
            new CompatMember("StatusEffect", "m_cooldown", "the power's own cooldown length"),
            new CompatMember("StatusEffect", "m_icon", "the power's HUD icon"),
            new CompatMember("StatusEffect", "m_name", "the power's display name"),
            new CompatMember("StatusEffect", "GetTimeString", "formatting the power's remaining cooldown"),
            new CompatMember("Hud", "UpdateGuardianPower", "the HUD hook the second-power icon is drawn beside"),
            new CompatMember("Hud", "m_gpRoot", "the vanilla power slot cloned for the second power"),
            new CompatMember("Hud", "m_gpIcon", "the cloned slot's icon, tinted and sprited for the power"),
            new CompatMember("Hud", "m_gpName", "the cloned slot's power-name label, hidden on the clone"),
            new CompatMember("Hud", "m_gpCooldown", "the cloned slot's cooldown text"),
            new CompatMember("Hud", "s_colorRedBlueZeroAlpha", "matching vanilla's on-cooldown icon tint"),
            new CompatMember("Localization", "instance", "localizing the power name and HUD text"),
            new CompatMember("Localization", "Localize", "localizing the power name and HUD text"),
            new CompatMember("ZInput", "GetKeyDown", "reading the second power's fire key"),
            new CompatMember("ZInput", "GetKey", "reading the assign modifier held at a stone"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            MultiplePowersConfig.Bind(config, section);
    }
}
