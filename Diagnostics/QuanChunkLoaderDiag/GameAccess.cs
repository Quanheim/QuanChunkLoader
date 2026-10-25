using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace QuanChunkLoaderDiag
{
    /// <summary>
    /// Read-only access to game and old-mod state. Every member here only reads; nothing sets a field,
    /// calls a mutating method, or touches the water mask initialiser (Floating.GetLiquidLevel).
    /// </summary>
    internal static class GameAccess
    {
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo WaterMaskField = typeof(Floating).GetField("s_waterVolumeMask", AnyStatic);
        private static readonly FieldInfo SceneInstancesField = typeof(ZNetScene).GetField("m_instances", AnyInstance);
        private static readonly FieldInfo ItemDropInstancesField = typeof(ItemDrop).GetField("s_instances", AnyStatic);

        // The private vanilla test behind ZNetScene.InActiveArea. The old mod postfixes InActiveArea itself, so calling
        // this one gives the unmodified answer: would vanilla consider this point loaded for a player standing there?
        private static readonly MethodInfo PointInsideActiveAreaMethod =
            typeof(ZNetScene).GetMethod("PointInsideActiveArea", AnyStatic, null, new[] { typeof(Vector2s), typeof(Vector3) }, null);

        public static bool VanillaActiveAreaAvailable => PointInsideActiveAreaMethod != null;

        /// <summary>Vanilla active-area test, ignoring forced zones. False if the method can't be found.</summary>
        public static bool VanillaInActiveArea(Vector3 point, Vector3 centre)
        {
            if (PointInsideActiveAreaMethod == null)
            {
                return false;
            }
            return (bool)PointInsideActiveAreaMethod.Invoke(null, new object[] { ZoneSystem.GetZone(centre), point });
        }

        private static bool _loaderTypeResolved;
        private static PropertyInfo _forceActiveProp;
        private static PropertyInfo _loadersProp;

        public static int ExpectedWaterMask => LayerMask.GetMask("WaterVolume");

        public static int? WaterMask => WaterMaskField?.GetValue(null) as int?;

        public static Dictionary<ZDO, ZNetView> SceneInstances =>
            ZNetScene.instance == null ? null : SceneInstancesField?.GetValue(ZNetScene.instance) as Dictionary<ZDO, ZNetView>;

        public static List<ItemDrop> ItemDrops => ItemDropInstancesField?.GetValue(null) as List<ItemDrop>;

        /// <summary>Whether an old chunk loader mod (Frogger / NerdLounge / zeeCamelSnake all use this type name) is loaded.</summary>
        public static bool OldLoaderModFound
        {
            get
            {
                ResolveLoaderType();
                return _forceActiveProp != null;
            }
        }

        /// <summary>The old mod's forced zones, or null if it isn't loaded or its type shape is unexpected.</summary>
        public static HashSet<Vector2s> ForcedZones
        {
            get
            {
                ResolveLoaderType();
                return _forceActiveProp?.GetValue(null, null) as HashSet<Vector2s>;
            }
        }

        public static List<ZDO> OldLoaders
        {
            get
            {
                ResolveLoaderType();
                return _loadersProp?.GetValue(null, null) as List<ZDO>;
            }
        }

        private static void ResolveLoaderType()
        {
            if (_loaderTypeResolved)
            {
                return;
            }
            _loaderTypeResolved = true;
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => SafeGetType(a, "ChunkLoader.ZoneLoadingManager"))
                .FirstOrDefault(t => t != null);
            _forceActiveProp = type?.GetProperty("ForceActive", AnyStatic);
            _loadersProp = type?.GetProperty("LoadersInWorld", AnyStatic);
            DiagLog.Write("SETUP", type == null
                ? "Old chunk loader mod not found (no ChunkLoader.ZoneLoadingManager type). Forced-zone tracking is off."
                : $"Old chunk loader mod found in {type.Assembly.GetName().Name}. ForceActive={(_forceActiveProp != null)}, LoadersInWorld={(_loadersProp != null)}.");
        }

        private static Type SafeGetType(Assembly assembly, string name)
        {
            try
            {
                return assembly.GetType(name, false);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The water level the game's ship code would see: Floating.GetWaterLevel uses s_waterVolumeMask as-is,
        /// so this reproduces the suspected bug when the mask is 0. A fresh cache ref keeps the ship's own cache untouched.
        /// </summary>
        public static float GameWaterLevel(Vector3 point)
        {
            WaterVolume cache = null;
            return Floating.GetWaterLevel(point, ref cache);
        }

        private static readonly Collider[] OverlapBuffer = new Collider[32];

        /// <summary>The water level a correctly initialised lookup finds, via our own physics query.</summary>
        public static float TrueWaterLevel(Vector3 point)
        {
            var best = -10000f;
            var hits = Physics.OverlapSphereNonAlloc(point, 0f, OverlapBuffer, ExpectedWaterMask);
            for (var i = 0; i < hits; i++)
            {
                var volume = OverlapBuffer[i] != null ? OverlapBuffer[i].GetComponent<WaterVolume>() : null;
                if (volume != null)
                {
                    best = Mathf.Max(best, volume.GetWaterSurface(point));
                }
            }
            return best;
        }

        public static string FormatLevel(float level) => level <= -9999f ? "none" : level.ToString("0.00");

        public sealed class BuoyancyProbe
        {
            public bool Applies;
            public string Summary;
        }

        /// <summary>
        /// Re-runs the buoyancy decision from Ship.CustomFixedUpdate with the same five sample points (centre of mass
        /// plus the float collider's bow, stern, left and right) and the same averaging, reading only.
        /// </summary>
        public static BuoyancyProbe ProbeBuoyancy(Ship ship, Rigidbody body)
        {
            var box = ship.m_floatCollider;
            if (box == null)
            {
                return new BuoyancyProbe { Applies = false, Summary = "no float collider" };
            }
            var t = box.transform;
            var size = box.size;
            var centre = body.worldCenterOfMass;
            var names = new[] { "centre", "bow", "stern", "left", "right" };
            var samples = new[]
            {
                centre,
                t.position + t.forward * size.z / 2f,
                t.position - t.forward * size.z / 2f,
                t.position - t.right * size.x / 2f,
                t.position + t.right * size.x / 2f,
            };

            var total = 0f;
            var parts = new List<string>();
            for (var i = 0; i < samples.Length; i++)
            {
                var name = names[i];
                var point = samples[i];
                var level = GameWaterLevel(point);
                total += level;
                var zone = ZoneSystem.GetZone(point);
                var loaded = ZoneSystem.instance.IsZoneLoaded(zone);
                parts.Add($"{name} ({point.x:0},{point.z:0}) zone {zone} {(loaded ? "loaded" : "NOT LOADED")} water {FormatLevel(level)}");
            }
            var average = total / samples.Length;
            var num2 = centre.y - average - ship.m_waterLevelOffset;
            var applies = !(num2 > ship.m_disableLevel);
            var yaw = ship.transform.eulerAngles.y;
            return new BuoyancyProbe
            {
                Applies = applies,
                Summary = $"buoyancy {(applies ? "ON" : "OFF")} (avg water {average:0.00}, depth term {num2:0.00} vs disable level {ship.m_disableLevel:0.00}), " +
                          $"heading {yaw:0}°, float box {size.x:0.0}x{size.z:0.0} m; samples: {string.Join("; ", parts)}",
            };
        }
    }
}
