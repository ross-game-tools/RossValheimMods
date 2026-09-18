using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using Jotunn.Utils;

namespace ItemDrawers.Game
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Drawer state, the RPC protocol and the container view's ZDO format
    // only work when every peer runs the same rules; a 0.9.x peer would
    // ignore view changes and a 1.0 peer reconciling against it loses them.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class DrawerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.rossdwest.itemdrawers";
        public const string PluginName = "ItemDrawers";
        public const string PluginVersion = "1.0.11";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);

            // Before any patching, so that if a Valheim update has moved
            // something out from under us the log leads with WHICH member,
            // rather than with whatever secondary symptom surfaces first.
            ValheimCompat.Verify();

            // A patch failing to apply (e.g. a future Valheim update renames
            // or removes a method a patch targets) must degrade only the
            // feature that patch serves -- never take registration down with
            // it, and never take any OTHER patch down with it either.
            // Harmony's own PatchAll() iterates every [HarmonyPatch] class in
            // one loop with no per-class try/catch of its own: one class
            // throwing during patching can abort that loop before later
            // classes are ever reached, silently leaving them unpatched --
            // which is exactly how a previous unguarded patch could have
            // stripped every drawer from a player's save on a future Valheim
            // update (CanBeRemoved was the only patch then; today Save/Load
            // are also on that list, and losing either of those the same way
            // reopens the very save corruption they exist to prevent). So
            // each [HarmonyPatch] class in this assembly is patched
            // individually via CreateClassProcessor, each in its own
            // try/catch: one class's failure logs and is skipped, every
            // other class still gets a chance. If PatchAll (or this loop)
            // threw uncaught here, Awake would abort before DrawerConfig.Bind
            // and the OnVanillaPrefabsAvailable subscription below ever ran,
            // and every drawer already built in a save would then be an
            // unknown prefab and get stripped from the world on load -- the
            // outer try/catch is what stops that from ever being fatal, even
            // if some future Harmony version's PatchAll-equivalent behaves
            // differently than assumed here.
            // One permanent summary line naming exactly what got patched.
            // Its absence cost a full round: with no patch-confirmation
            // logging, "OttoFuel/NVLB don't see drawers" and "our
            // GetInventory/Save/Load patches silently didn't apply" were
            // indistinguishable from the log alone. This is worth carrying
            // forever, not just for this investigation.
            var patchSummary = new List<string>();

            try
            {
                foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
                {
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0) continue;

                    try
                    {
                        // CreateClassProcessor(type).Patch() returns the
                        // original (target) MethodBases it actually patched.
                        // Empty/null means Prepare() returned false (or the
                        // attribute resolved to no method) -- Harmony
                        // swallows that as "nothing to do" rather than an
                        // exception, so it has to be checked explicitly to
                        // be reported at all.
                        var patched = _harmony.CreateClassProcessor(type).Patch();
                        if (patched == null || patched.Count == 0)
                        {
                            patchSummary.Add($"{type.Name}=SKIPPED(Prepare() false or no target)");
                        }
                        else
                        {
                            foreach (var method in patched)
                                patchSummary.Add($"{method.DeclaringType?.Name}.{method.Name}=OK(via {type.Name})");
                        }
                    }
                    catch (Exception ex)
                    {
                        patchSummary.Add($"{type.Name}=FAILED({ex.GetType().Name})");
                        Log.LogError($"Harmony patch '{type.FullName}' failed to apply; continuing without it: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                patchSummary.Add($"enumeration=FAILED({ex.GetType().Name})");
                Log.LogError($"Harmony patching failed unexpectedly; continuing without the affected patch(es): {ex}");
            }

            Log.LogInfo($"ItemDrawers Harmony patches: {string.Join("; ", patchSummary)}");

            DrawerManager.Create();

            DrawerConfig.Bind(Config);

            PrefabManager.OnVanillaPrefabsAvailable += OnPrefabsReady;
            ItemManager.OnItemsRegistered += OnItemsRegistered;
            RegisterDiagCommand();
            DebugCommands.Register();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        private void OnPrefabsReady()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= OnPrefabsReady;
            DrawerPieces.RegisterAll();
        }

        /// <summary>
        /// Fires as a postfix on ObjectDB.Awake, deliberately at
        /// HarmonyPriority(0) (the lowest, so it runs LAST among any
        /// postfixes on that method) -- confirmed by decompiling
        /// Jotunn.Managers.PrefabManager+Patches.InvokeOnItemsRegistered.
        /// That is what makes this the correct hook for the icon atlas:
        /// every other mod's own ObjectDB.Awake postfix (custom item
        /// registration, NoVikingLeftBehind's status effects, Jotunn's own
        /// "Adding N custom items" pass) has already run by the time this
        /// fires, so ObjectDB.instance.m_items is actually populated here.
        /// PrefabManager.OnVanillaPrefabsAvailable (used for piece
        /// registration above) fires earlier and is NOT safe for this --
        /// that is exactly what produced a "0 icons in 1x1" atlas before.
        /// Jotunn also gates this event to the "main" scene, so it never
        /// fires in the main menu, only once a world is actually loading --
        /// well before a player can place anything.
        /// </summary>
        private void OnItemsRegistered() => DrawerIconAtlas.Build();

        /// <summary>
        /// Reports the state that actually decides whether a piece reaches a
        /// build-menu tab. Needs a live player, so it is a console command
        /// rather than startup logging -- two hypotheses died for want of
        /// exactly these numbers.
        /// </summary>
        private static void RegisterDiagCommand()
        {
            new Terminal.ConsoleCommand("rid_diag", "report ItemDrawers drawer piece state", args =>
            {
                void Say(string s) { args.Context.AddString(s); Log.LogInfo("rid_diag: " + s); }

                foreach (var tier in DrawerTiers.All)
                {
                    var name = DrawerTiers.PrefabName(tier);
                    Say($"--- {name} ---");

                    var prefab = PrefabManager.Instance.GetPrefab(name);
                    var piece = prefab == null ? null : prefab.GetComponent<Piece>();
                    if (piece == null) { Say("prefab or Piece MISSING"); continue; }

                    Say($"m_enabled={piece.m_enabled}  m_category={piece.m_category}  m_name='{piece.m_name}'  m_icon={(piece.m_icon == null ? "NULL" : piece.m_icon.name)}");
                    Say($"m_usage={piece.m_usage}  (this is what 1.0's build-menu tabs filter on)");
                    Say($"m_craftingStation={(piece.m_craftingStation == null ? "none" : piece.m_craftingStation.m_name)}");

                    var reqs = piece.m_resources;
                    Say($"requirements={(reqs == null ? 0 : reqs.Length)}");
                    if (reqs != null)
                        foreach (var r in reqs)
                            Say($"   res={(r.m_resItem == null ? "UNRESOLVED" : r.m_resItem.m_itemData.m_shared.m_name)} x{r.m_amount}");

                    var player = Player.m_localPlayer;
                    if (player == null) { Say("no local player"); continue; }

                    Say($"IsRecipeKnown('{piece.m_name}')={player.IsRecipeKnown(piece.m_name)}");
                    Say($"HaveRequirements(IsKnown)={player.HaveRequirements(piece, Player.RequirementMode.IsKnown)}");

                    // HaveRequirements(IsKnown) returns false on an unknown
                    // crafting station BEFORE it looks at materials, so a
                    // false here reads as "missing materials" when the real
                    // cause is that the player has never stood near a
                    // workbench. Stations become known via AddKnownStation,
                    // which fires on proximity to a built one -- knowing the
                    // recipe is not enough. Report it rather than deduce it.
                    if (piece.m_craftingStation != null)
                    {
                        bool stationKnown = player.m_knownStations.ContainsKey(piece.m_craftingStation.m_name);
                        bool stationInRange = CraftingStation.HaveBuildStationInRange(
                            piece.m_craftingStation.m_name, player.transform.position);
                        Say($"station '{piece.m_craftingStation.m_name}': known={stationKnown} inRange={stationInRange}"
                            + (stationKnown ? "" : "  <-- BLOCKS IsKnown; build a workbench and stand near it"));
                    }
                    Say($"HaveRequirements(CanBuild)={player.HaveRequirements(piece, Player.RequirementMode.CanBuild)}");

                    // The table the PLAYER builds from is the one that matters.
                    // Everything measured so far came from PieceManager's table,
                    // which may be a different instance entirely.
                    var jotunnTable = PieceManager.Instance.GetPieceTable(PieceTables.Hammer);
                    var playerTable = player.m_buildPieces;

                    Say($"jotunnTable={(jotunnTable == null ? "null" : jotunnTable.name)}  "
                        + $"playerTable={(playerTable == null ? "null" : playerTable.name)}  "
                        + $"SAME INSTANCE={ReferenceEquals(jotunnTable, playerTable)}");

                    if (playerTable == null) { Say("player has no build table equipped"); continue; }

                    Say($"[player table] in m_pieces={playerTable.m_pieces.Contains(prefab)}  "
                        + $"in m_enabledPieces={playerTable.m_enabledPieces.Contains(piece)}  "
                        + $"in m_availablePieces={playerTable.m_availablePieces.Contains(piece)}");
                }

                var localPlayer = Player.m_localPlayer;
                var buildTable = localPlayer == null ? null : localPlayer.m_buildPieces;
                if (buildTable == null) { Say("no player / no build table for category dump"); }
                else
                {
                    Say($"[player table] selectedCategory={buildTable.GetSelectedCategory()}  "
                        + $"categories=[{string.Join(", ", buildTable.m_categories)}]");

                    for (int i = 0; i < buildTable.m_availablePiecesByCategory.Count; i++)
                    {
                        var bucket = buildTable.m_availablePiecesByCategory[i];
                        if (bucket.Count == 0) continue;
                        var names = bucket.ConvertAll(p => p == null ? "<null>" : p.m_name);
                        Say($"[player table] bucket {i} ({(Piece.PieceCategory)i}) x{bucket.Count}: {string.Join(" | ", names)}");
                    }
                }

                // ---------- Container bridge diagnostics ----------
                // Answers "does GetInventory() actually work on a live
                // drawer" directly, rather than inferring it from whether
                // OttoFuel/NVLB happen to react. Deliberately goes through
                // the Container-typed reference (`asContainer.GetInventory()`),
                // never DrawerComponent.View directly -- the
                // whole point is to exercise ContainerBridge's Harmony
                // patch on Container.GetInventory the same way any other
                // mod's call would, so a patch that failed to apply shows
                // up here as GetInventory() returning null, not as this
                // command silently reading the view around the patch.
                Say("--- Container bridge (nearby drawers) ---");
                if (localPlayer == null || DrawerManager.Instance == null)
                {
                    Say("no local player or DrawerManager -- cannot enumerate nearby drawers");
                }
                else
                {
                    var nearby = new System.Collections.Generic.List<DrawerComponent>();
                    DrawerManager.Instance.QueryNear(localPlayer.transform.position, 20f, nearby);
                    Say($"drawers within 20m of player: {nearby.Count}  (DrawerComponent.All total: {DrawerComponent.All.Count})");

                    foreach (var d in nearby)
                    {
                        bool inAll = DrawerComponent.All.Contains(d);

                        Container asContainer = d;
                        Inventory inv = asContainer.GetInventory();

                        string invDesc;
                        if (inv == null)
                        {
                            // A null result is only the patch's fault when
                            // the drawer actually has a view to return.
                            invDesc = !InventoryAccess.Available
                                ? "GetInventory()=NULL (InventoryAccess unavailable: no container view on this game version)"
                                : d.View == null
                                    ? "GetInventory()=NULL (this drawer has no view: Awake did not finish)"
                                    : "GetInventory()=NULL <-- ContainerBridge's GetInventory patch is not applying";
                        }
                        else
                        {
                            int viewTotal = 0;
                            foreach (var item in inv.GetAllItems()) viewTotal += item.m_stack;
                            invDesc = $"GetInventory()=non-null, {inv.GetWidth() * inv.GetHeight()} slot(s), "
                                      + $"{inv.NrOfItems()} stack(s), total={viewTotal}";
                        }

                        var zdo = d.Snapshot;

                        // m_persistent and the ZDO's creator both matter for
                        // a drawer to survive a reload and be found by a
                        // container-aware mod -- worth surfacing on demand
                        // here rather than only in the source comments.
                        string persistentDesc = "unknown", creatorDesc = "unknown";
                        try
                        {
                            var nview = AccessTools.FieldRefAccess<Container, ZNetView>(d, "m_nview");
                            if (nview != null)
                            {
                                persistentDesc = nview.m_persistent.ToString();
                                var nviewZdo = nview.GetZDO();
                                creatorDesc = nviewZdo != null
                                    ? nviewZdo.GetLong("creator".GetStableHashCode(), 0L).ToString()
                                    : "no ZDO";
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.LogWarning($"rid_diag: could not read m_nview/creator for '{d.DiagId}': {ex}");
                        }

                        Say($"{d.DiagId}: {invDesc}; "
                            + $"view=({d.ViewDiag}); "
                            + $"ZDO=('{zdo.ItemName}',{zdo.Amount}); "
                            + $"m_persistent={persistentDesc}; creator={creatorDesc}; "
                            + $"inAll={inAll}");
                    }
                }
            });
        }
    }
}
