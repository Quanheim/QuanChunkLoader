using System.Reflection;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace QuanChunkLoader
{
    /// <summary>
    /// The two Chunk Observers (docs/design.md, "Pieces"). Both use Frogger's original ChunkLoader_stone model, from
    /// the chunkloader asset bundle in JFHeim/ChunkLoader (MIT; see THIRD-PARTY-NOTICES.md), at 50%:
    /// - Quan_ChunkObserver: one column, loads its own zone (1x1).
    /// - Quan_ChunkObserverCluster: three columns turned 120 degrees apart on shared round plinths, loads 3x3.
    /// The bundle's prefab carries only vanilla scripts (ZNetView, Piece, WearNTear, LightLod), so it's cloned and
    /// set up here.
    /// </summary>
    internal static class ObserverPiece
    {
        internal const string SingleName = "Quan_ChunkObserver";
        internal const string ClusterName = "Quan_ChunkObserverCluster";
        internal static readonly int SingleHash = SingleName.GetStableHashCode();
        internal static readonly int ClusterHash = ClusterName.GetStableHashCode();

        internal const int SingleCost = 10;
        internal const int ClusterCost = 30;

        private const string BundleResource = "chunkloader";
        private const string ModelPrefab = "ChunkLoader_stone";
        private const string EffectsFrom = "stone_wall_1x1"; // vanilla stone piece: place, hit and destroy effects
        private const float ModelScale = 0.5f; // the 2.47 m column becomes about 1.25 m

        internal const string ModelChild = "new";
        internal const string ColumnChild = "new/SM_cloumn"; // the column mesh (sic); its runes glow red off, blue on
        internal const string EffectChild = "WayEffect"; // the light and particles: shown while switched on

        private const string Dedication =
            "\n\n<i>Dedicated to Frogger, who first brought chunk loading to Valheim. This is his original chunk " +
            "loader model, used as a tribute.</i>";

        private const string SingleDescription =
            "Keeps its own zone loaded while switched on, so your base keeps working while you're elsewhere. " +
            "Nearby players still own everything as usual." + Dedication;

        private const string ClusterDescription =
            "Keeps the 3x3 zones around it loaded while switched on, so a large base keeps working while you're " +
            "elsewhere. Nearby players still own everything as usual." + Dedication;

        /// <summary>The bundle's column material, shared by every observer; each observer swaps in its own copy.</summary>
        internal static Material ColumnMaterial { get; private set; }

        internal static Sprite IconActive { get; private set; }
        internal static Sprite IconDisabled { get; private set; }

        internal static bool IsObserver(int prefabHash) => prefabHash == SingleHash || prefabHash == ClusterHash;

        /// <summary>Zones loaded on each side of the observer's own zone: 0 for the single column, 1 for the cluster.</summary>
        internal static int RadiusOf(int prefabHash) => prefabHash == ClusterHash ? 1 : 0;

        internal static int ZonesOf(int prefabHash)
        {
            var side = 2 * RadiusOf(prefabHash) + 1;
            return side * side;
        }

        internal static string SizeLabel(int prefabHash) => prefabHash == ClusterHash ? "3x3" : "1x1";

        public static void Register()
        {
            var bundle = AssetUtils.LoadAssetBundleFromResources(BundleResource, Assembly.GetExecutingAssembly());
            if (bundle == null)
            {
                Log.Error($"The observer model's asset bundle '{BundleResource}' wasn't found; Chunk Observers not added.");
                return;
            }
            var source = bundle.LoadAsset<GameObject>(ModelPrefab);
            IconActive = bundle.LoadAsset<Sprite>("icon_active");
            IconDisabled = bundle.LoadAsset<Sprite>("icon_disabled");
            if (source == null)
            {
                Log.Error($"'{ModelPrefab}' isn't in the asset bundle; Chunk Observers not added.");
                return;
            }

            var single = PrefabManager.Instance.CreateClonedPrefab(SingleName, source);
            Setup(single);
            Add(single, "Quan's Chunk Observer", SingleDescription, SingleCost, IconActive);

            var cluster = PrefabManager.Instance.CreateClonedPrefab(ClusterName, source);
            Setup(cluster);
            try
            {
                ClusterModel.Build(cluster);
            }
            catch (System.Exception e)
            {
                Log.Error($"Couldn't build the Chunk Observer Cluster's model; it isn't added: {e}");
                return;
            }
            Add(cluster, "Quan's Chunk Observer Cluster", ClusterDescription, ClusterCost, RenderIcon(cluster) ?? IconActive);
        }

        private static void Add(GameObject prefab, string name, string description, int stone, Sprite icon)
        {
            var config = new PieceConfig
            {
                Name = name,
                Description = description,
                PieceTable = PieceTables.Hammer,
                Category = QuanChunkLoaderPlugin.HammerCategory,
                Requirements = new[] { new RequirementConfig("Stone", stone, 0, true) }
            };
            if (icon != null)
            {
                config.Icon = icon;
            }
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
            BuildPermission.Track(prefab.GetComponent<Piece>());
        }

        private static Sprite RenderIcon(GameObject prefab)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return null; // a dedicated server has no graphics, and no hammer to show it in
            }
            try
            {
                return RenderManager.Instance.Render(prefab, RenderManager.IsometricRotation);
            }
            catch (System.Exception e)
            {
                Log.Warning($"Couldn't render an icon for {prefab.name}; using the single column's: {e.Message}");
                return null;
            }
        }

        private static void Setup(GameObject prefab)
        {
            var nview = prefab.GetComponent<ZNetView>();
            nview.m_persistent = true;

            // Smaller than the original, which is very big. The root stays at scale 1 for the network view.
            foreach (var childName in new[] { ModelChild, EffectChild })
            {
                var child = prefab.transform.Find(childName);
                if (child != null)
                {
                    child.localScale *= ModelScale;
                }
                else
                {
                    Log.Warning($"Observer model: child '{childName}' not found.");
                }
            }
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            var light = prefab.GetComponentInChildren<Light>(true);
            if (light != null)
            {
                light.range *= ModelScale;
            }

            FixShaders(prefab);
            var column = prefab.transform.Find(ColumnChild);
            if (column != null && ColumnMaterial == null)
            {
                ColumnMaterial = column.GetComponent<Renderer>().sharedMaterial;
            }

            // Not a target for monsters, and can't be damaged (WearNTear.RPC_Damage is blocked in ObserverPatches).
            var piece = prefab.GetComponent<Piece>();
            piece.m_primaryTarget = false;
            piece.m_randomTarget = false;
            piece.m_targetNonPlayerBuilt = false;

            var wear = prefab.GetComponent<WearNTear>();
            wear.m_noRoofWear = false;
            wear.m_noSupportWear = false;
            wear.m_materialType = WearNTear.MaterialType.Stone;
            wear.m_burnable = false;
            wear.m_triggerPrivateArea = false;

            CopyEffects(piece, wear);
            prefab.AddComponent<ChunkObserver>();
        }

        internal static void CopyEffects(Piece piece, WearNTear wear)
        {
            var vanilla = PrefabManager.Instance.GetPrefab(EffectsFrom);
            if (vanilla == null)
            {
                Log.Warning($"'{EffectsFrom}' not found; {piece.name} has no place or destroy effects.");
                return;
            }
            piece.m_placeEffect = vanilla.GetComponent<Piece>().m_placeEffect;
            var vanillaWear = vanilla.GetComponent<WearNTear>();
            wear.m_hitEffect = vanillaWear.m_hitEffect;
            wear.m_destroyedEffect = vanillaWear.m_destroyedEffect;
        }

        /// <summary>
        /// Points the bundle's materials at the game's own copies of their shaders, so they render with the game's
        /// build of each shader. Logged, because the look needs checking in game (docs/design.md, "Pieces").
        /// </summary>
        private static void FixShaders(GameObject prefab)
        {
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null)
                    {
                        continue;
                    }
                    var name = material.shader.name;
                    var game = Shader.Find(name);
                    if (game != null)
                    {
                        material.shader = game;
                    }
                    Log.Debug($"Observer model: material '{material.name}' uses shader '{name}'" +
                              (game != null ? " (game copy)." : " (bundle copy; no game copy found)."));
                }
            }
        }
    }
}
