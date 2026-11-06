using System;
using System.Collections.Generic;
using HarmonyLib;
using QuanChunkLoader.Core;
using UnityEngine;

namespace QuanChunkLoader.Patches
{
    /// <summary>
    /// Keeps the server's forced-zone instances alive. Each scene pass, ZNetScene.RemoveObjects earmarks the objects
    /// near the reference point with (frame count &amp; 0xFF) and unloads every other instance. Marking ours with the
    /// same value first, in the same frame, keeps them. Unloading a non-persistent object you own deletes it, so
    /// those are always kept while in a forced zone (docs/design.md, "Safety rules").
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "RemoveObjects")]
    internal static class KeepForcedInstances
    {
        private static void Prefix(ZNetScene __instance)
        {
            if (!ServerLoader.Running || Observers.Forced.Count == 0)
            {
                return;
            }
            var mark = (byte)(Time.frameCount & 0xFF);
            foreach (var zdo in GameAccess.Instances(__instance).Keys)
            {
                if (Observers.IsForced(zdo.GetSector()) &&
                    (ObjectKinds.ServerLoads(zdo) || (!zdo.Persistent && zdo.IsOwner())))
                {
                    zdo.TempRemoveEarmark = mark;
                }
            }
        }
    }

    /// <summary>
    /// Unknown-prefab guard. When the server can't create an object because it doesn't know the prefab, vanilla
    /// deletes it for good ("Destroyed invalid prefab ZDO"). In forced zones those objects are taken out of the
    /// scene's list first, so they're left alone (the loader logs them).
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "CreateObjectsSorted")]
    internal static class UnknownPrefabGuardNear
    {
        private static void Prefix(List<ZDO> currentNearObjects) => UnknownPrefabGuard.Strip(currentNearObjects);
    }

    [HarmonyPatch(typeof(ZNetScene), "CreateDistantObjects")]
    internal static class UnknownPrefabGuardDistant
    {
        private static void Prefix(List<ZDO> objects) => UnknownPrefabGuard.Strip(objects);
    }

    internal static class UnknownPrefabGuard
    {
        private static readonly Predicate<ZDO> Unknown = zdo =>
            !zdo.Created && Observers.IsForced(zdo.GetSector()) && ObjectKinds.Of(zdo) == Kind.UnknownPrefab;

        internal static void Strip(List<ZDO> list)
        {
            if (ServerLoader.Running && Observers.Forced.Count > 0)
            {
                list.RemoveAll(Unknown);
            }
        }
    }

    /// <summary>
    /// Edge rule (docs/design.md, "Safety rules"). Building wear and support, crops falling (StaticPhysics) and
    /// SpawnArea only run in the "active" part of a loaded area. On the server, a point in a forced zone counts as
    /// active only when all eight neighbouring zones are forced too, mirroring vanilla's one-zone margin; edge pieces
    /// then keep full support instead of checking it without their neighbours.
    ///
    /// The active-area test is patched inside those three callers only (a scoped flag), because the same test also
    /// decides ownership (ZDOMan.ReleaseNearbyZDOS), which must stay vanilla. OutsideActiveArea itself is too small to
    /// patch safely (it may be inlined), so the flag is read in PointInsideActiveArea.
    /// </summary>
    [HarmonyPatch]
    internal static class EdgeRuleContext
    {
        [ThreadStatic] internal static int Depth;

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.UpdateWear));
            yield return AccessTools.Method(typeof(StaticPhysics), nameof(StaticPhysics.SUpdate));
            yield return AccessTools.Method(typeof(SpawnArea), "UpdateSpawn");
        }

        private static void Prefix() => Depth++;

        private static Exception Finalizer(Exception __exception)
        {
            Depth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "PointInsideActiveArea")]
    internal static class EdgeRule
    {
        private static void Postfix(Vector3 point, ref bool __result)
        {
            if (EdgeRuleContext.Depth == 0 || __result || !ServerLoader.Running || Observers.Forced.Count == 0)
            {
                return;
            }
            // Wear and support, falling crops and ordinary spawner areas: only the inner part of a loaded area.
            // A spawner area a phantom runs: anywhere in a loaded zone (see PhantomContext.SpawnAreaActiveAt).
            if (Observers.IsInterior(ZoneSystem.GetZone(point)) || PhantomContext.SpawnAreaActiveAt(point))
            {
                __result = true;
            }
        }
    }
}
