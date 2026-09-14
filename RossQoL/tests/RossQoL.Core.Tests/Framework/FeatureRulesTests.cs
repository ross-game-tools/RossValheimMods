using RossQoL.Core.Framework;
using Xunit;

namespace RossQoL.Core.Tests.Framework
{
    public class FeatureRulesTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void A_feature_is_active_only_when_its_category_and_its_own_toggle_are_on(
            bool category, bool feature, bool expected)
        {
            Assert.Equal(expected, FeatureRules.IsActive(category, feature));
        }

        [Fact]
        public void A_category_of_client_features_is_client_scoped()
        {
            Assert.Equal(FeatureScope.Client,
                FeatureRules.CategoryScope(new[] { FeatureScope.Client, FeatureScope.Client }));
        }

        [Fact]
        public void One_synced_feature_makes_the_whole_category_toggle_synced()
        {
            // Otherwise a client could switch a server-enforced feature off by
            // switching its section off.
            Assert.Equal(FeatureScope.Synced,
                FeatureRules.CategoryScope(new[] { FeatureScope.Client, FeatureScope.Synced }));
        }

        [Fact]
        public void An_empty_or_missing_category_is_client_scoped()
        {
            Assert.Equal(FeatureScope.Client, FeatureRules.CategoryScope(new FeatureScope[0]));
            Assert.Equal(FeatureScope.Client, FeatureRules.CategoryScope(null));
        }

        [Theory]
        [InlineData(FeatureScope.Client, true, true, true)]
        [InlineData(FeatureScope.Client, false, true, false)]   // disabled client features stay unpatched
        [InlineData(FeatureScope.Synced, false, true, true)]    // the server may switch it on later
        [InlineData(FeatureScope.Synced, true, true, true)]
        [InlineData(FeatureScope.Client, true, false, false)]   // missing members always win
        [InlineData(FeatureScope.Synced, true, false, false)]
        public void Patching_follows_scope_activation_and_member_presence(
            FeatureScope scope, bool active, bool membersPresent, bool expected)
        {
            Assert.Equal(expected, FeatureRules.ShouldPatch(scope, active, membersPresent));
        }

        [Fact]
        public void Client_descriptions_say_personal_and_restart_when_asked()
        {
            Assert.Equal("Does a thing. Personal setting. Requires restart.",
                FeatureRules.Describe("Does a thing.", FeatureScope.Client, requiresRestart: true));
            Assert.Equal("Does a thing. Personal setting.",
                FeatureRules.Describe("Does a thing.", FeatureScope.Client, requiresRestart: false));
        }

        [Fact]
        public void Toggles_that_only_restart_to_turn_on_say_so()
        {
            Assert.Equal("Does a thing. Personal setting. Turning it on requires a restart.",
                FeatureRules.Describe("Does a thing.", FeatureScope.Client,
                    requiresRestart: false, turningOnRequiresRestart: true));
        }

        [Fact]
        public void A_full_restart_note_wins_over_the_turning_on_note()
        {
            Assert.Equal("Does a thing. Personal setting. Requires restart.",
                FeatureRules.Describe("Does a thing.", FeatureScope.Client,
                    requiresRestart: true, turningOnRequiresRestart: true));
        }

        [Fact]
        public void Synced_descriptions_say_server_controlled()
        {
            Assert.Equal("Does a thing. Server-controlled when connected.",
                FeatureRules.Describe("Does a thing.", FeatureScope.Synced, requiresRestart: false));
        }
    }
}
