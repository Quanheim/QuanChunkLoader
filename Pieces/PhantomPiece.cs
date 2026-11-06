using System.Collections.Generic;
using System.Text;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace QuanChunkLoader
{
    /// <summary>
    /// Quan's Phantom Spawner (docs/design.md, "Pieces"): makes the spawners around it act as if a player stood
    /// there, inside zones a Chunk Observer keeps loaded. Look: the vanilla ward's model at 25%, tar-black, with its
    /// rune lines glowing red. Only the look comes across: the ward's PrivateArea, its area marker and anything else that would
    /// make it act as a ward are removed (audited in Strip, and logged at Debug).
    /// </summary>
    internal static class PhantomPiece
    {
        internal const string PrefabName = "Quan_PhantomSpawner";
        internal static readonly int PrefabHash = PrefabName.GetStableHashCode();
        internal const int Cost = 10;

        private const string WardPrefab = "guard_stone";
        private const float ModelScale = 0.25f;
        private static readonly Color Glow = new Color(1f, 0.15f, 0.1f);
        private static readonly Color TarBlack = new Color(0.16f, 0.14f, 0.13f); // multiplies the ward's own texture
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

        private const string Description =
            "Makes nearby creature spawners act as if a player were standing here, so a surtling farm keeps " +
            "producing while you're away. Only works inside zones a switched-on Chunk Observer keeps loaded.";

        public static void Register()
        {
            var ward = PrefabManager.Instance.GetPrefab(WardPrefab);
            if (ward == null)
            {
                Log.Error($"The vanilla ward '{WardPrefab}' wasn't found; Phantom Spawner not added.");
                return;
            }
            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, ward);
            Strip(prefab);
            Shrink(prefab);
            Redden(prefab);

            prefab.GetComponent<ZNetView>().m_persistent = true;
            var piece = prefab.GetComponent<Piece>();
            piece.m_primaryTarget = false;
            piece.m_randomTarget = false;
            piece.m_targetNonPlayerBuilt = false;
            piece.m_craftingStation = null;
            piece.m_comfort = 0;
            piece.m_isUpgrade = false;
            piece.m_spaceRequirement = 0f;
            var wear = prefab.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.m_noRoofWear = false;
                wear.m_noSupportWear = false;
                wear.m_materialType = WearNTear.MaterialType.Stone;
                wear.m_burnable = false;
                wear.m_triggerPrivateArea = false;
                ObserverPiece.CopyEffects(piece, wear);
            }
            prefab.AddComponent<PhantomSpawner>();

            var config = new PieceConfig
            {
                Name = "Quan's Phantom Spawner",
                Description = Description,
                PieceTable = PieceTables.Hammer,
                Category = QuanChunkLoaderPlugin.HammerCategory,
                CraftingStation = "",
                Requirements = new[] { new RequirementConfig("Stone", Cost, 0, true) }
            };
            // The ward's own icon is gold; render ours from the red model (skipped on a dedicated server).
            var icon = ObserverPiece.RenderIcon(prefab);
            if (icon != null)
            {
                config.Icon = icon;
            }
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
            BuildPermission.Track(piece);
        }

        /// <summary>Removes everything that would make the clone act as a ward or a base, keeping only its look.</summary>
        private static void Strip(GameObject prefab)
        {
            var removed = new StringBuilder();

            // Particle effects (the ward's flash and glow) go whole, renderer and all.
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps != null && ps.gameObject != prefab)
                {
                    removed.Append("particles ").Append(ps.name).Append("; ");
                    Object.DestroyImmediate(ps.gameObject);
                }
            }

            // Scripts other than the network view, piece and wear (PrivateArea, its area marker, any EffectArea,
            // flickering lights and so on), lights, sounds and trigger volumes.
            var keep = new HashSet<System.Type> { typeof(ZNetView), typeof(Piece), typeof(WearNTear) };
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }
                var remove = (component is MonoBehaviour && !keep.Contains(component.GetType())) ||
                             component is Light || component is AudioSource ||
                             (component is Collider c && c.isTrigger);
                if (remove)
                {
                    removed.Append(component.GetType().Name).Append(" on ").Append(component.name).Append("; ");
                    Object.DestroyImmediate(component);
                }
            }
            Log.Debug($"Phantom Spawner: removed from the ward clone: {removed}");
            if (prefab.GetComponentInChildren<PrivateArea>(true) != null || prefab.GetComponentInChildren<EffectArea>(true) != null)
            {
                throw new System.InvalidOperationException("the ward clone still has a PrivateArea or EffectArea");
            }
        }

        /// <summary>Scales every part to 25%. The root stays at scale 1 for the network view; its own colliders shrink.</summary>
        private static void Shrink(GameObject prefab)
        {
            foreach (Transform child in prefab.transform)
            {
                child.localPosition *= ModelScale;
                child.localScale *= ModelScale;
            }
            foreach (var box in prefab.GetComponents<BoxCollider>())
            {
                box.center *= ModelScale;
                box.size *= ModelScale;
            }
            foreach (var capsule in prefab.GetComponents<CapsuleCollider>())
            {
                capsule.center *= ModelScale;
                capsule.radius *= ModelScale;
                capsule.height *= ModelScale;
            }
            foreach (var sphere in prefab.GetComponents<SphereCollider>())
            {
                sphere.center *= ModelScale;
                sphere.radius *= ModelScale;
            }
        }

        /// <summary>
        /// Quandru's look (6 Nov 2026): the body tar-black, like darkwood and the tarred shingle roof, and only the
        /// ward's rune lines glowing red. The ward lights its runes by switching on its material's emission
        /// (PrivateArea: "_EMISSION"), and only the lines are painted on its emission map, so the red goes there.
        /// A material without an emission map gets no glow (glowing it would turn the whole surface red).
        /// </summary>
        private static void Redden(GameObject prefab)
        {
            var copies = new Dictionary<Material, Material>();
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var m = materials[i];
                    if (m == null)
                    {
                        continue;
                    }
                    if (!copies.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + "_phantom" };
                        if (copy.HasProperty("_Color"))
                        {
                            copy.color = TarBlack;
                        }
                        var glowMap = copy.HasProperty(EmissionMap) ? copy.GetTexture(EmissionMap) : null;
                        if (glowMap != null && copy.HasProperty(EmissionColor))
                        {
                            copy.EnableKeyword("_EMISSION");
                            copy.SetColor(EmissionColor, Glow * 2f);
                        }
                        else
                        {
                            copy.DisableKeyword("_EMISSION");
                        }
                        Log.Debug($"Phantom Spawner: material '{m.name}' ({m.shader?.name}) tar-black" +
                                  (glowMap != null ? $", red glow on its emission map '{glowMap.name}'." : ", no emission map."));
                        copies[m] = copy;
                    }
                    materials[i] = copy;
                }
                renderer.sharedMaterials = materials;
            }
        }
    }
}
