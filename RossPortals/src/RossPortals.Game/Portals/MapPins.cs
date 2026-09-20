using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RossPortals.Game.Portals
{
    /// <summary>
    /// Keeps a map pin on every portal marked "show on map", labelled with its
    /// name and drawn with Valheim's own portal pin icon. Pins are rebuilt from
    /// the (server-synced) registry, so they track portals being placed, renamed,
    /// hidden or removed by anyone.
    ///
    /// The pins are transient (save: false): never written to the map save, just
    /// re-derived each session from the synced list. That avoids duplicating them
    /// on reload and leaving stale pins when a portal is destroyed.
    ///
    /// <see cref="Minimap.PinType.Icon4"/> is the portal icon in the vanilla pin
    /// set (verified from a live portal pin), so we add pins under it and let the
    /// game supply the sprite -- no icon override, no prefab lookup.
    /// </summary>
    internal static class MapPins
    {
        private const Minimap.PinType PinType = Minimap.PinType.Icon4;

        private static readonly Dictionary<string, Minimap.PinData> _pins = new Dictionary<string, Minimap.PinData>();

        /// <summary>Reconcile the pins with the current portal list. Cheap to
        /// call on every registry change: only actual differences touch the map.</summary>
        public static void Refresh()
        {
            if (Env.IsHeadless || Minimap.instance == null) return;

            var seen = new HashSet<string>();

            foreach (var portal in PortalRegistry.Instance.GetList())
            {
                // Portals marked hidden get no pin; if one had a pin, leaving it
                // out of `seen` prunes it below.
                if (!portal.ShowOnMap) continue;

                var key = portal.Key;
                seen.Add(key);
                var name = string.IsNullOrEmpty(portal.Name) ? "Portal" : portal.Name;

                if (_pins.TryGetValue(key, out var pin))
                {
                    // Reposition/rename by replacing the pin -- PinData has no
                    // clean public "rename" and this only fires on real changes.
                    if (pin.m_pos == portal.Location && pin.m_name == name) continue;
                    Minimap.instance.RemovePin(pin);
                }

                _pins[key] = Minimap.instance.AddPin(portal.Location, PinType, name, save: false, isChecked: false);
            }

            foreach (var key in _pins.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                Minimap.instance.RemovePin(_pins[key]);
                _pins.Remove(key);
            }
        }

        /// <summary>Forget every pin (world unload).</summary>
        public static void Clear()
        {
            if (Minimap.instance != null)
                foreach (var pin in _pins.Values)
                    Minimap.instance.RemovePin(pin);
            _pins.Clear();
        }
    }
}
