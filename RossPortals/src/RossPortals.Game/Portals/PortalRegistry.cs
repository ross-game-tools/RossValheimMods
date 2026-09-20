using System.Collections.Generic;
using System.Linq;
using RossPortals.Core;

namespace RossPortals.Game.Portals
{
    /// <summary>
    /// The client-visible list of every portal in the world, keyed by ZDOID.
    /// The server owns the truth (it scans <c>ZDOMan.GetPortalList()</c>); every
    /// peer mirrors it here from sync RPCs. The panel reads from here, projected
    /// through <see cref="BuildEntries"/> into Core's engine-free shape.
    /// </summary>
    internal sealed class PortalRegistry
    {
        public static PortalRegistry Instance { get; } = new PortalRegistry();

        private readonly Dictionary<ZDOID, PortalRecord> _portals = new Dictionary<ZDOID, PortalRecord>();

        private PortalRegistry() { }

        public int Count => _portals.Count;

        public bool Contains(ZDOID id) => _portals.ContainsKey(id);

        public PortalRecord GetById(ZDOID id) => _portals.TryGetValue(id, out var p) ? p : null;

        /// <summary>Resolve the opaque string id a Core row carries back to the
        /// live record. Linear, but a world has dozens–hundreds of portals and
        /// this runs once per selection, not per frame.</summary>
        public PortalRecord GetByKey(string key)
            => key == null ? null : _portals.Values.FirstOrDefault(p => p.Key == key);

        public List<PortalRecord> GetList() => _portals.Values.ToList();

        public List<PortalRecord> GetPortalsWithTarget(ZDOID target)
            => _portals.Values.Where(p => p.Target == target).ToList();

        public PortalRecord AddOrUpdate(PortalRecord portal)
        {
            _portals[portal.Id] = portal;
            return portal;
        }

        public bool Remove(ZDOID id) => _portals.Remove(id);

        public void Reset() => _portals.Clear();

        /// <summary>Core entries for the panel, excluding one portal (a portal
        /// can't target itself — that's the one being configured).</summary>
        public IReadOnlyList<PortalEntry> BuildEntries(ZDOID exclude)
        {
            var entries = new List<PortalEntry>(_portals.Count);
            foreach (var portal in _portals.Values)
            {
                if (portal.Id == exclude) continue;
                entries.Add(portal.ToCore());
            }
            return entries;
        }

        // --- Wire format (server → client) ---

        public ZPackage Pack()
        {
            var all = GetList();
            var pkg = new ZPackage();
            pkg.Write(all.Count);
            foreach (var portal in all) pkg.Write(portal.Pack());
            return pkg;
        }

        /// <summary>Replace the whole list from a full resync package: add/update
        /// everything present, drop anything that isn't.</summary>
        public void ApplyResync(ZPackage pkg)
        {
            var count = pkg.ReadInt();
            var incoming = new List<PortalRecord>(count);
            for (int i = 0; i < count; i++)
                incoming.Add(PortalRecord.FromPackage(pkg.ReadPackage()));

            ReplaceAll(incoming);
        }

        /// <summary>Server-side: rebuild the list from the live portal ZDOs.</summary>
        public void ApplyPortalZdos(IEnumerable<ZDO> zdos)
            => ReplaceAll(zdos.Select(PortalZdo.Read).ToList());

        private void ReplaceAll(List<PortalRecord> incoming)
        {
            foreach (var portal in incoming) AddOrUpdate(portal);

            var incomingIds = new HashSet<ZDOID>(incoming.Select(p => p.Id));
            foreach (var id in _portals.Keys.Where(id => !incomingIds.Contains(id)).ToList())
                _portals.Remove(id);
        }
    }
}
