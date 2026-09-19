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

        [Fact]
        public void A_plain_grave_carries_no_entrance()
        {
            Assert.False(Sample().HasEntrance);

            // The eight-field line every record has always been.
            Assert.Equal(8, Sample().Format().Split(';').Length);
        }

        [Fact]
        public void A_grave_with_an_entrance_survives_a_round_trip()
        {
            var original = new GraveRecord(
                123456789L, 512.5f, 5030.75f, -64f, 12, 3, 9876543210L, 77u,
                510.25f, 28.5f, -66.75f);

            Assert.True(GraveRecord.TryParse(original.Format(), out var parsed));
            Assert.True(parsed.HasEntrance);
            Assert.Equal(original.Y, parsed.Y, 3);
            Assert.Equal(original.EntranceX, parsed.EntranceX, 3);
            Assert.Equal(original.EntranceY, parsed.EntranceY, 3);
            Assert.Equal(original.EntranceZ, parsed.EntranceZ, 3);
            Assert.Equal(original.ZdoId, parsed.ZdoId);
        }

        [Fact]
        public void A_record_written_before_entrances_existed_still_parses()
        {
            // Verbatim what a 0.21.0 character file holds. A player mid-run
            // must not lose their graves because a field appeared.
            const string old = "123456789;1024.5;-30.25;-2048;42;7;9876543210;4242";

            Assert.True(GraveRecord.TryParse(old, out var parsed));
            Assert.False(parsed.HasEntrance);
            Assert.Equal(123456789L, parsed.WorldId);
            Assert.Equal(1024.5f, parsed.X, 3);
            Assert.Equal(-30.25f, parsed.Y, 3);
            Assert.Equal(-2048f, parsed.Z, 3);
            Assert.Equal(42, parsed.Day);
            Assert.Equal(7, parsed.Items);
            Assert.Equal(9876543210L, parsed.ZdoUserId);
            Assert.Equal(4242u, parsed.ZdoId);
        }

        [Fact]
        public void An_entrance_is_formatted_the_same_in_every_locale()
        {
            string line = new GraveRecord(1L, 0f, 5000.5f, 0f, 1, 1, 2L, 3u, 1.25f, 2.5f, 3.75f).Format();

            Assert.DoesNotContain(",", line);
            Assert.Equal(11, line.Split(';').Length);
        }

        [Theory]
        [InlineData("1;2;3;4;5;6;7;8;9;10")]
        [InlineData("1;2;3;4;5;6;7;8;x;10;11")]
        [InlineData("1;2;3;4;5;6;7;8;9;10;11;12")]
        public void A_half_written_entrance_is_refused(string line)
        {
            Assert.False(GraveRecord.TryParse(line, out _));
        }
    }
}
