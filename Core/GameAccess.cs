using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace QuanChunkLoader.Core
{
    /// <summary>
    /// Private game members the mod reaches by reflection. Each is listed in docs/valheim-dependencies.md.
    /// Built once; a missing member throws here at startup with its name, rather than failing later in play.
    /// </summary>
    internal static class GameAccess
    {
        internal static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");

        internal static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> ObjectsById =
            AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");

        private static readonly Func<ZNetScene, ZDO, GameObject> CreateObjectCall =
            AccessTools.MethodDelegate<Func<ZNetScene, ZDO, GameObject>>(
                AccessTools.Method(typeof(ZNetScene), "CreateObject", new[] { typeof(ZDO) }));

        private static readonly Func<ZoneSystem, Vector2s, bool> PokeLocalZoneCall =
            AccessTools.MethodDelegate<Func<ZoneSystem, Vector2s, bool>>(
                AccessTools.Method(typeof(ZoneSystem), "PokeLocalZone", new[] { typeof(Vector2s) }));

        /// <summary>Instantiates a ZDO's object, as the scene does for objects near a player. Null if the prefab is unknown.</summary>
        internal static GameObject CreateObject(ZDO zdo) => CreateObjectCall(ZNetScene.instance, zdo);

        /// <summary>Keeps a zone loaded (resetting its time to live), loading it if needed. True when it loaded the zone this call.</summary>
        internal static bool PokeLocalZone(Vector2s zone) => PokeLocalZoneCall(ZoneSystem.instance, zone);
    }
}
