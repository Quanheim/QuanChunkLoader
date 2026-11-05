using System.Reflection;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace QuanChunkLoader
{
    /// <summary>
    /// Quan's Chunk Observer (docs/design.md, "Pieces"). The model is Frogger's original ChunkLoader_stone, from the
    /// chunkloader asset bundle in JFHeim/ChunkLoader (MIT; see THIRD-PARTY-NOTICES.md), scaled to 50%. The bundle's
    /// prefab carries only vanilla scripts (ZNetView, Piece, WearNTear, LightLod), so it's cloned and set up here.
    /// </summary>
    internal static class ObserverPiece
    {
        internal const string PrefabName = "Quan_ChunkObserver";
        internal static readonly int PrefabHash = PrefabName.GetStableHashCode();

        private const string BundleResource = "chunkloader";
        private const string ModelPrefab = "ChunkLoader_stone";
        private const string EffectsFrom = "stone_wall_1x1"; // vanilla stone piece: place, hit and destroy effects
        private const float ModelScale = 0.5f; // the 2.47 m column becomes about 1.25 m

        internal const string EffectChild = "WayEffect"; // the light and particles: shown while switched on
        internal const string ColumnChild = "new/SM_cloumn"; // the column mesh (sic); its runes glow red off, blue on

        private const string Description =
            "Keeps the zones around it loaded while switched on, so your base keeps working while you're elsewhere. " +
            "Nearby players still own everything as usual.\n\n" +
            "<i>Dedicated to Frogger, who first brought chunk loading to Valheim. This is his original chunk loader " +
            "model, used as a tribute.</i>";

        public static void Register()
        {
            var bundle = AssetUtils.LoadAssetBundleFromResources(BundleResource, Assembly.GetExecutingAssembly());
            if (bundle == null)
            {
                Log.Error($"The observer model's asset bundle '{BundleResource}' wasn't found; Chunk Observer not added.");
                return;
            }
            var source = bundle.LoadAsset<GameObject>(ModelPrefab);
            var icon = bundle.LoadAsset<Sprite>("icon_active");
            if (source == null)
            {
                Log.Error($"'{ModelPrefab}' isn't in the asset bundle; Chunk Observer not added.");
                return;
            }

            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, source);
            Setup(prefab);

            var config = new PieceConfig
            {
                Name = "Quan's Chunk Observer",
                Description = Description,
                PieceTable = PieceTables.Hammer,
                Category = QuanChunkLoaderPlugin.HammerCategory,
                Requirements = new[] { new RequirementConfig("Stone", Settings.StoneCost.Value, 0, true) }
            };
            if (icon != null)
            {
                config.Icon = icon;
            }
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
        }

        private static void Setup(GameObject prefab)
        {
            var nview = prefab.GetComponent<ZNetView>();
            nview.m_persistent = true;

            // Smaller than the original, which is very big. The root stays at scale 1 for the network view.
            foreach (var childName in new[] { "new", EffectChild })
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

            var vanilla = PrefabManager.Instance.GetPrefab(EffectsFrom);
            if (vanilla != null)
            {
                piece.m_placeEffect = vanilla.GetComponent<Piece>().m_placeEffect;
                var vanillaWear = vanilla.GetComponent<WearNTear>();
                wear.m_hitEffect = vanillaWear.m_hitEffect;
                wear.m_destroyedEffect = vanillaWear.m_destroyedEffect;
            }
            else
            {
                Log.Warning($"'{EffectsFrom}' not found; the observer has no place or destroy effects.");
            }

            prefab.AddComponent<ChunkObserver>();
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
