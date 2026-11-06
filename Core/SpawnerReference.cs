using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace QuanChunkLoader.Core
{
    /// <summary>
    /// Writes a reference list of every spawner in the game, including other mods', next to the config file, so
    /// admins can copy names into "Creature spawners" without looking them up. BepInEx rewrites the .cfg itself and
    /// keeps only settings and their descriptions, so the list lives in its own file; the setting's description
    /// points to it. Rewritten only when its contents change.
    /// </summary>
    internal static class SpawnerReference
    {
        internal const string FileName = "quandru.QuanChunkLoader.spawners.txt";

        internal static void Write()
        {
            try
            {
                var scene = ZNetScene.instance;
                if (scene == null)
                {
                    return;
                }
                var allowed = (Settings.PhantomCreatureSpawners.Value ?? "")
                    .Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

                var text = new StringBuilder();
                text.AppendLine("Quan's Chunk Loader: every spawner in this game, for the Phantom Spawner settings.");
                text.AppendLine("Written by the mod each time the game starts; edits to this file are overwritten.");
                text.AppendLine();
                text.AppendLine("Creature spawners: copy names into \"Creature spawners\" (section 4 - Phantom spawner),");
                text.AppendLine("comma-separated. Anything added runs its monsters on the server while nobody is near,");
                text.AppendLine("and they can attack the base. [in use] marks names already in your list.");
                text.AppendLine();
                text.AppendLine("Name | Spawns | Respawn (world minutes) | Trigger range (m)");

                var spawners = scene.m_prefabs
                    .Where(p => p != null && p.GetComponent<CreatureSpawner>() != null)
                    .OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase);
                foreach (var prefab in spawners)
                {
                    var s = prefab.GetComponent<CreatureSpawner>();
                    var creature = s.m_creaturePrefab != null ? s.m_creaturePrefab.name : "nothing";
                    text.Append(prefab.name).Append(" | ").Append(creature).Append(" | ")
                        .Append(s.m_respawnTimeMinuts > 0 ? s.m_respawnTimeMinuts.ToString("0.#") : "once").Append(" | ")
                        .Append(s.m_triggerDistance.ToString("0"));
                    if (allowed.Contains(prefab.name))
                    {
                        text.Append("  [in use]");
                    }
                    text.AppendLine();
                }

                text.AppendLine();
                text.AppendLine("Spawner areas (nests, bone piles): these aren't chosen by name. The \"Spawner areas\"");
                text.AppendLine("setting turns all of them on or off together. Listed so you know what it covers.");
                text.AppendLine();
                text.AppendLine("Name | Spawns");
                var areas = scene.m_prefabs
                    .Where(p => p != null && p.GetComponent<SpawnArea>() != null)
                    .OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase);
                foreach (var prefab in areas)
                {
                    var creatures = prefab.GetComponent<SpawnArea>().m_prefabs
                        .Where(d => d != null && d.m_prefab != null).Select(d => d.m_prefab.name).Distinct();
                    text.Append(prefab.name).Append(" | ").AppendLine(string.Join(", ", creatures));
                }

                var path = Path.Combine(Paths.ConfigPath, FileName);
                var content = text.ToString();
                if (!File.Exists(path) || File.ReadAllText(path) != content)
                {
                    File.WriteAllText(path, content);
                    Log.Info($"Wrote the spawner reference list to BepInEx/config/{FileName}.");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Couldn't write the spawner reference list: {e.Message}");
            }
        }
    }

    /// <summary>Writes the spawner list once the game's prefabs, and every mod's, are registered.</summary>
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class WriteSpawnerReference
    {
        private static void Postfix() => SpawnerReference.Write();
    }
}
