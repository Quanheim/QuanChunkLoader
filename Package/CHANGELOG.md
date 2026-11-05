# Changelog

## 1.0.0 - YYYY-MM-DD

First release. A ground-up rewrite that replaces Frogger's ChunkLoader, ChunkLoader Continued and ChunkLoaderFix.

- **Chunk Observer** (one zone) and **Chunk Observer Cluster** (the 3 × 3 zones around it): keep their zones loaded
  on the server while switched on. They use Frogger's original model, dedicated to him. Only the builder or an
  admin can switch one.
- **Phantom Spawner:** keeps surtling farms (and other allowed spawners) running with nobody near, inside loaded
  zones.
- **Ownership goes to whoever is nearby**, and the server only runs what no player is near. Auto-fuel, auto-store,
  plant regrowth and creature commands work in loaded bases again, and nobody runs a base from far away.
- **Boats at the edge of a loaded area stay afloat.**
- **Buildings at the edge of a loaded area keep their support.**
- **Up to an hour of production after the last player logs off**, in real time.
- **Despawn protection** for drops near production stations and phantoms.
- **Wild creatures and tames wait** while nobody is near, by default.
- **Objects from mods the server doesn't have are skipped, not deleted.**
- Limits counted in zones (per player and in total), expiry, admin-only building, an admin map of observers, and
  server logging.
- Fixes for picking and sail changes on objects the server owns.
