# Changelog

## 1.1.0 - unreleased

- The daily log file (off by default) moved to `BepInEx/QuanChunkLoaderLogs`.
- The zone grid now stays up for 2 minutes instead of 15 seconds, and the hover says so.
- **Zone grid shows other loaded zones nearby in yellow**, so overlaps and gaps between observers show. The server
  sends only the zone squares, not who built what.
- **Works with SteadyFrame's Object streaming.** Its "Unload by zone" unloaded everything in loaded zones on a
  dedicated server, so bases didn't really run while nobody was there. Loaded zones are now left alone. The server
  log also warns if anything else keeps unloading them.
- **Overlapping observers count each zone once** towards the limits: two clusters one zone apart count 12 zones, not 18.
- Server log: creatures a nearby player is running no longer count as paused in the 5-minute summary, and objects
  seen for the first time (a zone just switched on) no longer raise the "claimed by players far from them" warning.

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
