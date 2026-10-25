using System.Linq;
using BepInEx.Bootstrap;
using UnityEngine;

namespace QuanChunkLoaderDiag
{
    /// <summary>Written once per world load: environment, water mask, spawner prefabs, old loader prefab.</summary>
    internal static class StartupReport
    {
        private const string OldLoaderPrefab = "ChunkLoader_stone";

        public static void Write()
        {
            DiagLog.Write("START", $"{Plugin.PluginName} {PluginInfo.Version} on Valheim {Version.GetVersionString()}, dedicated server, {Chainloader.PluginInfos.Count} BepInEx plugins.");
            foreach (var info in Chainloader.PluginInfos.Values.OrderBy(p => p.Metadata.Name))
            {
                DiagLog.Write("PLUGIN", $"{info.Metadata.Name} {info.Metadata.Version} ({info.Metadata.GUID})");
            }

            DiagLog.Write("WATER", $"Water mask at world load: {GameAccess.WaterMask?.ToString() ?? "unreadable"} (a correctly initialised mask is {GameAccess.ExpectedWaterMask}).");

            WriteSpawners();
            WriteConverters();
            WriteOldLoaderPrefab();
        }

        private static void WriteSpawners()
        {
            var scene = ZNetScene.instance;
            int areas = 0, creatureSpawners = 0;
            foreach (var prefab in scene.m_prefabs.Where(p => p != null))
            {
                foreach (var area in prefab.GetComponentsInChildren<SpawnArea>(true))
                {
                    areas++;
                    var spawns = string.Join(", ", area.m_prefabs.Where(s => s?.m_prefab != null).Select(s => s.m_prefab.name));
                    if (IsOfInterest(prefab.name, spawns))
                    {
                        DiagLog.Write("SPAWNER", $"{prefab.name}: SpawnArea, trigger {area.m_triggerDistance} m, every {area.m_spawnIntervalSec} s, " +
                                                 $"radius {area.m_spawnRadius} m, max near {area.m_maxNear} within {area.m_nearRadius} m, " +
                                                 $"max total {area.m_maxTotal} within {area.m_farRadius} m, spawns [{spawns}]. {PlayerBaseNote(prefab)}");
                    }
                }
                foreach (var spawner in prefab.GetComponentsInChildren<CreatureSpawner>(true))
                {
                    creatureSpawners++;
                    var spawns = spawner.m_creaturePrefab != null ? spawner.m_creaturePrefab.name : "(group)";
                    if (IsOfInterest(prefab.name, spawns))
                    {
                        DiagLog.Write("SPAWNER", $"{prefab.name}: CreatureSpawner, trigger {spawner.m_triggerDistance} m (noise {spawner.m_triggerNoise}), " +
                                                 $"respawn {spawner.m_respawnTimeMinuts} min (world time), spawnInPlayerBase={spawner.m_spawnInPlayerBase}, " +
                                                 $"day={spawner.m_spawnAtDay} night={spawner.m_spawnAtNight}, spawns [{spawns}]. {PlayerBaseNote(prefab)}");
                    }
                }
            }
            DiagLog.Write("SPAWNER", $"Scanned {scene.m_prefabs.Count} prefabs: {areas} SpawnArea and {creatureSpawners} CreatureSpawner components in total; only surtling/fire/geyser ones are listed.");
        }

        /// <summary>Every smelter-type station as the server sees it, after other mods (e.g. Conversion Size &amp; Speed) have applied.</summary>
        private static void WriteConverters()
        {
            foreach (var prefab in ZNetScene.instance.m_prefabs.Where(p => p != null))
            {
                var smelter = prefab.GetComponent<Smelter>();
                if (smelter == null)
                {
                    continue;
                }
                var conversions = string.Join(", ", smelter.m_conversion.Where(c => c?.m_from != null && c.m_to != null)
                    .Select(c => $"{c.m_from.name}->{c.m_to.name}"));
                DiagLog.Write("CONVERTER", $"{prefab.name}: max input {smelter.m_maxOre}, max fuel {smelter.m_maxFuel}" +
                                           (smelter.m_maxFuel > 0 ? $" ({(smelter.m_fuelItem != null ? smelter.m_fuelItem.name : "?")} x{smelter.m_fuelPerProduct} per product)" : "") +
                                           $", {smelter.m_secPerProduct} s per product, stacks output {smelter.m_spawnStack}, wind-powered {smelter.m_windmill != null}, " +
                                           $"needs roof {smelter.m_requiresRoof}; [{conversions}]");
            }
        }

        private static bool IsOfInterest(string prefabName, string spawns)
        {
            var text = (prefabName + " " + spawns).ToLowerInvariant();
            return text.Contains("surtling") || text.Contains("fire") || text.Contains("geyser");
        }

        private static string PlayerBaseNote(GameObject prefab)
        {
            var bases = prefab.GetComponentsInChildren<EffectArea>(true).Where(e => (e.m_type & EffectArea.Type.PlayerBase) != 0).ToList();
            return bases.Count == 0 ? "No PlayerBase area." : $"Has {bases.Count} PlayerBase area(s).";
        }

        private static void WriteOldLoaderPrefab()
        {
            var prefab = ZNetScene.instance.GetPrefab(OldLoaderPrefab);
            if (prefab == null)
            {
                DiagLog.Write("LOADER", $"Prefab {OldLoaderPrefab} not registered.");
                return;
            }
            var components = prefab.GetComponentsInChildren<Component>(true)
                .Where(c => c != null)
                .Select(c => c.GetType().Name)
                .Distinct()
                .OrderBy(n => n);
            DiagLog.Write("LOADER", $"Prefab {OldLoaderPrefab} components: {string.Join(", ", components)}");
            foreach (var area in prefab.GetComponentsInChildren<EffectArea>(true))
            {
                DiagLog.Write("LOADER", $"Prefab {OldLoaderPrefab} has EffectArea on '{area.name}': type {area.m_type}" +
                                        ((area.m_type & EffectArea.Type.PlayerBase) != 0 ? " (counts as a player base, like a workbench)" : ""));
            }
        }
    }
}
