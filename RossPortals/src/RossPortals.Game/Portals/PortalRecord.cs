using RossPortals.Core;
using UnityEngine;

namespace RossPortals.Game.Portals
{
    /// <summary>
    /// One portal as the engine tracks it: the game-side counterpart to Core's
    /// <see cref="PortalEntry"/>. Carries real engine types (a <see cref="ZDOID"/>
    /// identity, a <see cref="Vector3"/> location); <see cref="ToCore"/> projects
    /// it down to the engine-free shape the list UI reasons about.
    ///
    /// This is also the wire format for the server→client sync: <see cref="Pack"/>
    /// and <see cref="FromPackage"/> must stay in lockstep. The colour and any
    /// per-session visual state deliberately aren't sent — the destination and
    /// the name are the only facts a peer can't derive for itself.
    /// </summary>
    internal sealed class PortalRecord
    {
        public ZDOID Id;
        public string Name;
        public Vector3 Location;
        public ZDOID Target;

        public PortalRecord(ZDOID id)
        {
            Id = id;
            Name = string.Empty;
            Location = Vector3.zero;
            Target = ZDOID.None;
        }

        public bool HasTarget => Target != ZDOID.None && !Target.IsNone();

        /// <summary>The opaque string Core uses to name this portal.</summary>
        public string Key => PortalKey.Of(Id);

        public PortalEntry ToCore()
            => new PortalEntry(
                Key,
                Name,
                new Vec3(Location.x, Location.y, Location.z),
                HasTarget ? PortalKey.Of(Target) : null);

        public ZPackage Pack()
        {
            var pkg = new ZPackage();
            pkg.Write(Id);
            pkg.Write(Name ?? string.Empty);
            pkg.Write(Location);
            pkg.Write(Target);
            return pkg;
        }

        public static PortalRecord FromPackage(ZPackage pkg)
        {
            var id = pkg.ReadZDOID();
            return new PortalRecord(id)
            {
                Name = pkg.ReadString(),
                Location = pkg.ReadVector3(),
                Target = pkg.ReadZDOID(),
            };
        }
    }
}
