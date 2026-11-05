using System.Collections.Generic;

namespace QuanChunkLoader.Core
{
    internal enum Kind
    {
        UnknownPrefab,
        Player,
        Creature,
        Ship,
        Container,
        Station,
        Item,
        Plant,
        Piece,
        Other
    }

    /// <summary>
    /// What a ZDO is, from its prefab, cached per prefab hash. Drives which objects the server loads and claims in
    /// loaded zones, and the categories in log lines.
    /// </summary>
    internal static class ObjectKinds
    {
        private static readonly Dictionary<int, Kind> Cache = new Dictionary<int, Kind>();

        /// <summary>Set on a creature the server spawned in a loaded zone (a phantom-run spawner): it runs as normal.</summary>
        internal const string PhantomSpawnedKey = "QCL_phantom";

        internal static Kind Of(ZDO zdo) => Of(zdo.GetPrefab());

        internal static Kind Of(int prefabHash)
        {
            if (Cache.TryGetValue(prefabHash, out var kind))
            {
                return kind;
            }
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabHash) : null;
            kind = prefab == null ? Kind.UnknownPrefab : Classify(prefab);
            // Don't cache "unknown" before the scene exists: prefabs may not be registered yet.
            if (ZNetScene.instance != null)
            {
                Cache[prefabHash] = kind;
            }
            return kind;
        }

        private static Kind Classify(UnityEngine.GameObject prefab)
        {
            if (prefab.GetComponent<Player>() != null) return Kind.Player;
            if (prefab.GetComponent<Character>() != null) return Kind.Creature;
            if (prefab.GetComponent<Ship>() != null) return Kind.Ship;
            if (prefab.GetComponent<Container>() != null) return Kind.Container;
            if (prefab.GetComponent<Smelter>() != null || prefab.GetComponent<Fermenter>() != null ||
                prefab.GetComponent<Beehive>() != null || prefab.GetComponent<CookingStation>() != null ||
                prefab.GetComponent<SapCollector>() != null)
                return Kind.Station;
            if (prefab.GetComponent<ItemDrop>() != null) return Kind.Item;
            if (prefab.GetComponent<Pickable>() != null || prefab.GetComponent<Plant>() != null) return Kind.Plant;
            if (prefab.GetComponent<Piece>() != null) return Kind.Piece;
            return Kind.Other;
        }

        internal static bool IsTame(ZDO zdo) => zdo.GetBool(ZDOVars.s_tamed);

        /// <summary>A short category for logs: tames are told apart from wild creatures.</summary>
        internal static string Category(ZDO zdo)
        {
            var kind = Of(zdo);
            if (kind == Kind.Creature)
            {
                return IsTame(zdo) ? "tames" : "creatures";
            }
            switch (kind)
            {
                case Kind.UnknownPrefab: return "unknown";
                case Kind.Player: return "players";
                case Kind.Ship: return "ships";
                case Kind.Container: return "containers";
                case Kind.Station: return "stations";
                case Kind.Item: return "items";
                case Kind.Plant: return "plants";
                case Kind.Piece: return "pieces";
                default: return "other";
            }
        }

        /// <summary>
        /// Whether the server instantiates this object in a loaded zone (docs/design.md: Tames, Wild creatures,
        /// Safety rules). Players load as in vanilla; unknown prefabs never do, or the game would delete them.
        /// </summary>
        internal static bool ServerLoads(ZDO zdo)
        {
            switch (Of(zdo))
            {
                case Kind.UnknownPrefab:
                    return false;
                case Kind.Creature:
                    // Tames only when the setting asks; wild creatures only if a phantom-run spawner made them here.
                    return IsTame(zdo) ? Settings.SimulateTames.Value : zdo.GetBool(PhantomSpawnedKey);
                default:
                    return true;
            }
        }

        /// <summary>
        /// Whether the server may take ownership of this object while no player is near it: only objects it runs
        /// itself, that persist, and that nobody is using.
        /// </summary>
        internal static bool ServerClaims(ZDO zdo)
        {
            return zdo.Persistent && Of(zdo) != Kind.Player && ServerLoads(zdo) && zdo.GetInt(ZDOVars.s_inUse) == 0;
        }
    }
}
