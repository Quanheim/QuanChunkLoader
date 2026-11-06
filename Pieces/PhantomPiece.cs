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
    /// there, inside zones a Chunk Observer keeps loaded. Look: the vanilla ward's model at 25%, in grey stone, with its
    /// rune lines glowing red, on a small round stone plinth. Only the look comes across: the ward's PrivateArea, its area marker and anything else that would
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
        private static readonly Color StoneTint = new Color(0.86f, 0.87f, 0.88f); // a cool grey, like the observers' stone
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
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
            AddPlinth(prefab);

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

        // A small round two-step stone plinth, the shape of the top of the cluster's capital (wide disc, narrower
        // step on top), in metres: radius, bottom, top. The ward stands on it (Quandru, 6 Nov 2026).
        private static readonly Vector3[] PlinthDiscs =
        {
            new Vector3(0.20f, 0.00f, 0.07f),
            new Vector3(0.16f, 0.07f, 0.11f)
        };

        private const float PlinthHeight = 0.11f;

        /// <summary>Lifts the ward onto a round stone plinth in the observers' stone.</summary>
        private static void AddPlinth(GameObject prefab)
        {
            var material = ObserverPiece.ColumnMaterial;
            if (material == null)
            {
                Log.Warning("Phantom Spawner: the observers' stone material isn't loaded, so it has no plinth.");
                return;
            }
            foreach (Transform child in prefab.transform)
            {
                child.localPosition += Vector3.up * PlinthHeight;
            }
            foreach (var box in prefab.GetComponents<BoxCollider>())
            {
                box.center += Vector3.up * PlinthHeight;
            }
            foreach (var capsule in prefab.GetComponents<CapsuleCollider>())
            {
                capsule.center += Vector3.up * PlinthHeight;
            }
            foreach (var sphere in prefab.GetComponents<SphereCollider>())
            {
                sphere.center += Vector3.up * PlinthHeight;
            }

            var plinth = new GameObject("plinth");
            plinth.layer = prefab.layer;
            plinth.transform.SetParent(prefab.transform, false);
            var mesh = ClusterModel.DiscStack(PlinthDiscs, "QuanPhantomSpawner_plinth");
            plinth.AddComponent<MeshFilter>().sharedMesh = mesh;
            plinth.AddComponent<MeshRenderer>().sharedMaterial = material;
            plinth.AddComponent<MeshCollider>().sharedMesh = mesh;
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
        /// Quandru's look (6 Nov 2026): the ward's carved wooden Odin statue turned to grey stone, matching the
        /// observers' stone, with only its rune lines glowing red. The ward's materials (Guardstone_Oden_mat and
        /// Guardstone_OdenGlow_mat, read from the game's bundle) share the wood texture OdenStatue_d; a stone-grey
        /// copy of it replaces it, so the carving and the UV layout stay exactly as they were. The rune lines are
        /// on the glow material's emission map (OdenStatue_e), which PrivateArea lights with "_EMISSION"; the red
        /// goes there. A material without an emission map gets no glow (it would turn the whole surface red).
        /// </summary>
        private static void Redden(GameObject prefab)
        {
            var stone = new Dictionary<Texture, Texture>();
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
                        var wood = copy.HasProperty(MainTex) ? copy.GetTexture(MainTex) : null;
                        if (wood != null)
                        {
                            if (!stone.TryGetValue(wood, out var grey))
                            {
                                grey = StoneCopy(wood);
                                stone[wood] = grey;
                            }
                            if (grey != null)
                            {
                                copy.SetTexture(MainTex, grey);
                            }
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
                        Log.Debug($"Phantom Spawner: material '{m.name}' ({m.shader?.name}) in stone" +
                                  (glowMap != null ? $", red glow on its emission map '{glowMap.name}'." : ", no emission map."));
                        copies[m] = copy;
                    }
                    materials[i] = copy;
                }
                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>
        /// A stone-grey copy of a texture: each pixel's brightness, tinted towards the observers' grey stone, with
        /// its alpha kept. The game's textures aren't CPU-readable, so it's copied through a render texture. Null on
        /// a dedicated server (no graphics, and nobody to see it) or if anything fails; the wood then stays.
        /// </summary>
        private static Texture StoneCopy(Texture source)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return null;
            }
            var active = RenderTexture.active;
            RenderTexture rt = null;
            try
            {
                rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var grey = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true)
                {
                    name = source.name + "_stone",
                    wrapMode = source.wrapMode,
                    filterMode = source.filterMode,
                    anisoLevel = source.anisoLevel
                };
                grey.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                var pixels = grey.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    // Brightness, matched to the observers' stone (checked against both textures, 6 Nov 2026).
                    var lum = (0.30f * p.r + 0.59f * p.g + 0.11f * p.b) / 255f;
                    var v = Mathf.Clamp01(0.08f + lum * 0.85f);
                    pixels[i] = new Color32(
                        (byte)(v * StoneTint.r * 255f), (byte)(v * StoneTint.g * 255f), (byte)(v * StoneTint.b * 255f), p.a);
                }
                grey.SetPixels32(pixels);
                grey.Apply(true, true);
                return grey;
            }
            catch (System.Exception e)
            {
                Log.Warning($"Phantom Spawner: couldn't make a stone copy of '{source.name}', so it stays wood: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = active;
                if (rt != null)
                {
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
        }
    }
}
