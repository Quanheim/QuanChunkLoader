# Quan's Chunk Loader: design

Status: design agreed on 2026-10-25, not yet built. Valheim 1.0.20 (network version 41), BepInEx, Jotunn.

Quan's Chunk Loader (`QuanChunkLoader`) is a new chunk loader for dedicated servers. It replaces Frogger's original ChunkLoader and two later forks of it, NerdLounge "ChunkLoader Continued" and zeeCamelSnake "ChunkLoaderFix". It's a rewrite rather than a fork. The old mods share one core mechanism, and that mechanism causes their main bugs (see [Why the old approach fails](#why-the-old-approach-fails)).

Every claim below was checked against the 1.0.20 decompile and, where possible, against logs from a read-only diagnostic plugin. The diagnostic ran for two weeks on a live 5-player, 69-mod server (see `diagnostic-test-plan.md`).

## Goals

1. **Keep a base's production running** while its owner is elsewhere in the world, without breaking anything when they come back.
2. **Ownership always goes back to a player** who is near an object. The server holds an object only while no player is near it.
3. **Boats stay afloat** in loaded areas.
4. **Surtling farms keep spawning** without a player present.
5. **Up to one real hour of offline production** after the last player logs off.
6. **Low overhead** and clean coexistence with large modpacks.

Out of scope for v1:
- Changing conversion capacity or speed. Leave that to other mods.
- A server-side collector that refuels smelters and empties output while nobody is near. It's a possible later feature.

## Why the old approach fails

All three old mods postfix `ZNetScene.InActiveArea` to return `true` for any point in a loaded ("forced") zone, whatever reference point is being asked about. That single answer feeds several unrelated systems.

- **The server hoards ownership.** `ZDOMan.ReleaseNearbyZDOS` runs every 2 s for the server and for each player. A player takes an object only if its current owner is out of range, and the patched check always says the server is in range. So objects in a loaded zone stay with the server even while a player stands next to them. The only exception is a chest a player opens, which moves explicitly. Logs: across a week there were 57 handovers to players, every one of them an opened chest or a touched tame, and none happened through proximity. Separately, 1,873 objects went to the server across 31 logouts.
- **Players who leave keep ownership.** The same check tells the game that every loaded object is in range of every player. A player who visits a base and walks away keeps its objects until they log out. One logout moved 271 objects at five different bases to the server. zeeCamelSnake also patches `IsInPeerActiveArea`, which makes this worse.
- **Mods that need a player owner stop.** AzuAutoStore and OttoFuel only act on objects the local player owns, so with the server as owner they never run. Smelting chains stall even with a player in the base.
- **Vanilla code that assumes the owner has a player** throws errors when the server is the owner:
  - `Pickable.RPC_Pick` calls `Player.m_localPlayer.GetZDOID()`, so picking fails silently. One harvest logged about 120 of these errors.
  - `Ship.UpdateSailSize` fails the same way. The exception escapes `MonoUpdatersExtra.CustomFixedUpdate` and aborts the rest of that physics tick for fish, AI, characters and area effects.
- **Plants never regrow.** Only the owner's copy of a pickable runs its regrow check (`Pickable.UpdateRespawn`, every 60 s). If the owner is a player who has left, nobody runs it. PlantEverything's hover then shows "Ready" while the plant stays bare, until someone switches the loader off and reloads the area.
- **Boats sink at the edge of a loaded area.** `Ship.CustomFixedUpdate` averages five water samples: centre, bow, stern, left and right. A sample in a zone the server hasn't loaded returns -10000, so the average collapses and buoyancy switches off entirely. A server-owned boat moored at the edge of a loaded area then rests on the seabed. The diagnostic confirmed this in about 40,000 observations with no counter-examples. Every sinking had a sample in an unloaded zone, and every boat with all five samples loaded floated.
- **The map lags.** The old mods remove and re-add every loader pin every 4 s and scan all ZDOs on the client. Opening the large map stalls for a second or two, which also blocks TargetPortal's portal selection.

Two more facts limit what any chunk loader can do:

- **The world clock stops on an empty server.** `ZNet.UpdateNetTime` only advances world time while at least one player is connected. Smelters, kilns, fermenters, beehives, crops and plant regrowth all measure progress against that clock, so nothing progresses with nobody online. The server program itself keeps running, and loaded zones stay loaded.
- **Auto-fuel and auto-store mods run on players' machines.** They need a local player, and a dedicated server has none. So with no player near a base, nothing refuels a smelter or collects its output.

## Pieces

**Observer model (decided 2026-10-25):** Frogger's original `ChunkLoader_stone` model, as a tribute. It comes from the `chunkloader` asset bundle in [JFHeim/ChunkLoader](https://github.com/JFHeim/ChunkLoader) (`src/assets/chunkloader`, MIT).
- Scale the model to 75% of its original size (the original is very big). Review the look when we build.
- Ship the bundle as an embedded resource. Build `Quan_ChunkObserver` from its mesh, materials, light and particles, not from its prefab: the prefab references the old mod's `ChunkLoaderMono` script, which would log missing-script warnings.
- MIT requires the copyright and licence notice in copies, so add `THIRD-PARTY-NOTICES.md` to the repo root and to `Package/` (which ships in the Thunderstore zip).
- Check the shaders on the bundle's materials in game. If they don't render correctly, remap them to Valheim's own shaders.
- The bundle can't be loaded twice, so it would clash with the old mod if both were installed. The incompatibility check already prevents that.


Both pieces go in the **QuanPieces** hammer tab. Each costs 10 stone (configurable) and needs no crafting station. Neither carries a PlayerBase `EffectArea`, so neither suppresses spawners or counts as a base.

### Quan's Chunk Observer (`Quan_ChunkObserver`)

- **Loads the 3×3 block of zones around itself** (or 1×1, set in config) while it's switched on.
- **No fuel.** It runs until switched off from its hover menu. Switching respects wards (`PrivateArea.CheckAccess`).
- **Its glow updates immediately for everyone nearby** when switched. The old mod took 2-3 s.
- **Hover text** shows on/off and the builder's name. **Admins only** also see "Admin-placed" on observers an admin built. Other players never see it. The flag is stored on the observer (it has to survive restarts); it isn't secret data, just not displayed to non-admins.
- **It can't be destroyed and monsters ignore it.** It isn't a target for AI (`StaticTarget`, `Piece.m_targetNonPlayerBuilt`), and a prefix on `WearNTear.RPC_Damage` blocks damage. Removing it with the hammer still works.
- **Overlap is allowed.** Loaded zones form a set, so overlapping observers cost nothing extra. That lets a player add one at a dock on the edge of a base.

### Quan's Phantom Spawner (`Quan_PhantomSpawner`)

- **Makes nearby spawners behave as if a player were standing there.** Surtling spawners are `CreatureSpawner`s (`Spawner_imp_respawn`: 60 m trigger, 5-minute respawn on world time). Support is limited to a config allowlist, default `Spawner_imp_respawn`. `SpawnArea` support is also included, on by default.
- **Active only inside a zone that an active observer keeps loaded.** Outside one, its hover text says "Dormant: no active chunk observer" so players know why. The server decides this, because non-admins don't receive the observer list. When a player hovers over a phantom, their client asks the server once with a small RPC and caches the answer for a few seconds.
- **No fuel or timer, no map pin, can't be destroyed** (same as the observer). There's no per-zone limit.
- **Shows its effect radius while being placed**, using the dotted ring the workbench uses.
- **How it works:**
  - A prefix and finalizer on the spawner update sets a scoped "spawner context" flag. While the flag is set, a postfix on `Player.IsPlayerInRange` treats the phantom as a player at its own position, within the caller's own `range`. That covers both the plain and the noise overloads.
  - Nothing global changes and no transpilers are involved. `IsPlayerInRange` contains a `foreach`, so it won't be inlined.
- **Respawning follows world time,** because the `CreatureSpawner` respawn delay uses it. A farm spawns while anyone is online and during the empty-server hour, and stops after that.
- **Farm behaviour:** surtlings that spawn into water die at once, so the spawner's caps never bind. Collecting the drops still needs a player nearby, because auto-store runs on players' machines. Despawn protection (below) keeps the drops in the meantime.

## How loading and ownership work

All of this runs on the server. Clients load nothing extra.

1. **Forced set.** This is the union of the zones of all active observers. It's rebuilt from every active observer whenever one is placed, removed or switched, never by subtracting a removed observer's zones, because another observer may still cover them. There's no periodic per-observer work.
2. **Loading.** The server keeps every forced zone generated and its objects instantiated. The old `InActiveArea` postfix is replaced by narrow patches that apply only to the server's own scene and ownership decisions.
3. **Edge ring (on by default).** Around the combined forced area the server also loads a one-zone ring of zone roots only: terrain and water, no objects (`PokeLocalZone`). Physics at the edge then has ground and water, so boats moored on the edge float. The ring is computed from the combined set, so overlapping areas share it.
4. **Ownership rule.**
   - The server claims an object in a forced zone only when no connected player's vanilla, unpatched active area covers it. It gives the object up as soon as one does, and vanilla `ReleaseNearbyZDOS` hands it to that player within one 2 s pass.
   - Observers only add zones to the set. They're never compared with player positions, so overlapping or duplicate observers can't change ownership.
   - We never patch the range checks as seen by players. That's what stops departed players keeping ownership.
5. **Observer list.**
   - The server holds the authoritative observer list: position, on/off, builder, admin flag. Clients don't receive observers' data from across the world.
   - **Who gets what.** The server knows who is an admin (the world's admin list); clients don't, so it tells each client.
     - **Admins** get the full list: a snapshot on connect, then small add/change/remove updates. A client replaces its list on each snapshot, so a missed update can't leave a stale pin. It can also ask for a fresh snapshot, for example when opening the large map.
     - **Other players** get only their own observers and the server-wide total, which is enough for the limit pre-check. They never receive anyone else's positions.
   - If a player's admin status changes while they're online, the server sends the right set on their next snapshot request or reconnect.

## The empty-server hour

**Behaviour:**
- When the last player logs off and at least one observer is active, the world clock keeps advancing at normal speed for up to one real hour (configurable; 0 = off).
- Loaded smelters, kilns, blast furnaces, spinning wheels, windmills, fermenters, beehives, crops and regrowing plants then carry on as if a player were online.
- After the hour the clock stops, as in vanilla. Anyone logging back in finds the work already done.

**Rules:**
- **Real time only.** The clock advances by the real seconds elapsed and never in a lump, so a 10-minute logout gains 10 minutes.
- **Repeated logouts can't be abused.** The window restarts on each logout, but the clock can never run faster than real time.
- **No window after a restart** with nobody online. The daily restart ends any open window.
- **It's world-wide,** like a player idling online: the day counter advances and objects anywhere catch up when they next load.

**Implementation:** a server-side component adds the real elapsed time (`Time.unscaledDeltaTime`) to the network time while the player count is 0 and the window is open. That avoids patching the small, inlinable `UpdateNetTime`.

**Limits to explain in the README:**
- Only what's already loaded gets processed, because auto-fuel and auto-store need a player. A smelter holding 10 ore at 30 s per bar runs dry after 5 minutes, so higher-capacity stations get far more from the hour.
- Windmill speed comes from the wind as the server sees it, worked out at the server's reference position rather than the windmill's location, so offline output varies. The v3 diagnostic records wind on an empty server.

## Despawn protection (on by default)

Dropped items are destroyed after 3,600 s of world time unless one of these applies:
- they're inside a PlayerBase area (and above y 28);
- a player is within 25 m;
- they're in tar.

Offline output would therefore start vanishing shortly after the next login. Two scoped protections fix this. Each uses the scoped-flag trick around `ItemDrop.TimedDestruction`'s 25 m player check:

- **Near conversion stations:** drops near any conversion station's output point inside a forced zone don't time out.
- **Near phantoms:** drops near an active phantom don't time out.

Neither adds a PlayerBase area, so neither affects spawners.

## Defensive patches

Normally the ownership rule means these situations don't arise. The patches cover the roughly 2 s before ownership moves.

- `Pickable.RPC_Pick` on a server owner hands ownership to the player who asked and re-sends the RPC.
- `Ship.UpdateSailSize` skips the local-player effect when there's no local player.

## Limits and admin controls

Server config, synced, admin-only:

| Setting | Default |
|---|---|
| Observers per player | 2 |
| Observers in total | 12 (108 zones at 3×3) |
| Admins ignore limits | On |
| Only admins can build observers and phantoms | Off |
| Area size | 3×3 (or 1×1; changing it recalculates live) |
| Edge ring | On |
| Expire after N hours without a refresh | Never |
| Switching respects wards | On |
| Empty-server window | 60 min (0 = off) |
| Phantom `CreatureSpawner` allowlist | `Spawner_imp_respawn` |
| Phantom `SpawnArea` support | On |
| Despawn protection | On |
| Build cost | 10 stone, no station |

- **Admin placement.** Each observer stores its builder and an admin flag, set when it's built and checked against the server's admin list. Admin-placed observers count towards neither limit, and losing admin status later changes nothing.
- **Refusal messages** say which limit was hit:
  - Per player: "You've reached your limit of N observers. Remove one of yours to place another."
  - Server total: "This server's observer limit (N) has been reached. Ask an admin to raise it or remove unused observers."
  - Admins: "Admin placement: doesn't count towards observer limits."
- **Admin-only building (off by default).** When on, both pieces are hidden from non-admins' hammer menus. If a non-admin's piece gets through anyway (a race, or a modified client), the server switches it off and messages the builder, as it does for limits. Pieces that already exist are untouched. Useful for a controlled trial on a live server.
- **Enforcement.** The client checks first, using its own observers and the server-wide total sent by the server. The server is the safety net for races: it switches an over-limit observer **off**, never deletes it, and messages the builder.
- **Expiry** counts real hours, because the world clock stops on an empty server. Switching an observer on or interacting with it restarts its countdown. Turning expiry on later starts every existing observer's countdown from that moment, so nothing expires instantly.

Client config:
- **Show observers on the map (admins only):** off by default, also toggleable from the large map. When on, every observer in the world is shown, with separate active and inactive icons. The toggle doesn't appear for non-admins, and they have no data to show anyway.
- **Pins persist.** Only the pins that changed are updated. There are no timers while the map is open and no client-side ZDO scans.

## Compatibility

- **Hard dependency:** Jotunn (in the manifest too). ServerSync is embedded.
- **Incompatible:** the three old chunk loaders, declared with `BepInIncompatibility`. Two GUIDs cover all three: `com.Frogger.ChunkLoader` (Frogger's original, and NerdLounge's ChunkLoader Continued 1.8.0, which kept that GUID) and `com.zeecamelsnake.ChunkLoaderFix` (zeeCamelSnake 1.0.4; checked from the Thunderstore DLLs on 2026-10-25). Running alongside one would bring back the ownership bug. If BepInEx doesn't show the refusal clearly, the mod detects the old loader itself, logs a clear error and stays inactive.
- **No soft dependencies.** Nothing needs a particular load order. The hammer tab name is a fixed constant, `QuanPieces`, matching the QuanPieces mod, so if both are installed their pieces share one tab. That needs only the same name, not a dependency.
- **Patch order.** Before release, check every installed mod for patches on the methods we touch, and set Harmony priority or before/after per patch as needed. The methods are `ItemDrop.TimedDestruction`, `Player.IsPlayerInRange`, `ZDOMan` ownership, `ZNetScene` object creation, `CreatureSpawner`/`SpawnArea` updates, `Pickable.RPC_Pick`, `Ship.UpdateSailSize` and `WearNTear.RPC_Damage`.
- **Known interactions, none needing code:**
  - **AzuAutoStore and OttoFuel** start working inside loaded bases again, because players get ownership.
  - **PlantEverything** hover timers become accurate.
  - **Conversion Size & Speed** and similar mods complement the empty-server hour.
  - **TargetPortal's** map selection is no longer blocked.

## Migrating from an old chunk loader

1. **Before the swap,** players remove their old observers with the hammer. Once the old mod is gone, its pieces can't load and are destroyed, and the materials are lost.
2. **Swap the mods on the server and every client at the same time.** Don't run the two side by side.
3. Players build new observers. Their positions and on/off states start fresh.

## README outline

1. **Intro:** what the mod is, in a couple of lines.
2. **What it fixes:** the symptoms players actually see, each with its cause.
   - Production chains stall while players are in the base: ownership never goes back to them, so auto-fuel and auto-store can't act.
   - Plants show "Ready" but never regrow.
   - Moored boats sink at the edge of a loaded area.
   - Picks fail silently.
   - Surtling farms stop without a player nearby.
   - The large map lags, which blocks portal selection.
3. **How chunk loading really works, and what every chunk loader gets wrong:** explained as how the game works, not as criticism of anyone's mod.
   - "Loaded" doesn't mean "owned by a player".
   - Telling the game every loaded point is in range of everyone makes the server and departed players hoard objects.
   - Some vanilla code assumes the owner is a player.
   - A boat needs every water sample point loaded.
4. **What no chunk loader can ever do:**
   - The world clock stops when nobody is online, so nothing runs all night.
   - Auto-fuel and auto-store need a player nearby, so offline production only uses what's already loaded.
   - What this mod does instead: up to an hour of real-time production after the last logout.
5. **Features:** the two pieces with costs and how to use them; ownership handback; boats; the phantom spawner; the empty-server hour; despawn protection; map and admin controls; the defensive fixes.
6. **Dedication and credit (highlighted):**
   - Frogger had the original idea of a Valheim chunk loader.
   - As a tribute, the Chunk Observer uses Frogger's original chunk loader model, under the MIT licence (Copyright (c) 2023 Vyacheslav, [JFHeim/ChunkLoader](https://github.com/JFHeim/ChunkLoader)).
   - The model is the only thing reused: no code comes from Frogger's mod or any other current chunk loader mod.
7. **Configuration reference.**
8. **Recommended companions,** described by type only, not by name: conversion capacity and speed mods, auto-fuel, auto-store, with why each matters. Readers who want our picks can see them in "Tested with".
9. **Tested with:** every mod and version from the live server's plugin list.
10. **Migrating from an old chunk loader.**
11. **Known limits:** windmill variability, one hour at most.

## Versioning

The first release is 1.0.0. Builds stay at 1.0.0 through development and testing. The tagged commit that goes to Thunderstore fixes what 1.0.0 is; later changes bump the version. It has to be on Thunderstore so every player gets it through the shared Gale profile.

## Test plan for the real mod

**Stage 1, home lab:** a separate dedicated server running a copy of the live world, with one or two clients. This catches crashes, save problems and mod conflicts before anything touches live. It covers ownership, boats, the empty-server hour, the map and limits.

**Stage 2, live server:** with "Only admins can build" on, so only admins place observers while everyone plays normally. This tests real bases, several players and the full modpack.
- It's a full swap, not a side-by-side trial. The old loader is incompatible, and every client needs the new mod, so the migration steps below happen first.
- Back up the world before the swap.
- Keep the diagnostic running so the logs show ownership, plants and boats under the new mod.
- Lift the restriction once the admins' observers have behaved for a few days.

1. **Ownership.**
   - Set up two overlapping observers.
   - A player stands in the overlap, then inside only one observer's area. Nearby objects should move to the player within about 2 s.
   - A player stands in a forced zone but out of range of a target object. That object should stay with the server until the player approaches.
   - After logout, everything should return to the server within about 2 s.
   - A player who walks away while staying online should lose ownership within about 2 s.
2. **Regrowth.**
   - Pick plants in a forced zone, walk out of range, wait past the regrow time and return. The plants should have regrown, under the server as owner.
   - Repeat with two players, where the one who picked leaves but stays online.
3. **Boats.** Moor longships with the bow over the edge zone. They should float under server ownership, both with and without the edge ring. Without the ring they should sink, which reproduces the bug.
4. **Auto mods.** AzuAutoStore and OttoFuel should work in a loaded base with a player present.
5. **Phantom.**
   - A surtling farm with no player nearby should keep spawning while someone is online elsewhere.
   - The phantom should show the dormant message without an observer.
   - Drops near it should survive more than an hour of world time.
6. **Empty-server hour.**
   - Log out with loaded smelters. World time should advance for 60 real minutes, then stop.
   - The output should still be there after the next login.
   - A 10-minute absence should gain exactly 10 minutes.
   - With no active observer there should be no window.
   - The world clock should never run faster than real time.
7. **Limits.** Test the per-player and server-total caps and their messages, admin placement, the server's over-limit safety net, and expiry turned on later.
8. **Map.** Toggle default off and visible only to admins. A non-admin has no toggle and receives no other players' observers (check the RPC traffic). An admin sees "Admin-placed" on hover; a non-admin doesn't. Pins should match the server list after offline changes. Open the map through a TargetPortal portal: portals should be clickable immediately.
9. **Toggle feedback.** The glow should change at once for every nearby player.
10. **Old mod present.** Our mod should refuse clearly.

## Still open

- **Seed Totem 4.3.13:** confirm by decompiling that its failures belong to the same "owner expected to be a local player" class. They are probably fixed by the ownership rule.
- **Plant ownership on the live server:** the v3 diagnostic (installed 2026-10-25) will show who owned overdue plants.
- **Real conversion station values and empty-server wind:** v3 logs both.
- **Phantom Spawner look:** to be chosen in game by the user. The fallback is a recoloured or kitbashed vanilla piece. Map icons: vanilla pin icons for now.
