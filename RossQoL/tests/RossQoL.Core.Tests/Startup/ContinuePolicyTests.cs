using RossQoL.Core.Startup;
using Xunit;

namespace RossQoL.Core.Tests.Startup
{
    public class ContinuePolicyTests
    {
        private static readonly LastSession Local = LastSession.LocalWorld("ross", "Local", "MyWorld", "Local");
        private static readonly LastSession Server =
            LastSession.Server("ross", "Local", ServerKind.Dedicated, "1.2.3.4:2456", "", "Friday Vikings");

        [Fact]
        public void Shows_for_a_resumable_local_world()
        {
            Assert.True(ContinuePolicy.ShouldShow(Local, characterExists: true, worldLoadable: true, launchedWithJoinArguments: false));
        }

        [Fact]
        public void Shows_for_a_server_without_checking_any_world()
        {
            Assert.True(ContinuePolicy.ShouldShow(Server, characterExists: true, worldLoadable: false, launchedWithJoinArguments: false));
        }

        [Fact]
        public void Hidden_with_no_record()
        {
            Assert.False(ContinuePolicy.ShouldShow(null, true, true, false));
        }

        [Fact]
        public void Hidden_when_the_character_is_gone()
        {
            Assert.False(ContinuePolicy.ShouldShow(Local, characterExists: false, worldLoadable: true, launchedWithJoinArguments: false));
            Assert.False(ContinuePolicy.ShouldShow(Server, characterExists: false, worldLoadable: true, launchedWithJoinArguments: false));
        }

        [Fact]
        public void Hidden_when_the_local_world_cannot_be_loaded()
        {
            Assert.False(ContinuePolicy.ShouldShow(Local, characterExists: true, worldLoadable: false, launchedWithJoinArguments: false));
        }

        [Fact]
        public void Hidden_when_the_game_was_launched_to_join_something()
        {
            Assert.False(ContinuePolicy.ShouldShow(Local, true, true, launchedWithJoinArguments: true));
        }

        [Theory]
        [InlineData("+connect")]
        [InlineData("+connect_lobby")]
        [InlineData("-joincode")]
        [InlineData("-joinserverwithcharacter")]
        public void Recognises_vanilla_join_arguments(string argument)
        {
            Assert.True(ContinuePolicy.HasJoinArguments(new[] { "valheim.exe", argument, "x" }));
        }

        [Fact]
        public void Other_arguments_are_not_join_arguments()
        {
            Assert.False(ContinuePolicy.HasJoinArguments(new[] { "valheim.exe", "-console", "-password", "x" }));
            Assert.False(ContinuePolicy.HasJoinArguments(null));
        }

        [Fact]
        public void Label_names_character_and_destination()
        {
            Assert.Equal("Continue: Ross on MyWorld", ContinuePolicy.Label("Ross", Local));
        }

        [Fact]
        public void Long_labels_are_truncated_with_an_ellipsis()
        {
            var session = LastSession.LocalWorld("ross", "Local", new string('W', 60), "Local");
            string label = ContinuePolicy.Label("Ross", session);
            Assert.Equal(ContinuePolicy.MaxLabelLength, label.Length);
            Assert.EndsWith("…", label);
        }

        [Fact]
        public void Truncation_never_splits_a_surrogate_pair()
        {
            // "Continue: Ross on " is 18 chars. Adding 20 'W's makes 38 chars.
            // Adding emoji "😀" (a 2-char UTF-16 pair) puts the high surrogate at index 38.
            // The truncation should detect this and back up.
            var worldName = new string('W', 20) + "😀XYZ";
            var session = LastSession.LocalWorld("ross", "Local", worldName, "Local");
            string label = ContinuePolicy.Label("Ross", session);

            // Verify no unpaired surrogates
            for (int i = 0; i < label.Length; i++)
            {
                if (char.IsHighSurrogate(label[i]))
                {
                    // High surrogate must be followed by low surrogate
                    Assert.True(i + 1 < label.Length && char.IsLowSurrogate(label[i + 1]),
                        "High surrogate at index " + i + " not followed by low surrogate");
                    i++; // Skip the low surrogate in next iteration
                }
                else if (char.IsLowSurrogate(label[i]))
                {
                    // Low surrogate must be preceded by high surrogate
                    Assert.True(i > 0 && char.IsHighSurrogate(label[i - 1]),
                        "Low surrogate at index " + i + " not preceded by high surrogate");
                }
            }

            Assert.EndsWith("…", label);
            Assert.True(label.Length <= ContinuePolicy.MaxLabelLength);
        }
    }
}
