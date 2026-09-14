# Changelog

## 0.1.0

First public build.

- Tames following you that are within `FollowRadius` come through the
  portal with you and arrive near your destination.
- Each arriving tame is placed at a clear spot within `SearchDistance`
  of your arrival point where possible, falling back to your own
  position if nothing clear is found nearby.
- Ridden creatures (e.g. a saddled lox) are excluded — they aren't left
  behind in the first place.
- Client-side only; no server install required.
