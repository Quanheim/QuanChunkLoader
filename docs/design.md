# Quan's Chunk Loader: design

Status: design agreed on 2026-10-25. Core built on 2026-11-04 and checked in single player on 2026-11-05.
**The rest of 1.0 built on 2026-11-05**, awaiting its single-player check. See [Build status](#build-status).
Valheim 1.0.21 (network version 41), BepInEx, Jotunn.

Quan's Chunk Loader (`QuanChunkLoader`) is a new chunk loader for dedicated servers. It replaces Frogger's original ChunkLoader and two later forks of it, NerdLounge "ChunkLoader Continued" and zeeCamelSnake "ChunkLoaderFix". It's a rewrite rather than a fork. The old mods share one core mechanism, and that mechanism causes their main bugs (see [Why the old approach fails](#why-the-old-approach-fails)).

Every claim below was checked against the 1.0.20 decompile, re-checked against 1.0.21 on 2026-11-01, and, where possible, against logs from a read-only diagnostic plugin. The diagnostic ran for two weeks on a live 5-player, 69-mod server (see `diagnostic-test-plan.md`).

## Build status

Updated 5 Nov 2026. **Everything planned for 1.0 is built.** The core passed its single-player check on 5 Nov;
the rest hasn't run in game yet.

**Pieces: three**, all in the QuanPieces hammer tab, none needing a crafting station:

| Piece | Prefab | Loads | Cost |
|---|---|---|---|
| Chunk Observer | `Quan_ChunkObserver` | 1 zone: its own (64 × 64 m) | 10 Stone |
| Chunk Observer Cluster | `Quan_ChunkObserverCluster` | 9 zones: its own and the 8 around it (192 × 192 m) | 30 Stone |
| Phantom Spawner | `Quan_PhantomSpawner` | Nothing; runs nearby spawners inside loaded zones | 10 Stone |

The two observers replace the "Area size" setting (Quandru, 5 Nov 2026): players choose per observer by what they
build. Costs are fixed; the "Stone cost" setting is gone (Quandru: a slider for it is pointless).

**Core: passed its single-player check on 5 Nov 2026** (local world, "Chunkloader test" Gale profile, 71 mods,
Log detail Trace), on the single observer when it still loaded 3×3:
- Built, switched, hit (no damage) and removed as designed; red runes off, blue on, the hammer's remove highlight
  clears back to the right colour.
- **Stayed loaded with the player away:** three 5-minute summaries (22:29–22:39) with the player far off showed
  9 zones, 1,178 of 1,188 objects running, 3 tames and 7 wild creatures paused with no owner. Passes took 0.4 ms.
- **Survived a reload:** "Found 1 chunk observer(s) in the world, 1 switched on" and the 9 zones loaded again.
- No errors from the mod.
- **Found:** passes that write ~1,000 Trace lines took up to about 1 s (loading the world, flying away, coming
  back), because every line was written and flushed one at a time. The daily file now flushes once a second;
  BepInEx's own log still writes each line. Info, the default, logs none of those lines. Don't leave Debug or Trace
  on for the live server.
- **Local game only:** the host and the server share one owner ID, so while the host passes the edge of their own
  area vanilla `ReleaseNearbyZDOS` drops objects to no owner and our pass claims them back, two or three times each.
  On a dedicated server the server's ID differs, so this can't happen.

| Part | What it means | Status |
|---|---|---|
| Chunk Observer and Cluster | Frogger's model at 50%: one column, or three on round plinths. Dedication on both build cards. Can't be damaged; the hammer removes them. | Single checked; cluster built 5 Nov |
| Switching | E switches it on or off. Only its builder or an admin can, by default; the hover shows the size, who built it, and "Admin-placed" to admins. | Checked |
| Server loading | The server keeps every zone of every active observer generated and its objects running. No wild creatures (except phantom spawns), no tames unless "Simulate tames" is on. Clients load nothing extra. | Checked |
| Ownership rule | Every 2 s the server takes objects in loaded zones that no player is near; vanilla hands them to nearby players. Objects owned by a far player are taken back; paused creatures held from afar are released. | Checked in single player; needs the dedi |
| Edge-support rule | Wear and support (and `StaticPhysics`, `SpawnArea`) only run where all 8 neighbouring zones are loaded. | Built |
| Unknown-prefab guard | Objects whose prefab the server doesn't know are skipped and logged, not deleted. | Built |
| Logging | Switches, zone changes, 5-minute summaries, warnings, distant claims, unknown prefabs, phantoms, the empty-server clock. | Checked |
| Phantom Spawner | Allowlisted creature spawners (default surtlings) and spawner areas act as if a player stood at an active phantom. | Built 5 Nov |
| Boat fix | Water readings from unloaded zones are replaced with the ship's centre reading while it runs on the server. | Built 5 Nov |
| Despawn protection | Items within 8 m of a server-run station or 40 m of an active phantom in loaded zones don't time out. | Built 5 Nov |
| Empty-server hour | With an observer on, the world clock keeps running in real time for up to 60 min after the last logout. | Built 5 Nov; dedicated server only |
| Limits and admin-only building | Zones per player (18) and in total (108), admin exemption, expiry, admin-only building. | Built 5 Nov |
| Map | Admins can show every observer on the map (personal setting); nobody else receives them. | Built 5 Nov |
| Defensive patches | `Pickable.RPC_Pick` and `Ship.UpdateSailSize` no longer fail when the server owns the object. | Built 5 Nov; needs the dedi |
| Release | Regenerate "Tested with" in `Package/README.md` from the live profile after the rollout; replace `docs/images/observer-card.png` with new build-card screenshots (the single observer's description changed, and the cluster has its own); date the changelog; publish 1.0.0. | To do |

**Next:** a short single-player check of the new parts (see [Test plan](#test-plan-for-the-real-mod)), then
publish and roll out. Several parts only show on a dedicated server (ownership between players, the empty-server
hour, the defensive patches); those are checked during the rollout.

## Goals

1. **Keep a base's production running** while its owner is elsewhere in the world, without breaking anything when they come back.
2. **Ownership always goes back to a player** who is near an object. The server holds an object only while no player is near it.
3. **Boats stay afloat** in loaded areas.
4. **Surtling farms keep spawning** without a player present.
5. **Up to one real hour of offline production** after the last player logs off.
6. **Low overhead** and clean coexistence with large modpacks.
7. **No player's game ever runs a loaded base it isn't near.** Only the server keeps a base running while nobody is there, so a player's client-side mods can never act on a base they've left.

Out of scope for v1:
- Changing conversion capacity or speed. Leave that to other mods.
- A server-side collector that refuels smelters and empties output while nobody is near. It's a possible later feature.

## Why the old approach fails

All three old mods postfix `ZNetScene.InActiveArea` to return `true` for any point in a loaded ("forced") zone, whatever reference point is being asked about. That single answer feeds several unrelated systems.

- **The server hoards ownership.** `ZDOMan.ReleaseNearbyZDOS` runs every 2 s for the server and for each player. A player takes an object only if its current owner is out of range, and the patched check always says the server is in range. So objects in a loaded zone stay with the server even while a player stands next to them. The only exception is a chest a player opens, which moves explicitly. Logs: across a week there were 57 handovers to players, every one of them an opened chest or a touched tame, and none happened through proximity. Separately, 1,873 objects went to the server across 31 logouts.
- **Players who leave keep ownership.** The same check tells the game that every loaded object is in range of every player. A player who visits a base and walks away keeps its objects until they log out. One logout moved 271 objects at five different bases to the server. zeeCamelSnake also patches `IsInPeerActiveArea`, which makes this worse.
- **Whoever switches a loader on takes the whole base.** The player standing there when it's switched on owns everything in its zones from then on, even if it isn't their base. On 1 Nov 2026 one player switched on the loader at another player's farm. The diagnostic showed all of its roughly 130 tames, plus its containers and stations, owned by that player for the next 75 minutes while they explored 4–5 km away.
- **Distant players run the base on their own machine.** The patched check also runs on clients, so a player keeps the loaded base's objects instantiated and simulated on their PC wherever they are. Their client-side mods then act on a base they aren't near. On 1 Nov, when that player died far away, a tame mod on their PC moved about 200 tames from two loaded bases to the death spot in one second. See the QuanCompanions feature list (`QuanCompanions/docs/feature-list.md`) for the tame mod's side of this.
- **Commands to a base's creatures get lost.** Vanilla sends a creature command (E to follow or stay) to the PC that owns the creature, and only that PC can change it. When the owner is a player far away, their PC can't find the requesting player, so the command is quietly ignored. On 1 Nov, players in the farm couldn't reliably make their own tames stay.
- **Mods that need a player owner stop.** AzuAutoStore and OttoFuel only act on objects the local player owns, so with the server as owner they never run. Smelting chains stall even with a player in the base.
- **Vanilla code that assumes the owner has a player** throws errors when the server is the owner:
  - `Pickable.RPC_Pick` calls `Player.m_localPlayer.GetZDOID()`, so picking fails silently. One harvest logged about 120 of these errors.
  - `Ship.UpdateSailSize` fails the same way. The exception escapes `MonoUpdatersExtra.CustomFixedUpdate` and aborts the rest of that physics tick for fish, AI, characters and area effects. 1.0.21 narrowed this: the local-player lookup now runs only while someone is at the ship's controls. It still runs on every machine that has the ship loaded, including the server, so a player changing sail inside a loaded zone still throws on the server.
- **Plants never regrow.** Only the owner's copy of a pickable runs its regrow check (`Pickable.UpdateRespawn`, every 60 s). If the owner is a player who has left, nobody runs it. PlantEverything's hover then shows "Ready" while the plant stays bare, until someone switches the loader off and reloads the area.
- **Boats sink at the edge of a loaded area.** `Ship.CustomFixedUpdate` averages five water samples: centre, bow, stern, left and right. A sample in a zone the server hasn't loaded returns -10000, so the average collapses and buoyancy switches off entirely. A server-owned boat moored at the edge of a loaded area then rests on the seabed. The diagnostic confirmed this in about 40,000 observations with no counter-examples. Every sinking had a sample in an unloaded zone, and every boat with all five samples loaded floated.
- **The map lags.** The old mods remove and re-add every loader pin every 4 s and scan all ZDOs on the client. Opening the large map stalls for a second or two, which also blocks TargetPortal's portal selection.

Two more facts limit what any chunk loader can do:

- **The world clock stops on an empty server.** `ZNet.UpdateNetTime` only advances world time while at least one player is connected. Smelters, kilns, fermenters, beehives, crops and plant regrowth all measure progress against that clock, so nothing progresses with nobody online. The server program itself keeps running, and loaded zones stay loaded.
- **Auto-fuel and auto-store mods run on players' machines.** They need a local player, and a dedicated server has none. So with no player near a base, nothing refuels a smelter or collects its output.

## Pieces

**Observer model (decided 2026-10-25):** Frogger's original `ChunkLoader_stone` model, as a tribute. It comes from the `chunkloader` asset bundle in [JFHeim/ChunkLoader](https://github.com/JFHeim/ChunkLoader) (`src/assets/chunkloader`, MIT).
- **Scale: 50% of the original size (Quandru, 5 Nov 2026),** about 1.25 m tall, chest height. The column mesh is
  2.47 m tall at full size; Frogger's mod never rescales it, so old loaders on the server are 2.47 m. The design
  first said 75% (about 1.85 m, player height); Quandru saw that in game next to the old mod and chose 50%.
- **Rune colour (Quandru, 5 Nov 2026):** red runes while off, blue and glowing while on, as Frogger's original. His
  mod set `_EmissionColor` to `Color.red * 6` when off and back to the material's own (2, 2, 2) when on; ours does
  the same on each observer's own material copy. The light comes on only while on. The bundle's `sparcs` particles
  don't visibly show; Quandru doesn't need them.
- Ship the bundle as an embedded resource. **Found when building (4 Nov 2026):** the bundle's `ChunkLoader_stone` prefab carries only vanilla scripts (`ZNetView`, `Piece`, `WearNTear`, `LightLod`); the old mod adds its `ChunkLoaderMono` in code. So both observers are clones of that prefab, renamed and set up by our code, with no missing-script warnings. Built with Unity 6000.0.72; the game runs 6000.0.75.
- **Dedication on the build card (Quandru, 4 Nov 2026):** each observer's description ends with a short line dedicating the model to Frogger: "Dedicated to Frogger, who first brought chunk loading to Valheim. This is his original chunk loader model, used as a tribute." Seen in game on 5 Nov 2026 ([docs/images/observer-card.png](images/observer-card.png); the description above it has since changed to "Keeps its own zone loaded"):

  <img src="images/observer-card.png" alt="The Chunk Observer's build card in game, with the dedication to Frogger and a cost of 10 Stone" width="480">
- MIT requires the copyright and licence notice in copies, so `THIRD-PARTY-NOTICES.md` is in the repo root and in `Package/` (which ships in the Thunderstore zip).
- The bundle can't be loaded twice, so it would clash with the old mod if both were installed. The incompatibility check already prevents that.

All three pieces go in the **QuanPieces** hammer tab and need no crafting station. Costs are fixed (no setting). None
carries a PlayerBase `EffectArea`, so none suppresses spawners or counts as a base.

### Quan's Chunk Observer (`Quan_ChunkObserver`) and Chunk Observer Cluster (`Quan_ChunkObserverCluster`)

**Two pieces instead of an "Area size" setting (Quandru, 5 Nov 2026).** Players choose per observer by what they
build:
- **Chunk Observer:** the single column. Loads its own zone (1×1, 64 × 64 m). 10 Stone.
- **Chunk Observer Cluster:** three columns. Loads its own zone and the 8 around it (3×3, 192 × 192 m). 30 Stone.
  - **Look (Quandru, 6 Nov 2026):** three of the columns, 120 degrees apart, each with a flat (rune) face pointing
    straight out, as close together as they can stand so only narrow V-seams show between them, on a shared round
    two-step base under a shared round capital whose lips only just cover them. (The first build, 5 Nov, had a
    corner of each pointing out; Quandru asked for flats.)
  - **How it's built** (`ClusterModel`): the column mesh is readable, so the shaft alone is cut out of it at load
    (triangles between 0.30 and 2.20 m of the 2.47 m column; the base and capital are dropped). Square columns
    120 degrees apart with flats out can only close up as a pinwheel: each is shifted sideways by its half-width
    (0.255 model units, before the 50% scale) and stands 0.255 out from the centre, so the three inner corners meet
    in the middle and the seams between them are 30-degree Vs, the narrowest possible. Their outer corners reach
    0.72; the lips above and below are 0.74. The round steps and capital are cylinders generated in code, textured
    with the plain stone patch of the column's own texture (the capital's top face), so they match. Each column's
    second rune face points into the middle.
  - **Icon:** rendered from the model with Jotunn's `RenderManager` (skipped on a dedicated server, which has no
    graphics); the single column's icon is the fallback.

Both observers:
- **No fuel.** It runs until switched off from its hover menu.
- **Only its builder or an admin can switch it** (server setting, on by default). Other players see who built it and can't change it. With the setting off, switching falls back to wards (`PrivateArea.CheckAccess`).
- **Its glow updates immediately for everyone nearby** when switched. The old mod took 2-3 s.
- **Hover text** shows on/off, the size it loads, and the builder's name. **Admins only** also see "Admin-placed" on observers an admin built. Other players never see it. The flag is stored on the observer (it has to survive restarts); it isn't secret data, just not displayed to non-admins.
- **It can't be destroyed and monsters ignore it.** It isn't a target for AI (`StaticTarget`, `Piece.m_targetNonPlayerBuilt`), and a prefix on `WearNTear.RPC_Damage` blocks damage. Removing it with the hammer still works.
- **Overlap is allowed.** Loaded zones form a set, so overlapping observers cost nothing extra. That lets a player add a single observer at a dock on the edge of a cluster's area.
- **Edge rule and the two sizes:** wear, support and spawner areas only run where all 8 neighbours are loaded, so a
  cluster's centre zone gets full vanilla wear and a single observer's zone never does. That's safe (edge pieces
  just don't weather); it also means spawner areas only run in a cluster's centre zone.

### Quan's Phantom Spawner (`Quan_PhantomSpawner`)

- **Look (placeholder, 2 Nov 2026):** the vanilla ward (`guard_stone`) model scaled to 25%, with red glowing copies of its materials and a small red light. Built from the ward's look only: its particle effects, lights, sounds, trigger volumes and every script other than `ZNetView`, `Piece` and `WearNTear` are removed (`PrivateArea`, its area marker and any `EffectArea` among them), and loading stops with an error if a `PrivateArea` or `EffectArea` were still there. What was removed is logged at Debug.
- **Makes nearby spawners behave as if a player were standing there.** Surtling spawners are `CreatureSpawner`s (`Spawner_imp_respawn`: 60 m trigger, 5-minute respawn on world time). Support is limited to a config allowlist, default `Spawner_imp_respawn`. `SpawnArea` support is also included, on by default.
- **Active only inside a zone that an active observer keeps loaded,** and only if it wasn't built while only admins could build. Outside one, its hover text says "Dormant: no active chunk observer" so players know why. The server decides this, because non-admins don't receive the observer list. **As built (5 Nov 2026):** the server writes "active" onto the phantom whenever the loaded zones change, instead of answering a hover RPC; every player's hover reads it with no extra traffic.
- **No fuel or timer, no map pin, can't be destroyed** (same as the observers). There's no per-zone limit.
- **Not built: the placement ring.** The design wanted the workbench's dotted ring while placing; the ward's own area marker is removed with the rest of the ward. A possible later addition.
- **How it works:**
  - A prefix and finalizer on the spawner update (`CreatureSpawner.UpdateSpawner`, `SpawnArea.UpdateSpawn`) sets a scoped "phantom context" flag on the server, for allowed spawners only. While it's set, a postfix on `Player.IsPlayerInRange` treats an active phantom as a player at its own position, within the caller's own `range`. That covers both the plain and the noise overloads.
  - Nothing global changes and no transpilers are involved. `IsPlayerInRange` contains a `foreach`, so it won't be inlined.
  - **Phantom spawns run as normal:** a creature made while the phantom passed the check is marked on its ZDO (`QCL_phantom`, from a postfix on `Character.Awake` inside the context). Marked wild creatures load and run on the server in loaded zones; other wild creatures stay paused.
- **Respawning follows world time,** because the `CreatureSpawner` respawn delay uses it. A farm spawns while anyone is online and during the empty-server hour, and stops after that.
- **Farm behaviour:** surtlings that spawn into water die at once, so the spawner's caps never bind. Collecting the drops still needs a player nearby, because auto-store runs on players' machines. Despawn protection (below) keeps the drops in the meantime.

## How loading and ownership work

All of this runs on the server. Clients load nothing extra.

1. **Forced set.** This is the union of the zones of all active observers. It's rebuilt from every active observer whenever one is placed, removed or switched, never by subtracting a removed observer's zones, because another observer may still cover them. There's no periodic per-observer work.
2. **Loading.** The server keeps every forced zone generated and its objects instantiated, **except tames** (see [Tames](#tames)). The old `InActiveArea` postfix is replaced by narrow patches that apply only to the server's own scene and ownership decisions.
3. **Edges: no extra zones loaded.** Nothing outside the forced set is loaded, not even terrain. Two rules make the edge safe instead:
   - **Boats:** a water sample in a zone that isn't loaded is left out of the ship's average (see [Boats at the edge](#boats-at-the-edge)).
   - **Buildings:** wear and support only run on pieces whose neighbouring zones are all loaded, as vanilla does (see [Safety rules](#safety-rules)).
4. **Ownership rule.**
   - The server claims an object in a forced zone only when no connected player's vanilla, unpatched active area covers it. It gives the object up as soon as one does, and vanilla `ReleaseNearbyZDOS` hands it to that player within one 2 s pass.
   - Observers only add zones to the set. They're never compared with player positions, so overlapping or duplicate observers can't change ownership.
   - We never patch the range checks as seen by players. That's what stops departed players keeping ownership.
   - **Who switched an observer on doesn't matter.** Switching only changes the forced set. It never gives the switcher ownership of anything.
   - **Distant claims don't stick.** Some mods take ownership of a creature or object directly (`ZNetView.ClaimOwnership`). If a player far from a forced zone does that, the server's 2 s pass sees the new owner out of their vanilla range and takes the object back. So a stray claim lasts at most one pass and can't turn into a base run from far away.
   - **Clients never load forced zones.** A client only creates and simulates objects in its own vanilla active area. Forced zones exist only on the server (see goal 7).
   - **Paused creatures held from afar are released (decided while building, 4 Nov 2026).** The server never claims a tame or wild creature it doesn't run. But if one in a forced zone is owned by a player who isn't near it (they teleported away, or a mod claimed it), the server sets its owner to nobody, as vanilla does when a player walks away. No distant client keeps a base's tames, and nobody runs them until a player comes near.
5. **Observer list.**
   - The server holds the authoritative observer list: position, on/off, builder, admin flag. Clients don't receive observers' data from across the world.
   - **Who gets what.** The server knows who is an admin (the world's admin list); clients don't, so it tells each client.
     - **Admins** get the full list: a snapshot on connect, then small add/change/remove updates. A client replaces its list on each snapshot, so a missed update can't leave a stale pin. It can also ask for a fresh snapshot, for example when opening the large map.
     - **Other players** get only their own observers and the server-wide total, which is enough for the limit pre-check. They never receive anyone else's positions.
   - If a player's admin status changes while they're online, the server sends the right set on their next snapshot request or reconnect.

## Wild creatures

**Decision (2 Nov 2026): wild creatures in forced zones aren't loaded.** As with tames, the server doesn't create
saved wild creatures in forced zones: no monsters wandering or attacking an empty base, and less work for the server.
They stay exactly where they were and load normally when a player comes near.
- The check reads the ZDO's prefab: anything with a `Character` that isn't tamed and isn't a player.
- **Exception:** creatures spawned while the zone is loaded by a spawner the phantom keeps running (the phantom's
  allowlist, default surtlings) run as normal, so surtling farms work. They usually die in the farm's water at once.

## Tames

**Decision (2026-11-01): the loader ignores tames.** A tamed creature in a forced zone is left exactly as vanilla leaves it in an unloaded zone. The server doesn't instantiate it, doesn't run its AI and never claims it. It's paused until a player comes near, and then vanilla instantiates it and hands it to that player as usual.

**Why.** On 1 Nov 2026 the old loader let one player's client own and instantiate about 200 tames at two bases 4–5 km away. That player died, and in the same second every one of those tames jumped to the death spot. The most likely cause is a BetterTames defect: its name-based follower match also matches tames with no leader when the player name is blank. We've reported it to the BetterTames author. Whatever the trigger, the old loader made it possible by giving a distant client both ownership and live instances. More generally:
- **Tame mods assume they act on nearby tames.** BetterTames and TeleportCreatures run on clients and act on `Character.GetAllCharacters()` or on ZDOs the client owns. Anything that lets a client hold distant tames turns a local bug into a world-wide one.
- **Server-run tame AI adds nothing players need.** A loaded pen would breed, get hungry and wander under the server, and tame mods' feeding and breeding code would run on the server. Breeding limits are local (BetterTames crowd limit, 10 m), so an unattended pen could keep growing.
- **It stops whole classes of problem:** tames pushed out of pens with nobody watching, server-side AI cost, and the server holding tames that a player then can't command. Vanilla routes `Tameable.RPC_Command` to the owner, and only the owner can clear `follow`. That's why our E presses were ignored on the 1st.

**How.**
- A prefix on the server's object creation for forced zones skips any ZDO whose `tamed` flag is set (`ZDOVars.s_tamed`). It reads the ZDO directly, so it doesn't need a prefab lookup or an instance.
- Instances in forced zones go through the same check every 2 s, so a creature tamed while the zone is loaded is dropped. Taming needs a player nearby, so ownership has already moved to them.
- Only the server's own creation decisions change. Players' vanilla active areas are untouched, so a player in the base sees and owns their tames normally.
- Players' clients never load forced zones (see [How loading and ownership work](#how-loading-and-ownership-work)). That's the other half of the protection: no client ever holds distant tames.

**What players will notice.** Tames in a loaded base don't eat, breed or regain health while nobody is near, the same as vanilla without a loader. **Growing up is different: it catches up.** A young tame grows up once enough world time has passed since it was born (`Growup.GrowUpdate` checks time since spawn on the world clock), so young left in an empty base grow up as soon as anyone comes near, including time from the empty-server hour. **Breeding doesn't catch up:** love points only build while a tame is running, every 10 s, with a partner near and not hungry (`Procreation.Procreate`). Offline breeding therefore needs the setting below. The README says so under Known limits.

**Config:** "Simulate tames in loaded zones", **off** by default, server-synced and admin-only. Turning it on restores server-run tames under the normal ownership rule, for servers that really want unattended breeding. The README warns about the risks above.

**Decision (2 Nov 2026): ship it off, run Quanheim with it on.** Other servers opt in knowingly; we turn it on in our
config for offline breeding. Breeding is vanilla's `Procreation`, not a mod feature: every 10 s a tamed adult that
isn't hungry or alarmed rolls a chance, checks the crowd limit (`m_maxCreatures` of its kind, adults and young, within
`m_totalCheckRange`) and a partner within `m_partnerCheckRange`, gains a love point, and becomes pregnant at
`m_requiredLovePoints`. BetterTames only tunes those numbers and shows them on hover. It all needs the tame running.

## Logging

The diagnostic plugin retires at 1.0, so everything below is built into the mod. On 1 Nov 2026 the only reason we could reconstruct the incident was the diagnostic plugin's log: who switched which loader on and when, who owned what, and the exact second objects left the loaded zones. The real mod keeps the useful part of that at info level on the server, without the diagnostic's volume:
- **Every observer switch:** position, on or off, who switched it, and the builder.
- **Forced set changes:** zones added and removed, with the total.
- **Ownership summaries for forced zones,** every 5 minutes: object counts by owner (server or player), as the diagnostic's beat line does.
- **Large changes at once:** a warning when 20 or more objects leave forced zones, or change owner, within one 2 s pass, with counts by category (tames, containers and so on) and the players involved. That is exactly what 1 Nov looked like.
  - **As built (4 Nov 2026):** ordinary hand-overs aren't warnings, because a player walking into a base takes hundreds of objects at once. The warning covers objects *leaving* the loaded zones while the zones themselves didn't change, and 20 or more objects taken back from players far from them. Hand-overs to nearby players and objects players walked away from are Debug lines, and show in the 5-minute summary's owner counts.
- **Distant claims taken back** (see the ownership rule), counted per player, so a misbehaving mod shows up.
- **Log detail is a server setting**, default **Info**, so the log stays small and respectful of disk space:
  - **Info:** only the lines above: switches, forced set changes, 5-minute summaries, warnings for large changes,
    distant claims taken back, unknown prefabs skipped. A handful of lines per minute at most, and nothing every frame.
  - **Debug:** adds per-object detail for ownership changes and loads.
  - **Trace:** adds every ownership decision and scene pass, for chasing a specific bug, then switching back.
- Everything goes to the normal BepInEx log. Writing a separate daily log file (as the diagnostic did, up to about
  10 MB a day) is an option, off by default, with a setting for how many days of files to keep.

Timestamps include the UTC offset, as the diagnostic does, so they can be matched against client `Player.log` files.

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

**Implementation:** a server-side component adds the real elapsed time (`Time.unscaledDeltaTime`) to the network time while the player count is 0 and the window is open. That avoids patching the small, inlinable `UpdateNetTime`. **As built (5 Nov 2026):** `EmptyServer`, dedicated servers only (a local game ends when its host leaves). The window opens when the player count drops to 0 with at least one zone loaded, and closes when it runs out, a player joins, or the last observer is switched off. Opening and closing are logged with the world time added.

**Limits to explain in the README:**
- Only what's already loaded gets processed, because auto-fuel and auto-store need a player. A smelter holding 10 ore at 30 s per bar runs dry after 5 minutes, so higher-capacity stations get far more from the hour.
- Windmill speed comes from the wind as the server sees it, worked out at the server's reference position rather than the windmill's location, so offline output varies. The v3 diagnostic records wind on an empty server.

## Despawn protection (on by default)

Dropped items are destroyed after 3,600 s of world time unless one of these applies:
- they're inside a PlayerBase area (and above y 28);
- a player is within 25 m;
- they're in tar.

Offline output would therefore start vanishing shortly after the next login. Two protections fix this, on the
server, inside forced zones:

- **Near conversion stations:** drops near any conversion station's output point don't time out.
- **Near phantoms:** drops near an active phantom don't time out.

**As built (5 Nov 2026):** a prefix on `ItemDrop.TimedDestruction` skips the check for an item in a forced zone within
8 m of a server-run station (smelters and kilns, fermenters, beehives, cooking stations, sap collectors; positions
refreshed each 2 s ownership pass) or within 40 m of an active phantom. Simpler than a scoped flag around the player
check, with the same effect: the item is kept.

Neither adds a PlayerBase area, so neither affects spawners.

## Boats at the edge

`Ship.CustomFixedUpdate` averages five water samples (centre, bow, stern, port, starboard) from
`Floating.GetWaterLevel`. A sample reads -10000 in two cases: the point is over **land**, or its zone **isn't
loaded**. With four samples at about 30 and the bow in an unloaded zone, the average is (4 x 30 - 10000) / 5, about
-1976, so buoyancy switches off and the boat settles on the seabed.

**Fix:** while a ship runs its physics on the server (a scoped flag set by a prefix and cleared by a finalizer on
`Ship.CustomFixedUpdate`), a postfix on `Floating.GetWaterLevel` replaces a -10000 reading **only when that point's
zone isn't loaded** (`ZoneSystem.IsZoneLoaded`), using the centre sample taken first in the same update. The average
and the per-corner forces then come out as if the water carried on. A -10000 over loaded land is real ("no water
here") and is left alone, so beached and docked ships behave exactly as in vanilla.

- No extra zones are loaded and no unvisited zones are generated.
- A ship that drifts out of the forced set entirely is unloaded by the game (`ZNetScene.RemoveObjects`): it stops
  simulating where it is rather than sinking, and loads normally when a player arrives.
- Runs on the server only. Players' own areas are vanilla.

## Safety rules

Nothing this mod does may permanently remove or damage anything. These rules hold throughout the code:

- **The mod never deletes an object itself.** No call removes a ZDO. Care is needed with unloading too: when the
  owner unloads a non-persistent object, the game deletes it (`ZNetScene.RemoveObjects`), so the mod never unloads
  anything it wouldn't be safe to lose, and tames are only ever skipped at creation, never destroyed.
- **Unknown objects are left alone.** When the server tries to create an object whose prefab it doesn't know, the
  game deletes it for good (`ZNetScene.CreateObjectsSorted`: "Destroyed invalid prefab ZDO"). A normal dedicated
  server rarely loads anything, but a chunk loader makes it load whole bases, so any object from a mod the server
  lacks, or doesn't register on the server, would be lost. That's how LetMeTameYou's fawns were deleted, and the old
  loaders carry the same exposure. In forced zones the server skips unknown prefabs and logs them instead.
- **Buildings at the edge keep full support.** Vanilla only runs building wear and support in the inner part of the
  loaded area, so a piece can always see the pieces holding it up (`WearNTear.UpdateWear` returns early when
  `ZNetScene.OutsideActiveArea` is true). Vanilla works by zone (64 x 64 m): around a player it loads the 5 x 5 zones
  centred on their zone and runs only the 3 x 3 in the middle (trimmed at the corners), keeping a one-zone margin of
  loaded neighbours (`ZNetScene.CreateDestroyObjects`, `ZNetScene.PointInsideActiveArea`). We mirror that margin.
  On the server, a point in a forced zone counts as "active" only when all
  eight neighbouring zones are forced too. Pieces at the edge of a loaded area then keep full support and don't
  weather, instead of checking their support without their neighbours and possibly collapsing. The same rule applies
  to crops dropping to the ground (`StaticPhysics`) and to `SpawnArea`, the other users of that check. With a 3x3
  observer the centre zone gets full vanilla wear. The phantom's `SpawnArea` support must allow for this.
- **Never claim an object that's in use** (an open chest, a station being used), and never claim near any player.
- **Everything is logged by this mod.** The diagnostic plugin retires at 1.0, so all the logging needed to spot an
  ownership or loading problem lives in the mod itself (see [Logging](#logging)).

## Defensive patches

Normally the ownership rule means these situations don't arise. The patches cover the roughly 2 s before ownership moves.

- `Pickable.RPC_Pick` on a server owner hands ownership to the player who asked and re-sends the RPC.
- `Ship.UpdateSailSize(float dt, bool haveControllingPlayer)` skips the local-player effect when there's no local player. The signature gained `haveControllingPlayer` in 1.0.21, so the patch must target the new one.

## Limits and admin controls

Config file layout (`quandru.QuanChunkLoader.cfg`), following the numbered-section convention in
`docs/DEVELOPMENT.md`. Enforced settings sync from the server through Jotunn and only admins can change them; personal ones are each player's own. Sections 0–6 and 8 are enforced; section 7 is personal and named so.
Settings are listed in the order they're bound.

| Section | Setting | Default | Enforced or personal |
|---|---|---|---|
| `0 - General` | Only admins can build observers and phantoms | Off | Enforced |
| | Only the builder or an admin can switch an observer | On (off: wards decide) | Enforced |
| `1 - Limits` | Zones per player | 18 (two clusters) | Enforced |
| | Zones in total | 108 (twelve clusters) | Enforced |
| | Admins ignore limits | On | Enforced |
| | Expire after hours without a refresh | 0 (never) | Enforced |
| `2 - Loading` | Simulate tames in loaded zones | Off (Quanheim runs it on) | Enforced |
| `3 - Empty server` | Empty-server window in minutes | 60 (0 = off) | Enforced |
| `4 - Phantom spawner` | Creature spawners | `Spawner_imp_respawn` | Enforced |
| | Spawner areas | On | Enforced |
| `5 - Despawn protection` | Despawn protection | On | Enforced |
| `6 - Map (personal)` | Show observers on the map | Off (admins only) | Personal |
| `7 - Logging` | Log detail | Info (Info, Debug or Trace) | Enforced |
| | Write a daily log file | Off | Enforced |
| | Days of daily files to keep | 14 | Enforced |

`7 - Logging` is a server setting too (the server is what logs); it sits last so the everyday sections come first.
**Gone (5 Nov 2026):** "Area size" (replaced by the two observer pieces) and the whole `6 - Pieces` section with
"Stone cost" (costs are fixed: 10 Stone, 30 for the cluster, 10 for the phantom). The sections were renumbered to
close the gap; nothing was released under the old names.

- **Limits count zones, not observers (decided while building, 5 Nov 2026).** With two sizes, "observers per
  player" no longer measures load: a cluster loads nine times what a single one does. So each switched-on observer
  counts the zones it loads (1 or 9), per builder and in total. Overlap isn't subtracted, so the count is simple and
  predictable. The defaults keep the old numbers' meaning: 18 per player is two clusters, 108 in total is twelve.
- **Checked when switching on, not when building (decided while building).** Observers are built switched off and
  load nothing until switched on, so the server checks the limits in the switch request, where it's authoritative
  and there's no race to recover from. Building is never refused for limits. Lowering a limit later doesn't switch
  anything off.
- **Admin placement.** Each observer stores its builder and an admin flag, set when it's built and checked against the server's admin list. With "Admins ignore limits" on, admin-placed observers count towards neither limit, and admins can switch any observer on regardless; losing admin status later changes nothing.
- **Refusal messages** say which limit was hit and by how much:
  - Per player: "That would load N zones for <builder>; the limit is M per player. Switch off or remove another of their observers first."
  - Server total: "This server's observers would load N zones; the limit is M. Ask an admin to raise it or switch off unused observers."
- **Admin-only building (off by default).** When on, all three pieces are hidden from non-admins' hammer menus (`Piece.m_enabled`). If a non-admin's piece gets through anyway (a race, or a modified client), the server marks it blocked and messages the builder: a blocked observer can only be switched on by an admin, and a blocked phantom stays dormant. Pieces that already exist are untouched. Useful for a controlled trial on a live server.
- **Expiry** counts real hours, because the world clock stops on an empty server. Switching an observer on restarts its countdown (stored on the observer as real UTC time). Turning expiry on later (or at a server start with it on) starts every switched-on observer's countdown from that moment, so nothing expires instantly. An expired observer is switched off, never deleted, and the server checks once a minute.

Client config:
- **Show observers on the map (admins only, personal):** off by default. When on, every observer in the world is shown, with Frogger's active and inactive icons and a label of its size and builder. **As built:** a config setting only; the toggle on the large map wasn't built. An admin's game asks the server once per session, and the server sends the full list again whenever an observer is built, switched or removed; non-admins are refused and never receive it.
- **Pins persist.** Pins are rebuilt only when a list arrives. There are no timers while the map is open and no client-side ZDO scans.

## Compatibility

- **Hard dependency:** Jotunn (in the manifest too). Settings sync through Jotunn's config sync (`IsAdminOnly`), not ServerSync (see `docs/DEVELOPMENT.md`).
- **Incompatible:** the three old chunk loaders, declared with `BepInIncompatibility`. Two GUIDs cover all three: `com.Frogger.ChunkLoader` (Frogger's original, and NerdLounge's ChunkLoader Continued 1.8.0, which kept that GUID) and `com.zeecamelsnake.ChunkLoaderFix` (zeeCamelSnake 1.0.4; checked from the Thunderstore DLLs on 2026-10-25). Running alongside one would bring back the ownership bug. If BepInEx doesn't show the refusal clearly, the mod detects the old loader itself, logs a clear error and stays inactive.
- **No soft dependencies.** Nothing needs a particular load order. The hammer tab name is a fixed constant, `QuanPieces`, matching the QuanPieces mod, so if both are installed their pieces share one tab. That needs only the same name, not a dependency.
- **Patch order.** Before release, check every installed mod for patches on the methods we touch, and set Harmony priority or before/after per patch as needed. The methods are listed in `docs/valheim-dependencies.md` ("Harmony patches"): `ZNetScene` object removal and creation, `ZNetScene.PointInsideActiveArea` with its three callers, `ItemDrop.TimedDestruction`, `Player.IsPlayerInRange`, `CreatureSpawner.UpdateSpawner`, `SpawnArea.UpdateSpawn`, `Character.Awake`, `Pickable.RPC_Pick`, `Ship.UpdateSailSize`, `Ship.CustomFixedUpdate` with `Floating.GetWaterLevel`, `WearNTear.RPC_Damage` and `Game.Start`. The full list, with what each relies on, belongs in the mod's Valheim Code Dependency doc.
- **Known interactions, none needing code:**
  - **AzuAutoStore and OttoFuel** start working inside loaded bases again, because players get ownership.
  - **PlantEverything** hover timers become accurate.
  - **Seed Totem** plants and harvests normally while a player is near, because that player owns the totem. With nobody near it doesn't plant, the same as auto-fuel and auto-store (see Still open for why).
  - **Conversion Size & Speed** and similar mods complement the empty-server hour.
  - **TargetPortal's** map selection is no longer blocked.
  - **BetterTames and TeleportCreatures** only ever see tames near a player, because neither the server nor a distant client keeps tames loaded (see [Tames](#tames)).

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
   - Tames in a loaded base ignore E, or get moved by another player's mods: whoever switched the loader on keeps running the base from wherever they are.
3. **How chunk loading really works, and what every chunk loader gets wrong:** explained as how the game works, not as criticism of anyone's mod.
   - "Loaded" doesn't mean "owned by a player".
   - Telling the game every loaded point is in range of everyone makes the server and departed players hoard objects.
   - Some vanilla code assumes the owner is a player.
   - A boat needs every water sample point loaded.
4. **What no chunk loader can ever do:**
   - The world clock stops when nobody is online, so nothing runs all night.
   - Auto-fuel and auto-store need a player nearby, so offline production only uses what's already loaded.
   - What this mod does instead: up to an hour of real-time production after the last logout.
5. **Features:** the three pieces with costs and how to use them; ownership handback; boats; the phantom spawner; the empty-server hour; despawn protection; map and admin controls; the defensive fixes.
6. **Dedication and credit (highlighted):**
   - Frogger had the original idea of a Valheim chunk loader.
   - As a tribute, the Chunk Observer uses Frogger's original chunk loader model, under the MIT licence (Copyright (c) 2023 Vyacheslav, [JFHeim/ChunkLoader](https://github.com/JFHeim/ChunkLoader)).
   - The model is the only thing reused: no code comes from Frogger's mod or any other current chunk loader mod.
7. **Configuration reference.**
8. **Recommended companions,** described by type only, not by name: conversion capacity and speed mods, auto-fuel, auto-store, with why each matters. Readers who want our picks can see them in "Tested with".
9. **Tested with:** every mod and version from the live server's plugin list.
10. **Migrating from an old chunk loader.**
11. **Known limits:** windmill variability, one hour at most, and tames pause while nobody is near (with the config option to change that and its risks).

## Versioning

The first release is 1.0.0. Builds stay at 1.0.0 through development and testing. The tagged commit that goes to Thunderstore fixes what 1.0.0 is; later changes bump the version. It has to be on Thunderstore so every player gets it through the shared Gale profile.

## Test plan for the real mod

**Single-player check of the full build (before publishing).** In the "Chunkloader test" Gale profile, in a local
world, with Log detail on Info. What can only be seen on a dedicated server is left to the rollout below.

| # | Check | Expected |
|---|---|---|
| 1 | The hammer's QuanPieces tab | Chunk Observer (10 Stone), Chunk Observer Cluster (30 Stone, its own icon), Phantom Spawner (10 Stone). |
| 2 | Build a cluster | Three columns 120° apart, a flat rune face of each pointing out, close together with narrow V-seams between them, on one round base under one round capital whose lips just cover them; stone matches; no gaps. Red runes off, blue and lit on, on all three. |
| 3 | Switch the cluster on, then a single observer elsewhere | Log: "now 9 zone(s)", then 10. Hover shows "3x3" and "1x1". |
| 4 | Go far away, wait for a Summary, come back | Summary shows the zones and objects running on the server. |
| 5 | Limits: set "Zones per player" to 9 and "Admins ignore limits" off, then switch both on | The second is refused with the per-player message. Put the settings back. |
| 6 | Build a Phantom Spawner outside any loaded zone | Small red-glowing ward; hover "Dormant: no active chunk observer". It isn't a ward: no area ring, no access list. |
| 7 | Move it inside a switched-on observer's zone (or switch one on near it) | Hover changes to "Active"; log "Phantom spawner … is now active". |
| 8 | Surtling farm, if one is handy | With a phantom in a cluster's zone and you far away, surtlings keep spawning (log at Debug: "Phantom-run spawner made …"). |
| 9 | "Show observers on the map" on | Pins for every observer, lit or unlit, labelled with size and builder. Off removes them. |
| 10 | Hit the cluster and the phantom; remove both with the hammer | No damage; both come off; logged. |
| 11 | The log | No errors from the mod; Debug lines list what was stripped from the ward clone. |


**Tested on the live server.** There's no separate test server: this is a hobby, and the mod is a luxury the server
can run without. That's acceptable because of the risk assessment (2 Nov 2026):
- Almost every failure is fixed by removing the mod from the shared Gale profile.
- The ways to lose something permanently are designed out (see [Safety rules](#safety-rules)) or are small and
  local: a few items in a chest if an ownership bug slips through, or one boat.
- The mod never touches how the game saves, so corrupting the world is very unlikely.
- The old loader it replaces has already done more damage than these risks allow.

**Rollout:**
1. **Back up the world** just before the swap, and keep that backup for a couple of weeks.
2. **Swap** the old loader for ours on the server and every client at once (see
   [Migrating from an old chunk loader](#migrating-from-an-old-chunk-loader)).
3. **Start cautious:** "Only admins can build" on, empty-server window 0 (off). Admins place a few observers at bases
   that want them.
4. **Day one checks:** moored boats at the edge of a loaded area are afloat; a busy base's chests and smelters hold
   what they should; the server log shows no unknown-prefab skips, no large ownership swings and no errors.
5. **Then switch on one thing at a time:** the empty-server hour, phantoms, more observers, and finally building for
   everyone.
6. **Rollback:** remove the mod from the profile. Restore the backup only if the logs show something was actually
   lost.

The checks below are done on the live server as each feature is switched on.

1. **Ownership.**
   - Set up two overlapping observers.
   - A player stands in the overlap, then inside only one observer's area. Nearby objects should move to the player within about 2 s.
   - A player stands in a forced zone but out of range of a target object. That object should stay with the server until the player approaches.
   - After logout, everything should return to the server within about 2 s.
   - A player who walks away while staying online should lose ownership within about 2 s.
   - Player A switches on an observer at player B's base, then travels 1 km or more away. Nothing at that base should stay with A, and A's client should have none of its objects instantiated.
   - With A far away, use a mod or a console command to have A claim an object in the forced zone. The server should take it back within about 2 s and log it.
   - With A far away, player B stands in the base and presses E on tames to follow and stay. Every command should work at once.
2. **Regrowth.**
   - Pick plants in a forced zone, walk out of range, wait past the regrow time and return. The plants should have regrown, under the server as owner.
   - Repeat with two players, where the one who picked leaves but stays online.
   - Pick plants planted with PlantEverything (thistles in particular) inside a forced zone and leave. They should regrow on time under the server. This checks the zone 7,-7 thistles that the v3 logs showed stuck.
3. **Boats.** Moor longships with the bow over the edge of a loaded area. They should float under server ownership. A ship pulled up on a beach inside a loaded area should sit on the sand as in vanilla.
4. **Edge buildings.** A building that crosses the edge of a loaded area should keep its support with nobody near: no damage, no collapse.
5. **Auto mods.** AzuAutoStore and OttoFuel should work in a loaded base with a player present.
6. **Phantom.**
   - A surtling farm with no player nearby should keep spawning while someone is online elsewhere.
   - The phantom should show the dormant message without an observer.
   - Drops near it should survive more than an hour of world time.
7. **Empty-server hour.**
   - Log out with loaded smelters. World time should advance for 60 real minutes, then stop.
   - Picked plants in the loaded base should regrow during the hour.
   - The output should still be there after the next login.
   - A 10-minute absence should gain exactly 10 minutes.
   - With no active observer there should be no window.
   - The world clock should never run faster than real time.
8. **Limits.** Test the per-player and server-total caps and their messages, admin placement, the server's over-limit safety net, and expiry turned on later.
9. **Map.** Toggle default off and visible only to admins. A non-admin has no toggle and receives no other players' observers (check the RPC traffic). An admin sees "Admin-placed" on hover; a non-admin doesn't. Pins should match the server list after offline changes. Open the map through a TargetPortal portal: portals should be clickable immediately.
10. **Toggle feedback.** The glow should change at once for every nearby player. Only the builder or an admin can switch it; another player's attempt is refused with a message.
11. **Old mod present.** Our mod should refuse clearly.
12. **Seed Totem.** In a loaded base with a player near, a totem with seeds queued plants them, and hitting it harvests ripe crops. Nobody near: nothing planted, no errors.
13. **Tames (the 1 Nov scenario).** At a base with tames in pens, with BetterTames installed.
    - Switch on an observer at a base with tames. The server log should show no tames instantiated or owned by the server in its zones.
    - A player switches it on, walks 1 km or more away, then dies and respawns. No tame at the base should move, change owner or change `follow`.
    - Do the same with Alt+H and a portal trip. Neither should touch the base's tames.
    - Walk back in. The tames should load normally and pass to that player within about 2 s. E (follow and stay) should work at once.
    - Tame a creature inside a forced zone with a player present, then leave. The server should drop it within about 2 s and never claim it.
    - Turn on "Simulate tames in loaded zones" and repeat the first check. Tames should be server-owned with no player near and handed over when one arrives.

## Still open

- **Two observer pieces instead of an area-size setting: decided and built (5 Nov 2026).** A single 1×1 observer
  and a 3×3 cluster of three columns; "Area size" is gone, and limits count zones (see [Pieces](#pieces) and
  [Limits and admin controls](#limits-and-admin-controls)).

- **Phantom Spawner placement ring:** not built (see [Pieces](#pieces)). Add later if wanted.
- **Map toggle on the large map:** not built; the map is a personal config setting. Add later if wanted.

- **Seed Totem 4.3.13: checked (2 Nov 2026), no extra testing needed beyond the rollout check.** Decompiled into
  `research/`. Its failures are the same "owner expected to be a local player" class, and the ownership rule fixes
  them:
  - **Planting** (`SeedTotem.DisperseSeeds`) only runs when the totem's owner is a PC with a local player
    (`m_nview.IsOwner() && Player.m_localPlayer != null`). Under the old loader the server kept ownership, so a totem
    in a loaded base never planted even with players standing next to it. With our rule a nearby player owns it and it
    plants. With nobody near, the server owns it and it doesn't plant: a limitation, like auto-fuel, not a bug. Making
    it plant on the server would mean patching Seed Totem, which is out of scope.
  - **Harvest on hit** runs on the hitting player's PC and calls `Pickable.Interact` on each crop, which sends
    `RPC_Pick` to the crop's owner. A server-owned crop then fails exactly as vanilla picking does; the ownership rule
    and our `Pickable.RPC_Pick` defensive patch cover it.
  - **Adding seeds, restricting and resizing** are RPCs handled by whoever owns the totem, with no local-player check,
    so they work whoever owns it.

- **Plant ownership on the live server: settled enough to build.** The v3 logs (25–31 Oct) show:
  - **The server regrows plants it owns and has loaded.** Batches of 17 to 64 regrew on time, none overdue.
  - **The old loader's plant bug is the walked-away case:** a player picks, walks away but stays online, and keeps ownership. It was seen only twice, owners 134–208 m away. The ownership rule fixes it.
  - **Most overdue plants had no owner:** wild dandelions, flint, branches and mushrooms outside the loaded zones. They regrew as soon as a player came near, even 150+ h late. That's vanilla behaviour, and we leave it alone.
  - **Plants found overdue when a player arrives** regrew within minutes of that player taking ownership. Not a bug.
  - **One anomaly:** three server-owned, loaded thistles beside Haudr's loader (zone 7,-7) stayed overdue for at least 1 h 45 min on 31 Oct, while the server regrew 14 other plants in the same 5-minute window. The cause is unknown and probably separate from ownership. Check them in game (are they PlantEverything plantings?) and watch for repeats under the new mod.
  - **Offline regrowth can't show up in these logs**, because the world clock stops with nobody online. Regrowth during the empty-server hour is tested with the new mod.
- **Real conversion station values and empty-server wind:** v3 logs both.
- **The diagnostic retires at 1.0.** Its logs up to the swap stay in `research/logs` as the reference for what the old loader did. Anything we still need to see after the swap is logged by the mod itself.
- **Game version: checked, nothing blocks the build.** The 1.0.20 to 1.0.21 diff (in the `.decompiles` repo) changes none of `ZNetScene`, `ZDOMan`, `ZNet`, `ZoneSystem`, `Pickable`, `CreatureSpawner`, `SpawnArea`, `Smelter` or `Fermenter`, nor `ItemDrop.TimedDestruction`, `Player.IsPlayerInRange` or `WearNTear.RPC_Damage`. Changes that touch us:
  - `Ship.UpdateSailSize` gained a parameter and now skips the local player when nobody steers. Our patch stays, smaller, on the new signature (see [Defensive patches](#defensive-patches)).
  - `WearNTear` caches its supporting pieces differently. Support isn't something we patch, and the edge rule in [Safety rules](#safety-rules) keeps edge pieces at full support.
  - `CookingStation` now records who put each item on, and credits them as crafter when it's taken off. It falls back to `Player.m_localPlayer` only for items put on before 1.0.21, which throws when the server owns the station. That's the same bug class as `Pickable.RPC_Pick`, already handled by the ownership rule (a player nearby takes ownership), so no new patch. Test: take cooked food off a station in a loaded zone, both with a player present and after the empty-server hour.
  - The earlier note that 1.0.21 moved saves to chunk files was wrong: chunked saves were already in 1.0.20. 1.0.21 only changed how the save list reloads, and the world version is unchanged.
