using BepInEx;
using Jotunn.Utils;

namespace QuanChunkLoader
{
    /// <summary>
    /// Quan's Chunk Loader: server-side chunk loading that yields object ownership to nearby players.
    /// Design: docs/design.md.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class QuanChunkLoaderPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "quandru.QuanChunkLoader";
        public const string PluginName = "QuanChunkLoader";
        public const string PluginVersion = PluginInfo.Version; // generated from <Version> in the .csproj

        // Shared with the QuanPieces mod by name only, so both mods' pieces land in one hammer tab when both are installed.
        internal const string HammerCategory = "QuanPieces";

        private void Awake()
        {
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }
    }
}
