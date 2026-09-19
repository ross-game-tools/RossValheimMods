using System;

namespace RossQoL.Core.Death
{
    /// <summary>Where the marker goes this frame, and which way it points.</summary>
    public readonly struct MarkerPlacement
    {
        /// <summary>False when there is nothing to draw; true when the marker should appear on screen.</summary>
        public bool Visible { get; }

        /// <summary>Screen x coordinate in pixels, measured from the bottom-left.</summary>
        public float X { get; }

        /// <summary>Screen y coordinate in pixels, measured from the bottom-left.</summary>
        public float Y { get; }

        /// <summary>True when the grave is in view; when false, the marker points toward it from the screen edge.</summary>
        public bool OnScreen { get; }

        /// <summary>Rotation in degrees: 0 when the grave is straight up the screen, increasing clockwise. Always 0 when OnScreen is true.</summary>
        public float AngleDegrees { get; }

        /// <summary>Creates a marker placement at the given coordinates with the given visibility and angle.</summary>
        public MarkerPlacement(bool visible, float x, float y, bool onScreen, float angleDegrees)
        {
            Visible = visible;
            X = x; Y = y;
            OnScreen = onScreen;
            AngleDegrees = angleDegrees;
        }

        /// <summary>A placement with Visible=false, used when there is nothing to draw.</summary>
        public static MarkerPlacement Hidden => new MarkerPlacement(false, 0f, 0f, false, 0f);
    }

    /// <summary>
    /// Turns a projected grave position into a marker placement: over the
    /// grave while it is in view, pinned to the screen edge and pointing at it
    /// when it is not.
    ///
    /// Screen coordinates are the ones Unity hands back -- pixels from the
    /// bottom-left, with z the distance in front of the camera. A point BEHIND
    /// the camera comes back with a negative z and its x and y mirrored, so it
    /// is flipped about the centre before anything else. Miss that and the
    /// marker points exactly the wrong way whenever the grave is behind you,
    /// which is most of the run back.
    /// </summary>
    public static class ScreenMarker
    {
        /// <summary>
        /// Places a marker on or off screen to track the grave.
        ///
        /// Returns the grave's position if in view, or a marker placement pinned
        /// to the screen edge with an angle pointing toward the grave, with a
        /// margin of breathing room inside the edge. When the screen has no size,
        /// or the grave is neither in view nor yielding a valid direction, returns
        /// Hidden.
        ///
        /// Screen coordinates are pixels from the bottom-left, as Unity's
        /// Camera.WorldToScreenPointScaled returns them: x from 0 (left) to
        /// screenWidth (right), y from 0 (bottom) to screenHeight (top), and z
        /// the distance from the camera. A negative z indicates the point is
        /// behind the camera; its x and y are mirrored, and must be flipped
        /// about the screen centre before use, else the marker points exactly
        /// backward whenever the grave is behind you — the worst failure this
        /// can have.
        ///
        /// The margin keeps the marker inset from the screen edge so it does not
        /// vanish under UI or overlap with controls, but is capped to prevent an
        /// oversized margin from collapsing the usable area.
        /// </summary>
        /// <param name="screenX">Grave's x coordinate in screen pixels.</param>
        /// <param name="screenY">Grave's y coordinate in screen pixels.</param>
        /// <param name="screenZ">Grave's z coordinate, the distance from the camera; negative means behind.</param>
        /// <param name="screenWidth">The width of the viewport in pixels.</param>
        /// <param name="screenHeight">The height of the viewport in pixels.</param>
        /// <param name="margin">The minimum distance from the screen edge to place the marker.</param>
        /// <returns>A MarkerPlacement describing where and how to draw the marker; Visible=false if it cannot be placed.</returns>
        public static MarkerPlacement Place(
            float screenX, float screenY, float screenZ,
            float screenWidth, float screenHeight, float margin)
        {
            if (screenWidth <= 0f || screenHeight <= 0f) return MarkerPlacement.Hidden;

            float centreX = screenWidth / 2f;
            float centreY = screenHeight / 2f;

            float x = screenX;
            float y = screenY;
            if (screenZ < 0f)
            {
                x = centreX - (screenX - centreX);
                y = centreY - (screenY - centreY);
            }

            bool onScreen = screenZ >= 0f && x >= 0f && x <= screenWidth && y >= 0f && y <= screenHeight;
            if (onScreen) return new MarkerPlacement(true, x, y, true, 0f);

            // Cap the inset at just under half the screen so an oversized
            // margin cannot push the two edges past each other.
            float insetX = Math.Min(margin, centreX * 0.9f);
            float insetY = Math.Min(margin, centreY * 0.9f);

            float dirX = x - centreX;
            float dirY = y - centreY;
            if (dirX == 0f && dirY == 0f) dirY = 1f;

            // Walk out from the centre until the first edge is reached.
            float scaleX = dirX == 0f ? float.MaxValue : (centreX - insetX) / Math.Abs(dirX);
            float scaleY = dirY == 0f ? float.MaxValue : (centreY - insetY) / Math.Abs(dirY);
            float scale = Math.Min(scaleX, scaleY);

            float angle = (float)(Math.Atan2(dirX, dirY) * 180.0 / Math.PI);
            if (angle < 0f) angle += 360f;

            return new MarkerPlacement(
                true, centreX + dirX * scale, centreY + dirY * scale, false, angle);
        }
    }
}
