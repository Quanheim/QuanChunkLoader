using System;
using System.Collections;
using HarmonyLib;
using QuanChunkLoader.Core;
using UnityEngine;

namespace QuanChunkLoader.Patches
{
    /// <summary>
    /// Despawn protection (docs/design.md, "Despawn protection"). Vanilla destroys an item that has been on the
    /// ground for an hour of world time unless it's in a base, near a player or in tar. In loaded zones on the
    /// server, items near a production station or an active Phantom Spawner are kept until someone collects them.
    /// </summary>
    [HarmonyPatch(typeof(ItemDrop), "TimedDestruction")]
    internal static class DespawnProtection
    {
        private const float NearStation = 8f;
        private const float NearPhantom = 40f;

        private static bool Prefix(ItemDrop __instance) => !Protected(__instance.transform.position);

        private static bool Protected(Vector3 pos)
        {
            if (!Settings.DespawnProtection.Value || !ServerLoader.Running || !Observers.IsForced(ZoneSystem.GetZone(pos)))
            {
                return false;
            }
            foreach (var station in ServerLoader.Stations)
            {
                if (Vector3.Distance(station, pos) < NearStation)
                {
                    return true;
                }
            }
            return Phantoms.AnyWithin(pos, NearPhantom);
        }
    }

    /// <summary>
    /// Boats at the edge (docs/design.md, "Boats at the edge"). A ship averages five water samples, and a sample in
    /// a zone that isn't loaded reads -10000, which sinks it. While a ship in a loaded zone runs its physics on the
    /// server, such a reading is replaced with the ship's own centre sample. A -10000 over loaded land is real ("no
    /// water here") and is left alone, so beached ships behave as in vanilla.
    /// </summary>
    internal static class BoatContext
    {
        [ThreadStatic] internal static bool Active;
        [ThreadStatic] internal static int Samples;
        [ThreadStatic] internal static float Centre;
        internal const float NoWater = -10000f;
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class BoatContextScope
    {
        private static void Prefix(Ship __instance)
        {
            BoatContext.Active = ServerLoader.Running && Observers.Forced.Count > 0 &&
                                 Observers.IsForced(ZoneSystem.GetZone(__instance.transform.position));
            BoatContext.Samples = 0;
            BoatContext.Centre = BoatContext.NoWater;
        }

        private static Exception Finalizer(Exception __exception)
        {
            BoatContext.Active = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Floating), nameof(Floating.GetWaterLevel), new[] { typeof(Vector3), typeof(WaterVolume) },
        new[] { ArgumentType.Normal, ArgumentType.Ref })]
    internal static class BoatIgnoresUnloadedWater
    {
        private static void Postfix(Vector3 p, ref float __result)
        {
            if (!BoatContext.Active)
            {
                return;
            }
            // Ship.CustomFixedUpdate samples the centre first, then the four sides.
            if (BoatContext.Samples++ == 0)
            {
                BoatContext.Centre = __result;
                return;
            }
            if (__result <= BoatContext.NoWater + 1f && BoatContext.Centre > BoatContext.NoWater + 1f &&
                !ZoneSystem.instance.IsZoneLoaded(ZoneSystem.GetZone(p)))
            {
                __result = BoatContext.Centre;
            }
        }
    }

    /// <summary>
    /// Defensive patch (docs/design.md, "Defensive patches"). Picking reads Player.m_localPlayer, which a dedicated
    /// server doesn't have, so a pick sent to a server-owned plant fails. The server hands the plant to the player
    /// who picked it and sends the pick on to them once they own it.
    /// </summary>
    [HarmonyPatch(typeof(Pickable), "RPC_Pick")]
    internal static class PickOnServerOwned
    {
        private static bool Prefix(Pickable __instance, long sender, int bonus)
        {
            if (Player.m_localPlayer != null || sender == ZDOMan.GetSessionID())
            {
                return true;
            }
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner() || __instance.GetPicked())
            {
                return true;
            }
            var zdo = nview.GetZDO();
            zdo.SetOwner(sender);
            Log.Debug($"Pick on server-owned {Utils.GetPrefabName(__instance.gameObject)} {zdo.m_uid}: handed to {Observers.PlayerName(sender)}.");
            QuanChunkLoaderPlugin.Instance.StartCoroutine(ResendPick(zdo.m_uid, sender, bonus));
            return false;
        }

        // The new owner must have the ownership change before the pick arrives, or they ignore it.
        private static IEnumerator ResendPick(ZDOID id, long picker, int bonus)
        {
            yield return new WaitForSeconds(0.5f);
            var zdo = ZDOMan.instance?.GetZDO(id);
            if (zdo != null && zdo.GetOwner() == picker)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(picker, id, "RPC_Pick", bonus);
            }
        }
    }

    /// <summary>
    /// Defensive patch: the sail's "changed position" effect reads Player.m_localPlayer while someone steers, on every
    /// machine with the ship loaded. A dedicated server has no local player, so it skips just that effect.
    /// </summary>
    [HarmonyPatch(typeof(Ship), "UpdateSailSize")]
    internal static class SailOnServer
    {
        private static void Prefix(ref bool haveControllingPlayer)
        {
            if (Player.m_localPlayer == null)
            {
                haveControllingPlayer = false;
            }
        }
    }
}
