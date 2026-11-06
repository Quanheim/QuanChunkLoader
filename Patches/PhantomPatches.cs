using System;
using System.Collections.Generic;
using HarmonyLib;
using QuanChunkLoader.Core;
using UnityEngine;

namespace QuanChunkLoader.Patches
{
    /// <summary>
    /// The Phantom Spawner (docs/design.md, "Quan's Phantom Spawner"). While an allowed spawner runs its update on
    /// the server, a scoped flag is set; inside it, Player.IsPlayerInRange counts an active phantom within the
    /// spawner's own range as a player. Nothing global changes: the same check everywhere else stays vanilla.
    ///
    /// The other half (Quandru, 6 Nov 2026): no monster AI runs in loaded zones except what a phantom drives, so a
    /// spawner the server runs in a loaded zone is skipped unless an active phantom is driving it. Without this, a
    /// spawner area with a player somewhere in its trigger range would spawn creatures the server pauses at once,
    /// and, counting only running creatures, keep spawning more.
    /// </summary>
    internal static class PhantomContext
    {
        [ThreadStatic] internal static bool Allowed;   // this spawner may be run by a phantom
        [ThreadStatic] internal static bool Triggered; // a phantom made its range check pass this update
        [ThreadStatic] internal static float SpawnAreaRange; // the spawner area being updated, else 0

        private static readonly char[] Comma = { ',' };
        private static string _listText;
        private static HashSet<string> _list = new HashSet<string>();

        internal static bool Enter(bool kindAllowed)
        {
            Allowed = kindAllowed && ServerLoader.Running && Phantoms.AnyActive;
            Triggered = false;
            return Allowed;
        }

        internal static void Exit()
        {
            Allowed = false;
            Triggered = false;
            SpawnAreaRange = 0f;
        }

        /// <summary>
        /// A spawner area a phantom runs counts its point as active anywhere in a loaded zone, not only where all
        /// eight neighbours are loaded (Quandru, 6 Nov 2026), so phantoms work with a single observer too.
        /// </summary>
        internal static bool SpawnAreaActiveAt(Vector3 point) =>
            Allowed && SpawnAreaRange > 0f && Observers.IsForced(ZoneSystem.GetZone(point)) &&
            Phantoms.AnyWithin(point, SpawnAreaRange);

        /// <summary>
        /// Whether the server should skip this spawner's update: it's in a loaded zone, no phantom drives it, and (in
        /// a local game) the host isn't genuinely near it either. Players' own games and spawners outside loaded
        /// zones are untouched.
        /// </summary>
        internal static bool ServerSkips(Vector3 position, float range)
        {
            if (!ServerLoader.Running || Observers.Forced.Count == 0 || !Observers.IsForced(ZoneSystem.GetZone(position)))
            {
                return false;
            }
            if (Allowed && Phantoms.AnyWithin(position, range))
            {
                return false;
            }
            return ZNet.instance.IsDedicated() || !ZNetScene.InActiveArea(position, ZNet.instance.GetReferencePosition());
        }

        internal static bool CreatureSpawnerAllowed(CreatureSpawner spawner)
        {
            var text = Settings.PhantomCreatureSpawners.Value ?? "";
            if (text != _listText)
            {
                _listText = text;
                _list = new HashSet<string>();
                foreach (var name in text.Split(Comma, StringSplitOptions.RemoveEmptyEntries))
                {
                    _list.Add(name.Trim());
                }
            }
            return _list.Contains(Utils.GetPrefabName(spawner.gameObject));
        }
    }

    [HarmonyPatch(typeof(CreatureSpawner), "UpdateSpawner")]
    internal static class PhantomCreatureSpawnerContext
    {
        private static bool Prefix(CreatureSpawner __instance)
        {
            PhantomContext.Enter(PhantomContext.CreatureSpawnerAllowed(__instance));
            return !PhantomContext.ServerSkips(__instance.transform.position, __instance.m_triggerDistance);
        }

        private static Exception Finalizer(Exception __exception)
        {
            PhantomContext.Exit();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SpawnArea), "UpdateSpawn")]
    internal static class PhantomSpawnAreaContext
    {
        private static bool Prefix(SpawnArea __instance)
        {
            if (PhantomContext.Enter(Settings.PhantomSpawnAreas.Value))
            {
                PhantomContext.SpawnAreaRange = __instance.m_triggerDistance;
            }
            return !PhantomContext.ServerSkips(__instance.transform.position, __instance.m_triggerDistance);
        }

        private static Exception Finalizer(Exception __exception)
        {
            PhantomContext.Exit();
            return __exception;
        }
    }

    /// <summary>Both range checks the spawners use: the plain one and the noise one.</summary>
    [HarmonyPatch]
    internal static class PhantomCountsAsPlayer
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Player), nameof(Player.IsPlayerInRange), new[] { typeof(Vector3), typeof(float) });
            yield return AccessTools.Method(typeof(Player), nameof(Player.IsPlayerInRange), new[] { typeof(Vector3), typeof(float), typeof(float) });
        }

        private static void Postfix(Vector3 point, float range, ref bool __result)
        {
            if (!__result && PhantomContext.Allowed && Phantoms.AnyWithin(point, range))
            {
                __result = true;
                PhantomContext.Triggered = true;
            }
        }
    }

    /// <summary>
    /// A creature a phantom-run spawner makes is marked, so the server keeps running it in the loaded zone (wild
    /// creatures otherwise aren't loaded there; see ObjectKinds.ServerLoads).
    /// </summary>
    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class MarkPhantomSpawned
    {
        private static void Postfix(Character __instance)
        {
            if (!PhantomContext.Triggered)
            {
                return;
            }
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null)
            {
                zdo.Set(ObjectKinds.PhantomSpawnedKey, true);
                Log.Debug($"Phantom-run spawner made {Utils.GetPrefabName(__instance.gameObject)} {zdo.m_uid}.");
            }
        }
    }
}
