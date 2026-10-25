# Quan's Chunk Loader

A Valheim chunk loader for dedicated servers (BepInEx and Jotunn). It keeps bases loaded while players are
elsewhere, hands object ownership back to whoever is nearby, keeps moored boats afloat at the edge of loaded
areas, keeps surtling farms spawning, and gives up to an hour of production after the last player logs off.

> **Dedicated to Frogger**, who had the original idea of a Valheim chunk loader. As a tribute, the Chunk Observer
> uses Frogger's original chunk loader model ([JFHeim/ChunkLoader](https://github.com/JFHeim/ChunkLoader), MIT
> licence). The model is the only thing reused: this mod is a ground-up rewrite, and no code comes from Frogger's
> mod or any other current chunk loader.

- Design: [`docs/design.md`](docs/design.md)
- Read-only server diagnostic used to verify the design: [`docs/diagnostic-test-plan.md`](docs/diagnostic-test-plan.md)
- Building and packaging: [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md)
- Thunderstore page: [`Package/README.md`](Package/README.md)
