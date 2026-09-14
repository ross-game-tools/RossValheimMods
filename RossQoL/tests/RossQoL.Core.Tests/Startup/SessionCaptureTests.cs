using RossQoL.Core.Startup;
using Xunit;

namespace RossQoL.Core.Tests.Startup
{
    public class SessionCaptureTests
    {
        [Fact]
        public void Hosting_a_local_world_records_it()
        {
            var capture = new SessionCapture();
            var session = capture.CommitOnInitialSpawn(true, "ross", "Local", "MyWorld", "Cloud");
            Assert.Equal(LastSession.LocalWorld("ross", "Local", "MyWorld", "Cloud"), session);
        }

        [Fact]
        public void Joining_a_server_records_the_stashed_intent()
        {
            var capture = new SessionCapture();
            capture.ServerJoinRequested(ServerKind.PlayFab, "ABCDEF", "123456", "Crossplay");
            var session = capture.CommitOnInitialSpawn(false, "ross", "Local", null, null);
            Assert.Equal(LastSession.Server("ross", "Local", ServerKind.PlayFab, "ABCDEF", "123456", "Crossplay"), session);
        }

        [Fact]
        public void A_client_spawn_without_intent_records_nothing()
        {
            Assert.Null(new SessionCapture().CommitOnInitialSpawn(false, "ross", "Local", null, null));
        }

        [Fact]
        public void An_invalid_intent_records_nothing()
        {
            var capture = new SessionCapture();
            capture.ServerJoinRequested(ServerKind.None, "1.2.3.4:2456", "", "x");
            Assert.Null(capture.CommitOnInitialSpawn(false, "ross", "Local", null, null));
        }

        [Fact]
        public void Starting_a_local_world_discards_a_stale_server_intent()
        {
            // A join that failed (wrong password, offline) leaves an intent that
            // must not be committed by a later, unrelated session.
            var capture = new SessionCapture();
            capture.ServerJoinRequested(ServerKind.Dedicated, "1.2.3.4:2456", "", "Old");
            capture.LocalWorldStartRequested();
            Assert.Null(capture.CommitOnInitialSpawn(false, "ross", "Local", null, null));
        }

        [Fact]
        public void A_commit_consumes_the_intent()
        {
            var capture = new SessionCapture();
            capture.ServerJoinRequested(ServerKind.Dedicated, "1.2.3.4:2456", "", "Srv");
            Assert.NotNull(capture.CommitOnInitialSpawn(false, "ross", "Local", null, null));
            Assert.Null(capture.CommitOnInitialSpawn(false, "ross", "Local", null, null));
        }

        [Fact]
        public void The_latest_join_request_wins()
        {
            var capture = new SessionCapture();
            capture.ServerJoinRequested(ServerKind.Dedicated, "1.1.1.1:2456", "", "First");
            capture.ServerJoinRequested(ServerKind.Dedicated, "2.2.2.2:2456", "", "Second");
            Assert.Equal("2.2.2.2:2456", capture.CommitOnInitialSpawn(false, "ross", "Local", null, null).ServerAddress);
        }

        [Fact]
        public void A_spawn_with_no_character_records_nothing()
        {
            Assert.Null(new SessionCapture().CommitOnInitialSpawn(true, "", "Local", "MyWorld", "Local"));
        }
    }
}
