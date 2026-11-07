using System.Collections.Generic;
using QuanChunkLoader.Core;
using UnityEngine;

namespace QuanChunkLoader
{
    /// <summary>
    /// Shows the zones an observer loads (docs/design.md, "Pieces"): thin white lines along the zone borders,
    /// following the ground, and a thin white post at every zone corner, for a while after the player asks.
    /// Drawn on the player's own machine from the observer's position and size, so it works for anyone.
    /// The look follows QuanTerraforming's zone grid.
    ///
    /// Other loaded zones nearby are drawn in yellow (Quandru, 7 Nov 2026), so overlaps and gaps show. They come from
    /// the server, which answers with zone squares only: never who built what or where an observer stands.
    /// Where a yellow line would lie on the white grid, white wins.
    /// </summary>
    internal sealed class ZoneGrid : MonoBehaviour
    {
        internal const float ShowSeconds = 120f;    // 2 minutes (Quandru, 7 Nov 2026)

        /// <summary>How far around the observer's own zone other loaded zones are shown, in zones.</summary>
        private const int NearbyReach = 4;

        private const string RequestRpc = "QCL_ZonesNear";
        private const string ReplyRpc = "QCL_ZonesNearReply";

        private const float ZoneSize = 64f;     // ZoneSystem.m_zoneSize
        private const float WaterLevel = 30f;   // ZoneSystem.m_waterLevel
        private const float SampleStep = 2f;    // ground lines follow the terrain every 2 m
        private const float LineWidth = 0.12f;
        private const float PostWidth = 0.15f;
        private const float PostHeight = 20f;
        private static readonly Color White = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color Yellow = new Color(1f, 0.85f, 0.1f, 0.85f);

        private static ZoneGrid _instance;

        private readonly List<LineRenderer> _pool = new List<LineRenderer>();
        private Transform _root;
        private Material _material;
        private float _hideAt;

        // What's on screen: the observer's block, and the other loaded zones near it once the server answers.
        private Vector2s _centre;
        private int _radius;

        internal static void Hook(GameObject host) => _instance = host.AddComponent<ZoneGrid>();

        /// <summary>Shows the block of zones around a centre zone (radius 0 = just that zone) for a while.</summary>
        internal static void Show(Vector2s centre, int radius)
        {
            if (_instance == null)
            {
                return;
            }
            _instance._centre = centre;
            _instance._radius = radius;
            _instance.Draw(null);
            _instance._hideAt = Time.time + ShowSeconds;
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(RequestRpc, (int)centre.x, (int)centre.y);
            }
        }

        // ---- Nearby loaded zones (server query) ----------------------------------------------------------

        internal static void RegisterRpcs()
        {
            ZRoutedRpc.instance.Register<int, int>(RequestRpc, RPC_Request);
            ZRoutedRpc.instance.Register<ZPackage>(ReplyRpc, RPC_Reply);
        }

        /// <summary>Server: sends back the loaded zones within reach of a zone. Zone squares only.</summary>
        private static void RPC_Request(long sender, int x, int z)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }
            var zones = new List<Vector2s>();
            foreach (var zone in Observers.Forced)
            {
                if (Mathf.Abs(zone.x - x) <= NearbyReach && Mathf.Abs(zone.y - z) <= NearbyReach)
                {
                    zones.Add(zone);
                }
            }
            var package = new ZPackage();
            package.Write(x);
            package.Write(z);
            package.Write(zones.Count);
            foreach (var zone in zones)
            {
                package.Write((int)zone.x);
                package.Write((int)zone.y);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ReplyRpc, package);
        }

        /// <summary>Client: adds the server's answer to the grid on screen, if it's still showing that observer.</summary>
        private static void RPC_Reply(long sender, ZPackage package)
        {
            if (!ZNet.instance.IsServer())
            {
                var server = ZNet.instance.GetServerPeer();
                if (server == null || sender != server.m_uid)
                {
                    return;
                }
            }
            var x = package.ReadInt();
            var z = package.ReadInt();
            var count = package.ReadInt();
            var zones = new HashSet<Vector2s>();
            for (var i = 0; i < count; i++)
            {
                zones.Add(new Vector2s((short)package.ReadInt(), (short)package.ReadInt()));
            }
            var grid = _instance;
            if (grid == null || !grid._root.gameObject.activeSelf || grid._centre.x != x || grid._centre.y != z)
            {
                return;
            }
            grid.Draw(zones);
        }

        // ---- Drawing -------------------------------------------------------------------------------------

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

        // Grid corners are numbered so corner (i, j) sits at (i * 64 - 32, j * 64 - 32): zone (x, z) spans corners
        // x..x+1 and z..z+1 (ZoneSystem.GetZone).
        private static float CornerCoord(int i) => i * ZoneSize - ZoneSize / 2f;

        /// <summary>Draws the observer's block in white and, if given, the other loaded zones near it in yellow.</summary>
        private void Draw(HashSet<Vector2s> loadedNearby)
        {
            var used = 0;
            int iMin = _centre.x - _radius, iMax = _centre.x + _radius + 1;
            int jMin = _centre.y - _radius, jMax = _centre.y + _radius + 1;

            // White: every grid line across the block and a post at every corner.
            for (var i = iMin; i <= iMax; i++)
            {
                GroundLine(CornerCoord(i), CornerCoord(jMin), CornerCoord(i), CornerCoord(jMax), White, ref used);
            }
            for (var j = jMin; j <= jMax; j++)
            {
                GroundLine(CornerCoord(iMin), CornerCoord(j), CornerCoord(iMax), CornerCoord(j), White, ref used);
            }
            for (var i = iMin; i <= iMax; i++)
            {
                for (var j = jMin; j <= jMax; j++)
                {
                    Post(CornerCoord(i), CornerCoord(j), White, ref used);
                }
            }

            // Yellow: the edges and corners of other loaded zones that aren't already part of the white block.
            if (loadedNearby != null)
            {
                bool InBlock(int i, int j) => i >= iMin && i <= iMax && j >= jMin && j <= jMax;
                var edges = new HashSet<long>();
                var posts = new HashSet<long>();
                foreach (var zone in loadedNearby)
                {
                    int i = zone.x, j = zone.y;
                    if (i >= iMin && i < iMax && j >= jMin && j < jMax)
                    {
                        continue; // inside the white block
                    }
                    // An edge lies on the white grid when both its ends are corners of the block.
                    TryEdge(i, j, i + 1, j);
                    TryEdge(i, j + 1, i + 1, j + 1);
                    TryEdge(i, j, i, j + 1);
                    TryEdge(i + 1, j, i + 1, j + 1);
                    TryPost(i, j);
                    TryPost(i + 1, j);
                    TryPost(i, j + 1);
                    TryPost(i + 1, j + 1);
                }

                void TryEdge(int ai, int aj, int bi, int bj)
                {
                    if (InBlock(ai, aj) && InBlock(bi, bj))
                    {
                        return;
                    }
                    var vertical = ai == bi ? 1L : 0L;
                    if (edges.Add(Key(ai, aj) * 2 + vertical))
                    {
                        GroundLine(CornerCoord(ai), CornerCoord(aj), CornerCoord(bi), CornerCoord(bj), Yellow, ref used);
                    }
                }

                void TryPost(int ci, int cj)
                {
                    if (!InBlock(ci, cj) && posts.Add(Key(ci, cj)))
                    {
                        Post(CornerCoord(ci), CornerCoord(cj), Yellow, ref used);
                    }
                }
            }

            for (var k = used; k < _pool.Count; k++)
            {
                _pool[k].gameObject.SetActive(false);
            }
            _root.gameObject.SetActive(true);
        }

        private static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

        private static float Ground(Vector3 at) =>
            Mathf.Max(Heightmap.GetHeight(at, out var height) ? height : WaterLevel, WaterLevel);

        private void GroundLine(float ax, float az, float bx, float bz, Color colour, ref int used)
        {
            var a = new Vector3(ax, 0f, az);
            var b = new Vector3(bx, 0f, bz);
            var n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) / SampleStep) + 1);
            var line = Next(ref used, LineWidth, colour);
            line.positionCount = n;
            for (var i = 0; i < n; i++)
            {
                var q = Vector3.Lerp(a, b, i / (float)(n - 1));
                q.y = Ground(q) + 0.3f;
                line.SetPosition(i, q);
            }
        }

        private void Post(float x, float z, Color colour, ref int used)
        {
            var bottom = Ground(new Vector3(x, 0f, z));
            var line = Next(ref used, PostWidth, colour);
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(x, bottom, z));
            line.SetPosition(1, new Vector3(x, bottom + PostHeight, z));
        }

        private LineRenderer Next(ref int used, float width, Color colour)
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
            r.startColor = r.endColor = colour;
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
