# AGENTS.md

Conventions for agents working in this repository.

## Verified Valheim internals go in `docs/valheim-api/`

Both mods here patch Valheim by name, so a wrong signature or a half-remembered
method body costs a build-deploy-test cycle to find. Everything we learn about
the game's own code is therefore written down:

- **Look there first.** Before decompiling anything, read
  `docs/valheim-api/README.md` and the file for the subsystem you are touching.
  Re-deriving what is already recorded wastes a decompile pass.
- **Add to it as you go.** Whenever you verify a Valheim type, member,
  signature or method body — during design, while writing a patch, or while
  debugging one — append it to the relevant file in the same session, with the
  real decompiled code quoted. Do not start a fresh dump beside an existing
  one; extend the existing one.
- **Only verified facts.** Nothing in there comes from memory. Every file
  records the game version it was read from, and anything that could not be
  confirmed from the assemblies is called out as unverified — Unity serialized
  values (status effect durations, icons, prefab fields) are not in the DLLs at
  all and must be read at runtime.
- **Write it down even when it turns out you were wrong.** A member that does
  not exist, a hook that never fires, an inlined method a patch cannot reach:
  those are the entries that save the most time later.
