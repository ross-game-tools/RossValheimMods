namespace ItemDrawers.Core
{
    public enum HandleStyle { None, Bar, Knobs, Pull }

    /// <summary>
    /// Drawer geometry, in metres. Settled interactively; see
    /// docs/drawer-spec.md. The overall cube size (0.66m, not 1m) is
    /// measured, not tuned: extracted from KG's shipped
    /// kg_itemdrawers asset bundle with UnityPy -- every drawer variant's
    /// Cube transform has scale=(0.66, 0.66, 0.66) on a unit-AABB mesh, and
    /// its snap points sit at +/-0.33 on each axis, which is exactly half
    /// of 0.66. Every other absolute-metre constant below (FrameThickness,
    /// RecessDepth, Bevel, HandleWidth, HandleSection, HandleProud) is
    /// scaled by the same 0.66 factor from the values tuned against a 1m
    /// cube in the original interactive preview, so the approved
    /// proportions carry over rather than becoming relatively chunkier on
    /// the smaller drawer.
    /// </summary>
    public sealed class DrawerProportions
    {
        public float Width = 0.660f;
        public float Height = 0.660f;
        public float Depth = 0.660f;
        public float FrameThickness = 0.0330f;
        public float RecessDepth = 0.0198f;
        public float Bevel = 0.00396f;
        public float HandleWidth = 0.2640f;
        public float HandleSection = 0.0429f;
        /// <summary>Handle block depth. The recess consumes RecessDepth of it, so net protrusion past the frame face is HandleProud - RecessDepth. The two constants are coupled: raising RecessDepth above HandleProud sinks the handle below the frame.</summary>
        public float HandleProud = 0.0231f;
        public HandleStyle Handle = HandleStyle.Bar;

        /// <summary>How much of the frame opening the icon fills. Approved at 0.98.</summary>
        public float LabelScale = 0.98f;

        /// <summary>
        /// Label size is derived, not free: the frame opening minus handle
        /// clearance, times LabelScale. docs/drawer-spec.md records 0.691 for
        /// the original 1m-cube proportions (and ~0.456 for the measured
        /// 0.66m cube these defaults now use, since every dimension feeding
        /// this calculation scaled by the same 0.66 factor); this recomputes
        /// it so the two cannot drift.
        /// </summary>
        public float LabelSize
        {
            get
            {
                float openW = Width - 2f * FrameThickness;
                float openH = Height - 2f * FrameThickness;
                if (Handle == HandleStyle.Bar) openH -= HandleSection * 3.0f;
                float min = openW < openH ? openW : openH;
                float size = min * LabelScale;
                return size < 0.05f ? 0.05f : size;
            }
        }
    }
}
