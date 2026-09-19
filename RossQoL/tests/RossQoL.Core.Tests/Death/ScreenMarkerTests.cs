using RossQoL.Core.Death;
using Xunit;

namespace RossQoL.Core.Tests.Death
{
    public class ScreenMarkerTests
    {
        private const float W = 1920f;
        private const float H = 1080f;
        private const float Margin = 60f;

        private static MarkerPlacement Place(float x, float y, float z = 10f) =>
            ScreenMarker.Place(x, y, z, W, H, Margin);

        [Fact]
        public void A_grave_in_view_is_marked_where_it_is()
        {
            var placement = Place(900f, 500f);

            Assert.True(placement.Visible);
            Assert.True(placement.OnScreen);
            Assert.Equal(900f, placement.X, 3);
            Assert.Equal(500f, placement.Y, 3);
            Assert.Equal(0f, placement.AngleDegrees, 3);
        }

        [Fact]
        public void A_grave_off_the_right_edge_is_clamped_inside_the_margin()
        {
            var placement = Place(3000f, 540f);

            Assert.True(placement.Visible);
            Assert.False(placement.OnScreen);
            Assert.Equal(W - Margin, placement.X, 3);
            Assert.InRange(placement.Y, Margin, H - Margin);
            Assert.Equal(90f, placement.AngleDegrees, 1);
        }

        [Fact]
        public void A_grave_off_the_top_points_up()
        {
            var placement = Place(960f, 2000f);

            Assert.False(placement.OnScreen);
            Assert.Equal(H - Margin, placement.Y, 3);
            Assert.Equal(0f, placement.AngleDegrees, 1);
        }

        [Fact]
        public void A_grave_off_the_bottom_left_leaves_through_the_nearer_edge()
        {
            var placement = Place(-500f, -500f);

            Assert.False(placement.OnScreen);

            // The ray reaches the bottom edge before the left one, so the
            // marker pins to the bottom and slides left along it -- a corner
            // would need a direction exactly into the corner.
            Assert.Equal(Margin, placement.Y, 3);
            Assert.InRange(placement.X, Margin, W / 2f);

            // Down and to the left: between straight-down (180) and
            // straight-left (270).
            Assert.InRange(placement.AngleDegrees, 180f, 270f);
        }

        [Fact]
        public void A_grave_behind_the_camera_is_flipped_rather_than_mirrored()
        {
            // Behind the camera Unity mirrors x and y, so a grave behind and to
            // the left reports a point to the right. Without the flip the
            // marker points exactly the wrong way -- the worst failure this can
            // have.
            var behind = ScreenMarker.Place(1500f, 700f, -10f, W, H, Margin);

            Assert.True(behind.Visible);
            Assert.False(behind.OnScreen);
            Assert.InRange(behind.AngleDegrees, 180f, 360f);
        }

        [Fact]
        public void A_screen_with_no_size_is_refused_rather_than_dividing_by_zero()
        {
            Assert.False(ScreenMarker.Place(10f, 10f, 10f, 0f, 0f, Margin).Visible);
            Assert.False(ScreenMarker.Place(10f, 10f, 10f, W, 0f, Margin).Visible);
        }

        [Fact]
        public void A_margin_bigger_than_the_screen_still_lands_on_the_screen()
        {
            var placement = ScreenMarker.Place(5000f, 540f, 10f, W, H, margin: 5000f);

            Assert.InRange(placement.X, 0f, W);
            Assert.InRange(placement.Y, 0f, H);
        }

        [Fact]
        public void A_grave_exactly_under_the_camera_is_marked_without_a_nan()
        {
            var placement = ScreenMarker.Place(W / 2f, H / 2f, -10f, W, H, Margin);

            Assert.True(placement.Visible);
            Assert.False(float.IsNaN(placement.X));
            Assert.False(float.IsNaN(placement.Y));
            Assert.False(float.IsNaN(placement.AngleDegrees));
        }
    }
}
