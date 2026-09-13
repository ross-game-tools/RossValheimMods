# RossPortalTames

Tames that are following you come through the portal with you.

Walk your wolves up to a portal, step through, and they step through
too — instead of being left behind on the other side of the map. Only
tames that are actually following you and within range make the trip;
anything ridden (a saddled lox, for example) is excluded, since it isn't
"left behind" in the same sense.

## Client-side only

This mod is entirely client-side. There is nothing to install on a
dedicated server — install it only for the players who want their tames
to follow them through portals, and it works with servers that don't
have it at all.

## Config

All settings live under `[General]` and are local to your own client —
they only affect which of your own tames follow you.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Whether tames following you come through portals at all. |
| `FollowRadius` | `20` (metres) | How close a following tame must be to come along. Measured in three dimensions, so a tame on a roof is as far as one across the ground. Set to `0` to bring nothing. |
| `SearchDistance` | `6` (metres) | How far from your arrival point to look for a clear spot to place each tame. Anything that can't be placed within this distance is put at your own position instead, where creatures separate themselves naturally. Lower it for tight portal huts. |

## Dependencies

- BepInEx 5.4.2350

No Jotunn dependency.
