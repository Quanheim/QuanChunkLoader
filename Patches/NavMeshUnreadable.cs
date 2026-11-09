using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace QuanChunkLoader
{
    /// <summary>
    /// Known error suppression: "RuntimeNavMeshBuilder: Source mesh default is skipped because it does not allow read
    /// access". About 800 vanilla models named "default" (cart wheels, ship hulls, Hildir's chests, logs, stones) are
    /// collider meshes Unity can't read, so the navmesh build skips them and warns, every tile rebuild. Pathfinding
    /// hands its collected sources to UpdateNavMeshDataAsync; taking the unreadable ones out first gives the same
    /// navmesh without the warning. Personal: it only changes this machine's log.
    /// </summary>
    internal static class NavMeshUnreadable
    {
        internal static void Apply(Harmony harmony)
        {
            try
            {
                var build = AccessTools.Method(typeof(NavMeshBuilder), nameof(NavMeshBuilder.UpdateNavMeshDataAsync),
                    new[] { typeof(NavMeshData), typeof(NavMeshBuildSettings), typeof(List<NavMeshBuildSource>), typeof(Bounds) });
                harmony.Patch(build, prefix: new HarmonyMethod(typeof(NavMeshUnreadable), nameof(Prefix)));
            }
            catch (Exception e)
            {
                Log.Warning($"Couldn't patch the navmesh build, so \"Vanilla default model collider no read access\" is off: {e.Message}");
            }
        }

        // __2: the sources list (by position, so a renamed parameter doesn't matter).
        private static void Prefix(List<NavMeshBuildSource> __2)
        {
            if (__2 == null || !Settings.SuppressUnreadableNavMesh.Value)
            {
                return;
            }
            __2.RemoveAll(s => s.shape == NavMeshBuildSourceShape.Mesh && s.sourceObject is Mesh m && !m.isReadable);
        }
    }
}
