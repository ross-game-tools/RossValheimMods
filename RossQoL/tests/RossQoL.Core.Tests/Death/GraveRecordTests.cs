using RossQoL.Core.Death;
using Xunit;

namespace RossQoL.Core.Tests.Death
{
    public class GraveRecordTests
    {
        private static GraveRecord Sample() =>
            new GraveRecord(123456789L, 1024.5f, -30.25f, -2048f, 42, 7, 9876543210L, 4242u);

        [Fact]
        public void A_record_survives_a_round_trip()
        {
            var original = Sample();

            Assert.True(GraveRecord.TryParse(original.Format(), out var parsed));
            Assert.Equal(original.WorldId, parsed.WorldId);
            Assert.Equal(original.X, parsed.X, 3);
            Assert.Equal(original.Y, parsed.Y, 3);
            Assert.Equal(original.Z, parsed.Z, 3);
            Assert.Equal(original.Day, parsed.Day);
            Assert.Equal(original.Items, parsed.Items);
            Assert.Equal(original.ZdoUserId, parsed.ZdoUserId);
            Assert.Equal(original.ZdoId, parsed.ZdoId);
        }

        [Fact]
        public void A_record_is_formatted_the_same_in_every_locale()
        {
            // A decimal comma would split the record's own fields.
            Assert.DoesNotContain(",", Sample().Format());
            Assert.Contains(".", Sample().Format());
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("not a record")]
        [InlineData("1;2;3")]
        [InlineData("x;1;2;3;4;5;6;7")]
        [InlineData("1;2;3;4;5;6;7;8;9")]
        public void Nonsense_is_refused_rather_than_throwing(string line)
        {
            Assert.False(GraveRecord.TryParse(line, out _));
        }
    }
}
