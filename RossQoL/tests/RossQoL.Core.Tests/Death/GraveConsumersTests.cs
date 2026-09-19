using RossQoL.Core.Death;
using Xunit;

namespace RossQoL.Core.Tests.Death
{
    public class GraveConsumersTests
    {
        [Theory]
        [InlineData("GraveMarker", true)]
        [InlineData("CorpseRun", true)]
        [InlineData("RespawnFood", false)]
        [InlineData("RespawnRested", false)]
        [InlineData("SkillLoss", false)]
        [InlineData("", false)]
        public void Only_the_features_that_point_at_a_grave_need_the_record(string key, bool expected)
        {
            Assert.Equal(expected, GraveConsumers.NeedsGraveRecord(key));
        }

        [Theory]
        [InlineData("RespawnFood", true)]
        [InlineData("RespawnRested", true)]
        [InlineData("GraveMarker", false)]
        [InlineData("CorpseRun", false)]
        [InlineData("SkillLoss", false)]
        public void Only_the_respawn_handouts_need_the_died_flag(string key, bool expected)
        {
            Assert.Equal(expected, GraveConsumers.NeedsDiedFlag(key));
        }

        [Fact]
        public void Skill_loss_alone_records_nothing_at_all()
        {
            // The whole point of the split: SkillLoss is Synced, so it is
            // patched even when off, and the recording hook it shares must
            // not write into the character file on its account.
            var active = new[] { "SkillLoss" };

            Assert.False(GraveConsumers.AnyNeedsGraveRecord(active));
            Assert.False(GraveConsumers.AnyNeedsDiedFlag(active));
        }

        [Fact]
        public void Nothing_active_means_nothing_is_written()
        {
            Assert.False(GraveConsumers.AnyNeedsGraveRecord(new string[0]));
            Assert.False(GraveConsumers.AnyNeedsDiedFlag(new string[0]));
            Assert.False(GraveConsumers.AnyNeedsGraveRecord(null));
            Assert.False(GraveConsumers.AnyNeedsDiedFlag(null));
        }

        [Fact]
        public void One_active_consumer_is_enough_to_keep_recording_alive()
        {
            Assert.True(GraveConsumers.AnyNeedsGraveRecord(new[] { "SkillLoss", "CorpseRun" }));
            Assert.True(GraveConsumers.AnyNeedsDiedFlag(new[] { "SkillLoss", "RespawnRested" }));
        }

        [Fact]
        public void The_handouts_keep_the_flag_without_keeping_a_grave_list()
        {
            var active = new[] { "RespawnFood", "RespawnRested" };

            Assert.True(GraveConsumers.AnyNeedsDiedFlag(active));
            Assert.False(GraveConsumers.AnyNeedsGraveRecord(active));
        }

        [Fact]
        public void An_unknown_key_is_ignored_rather_than_keeping_everything_alive()
        {
            var active = new string[] { "SomethingElse", null };

            Assert.False(GraveConsumers.AnyNeedsGraveRecord(active));
            Assert.False(GraveConsumers.AnyNeedsDiedFlag(active));
        }
    }
}
