# Diagnostic build: test plan

The diagnostic build is a small, server-only BepInEx plugin, `QuanChunkLoaderDiag`. It watches the existing NerdLounge chunk loader on the live server and records what is happening. It changes nothing. Its job is to confirm or rule out four suspected causes before we build the real mod.

## What it answers

| # | Question | Suspected answer |
|---|---|---|
| 1 | Who owns objects in a forced zone as players log in and out? | The server keeps ownership and never hands it back to a player standing there. |
| 2 | Why do server-owned boats sink? | The server's water detection mask (`Floating.s_waterVolumeMask`) is never initialised, so the server sees no water. |
| 3 | What kind of spawner is a surtling geyser? | A single-creature spawner (`CreatureSpawner`), because a workbench stops it. |
| 4 | Does the old chunk loader piece count as a player base, like a workbench? | Unknown. |

## Why it's safe to run on the live server

- **Server only.** Clients don't install it, and it doesn't take part in mod version checks, so nobody else has to do anything.
- **Read only.** It uses no Harmony patches. It looks at the world every few seconds and writes what it sees to a log. It never changes an object, its ownership or any setting.
- **It won't accidentally fix the boat.** It reads the water mask without calling the game method that would set it, so its presence can't change the result it's measuring.
- **Cheap.** It only looks at objects of interest (ships, chests, smelters/kilns, spawners, tames, fermenters, beehives) and only inside forced zones. It also counts dropped items, without listing them.
- **Bounded log.** One file per day under `BepInEx/QuanChunkLoaderDiag/v3/` (v2 wrote to `v2/`, v1 to `BepInEx/QuanChunkLoaderDiag/`), capped at 20 MB per day. It records changes, not every check.
- **Easy to remove.** Delete the DLL and restart. It leaves nothing in the world save.

## Installing and removing it

1. Build it with `dotnet build -c Release` in `Diagnostics/QuanChunkLoaderDiag/`. The DLL lands in `Diagnostics/dist/QuanChunkLoaderDiagV3.dll`.
   - **Upgrading:** delete the older DLL (`QuanChunkLoaderDiag.dll` or `QuanChunkLoaderDiagV2.dll`) from the server first. Both versions share a plugin GUID, so BepInEx refuses to load two at once anyway. Older logs stay where they are.
2. Copy that one file into the **server's** `BepInEx/plugins/` folder (any subfolder is fine). It doesn't go in the Gale profile, so clients are unaffected.
3. Restart the server. Its `LogOutput.log` should show `QuanChunkLoaderDiag v3 0.3.0 loaded`, and `BepInEx/QuanChunkLoaderDiag/v3/` should appear once the world has loaded.
4. To remove it, delete the DLL and restart.

## What it records

- **On every server start:**
  - Every installed BepInEx plugin with its version, which also answers the "which auto-fuel, Seed Totem, longship and portal mods?" question.
  - Every surtling geyser and spawner prefab, with its spawner type, trigger distance, respawn time and whether it may spawn inside a player base.
  - Whether the old `ChunkLoader_stone` prefab carries a player-base area (`EffectArea` PlayerBase).
  - The water mask value.
- **Every 2 seconds:** the forced zones, and every ownership change of a watched object in them (from owner, to owner, and whether the new owner is the server or a named player).
- **Every 5 seconds, for each boat the server has loaded:**
  - its owner and height;
  - the water level the game's own lookup returns (expected to be "no water" if the mask is 0);
  - the water level our own correct lookup finds;
  - (v0.2) the ship's own buoyancy decision re-run read-only: ON or OFF, plus each of its 5 water sample points with its zone, whether the server has that zone loaded, and the water level found there.
- **Every 30 seconds (v0.3), picked plants** in forced zones and around each player: any plant more than 2 minutes past its regrow time is logged with its owner and whether that owner can actually run the regrow check (server has it loaded / player is near it / player far away / offline). Regrowth of overdue plants is logged too, and each heartbeat adds a plant summary line.
- **Events:** player connect and disconnect (name and position), chunk loader on/off, world time, player count.

## Phase 1: passive (3 to 7 days, no effort needed)

Install the DLL on the server and restart it. Then play normally. The daily 04:00 restart, people logging in and out at different times, and boats sitting at the dock are exactly the data we need. Seven days is ideal; three is enough.

## Phase 2: scripted session (about 45 minutes, one player)

Do this on one day during Phase 1, at a quiet time. One player is enough; a second player only makes step 4 easier to watch. Note the real clock time (to the minute) when you start each step, so I can match your steps to the log.

| Step | What you do | Time | What it shows |
|---|---|---|---|
| 1 | Log in somewhere **away** from the base and wait there. | 10 min | Ownership while you're online elsewhere, and whether boats sink with the server as owner. |
| 2 | Travel to the base. Stand at the dock without touching anything. | 5 min | Whether ownership comes to you just by being there (expected: no). |
| 3 | Open one chest in a production chain, then close it. | 2 min | Opening a chest transfers ownership (expected: yes, for that chest only). |
| 4 | Switch the chunk loader **off**, watch the boats, then switch it back **on**. | 5 min | How quickly ownership is handed over, and whether boats surface. |
| 5 | Log out at the base. Stay out for 10 minutes. Log back in at the base. | 15 min | The logout/login boundary that triggers the original bug. |
| 6 | Visit one surtling farm and stand near the geyser. | 5 min | Spawner behaviour with a real player present. |
| 7 | Note the end time and carry on as normal. | | |

Afterwards, write down anything you saw that seemed odd (a boat sinking or surfacing, a chain stopping), with the rough time.

## What to send back

- Everything in `BepInEx/QuanChunkLoaderDiag/` (including the `v2/` and `v3/` folders) for the test period.
- The server's `BepInEx/LogOutput.log` from the scripted session day.
- Your Phase 2 notes, with the step times. Tell me if the server's clock or time zone differs from yours.

## Optional: a test server

Not needed for the diagnostic, because it's read-only. The test server becomes important later, when we try out the real mod, since that one does change behaviour. Keep a copy of the live world ready for that.
