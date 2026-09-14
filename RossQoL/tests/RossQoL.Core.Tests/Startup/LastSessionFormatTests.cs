using RossQoL.Core.Startup;
using Xunit;

namespace RossQoL.Core.Tests.Startup
{
    public class LastSessionFormatTests
    {
        private static LastSession Local() =>
            LastSession.LocalWorld("ross", "Local", "MyWorld", "Cloud");

        private static LastSession Dedicated() =>
            LastSession.Server("ross", "Local", ServerKind.Dedicated, "play.example.com:2456", "", "Friday Vikings");

        [Fact]
        public void A_local_world_round_trips()
        {
            Assert.Equal(Local(), LastSessionFormat.Read(LastSessionFormat.Write(Local())));
        }

        [Fact]
        public void A_server_round_trips()
        {
            Assert.Equal(Dedicated(), LastSessionFormat.Read(LastSessionFormat.Write(Dedicated())));
        }

        [Fact]
        public void A_playfab_server_keeps_its_join_code()
        {
            var session = LastSession.Server("ross", "Cloud", ServerKind.PlayFab, "ABCDEF0123", "123456", "Crossplay");
            var read = LastSessionFormat.Read(LastSessionFormat.Write(session));
            Assert.Equal("123456", read.JoinCode);
            Assert.Equal(session, read);
        }

        [Fact]
        public void Names_containing_the_separator_and_escape_survive()
        {
            var session = LastSession.LocalWorld("ro|ss", "Local", @"My\World|2", "Local");
            Assert.Equal(session, LastSessionFormat.Read(LastSessionFormat.Write(session)));
        }

        [Fact]
        public void A_server_with_no_display_name_shows_its_address()
        {
            var session = LastSession.Server("ross", "Local", ServerKind.Dedicated, "1.2.3.4:2456", "", "");
            Assert.Equal("1.2.3.4:2456", session.DisplayName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("1|LocalWorld|ross")]                                          // truncated
        [InlineData("2|LocalWorld|ross|Local|MyWorld|Local|None|||MyWorld")]       // unknown version
        [InlineData("1|Nonsense|ross|Local|MyWorld|Local|None|||MyWorld")]         // unknown kind
        [InlineData("1|7|ross|Local|MyWorld|Local|None|||MyWorld")]                // numeric enum
        [InlineData("1|LocalWorld||Local|MyWorld|Local|None|||MyWorld")]           // no character
        [InlineData("1|LocalWorld|ross|Local||Local|None|||")]                     // no world
        [InlineData("1|Server|ross|Local|||None|1.2.3.4:2456||x")]                 // server without kind
        [InlineData("1|Server|ross|Local|||Dedicated|||x")]                        // server without address
        [InlineData("1|LocalWorld|ross|Local|MyWorld|Local|None|||MyWorld\\")]     // dangling escape
        [InlineData("1|Server|ross|Local|||+1|1.2.3.4:2456||x")]                   // plus sign in enum
        [InlineData("1|Server|ross|Local||| Dedicated|1.2.3.4:2456||x")]           // space in enum
        [InlineData("1|localworld|ross|Local|MyWorld|Local|None|||MyWorld")]       // lowercase enum
        public void Invalid_text_reads_as_no_record(string text)
        {
            Assert.Null(LastSessionFormat.Read(text));
        }
    }
}
