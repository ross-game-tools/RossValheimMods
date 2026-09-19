using System.Collections.Generic;

namespace RossQoL.Core.Death
{
    /// <summary>
    /// Every remembered grave, newest first, as one string.
    ///
    /// The whole character file is rewritten on every save and a cloud profile
    /// past a megabyte is silently demoted to local-only, so the store is
    /// capped rather than left to grow: a few graves per world, a hard total
    /// across all of them. The caps are constants, not settings -- nobody
    /// wants to tune them, and getting them wrong bloats a save file.
    /// </summary>
    public static class GraveStore
    {
        public const int DefaultPerWorld = 5;
        public const int DefaultTotal = 20;

        private const char LineSeparator = '\n';

        /// <summary>Extracts all graves from the store string, skipping malformed lines.</summary>
        public static IReadOnlyList<GraveRecord> Parse(string stored)
        {
            var records = new List<GraveRecord>();
            if (string.IsNullOrEmpty(stored)) return records;

            foreach (string line in stored.Split(LineSeparator))
                if (GraveRecord.TryParse(line, out var record))
                    records.Add(record);

            return records;
        }

        /// <summary>Graves for a single world, in newest-first order.</summary>
        public static IReadOnlyList<GraveRecord> ForWorld(string stored, long worldId)
        {
            var records = new List<GraveRecord>();
            foreach (var record in Parse(stored))
                if (record.WorldId == worldId)
                    records.Add(record);

            return records;
        }

        /// <summary>
        /// Inserts a grave at the front, then enforces the per-world and total caps.
        /// Takes the caps as parameters rather than reading constants, so tests can drive them.
        /// </summary>
        public static string Add(string stored, GraveRecord record, int perWorld, int total)
        {
            var records = new List<GraveRecord>(Parse(stored));
            records.Insert(0, record);

            // Newest first, so keeping the first N of a world keeps the newest.
            int kept = 0;
            for (int i = 0; i < records.Count;)
            {
                bool sameWorld = records[i].WorldId == record.WorldId;
                if (sameWorld && ++kept > perWorld) records.RemoveAt(i);
                else i++;
            }

            while (records.Count > total) records.RemoveAt(records.Count - 1);

            return Format(records);
        }

        /// <summary>
        /// Removes the grave by its tombstone ZDOID. Returns the store unchanged if no grave matches.
        /// </summary>
        public static string Remove(string stored, long worldId, uint zdoId)
        {
            var kept = new List<GraveRecord>();
            foreach (var record in Parse(stored))
                if (record.WorldId != worldId || record.ZdoId != zdoId)
                    kept.Add(record);

            return Format(kept);
        }

        private static string Format(IReadOnlyList<GraveRecord> records)
        {
            var lines = new List<string>(records.Count);
            foreach (var record in records) lines.Add(record.Format());

            return string.Join(LineSeparator.ToString(), lines);
        }
    }
}
