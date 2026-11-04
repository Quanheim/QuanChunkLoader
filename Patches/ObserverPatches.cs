using HarmonyLib;
using QuanChunkLoader.Core;

namespace QuanChunkLoader.Patches
{
    /// <summary>Observers can't be damaged (docs/design.md, "Pieces"). Removing one with the hammer still works.</summary>
    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    internal static class ObserverTakesNoDamage
    {
        private static bool Prefix(WearNTear __instance) => __instance.GetComponent<ChunkObserver>() == null;
    }

    /// <summary>Registers the mod's routed RPCs once per session, on every machine.</summary>
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class RegisterRpcs
    {
        private static void Postfix() => Observers.RegisterRpcs();
    }
}
