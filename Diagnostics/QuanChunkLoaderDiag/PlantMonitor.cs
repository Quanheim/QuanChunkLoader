using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuanChunkLoaderDiag
{
    /// <summary>
    /// Watches picked plants (anything with a Pickable that regrows) in forced zones and around each player, and
    /// records who owns the ones that are overdue to regrow. Only the owner's copy of a plant runs the regrow check
    /// (Pickable.UpdateRespawn, every 60 s), so an overdue plant tells us its owner isn't running it.
    /// Reads ZDOs only; never writes.
    /// </summary>
    internal sealed class PlantMonitor
    {
        // The owner checks once a minute, so anything more than this past its regrow time is genuinely stuck.
        private const double OverdueGraceMinutes = 2.0;
        private const int MaxDetailLines = 10;

        private sealed class PlantState
        {
            public string Name;
            public Vector3 Position;
            public long Owner;
            public bool Overdue;
            public double OverdueMinutes;
        }

        private readonly Func<long, string> _ownerName;
        private readonly Dictionary<int, float> _respawnMinutesByPrefab = new Dictionary<int, float>();
        private readonly Dictionary<ZDOID, PlantState> _picked = new Dictionary<ZDOID, PlantState>();
        private readonly List<ZDO> _scratch = new List<ZDO>();
        private int _regrewSinceBeat, _regrewOverdueSinceBeat;
        private readonly Dictionary<string, int> _regrewByOwnerSinceBeat = new Dictionary<string, int>();
        private bool _reportedSetup;

        public PlantMonitor(Func<long, string> ownerName)
        {
            _ownerName = ownerName;
        }

        private static long ServerId => ZDOMan.GetSessionID();

        private static string Where(Vector3 p) => $"({p.x:0},{p.y:0.0},{p.z:0}) zone {ZoneSystem.GetZone(p)}";

        private static string WorldTime => $"world {ZNet.instance.GetTimeSeconds():0}s, {ZNet.instance.GetNrOfPlayers()} player(s)";

        public void Poll()
        {
            if (!_reportedSetup)
            {
                _reportedSetup = true;
                DiagLog.Write("PLANT", GameAccess.VanillaActiveAreaAvailable
                    ? "Plant monitor active: scanning forced zones and each player's area every 30 s."
                    : "Plant monitor active, but the vanilla active-area test wasn't found; 'near owner' will read false for players.");
            }

            var candidates = CollectCandidates();
            var now = ZNet.instance.GetTime().Ticks;
            var peers = ZNet.instance.GetPeers().Where(p => p.m_uid != 0).ToDictionary(p => p.m_uid);

            var seen = new HashSet<ZDOID>();
            var becameOverdue = new List<KeyValuePair<ZDO, PlantState>>();
            var ownerChanged = new Dictionary<string, List<PlantState>>();
            var regrew = new List<PlantState>();

            foreach (var zdo in candidates)
            {
                var respawnMinutes = RespawnMinutes(zdo.GetPrefab());
                if (respawnMinutes <= 0f)
                {
                    continue;
                }
                seen.Add(zdo.m_uid);
                _picked.TryGetValue(zdo.m_uid, out var state);

                if (!zdo.GetBool(ZDOVars.s_picked))
                {
                    if (state != null)
                    {
                        regrew.Add(state);
                        _picked.Remove(zdo.m_uid);
                    }
                    continue;
                }

                var pickedTicks = zdo.GetLong(ZDOVars.s_pickedTime, 0L);
                var minutesSincePicked = pickedTicks > 1 ? TimeSpan.FromTicks(now - pickedTicks).TotalMinutes : double.MaxValue;
                var overdueMinutes = minutesSincePicked - respawnMinutes;
                var overdue = overdueMinutes > OverdueGraceMinutes;
                var owner = zdo.GetOwner();

                if (state == null)
                {
                    state = new PlantState { Name = PrefabName(zdo.GetPrefab()), Owner = owner };
                    _picked[zdo.m_uid] = state;
                }
                else if (state.Owner != owner && state.Overdue)
                {
                    var key = $"{_ownerName(state.Owner)} -> {_ownerName(owner)}";
                    if (!ownerChanged.TryGetValue(key, out var list))
                    {
                        ownerChanged[key] = list = new List<PlantState>();
                    }
                    list.Add(state);
                }
                state.Owner = owner;
                state.Position = zdo.GetPosition();
                state.OverdueMinutes = overdueMinutes;
                if (overdue && !state.Overdue)
                {
                    becameOverdue.Add(new KeyValuePair<ZDO, PlantState>(zdo, state));
                }
                state.Overdue = overdue;
            }

            // Plants that left the scan area are dropped quietly; we only know about what we can see.
            foreach (var id in _picked.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                _picked.Remove(id);
            }

            if (becameOverdue.Count > 0)
            {
                var groups = becameOverdue.GroupBy(p => $"{p.Value.Name}, owner {_ownerName(p.Value.Owner)}, {OwnerHasItLoaded(p.Key, p.Value.Owner, peers)}");
                DiagLog.Write("PLANT", $"{becameOverdue.Count} picked plant(s) now overdue to regrow (more than {OverdueGraceMinutes:0} min past their time): " +
                                       string.Join("; ", groups.Select(g => $"{g.Key} x{g.Count()}")) + $"; {WorldTime}.");
                foreach (var p in becameOverdue.Take(MaxDetailLines))
                {
                    DiagLog.Write("PLANT", $"  {p.Value.Name} at {Where(p.Value.Position)}, {FormatOverdue(p.Value.OverdueMinutes)} overdue, owner {_ownerName(p.Value.Owner)}");
                }
            }
            foreach (var change in ownerChanged)
            {
                DiagLog.Write("PLANT", $"Overdue plants changed owner {change.Key}: {change.Value.Count} plant(s) ({string.Join(", ", change.Value.GroupBy(s => s.Name).Select(g => $"{g.Key} x{g.Count()}"))}); {WorldTime}.");
            }
            if (regrew.Count > 0)
            {
                _regrewSinceBeat += regrew.Count;
                foreach (var g in regrew.GroupBy(s => _ownerName(s.Owner)))
                {
                    _regrewByOwnerSinceBeat.TryGetValue(g.Key, out var n);
                    _regrewByOwnerSinceBeat[g.Key] = n + g.Count();
                }
                var stuck = regrew.Where(s => s.Overdue).ToList();
                _regrewOverdueSinceBeat += stuck.Count;
                if (stuck.Count > 0)
                {
                    DiagLog.Write("PLANT", $"{stuck.Count} overdue plant(s) regrew; owner before regrowth: " +
                                           string.Join(", ", stuck.GroupBy(s => _ownerName(s.Owner)).Select(g => $"{g.Key} x{g.Count()}")) +
                                           $"; longest overdue {FormatOverdue(stuck.Max(s => s.OverdueMinutes))}; {WorldTime}.");
                }
            }
        }

        /// <summary>One line per heartbeat: how many picked plants we see, how many are stuck, and who owns the stuck ones.</summary>
        public void Heartbeat()
        {
            var peers = ZNet.instance.GetPeers().Where(p => p.m_uid != 0).ToDictionary(p => p.m_uid);
            var overdue = _picked.Where(p => p.Value.Overdue).ToList();
            var byOwner = overdue
                .GroupBy(p => $"{_ownerName(p.Value.Owner)} ({OwnerHasItLoaded(ZDOMan.instance.GetZDO(p.Key), p.Value.Owner, peers)})")
                .Select(g => $"{g.Key} x{g.Count()}");
            var regrewBy = string.Join(", ", _regrewByOwnerSinceBeat.Select(kv => $"{kv.Key} x{kv.Value}"));
            DiagLog.Write("PLANT", $"Picked plants in view {_picked.Count}, overdue {overdue.Count} [{string.Join("; ", byOwner)}]; " +
                                   $"regrew since last beat {_regrewSinceBeat} ({_regrewOverdueSinceBeat} of them overdue) [{regrewBy}]; {WorldTime}.");
            _regrewSinceBeat = 0;
            _regrewOverdueSinceBeat = 0;
            _regrewByOwnerSinceBeat.Clear();
        }

        /// <summary>
        /// Whether the owner is actually able to run the regrow check. For the server: it has the plant instantiated.
        /// For a player: the plant is inside the vanilla active area around them (ignoring forced zones).
        /// </summary>
        private static string OwnerHasItLoaded(ZDO zdo, long owner, Dictionary<long, ZNetPeer> peers)
        {
            if (owner == 0)
            {
                return "no owner";
            }
            if (owner == ServerId)
            {
                return zdo != null && ZNetScene.instance.FindInstance(zdo) != null ? "server has it loaded" : "server does NOT have it loaded";
            }
            if (!peers.TryGetValue(owner, out var peer))
            {
                return "owner offline";
            }
            return zdo != null && GameAccess.VanillaInActiveArea(zdo.GetPosition(), peer.GetRefPos())
                ? "near owner"
                : $"owner far away ({Vector3.Distance(zdo?.GetPosition() ?? Vector3.zero, peer.GetRefPos()):0} m)";
        }

        private List<ZDO> CollectCandidates()
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

            var forced = GameAccess.ForcedZones;
            if (forced != null)
            {
                var single = new SimulationDistance(0, 0);
                foreach (var zone in forced)
                {
                    _scratch.Clear();
                    ZDOMan.instance.FindSectorObjects(zone, single, _scratch);
                    Add(_scratch);
                }
            }

            var synced = ZNet.instance.GetSyncedSimulationDistance();
            var near = new SimulationDistance(synced.NearSimulationDistance, 0, synced.IsClassic);
            foreach (var peer in ZNet.instance.GetPeers().Where(p => p.m_uid != 0))
            {
                _scratch.Clear();
                ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(peer.GetRefPos()), near, _scratch);
                Add(_scratch);
            }
            return result;
        }

        /// <summary>Regrow time in minutes for a prefab, or 0 if it isn't a regrowing pickable. Uses the server's prefab values.</summary>
        private float RespawnMinutes(int prefabHash)
        {
            if (_respawnMinutesByPrefab.TryGetValue(prefabHash, out var minutes))
            {
                return minutes;
            }
            var prefab = ZNetScene.instance.GetPrefab(prefabHash);
            var pickable = prefab != null ? prefab.GetComponent<Pickable>() : null;
            minutes = pickable != null ? pickable.m_respawnTimeMinutes : 0f;
            _respawnMinutesByPrefab[prefabHash] = minutes;
            return minutes;
        }

        private static string PrefabName(int prefabHash)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabHash);
            return prefab != null ? prefab.name : $"prefab {prefabHash}";
        }

        private static string FormatOverdue(double minutes) =>
            minutes > 1e9 ? "no pick time recorded" : minutes >= 60 ? $"{minutes / 60:0.0} h" : $"{minutes:0} min";
    }
}
