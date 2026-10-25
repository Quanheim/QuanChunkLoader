using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuanChunkLoaderDiag
{
    /// <summary>Polls world state on the server and logs changes. Never writes to the game.</summary>
    internal sealed class WorldMonitor
    {
        private const int MaxDetailLinesPerChange = 15;
        private const float ShipMoveThreshold = 0.25f;

        private sealed class Watched
        {
            public string Category;
            public string Name;
            public Vector3 Position;
            public long Owner;
        }

        private sealed class ShipState
        {
            public long Owner;
            public float Height;
            public bool GameSeesWater;
            public bool InForcedZone;
            public bool BuoyancyOn;
        }

        private readonly Dictionary<long, string> _peers = new Dictionary<long, string>();
        private readonly Dictionary<long, string> _knownNames = new Dictionary<long, string>();
        private readonly Dictionary<ZDOID, Watched> _watched = new Dictionary<ZDOID, Watched>();
        private readonly Dictionary<int, string> _categoryCache = new Dictionary<int, string>();
        private readonly Dictionary<ZDOID, string> _loaderStates = new Dictionary<ZDOID, string>();
        private readonly Dictionary<ZDOID, ShipState> _ships = new Dictionary<ZDOID, ShipState>();
        private HashSet<Vector2s> _lastForced = new HashSet<Vector2s>();
        private int? _lastWaterMask;
        private bool _firstOwnershipPass = true;

        private static long ServerId => ZDOMan.GetSessionID();

        private HashSet<Vector2s> CurrentForced => GameAccess.ForcedZones ?? new HashSet<Vector2s>();

        internal string OwnerName(long owner)
        {
            if (owner == 0)
            {
                return "none";
            }
            if (owner == ServerId)
            {
                return "SERVER";
            }
            if (_peers.TryGetValue(owner, out var name))
            {
                return name;
            }
            return _knownNames.TryGetValue(owner, out var known) ? $"{known} (offline)" : $"unknown player ({owner})";
        }

        private static string Where(Vector3 p) => $"({p.x:0},{p.y:0.0},{p.z:0}) zone {ZoneSystem.GetZone(p)}";

        private static string WorldTime => $"world {ZNet.instance.GetTimeSeconds():0}s, {ZNet.instance.GetNrOfPlayers()} player(s)";

        public void PollPeers()
        {
            var current = ZNet.instance.GetPeers().Where(p => p.m_uid != 0).ToList();
            foreach (var peer in current.Where(p => !_peers.ContainsKey(p.m_uid)))
            {
                DiagLog.Write("PLAYER", $"Connected: {peer.m_playerName} ({peer.m_uid}) at {Where(peer.GetRefPos())}; {WorldTime}.");
            }
            var gone = _peers.Keys.Where(uid => current.All(p => p.m_uid != uid)).ToList();
            foreach (var uid in gone)
            {
                DiagLog.Write("PLAYER", $"Disconnected: {_peers[uid]} ({uid}); {WorldTime}.");
                _peers.Remove(uid);
            }
            foreach (var peer in current)
            {
                // Names can arrive a moment after the connection, so keep refreshing them.
                var name = string.IsNullOrEmpty(peer.m_playerName) ? $"player({peer.m_uid})" : peer.m_playerName;
                _peers[peer.m_uid] = name;
                _knownNames[peer.m_uid] = name;
            }
        }

        public void PollLoaders()
        {
            var forced = CurrentForced;
            if (!forced.SetEquals(_lastForced))
            {
                var zones = string.Join(" ", forced.OrderBy(z => z.x).ThenBy(z => z.y).Select(z => z.ToString()));
                DiagLog.Write("ZONES", $"Forced zones changed: {forced.Count} zone(s) [{zones}]; {WorldTime}.");
                _lastForced = new HashSet<Vector2s>(forced);
            }

            var loaders = GameAccess.OldLoaders;
            if (loaders == null)
            {
                return;
            }
            var seen = new HashSet<ZDOID>();
            foreach (var zdo in loaders.Where(z => z != null && z.IsValid()))
            {
                seen.Add(zdo.m_uid);
                var state = zdo.GetInt(ZDOVars.s_state, 1) == 1 ? "ON" : "OFF";
                var summary = $"{state}, fuel {zdo.GetFloat(ZDOVars.s_fuel, 0f):0.0}, owner {OwnerName(zdo.GetOwner())}";
                if (!_loaderStates.TryGetValue(zdo.m_uid, out var previous) || previous != summary)
                {
                    DiagLog.Write("LOADER", $"Loader {zdo.m_uid} at {Where(zdo.GetPosition())}: {summary} (was {previous ?? "unseen"}); {WorldTime}.");
                    _loaderStates[zdo.m_uid] = summary;
                }
            }
            foreach (var id in _loaderStates.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                DiagLog.Write("LOADER", $"Loader {id} no longer listed (removed or unloaded).");
                _loaderStates.Remove(id);
            }
        }

        public void PollOwnership()
        {
            var instances = GameAccess.SceneInstances;
            var forced = CurrentForced;
            if (instances == null)
            {
                return;
            }

            var seen = new HashSet<ZDOID>();
            var changes = new Dictionary<string, List<Watched>>();
            var appeared = new List<Watched>();

            foreach (var pair in instances)
            {
                var zdo = pair.Key;
                var view = pair.Value;
                if (zdo == null || view == null)
                {
                    continue;
                }
                var position = zdo.GetPosition();
                if (!forced.Contains(ZoneSystem.GetZone(position)))
                {
                    continue;
                }
                var category = CategoryOf(view);
                if (category == null)
                {
                    continue;
                }

                seen.Add(zdo.m_uid);
                var owner = zdo.GetOwner();
                if (!_watched.TryGetValue(zdo.m_uid, out var watched))
                {
                    watched = new Watched { Category = category, Name = Utils.GetPrefabName(view.gameObject), Position = position, Owner = owner };
                    _watched[zdo.m_uid] = watched;
                    appeared.Add(watched);
                    continue;
                }
                watched.Position = position;
                if (watched.Owner != owner)
                {
                    var key = $"{OwnerName(watched.Owner)} -> {OwnerName(owner)}";
                    if (!changes.TryGetValue(key, out var list))
                    {
                        changes[key] = list = new List<Watched>();
                    }
                    list.Add(watched);
                    watched.Owner = owner;
                }
            }

            var vanished = _watched.Keys.Where(id => !seen.Contains(id)).ToList();
            foreach (var id in vanished)
            {
                _watched.Remove(id);
            }

            if (appeared.Count > 0)
            {
                var label = _firstOwnershipPass ? "Now tracking" : "Started tracking";
                DiagLog.Write("OWNER", $"{label} {appeared.Count} object(s) in forced zones: {CountByCategory(appeared)}; owners: {CountByOwner(appeared)}; {WorldTime}.");
                _firstOwnershipPass = false;
            }
            if (vanished.Count > 0)
            {
                DiagLog.Write("OWNER", $"Stopped tracking {vanished.Count} object(s) (destroyed, unloaded or left the forced zones).");
            }
            foreach (var change in changes)
            {
                DiagLog.Write("OWNER", $"{change.Key}: {change.Value.Count} object(s): {CountByCategory(change.Value)}; {WorldTime}.");
                // Ships, spawners and tames always get their own line; other categories are sampled.
                var details = change.Value.Where(IsAlwaysDetailed)
                    .Concat(change.Value.Where(w => !IsAlwaysDetailed(w)).Take(MaxDetailLinesPerChange));
                foreach (var w in details)
                {
                    DiagLog.Write("OWNER", $"  {w.Category} {w.Name} at {Where(w.Position)}");
                }
            }
        }

        private static bool IsAlwaysDetailed(Watched w) =>
            w.Category == "Ship" || w.Category == "SpawnArea" || w.Category == "CreatureSpawner" || w.Category == "Tame";

        private string CategoryOf(ZNetView view)
        {
            var id = view.GetInstanceID();
            if (_categoryCache.TryGetValue(id, out var cached))
            {
                return cached;
            }
            if (_categoryCache.Count > 50000)
            {
                _categoryCache.Clear();
            }

            string category = null;
            if (view.GetComponent<Ship>()) category = "Ship";
            else if (view.GetComponentInChildren<SpawnArea>()) category = "SpawnArea";
            else if (view.GetComponentInChildren<CreatureSpawner>()) category = "CreatureSpawner";
            else if (view.GetComponent<Smelter>()) category = "Smelter";
            else if (view.GetComponent<Fermenter>()) category = "Fermenter";
            else if (view.GetComponent<Beehive>()) category = "Beehive";
            else if (view.GetComponent<CookingStation>()) category = "CookingStation";
            else if (view.GetComponent<Container>()) category = "Container";
            else
            {
                var character = view.GetComponent<Character>();
                if (character != null && !character.IsPlayer() && character.IsTamed()) category = "Tame";
            }
            _categoryCache[id] = category;
            return category;
        }

        private static string CountByCategory(IEnumerable<Watched> items) =>
            string.Join(", ", items.GroupBy(w => w.Category).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));

        private string CountByOwner(IEnumerable<Watched> items) =>
            string.Join(", ", items.GroupBy(w => w.Owner).Select(g => $"{OwnerName(g.Key)} {g.Count()}"));

        public void PollShipsAndWater()
        {
            var mask = GameAccess.WaterMask;
            if (mask != _lastWaterMask)
            {
                DiagLog.Write("WATER", $"Water mask {_lastWaterMask?.ToString() ?? "unknown"} -> {mask?.ToString() ?? "unreadable"} (correct value {GameAccess.ExpectedWaterMask}); {WorldTime}.");
                _lastWaterMask = mask;
            }

            var forced = CurrentForced;
            var seen = new HashSet<ZDOID>();
            foreach (var ship in Ship.Instances.OfType<Ship>())
            {
                var view = ship.GetComponent<ZNetView>();
                var zdo = view != null ? view.GetZDO() : null;
                var body = ship.GetComponent<Rigidbody>();
                if (zdo == null || body == null)
                {
                    continue;
                }
                seen.Add(zdo.m_uid);

                var center = body.worldCenterOfMass;
                var gameLevel = GameAccess.GameWaterLevel(center);
                var trueLevel = GameAccess.TrueWaterLevel(center);
                var probe = GameAccess.ProbeBuoyancy(ship, body);
                var current = new ShipState
                {
                    Owner = zdo.GetOwner(),
                    Height = center.y,
                    GameSeesWater = gameLevel > -9999f,
                    InForcedZone = forced.Contains(ZoneSystem.GetZone(center)),
                    BuoyancyOn = probe.Applies,
                };

                _ships.TryGetValue(zdo.m_uid, out var previous);
                string reason = null;
                if (previous == null) reason = "first seen";
                else if (previous.Owner != current.Owner) reason = "owner changed";
                else if (previous.GameSeesWater != current.GameSeesWater) reason = "game water lookup changed";
                else if (previous.InForcedZone != current.InForcedZone) reason = "forced-zone status changed";
                else if (previous.BuoyancyOn != current.BuoyancyOn) reason = "buoyancy verdict changed";
                else if (Mathf.Abs(previous.Height - current.Height) > ShipMoveThreshold) reason = "height changed";

                if (reason != null)
                {
                    DiagLog.Write("SHIP", $"{Utils.GetPrefabName(ship.gameObject)} {zdo.m_uid} ({reason}): owner {OwnerName(current.Owner)}, " +
                                          $"centre of mass y {center.y:0.00}, game water {GameAccess.FormatLevel(gameLevel)}, " +
                                          $"true water {GameAccess.FormatLevel(trueLevel)}, vertical speed {body.linearVelocity.y:0.00}, " +
                                          $"kinematic {body.isKinematic}, forced zone {current.InForcedZone}, at {Where(center)}; {WorldTime}.");
                    DiagLog.Write("SHIP", $"  {probe.Summary}");
                    _ships[zdo.m_uid] = current;
                }
            }
            foreach (var id in _ships.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                DiagLog.Write("SHIP", $"Ship {id} no longer instantiated on the server.");
                _ships.Remove(id);
            }
        }

        public void Heartbeat()
        {
            var forced = CurrentForced;
            var drops = GameAccess.ItemDrops?.Count(d => d != null && forced.Contains(ZoneSystem.GetZone(d.transform.position))) ?? -1;
            var surtlings = Character.GetAllCharacters()
                .Count(c => c != null && forced.Contains(ZoneSystem.GetZone(c.transform.position)) && Utils.GetPrefabName(c.gameObject).StartsWith("Surtling"));
            var waterVolumes = WaterVolume.Instances.Count(w => w != null && forced.Contains(ZoneSystem.GetZone(w.transform.position)));
            DiagLog.Write("BEAT", $"{WorldTime}; forced zones {forced.Count}; water mask {GameAccess.WaterMask?.ToString() ?? "?"}; " +
                                  $"water volumes in forced zones {waterVolumes}; wind {(EnvMan.instance != null ? EnvMan.instance.GetWindIntensity().ToString("0.00") : "no EnvMan")}; item drops in forced zones {drops}; surtlings in forced zones {surtlings}; " +
                                  $"tracked objects {_watched.Count} ({CountByOwner(_watched.Values)}).");
        }
    }
}
