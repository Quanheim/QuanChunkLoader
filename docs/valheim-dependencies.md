# Valheim Code Dependency

Everything this mod relies on from Valheim, so a game update can be checked quickly. `check-mods.ps1` in the
`.decompiles` repo reads the **Game code** tables and flags any entry the game changed. See "Valheim updates" in
`docs/DEVELOPMENT.md` for the whole check.

**Checked against Valheim:** 1.0.21 (2026-11-02)

Update this file in the same commit as any code that patches, calls or relies on something new in the game, and
update the version line above each time the mod passes the Valheim update check.

The core is built (observer, forced zones, ownership rule, safety rules, logging). Entries for the features still to
come are marked *planned*, from `docs/design.md`; replace them with the real entries as the code lands. The read-only
diagnostic plugin (`Diagnostics/QuanChunkLoaderDiag`) is listed too, marked *diagnostic*, until it retires at 1.0.

## Game code

The first column holds the game class or member in backticks, as `Class` or `Class.Member`. Nested types use their
parent's name, e.g. `Player.PlacementStatus`. Only the first column is read by the script; the rest is for people.

### Harmony patches

| Game code | Patch | What it does | If the game changes it |
|---|---|---|---|
| `ZNetScene.RemoveObjects` | Prefix (`KeepForcedInstances`) | On the server, earmarks forced-zone instances with this frame's mark (`Time.frameCount & 0xFF`) so the scene pass keeps them. Tames and wild creatures aren't marked, so they unload. | Core of loading: still earmarks by frame count and unloads unmarked instances? Still deletes non-persistent objects it unloads? |
| `ZNetScene.CreateObjectsSorted` | Prefix (`UnknownPrefabGuardNear`) | Takes unknown-prefab ZDOs in forced zones out of the list, so vanilla can't delete them. Parameter `currentNearObjects`. | Still destroys ZDOs whose prefab it can't create? Parameter renamed? |
| `ZNetScene.CreateDistantObjects` | Prefix (`UnknownPrefabGuardDistant`) | As above, for distant objects. Parameter `objects`. | As above. |
| `ZNetScene.PointInsideActiveArea` | Postfix (`EdgeRule`) | Inside the three callers below only, on the server: a point in a forced zone whose 8 neighbours are all forced counts as active. Parameter `point`. | Still the test behind `OutsideActiveArea` and `InActiveArea`? Still private static `(Vector2s, Vector3)`? |
| `WearNTear.UpdateWear` | Prefix and Finalizer (`EdgeRuleContext`) | Scoped flag for the edge rule. | Still skips wear and keeps full support when `OutsideActiveArea` is true? |
| `StaticPhysics.SUpdate` | Prefix and Finalizer (`EdgeRuleContext`) | Scoped flag for the edge rule. | Still asks `OutsideActiveArea`? |
| `SpawnArea.UpdateSpawn` | Prefix and Finalizer (`EdgeRuleContext`) | Scoped flag for the edge rule. | Still asks `OutsideActiveArea`? |
| `WearNTear.RPC_Damage` | Prefix (`ObserverTakesNoDamage`) | Observers take no damage. | Still the damage entry point for pieces? |
| `Game.Start` | Postfix (`RegisterRpcs`) | Registers the mod's routed RPCs each session. | Still runs once per session after `ZRoutedRpc` exists? |
| `ItemDrop.TimedDestruction` | Scoped flag, *planned* | Despawn protection near stations and phantoms. | The 25 m player check it wraps. |
| `Player.IsPlayerInRange` | Scoped flag, *planned* | The check the despawn and spawner flags answer. | Overloads and which callers use which. |
| `CreatureSpawner.UpdateSpawner` | *planned* | Phantom keeps surtling spawners running. | Spawn conditions. |
| `SpawnArea.UpdateSpawn` (phantom) | *planned* | Phantom keeps spawn areas running. | Spawn conditions. |
| `Pickable.RPC_Pick` | Prefix, *planned* | On a server owner, hands ownership to the picker and re-sends. | Still assumes a local player? If fixed, drop the patch. |
| `Ship.UpdateSailSize` | Prefix, *planned* | Skips the local-player effect on the server. Signature `(float, bool)` since 1.0.21. | If Iron Gate guards `Player.m_localPlayer`, drop the patch. |
| `Ship.CustomFixedUpdate` | Prefix and Finalizer, *planned* | Scoped flag while a ship runs physics on the server. | Still averages `Floating.GetWaterLevel` samples? |
| `Floating.GetWaterLevel` | Postfix, *planned* | In ship context, replaces -10000 from an unloaded zone with the centre sample. | Still returns -10000 for no water? Still static with this signature? |

### Private members reached by reflection

| Game code | Kind | Used for |
|---|---|---|
| `ZNetScene.m_instances` | Field | Which ZDOs the server has instantiated (earmarking). Diagnostic too. |
| `ZNetScene.CreateObject` | Method `(ZDO)` | Instantiating forced-zone objects on the server. Returns null for an unknown prefab; the mod never destroys then. |
| `ZoneSystem.PokeLocalZone` | Method `(Vector2s)` | Keeping forced zones loaded (resets their time to live), loading or generating them as for a visiting player. |
| `ZDOMan.m_objectsByID` | Field | Finding every observer in the world at server start. |
| `ZNetScene.PointInsideActiveArea` | Method | Patched (above). Diagnostic: vanilla's active-area test. |
| `ItemDrop.s_instances` | Field | *Diagnostic:* dropped items in forced zones. |
| `Floating.s_waterVolumeMask` | Field | *Diagnostic:* boat water sampling. |
| `ZNet.m_netTime` | Field, *planned* | Empty-server hour: advance the world clock with no players. |

### Public game code called or read

| Game code | Used for |
|---|---|
| `ZNetScene.InActiveArea` | The ownership rule's "is a player near": vanilla's own test, never patched. |
| `ZNetScene.FindInstance` | Is an object instantiated on the server (summaries). |
| `ZNetScene.GetPrefab` | Classifying objects by prefab (`ObjectKinds`). |
| `ZDO.TempRemoveEarmark` | Keeping forced-zone instances (see `ZNetScene.RemoveObjects`). |
| `ZDO.Created` | Which forced-zone objects still need creating. |
| `ZDO.Persistent` | Only persistent objects are claimed, as vanilla's own ownership pass does. |
| `ZDO.GetSector` | An object's zone. |
| `ZDO.SetOwner` | Claiming, handing back and releasing objects. |
| `ZDOMan.FindSectorObjects` | Objects in one zone, called with `SimulationDistance(0, 0)`. |
| `ZDOMan.GetSessionID` | The server's own owner ID. |
| `ZDOMan.GetZDO` | Looking objects up by ID. |
| `ZDOMan.m_onZDODestroyed` | Noticing an observer was removed. |
| `ZoneSystem.IsZoneLoaded` | Only creating objects once their zone's terrain is in. Diagnostic: the same check. *Planned:* boat fix. |
| `ZoneSystem.IsZoneReadyForType` | Creating objects in the order vanilla does. |
| `ZoneSystem.GetZone` | Zone of a position. |
| `ZNet.GetPeers` | Connected players and their positions (`ZNetPeer.GetRefPos`, `m_playerID`, `m_characterID`, `m_playerName`, `m_socket`). |
| `ZNet.GetPeer` | A player by session ID. |
| `ZNet.GetServerPeer` | Observer owners accept flag changes only from the server. |
| `ZNet.IsAdmin` | Admin check for switching and the admin-placed flag. |
| `ZNet.IsServer` | Server-only logic. |
| `ZNet.IsDedicated` | Telling a dedicated server from a local game's host. |
| `ZNet.GetConnectionStatus` | Waiting for the world before starting. |
| `ZNet.GetReferencePosition` | The host's own area in a local game. |
| `ZRoutedRpc.Register` | The switch, placed and message RPCs. |
| `ZRoutedRpc.InvokeRoutedRPC` | As above, and flag changes routed to an observer's owner. |
| `ZDOVars.s_tamed` | Skipping tames in forced zones. |
| `ZDOVars.s_inUse` | Never claiming an object in use. |
| `ZDOVars.s_creator` | An observer's builder. |
| `ZDOVars.s_playerID` | A player's ID from their character ZDO, if the peer doesn't have it. |
| `Piece.GetCreator` | Recording the builder's name on placement. |
| `Piece.IsCreator` | Who may switch an observer. |
| `PrivateArea.CheckAccess` | Wards decide who may switch when the builder-or-admin setting is off. |
| `MessageHud.ShowMessage` | Refusal messages. |
| `Character` | A prefab with a `Character` that isn't tamed or a player is a wild creature, not loaded in forced zones. |
| `Player` | Players load as in vanilla, and are never claimed. |
| `Ship`, `Container`, `Smelter`, `Fermenter`, `Beehive`, `CookingStation`, `SapCollector`, `ItemDrop`, `Pickable`, `Plant`, `Piece` | Categories in log lines. |
| `ZNetScene.m_prefabs` | *Diagnostic:* startup report. |
| `ZNet.GetSyncedSimulationDistance` | *Diagnostic:* players' vanilla area size. |
| `ZNet.GetTime` | *Diagnostic:* world clock. |
| `ZNet.GetTimeSeconds` | *Diagnostic:* world clock in logs. |
| `ZNet.GetNrOfPlayers` | *Planned:* empty-server detection. |
| `ZDOVars.s_picked` | *Diagnostic:* plant state. |
| `ZDOVars.s_pickedTime` | *Diagnostic:* plant regrow timing. |
| `ZDOVars.s_state` | *Diagnostic:* old loader on/off. |
| `ZDOVars.s_fuel` | *Diagnostic:* old loader fuel. |
| `Pickable.m_respawnTimeMinutes` | *Diagnostic:* plant regrow time. |
| `WaterVolume.Instances` | *Diagnostic:* water in forced zones. |
| `EnvMan.GetWindIntensity` | *Diagnostic:* wind on an empty server. |
| `Utils.GetPrefabName` | Prefab names in logs. |

### Behaviour the mod assumes

| Game code | Assumption |
|---|---|
| `ZDOMan.ReleaseNearbyZDOS` | Runs every 2 s per peer and hands an object to a player when its owner is out of range (the server counts as out of range of a forced zone). This is what hands server-owned objects to a player who comes near. |
| `ZDOMan.IsInPeerActiveArea` | For the server's own session, tests against the server's reference position, so a forced zone away from it counts as out of the server's range. |
| `ZNetScene.CreateDestroyObjects` | The scene pass runs `CreateObjects` then `RemoveObjects` in the same frame. |
| `ZNetScene.RemoveObjects` | Unloading a non-persistent object you own deletes it. The mod never relies on unloading being harmless. |
| `ZNetScene.CreateObjectsSorted` | On the server, a ZDO whose prefab can't be created is deleted. |
| `ZoneSystem.UpdateTTL` | A zone unloads once its time to live runs out and nothing is instantiated in it; poking resets it. |
| `ZoneSystem.Update` | A dedicated server only loads zones around its own reference position, not around players. |
| `ZDOMan.CreateSyncList` | The server sends players only the ZDOs around them, so clients never receive forced zones they aren't near. |
| `ZNetScene.OutsideActiveArea` | What `WearNTear`, `StaticPhysics` and `SpawnArea` ask; too small to patch safely (may be inlined). |
| `WearNTear.UpdateWear` | Skips wear and support (keeping full support) when `OutsideActiveArea` is true. Only the owner runs it. |
| `Pickable.UpdateRespawn` | Only the owner runs the regrow check, every 60 s. |
| `ZNet.UpdateNetTime` | World time only advances with a player online. |
| `Ship.CustomFixedUpdate` | Averages five water samples; a sample over land or in an unloaded zone reads -10000. |
| `Tameable.RPC_Command` | Routed to the owner; only the owner can clear `follow`. |
| `Smelter` | Conversion runs on the owner from world time, so it catches up when the clock moves. |
| `Fermenter` | As above. |
| `Beehive` | As above. |
| `CookingStation` | As above. Since 1.0.21 it credits the cook by stored player ID; older slots fall back to `Player.m_localPlayer`. |
| `Plant` | Crops grow on the owner from world time. |
| `Piece.m_targetNonPlayerBuilt` | Monsters target player-built pieces; set off on observers, with `StaticTarget.m_primaryTarget` and `m_randomTarget`. |
| `WearNTear.m_noRoofWear` | False means no rain wear; set false on observers, as on vanilla stone. |
| `WearNTear.m_noSupportWear` | False means no damage from lack of support; set false on observers. |
| `Growup.GrowUpdate` | Young grow up once enough world time has passed since spawn, so they catch up when loaded. |
| `Procreation.Procreate` | Love points only build while the tame is running (every 10 s, partner near, not hungry): breeding doesn't catch up. |
| `Character.GetAllCharacters` | Tame mods act on this client-side list; clients must never hold distant tames. |

## Prefabs and assets

| Prefab or asset | Used for | Breaks if |
|---|---|---|
| `Stone` | Observer build cost. | Renamed. |
| `stone_wall_1x1` | Observer place, hit and destroy effects (copied from its `Piece` and `WearNTear`). | Renamed; the observer then has no effects and logs a warning. |
| `chunkloader` asset bundle (embedded, from JFHeim/ChunkLoader) | Observer model: `ChunkLoader_stone` (children `new` and `WayEffect`), sprite `icon_active`. Built with Unity 6000.0.72; the game runs 6000.0.75. Its prefab carries vanilla `ZNetView`, `Piece`, `WearNTear` and `LightLod`. | A Unity upgrade the bundle can't load in; vanilla scripts it references renamed. Shaders: `Standard`, `Particles/Standard Unlit`, `Legacy Shaders/Particles/Alpha Blended`, swapped for the game's copies when found. |
| `guard_stone` (vanilla ward, check the prefab name in game) | *Planned:* Phantom Spawner placeholder look: mesh and materials at 25%, red glow. `PrivateArea` must not be copied. | Renamed or re-modelled. |
| Old loader prefab (`StartupReport.OldLoaderPrefab`) | *Diagnostic:* finding old loaders in the world. | The old mod renames it. |
| Surtling prefab names | *Diagnostic:* counts surtlings in forced zones by name. | Renamed. |
