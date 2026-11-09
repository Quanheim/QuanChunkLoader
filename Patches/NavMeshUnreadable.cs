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
    ///
    /// Unity raises the warning inside the build call itself, not while collecting: checked in UnityPlayer.dll (9 Nov
    /// 2026), the function holding the message is reached only from NavMeshBuilder::UpdateNavMeshDataListInternal and
    /// UpdateNavMeshDataAsyncListInternal, never from CollectSourcesInternal.
    ///
    /// Either way it says so once per session in the log, for anyone (or any AI) reading it: that the warning was
    /// seen and how to stop it, or that it's being suppressed and by which setting.
    /// </summary>
    internal static class NavMeshUnreadable
    {
        private const string SettingPath =
            "\"Vanilla default model collider no read access\" in [9 - Known error suppression (personal)] of quandru.QuanChunkLoader.cfg";

        private static volatile bool _seen;
        private static bool _seenReported;
        private static bool _suppressedReported;

        internal static void Apply(Harmony harmony)
        {
            Application.logMessageReceivedThreaded += OnLog;
            try
            {
                var build = AccessTools.Method(typeof(NavMeshBuilder), nameof(NavMeshBuilder.UpdateNavMeshDataAsync),
                    new[] { typeof(NavMeshData), typeof(NavMeshBuildSettings), typeof(List<NavMeshBuildSource>), typeof(Bounds) });
                harmony.Patch(build, prefix: new HarmonyMethod(typeof(NavMeshUnreadable), nameof(Prefix)));
            }
            catch (Exception e)
            {
                Log.Warning($"Couldn't patch the navmesh build, so {SettingPath} has no effect: {e.Message}");
            }
        }

        // __2: the sources list (by position, so a renamed parameter doesn't matter).
        private static void Prefix(List<NavMeshBuildSource> __2)
        {
            if (__2 == null || !Settings.SuppressUnreadableNavMesh.Value)
            {
                return;
            }
            var removed = __2.RemoveAll(s => s.shape == NavMeshBuildSourceShape.Mesh && s.sourceObject is Mesh m && !m.isReadable);
            if (removed > 0 && !_suppressedReported)
            {
                _suppressedReported = true;
                Log.Info("Unity's \"RuntimeNavMeshBuilder: Source mesh ... is skipped because it does not allow read " +
                         "access\" warnings would appear in this log, but QuanChunkLoader is suppressing them because " +
                         $"{SettingPath} is ticked. They're harmless vanilla noise: unreadable vanilla collider models " +
                         "(cart wheels, ship hulls, logs) that the navmesh skips anyway, so pathing is unchanged. " +
                         "Untick it to see them again. Said once per session.");
            }
        }

        // Any thread. Only notes it; the log line is written from the main thread (Tick).
        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!_seen && condition != null &&
                condition.StartsWith("RuntimeNavMeshBuilder: Source mesh", StringComparison.Ordinal) &&
                condition.EndsWith("does not allow read access", StringComparison.Ordinal))
            {
                _seen = true;
            }
        }

        /// <summary>Main thread, every frame: reports the first sighting once, then stops listening.</summary>
        internal static void Tick()
        {
            if (!_seen || _seenReported)
            {
                return;
            }
            _seenReported = true;
            Application.logMessageReceivedThreaded -= OnLog;
            if (Settings.SuppressUnreadableNavMesh.Value)
            {
                Log.Warning("Spotted Unity's \"RuntimeNavMeshBuilder: Source mesh ... does not allow read access\" " +
                            $"warning although {SettingPath} is ticked, so the suppression isn't catching it. Please " +
                            "report it to QuanChunkLoader with this log. The warning itself is harmless.");
            }
            else
            {
                Log.Info("Spotted Unity's \"RuntimeNavMeshBuilder: Source mesh ... is skipped because it does not allow " +
                         "read access\" warning above. It's harmless vanilla noise (unreadable vanilla collider models " +
                         "such as cart wheels, ship hulls and logs, which the navmesh skips anyway), and a chunk-loaded " +
                         $"base logs far more of it. QuanChunkLoader has a fix: tick {SettingPath}. Said once per session.");
            }
        }
    }
}
