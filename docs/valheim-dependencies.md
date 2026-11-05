# Valheim Code Dependency

Everything this mod relies on from Valheim, so a game update can be checked quickly. `check-mods.ps1` in the
`.decompiles` repo reads the **Game code** tables and flags any entry the game changed. See "Valheim updates" in
`docs/DEVELOPMENT.md` for the whole check.

**Checked against Valheim:** 1.0.21 (2026-11-02)

Update this file in the same commit as any code that patches, calls or relies on something new in the game, and
update the version line above each time the mod passes the Valheim update check.

Everything planned for 1.0 is built (5 Nov 2026), so nothing is marked *planned* any more. The read-only diagnostic
plugin (`Diagnostics/QuanChunkLoaderDiag`) is listed too, marked *diagnostic*, until it retires at 1.0.

## Game code

The first column holds the game class or member in backticks, as `Class` or `Class.Member`. Nested types use their
parent's name, e.g. `Player.PlacementStatus`. Only the first column is read by the script; the rest is for people.

### Harmony patches

| Game code | Patch | What it does | If the game changes it |
|---|---|---|---|
| `ZNetScene.RemoveObjects` | Prefix (`KeepForcedInstances`) | On the server, earmarks forced-zone instances with this frame's mark (`Time.frameCount & 0xFF`) so the scene pass keeps them. Tames and wild creatures aren't marked (except phantom spawns), so they unload. | Core of loading: still earmarks by frame count and unloads unmarked instances? Still deletes non-persistent objects it unloads? |
| `ZNetScene.CreateObjectsSorted` | Prefix (`UnknownPrefabGuardNear`) | Takes unknown-prefab ZDOs in forced zones out of the list, so vanilla can't delete them. Parameter `currentNearObjects`. | Still destroys ZDOs whose prefab it can't create? Parameter renamed? |
| `ZNetScene.CreateDistantObjects` | Prefix (`UnknownPrefabGuardDistant`) | As above, for distant objects. Parameter `objects`. | As above. |
| `ZNetScene.PointInsideActiveArea` | Postfix (`EdgeRule`) | Inside the three callers below only, on the server: a point in a forced zone whose 8 neighbours are all forced counts as active. Parameter `point`. | Still the test behind `OutsideActiveArea` and `InActiveArea`? Still private static `(Vector2s, Vector3)`? |
| `WearNTear.UpdateWear` | Prefix and Finalizer (`EdgeRuleContext`) | Scoped flag for the edge rule. | Still skips wear and keeps full support when `OutsideActiveArea` is true? |
| `StaticPhysics.SUpdate` | Prefix and Finalizer (`EdgeRuleContext`) | Scoped flag for the edge rule. | Still asks `OutsideActiveArea`? |
| `SpawnArea.UpdateSpawn` | Prefix and Finalizer (`EdgeRuleContext`) | Scoped flag for the edge rule. | Still asks `OutsideActiveArea`? |
| `WearNTear.RPC_Damage` | Prefix (`ObserverTakesNoDamage`) | Observers and phantoms take no damage. | Still the damage entry point for pieces? |
| `Game.Start` | Postfix (`RegisterRpcs`) | Registers the mod's routed RPCs each session. | Still runs once per session after `ZRoutedRpc` exists? |
| `ItemDrop.TimedDestruction` | Prefix (`DespawnProtection`) | On the server, skips the hour-old check for an item in a forced zone within 8 m of a server-run station or 40 m of an active phantom. | Still the only despawn path for dropped items? Still private, no parameters? |
| `Player.IsPlayerInRange` | Postfix (`PhantomCountsAsPlayer`), overloads `(Vector3, float)` and `(Vector3, float, float)` | Inside the phantom context only: an active phantom within `range` of `point` counts as a player. Parameters `point`, `range`. | Overloads or parameter names changed? Do spawners still use these two? |
| `CreatureSpawner.UpdateSpawner` | Prefix and Finalizer (`PhantomCreatureSpawnerContext`) | Phantom context for allowlisted creature spawners (by prefab name). | Still checks `Player.IsPlayerInRange` with `m_triggerDistance`? Still spawns inside this update? |
| `SpawnArea.UpdateSpawn` (phantom) | Prefix and Finalizer (`PhantomSpawnAreaContext`) | Phantom context for spawner areas, when the setting is on. | Still checks `Player.IsPlayerInRange` and spawns via `SpawnOne` inside it? |
| `Character.Awake` | Postfix (`MarkPhantomSpawned`) | Inside the phantom context, after a phantom passed the range check: marks the new creature's ZDO `QCL_phantom` so the server keeps running it. | Still runs when a spawner instantiates a creature (`CreatureSpawner.Spawn`, `SpawnArea.SpawnOne`)? |
| `Pickable.RPC_Pick` | Prefix (`PickOnServerOwned`) | With no local player (a dedicated server) and a pick from a player: hands the plant to the picker and re-sends `RPC_Pick` to them 0.5 s later. Parameters `sender`, `bonus`. | Still reads `Player.m_localPlayer`? If fixed, drop the patch. |
| `Ship.UpdateSailSize` | Prefix (`SailOnServer`) | With no local player, sets `haveControllingPlayer` false so the sail effect that reads `Player.m_localPlayer` is skipped. Signature `(float, bool)` since 1.0.21. | If Iron Gate guards `Player.m_localPlayer`, drop the patch. Parameter renamed? |
| `Ship.CustomFixedUpdate` | Prefix and Finalizer (`BoatContextScope`) | Boat context while a ship in a forced zone runs physics on the server. | Still samples `Floating.GetWaterLevel` five times, centre first? |
| `Floating.GetWaterLevel` | Postfix (`BoatIgnoresUnloadedWater`), overload `(Vector3, ref WaterVolume)` | In boat context, replaces a -10000 side sample from an unloaded zone with the centre sample. | Still returns -10000 for no water? Still static with this signature? |

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
| `ZNet.m_netTime` | Field | Empty-server hour: advances the world clock with no players (`EmptyServer`). |
| `Player.UpdateAvailablePiecesList` | Method | Refreshes the hammer after admin-only building hides or shows this mod's pieces. |

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
| `ZoneSystem.IsZoneLoaded` | Only creating objects once their zone's terrain is in; the boat fix's "unloaded zone" test. Diagnostic: the same check. |
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
| `ZNet.GetNrOfPlayers` | Empty-server hour: the window opens when it drops to 0. |
| `ZDOVars.s_picked` | *Diagnostic:* plant state. |
| `ZDOVars.s_pickedTime` | *Diagnostic:* plant regrow timing. |
| `ZDOVars.s_state` | *Diagnostic:* old loader on/off. |
| `ZDOVars.s_fuel` | *Diagnostic:* old loader fuel. |
| `Pickable.m_respawnTimeMinutes` | *Diagnostic:* plant regrow time. |
| `WaterVolume.Instances` | *Diagnostic:* water in forced zones. |
| `EnvMan.GetWindIntensity` | *Diagnostic:* wind on an empty server. |
| `Utils.GetPrefabName` | Prefab names in logs; matching creature spawners against the phantom allowlist. |
| `Pickable.GetPicked` | The pick fix only acts on unpicked plants. |
| `Piece.m_enabled` | Admin-only building hides this mod's pieces from non-admins' hammers. |
| `Minimap.AddPin` | Admin map pins (type `Icon3`, not saved). |
| `Minimap.RemovePin` | Removing the admin map pins before redrawing. |
| `Minimap.PinData` | `m_icon` replaced with Frogger's icons after `AddPin`. |
| `Character` | Phantom spawns: a new creature's `ZNetView` ZDO gets the `QCL_phantom` flag. |

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
| `PieceTable.UpdateAvailable` | Leaves pieces with `Piece.m_enabled` false out of the hammer (admin-only building). |
| `CreatureSpawner.Spawn` | Instantiates the creature inside `UpdateSpawner`, so its `Character.Awake` runs in the phantom context. |
| `SpawnArea.SpawnOne` | As above, inside `UpdateSpawn`. |
| `ZNet.UpdateNetTime` | On the server, adds time only while `GetNrOfPlayers() > 0`, so the empty-server hour adds its own and they never overlap. |
| `Smelter.UpdateSmelter` | Catches up on up to an hour of elapsed world time when next loaded, so a smelter can't show whether its zone stayed loaded (tests use the log's Summary instead). |
| `WearNTear.Highlight` | The hammer's hover highlight writes `_EmissionColor` through `MaterialMan` property blocks and then clears it. So the observer changes its rune colour on its own material copy, which the highlight's reset doesn't undo. |

## Prefabs and assets

| Prefab or asset | Used for | Breaks if |
|---|---|---|
| `Stone` | Build cost of all three pieces (10, 30 for the cluster, 10). | Renamed. |
| `stone_wall_1x1` | Observer and phantom place, hit and destroy effects (copied from its `Piece` and `WearNTear`). | Renamed; the observer then has no effects and logs a warning. |
| `chunkloader` asset bundle (embedded, from JFHeim/ChunkLoader) | Observer model: `ChunkLoader_stone` (children `new`, `new/SM_cloumn` and `WayEffect`), sprites `icon_active` (piece icon, map) and `icon_disabled` (map). The column mesh `SM_cloumn` is readable; the cluster cuts its shaft (0.295–2.205 of 2.47 m) out of it and textures its round plinths with the capital-top patch of `T_column_A` (UV 0.04–0.20, 0.72–0.88), measured 5 Nov 2026. The column's `Standard` material has an `_EmissionColor` (blue runes at (2, 2, 2)); the observer sets it to red (6, 0, 0) while off, as Frogger's mod did. Built with Unity 6000.0.72; the game runs 6000.0.75. Its prefab carries vanilla `ZNetView`, `Piece`, `WearNTear` and `LightLod`. | A Unity upgrade the bundle can't load in; vanilla scripts it references renamed. The mesh not readable (the cluster fails to build and isn't added; logged). Shaders: `Standard`, `Particles/Standard Unlit`, `Legacy Shaders/Particles/Alpha Blended`, swapped for the game's copies when found. |
| `guard_stone` (vanilla ward) | Phantom Spawner placeholder look: cloned, every script but `ZNetView`, `Piece` and `WearNTear` removed (with its particles, lights, sounds and trigger volumes), children and root colliders at 25%, red glowing material copies. Loading stops if a `PrivateArea` or `EffectArea` survives. | Renamed (the phantom isn't added; logged), or re-modelled so the stripped clone looks wrong. |
| `Spawner_imp_respawn` | Default phantom allowlist entry (the surtling spawner), by prefab name. | Renamed: surtling farms stop until the setting is updated. |
| Old loader prefab (`StartupReport.OldLoaderPrefab`) | *Diagnostic:* finding old loaders in the world. | The old mod renames it. |
| Surtling prefab names | *Diagnostic:* counts surtlings in forced zones by name. | Renamed. |
