using System.Linq;
using Xunit;

namespace RossPortals.Core.Tests
{
    public class PortalNameTests
    {
        private const char Sep = '/';

        [Fact]
        public void Ungrouped_name_is_a_single_leaf_with_no_folder()
        {
            Assert.Equal(new[] { "Home" }, PortalName.Segments("Home", Sep).ToArray());
            Assert.Equal("Home", PortalName.Leaf("Home", Sep));
            Assert.Empty(PortalName.GroupPath("Home", Sep));
        }

        [Fact]
        public void Separator_splits_folder_path_from_leaf()
        {
            Assert.Equal("North", PortalName.Leaf("Mines/Copper/North", Sep));
            Assert.Equal(new[] { "Mines", "Copper" }, PortalName.GroupPath("Mines/Copper/North", Sep).ToArray());
        }

        [Fact]
        public void Blank_leading_trailing_and_doubled_separators_are_dropped()
        {
            // "/Mines//Copper/" would otherwise create empty folders.
            Assert.Equal(new[] { "Mines", "Copper" }, PortalName.Segments("/Mines//Copper/", Sep).ToArray());
            Assert.Equal("Copper", PortalName.Leaf("/Mines//Copper/", Sep));
            Assert.Equal(new[] { "Mines" }, PortalName.GroupPath("/Mines//Copper/", Sep).ToArray());
        }

        [Fact]
        public void Trailing_separator_leaves_the_name_ungrouped()
        {
            // "Mines/" is just the portal "Mines", not folder "Mines" with a
            // nameless portal inside it.
            Assert.Empty(PortalName.GroupPath("Mines/", Sep));
            Assert.Equal("Mines", PortalName.Leaf("Mines/", Sep));
        }

        [Fact]
        public void Segments_are_trimmed_so_spaced_separators_work()
        {
            Assert.Equal(new[] { "Mines", "Copper" }, PortalName.Segments("Mines / Copper", Sep).ToArray());
        }

        [Fact]
        public void Empty_name_has_no_segments_and_an_empty_leaf()
        {
            Assert.Empty(PortalName.Segments("", Sep));
            Assert.Empty(PortalName.Segments("   ", Sep));
            Assert.Equal("", PortalName.Leaf("", Sep));
        }
    }
}
