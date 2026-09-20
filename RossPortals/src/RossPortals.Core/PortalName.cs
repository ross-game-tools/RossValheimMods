using System;
using System.Collections.Generic;

namespace RossPortals.Core
{
    /// <summary>
    /// Turns a portal tag into the path segments that drive grouping. The
    /// whole grouping feature rides on one convention: a separator in the name
    /// (default '/') means "folder". "Mines/Copper/North" is the portal
    /// "North" inside folder "Mines" inside folder "Copper"... no — inside
    /// "Mines" then "Copper": segments are read left to right, the last is the
    /// portal, everything before it is the folder path.
    ///
    /// This lives in Core, not the panel, because it is pure string logic with
    /// fiddly edges (blank segments, leading/trailing/doubled separators) that
    /// are far easier to pin down with unit tests than to eyeball in-game.
    /// </summary>
    public static class PortalName
    {
        public const char DefaultSeparator = '/';

        /// <summary>
        /// Split a name into non-empty, trimmed segments. Blank pieces are
        /// dropped, so "Mines/", "/Mines" and "Mines//Copper" all behave
        /// sensibly rather than producing empty folders. An empty or
        /// whitespace-only name yields no segments (an untagged portal).
        /// </summary>
        public static IReadOnlyList<string> Segments(string name, char separator)
        {
            if (string.IsNullOrWhiteSpace(name)) return Array.Empty<string>();

            var parts = name.Split(separator);
            var result = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
            return result;
        }

        /// <summary>
        /// The portal's own label: the last segment, or empty for an untagged
        /// portal (the Game layer shows a "(no name)" placeholder for that).
        /// </summary>
        public static string Leaf(string name, char separator)
        {
            var segments = Segments(name, separator);
            return segments.Count == 0 ? string.Empty : segments[segments.Count - 1];
        }

        /// <summary>
        /// The folder path above the portal: every segment except the last.
        /// Empty when the portal is ungrouped (no separator, or untagged).
        /// </summary>
        public static IReadOnlyList<string> GroupPath(string name, char separator)
        {
            var segments = Segments(name, separator);
            if (segments.Count <= 1) return Array.Empty<string>();

            var path = new List<string>(segments.Count - 1);
            for (int i = 0; i < segments.Count - 1; i++) path.Add(segments[i]);
            return path;
        }
    }
}
