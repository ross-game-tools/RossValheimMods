using System.Collections.Generic;
using System.Linq;
using ItemDrawers.Core;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ItemDrawers.Game
{
    internal static class MeshDataExtensions
    {
        public static Mesh ToUnityMesh(this MeshData data, string name)
        {
            var vertices = new Vector3[data.VertexCount];
            var normals = new Vector3[data.VertexCount];
            var uvs = new Vector2[data.VertexCount];
            var triangles = new int[data.VertexCount];

            for (int i = 0; i < data.VertexCount; i++)
            {
                vertices[i] = new Vector3(data.Vertices[i * 3], data.Vertices[i * 3 + 1], data.Vertices[i * 3 + 2]);
                normals[i] = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 1], data.Normals[i * 3 + 2]);
                uvs[i] = new Vector2(data.Uvs[i * 2], data.Uvs[i * 2 + 1]);
            }

            // Winding is passed through UNCHANGED. An earlier version swapped
            // the last two indices, reasoning that Core emits CCW-front under
            // the right-hand convention while Unity treats CW as front-facing.
            // Two independent derivations agreed on that and both were wrong:
            // in game the drawers rendered as dark boxes with holes, which is
            // what culled front faces look like when you are seeing interior
            // surfaces lit by outward-facing normals. Observation beats
            // derivation. If drawers ever render inside out again, swap
            // triangles[t*3+1] and triangles[t*3+2] here -- one line, and this
            // is the only place winding is decided.
            for (int t = 0; t < data.TriangleCount; t++)
            {
                triangles[t * 3] = t * 3;
                triangles[t * 3 + 1] = t * 3 + 1;
                triangles[t * 3 + 2] = t * 3 + 2;
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: false);
            return mesh;
        }
    }

    public static class DrawerPieces
    {
        private static Mesh _sharedMesh;
        private static GameObject _prefabTemplateContainer;

        /// <summary>
        /// An inactive, DontDestroyOnLoad holder for prefab templates while
        /// they're under construction -- our own equivalent of Jotunn's
        /// internal PrefabManager.PrefabContainer (inaccessible from this
        /// assembly). Parenting a template here keeps it out of Awake's
        /// reach without setting the template's own activeSelf to false,
        /// which is what let a placed CLONE inherit that false value too.
        /// See the parenting comment in BuildPrefab for the full story.
        /// </summary>
        private static GameObject PrefabTemplateContainer()
        {
            if (_prefabTemplateContainer == null)
            {
                _prefabTemplateContainer = new GameObject("rid_prefab_template_container");
                _prefabTemplateContainer.SetActive(false);
                Object.DontDestroyOnLoad(_prefabTemplateContainer);
            }
            return _prefabTemplateContainer;
        }

        public static void RegisterAll()
        {
            var proportions = new DrawerProportions();

            // The carcass is built with NO handle, and the handle is built
            // separately below. Two meshes rather than one because they
            // need two materials: the handle is metal and the body is wood,
            // stone or marble, and a single mesh carries a single material.
            //
            // DrawerMeshBuilder is untouched by this -- the handle is still
            // its geometry, via the ChamferBox entry point it already
            // exposes, with the same dimensions AddHandle would have used.
            // Only the placement changed (see HandleCenterY).
            var carcassProportions = new DrawerProportions { Handle = HandleStyle.None };
            _sharedMesh = DrawerMeshBuilder.Build(carcassProportions).ToUnityMesh("rid_drawer");
            _sharedHandleMesh = BuildHandleMesh(proportions);

            foreach (var tier in DrawerTiers.All)
            {
                // Each tier is independent, and a throw while building or
                // registering one must not cost the other two -- an
                // uncaught exception here previously aborted RegisterAll
                // entirely partway through the foreach, so one bad tier
                // (black marble, say) silently removed wood and stone from
                // the game as well, with nothing in the log pointing at
                // which tier was actually responsible.
                try
                {
                    RegisterOne(tier, proportions);
                }
                catch (System.Exception ex)
                {
                    DrawerPlugin.Log.LogError($"{tier}: registration threw and was skipped: {ex}");
                }
            }
        }

        private static Mesh _sharedHandleMesh;

        /// <summary>
        /// The handle bar, as its own mesh so it can wear its own material.
        ///
        /// Dimensions are AddHandle's Bar case verbatim -- same width clamp,
        /// same section, same depth, same chamfer, same panel-relative Z --
        /// so this is a relocation of that geometry, not a reimplementation
        /// of it. Only the vertical placement differs; see HandleCenterY.
        /// </summary>
        private static Mesh BuildHandleMesh(DrawerProportions p)
        {
            float frame = System.Math.Min(p.FrameThickness, System.Math.Min(p.Width, p.Height) / 2f - 0.02f);
            float recess = System.Math.Min(p.RecessDepth, p.Depth * 0.5f);
            float panelZ = p.Depth / 2f - recess;

            float bw = System.Math.Min(p.HandleWidth, p.Width - 2f * frame - 0.02f);
            float chamfer = System.Math.Min(p.Bevel, p.HandleSection / 2.5f);

            return DrawerMeshBuilder.ChamferBox(
                0f, HandleCenterY(p, frame), panelZ + p.HandleProud / 2f,
                bw, p.HandleSection, p.HandleProud, chamfer).ToUnityMesh("rid_drawer_handle");
        }

        /// <summary>
        /// Raised from AddHandle's own `HandleSection * 1.4f`, which left
        /// the bar close enough to the bottom of the opening that it read
        /// as part of the frame rather than as a handle sitting on the
        /// drawer front -- and a handle that reads as frame is no use as a
        /// which-way-is-front cue, which is the whole reason this pass
        /// exists.
        ///
        /// 2.4 puts roughly a full handle-section of gap between the bar
        /// and the bottom frame, so the gap is legible at the distance a
        /// wall of drawers is actually viewed from. Still well below the
        /// label's own band -- the label root clears the handle by
        /// HandleSection * 1.1 from centre (see DrawerRenderer.Attach) and
        /// the icon starts above that, so nothing collides.
        /// </summary>
        private static float HandleCenterY(DrawerProportions p, float frame)
        {
            float openH = p.Height - 2f * frame;
            return -openH / 2f + p.HandleSection * 2.4f;
        }

        /// <summary>
        /// How much brighter the front label plate is than the drawer body.
        ///
        /// Not one shared constant, because the tiers do not start from the
        /// same place. A multiply is proportional, so the same factor moves
        /// a near-black body by a fraction of what it moves a mid-tone one:
        /// Black Marble sits around 0.15 lightness and Stone around 0.49,
        /// so Stone x1.28 gains roughly 0.14 of absolute lightness while
        /// Black Marble x1.28 gains only 0.04 -- invisible, and Black
        /// Marble is the tier the front-face problem was reported on.
        /// These factors are chosen to land each tier's plate at a similar
        /// absolute separation from its own body instead.
        /// </summary>
        /// <summary>
        /// Absolute target lightness for the faceplate, the same for every
        /// tier.
        ///
        /// Absolute rather than "N times the body", which is what this was
        /// before and why Black Marble kept coming out wrong: a multiply is
        /// proportional, so one factor lands three tiers at three different
        /// separations, and the tier needing the most help gets the least.
        ///
        /// DARK, where this was 0.62 and light. The plate originally had to
        /// do two jobs -- mark the front, and back the icon and count --
        /// and a light plate is only good at the first. In direct sunlight
        /// a 0.62 plate blows out, and the label text is a near-white cream
        /// (see DrawerRenderer), so on the Wood and Stone tiers the count
        /// became light-on-light and unreadable exactly when the sun was
        /// on it.
        ///
        /// The metal handle now marks the front on its own, which frees the
        /// plate to be what the text actually needs: a consistently dark
        /// ground that holds contrast under any lighting. It still marks
        /// the front on Wood and Stone, being well below both bodies --
        /// only now by being darker instead of lighter.
        /// </summary>
        private const float FaceplateLightness = 0.16f;

        private static void RegisterOne(DrawerTier tier, DrawerProportions proportions)
        {
            var prefab = BuildPrefab(tier, _sharedMesh, proportions);
            if (prefab == null) return;

            var config = new PieceConfig
            {
                Name = DrawerTiers.DisplayName(tier),
                Description = "Holds a great many of a single item.",
                PieceTable = PieceTables.Hammer,
                CraftingStation = CraftingStations.Workbench,
                // DrawerConfig.RecipeFor, not DrawerTiers.Recipe: the
                // latter is only the default now, and reaching past the
                // config here would silently ignore a server admin's
                // edit. RecipeFor falls back to that same default, and
                // says so in the log, when a configured recipe cannot be
                // parsed.
                Requirements = DrawerConfig.RecipeFor(tier)
                    .Select(r => new RequirementConfig(r.Item, r.Amount, 0, true))
                    .ToArray()

                // Deliberately NOT setting Category here. Valheim 1.0's
                // build menu tabs are driven by Piece.m_usage
                // (ByUsagePieceList), not by m_category -- Category on
                // PieceConfig is a string Jotunn maps onto
                // Piece.PieceCategory, which is set directly below for
                // compatibility only, never relied on for visibility.
            };

            var custom = new CustomPiece(prefab, fixReference: false, config);

            if (!custom.IsValid())
            {
                DrawerPlugin.Log.LogError(
                    $"{DrawerTiers.PrefabName(tier)} FAILED Jotunn validation; not registered. "
                    + "See the Jotunn error immediately above for which check failed.");
                return;
            }

            if (!PieceManager.Instance.AddPiece(custom))
            {
                DrawerPlugin.Log.LogError($"AddPiece rejected {DrawerTiers.PrefabName(tier)}.");
                return;
            }

            DrawerPlugin.Log.LogInfo($"Registered {DrawerTiers.PrefabName(tier)}");
        }

        public static GameObject BuildPrefab(DrawerTier tier, Mesh sharedMesh, DrawerProportions proportions)
        {
            // Try each candidate donor in order and take the first that is
            // fully usable. A single hardcoded name fails silently: the tier
            // just never registers and the drawer is absent from the build
            // menu with nothing visibly wrong. That is exactly how
            // 'blackmarble_post01' -- a prefab that does not exist -- cost a
            // test cycle.
            GameObject donor = null;
            MeshRenderer donorRenderer = null;
            Piece donorPiece = null;
            string donorName = null;

            foreach (var candidate in DrawerTiers.MaterialDonors(tier))
            {
                var go2 = PrefabManager.Instance.GetPrefab(candidate);
                if (go2 == null)
                {
                    DrawerPlugin.Log.LogInfo($"{tier}: donor '{candidate}' not found, trying next.");
                    continue;
                }

                var r = go2.GetComponentInChildren<MeshRenderer>();
                var p = go2.GetComponent<Piece>();
                if (r == null || r.sharedMaterial == null || p == null || p.m_icon == null)
                {
                    DrawerPlugin.Log.LogInfo(
                        $"{tier}: donor '{candidate}' unusable "
                        + $"(renderer={(r == null ? "none" : "ok")}, "
                        + $"material={(r == null || r.sharedMaterial == null ? "none" : "ok")}, "
                        + $"piece={(p == null ? "none" : "ok")}, "
                        + $"icon={(p == null || p.m_icon == null ? "none" : "ok")}), trying next.");
                    continue;
                }

                donor = go2; donorRenderer = r; donorPiece = p; donorName = candidate;
                break;
            }

            if (donor == null)
            {
                DrawerPlugin.Log.LogError(
                    $"No usable material donor for {tier}. Tried: "
                    + string.Join(", ", DrawerTiers.MaterialDonors(tier))
                    + ". This tier will be missing from the build menu.");
                return null;
            }

            // Donor/shader/texture status is reported once, at the end of
            // this method, as a single collapsed Info line (see
            // `donorSummary` below) rather than the half-dozen separate
            // lines this used to print investigating a since-resolved
            // vanilla-shader/UV issue -- useful while that was being
            // diagnosed, just startup noise now that it's fixed.
            var go = new GameObject(DrawerTiers.PrefabName(tier));

            // Parented under an inactive container BEFORE any AddComponent,
            // rather than SetActive(false) on go itself. A GameObject is
            // active by default, so every component added below would
            // otherwise run its Awake immediately -- including
            // ZNetView.Awake, which destroys itself on the spot when
            // ZDOMan.instance is null:
            //
            //   private void Awake()
            //   {
            //       if (m_forceDisableInit || ZDOMan.instance == null)
            //       {
            //           UnityEngine.Object.Destroy(this);
            //           return;
            //       }
            //       ...
            //
            // This method runs from PrefabManager.OnVanillaPrefabsAvailable,
            // which fires before any world is loaded, so ZDOMan.instance is
            // null here every time.
            //
            // An earlier version of this fix called go.SetActive(false)
            // directly. That has a second-order effect that cost a full
            // investigation to find: activeSelf is copied onto every clone,
            // and Player.PlacePiece places a piece via a plain
            // Object.Instantiate(original, pos, rot) -- so every PLACED
            // drawer was ALSO instantiated inactive, ZNetView.Awake never
            // ran on the placed clone either, and it never created a ZDO
            // (ZNetView.GetZDO() is a bare `return m_zdo`, which stayed null
            // forever). A piece whose ZNetView has no ZDO cannot survive
            // placement; that alone explained the whole PlacePiece failure,
            // not just the one `cheated`-branch statement that happened to
            // be the first thing to dereference it.
            //
            // Parenting under an inactive container instead threads the
            // needle: activeInHierarchy (activeSelf AND every ancestor's
            // activeSelf) is false while parented here, so Awake still does
            // not run during construction -- but go's OWN activeSelf stays
            // true, so a clone instantiated WITHOUT this parent (exactly
            // what PlacePiece does) is active immediately and Awake runs
            // normally, the same as any vanilla piece. This is the same
            // pattern Jotunn and vanilla Valheim already use for their own
            // un-instantiated template prefabs. Jotunn exposes
            // PrefabManager.Instance.PrefabContainer for this, but its
            // getter is `internal` -- inaccessible from this assembly
            // without publicizing Jotunn.dll too, which was not done here.
            // PrefabTemplateContainer() below is our own equivalent instead,
            // confirmed by the same reasoning Jotunn's own doc comment
            // states for theirs ("Container for custom prefabs in the
            // DontDestroyOnLoad scene").
            //
            // Children created under an inactive parent inherit its
            // inactive-in-hierarchy state, so nothing else built below --
            // the body mesh, the label/icon/count renderer -- needs its own
            // SetActive(false) call of its own for THIS reason (the "count"
            // text child does call SetActive(false), but for an unrelated,
            // per-instance reason -- see DrawerRenderer's own comment on it).
            go.transform.SetParent(PrefabTemplateContainer().transform, false);

            var body = new GameObject("body");
            body.transform.SetParent(go.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = sharedMesh;

            var renderer = body.AddComponent<MeshRenderer>();

            // Build a fresh material from the donor's SHADER only, not the
            // donor's material. `new Material(donorMaterial)` was the
            // earlier approach, and it was wrong: it clones every keyword
            // the donor happened to have enabled, each tuned for that
            // donor's own geometry and art, not ours. That is what actually
            // caused "every drawer is a different, oddly-angled shape": a
            // one-line-per-drawer diagnostic (mesh id, vertex count, bounds,
            // scale) proved the geometry was byte-identical on every placed
            // drawer of every tier, so the variation had to be in the
            // shader -- specifically _VALUENOISEVERTEX_ON, inherited from
            // the wood_floor donor, which displaces vertex positions by
            // world-position noise. That is exactly "a different shape per
            // drawer, with angled sides": one static mesh, warped
            // differently at every world coordinate. `_PARALLAXMAP`,
            // likewise inherited, fakes depth from a height map read out of
            // the *donor's* texture set -- mismatched against our own
            // generated albedo/normal, and proportionally large on a 0.66m
            // box, compounding the same look. `new Material(shader)`
            // instead starts from the shader's own declared defaults, so
            // neither keyword (nor any other donor tuning) is present
            // unless this code turns it on.
            var tierMaterial = new Material(donorRenderer.sharedMaterial.shader)
            {
                name = $"rid_drawer_{tier}_mat"
            };

            // _Color is a plain multiplicative tint that Custom/Piece (like
            // most Unity surface shaders) declares in its Properties block
            // with its own default -- normally white / no tint -- so
            // `new Material(shader)` already initializes it correctly
            // without this. Set explicitly anyway, rather than trust an
            // implicit default sight unseen: cheap, self-documenting, and
            // removes any risk of this shader being the one exception that
            // defaults a tint to black.
            if (tierMaterial.HasProperty("_Color")) tierMaterial.SetColor("_Color", Color.white);

            // Rain darkening, wetness, and snow shading on Custom/Piece are
            // NOT per-material keywords at all -- confirmed by decompiling
            // EnvMan (current buildid 25253764 assembly_valheim.dll):
            // EnvMan.UpdateWetness does `Shader.SetGlobalFloat(_Wet, ...)`,
            // a GLOBAL shader uniform pushed every frame from the weather
            // system, which every object using this shader reads
            // automatically. Nothing per-material is needed to opt in, and
            // this is corroborated by the live keyword capture that
            // diagnosed the shape bug: the donor materials' own keyword
            // lists (e.g. Wood: _NORMALMAP,_PARALLAXMAP,_TRIPLANARMAP_ON,
            // _VALUENOISEVERTEX_ON) never included any rain/wetness/snow
            // keyword either, on vanilla pieces that unquestionably do
            // weather correctly in-game. So a drawer weathers like vanilla
            // furniture by virtue of using Custom/Piece at all -- nothing
            // below has to reproduce it.
            bool triplanarEnabled = tierMaterial.HasProperty("_TriplanarScale");
            bool triplanarLocalPos = false;
            if (triplanarEnabled)
            {
                tierMaterial.EnableKeyword("_TRIPLANARMAP_ON");
                // 1 world-unit repeat on a triplanar-projected texture means
                // the whole 512x512 texture covers exactly one metre of wall,
                // regardless of any one object's own size -- triplanar
                // projection is driven by world position, not local UVs. The
                // drawer body measures 0.66m (see BoxCollider below, and
                // DrawerProportions' docstring for where that figure comes
                // from), so one drawer face now shows about two thirds of a
                // full plank/blotch/vein cycle rather than a whole one; left
                // unchanged here since this is a texture-density judgement
                // call, not a geometric proportion that has to track the
                // resize the way the collider and frame dimensions do, and
                // it was not reported as visually wrong.
                tierMaterial.SetFloat("_TriplanarScale", 1f);

                // Still wanted on its own merits even though it turned out
                // not to be the shape bug: world-space triplanar projection
                // samples the albedo/normal maps by the drawer's world
                // position, so without this, two otherwise-identical
                // drawers at different coordinates would show visibly
                // different texture detail (a different crop of the same
                // maps, not a different shape). Confirmed present on this
                // shader by decompiling Player.SetupPlacementGhost, which
                // sets this same float on the placement-ghost material for
                // the identical reason -- a preview must look the same
                // regardless of where it is currently hovering.
                triplanarLocalPos = tierMaterial.HasProperty("_TriplanarLocalPos");
                if (triplanarLocalPos)
                {
                    tierMaterial.SetFloat("_TriplanarLocalPos", 1f);
                }
                else
                {
                    DrawerPlugin.Log.LogWarning(
                        $"{tier}: shader has no _TriplanarLocalPos; triplanar mapping "
                        + "will stay projected in world space, which makes each placed drawer's "
                        + "surfaces sample the normal map differently.");
                }
            }
            else
            {
                // A donor shader with no _TriplanarScale is a real anomaly
                // (every known donor ships it), not routine status -- stays
                // a warning of its own rather than folding into the
                // one-line summary below, where it could get lost.
                DrawerPlugin.Log.LogWarning(
                    $"{tier}: shader has no _TriplanarScale; leaving UV mapping in place.");
            }

            // Our own generated, seamless textures -- the only art this
            // material carries. No donor texture is ever set: there is no
            // donor material here to inherit one from.
            var (albedo, normal) = DrawerMaterialTextures.Get(tier);
            bool hasMainTex = tierMaterial.HasProperty("_MainTex");
            if (hasMainTex) tierMaterial.SetTexture("_MainTex", albedo);
            bool hasBumpMap = tierMaterial.HasProperty("_BumpMap");
            if (hasBumpMap)
            {
                tierMaterial.SetTexture("_BumpMap", normal);
                tierMaterial.EnableKeyword("_NORMALMAP");
            }

            string textureStatus = hasMainTex && hasBumpMap ? "albedo+normal"
                : hasMainTex ? "albedo only (shader has no _BumpMap)"
                : "NONE (shader has neither _MainTex nor _BumpMap)";

            // The single collapsed per-tier line this replaces five/seven
            // separate ones with: donor, shader, and texture status is
            // exactly the "is this tier healthy" summary an operator needs
            // at a glance; the per-property/keyword/mesh dumps this used to
            // print were useful while diagnosing a since-fixed shader/UV
            // issue, not routine signal. `keywords` now doubles as proof
            // the fresh-material approach worked: it should list only
            // _NORMALMAP and _TRIPLANARMAP_ON, never _VALUENOISEVERTEX_ON
            // or _PARALLAXMAP.
            DrawerPlugin.Log.LogInfo(
                $"{tier}: donor='{donorName}', shader='{donorRenderer.sharedMaterial.shader.name}', "
                + $"triplanar={(triplanarEnabled ? "on" : "off")}, "
                + $"triplanarLocalPos={(triplanarLocalPos ? "on" : "off")}, textures={textureStatus}, "
                + $"keywords=[{string.Join(",", tierMaterial.shaderKeywords)}]");

            renderer.sharedMaterial = tierMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // The label plate that marks the drawer's front -- see
            // DrawerRenderer.BuildFaceplate for why it is needed at all.
            // Built from the tier material so it carries the same grain,
            // normal map and keywords, then tinted brighter; _Color on
            // Custom/Piece is a plain multiply, so a factor above 1
            // lightens rather than clamping.
            //
            // One material per tier, created here and shared by every
            // drawer of that tier. Never per-instance: that would cost a
            // draw call per drawer and defeat the batching this mod's whole
            // design rests on.
            Material faceplateMaterial = null;
            if (hasMainTex)
            {
                faceplateMaterial = new Material(tierMaterial) { name = tierMaterial.name + "_faceplate" };
                faceplateMaterial.SetTexture("_MainTex",
                    DrawerMaterialTextures.GetAtLightness(tier, FaceplateLightness));
            }

            // 0.66m, matching DrawerProportions' measured cube size -- an
            // unscaled 1m collider here would give every drawer an invisible
            // hitbox roughly 1.5x too large in every dimension.
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.66f, 0.66f, 0.66f);

            AddSnapPoints(go, proportions);

            // ZNetView's own fields (m_persistent, m_type, m_distant,
            // m_syncInitialScale) are NOT set from a serialized prefab
            // asset here the way a vanilla piece_* prefab authored in the
            // Unity Editor would have them -- this GameObject is built
            // entirely in code, so AddComponent<ZNetView>() leaves every
            // one of them at its bare C# field default. Confirmed by
            // decompiling ZNetView: `public bool m_persistent;` with no
            // initializer, same for m_distant and m_syncInitialScale (all
            // default false), and `public ZDO.ObjectType m_type;` (default
            // ObjectType.Default, value 0 -- see ZDO.ObjectType's enum
            // ordering: Default, Prioritized, Solid, Terrain).
            //
            // m_persistent=false is the one that actually destroys drawers
            // on reload, and this was the exact bug: ZNetView.Awake sets
            // `m_zdo.Persistent = m_persistent` when it creates a fresh ZDO,
            // and ZDOMan gates whether a ZDO's containing zone chunk is ever
            // marked dirty (and therefore ever written to the zone file at
            // all) on `zdo.Persistent` -- confirmed in ZDOMan.AddToSector/
            // RemoveFromSector/SetDirtySector, all of which no-op when
            // Persistent is false. A non-persistent ZDO is also actively
            // cleaned up mid-session once its owning client disconnects
            // (ZDOMan.RemoveOrphanNonPersistentZDOS), which is a worse fate
            // than merely "not saved". This is the same family of bug as
            // Piece.m_enabled/m_usage/m_icon and the [SerializeField]
            // problem in DrawerRenderer -- editor-authored state that code
            // constructing a component at runtime must set explicitly, or
            // it silently keeps the type's bare default instead of whatever
            // a normal placed piece needs.
            //
            // m_type = Default and m_distant/m_syncInitialScale = false are
            // each individually correct for an ordinary static furniture
            // piece (Default is the common case for most placed pieces;
            // Prioritized is for latency-sensitive gameplay objects, Solid/
            // Terrain for terrain-interacting ones, none of which describe
            // a drawer; m_distant would only matter for something that must
            // render/exist far outside loaded zones, e.g. a portal; a
            // drawer's scale never changes after placement, so
            // m_syncInitialScale has nothing to do) -- so only m_persistent
            // actually needed to change from its default.
            var nview = go.AddComponent<ZNetView>();
            nview.m_persistent = true;
            var drawer = go.AddComponent<DrawerComponent>();

            // PrivacySetting.Private is 0, so a Container built in code and
            // never told otherwise is PRIVATE -- the fifth editor-set field
            // in this prefab to default wrong (see m_usage, m_icon,
            // m_enabled, and the Piece component itself). Vanilla's chest
            // prefabs set this in the Unity editor; nothing sets it here.
            //
            // The symptom was not "drawers are locked", which would have
            // been obvious. Container.CheckAccess passes when the ZDO
            // records no creator, so every drawer placed before
            // DrawerComponent.EnsureCreator existed kept working, and only
            // newly placed ones failed -- and they failed silently, for
            // everyone except the player who placed them. OttoFuel calls
            // CheckAccess before reading a container and simply skipped
            // them; NoVikingLeftBehind could not craft from a drawer another
            // player had placed.
            //
            // Public is the honest setting for this piece. A drawer wears
            // its contents on its face and is built to be shared -- there is
            // nothing private about it, and Private was never a decision,
            // just a default nobody set.
            drawer.m_privacy = Container.PrivacySetting.Public;

            // Also explicitly off rather than left at its default: a drawer
            // inside someone's ward should be no more restricted than the
            // wall it is part of, and leaving this to another unexamined
            // default is what this whole comment is about.
            drawer.m_checkGuardStone = false;
            // The handle, as its own child with its own material. Built
            // from the tier material so it keeps the shader, normal map and
            // keywords that make it light like everything else, then given
            // a brightened albedo and a cool cast so it reads as metal.
            if (_sharedHandleMesh != null && hasMainTex)
            {
                var handleMaterial = new Material(tierMaterial) { name = tierMaterial.name + "_handle" };
                handleMaterial.SetTexture("_MainTex", DrawerMaterialTextures.Metal());

                // Drop the body's normal map too. It is the drawer's wood
                // grain or stone pitting, and leaving it on a smooth metal
                // bar keeps exactly the surface detail the flat albedo was
                // meant to remove -- the lighting would still carve grain
                // into it even with the colour gone. A metal handle shades
                // from its own chamfered silhouette, nothing else.
                if (handleMaterial.HasProperty("_BumpMap"))
                {
                    handleMaterial.SetTexture("_BumpMap", null);
                    handleMaterial.DisableKeyword("_NORMALMAP");
                }

                // Smoothness if the shader offers it: a metal handle that
                // catches light differently from the body around it is a
                // far stronger cue than colour alone, especially on Black
                // Marble where there is very little colour range to work
                // with. Guarded per property because Custom/Piece's exact
                // surface parameters are not something this mod controls or
                // can assume across game updates.
                foreach (var prop in new[] { "_Glossiness", "_Smoothness" })
                {
                    if (handleMaterial.HasProperty(prop)) handleMaterial.SetFloat(prop, 0.65f);
                }
                if (handleMaterial.HasProperty("_Metallic")) handleMaterial.SetFloat("_Metallic", 0.8f);

                var handleGo = new GameObject("handle");
                handleGo.transform.SetParent(go.transform, false);
                handleGo.AddComponent<MeshFilter>().sharedMesh = _sharedHandleMesh;
                var handleRenderer = handleGo.AddComponent<MeshRenderer>();
                handleRenderer.sharedMaterial = handleMaterial;
                handleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            DrawerRenderer.Attach(go, proportions, faceplateMaterial);

            // Piece and WearNTear are added right here (not deferred) because
            // Jotunn's CustomPiece.IsValid() requires Piece on the root, and
            // this is the earliest point the prefab's own mesh/material exist
            // for RenderManager to photograph.

            var piece = go.AddComponent<Piece>();
            piece.m_name = DrawerTiers.DisplayName(tier);
            piece.m_description = "Holds a great many of a single item.";
            // Not RenderIcon(go, tier): RenderManager.Render's own gate
            // (Jotunn's Render(RenderRequest)) checks
            // target.GetComponentsInChildren<Component>(includeInactive:
            // false).Any(IsVisualComponent) BEFORE it ever clones or
            // activates anything -- against go itself, which must stay
            // inactive (see the SetActive(false) comment above) to keep
            // ZNetView alive. Against an inactive go that check always finds
            // nothing and Render returns null unconditionally. Briefly
            // reactivating go to work around that would re-trigger Awake on
            // every component for the first time, including ZNetView's
            // self-destruct -- the exact bug this method exists to avoid.
            // Rendered instead from a disposable, component-minimal stand-in
            // with just the mesh and material: an ordinary GameObject with
            // no ZNetView, Piece, WearNTear, or DrawerComponent on it at
            // all, so nothing about it can be damaged by being active.
            piece.m_icon = RenderIconFromMesh(sharedMesh, tierMaterial, tier) ?? donorPiece.m_icon;

            // Piece.m_enabled defaults to false on a Piece created at
            // runtime. false gates two separate paths: PieceTable never
            // adds it to m_availablePieces (no tab shows it) and
            // Player.UpdateKnownRecipesList never calls AddKnownPiece (it
            // never becomes a known recipe either). Vanilla piece prefabs
            // have this ticked in the editor; ours must set it explicitly.
            piece.m_enabled = true;

            piece.m_category = Piece.PieceCategory.Furniture;

            // m_category does NOT drive the 1.0 build menu -- Valheim 1.0
            // rebuilt it around ByUsagePieceList, which filters every tab
            // by m_usage.HasFlag(tag). A Piece created in code defaults to
            // m_usage = 0, which matches no tag and is reachable only under
            // "show all".
            //
            // Set explicitly, never copied from the donor. The donor supplies
            // a MATERIAL, and its own tags describe what the donor is, not
            // what a drawer is: blackmarble_1x1 is a wall block tagged
            // Architecture, so copying put the black marble drawer under
            // Architecture instead of with the other storage. A drawer is
            // storage furniture whatever it is faced with.
            piece.m_usage = Piece.UsageTagFlags.Furniture | Piece.UsageTagFlags.Storage;
            piece.m_comfort = 0;

            var wear = go.AddComponent<WearNTear>();
            wear.m_health = tier == DrawerTier.Wood ? 200f : 400f;
            wear.m_noSupportWear = false;
            wear.m_noRoofWear = tier != DrawerTier.Wood;
            wear.m_destroyedEffect = donor.GetComponent<WearNTear>()?.m_destroyedEffect;
            wear.m_hitEffect = donor.GetComponent<WearNTear>()?.m_hitEffect;

            return go;
        }

        /// <summary>
        /// Renders an icon from a disposable, purely-visual stand-in (a
        /// plain active GameObject with just a MeshFilter/MeshRenderer for
        /// the given mesh and material) rather than the real drawer prefab,
        /// which must stay inactive throughout construction -- see the
        /// SetActive(false) comment in BuildPrefab for why activating it
        /// even briefly would destroy its ZNetView. The stand-in carries no
        /// gameplay components at all, so there is nothing on it for that
        /// problem to reach.
        /// </summary>
        /// <summary>
        /// Adds Valheim's own snap points -- the six face centres and eight
        /// corners of the drawer's bounding cube -- as tagged child
        /// GameObjects of the prefab root. Without these, a player cannot
        /// click a drawer into alignment with another drawer or with a wall
        /// or floor, which matters precisely because the normal use of this
        /// mod is a wall of a hundred of them.
        ///
        /// Mechanism confirmed by decompiling Piece.GetSnapPoints(List
        /// &lt;Transform&gt;) in assembly_valheim.dll: it walks
        /// `this.transform`'s DIRECT children and keeps every one whose
        /// GameObject.CompareTag("snappoint") is true -- the tag string is
        /// exactly "snappoint" (lowercase, one word; verified from the
        /// decompiled literal, not assumed), and the check is a plain tag
        /// comparison with no active-state guard of any kind, so this
        /// mod's inactive-template-during-registration pattern (see the
        /// SetActive(false)/PrefabTemplateContainer comment above) cannot
        /// interfere with it -- a GameObject's tag is data on the
        /// GameObject itself, not something Unity's inactive-hierarchy
        /// Awake-suppression touches, and it is copied onto every
        /// Object.Instantiate clone exactly like every other component this
        /// method attaches to the same, already-inactive `go` (the mesh,
        /// the collider, ZNetView, Piece itself). "snappoint" is a tag
        /// Valheim's own project already defines (every vanilla building
        /// piece uses it), so setting it here at runtime does not require
        /// this mod to register a new tag of its own.
        ///
        /// Positions are local offsets from `go`'s own transform, not from
        /// any child mesh transform: `body` (the mesh's GameObject) is
        /// parented onto `go` via `SetParent(go.transform, false)` starting
        /// from a freshly-created, untouched identity transform, so it sits
        /// at local (0,0,0) relative to `go` -- the same origin
        /// `Piece.GetSnapPoints` reads these children's positions relative
        /// to. Confirmed, not assumed, since an offset piece origin would
        /// silently misalign every snap point by exactly that offset.
        ///
        /// Offsets are derived from DrawerProportions (half of Width/
        /// Height/Depth), never a hardcoded 0.33 -- KG's original
        /// kg_itemdrawers bundle measures out to exactly half of the 0.66m
        /// cube on each axis for all thirteen points (confirmed via the
        /// same UnityPy extraction that measured the drawer's overall
        /// size), which is exactly what half-extents against the current
        /// DrawerProportions already produce, and keeps producing if the
        /// drawer is ever resized again -- unlike the label z-clearance
        /// constant an earlier round found had silently drifted out of
        /// proportion after a resize by staying a flat, unscaled number.
        /// </summary>
        private static void AddSnapPoints(GameObject go, DrawerProportions p)
        {
            float hx = p.Width / 2f, hy = p.Height / 2f, hz = p.Depth / 2f;

            // Six face centres.
            var faces = new[]
            {
                new Vector3(0f, hy, 0f), new Vector3(0f, -hy, 0f),
                new Vector3(hx, 0f, 0f), new Vector3(-hx, 0f, 0f),
                new Vector3(0f, 0f, hz), new Vector3(0f, 0f, -hz),
            };

            // Eight corners.
            var corners = new List<Vector3>(8);
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sy in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
                corners.Add(new Vector3(sx * hx, sy * hy, sz * hz));

            int index = 1;
            foreach (var offset in faces)
            {
                var snap = new GameObject($"snappoint_{index++}") { tag = "snappoint" };
                snap.transform.SetParent(go.transform, false);
                snap.transform.localPosition = offset;
            }
            foreach (var offset in corners)
            {
                var snap = new GameObject($"snappoint_{index++}") { tag = "snappoint" };
                snap.transform.SetParent(go.transform, false);
                snap.transform.localPosition = offset;
            }
        }

        private static Sprite RenderIconFromMesh(Mesh mesh, Material material, DrawerTier tier)
        {
            var source = new GameObject("rid_icon_source");
            try
            {
                var body = new GameObject("body");
                body.transform.SetParent(source.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = mesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = material;

                return RenderIcon(source, tier);
            }
            finally
            {
                Object.Destroy(source);
            }
        }

        /// <summary>
        /// Renders a real icon of the given active GameObject via Jotunn's
        /// RenderManager, which is a synchronous call (it spawns a stripped
        /// clone, snaps a camera render, reads it back, and returns
        /// immediately -- no coroutine required). Returns null, rather than
        /// throwing, if rendering is unavailable or fails so callers can
        /// fall back to a borrowed icon.
        ///
        /// The target must be active: RenderManager.Render's own gate
        /// (target.GetComponentsInChildren&lt;Component&gt;(includeInactive:
        /// false).Any(IsVisualComponent)) always finds nothing, and returns
        /// null unconditionally, against an inactive target.
        /// </summary>
        private static Sprite RenderIcon(GameObject prefab, DrawerTier tier)
        {
            try
            {
                var request = new RenderManager.RenderRequest(prefab)
                {
                    Rotation = RenderManager.IsometricRotation,
                    Width = 128,
                    Height = 128
                };
                var sprite = RenderManager.Instance.Render(request);
                if (sprite == null)
                {
                    DrawerPlugin.Log.LogWarning(
                        $"RenderManager returned no sprite for {tier}; falling back to donor icon.");
                }
                return sprite;
            }
            catch (System.Exception ex)
            {
                DrawerPlugin.Log.LogWarning(
                    $"RenderManager threw rendering {tier}'s icon ({ex.Message}); falling back to donor icon.");
                return null;
            }
        }
    }
}
