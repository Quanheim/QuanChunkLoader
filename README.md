# Quan's Chunk Loader

A Valheim chunk loader for dedicated servers (BepInEx and Jotunn). It keeps bases loaded while players are
elsewhere, hands object ownership back to whoever is nearby, keeps moored boats afloat at the edge of loaded
areas, keeps surtling farms spawning, and keeps production going for up to an hour (by default) after the last player logs off.

- What it does and how to use it: [Package/README.md](Package/README.md) (the Thunderstore page).
- **Where we are and what's next: [docs/design.md](docs/design.md#build-status)** ("Build status" at the top).
- Building and releasing: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).
- Game code it relies on: [docs/valheim-dependencies.md](docs/valheim-dependencies.md).
- The read-only server diagnostic that verified the design: [docs/diagnostic-test-plan.md](docs/diagnostic-test-plan.md).

> **Dedicated to Frogger**, who had the original idea of a Valheim chunk loader. As a tribute, the Chunk Observers
> use Frogger's original chunk loader model ([JFHeim/ChunkLoader](https://github.com/JFHeim/ChunkLoader), MIT
> licence, see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)). The model is the only thing reused: this mod is a
> ground-up rewrite, and no code comes from Frogger's mod or any other current chunk loader.

## Status

In development. Everything planned for 1.0 is built (5 Nov 2026); the core has passed a single-player check and the
rest is waiting for one. What's been checked so far is in [docs/design.md](docs/design.md#build-status).

## Code

| File | What it does |
|---|---|
| [QuanChunkLoaderPlugin.cs](QuanChunkLoaderPlugin.cs) | Start-up: refuses to run next to an old chunk loader, applies the patches (all or nothing), adds the pieces. |
| [Settings.cs](Settings.cs) | The config file, in numbered sections. Every setting is the server's and admin-only, except the map. |
| [Log.cs](Log.cs) | Log lines with UTC-offset timestamps, the detail levels and the optional daily file. |
| [Core/Observers.cs](Core/Observers.cs) | The server's list of observers, the set of loaded ("forced") zones, switching, limits, expiry and admin-only building. |
| [Core/Phantoms.cs](Core/Phantoms.cs) | The server's list of Phantom Spawners and which are active. |
| [Core/EmptyServer.cs](Core/EmptyServer.cs) | The empty-server hour: keeps the world clock running after the last logout. |
| [Core/ObserverMap.cs](Core/ObserverMap.cs) | The admin map: the server sends admins the observer list; their game draws the pins. |
| [Core/ServerLoader.cs](Core/ServerLoader.cs) | Server only: loads forced zones and their objects, and runs the 2 s ownership pass and the summaries. |
| [Core/ObjectKinds.cs](Core/ObjectKinds.cs) | What the server loads (no wild creatures or tames by default) and what it may own. |
| [Core/GameAccess.cs](Core/GameAccess.cs) | Reflection access to the private game fields and methods used. |
| [Patches/ScenePatches.cs](Patches/ScenePatches.cs) | Keeps forced-zone objects loaded, skips unknown prefabs, and the edge-support rule. |
| [Patches/ObserverPatches.cs](Patches/ObserverPatches.cs) | Observers and phantoms take no damage; registers the network calls. |
| [Patches/PhantomPatches.cs](Patches/PhantomPatches.cs) | Spawners near an active phantom act as if a player were there; marks what they spawn. |
| [Patches/WorldPatches.cs](Patches/WorldPatches.cs) | Despawn protection, the boat fix, and the pick and sail fixes for server-owned objects. |
| [Pieces/ObserverPiece.cs](Pieces/ObserverPiece.cs) | Builds the Chunk Observer and Cluster from Frogger's model. |
| [Pieces/ClusterModel.cs](Pieces/ClusterModel.cs) | The cluster's model: three shafts cut from the column, on round plinths made in code. |
| [Pieces/PhantomPiece.cs](Pieces/PhantomPiece.cs) | Builds the Phantom Spawner from the vanilla ward, stripped of everything that makes it a ward. |
| [Pieces/PhantomSpawner.cs](Pieces/PhantomSpawner.cs) | The phantom's hover text and placement. |
| [Pieces/BuildPermission.cs](Pieces/BuildPermission.cs) | Hides the pieces from non-admins when only admins can build. |
| [Pieces/ChunkObserver.cs](Pieces/ChunkObserver.cs) | An observer's hover text, switching and rune colour. |
| [tools/render_package_icon.py](tools/render_package_icon.py) | Draws the Thunderstore icon. |
| [Assets/chunkloader](Assets/chunkloader) | Frogger's asset bundle, embedded in the DLL. |
| [Diagnostics/](Diagnostics/) | The read-only diagnostic plugin that ran on the live server before the build. Not part of the mod. |
