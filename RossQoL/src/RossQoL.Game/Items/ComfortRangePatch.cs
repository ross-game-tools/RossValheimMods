using System;
using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// SE_Rested.GetNearbyComfortPieces is the one place comfort furniture is
    /// gathered, and it asks for a hardcoded 10 metres. The prefix gathers the
    /// same pieces at the configured reach and hands them back, so everything
    /// after it -- the sort, the collapsing of duplicates and same-group
    /// pieces, the shelter requirement -- is vanilla's, working on a longer
    /// list.
    /// </summary>
    [HarmonyPatch(typeof(SE_Rested), "GetNearbyComfortPieces")]
    internal static class ComfortRangePatch
    {
        // Ours rather than vanilla's s_tempPieces: the caller only reads the
        // list it is given, and sharing vanilla's would mean two owners of one
        // buffer for no gain.
        private static readonly List<Piece> Pieces = new List<Piece>();

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(SE_Rested), "GetNearbyComfortPieces", ComfortRangeFeature.FeatureName);

        private static bool Prefix(Vector3 point, ref List<Piece> __result)
        {
            if (ComfortRangeFeature.Instance?.IsActive != true) return true;

            try
            {
                float radius = WorldConfig.ComfortRadius?.Value ?? 20f;

                Pieces.Clear();
                Piece.GetAllComfortPiecesInRadius(point, radius, Pieces);
                __result = Pieces;
                return false;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"ComfortRange: leaving comfort to vanilla: {ex}");
                return true;
            }
        }
    }
}
