# Test plan: Combat / InstantLoot

What changed: a postfix on `Ragdoll.Setup` drops a dying creature's stored
loot immediately, on the corpse's owner, then zeroes the stored count so the
fade-out drops nothing. The risk is **duplicated or lost loot**, so every
step below watches for loot appearing twice or not at all.

Build under test: branch `feat/rossqol-instant-loot`, deployed to the r2modman
`dev` profile. The plugin still reports version 0.2.0; confirm it is this
build by the log line `Combat/InstantLoot patched.`

## 0. Backup (do this first)

1. Quit Valheim.
2. Copy these folders somewhere safe:
   - `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local`
   - `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\characters_local`
   - `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\adminlist.txt` (if present)
3. Use a throwaway world named `loottest` and a throwaway character for
   everything below. Never test on a real world.

## 1. Setup

### Client (dev profile)

1. In r2modman, `dev` profile: confirm **RossQoL** and **Jotunn** are enabled.
2. Add `-console` to the launch arguments (Settings, Set launch parameters)
   so F5 opens the console.
3. **Disable InstantMonsterLootDrop** for sections 2 to 4; it is turned back
   on in section 5.
4. Compatibility mod, for section 5: install **Drop That** (ASharpPen) in the
   `dev` profile, disabled for now. It rewrites creature drop tables, which is
   exactly the list this feature drops early.

### Local dedicated server (section 6)

1. Steam Library, Tools: install **Valheim Dedicated Server**.
2. Open `%APPDATA%\r2modmanPlus-local\Valheim\profiles\dev`. Copy
   `BepInEx\`, `winhttp.dll` and `doorstop_config.ini` into
   `C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\`.
   In the server's `BepInEx\plugins`, keep only `ValheimModding-Jotunn` and
   `RossQoL`. Delete the other plugins there.
3. Copy `start_headless_server.bat` to `start_test_server.bat` and set the
   launch line to:
   `valheim_server -nographics -batchmode -name "RossTest" -port 2456 -world "loottest" -password "loot12345" -crossplay 0 -public 0`
4. To allow console commands on the server, add your SteamID64 to
   `adminlist.txt` in the server's save folder (the folder is printed in the
   server window on start).

## 2. Single player: instant loot, once

Load `loottest`. In the console run `devcommands`, `god`, then `spawn Club`
and pick up the club.

| # | Action | Expected |
|---|---|---|
| 2.1 | Check `BepInEx\LogOutput.log` | `Combat/InstantLoot patched.`, and no `InstantLoot:` errors |
| 2.2 | `spawn Boar 3`, kill each with a club | Loot pops out **as it dies**, at the body. The corpse falls over and stays |
| 2.3 | Stand still for 15 s and watch each corpse | The corpse fades with its poof. **No second batch of loot appears** |
| 2.4 | `spawn Troll`, kill it | Troll hide and coins drop at death. Nothing more when the corpse fades |
| 2.5 | `spawn Greydwarf 5 3` (level 3 = two stars), kill them | Drops at death, larger amounts as vanilla gives starred creatures. Nothing at fade |
| 2.6 | Kill a Neck in water, or a Deathsquito mid-air | Loot drops where it dies; nothing extra at fade |
| 2.7 | Pick everything up. Note the inventory counts per item | Keep these as the "on" counts |

**Duplication check:** after every kill, count the ground items twice: right
after death, and again after the corpse has faded. The two counts must be equal.

## 3. Single player: vanilla comparison

1. In `BepInEx\config\com.rossdwest.rossqol.cfg` set `[Combat] InstantLoot = false`
   and save while the game runs. The log shows
   `Config reload: [Combat] InstantLoot true -> false`.
2. Repeat 2.2 and 2.4: loot now appears **only when the corpse fades**, and only once.
3. Across about 10 boars in each mode, the drops should be in the same range.
   Drops are random, so compare ranges, not exact numbers.
4. Set `InstantLoot = true` again and save. The next kill drops instantly (no restart needed).

## 4. Edge cases

| # | Action | Expected |
|---|---|---|
| 4.1 | Kill a creature, then log out before its corpse fades. Log back in and wait | Loot was already on the ground and is still there once. The corpse (if it reloads) drops nothing |
| 4.2 | Kill a creature at the edge of loaded range and run away before the fade | No loot lost: it dropped at death |
| 4.3 | Kill a tamed creature: tame a boar, then stand next to it and run `killtame` (or hit it with the Butcher knife) | Drops once at death |
| 4.4 | Die yourself | Tombstone as normal; nothing extra dropped |

## 5. Compatibility

| # | Setup | Action | Expected |
|---|---|---|---|
| 5.1 | Enable **InstantMonsterLootDrop**, restart | Kill 5 boars | Loot drops **once**. Corpses vanish almost at once (that is IMLD). No errors from either mod |
| 5.2 | IMLD on, `InstantLoot = false` | Kill 5 boars | Loot once, corpses vanish (IMLD alone) |
| 5.3 | IMLD off, enable **Drop That**, restart | Kill boars, trolls, greydwarves | Drops match Drop That's tables, at death, once. No errors |
| 5.4 | Disable Drop That again | | |

## 6. Local dedicated server, one client

1. Run `start_test_server.bat`. In the server's `BepInEx\LogOutput.log`,
   wait for `Combat/InstantLoot patched.` and the "Game server connected" line.
2. Launch the `dev` client, choose Join game, add server `127.0.0.1:2456`,
   password `loot12345`.

| # | Action | Expected |
|---|---|---|
| 6.1 | Kill 5 boars near you | Loot at death, once. Nothing at fade |
| 6.2 | On the **server**, set `[Combat] InstantLoot = false` in its cfg and save | Server log shows `Config reload: [Combat] InstantLoot true -> false`. The next kill on the client drops at fade (vanilla), once |
| 6.3 | On the **client**, set `InstantLoot = true` in your own cfg and save | Ignored while connected: kills still follow the server's `false`. If kills turn instant here, report it: that is a bug in the live-reload code, not in InstantLoot |
| 6.4 | Server back to `true` | Instant drops again, with no reconnect |
| 6.5 | Disconnect and reconnect | Still follows the server's value |
| 6.6 | Check both logs | No `InstantLoot:` errors, no `Ragdoll: Missing prefab` |

## 7. Two clients (needs a second PC or Steam account)

Client B joins the same server. The first player in an area owns its creatures.

| # | Action | Expected |
|---|---|---|
| 7.1 | B stands in an area first; A arrives and kills a boar there (B owns it) | Loot at death, once, seen by both |
| 7.2 | Swap roles | Same |
| 7.3 | Both hit the same troll; it dies | Loot once |
| 7.4 | Both count ground items after death and after fade | Counts equal on both clients |

## 8. Pass criteria

- Loot never appears twice, in any section.
- Loot is never missing compared with vanilla ranges (section 3).
- No `InstantLoot:` errors in any log.
- Toggling on the server changes behaviour on connected clients without a reconnect.

## 9. Cleanup

Restore the backups from section 0 if anything went wrong. Remove the
server's BepInEx files if the dedicated server is not needed again.
