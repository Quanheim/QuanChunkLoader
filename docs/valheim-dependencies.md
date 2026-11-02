# Valheim Code Dependency

Everything this mod relies on from Valheim, so a game update can be checked quickly. `check-mods.ps1` in the
`.decompiles` repo reads the **Game code** tables and flags any entry the game changed. See "Valheim updates" in
`docs/DEVELOPMENT.md` for the whole check.

**Checked against Valheim:** 1.0.21 (2026-11-02)

Update this file in the same commit as any code that patches, calls or relies on something new in the game, and
update the version line above each time the mod passes the Valheim update check.

The real mod isn't built yet. Its entries come from `docs/design.md` and are marked *planned*; replace them with the
real list as the code lands. The read-only diagnostic plugin (`Diagnostics/QuanChunkLoaderDiag`) is live on the
server, so its entries are listed too.

## Game code

The first column holds the game class or member in backticks, as `Class` or `Class.Member`. Nested types use their
parent's name, e.g. `Player.PlacementStatus`. Only the first column is read by the script; the rest is for people.

### Harmony patches

| Game code | Patch | What it does | If the game changes it |
|---|---|---|---|
| `ZNetScene.CreateObjects` | Prefix, *planned* | Server instantiates forced-zone objects, skipping tamed ZDOs. | Core of the mod: re-read how the scene picks objects to create. |
| `ZNetScene.RemoveObjects` | Prefix, *planned* | Server keeps forced-zone objects; drops tames every pass. | As above. |
| `ZNetScene.CreateDestroyObjects` | *planned* | Where the server's scene pass starts. | As above. |
| `ZDOMan.ReleaseNearbyZDOS` | *planned* | Server claims forced-zone objects no player's vanilla area covers; takes back distant claims. | Core of the ownership rule: re-read the hand-over logic. |
| `ItemDrop.TimedDestruction` | Scoped flag, *planned* | Despawn protection near stations and phantoms. | The 25 m player check it wraps. |
| `Player.IsPlayerInRange` | Scoped flag, *planned* | The check the despawn and spawner flags answer. | Overloads and which callers use which. |
| `CreatureSpawner.UpdateSpawner` | *planned* | Phantom keeps surtling spawners running. | Spawn conditions. |
| `SpawnArea.UpdateSpawn` | *planned* | Phantom keeps spawn areas running. | Spawn conditions. |
| `Pickable.RPC_Pick` | Prefix, *planned* | On a server owner, hands ownership to the picker and re-sends. | Still assumes a local player? If fixed, drop the patch. |
| `Ship.UpdateSailSize` | Prefix, *planned* | Skips the local-player effect on the server. Signature `(float, bool)` since 1.0.21. | If Iron Gate guards `Player.m_localPlayer`, drop the patch. |
| `WearNTear.RPC_Damage` | *planned* | Defensive damage handling in forced zones (see design). | Damage path. |
| `Ship.CustomFixedUpdate` | Prefix and Finalizer, *planned* | Scoped flag while a ship runs physics on the server. | Still averages `Floating.GetWaterLevel` samples? |
| `Floating.GetWaterLevel` | Postfix, *planned* | In ship context, replaces -10000 from an unloaded zone with the centre sample. | Still returns -10000 for no water? Still static with this signature? |
| `ZNetScene.OutsideActiveArea` | Postfix, *planned* | On the server, forced points count as active only with all 8 neighbouring zones forced. | Still what `WearNTear`, `StaticPhysics` and `SpawnArea` ask? |
| `ZNetScene.CreateObjectsSorted` | *planned* | Unknown-prefab guard: never let the server delete unknown objects in forced zones. | Still destroys invalid-prefab ZDOs on the server? |

### Private members reached by reflection

| Game code | Kind | Used for |
|---|---|---|
| `ZNetScene.PointInsideActiveArea` | Method | Diagnostic: vanilla's active-area test, bypassing the old loader's patch. The real mod's ownership rule needs the same test. |
| `ZNetScene.m_instances` | Field | Diagnostic: which ZDOs the server has instantiated. |
| `ItemDrop.s_instances` | Field | Diagnostic: dropped items in forced zones. |
| `Floating.s_waterVolumeMask` | Field | Diagnostic: boat water sampling. |
| `ZNet.m_netTime` | Field, *planned* | Empty-server hour: advance the world clock with no players. |

### Public game code called or read

| Game code | Used for |
|---|---|
| `ZNetScene.InActiveArea` | The old loaders' patch target; the real mod must not patch it. |
| `ZNetScene.FindInstance` | Diagnostic: is an object loaded on the server. |
| `ZNetScene.GetPrefab` | Prefab names and regrow times. |
| `ZNetScene.m_prefabs` | Diagnostic startup report. |
| `ZDOMan.FindSectorObjects` | Objects in a zone. |
| `ZDOMan.GetSessionID` | The server's own owner ID. |
| `ZDOMan.GetZDO` | Looking objects up by ID. |
| `ZNet.GetPeers` | Connected players and their positions. |
| `ZNet.GetSyncedSimulationDistance` | Players' vanilla area size. |
| `ZNet.GetTime` | World clock. |
| `ZNet.GetTimeSeconds` | World clock in logs. |
| `ZNet.GetNrOfPlayers` | Empty-server detection. |
| `ZNet.IsDedicated` | Server-only logic. |
| `ZoneSystem.GetZone` | Zone of a position. |
| `ZoneSystem.IsZoneLoaded` | Boat fix: is a water sample's zone loaded. Diagnostic: the same check. |
| `ZDOVars.s_tamed` | Skipping tames in forced zones. |
| `ZDOVars.s_picked` | Plant state. |
| `ZDOVars.s_pickedTime` | Plant regrow timing. |
| `ZDOVars.s_state` | Old loader on/off (diagnostic). |
| `ZDOVars.s_fuel` | Old loader fuel (diagnostic). |
| `Pickable.m_respawnTimeMinutes` | Plant regrow time. |
| `WaterVolume.Instances` | Diagnostic: water in forced zones. |
| `EnvMan.GetWindIntensity` | Diagnostic: wind on an empty server. |
| `Utils.GetPrefabName` | Prefab names in logs. |
| `Ship.CustomFixedUpdate` | Water sampling; why boats sink at an unloaded edge. |

### Behaviour the mod assumes

| Game code | Assumption |
|---|---|
| `Pickable.UpdateRespawn` | Only the owner runs the regrow check, every 60 s. |
| `ZNet.UpdateNetTime` | World time only advances with a player online. |
| `ZDOMan.ReleaseNearbyZDOS` | Runs every 2 s per peer and hands an object over only when its owner is out of range. |
| `ZNetScene.RemoveObjects` | Unloading a non-persistent object you own deletes it. The mod must never rely on unloading being harmless. |
| `WearNTear.UpdateWear` | Skips wear and support (keeping full support) when `OutsideActiveArea` is true. |
| `Ship.CustomFixedUpdate` | Averages five water samples; a sample over land or in an unloaded zone reads -10000. |
| `Tameable.RPC_Command` | Routed to the owner; only the owner can clear `follow`. |
| `Smelter` | Conversion runs on the owner from world time, so it catches up when the clock moves. |
| `Fermenter` | As above. |
| `Beehive` | As above. |
| `CookingStation` | As above. Since 1.0.21 it credits the cook by stored player ID; older slots fall back to `Player.m_localPlayer`. |
| `Plant` | Crops grow on the owner from world time. |
| `PrivateArea.CheckAccess` | Wards decide who may switch an observer. |
| `Piece.m_targetNonPlayerBuilt` | Monsters target player-built pieces (raid behaviour in loaded zones). |
| `Character.GetAllCharacters` | Tame mods act on this client-side list; clients must never hold distant tames. |

## Prefabs and assets

| Prefab or asset | Used for | Breaks if |
|---|---|---|
| Old loader prefab (`StartupReport.OldLoaderPrefab`) | Diagnostic and migration: finding old loaders in the world. | The old mod renames it. |
| Observer and phantom models | *planned*, chosen in game (see design "Still open"). | Depends on the pieces chosen. |
| Surtling prefab names | Diagnostic counts surtlings in forced zones by name. | Renamed. |
