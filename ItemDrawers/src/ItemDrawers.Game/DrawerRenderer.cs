using ItemDrawers.Core;
using TMPro;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// A drawer's face. Four vertices for the icon whose UVs address the
    /// shared atlas, plus a 3D TextMeshPro for the count. No Canvas, and no
    /// per-drawer material, so every drawer in a wall batches together.
    ///
    /// Construction is split across two phases, and that split is load-
    /// bearing, not stylistic: Attach/BuildGraph run at prefab-build time,
    /// while the whole prefab hierarchy is deliberately inactive (see
    /// DrawerPieces.BuildPrefab's SetActive(false) comment -- required so
    /// ZNetView doesn't destroy itself before ZDOMan exists). TextMeshPro's
    /// own Awake, which is what actually creates its runtime material from
    /// its font asset, does not run on an inactive GameObject. Reading or
    /// writing almost any TMP_Text property before that Awake has run --
    /// outlineWidth first among them -- reaches into that not-yet-created
    /// material and throws a NullReferenceException in
    /// TextMeshPro.SetOutlineThickness. So BuildGraph only ever builds the
    /// object graph (GameObjects, components, the mesh, positions) and
    /// stores what Awake will need; every TMP property that touches a
    /// material or font is deferred to Awake, which fires once per real,
    /// placed drawer clone.
    ///
    /// That alone is not enough, though: Unity does not guarantee Awake
    /// order between sibling/parent-child components on a freshly activated
    /// object, so TextMeshPro's own Awake can run before this class's Awake
    /// gets a chance to assign a font, and it warns loudly ("Font Asset was
    /// not found") the moment it does. The "count" child is therefore kept
    /// independently inactive (its own SetActive(false), set before
    /// AddComponent, which survives cloning onto every placed instance)
    /// until this class's Awake has configured it -- font, then everything
    /// else that depends on it -- and only then activates it. TextMeshPro's
    /// first-ever Awake for that instance cannot fire before that point, so
    /// it never finds a missing font to warn about.
    ///
    /// One more thing every clone inherits from BuildGraph, and the one
    /// that turned out to matter most: BuildGraph runs exactly once, on the
    /// shared template, so `_quad` was one specific Mesh object. Chasing
    /// what that meant for Instantiate cost three separate rounds of
    /// contradictory symptoms (round 18: every clone shared one Mesh,
    /// visible as IndexOutOfRangeException; round 19: that Mesh being null
    /// on a clone, visible as NullReferenceException at the same call
    /// site) before the actual, simpler explanation surfaced: fields with
    /// no [SerializeField] are NOT reliably carried from a template to an
    /// Instantiate() clone, including intra-hierarchy component references
    /// like `_iconFilter`/`_iconRenderer`/`_countText` (set once by
    /// BuildGraph, on the template, via AddComponent -- never re-acquired
    /// per instance) and plain data like `_labelSize`. This is why
    /// TryConfigureCountText -- and therefore DrawerFont.Shared, and that
    /// property's own diagnostic logging -- never ran at all on a real
    /// clone: `_countText` was null, so its very first guard clause
    /// returned silently, every single time, on every drawer.
    ///
    /// Fixed at the root rather than patched around: `_iconFilter`,
    /// `_iconRenderer`, `_countText`, and `_labelSize` are now
    /// [SerializeField]. That is Unity's own, documented mechanism for
    /// exactly this guarantee -- correct intra-hierarchy remapping onto
    /// every clone -- and removes any dependence on the undocumented,
    /// apparently-inconsistent behavior of non-serialized fields that cost
    /// three rounds to fully characterise. `_quad`, `_corners`, and `_uvs`
    /// still don't need it: `_quad` is rebuilt fresh in Awake every time
    /// (never trusted from the template), `_corners` is now recomputed
    /// fresh from `_labelSize` in Awake for the same reason, and `_uvs` is
    /// populated fresh per Show() call regardless of instance history.
    /// </summary>
    public class DrawerRenderer : MonoBehaviour
    {
        // [SerializeField] on all four: this is what guarantees Unity
        // carries the correct, per-clone values across Object.Instantiate,
        // with intra-hierarchy references (_iconFilter/_iconRenderer/
        // _countText point at children of THIS transform) correctly
        // remapped to the clone's own children rather than the template's.
        // See the class docstring for the three rounds of symptoms that
        // preceded understanding why this was necessary.
        [SerializeField] private MeshFilter _iconFilter;
        [SerializeField] private MeshRenderer _iconRenderer;
        [SerializeField] private TextMeshPro _countText;
        [SerializeField] private float _labelSize;

        // Rebuilt/repopulated fresh every time they're needed (Awake for
        // _quad/_corners, every Show() call for _uvs), so none of the three
        // depend on whatever an instance inherited from the template.
        private Mesh _quad;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector2[] _uvs = new Vector2[4];

        private string _shownItem;
        private int _shownAmount = -1;

        // Set once TryConfigureCountText actually configures and activates
        // the count text (i.e. a font was found). false forever if no font
        // has ever been found yet -- checked by DrawerManager's own tick
        // (see NeedsFont) to retry on a later frame, since Awake only ever
        // gets one attempt and DrawerFont.Shared can legitimately find
        // nothing the first time it's asked (Valheim's own UI may not have
        // created any TMP_Text yet that early) without ever caching that
        // failure.
        private bool _countTextConfigured;

        // Show() has thrown twice now (round 18: IndexOutOfRangeException
        // from the shared-mesh bug; round 19: NullReferenceException at the
        // same SetUVs call site after the template mesh it cloned from had
        // been destroyed). Icon and count are guarded SEPARATELY
        // (_iconFailureLogged / _countFailureLogged) rather than one flag
        // for the whole method: a failure specific to one must not also
        // suppress the other, which is exactly what one shared try/catch
        // around the whole body did before -- a broken icon path meant the
        // count never even got a chance to show, and vice versa. Each logs
        // once, then goes quiet; per-instance, not static, since a failure
        // on one drawer says nothing about whether another drawer's Show()
        // is fine.
        private bool _iconFailureLogged;
        private bool _countFailureLogged;

        public static DrawerRenderer Attach(GameObject drawer, DrawerProportions p, Material faceplate)
        {
            var root = new GameObject("label");
            root.transform.SetParent(drawer.transform, false);

            float size = p.LabelSize;
            float y = p.Handle == HandleStyle.Bar ? p.HandleSection * 1.1f : 0f;

            // Clearance in front of the recessed panel (DrawerMeshBuilder's
            // carcass front face, at Depth/2 - RecessDepth exactly -- see
            // AddChamferBox's centre/depth there), just enough that the
            // label plane doesn't z-fight with that flat background.
            //
            // This is an ABSOLUTE metre value, deliberately NOT scaled with
            // Depth the way every other constant in DrawerProportions is.
            // A previous round changed this from a flat 0.004f to
            // `Depth * 0.004f` on the theory that it should track the same
            // 0.66 scale factor as everything else -- but that produced
            // 2.6mm at the current 0.66m Depth, smaller than the 4mm it
            // replaced, and made the z-fighting worse (camera-angle-
            // dependent flicker between the label and the panel behind it,
            // not a positioning error -- the two were nearly coplanar and
            // the depth buffer picked a winner per pixel per angle). Z-
            // fighting is a function of depth-buffer precision at the
            // render distance involved, which does not shrink just because
            // the model does; a small drawer needs the SAME absolute
            // separation as a large one to keep two surfaces distinguishable
            // to the depth buffer, not a proportionally smaller one. 0.01f
            // (1cm) is used here: comfortably larger than either the old
            // 4mm or the broken 2.6mm, and still safely less than
            // RecessDepth (~2cm at the current size), so the label stays
            // inside the recess rather than poking past the frame's own
            // front face.
            // Raised again, from 0.01, after z-fighting survived the
            // faceplate-centring fix -- visible at mid range and a grazing
            // camera angle, which is the signature of parallel surfaces
            // that are too CLOSE rather than misplaced. The plate sits at
            // Clearance minus BuildFaceplate's own offset, so that was only
            // 5mm above the recessed panel; near-edge-on, the depth
            // difference two parallel planes project into shrinks toward
            // nothing and the depth buffer stops separating them
            // reliably. 16mm here leaves the plate 10mm above the panel --
            // double the old separation -- while staying inside the ~19.8mm
            // recess, so neither the plate nor the icon pokes past the
            // frame's own front face.
            // Budget for the whole label stack, and the two constants that
            // split it (this one and BuildFaceplate's BehindIcon) only mean
            // anything together. From the recessed panel outward:
            //
            //     panel ---- 11mm ---- faceplate ---- 11mm ---- icon/count
            //
            // Three rounds went into widening the panel-to-plate gap before
            // the evidence said that was the wrong pair. What actually
            // fights is the icon and count against the plate, and the
            // history says so plainly in hindsight: the artifact tracked
            // BehindIcon, not this constant. 5mm eased when BehindIcon went
            // to 6mm and came back in full when it dropped to 4mm, while
            // this one tripled from 5mm to 15mm across the same rounds
            // without settling it.
            //
            // Two tells separate this from ordinary depth precision. It
            // pulses with the camera STATIONARY -- a fixed view has a fixed
            // depth comparison, so real z-fighting holds a steady pattern
            // rather than blinking; the blink is temporal AA jittering the
            // projection sub-pixel each frame and flipping a marginal
            // comparison. And it appeared only from one side, which is the
            // sun: the marginal comparison is visible where the contrast
            // between the two surfaces is highest, not where the geometry
            // differs.
            //
            // 22mm total puts the icon 2.2mm proud of the frame's own front
            // face, since the recess is only ~19.8mm deep. That is
            // deliberate and is what buys the second 11mm gap -- at this
            // scale it reads as a plaque sitting on the drawer front, which
            // is what it is.
            const float Clearance = 0.022f;
            float z = p.Depth / 2f - p.RecessDepth + Clearance;
            root.transform.localPosition = new Vector3(0f, y, z);

            // Position alone is not orientation. Live evidence settled this:
            // the icon was invisible until its winding was flipped, and even
            // after that the count text still rendered MIRRORED -- the
            // signature of the whole label root facing the wrong way, not
            // two independent per-child bugs. `DrawerMeshBuilder` builds the
            // recessed panel and handle toward the DRAWER's own +Z (see
            // `panelZ = p.Depth/2f - recess` there), which is the direction
            // this root's own `z` above already leans toward -- but Unity
            // does not guarantee a freshly parented child's "forward" reads
            // the same as its parent's just because their local axes are
            // unrotated relative to each other in the editor sense; what
            // actually matters is which way a child plane's own front faces
            // once instantiated in the placed piece's real orientation, and
            // the live symptom says it was backward. Fixing it at the root
            // -- a single 180-degree turn about Y -- reorients both children
            // at once (a physical turn, not a texture flip: this is the same
            // fix as spinning a sheet of backward-printed text around a
            // vertical axis until it reads correctly from the front, rather
            // than mirroring the ink on the page). The icon's own winding
            // reversal from the previous round is reverted below (see
            // BuildGraph/Awake) now that the root itself is corrected --
            // with the root facing the right way, the ORIGINAL winding was
            // already correct, and compensating at both levels would cancel
            // back to wrong.
            root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var renderer = root.AddComponent<DrawerRenderer>();
            renderer.BuildGraph(size, faceplate, p);
            return renderer;
        }

        /// <summary>
        /// Vertical layout inside the LabelSize x LabelSize box: the icon
        /// occupies the upper portion, the count sits just below it with a
        /// small gap, and the two together never exceed LabelSize in either
        /// dimension. Every offset here is derived from the icon's own
        /// half-height plus a gap -- not a fraction that happened to look
        /// right against the old, larger drawer (the previous
        /// `-size * 0.62f` text position, which put the count 0.28m below
        /// centre on a 0.66m-tall drawer -- at or past the bottom edge).
        /// Shared by BuildGraph, Awake (which must recompute the icon quad
        /// fresh per clone -- see the class docstring), and
        /// TryConfigureCountText (which needs the count's target box), so
        /// the three can never disagree about where anything sits.
        /// </summary>
        private static void ComputeLayout(
            float size,
            out float iconSize,
            out float iconCenterY,
            out float textCenterY,
            out float textTargetWidth,
            out float textTargetHeight)
        {
            iconSize = size * 0.65f;
            float gap = size * 0.05f;
            float textBandHeight = size - iconSize - gap;

            float topY = size / 2f;
            iconCenterY = topY - iconSize / 2f;
            float iconBottomY = iconCenterY - iconSize / 2f;
            textCenterY = iconBottomY - gap - textBandHeight / 2f;

            // A small margin inside each of the two bounds above so the
            // fitted text never touches the icon or the frame. Widened
            // from 0.92 (both axes) -- "the text is a bit hard to read,
            // maybe... font size a smidge larger" -- which, since
            // TryConfigureCountText always scales the widest possible
            // string ("88888") to exactly fill this box (see there), is
            // the one lever that makes the fitted text bigger without
            // touching the measure-and-fit mechanism itself. Still a real
            // margin, just a smaller one; verified against the physical
            // 0.66m drawer face that the widest count (10,000, black
            // marble's stack limit, five digits) still fits inside it --
            // see the fit-scale reasoning in TryConfigureCountText's
            // docstring.
            textTargetWidth = size * 0.96f;
            textTargetHeight = textBandHeight * 0.96f;
        }

        /// <summary>
        /// A flat panel filling the recess, in a lightened version of the
        /// tier's own material.
        ///
        /// This exists to answer "which side is the front", which stopped
        /// being obvious once the tiers were re-hued to match their build
        /// materials. Before that, every tier was a pale tan and the
        /// recess read clearly as a shadowed inset; afterwards -- on Black
        /// Marble especially, whose body is near-black -- the recess, the
        /// handle and the carcass all sit within a few percent of each
        /// other in value, and the front face is indistinguishable from the
        /// back at a glance.
        ///
        /// A brightness cue rather than a geometric one because the
        /// geometry was already there and already wasn't enough: the recess
        /// and handle exist and are correctly shaped, but shading alone
        /// cannot separate them on a body this dark. Lightening a surface
        /// that is already in shadow is also the physically odd choice, so
        /// it is deliberately read as a label plate set into the drawer
        /// front -- which is what it is, since the icon and count sit
        /// directly on top of it.
        ///
        /// Sized to the recess OPENING (Width/Height minus the frame on
        /// both sides), not to LabelSize: LabelSize carries an extra 0.98
        /// inset so text never touches the frame, and a plate at that size
        /// would leave a thin mismatched border of carcass showing around
        /// it.
        ///
        /// Costs one extra material and one extra draw call PER TIER, not
        /// per drawer -- the material is built once in DrawerPieces and
        /// shared by every drawer of that tier, and this quad is four
        /// vertices. A hundred-drawer wall pays three draw calls total for
        /// it, which is why this is a shared material and not a per-
        /// instance tint.
        /// </summary>
        private void BuildFaceplate(Material faceplate, DrawerProportions p)
        {
            // No material means DrawerPieces could not build one for this
            // tier. Skip the plate rather than adding an untextured magenta
            // quad across the drawer front -- the drawer is still fully
            // usable without it, and a missing cue beats a broken one.
            if (faceplate == null) return;

            float w = (p.Width - 2f * p.FrameThickness) / 2f;
            float h = (p.Height - 2f * p.FrameThickness) / 2f;

            // Undo the label root's own vertical offset. The root is lifted
            // by HandleSection * 1.1 so the icon and count clear the handle
            // (see Attach), and this plate is its child -- so a plate built
            // symmetrically about the root's origin sits that far ABOVE the
            // opening it is meant to fill, overlapping the top frame plank
            // and z-fighting against it along the whole top edge. The icon
            // and count want the offset; the plate does not.
            float rootLift = p.Handle == HandleStyle.Bar ? p.HandleSection * 1.1f : 0f;

            // A hair inside the opening on every side. At exactly the
            // opening size the plate's edges are coplanar with the recess
            // side walls, which is its own z-fighting seam -- distinct from
            // the offset bug above, and it would have survived fixing it.
            const float EdgeInset = 0.001f;
            w -= EdgeInset;
            h -= EdgeInset;

            // Behind the icon and count, toward the recessed panel. The
            // label root is rotated 180 degrees about Y (see Attach), so
            // this object's local +Z points INTO the drawer -- the opposite
            // of what the sign suggests if you read it against the drawer's
            // own axes. 5mm sits comfortably inside Attach's 10mm clearance
            // from the panel, so the plate neither z-fights the panel
            // behind it nor reaches the icon in front.
            // Paired with Attach's Clearance, and the two only mean
            // anything together: the plate ends up at (Clearance -
            // BehindIcon) above the recessed panel, and the icon sits
            // BehindIcon in front of the plate. At 0.016 / 0.006 that is
            // 10mm of panel-to-plate separation -- what the grazing-angle
            // z-fighting needed -- and 6mm of plate-to-icon, which was
            // never fighting and does not need more.
            // Half of Attach's Clearance, which splits the label budget
            // evenly: the plate lands at (Clearance - BehindIcon) above the
            // recessed panel, and the icon and count sit BehindIcon in
            // front of the plate. 0.022 / 0.011 gives both pairs 11mm.
            //
            // The icon/count-to-plate gap is the one that was actually
            // fighting -- see Attach for the evidence -- and it had been
            // starved down to 4mm while the other pair was repeatedly
            // widened. Splitting evenly rather than swinging the imbalance
            // the other way: at 11mm each, neither pair is close enough for
            // temporal AA jitter to flip.
            const float BehindIcon = 0.011f;

            var mesh = new Mesh { name = "rid_faceplate_quad" };
            // Same winding as the icon quad for the same reason -- see
            // BuildGraph -- so the plate faces out of the drawer front
            // rather than being back-face culled into invisibility.
            mesh.SetVertices(new[]
            {
                new Vector3(-w, -h - rootLift, BehindIcon),
                new Vector3(-w,  h - rootLift, BehindIcon),
                new Vector3( w,  h - rootLift, BehindIcon),
                new Vector3( w, -h - rootLift, BehindIcon),
            });
            // Full 0-1 UVs: the tier albedo is seamless and tiles, so the
            // plate samples the same grain as the body rather than a
            // stretched crop of it.
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f),
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("faceplate");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = faceplate;
            // A flat plate inside a recess casts nothing worth the cost,
            // and shadow casting on a hundred-drawer wall is exactly the
            // sort of per-object work this mod was built to avoid.
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        /// <summary>
        /// Prefab-build time, on the inactive template. Builds the child
        /// GameObjects, adds MeshFilter/MeshRenderer/TextMeshPro, and
        /// positions everything -- all plain object-graph and field writes
        /// that do not depend on any component's Awake having run. Must
        /// NEVER touch a TMP_Text property that reaches into its material
        /// (font, fontSize, color, outlineWidth, outlineColor, alignment,
        /// wrapping) or the icon's MeshRenderer.sharedMaterial -- see the
        /// class docstring for why, and Awake for where those move instead.
        /// </summary>
        private void BuildGraph(float size, Material faceplate, DrawerProportions p)
        {
            _labelSize = size;

            BuildFaceplate(faceplate, p);

            ComputeLayout(size, out float iconSize, out float iconCenterY, out float textCenterY,
                out float textTargetWidth, out float textTargetHeight);

            // Winding: {0,1,2,0,2,3} against corners [BL,TL,TR,BR] produces
            // a face normal of (0,0,-size^2) in THIS OBJECT'S OWN local
            // space, by Unity's own cross(v1-v0, v2-v0) convention -- i.e.
            // facing this object's local -Z. A previous round reversed this
            // winding to compensate for the icon being invisible, which
            // worked for the icon alone but left the count text mirrored --
            // exactly the signature of the real defect being the label
            // ROOT'S orientation, not this quad's winding (see Attach's
            // 180-degree Y rotation, added this round, which is the actual
            // fix). With the root corrected, local -Z is once again the
            // right way for this quad to face, so the original winding
            // belongs here, not a second, compensating flip.
            float h = iconSize / 2f;
            _corners[0] = new Vector3(-h, iconCenterY - h, 0f);
            _corners[1] = new Vector3(-h, iconCenterY + h, 0f);
            _corners[2] = new Vector3(h, iconCenterY + h, 0f);
            _corners[3] = new Vector3(h, iconCenterY - h, 0f);

            _quad = new Mesh { name = "rid_label_quad" };
            _quad.SetVertices(_corners);
            _quad.SetUVs(0, _uvs);
            _quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            _quad.RecalculateNormals();
            _quad.RecalculateBounds();
            _quad.MarkDynamic();

            var iconGo = new GameObject("icon");
            iconGo.transform.SetParent(transform, false);
            _iconFilter = iconGo.AddComponent<MeshFilter>();
            _iconFilter.sharedMesh = _quad;
            _iconRenderer = iconGo.AddComponent<MeshRenderer>();
            _iconRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _iconRenderer.receiveShadows = false;
            // sharedMaterial is NOT set here -- see Awake.

            var textGo = new GameObject("count");
            // Set inactive BEFORE AddComponent, and independently of the
            // parent hierarchy's own active state -- see Awake for why this
            // one child needs its own flag rather than relying on the
            // template being inactive the way everything else here does.
            textGo.SetActive(false);
            textGo.transform.SetParent(transform, false);
            // Positioned just below the icon's own bottom edge plus a gap
            // (see ComputeLayout) -- not a fraction of the label size that
            // happened to look right on the old, larger drawer. The old
            // `-size * 0.62f` put the count 0.62 x LabelSize below centre,
            // which on the resized 0.66m drawer (LabelSize = 0.456) is
            // 0.28m down -- at or past the bottom edge of a 0.66m-tall
            // drawer.
            textGo.transform.localPosition = new Vector3(0f, textCenterY, 0f);
            _countText = textGo.AddComponent<TextMeshPro>();
            // No other TMP property is set here -- see Awake/TryConfigureCountText.

            // Actual sizing/fitting happens in TryConfigureCountText, which
            // measures TMP's own rendered bounds rather than predicting
            // them -- see that method. This RectTransform is set only for
            // clarity/hover-picking; it does not drive font size (auto-
            // sizing is no longer used at all -- see TryConfigureCountText).
            var rect = textGo.GetComponent<RectTransform>();
            if (rect != null) rect.sizeDelta = new Vector2(textTargetWidth, textTargetHeight);
        }

        /// <summary>
        /// Per placed instance, once Valheim activates the real drawer.
        /// </summary>
        private void Awake()
        {
            // Recomputed fresh from _labelSize -- now [SerializeField], so
            // guaranteed to carry this instance's real value -- rather than
            // trusting BuildGraph's original _corners array to have
            // survived on its own. Never Instantiate/reuse the template's
            // own Mesh either: that shared object's runtime state (in
            // particular, whether some earlier drawer of this tier already
            // destroyed it via OnDestroy -- see the class docstring) is not
            // something a clone should depend on. Every clone gets a brand
            // new Mesh it owns outright.
            ComputeLayout(_labelSize, out float iconSize, out float iconCenterY, out _, out _, out _);
            float h = iconSize / 2f;
            _corners[0] = new Vector3(-h, iconCenterY - h, 0f);
            _corners[1] = new Vector3(-h, iconCenterY + h, 0f);
            _corners[2] = new Vector3(h, iconCenterY + h, 0f);
            _corners[3] = new Vector3(h, iconCenterY - h, 0f);

            _quad = new Mesh { name = "rid_label_quad" };
            _quad.SetVertices(_corners);
            _quad.SetUVs(0, _uvs);
            _quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            _quad.RecalculateNormals();
            _quad.RecalculateBounds();
            _quad.MarkDynamic();
            if (_iconFilter != null) _iconFilter.sharedMesh = _quad;

            TryConfigureCountText();

            if (_iconRenderer != null)
                _iconRenderer.sharedMaterial = DrawerIconAtlas.SharedMaterial;

            // Confirmed live: [SerializeField] on _iconFilter/_iconRenderer/
            // _countText/_labelSize correctly survives Object.Instantiate
            // onto every placed clone (see the class docstring for the
            // three rounds that preceded understanding why that attribute
            // was necessary). A null here would mean that guarantee broke
            // -- a real regression worth a loud warning, not routine status
            // worth logging on every drawer every placement.
            if (_iconFilter == null || _iconRenderer == null || _countText == null)
            {
                DrawerPlugin.Log.LogWarning(
                    $"DrawerRenderer on '{gameObject.name}' is missing an expected child reference "
                    + $"(iconFilter={(_iconFilter != null ? "ok" : "NULL")}, "
                    + $"iconRenderer={(_iconRenderer != null ? "ok" : "NULL")}, "
                    + $"countText={(_countText != null ? "ok" : "NULL")}) -- its label will not render correctly.");
            }

            Hide();
        }

        /// <summary>
        /// True while the count text has never successfully been configured
        /// (no font found yet). DrawerManager's own tick checks this on
        /// every drawer it visits during its periodic face sync and calls
        /// TryConfigureCountText() again -- Awake only gets one attempt, and
        /// DrawerFont.Shared can legitimately find nothing that early
        /// (Valheim's own UI may not have created any TMP_Text yet) without
        /// that being permanent.
        /// </summary>
        public bool NeedsFont => _countText != null && !_countTextConfigured;

        /// <summary>
        /// Resolves a font and finishes configuring the count text, font
        /// first since everything else here reaches into the material the
        /// font asset supplies (see the class docstring on why order
        /// matters). Safe to call repeatedly and does nothing once already
        /// configured; if DrawerFont.Shared still finds nothing, this is a
        /// silent no-op ready to be called again later -- DrawerFont itself
        /// never caches a null, so every call is a genuine fresh attempt.
        /// </summary>
        /// <summary>
        /// The widest string this drawer can ever display: a stack limit of
        /// 10,000 (the marble tier) shows 5 digits. Fitting against this,
        /// rather than whatever count happens to be showing right now,
        /// means the fitted scale is fixed once and never changes again --
        /// no visible resize as a drawer's count crosses a digit boundary.
        /// "8" rather than "9" or "0" only because most fonts render it at
        /// least as wide as any other digit; the exact digit doesn't matter
        /// since every digit in a font asset should share advance width,
        /// but this avoids depending on that being true.
        /// </summary>
        private const string WidestCount = "88888";

        public void TryConfigureCountText()
        {
            if (_countTextConfigured || _countText == null) return;

            var font = DrawerFont.Shared;
            if (font == null) return;

            var textGo = _countText.gameObject;
            _countText.font = font;
            _countText.alignment = TextAlignmentOptions.Center;

            // No auto-sizing, and no multiplier from LabelSize to a font
            // size: TMP's world-space font size is in the font asset's own
            // units, which do not map to world metres by any fixed
            // constant (it depends on that font's internal point size).
            // That is exactly why a flat 500 was absurd and a guessed
            // `_labelSize * 9` was STILL too big -- neither was ever
            // verified against anything real. Fixed here at a nominal seed
            // value; the actual size that ends up on screen is set below by
            // MEASURING TMP's own rendered mesh and scaling the whole text
            // object's transform to fit, not by predicting a font size.
            _countText.enableAutoSizing = false;
            _countText.fontSize = _labelSize;

            _countText.color = new Color32(246, 236, 214, 255);
            // Outline and face weight are two separate knobs, and three
            // rounds of tuning went into one of them before that was clear.
            // TMP grows the outline INWARD from the glyph edge, so raising
            // outlineWidth eats the digit's own stroke rather than fringing
            // it -- at 0.32 "the dark border is most of the numbers".
            // Dropping to 0.18 fixed the border but left the numerals
            // themselves looking thin, which is NOT an outline problem: the
            // face is simply light at this size, and thickening the outline
            // again to compensate only walks back into the first problem.
            // So the outline stays at the 0.18 that read correctly, and the
            // weight of the digits comes from _FaceDilate instead (set
            // after activation below), which pushes the SDF face outward
            // and is the only property that makes the strokes heavier.
            _countText.outlineWidth = 0.18f;
            _countText.outlineColor = new Color32(12, 9, 6, 235);
            // enableWordWrapping is obsolete under Unity 6000's TextMeshPro
            // (the brief predates that Unity version); textWrappingMode is
            // the current replacement.
            _countText.textWrappingMode = TextWrappingModes.NoWrap;
            _countText.raycastTarget = false;

            // Only now does TextMeshPro's own Awake get to run -- with a
            // font already assigned, so it never warns. It must run before
            // ForceMeshUpdate below can produce a real mesh to measure.
            textGo.SetActive(true);

            // Deferred until after activation on purpose. fontMaterial is
            // the per-instance material copy, and it only exists once TMP's
            // own Awake has created it from the font asset -- the same
            // not-yet-created material the class docstring describes. Set
            // here it is guaranteed real; set above, alongside the other
            // TMP properties, it would be reaching for it a frame early.
            //
            // fontMaterial rather than fontSharedMaterial: the shared one
            // would push this weight onto every other TMP_Text using the
            // same font asset, Valheim's own HUD included. It costs no
            // extra batching, since outlineWidth/outlineColor above already
            // force TMP to instance this text's material regardless.
            //
            // Dilation is applied in the shader against the signed-distance
            // field, not to the generated mesh, so textBounds and the fit
            // measurement below are unaffected and stay valid.
            _countText.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0.15f);

            // Same tie-breaking fix as the icon atlas material -- see the
            // renderQueue comment in DrawerIconAtlas for why draw order,
            // not separation, is what actually settles this. One past the
            // icon so the count draws last of all three label surfaces;
            // they never overlap each other, so the exact order between
            // them is arbitrary, but a defined one costs nothing.
            _countText.fontMaterial.renderQueue = 2451;

            ComputeLayout(_labelSize, out _, out _, out _, out float targetWidth, out float targetHeight);

            // Measure the actual rendered size against the widest string
            // this drawer can ever show, then scale the text object so
            // that measured size fits inside the label's target box. This
            // is the "measure, don't predict" fix: textBounds after
            // ForceMeshUpdate is TMP's own report of what it actually drew,
            // in local units, at the fontSize just set above -- not a
            // number derived from any assumption about how that fontSize
            // relates to world metres.
            _countText.SetText(WidestCount);
            _countText.ForceMeshUpdate();
            Bounds before = _countText.textBounds;

            float scale = 1f;
            if (before.size.x > 0.0001f && before.size.y > 0.0001f)
            {
                float scaleW = targetWidth / before.size.x;
                float scaleH = targetHeight / before.size.y;
                scale = Mathf.Min(scaleW, scaleH);
            }
            else
            {
                // A zero-size measurement means TMP produced no mesh at
                // all for the widest count string -- the count would then
                // render at whatever the unscaled nominal fontSize happens
                // to be, un-fitted. A real anomaly (bad font asset, TMP
                // failing to lay out text at all), not routine status.
                DrawerPlugin.Log.LogWarning(
                    $"DrawerRenderer on '{gameObject.name}': count text measured zero size while fitting "
                    + $"'{WidestCount}' -- it will not be scaled to fit the label.");
            }
            textGo.transform.localScale = new Vector3(scale, scale, scale);

            // Hide the "88888" placeholder used for measurement -- this
            // method can also run later, well after this drawer's own
            // Awake/Hide already ran once (see NeedsFont / DrawerManager's
            // retry), so it must not leave that placeholder on screen. The
            // real count is (re)applied by the next Show() call.
            _countText.enabled = false;

            _countTextConfigured = true;
        }

        /// <summary>
        /// Repoints the icon quad's UVs into DrawerIconAtlas and updates the
        /// count text. Uses TryGetUv's min/max directly, with no V flip:
        /// AtlasLayout.TryGetUv returns v = rect.Y / Height, uninverted, and
        /// that already matches both Texture2D.SetPixels32's bottom-left
        /// origin (used to blit each icon in DrawerIconAtlas.Build) and
        /// Unity's own bottom-left UV origin. Corner 0 (bottom-left of the
        /// quad, see _corners above) gets (min.x, min.y); corner 1
        /// (top-left) gets (min.x, max.y); and so on around the quad --
        /// mirroring this mapping would flip every icon vertically.
        ///
        /// Icon and count are drawn by two independently try/catch-guarded
        /// steps (ShowIcon, ShowCount), not one. An earlier version wrapped
        /// the whole body in a single try/catch: a failure anywhere in the
        /// icon path aborted the method before the count was ever touched,
        /// and vice versa -- so a font problem could take the icon down
        /// with it, and a mesh problem could take the count down with it,
        /// neither of which has anything to do with the other. Splitting
        /// them means a font failure costs only the number, and a mesh/UV
        /// failure costs only the icon.
        /// </summary>
        public void Show(string itemName, int amount)
        {
            if (itemName == _shownItem && amount == _shownAmount) return;   // nothing changed

            Vector2 min = Vector2.zero, max = Vector2.zero;
            bool visible = !string.IsNullOrEmpty(itemName) && DrawerIconAtlas.TryGetUv(itemName, out min, out max);

            ShowIcon(visible, itemName, min, max);
            ShowCount(visible, amount);

            _shownItem = itemName;
            _shownAmount = amount;
        }

        private void ShowIcon(bool visible, string itemName, Vector2 min, Vector2 max)
        {
            try
            {
                if (_iconRenderer == null) return;

                if (!visible)
                {
                    _iconRenderer.enabled = false;
                    return;
                }

                if (itemName != _shownItem && _quad != null)
                {
                    // Repointing four UVs is the whole cost of changing a
                    // drawer's item. No new material, no atlas rebuild.
                    _uvs[0] = new Vector2(min.x, min.y);
                    _uvs[1] = new Vector2(min.x, max.y);
                    _uvs[2] = new Vector2(max.x, max.y);
                    _uvs[3] = new Vector2(max.x, min.y);
                    _quad.SetUVs(0, _uvs);
                }

                _iconRenderer.enabled = true;
            }
            catch (System.Exception ex)
            {
                if (_iconFailureLogged) return;
                _iconFailureLogged = true;
                DrawerPlugin.Log.LogError(
                    $"DrawerRenderer icon path on '{gameObject.name}' threw and will not log "
                    + $"again for this drawer: {ex}");
            }
        }

        private void ShowCount(bool visible, int amount)
        {
            try
            {
                if (_countText == null) return;

                bool showCount = visible && amount > 0;
                _countText.enabled = showCount;
                if (showCount) _countText.SetText("{0}", amount);
            }
            catch (System.Exception ex)
            {
                if (_countFailureLogged) return;
                _countFailureLogged = true;
                DrawerPlugin.Log.LogError(
                    $"DrawerRenderer count path on '{gameObject.name}' threw and will not log "
                    + $"again for this drawer: {ex}");
            }
        }

        public void Hide()
        {
            if (_iconRenderer != null) _iconRenderer.enabled = false;
            if (_countText != null) _countText.enabled = false;
        }

        /// <summary>Distance culling, driven by DrawerManager. A wall of unreadable text is wasted work.</summary>
        public void SetLabelVisible(bool visible)
        {
            if (_iconRenderer == null || _countText == null) return;
            if (!visible)
            {
                _iconRenderer.enabled = false;
                _countText.enabled = false;
                return;
            }
            _iconRenderer.enabled = !string.IsNullOrEmpty(_shownItem);
            _countText.enabled = _shownAmount > 0;
        }

        private void OnDestroy()
        {
            // Safe now that Awake clones _quad per instance: destroying it
            // only ever affects this drawer's own mesh, never a sibling's.
            if (_quad != null) Destroy(_quad);
        }
    }
}
