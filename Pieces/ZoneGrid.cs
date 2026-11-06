using System.Collections.Generic;
using UnityEngine;

namespace QuanChunkLoader
{
    /// <summary>
    /// Shows the zones an observer loads (docs/design.md, "Pieces"): thin white lines along the zone borders,
    /// following the ground, and a thin white post at every zone corner, for a short while after the player asks.
    /// Drawn on the player's own machine from the observer's position and size, so it works for anyone.
    /// The look follows QuanTerraforming's zone grid, in white throughout.
    /// </summary>
    internal sealed class ZoneGrid : MonoBehaviour
    {
        internal const float ShowSeconds = 15f;

        private const float ZoneSize = 64f;     // ZoneSystem.m_zoneSize
        private const float WaterLevel = 30f;   // ZoneSystem.m_waterLevel
        private const float SampleStep = 2f;    // ground lines follow the terrain every 2 m
        private const float LineWidth = 0.12f;
        private const float PostWidth = 0.15f;
        private const float PostHeight = 20f;
        private static readonly Color White = new Color(1f, 1f, 1f, 0.85f);

        private static ZoneGrid _instance;

        private readonly List<LineRenderer> _pool = new List<LineRenderer>();
        private Transform _root;
        private Material _material;
        private float _hideAt;

        internal static void Hook(GameObject host) => _instance = host.AddComponent<ZoneGrid>();

        /// <summary>Shows the block of zones around a centre zone (radius 0 = just that zone) for a while.</summary>
        internal static void Show(Vector2s centre, int radius)
        {
            if (_instance != null)
            {
                _instance.Draw(centre, radius);
            }
        }

        private void Awake()
        {
            _root = new GameObject("QuanChunkLoader_ZoneGrid").transform;
            DontDestroyOnLoad(_root.gameObject);
            _root.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_root != null)
            {
                Destroy(_root.gameObject);
            }
        }

        private void Update()
        {
            if (_root.gameObject.activeSelf && (Time.time >= _hideAt || Player.m_localPlayer == null))
            {
                _root.gameObject.SetActive(false);
            }
        }

        private void Draw(Vector2s centre, int radius)
        {
            var used = 0;
            // Zone (x, z) spans its centre (x * 64, z * 64) plus or minus 32 m (ZoneSystem.GetZone).
            var minX = (centre.x - radius) * ZoneSize - ZoneSize / 2f;
            var maxX = (centre.x + radius + 1) * ZoneSize - ZoneSize / 2f;
            var minZ = (centre.y - radius) * ZoneSize - ZoneSize / 2f;
            var maxZ = (centre.y + radius + 1) * ZoneSize - ZoneSize / 2f;

            for (var x = minX; x <= maxX + 0.1f; x += ZoneSize)
            {
                GroundLine(new Vector3(x, 0f, minZ), new Vector3(x, 0f, maxZ), ref used);
            }
            for (var z = minZ; z <= maxZ + 0.1f; z += ZoneSize)
            {
                GroundLine(new Vector3(minX, 0f, z), new Vector3(maxX, 0f, z), ref used);
            }
            for (var x = minX; x <= maxX + 0.1f; x += ZoneSize)
            {
                for (var z = minZ; z <= maxZ + 0.1f; z += ZoneSize)
                {
                    Post(x, z, ref used);
                }
            }
            for (var i = used; i < _pool.Count; i++)
            {
                _pool[i].gameObject.SetActive(false);
            }
            _root.gameObject.SetActive(true);
            _hideAt = Time.time + ShowSeconds;
        }

        private static float Ground(Vector3 at) =>
            Mathf.Max(Heightmap.GetHeight(at, out var height) ? height : WaterLevel, WaterLevel);

        private void GroundLine(Vector3 a, Vector3 b, ref int used)
        {
            var n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) / SampleStep) + 1);
            var line = Next(ref used, LineWidth);
            line.positionCount = n;
            for (var i = 0; i < n; i++)
            {
                var q = Vector3.Lerp(a, b, i / (float)(n - 1));
                q.y = Ground(q) + 0.3f;
                line.SetPosition(i, q);
            }
        }

        private void Post(float x, float z, ref int used)
        {
            var at = new Vector3(x, 0f, z);
            var bottom = Ground(at);
            var line = Next(ref used, PostWidth);
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(x, bottom, z));
            line.SetPosition(1, new Vector3(x, bottom + PostHeight, z));
        }

        private LineRenderer Next(ref int used, float width)
        {
            if (used == _pool.Count)
            {
                var go = new GameObject("line");
                go.transform.SetParent(_root, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.material = Material();
                _pool.Add(lr);
            }
            var r = _pool[used++];
            r.gameObject.SetActive(true);
            r.startWidth = r.endWidth = width;
            r.startColor = r.endColor = White;
            return r;
        }

        private Material Material()
        {
            if (_material != null)
            {
                return _material;
            }
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("UI/Default");
            _material = new Material(shader);
            return _material;
        }
    }
}
