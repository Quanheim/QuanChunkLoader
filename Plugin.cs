using BepInEx;
using Jotunn.Utils;

namespace MyValheimPlugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "quandru.MyValheimPlugin";
        public const string PluginName = "MyValheimPlugin";
        public const string PluginVersion = PluginInfo.Version; // generated from <Version> in the .csproj

        private void Awake()
        {
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }
    }
}
