using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class DrawerProportionsTests
    {
        [Fact]
        public void Approved_proportions_produce_the_recorded_label_size()
        {
            // docs/drawer-spec.md records 0.456 for the measured 0.66m-cube
            // proportions (0.691 was the figure for the original 1m-cube
            // tuning, before the 0.66 resize -- every dimension feeding this
            // calculation scales by the same factor, so 0.691 * 0.66 = 0.456
            // to three decimals). If this fails, either the proportions
            // changed or the derivation did.
            var p = new DrawerProportions();
            Assert.Equal(0.456f, p.LabelSize, 3);
        }

        [Fact]
        public void Label_never_collapses_below_the_floor()
        {
            var p = new DrawerProportions { FrameThickness = 0.49f };
            Assert.Equal(0.05f, p.LabelSize);
        }
    }
}
