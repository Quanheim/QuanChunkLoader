# Changelog

## 1.2.0 - 2026-11-10

- **Eitr radiation pauses while nobody is near.** Dropped Refined Eitr and running eitr refineries fire radiation
  bolts (30 lightning + 30 poison, in random directions) on whichever machine runs them. In a loaded base with nobody
  there, the server ran them all night: on 9 Nov 2026 a pile of over 100 dropped Eitr wrecked much of a base. Now,
  in loaded zones with no player near, they hold their fire, as if the zone were unloaded. A player arriving brings
  back the vanilla sparks and damage. New section `8 - Unattended bases`, "Pause Eitr radiation while nobody is
  near", on by default. The 5-minute summary counts the bolts held back.
- **New section `9 - Known error suppression (personal)`**, with one switch so far, "Vanilla default model collider
  no read access" (off by default). On, it stops the repeated "RuntimeNavMeshBuilder: Source mesh default is skipped
  because it does not allow read access" lines. They come from about 800 vanilla models (cart wheels, ship hulls,
  logs, stones) the navmesh build can't read and skips anyway; a loaded base rebuilds its navmesh far more often, so
  it logs far more of them. This leaves those models out before the build, so creatures path exactly as before.
- **The log says what it knows, once per session**, for whoever reads it (you or an AI): when that warning appears
  with the switch off, a line names it as harmless and points to the switch; with the switch on, a line says the
  warnings are being suppressed and by which setting.
- Both are applied on their own: if a game update breaks either, the log says so and only that feature turns off.

## 1.1.3 - 2026-11-08

- Pressing Shift while typing (in chat, the console, a sign or a menu) no longer shows the zone grid of the observer
  under your cursor.
- The "had to be created again" warning no longer counts things that move (creatures, players, birds, fish, items,
  effects). They cross the edge of the loaded area and come back as normal, so the warning was a false alarm with a
  player at the base. It now counts only things that stay put, such as buildings, stations, containers and plants.

## 1.1.2 - 2026-11-08

- Readme and changelog only: 1.1.1 went out with its changelog still marked "unreleased" and the readme not
  mentioning the Debug log checks. No change to the mod itself.

## 1.1.1 - 2026-11-08

- "Show observers on the map" no longer turns the map's dot-pin filter back on. Observer pins now have no filter
  of their own, like the map's location icons.
- **Debug log checks.** With "Log detail" on Debug, the server also logs a tame watch (each tame in a loaded zone that
  moves 10 m or more, jumps, or changes who runs it, with its name and position), owner changes grouped, players
  coming and going, ships' buoyancy, picked plants overdue to regrow, every station's capacity at start, and a
  heartbeat with each summary. These are the checks of the old diagnostic plugin, now built in. The per-object lines
  for loading and taking objects moved from Debug to Trace, so Debug doesn't flood the log when players come and go.
- The "had to be created again" warning now names the objects it counted most.

## 1.1.0 - 2026-11-07

- The daily log file (off by default) moved to `BepInEx/QuanChunkLoaderLogs`.
- The zone grid now stays up for 2 minutes instead of 15 seconds, and the hover says so.
- **Zone grid shows other loaded zones nearby in yellow**, so overlaps and gaps between observers show. The server
  sends only the zone squares, not who built what.
- **Works with SteadyFrame's Object streaming.** Its "Unload by zone" unloaded everything in loaded zones on a
  dedicated server, so bases didn't really run while nobody was there. Loaded zones are now left alone. The server
  log also warns if anything else keeps unloading them.
- **Overlapping observers count each zone once** towards the limits: two clusters one zone apart count 12 zones, not 18.
- Server log: creatures a nearby player is running no longer count as paused in the 5-minute summary, and the
  "claimed by players far from them" warning no longer fires for a player who just flew past or for a zone just
  switched on: only for objects taken back again within a minute, as a mod claiming at a distance would cause.

## 1.0.0 - 2026-11-06

First release. A ground-up rewrite that replaces Frogger's ChunkLoader, ChunkLoader Continued and ChunkLoaderFix.

- **Chunk Observer** (one zone) and **Chunk Observer Cluster** (the 3 × 3 zones around it): keep their zones loaded
  on the server while switched on. They use Frogger's original model, dedicated to him. Only the builder or an
  admin can switch one, and either can show the zones it covers.
- **Phantom Spawner:** keeps surtling farms (and other spawners admins allow) running with nobody near, inside loaded
  zones. A reference list of every spawner in the game, other mods' included, is written next to the config.
- **Ownership goes to whoever is nearby**, and the server only runs what no player is near. Auto-fuel, auto-store,
  plant regrowth and creature commands work in loaded bases again, and nobody runs a base from far away.
- **Production after the last player logs off**, in real time: an hour by default, up to four.
- **Tames can keep breeding and growing** with nobody near, if the server turns it on.
- **Despawn protection** for drops near production stations and phantoms.
- **Boats at the edge of a loaded area stay afloat**, and **buildings at the edge keep their support.**
- **Wild creatures and spawners wait** while nobody is near.
- **Objects from mods the server doesn't have are skipped, not deleted.**
- Limits counted in zones (per player and in total), expiry, admin-only building, an admin map of observers, and
  server logging.
- Fixes for picking and sail changes on objects the server owns.
