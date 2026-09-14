using System;
using System.Collections.Generic;

namespace RossQoL.Core.Startup
{
    /// <summary>
    /// Whether the Continue button is shown, and what it says. The Game layer
    /// answers the questions about files on disk; this decides.
    /// </summary>
    public static class ContinuePolicy
    {
        public const int MaxLabelLength = 40;

        /// <summary>
        /// Vanilla's own launch-time joins. When present, the game is already
        /// joining something and Continue must not race it.
        /// </summary>
        private static readonly string[] JoinArguments =
            { "+connect", "+connect_lobby", "-joincode", "-joinserverwithcharacter" };

        public static bool HasJoinArguments(IEnumerable<string> args)
        {
            if (args == null) return false;

            foreach (var arg in args)
                foreach (var join in JoinArguments)
                    if (string.Equals(arg, join, StringComparison.Ordinal)) return true;

            return false;
        }

        public static bool ShouldShow(
            LastSession session, bool characterExists, bool worldLoadable, bool launchedWithJoinArguments)
        {
            if (session == null || !session.IsComplete) return false;
            if (launchedWithJoinArguments) return false;
            if (!characterExists) return false;
            if (session.Kind == SessionKind.LocalWorld && !worldLoadable) return false;
            return true;
        }

        public static string Label(string characterName, LastSession session)
        {
            string label = $"Continue: {characterName} on {session.DisplayName}";
            return label.Length <= MaxLabelLength
                ? label
                : label.Substring(0, MaxLabelLength - 1) + "…";
        }
    }
}
