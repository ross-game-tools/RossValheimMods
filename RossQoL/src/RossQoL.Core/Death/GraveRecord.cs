using System;
using System.Globalization;

namespace RossQoL.Core.Death
{
    /// <summary>
    /// One remembered grave. Stored as a single line in the player's custom
    /// data, so every field is formatted invariantly -- a decimal comma from
    /// a European locale would split the record's own fields.
    ///
    /// The line comes in two lengths. Eight fields is a plain grave; eleven
    /// adds the dungeon entrance the grave sits behind, which only a grave
    /// inside an interior ever has. <see cref="TryParse"/> accepts both, so a
    /// character whose graves were written before entrances existed keeps
    /// every one of them -- losing a player's graves mid-run to a format bump
    /// would cost them the gear those records point at.
    ///
    /// The reverse is not true, and deliberately so: an eleven-field line
    /// read by a build that predates this one fails to parse and is skipped.
    /// That is a downgrade only, it costs dungeon graves and nothing else,
    /// and pre-tagging every old record with a version field to avoid it
    /// would have cost exactly the compatibility it was meant to protect.
    /// </summary>
    public readonly struct GraveRecord
    {
        private const char FieldSeparator = ';';

        /// <summary>Field counts the two shapes of a stored line have.</summary>
        private const int PlainFieldCount = 8;
        private const int WithEntranceFieldCount = 11;

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

        /// <summary>
        /// Where the dungeon door back to the surface is, for a grave inside
        /// an interior. Recorded at death, while the interior is certainly
        /// loaded and the player is standing in it -- resolving it later would
        /// need the dungeon streamed in, which is exactly what it is not when
        /// the marker is pointing across the map. See <see cref="GraveAim"/>
        /// for which of the two points is aimed at.
        /// </summary>
        public bool HasEntrance { get; }
        public float EntranceX { get; }
        public float EntranceY { get; }
        public float EntranceZ { get; }

        public GraveRecord(long worldId, float x, float y, float z, int day, int items, long zdoUserId, uint zdoId)
            : this(worldId, x, y, z, day, items, zdoUserId, zdoId, false, 0f, 0f, 0f)
        {
        }

        public GraveRecord(
            long worldId, float x, float y, float z, int day, int items, long zdoUserId, uint zdoId,
            float entranceX, float entranceY, float entranceZ)
            : this(worldId, x, y, z, day, items, zdoUserId, zdoId, true, entranceX, entranceY, entranceZ)
        {
        }

        private GraveRecord(
            long worldId, float x, float y, float z, int day, int items, long zdoUserId, uint zdoId,
            bool hasEntrance, float entranceX, float entranceY, float entranceZ)
        {
            WorldId = worldId;
            X = x; Y = y; Z = z;
            Day = day;
            Items = items;
            ZdoUserId = zdoUserId;
            ZdoId = zdoId;
            HasEntrance = hasEntrance;
            EntranceX = entranceX; EntranceY = entranceY; EntranceZ = entranceZ;
        }

        /// <summary>
        /// The eight common fields, plus the three entrance fields only when
        /// there is an entrance -- a surface grave, which is nearly all of
        /// them, stays exactly the line it has always been.
        /// </summary>
        public string Format()
        {
            string common = string.Join(
                FieldSeparator.ToString(),
                WorldId.ToString(CultureInfo.InvariantCulture),
                X.ToString("R", CultureInfo.InvariantCulture),
                Y.ToString("R", CultureInfo.InvariantCulture),
                Z.ToString("R", CultureInfo.InvariantCulture),
                Day.ToString(CultureInfo.InvariantCulture),
                Items.ToString(CultureInfo.InvariantCulture),
                ZdoUserId.ToString(CultureInfo.InvariantCulture),
                ZdoId.ToString(CultureInfo.InvariantCulture));

            if (!HasEntrance) return common;

            return string.Join(
                FieldSeparator.ToString(),
                common,
                EntranceX.ToString("R", CultureInfo.InvariantCulture),
                EntranceY.ToString("R", CultureInfo.InvariantCulture),
                EntranceZ.ToString("R", CultureInfo.InvariantCulture));
        }

        public static bool TryParse(string line, out GraveRecord record)
        {
            record = default;
            if (string.IsNullOrWhiteSpace(line)) return false;

            string[] parts = line.Split(FieldSeparator);
            bool hasEntrance = parts.Length == WithEntranceFieldCount;
            if (parts.Length != PlainFieldCount && !hasEntrance) return false;

            var invariant = CultureInfo.InvariantCulture;
            if (!long.TryParse(parts[0], NumberStyles.Integer, invariant, out long worldId)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, invariant, out float x)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, invariant, out float y)) return false;
            if (!float.TryParse(parts[3], NumberStyles.Float, invariant, out float z)) return false;
            if (!int.TryParse(parts[4], NumberStyles.Integer, invariant, out int day)) return false;
            if (!int.TryParse(parts[5], NumberStyles.Integer, invariant, out int items)) return false;
            if (!long.TryParse(parts[6], NumberStyles.Integer, invariant, out long zdoUserId)) return false;
            if (!uint.TryParse(parts[7], NumberStyles.Integer, invariant, out uint zdoId)) return false;

            if (!hasEntrance)
            {
                record = new GraveRecord(worldId, x, y, z, day, items, zdoUserId, zdoId);
                return true;
            }

            if (!float.TryParse(parts[8], NumberStyles.Float, invariant, out float ex)) return false;
            if (!float.TryParse(parts[9], NumberStyles.Float, invariant, out float ey)) return false;
            if (!float.TryParse(parts[10], NumberStyles.Float, invariant, out float ez)) return false;

            record = new GraveRecord(worldId, x, y, z, day, items, zdoUserId, zdoId, ex, ey, ez);
            return true;
        }
    }
}
