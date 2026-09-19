using System;
using System.Globalization;

namespace RossQoL.Core.Death
{
    /// <summary>
    /// One remembered grave. Stored as a single line in the player's custom
    /// data, so every field is formatted invariantly -- a decimal comma from
    /// a European locale would split the record's own fields.
    /// </summary>
    public readonly struct GraveRecord
    {
        private const char FieldSeparator = ';';

        public long WorldId { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public int Day { get; }
        public int Items { get; }

        /// <summary>
        /// The tombstone ZDO's id as it stood when the grave was recorded.
        /// A NAME for this record, not a handle on anything: Valheim re-keys
        /// every ZDO it loads from the world save (`ZDO.Load` does
        /// `m_uid.SetID(++ZDOID.m_loadID)`), so these two numbers identify no
        /// live object at all once the world has been reloaded. Nothing may
        /// look a tombstone up by them -- see <see cref="GraveClearRule"/>.
        /// </summary>
        public long ZdoUserId { get; }
        public uint ZdoId { get; }

        public GraveRecord(long worldId, float x, float y, float z, int day, int items, long zdoUserId, uint zdoId)
        {
            WorldId = worldId;
            X = x; Y = y; Z = z;
            Day = day;
            Items = items;
            ZdoUserId = zdoUserId;
            ZdoId = zdoId;
        }

        public string Format() => string.Join(
            FieldSeparator.ToString(),
            WorldId.ToString(CultureInfo.InvariantCulture),
            X.ToString("R", CultureInfo.InvariantCulture),
            Y.ToString("R", CultureInfo.InvariantCulture),
            Z.ToString("R", CultureInfo.InvariantCulture),
            Day.ToString(CultureInfo.InvariantCulture),
            Items.ToString(CultureInfo.InvariantCulture),
            ZdoUserId.ToString(CultureInfo.InvariantCulture),
            ZdoId.ToString(CultureInfo.InvariantCulture));

        public static bool TryParse(string line, out GraveRecord record)
        {
            record = default;
            if (string.IsNullOrWhiteSpace(line)) return false;

            string[] parts = line.Split(FieldSeparator);
            if (parts.Length != 8) return false;

            var invariant = CultureInfo.InvariantCulture;
            if (!long.TryParse(parts[0], NumberStyles.Integer, invariant, out long worldId)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, invariant, out float x)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, invariant, out float y)) return false;
            if (!float.TryParse(parts[3], NumberStyles.Float, invariant, out float z)) return false;
            if (!int.TryParse(parts[4], NumberStyles.Integer, invariant, out int day)) return false;
            if (!int.TryParse(parts[5], NumberStyles.Integer, invariant, out int items)) return false;
            if (!long.TryParse(parts[6], NumberStyles.Integer, invariant, out long zdoUserId)) return false;
            if (!uint.TryParse(parts[7], NumberStyles.Integer, invariant, out uint zdoId)) return false;

            record = new GraveRecord(worldId, x, y, z, day, items, zdoUserId, zdoId);
            return true;
        }
    }
}
