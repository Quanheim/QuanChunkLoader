# Quan's Chunk Loader

A Valheim chunk loader for dedicated servers (BepInEx and Jotunn). It keeps bases loaded while players are
elsewhere, hands object ownership back to whoever is nearby, keeps moored boats afloat at the edge of loaded
areas, keeps surtling farms spawning, and gives up to an hour of production after the last player logs off.

The original Valheim chunk loader idea is Frogger's. This mod is a ground-up rewrite: no code is reused from
Frogger's mod or any other current chunk loader.

- Design: [`docs/design.md`](docs/design.md)
- Read-only server diagnostic used to verify the design: [`docs/diagnostic-test-plan.md`](docs/diagnostic-test-plan.md)
- Building and packaging: [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md)
- Thunderstore page: [`Package/README.md`](Package/README.md)
