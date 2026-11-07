using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace QuanChunkLoader.Core
{
    /// <summary>
    /// Debug log only (Log detail Debug or Trace): the checks the old diagnostic mod (QuanChunkLoaderDiag, 25 Oct
    /// 2026) ran on the live server, now part of the loader so they can be turned on without installing anything.
    /// Server only, read only: nothing here changes the game.
    /// - Once per start: game version, every plugin, the water lookup, and every smelter-type station's capacity
    ///   as other mods have left it (Conversion Size &amp; Speed, for example).
    /// - Players connecting and leaving, with where they are and the world time.
    /// - Changes of owner in loaded zones, grouped (fed by the ownership pass); ships get their own lines.
    /// - Ships on the server: water readings and whether buoyancy applies, with and without the boat fix.
    /// - Picked plants overdue to regrow in loaded zones and around players, and who owns them.
    /// - A heartbeat with the 5-minute summary.
    /// Tames are followed by TameWatch.
    /// The old mod's checks on the old chunk loader's own pieces and zones aren't needed: the loader logs its own.
    /// </summary>
    internal static class DebugWatch
    {
        private const float PlayerInterval = 2f;
        private const float ShipInterval = 5f;
        private const float PlantInterval = 30f;
        private const int MaxErrorsPerCheck = 5;
        private const int MaxDetailLines = 15;

        private static readonly Dictionary<string, int> Errors = new Dictionary<string, int>();
        private static bool _started;
        private static float _playerTimer, _shipTimer, _plantTimer;

        internal static void Reset()
        {
            Errors.Clear();
            _started = false;
            _playerTimer = _shipTimer = 0f;
            _plantTimer = PlantInterval;
            Peers.Clear();
            Ships.Clear();
            Picked.Clear();
            OwnerChanges.Clear();
            _lastWaterMask = null;
            _regrew = _regrewOverdue = 0;
            RegrewBy.Clear();
        }

        internal static void Update(float dt)
        {
            if (!Log.DebugOn)
            {
                if (_started)
                {
                    Reset();
                }
                return;
            }
            if (!_started)
            {
                _started = true;
                Guard("startup report", StartupReport);
            }
            if ((_playerTimer += dt) >= PlayerInterval)
            {
                _playerTimer = 0f;
                Guard("players", PollPlayers);
            }
            if ((_shipTimer += dt) >= ShipInterval)
            {
                _shipTimer = 0f;
                Guard("ships and water", PollShips);
            }
            if ((_plantTimer += dt) >= PlantInterval)
            {
                _plantTimer = 0f;
                Guard("plants", PollPlants);
            }
        }

        /// <summary>Runs one check in isolation; one that keeps failing is switched off rather than filling the log.</summary>
        private static void Guard(string check, Action action)
        {
            Errors.TryGetValue(check, out var count);
            if (count >= MaxErrorsPerCheck)
            {
                return;
            }
            try
            {
                action();
            }
            catch (Exception e)
            {
                Errors[check] = ++count;
                var off = count >= MaxErrorsPerCheck ? " Switched off until the next restart." : "";
                Log.Warning($"Debug check \"{check}\" failed ({count}/{MaxErrorsPerCheck}).{off} {e}");
            }
        }

        // ---- Shared ------------------------------------------------------------------------------------------

        private static string Where(Vector3 p) => $"({p.x:0}, {p.y:0.0}, {p.z:0}) zone {ZoneSystem.GetZone(p).x},{ZoneSystem.GetZone(p).y}";

        private static string WorldTime => $"world time {ZNet.instance.GetTimeSeconds():0} s, {ZNet.instance.GetNrOfPlayers()} player(s)";

        private static bool InLoadedZone(Vector3 p) => Observers.IsForced(ZoneSystem.GetZone(p));

        private static readonly FieldInfo WaterMaskField = AccessTools.Field(typeof(Floating), "s_waterVolumeMask");
        private static readonly FieldInfo ItemDropsField = AccessTools.Field(typeof(ItemDrop), "s_instances");

        private static int? WaterMask => WaterMaskField?.GetValue(null) as int?;
        private static int ExpectedWaterMask => LayerMask.GetMask("WaterVolume");

        // ---- Startup ----------------------------------------------------------------------------------------

        private static void StartupReport()
        {
            Log.Debug($"Debug checks on: Valheim {Version.GetVersionString()}, {Chainloader.PluginInfos.Count} BepInEx plugins.");
            foreach (var info in Chainloader.PluginInfos.Values.OrderBy(p => p.Metadata.Name))
            {
                Log.Debug($"  Plugin: {info.Metadata.Name} {info.Metadata.Version} ({info.Metadata.GUID})");
            }
            Log.Debug($"Water lookup mask at start: {WaterMask?.ToString() ?? "unreadable"} (set up correctly it's {ExpectedWaterMask}, " +
                      "or 0 until the game's first lookup).");
            foreach (var prefab in ZNetScene.instance.m_prefabs.Where(p => p != null))
            {
                var smelter = prefab.GetComponent<Smelter>();
                if (smelter == null)
                {
                    continue;
                }
                var conversions = string.Join(", ", smelter.m_conversion.Where(c => c?.m_from != null && c.m_to != null)
                    .Select(c => $"{c.m_from.name}->{c.m_to.name}"));
                var fuel = smelter.m_maxFuel > 0
                    ? $"max fuel {smelter.m_maxFuel} ({(smelter.m_fuelItem != null ? smelter.m_fuelItem.name : "?")} x{smelter.m_fuelPerProduct} per product)"
                    : "no fuel";
                Log.Debug($"  Station {prefab.name}: max input {smelter.m_maxOre}, {fuel}, {smelter.m_secPerProduct} s per product, " +
                          $"stacks output {smelter.m_spawnStack}, needs roof {smelter.m_requiresRoof}; [{conversions}]");
            }
            Log.Debug("Spawners: see the reference list next to the config (quandru.QuanChunkLoader.spawners.txt).");
        }

        // ---- Players ----------------------------------------------------------------------------------------

        private static readonly Dictionary<long, string> Peers = new Dictionary<long, string>();

        private static void PollPlayers()
        {
            var current = ZNet.instance.GetPeers().Where(p => p.m_uid != 0).ToList();
            foreach (var peer in current.Where(p => !Peers.ContainsKey(p.m_uid)))
            {
                Log.Debug($"Player connected: {Name(peer)} ({peer.m_uid}) at {Where(peer.GetRefPos())}; {WorldTime}.");
            }
            foreach (var uid in Peers.Keys.Where(uid => current.All(p => p.m_uid != uid)).ToList())
            {
                Log.Debug($"Player left: {Peers[uid]} ({uid}); {WorldTime}.");
                Peers.Remove(uid);
            }
            foreach (var peer in current)
            {
                Peers[peer.m_uid] = Name(peer); // names can arrive a moment after the connection
            }
        }

        private static string Name(ZNetPeer peer) => string.IsNullOrEmpty(peer.m_playerName) ? $"player {peer.m_uid}" : peer.m_playerName;

        // ---- Owner changes (fed by ServerLoader.OwnershipPass) ------------------------------------------------

        private sealed class Change
        {
            internal string Category;
            internal string Name;
            internal Vector3 Position;
        }

        private static readonly Dictionary<string, List<Change>> OwnerChanges = new Dictionary<string, List<Change>>();

        internal static void OwnerChanged(ZDO zdo, string category, long from, long to)
        {
            if (!Log.DebugOn)
            {
                return;
            }
            var key = $"{Observers.PlayerName(from)} -> {Observers.PlayerName(to)}";
            if (!OwnerChanges.TryGetValue(key, out var list))
            {
                OwnerChanges[key] = list = new List<Change>();
            }
            var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            list.Add(new Change { Category = category, Name = prefab != null ? prefab.name : "?", Position = zdo.GetPosition() });
        }

        internal static void EndOwnershipPass()
        {
            if (OwnerChanges.Count == 0)
            {
                return;
            }
            foreach (var kv in OwnerChanges)
            {
                var counts = string.Join(", ", kv.Value.GroupBy(c => c.Category).OrderByDescending(g => g.Count())
                    .Select(g => $"{g.Count()} {g.Key}"));
                Log.Debug($"Owner change {kv.Key}: {kv.Value.Count} object(s) ({counts}); {WorldTime}.");
                // Ships always get their own line (tames have the tame watch); a sample of the rest.
                var details = kv.Value.Where(c => c.Category == "ships")
                    .Concat(kv.Value.Where(c => c.Category != "ships" && c.Category != "tames").Take(MaxDetailLines));
                foreach (var c in details)
                {
                    Log.Debug($"  {c.Category} {c.Name} at {Where(c.Position)}");
                }
            }
            OwnerChanges.Clear();
        }

        // ---- Ships and water --------------------------------------------------------------------------------

        private sealed class ShipState
        {
            internal long Owner;
            internal float Height;
            internal bool GameSeesWater;
            internal bool InLoadedZone;
            internal bool Floats;
            internal bool FloatsWithFix;
        }

        private static readonly Dictionary<ZDOID, ShipState> Ships = new Dictionary<ZDOID, ShipState>();
        private static int? _lastWaterMask;
        private const float ShipMoveThreshold = 0.25f;

        private static void PollShips()
        {
            var mask = WaterMask;
            if (mask != _lastWaterMask)
            {
                Log.Debug($"Water lookup mask {_lastWaterMask?.ToString() ?? "unknown"} -> {mask?.ToString() ?? "unreadable"} " +
                          $"(correct value {ExpectedWaterMask}); {WorldTime}.");
                _lastWaterMask = mask;
            }

            var seen = new HashSet<ZDOID>();
            foreach (var ship in Ship.Instances.OfType<Ship>())
            {
                var view = ship.GetComponent<ZNetView>();
                var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                var body = ship.GetComponent<Rigidbody>();
                if (zdo == null || body == null)
                {
                    continue;
                }
                seen.Add(zdo.m_uid);

                var centre = body.worldCenterOfMass;
                var probe = ProbeBuoyancy(ship, body);
                var now = new ShipState
                {
                    Owner = zdo.GetOwner(),
                    Height = centre.y,
                    GameSeesWater = GameWaterLevel(centre) > BoatNoWater + 1f,
                    InLoadedZone = InLoadedZone(centre),
                    Floats = probe.Floats,
                    FloatsWithFix = probe.FloatsWithFix,
                };

                Ships.TryGetValue(zdo.m_uid, out var before);
                string reason = null;
                if (before == null) reason = "first seen";
                else if (before.Owner != now.Owner) reason = "owner changed";
                else if (before.GameSeesWater != now.GameSeesWater) reason = "water under it changed";
                else if (before.InLoadedZone != now.InLoadedZone) reason = "loaded-zone status changed";
                else if (before.Floats != now.Floats || before.FloatsWithFix != now.FloatsWithFix) reason = "buoyancy changed";
                else if (Mathf.Abs(before.Height - now.Height) > ShipMoveThreshold) reason = "height changed";

                if (reason != null)
                {
                    Log.Debug($"Ship {Utils.GetPrefabName(ship.gameObject)} {zdo.m_uid} ({reason}): owner {Observers.PlayerName(now.Owner)}, " +
                              $"centre of mass y {centre.y:0.00}, game water {FormatLevel(GameWaterLevel(centre))}, " +
                              $"true water {FormatLevel(TrueWaterLevel(centre))}, vertical speed {body.linearVelocity.y:0.00}, " +
                              $"kinematic {body.isKinematic}, in a loaded zone {now.InLoadedZone}, at {Where(centre)}; {WorldTime}.");
                    Log.Debug($"  {probe.Summary}");
                    Ships[zdo.m_uid] = now;
                }
            }
            foreach (var id in Ships.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                Log.Debug($"Ship {id} no longer instantiated on the server.");
                Ships.Remove(id);
            }
        }

        private const float BoatNoWater = -10000f;

        // Outside Ship.CustomFixedUpdate, so the boat fix doesn't apply: the game's own reading. A fresh cache keeps
        // the ship's own cache untouched.
        private static float GameWaterLevel(Vector3 p)
        {
            WaterVolume cache = null;
            return Floating.GetWaterLevel(p, ref cache);
        }

        private static readonly Collider[] Overlap = new Collider[32];

        private static float TrueWaterLevel(Vector3 p)
        {
            var best = BoatNoWater;
            var hits = Physics.OverlapSphereNonAlloc(p, 0f, Overlap, ExpectedWaterMask);
            for (var i = 0; i < hits; i++)
            {
                var volume = Overlap[i] != null ? Overlap[i].GetComponent<WaterVolume>() : null;
                if (volume != null)
                {
                    best = Mathf.Max(best, volume.GetWaterSurface(p));
                }
            }
            return best;
        }

        private static string FormatLevel(float level) => level <= BoatNoWater + 1f ? "none" : level.ToString("0.00");

        private sealed class Probe
        {
            internal bool Floats;
            internal bool FloatsWithFix;
            internal string Summary;
        }

        /// <summary>
        /// Re-runs the buoyancy decision in Ship.CustomFixedUpdate (centre of mass plus the float box's bow, stern,
        /// left and right, averaged), as the game alone and with the boat fix (WorldPatches.BoatIgnoresUnloadedWater).
        /// </summary>
        private static Probe ProbeBuoyancy(Ship ship, Rigidbody body)
        {
            var box = ship.m_floatCollider;
            if (box == null)
            {
                return new Probe { Summary = "no float collider" };
            }
            var t = box.transform;
            var size = box.size;
            var centre = body.worldCenterOfMass;
            var names = new[] { "centre", "bow", "stern", "left", "right" };
            var points = new[]
            {
                centre,
                t.position + t.forward * size.z / 2f,
                t.position - t.forward * size.z / 2f,
                t.position - t.right * size.x / 2f,
                t.position + t.right * size.x / 2f,
            };
            float total = 0f, totalFixed = 0f, centreLevel = 0f;
            var parts = new List<string>();
            for (var i = 0; i < points.Length; i++)
            {
                var level = GameWaterLevel(points[i]);
                var zone = ZoneSystem.GetZone(points[i]);
                var loaded = ZoneSystem.instance.IsZoneLoaded(zone);
                if (i == 0)
                {
                    centreLevel = level;
                }
                var fixedLevel = i > 0 && level <= BoatNoWater + 1f && centreLevel > BoatNoWater + 1f && !loaded ? centreLevel : level;
                total += level;
                totalFixed += fixedLevel;
                parts.Add($"{names[i]} ({points[i].x:0},{points[i].z:0}) zone {zone.x},{zone.y} {(loaded ? "loaded" : "NOT LOADED")} water {FormatLevel(level)}");
            }
            var depth = centre.y - total / points.Length - ship.m_waterLevelOffset;
            var depthFixed = centre.y - totalFixed / points.Length - ship.m_waterLevelOffset;
            var floats = !(depth > ship.m_disableLevel);
            var floatsFixed = !(depthFixed > ship.m_disableLevel);
            return new Probe
            {
                Floats = floats,
                FloatsWithFix = floatsFixed,
                Summary = $"buoyancy {(floats ? "ON" : "OFF")} by the game alone, {(floatsFixed ? "ON" : "OFF")} with the boat fix " +
                          $"(depth term {depth:0.00} / {depthFixed:0.00} vs disable level {ship.m_disableLevel:0.00}), heading " +
                          $"{ship.transform.eulerAngles.y:0}°, float box {size.x:0.0}x{size.z:0.0} m; samples: {string.Join("; ", parts)}",
            };
        }

        // ---- Plants -----------------------------------------------------------------------------------------

        // The owner checks once a minute (Pickable.UpdateRespawn), so more than this past the regrow time is stuck.
        private const double OverdueGraceMinutes = 2.0;

        private sealed class PlantState
        {
            internal string Name;
            internal Vector3 Position;
            internal long Owner;
            internal bool Overdue;
            internal double OverdueMinutes;
        }

        private static readonly Dictionary<ZDOID, PlantState> Picked = new Dictionary<ZDOID, PlantState>();
        private static readonly Dictionary<int, float> RespawnMinutesByPrefab = new Dictionary<int, float>();
        private static readonly List<ZDO> Scratch = new List<ZDO>();
        private static int _regrew, _regrewOverdue;
        private static readonly Dictionary<string, int> RegrewBy = new Dictionary<string, int>();

        private static void PollPlants()
        {
            var now = ZNet.instance.GetTime().Ticks;
            var seen = new HashSet<ZDOID>();
            var becameOverdue = new List<ZDO>();
            var regrew = new List<PlantState>();

            foreach (var zdo in PlantCandidates())
            {
                var respawn = RespawnMinutes(zdo.GetPrefab());
                if (respawn <= 0f)
                {
                    continue;
                }
                seen.Add(zdo.m_uid);
                Picked.TryGetValue(zdo.m_uid, out var state);
                if (!zdo.GetBool(ZDOVars.s_picked))
                {
                    if (state != null)
                    {
                        regrew.Add(state);
                        Picked.Remove(zdo.m_uid);
                    }
                    continue;
                }
                var pickedTicks = zdo.GetLong(ZDOVars.s_pickedTime, 0L);
                var sincePicked = pickedTicks > 1 ? TimeSpan.FromTicks(now - pickedTicks).TotalMinutes : double.MaxValue;
                var overdueMinutes = sincePicked - respawn;
                var overdue = overdueMinutes > OverdueGraceMinutes;
                if (state == null)
                {
                    var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                    state = new PlantState { Name = prefab != null ? prefab.name : "?", Owner = zdo.GetOwner() };
                    Picked[zdo.m_uid] = state;
                }
                else if (state.Owner != zdo.GetOwner() && state.Overdue)
                {
                    Log.Debug($"Overdue {state.Name} at {Where(zdo.GetPosition())} changed owner " +
                              $"{Observers.PlayerName(state.Owner)} -> {Observers.PlayerName(zdo.GetOwner())}; {WorldTime}.");
                }
                state.Owner = zdo.GetOwner();
                state.Position = zdo.GetPosition();
                state.OverdueMinutes = overdueMinutes;
                if (overdue && !state.Overdue)
                {
                    becameOverdue.Add(zdo);
                }
                state.Overdue = overdue;
            }
            foreach (var id in Picked.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                Picked.Remove(id); // left the area we look at
            }

            if (becameOverdue.Count > 0)
            {
                Log.Debug($"{becameOverdue.Count} picked plant(s) now overdue to regrow (more than {OverdueGraceMinutes:0} min past their time); {WorldTime}:");
                foreach (var zdo in becameOverdue.Take(MaxDetailLines))
                {
                    var s = Picked[zdo.m_uid];
                    Log.Debug($"  {s.Name} at {Where(s.Position)}, {FormatOverdue(s.OverdueMinutes)} overdue, " +
                              $"owner {Observers.PlayerName(s.Owner)} ({OwnerRunsIt(zdo, s.Owner)})");
                }
            }
            if (regrew.Count > 0)
            {
                _regrew += regrew.Count;
                foreach (var g in regrew.GroupBy(s => Observers.PlayerName(s.Owner)))
                {
                    RegrewBy.TryGetValue(g.Key, out var n);
                    RegrewBy[g.Key] = n + g.Count();
                }
                var stuck = regrew.Where(s => s.Overdue).ToList();
                _regrewOverdue += stuck.Count;
                if (stuck.Count > 0)
                {
                    Log.Debug($"{stuck.Count} overdue plant(s) regrew; owner before: " +
                              string.Join(", ", stuck.GroupBy(s => Observers.PlayerName(s.Owner)).Select(g => $"{g.Key} x{g.Count()}")) +
                              $"; longest overdue {FormatOverdue(stuck.Max(s => s.OverdueMinutes))}; {WorldTime}.");
                }
            }
        }

        /// <summary>Whether the owner can run the regrow check: the server has it loaded, or it's near the player.</summary>
        private static string OwnerRunsIt(ZDO zdo, long owner)
        {
            if (owner == 0)
            {
                return "no owner";
            }
            if (owner == ZDOMan.GetSessionID())
            {
                return ZNetScene.instance.FindInstance(zdo) != null ? "server has it loaded" : "server does NOT have it loaded";
            }
            var peer = ZNet.instance.GetPeer(owner);
            if (peer == null)
            {
                return "owner offline";
            }
            return ZNetScene.InActiveArea(zdo.GetPosition(), peer.GetRefPos())
                ? "near owner"
                : $"owner far away ({Vector3.Distance(zdo.GetPosition(), peer.GetRefPos()):0} m)";
        }

        private static List<ZDO> PlantCandidates()
        {
            var result = new List<ZDO>();
            var ids = new HashSet<ZDOID>();
            void Add(List<ZDO> found)
            {
                foreach (var z in found)
                {
                    if (z != null && z.IsValid() && ids.Add(z.m_uid))
                    {
                        result.Add(z);
                    }
                }
            }
            foreach (var zone in Observers.Forced)
            {
                Scratch.Clear();
                ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), Scratch);
                Add(Scratch);
            }
            var synced = ZNet.instance.GetSyncedSimulationDistance();
            var near = new SimulationDistance(synced.NearSimulationDistance, 0, synced.IsClassic);
            foreach (var peer in ZNet.instance.GetPeers().Where(p => p.m_uid != 0))
            {
                Scratch.Clear();
                ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(peer.GetRefPos()), near, Scratch);
                Add(Scratch);
            }
            return result;
        }

        private static float RespawnMinutes(int prefabHash)
        {
            if (RespawnMinutesByPrefab.TryGetValue(prefabHash, out var minutes))
            {
                return minutes;
            }
            var prefab = ZNetScene.instance.GetPrefab(prefabHash);
            var pickable = prefab != null ? prefab.GetComponent<Pickable>() : null;
            minutes = pickable != null ? pickable.m_respawnTimeMinutes : 0f;
            RespawnMinutesByPrefab[prefabHash] = minutes;
            return minutes;
        }

        private static string FormatOverdue(double minutes) =>
            minutes > 1e9 ? "no pick time recorded" : minutes >= 60 ? $"{minutes / 60:0.0} h" : $"{minutes:0} min";

        // ---- Heartbeat --------------------------------------------------------------------------------------

        /// <summary>With the 5-minute summary.</summary>
        internal static void Heartbeat()
        {
            if (!Log.DebugOn)
            {
                return;
            }
            Guard("heartbeat", () =>
            {
                var drops = (ItemDropsField?.GetValue(null) as List<ItemDrop>)?.Count(d => d != null && InLoadedZone(d.transform.position)) ?? -1;
                var surtlings = Character.GetAllCharacters()
                    .Count(c => c != null && InLoadedZone(c.transform.position) && Utils.GetPrefabName(c.gameObject).StartsWith("Surtling"));
                var water = WaterVolume.Instances.Count(w => w != null && InLoadedZone(w.transform.position));
                var wind = EnvMan.instance != null ? EnvMan.instance.GetWindIntensity().ToString("0.00") : "?";
                var overdue = Picked.Values.Where(p => p.Overdue).GroupBy(p => Observers.PlayerName(p.Owner))
                    .Select(g => $"{g.Key} x{g.Count()}");
                var regrewBy = string.Join(", ", RegrewBy.Select(kv => $"{kv.Key} x{kv.Value}"));
                Log.Debug($"Heartbeat: {WorldTime}; water lookup mask {WaterMask?.ToString() ?? "?"}; water volumes in loaded zones {water}; " +
                          $"wind {wind}; items on the ground in loaded zones {drops}; surtlings in loaded zones {surtlings}; " +
                          $"ships on the server {Ships.Count}; picked plants in view {Picked.Count}, overdue {Picked.Values.Count(p => p.Overdue)} " +
                          $"[{string.Join(", ", overdue)}]; regrew since the last heartbeat {_regrew} ({_regrewOverdue} overdue) [{regrewBy}].");
                _regrew = _regrewOverdue = 0;
                RegrewBy.Clear();
            });
        }
    }
}
