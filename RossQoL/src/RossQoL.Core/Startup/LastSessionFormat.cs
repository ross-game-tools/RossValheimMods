using System;
using System.Collections.Generic;
using System.Text;

namespace RossQoL.Core.Startup
{
    /// <summary>
    /// One-line text form of <see cref="LastSession"/>, stored in PlatformPrefs.
    /// Version-prefixed so the layout can change later; anything unreadable
    /// reads as "no record" rather than throwing, so a corrupt value only
    /// hides the button until the next session overwrites it.
    /// </summary>
    public static class LastSessionFormat
    {
        private const string Version = "1";
        private const char Separator = '|';
        private const char Escape = '\\';
        private const int FieldCount = 10;

        public static string Write(LastSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var fields = new[]
            {
                Version, session.Kind.ToString(), session.CharacterFile, session.CharacterSource,
                session.WorldName, session.WorldSource, session.ServerKind.ToString(),
                session.ServerAddress, session.JoinCode, session.DisplayName,
            };

            var text = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) text.Append(Separator);
                foreach (char c in fields[i])
                {
                    if (c == Separator || c == Escape) text.Append(Escape);
                    text.Append(c);
                }
            }

            return text.ToString();
        }

        public static LastSession Read(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var f = Split(text);
            if (f == null || f.Count != FieldCount || f[0] != Version) return null;

            if (!TryParseEnum(f[1], out SessionKind kind)) return null;
            if (!TryParseEnum(f[6], out ServerKind serverKind)) return null;

            var session = kind == SessionKind.LocalWorld
                ? LastSession.LocalWorld(f[2], f[3], f[4], f[5])
                : LastSession.Server(f[2], f[3], serverKind, f[7], f[8], f[9]);

            return session.IsComplete ? session : null;
        }

        /// <summary>Names only: Enum.TryParse also accepts numbers such as "7" or "+1", which are not values we wrote.</summary>
        private static bool TryParseEnum<T>(string text, out T value) where T : struct
        {
            value = default;
            if (text.Length == 0) return false;
            if (System.Array.IndexOf(Enum.GetNames(typeof(T)), text) < 0) return false;
            return Enum.TryParse(text, ignoreCase: false, out value);
        }

        private static List<string> Split(string text)
        {
            var fields = new List<string>();
            var current = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == Escape)
                {
                    if (i + 1 >= text.Length) return null;
                    current.Append(text[++i]);
                }
                else if (c == Separator)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());
            return fields;
        }
    }
}
