# Quan's Chunk Loader

A Valheim chunk loader for dedicated servers (BepInEx and Jotunn). It keeps bases loaded while players are
elsewhere, hands object ownership back to whoever is nearby, keeps moored boats afloat at the edge of loaded
areas, keeps surtling farms spawning, and gives up to an hour of production after the last player logs off.

- What it does and how to use it: [Package/README.md](Package/README.md) (the Thunderstore page).
- **Where we are and what's next: [docs/design.md](docs/design.md#build-status)** ("Build status" at the top).
- Building and releasing: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).
- Game code it relies on: [docs/valheim-dependencies.md](docs/valheim-dependencies.md).
- The read-only server diagnostic that verified the design: [docs/diagnostic-test-plan.md](docs/diagnostic-test-plan.md).

> **Dedicated to Frogger**, who had the original idea of a Valheim chunk loader. As a tribute, the Chunk Observer
> uses Frogger's original chunk loader model ([JFHeim/ChunkLoader](https://github.com/JFHeim/ChunkLoader), MIT
> licence, see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)). The model is the only thing reused: this mod is a
> ground-up rewrite, and no code comes from Frogger's mod or any other current chunk loader.

## Status

In development. The core is built (4 Nov 2026) and waiting for its first single-player check; the rest of 1.0 comes
next. The Thunderstore page describes the finished 1.0, so parts of it aren't built yet. The full list of what is
and isn't built is in [docs/design.md](docs/design.md#build-status).

## Code

| File | What it does |
|---|---|
| [QuanChunkLoaderPlugin.cs](QuanChunkLoaderPlugin.cs) | Start-up: refuses to run next to an old chunk loader, applies the patches (all or nothing), adds the piece. |
| [Settings.cs](Settings.cs) | The config file, in numbered sections. Every setting is the server's and admin-only. |
| [Log.cs](Log.cs) | Log lines with UTC-offset timestamps, the detail levels and the optional daily file. |
| [Core/Observers.cs](Core/Observers.cs) | The server's list of observers, the set of loaded ("forced") zones, and switching. |
| [Core/ServerLoader.cs](Core/ServerLoader.cs) | Server only: loads forced zones and their objects, and runs the 2 s ownership pass and the summaries. |
| [Core/ObjectKinds.cs](Core/ObjectKinds.cs) | What the server loads (no wild creatures or tames by default) and what it may own. |
| [Core/GameAccess.cs](Core/GameAccess.cs) | Reflection access to the private game fields and methods used. |
| [Patches/ScenePatches.cs](Patches/ScenePatches.cs) | Keeps forced-zone objects loaded, skips unknown prefabs, and the edge-support rule. |
| [Patches/ObserverPatches.cs](Patches/ObserverPatches.cs) | The observer takes no damage; registers the network calls. |
| [Pieces/ObserverPiece.cs](Pieces/ObserverPiece.cs) | Builds the Chunk Observer from Frogger's model. |
| [Pieces/ChunkObserver.cs](Pieces/ChunkObserver.cs) | The observer's hover text, switching and glow. |
| [Assets/chunkloader](Assets/chunkloader) | Frogger's asset bundle, embedded in the DLL. |
| [Diagnostics/](Diagnostics/) | The read-only diagnostic plugin that ran on the live server before the build. Not part of the mod. |
