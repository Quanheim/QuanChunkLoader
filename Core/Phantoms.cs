using System.Collections.Generic;
using UnityEngine;

namespace QuanChunkLoader.Core
{
    /// <summary>
    /// The server's list of Phantom Spawners (docs/design.md, "Quan's Phantom Spawner"). A phantom is active while
    /// it stands in a zone an active observer keeps loaded and wasn't built while only admins could build. The
    /// server writes that onto the phantom (ActiveKey), so every player's hover shows it without asking.
    /// </summary>
    internal static class Phantoms
    {
        internal const string ActiveKey = "QCL_active";

        private sealed class Info
        {
            internal ZDOID Id;
            internal Vector3 Position;
            internal bool Blocked;
            internal bool Active;
        }

        private static readonly Dictionary<ZDOID, Info> All = new Dictionary<ZDOID, Info>();
        private static readonly List<Vector3> ActivePositions = new List<Vector3>();

        internal static int Count => All.Count;

        internal static void Clear()
        {
            All.Clear();
            ActivePositions.Clear();
        }

        /// <summary>A phantom found at server start.</summary>
        internal static void Add(ZDO zdo)
        {
            All[zdo.m_uid] = new Info
            {
                Id = zdo.m_uid,
                Position = zdo.GetPosition(),
                Blocked = zdo.GetBool(Observers.BlockedKey),
                Active = zdo.GetBool(ActiveKey)
            };
        }

        /// <summary>A phantom just built.</summary>
        internal static void Add(ZDO zdo, bool admin, bool blocked)
        {
            Add(zdo);
            All[zdo.m_uid].Blocked = blocked;
            Log.Info($"Phantom spawner built at ({zdo.GetPosition().x:0}, {zdo.GetPosition().z:0}) by " +
                     $"{zdo.GetString(Observers.BuilderKey)}" + (admin ? ", admin-placed" : "") +
                     (blocked ? " (blocked: only admins can build)." : "."));
            Refresh();
        }

        internal static bool Remove(ZDOID id)
        {
            if (!All.TryGetValue(id, out var p))
            {
                return false;
            }
            All.Remove(id);
            Log.Info($"Phantom spawner removed at ({p.Position.x:0}, {p.Position.z:0}).");
            Refresh();
            return true;
        }

        /// <summary>Recomputes which phantoms are active after the loaded zones change, and tells the phantoms.</summary>
        internal static void Refresh()
        {
            ActivePositions.Clear();
            foreach (var p in All.Values)
            {
                var active = !p.Blocked && Observers.IsForced(ZoneSystem.GetZone(p.Position));
                if (active)
                {
                    ActivePositions.Add(p.Position);
                }
                if (active == p.Active)
                {
                    continue;
                }
                p.Active = active;
                var zdo = ZDOMan.instance.GetZDO(p.Id);
                if (zdo != null)
                {
                    Observers.WriteBool(zdo, ActiveKey, active);
                }
                Log.Info($"Phantom spawner at ({p.Position.x:0}, {p.Position.z:0}) is now {(active ? "active" : "dormant")}.");
            }
        }

        /// <summary>Whether an active phantom stands within range of a point (a spawner's own trigger range).</summary>
        internal static bool AnyWithin(Vector3 point, float range)
        {
            foreach (var p in ActivePositions)
            {
                if (Vector3.Distance(p, point) < range)
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool AnyActive => ActivePositions.Count > 0;
    }
}
