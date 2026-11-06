using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using QuanChunkLoader.Core;

namespace QuanChunkLoader
{
    /// <summary>
    /// Quan's Chunk Loader: server-side chunk loading that yields object ownership to nearby players.
    /// Design: docs/design.md.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // The old chunk loaders patch the same decisions in the opposite way; running alongside one would bring back
    // the ownership bug. Frogger's original and NerdLounge's ChunkLoader Continued share the first GUID.
    [BepInIncompatibility(FroggerGuid)]
    [BepInIncompatibility(ZeeCamelSnakeGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class QuanChunkLoaderPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "quandru.QuanChunkLoader";
        public const string PluginName = "QuanChunkLoader";
        public const string PluginVersion = PluginInfo.Version; // generated from <Version> in the .csproj

        private const string FroggerGuid = "com.Frogger.ChunkLoader";
        private const string ZeeCamelSnakeGuid = "com.zeecamelsnake.ChunkLoaderFix";

        // Shared with the QuanPieces mod by name only, so both mods' pieces land in one hammer tab when both are installed.
        internal const string HammerCategory = "QuanPieces";

        /// <summary>The plugin's own object, for coroutines.</summary>
        internal static QuanChunkLoaderPlugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log.Source = Logger;
            Settings.Bind(Config);

            // Belt and braces: BepInEx should already have refused to load us next to an old loader.
            foreach (var guid in new[] { FroggerGuid, ZeeCamelSnakeGuid })
            {
                if (Chainloader.PluginInfos.ContainsKey(guid))
                {
                    Log.Error($"An old chunk loader ({guid}) is installed. Quan's Chunk Loader stays inactive: remove " +
                              "the old one from the server and every client (see the README, \"Migrating\").");
                    return;
                }
            }

            if (!ApplyPatches())
            {
                // Loading without every scene patch could unload, or fail to protect, what it loads. Stay out.
                Log.Error("Not every patch applied (see above), so chunk loading is off. Check for a game update.");
                return;
            }
            PrefabManager.OnVanillaPrefabsAvailable += AddPieces;
            Settings.ExpireAfterHours.SettingChanged += (_, __) => Observers.ExpiryChanged();
            BuildPermission.Hook();
            ObserverMap.Hook();
            ZoneGrid.Hook(gameObject);
            gameObject.AddComponent<ServerLoader>();

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        /// <summary>
        /// Applies each Harmony patch class on its own, so one that no longer fits the game (after an update)
        /// is logged by name. Returns false if any failed.
        /// </summary>
        private static bool ApplyPatches()
        {
            var allApplied = true;
            var harmony = new Harmony(PluginGuid);
            foreach (var type in typeof(QuanChunkLoaderPlugin).Assembly.GetTypes())
            {
                if (!type.IsDefined(typeof(HarmonyPatch), false))
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    Log.Error($"Couldn't apply {type.Name}: {e}");
                    allApplied = false;
                }
            }
            return allApplied;
        }

        private void AddPieces()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddPieces;
            try
            {
                ObserverPiece.Register();
            }
            catch (System.Exception e)
            {
                Log.Error($"Couldn't add the Chunk Observers: {e}");
            }
            try
            {
                PhantomPiece.Register();
            }
            catch (System.Exception e)
            {
                Log.Error($"Couldn't add the Phantom Spawner: {e}");
            }
        }

        private void Update() => ObserverMap.ClientUpdate();
    }
}
