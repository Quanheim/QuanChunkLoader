using System.Collections.Generic;
using UnityEngine;

namespace QuanChunkLoader
{
    /// <summary>
    /// Builds the Chunk Observer Cluster's model from Frogger's column (docs/design.md, "Pieces"): three copies of
    /// the column's shaft, 120 degrees apart with a rune face pointing straight out, overlapping tightly around the
    /// middle (Quandru's sketch, 6 Nov 2026), on a shared round two-step base under a shared round capital whose lips
    /// only just extend beyond their corners. Measurements are in the model's own units, before the 50% scale on
    /// its parent (the original column is 2.47 m tall).
    /// </summary>
    internal static class ClusterModel
    {
        // The column mesh, measured from the bundle (5 Nov 2026): square base steps up to 0.30, the shaft
        // (about 0.5 wide, runes on its two local-x faces) from 0.30 to 2.20, the capital above.
        private const float ShaftBottom = 0.295f;
        private const float ShaftTop = 2.205f;

        // Each shaft's centre stands this far out from the middle, towards its outer face: about 15% of its width,
        // measured from Quandru's sketch. The shafts overlap in a triangle around the middle.
        private const float ColumnOut = 0.08f;
        private const float ColumnSideways = 0f;

        // Round plinths: radius, bottom, top. The shafts' half-width is 0.255 (with the stone's bumps), so their outer
        // corners reach 0.42 from the centre; the lips directly above and below them (0.44) only just extend beyond.
        private static readonly Vector3[] Discs =
        {
            new Vector3(0.56f, 0.00f, 0.15f), // lower base step
            new Vector3(0.44f, 0.15f, 0.31f), // upper base step: the lip under the columns
            new Vector3(0.44f, 2.19f, 2.37f), // capital: the lip over the columns
            new Vector3(0.37f, 2.37f, 2.47f)  // capital's top step
        };

        // A plain stone patch of the column's texture: the capital's top face, inset from its edges.
        private static readonly Rect Stone = new Rect(0.04f, 0.72f, 0.16f, 0.16f);

        private const int Segments = 32;

        internal static void Build(GameObject prefab)
        {
            var column = prefab.transform.Find(ObserverPiece.ColumnChild);
            var model = prefab.transform.Find(ObserverPiece.ModelChild);
            if (column == null || model == null)
            {
                throw new System.InvalidOperationException("the column or model child is missing");
            }
            var source = column.GetComponent<MeshFilter>().sharedMesh;
            var material = column.GetComponent<Renderer>().sharedMaterial;
            var shaft = ShaftOnly(source);

            // The original column becomes the first shaft; two copies make the others.
            var columns = new List<Transform> { column };
            for (var i = 1; i < 3; i++)
            {
                columns.Add(Object.Instantiate(column.gameObject, model, false).transform);
            }
            for (var i = 0; i < 3; i++)
            {
                var c = columns[i];
                c.name = $"column{i}";
                var angle = i * 120f;
                var turn = Quaternion.Euler(0f, angle, 0f);
                c.localPosition = turn * new Vector3(ColumnSideways, 0f, ColumnOut);
                // Turned so the shaft's local +x face (a rune face) points straight out.
                c.localRotation = Quaternion.Euler(0f, angle - 90f, 0f);
                c.GetComponent<MeshFilter>().sharedMesh = shaft;
                var collider = c.GetComponent<MeshCollider>();
                if (collider != null)
                {
                    collider.sharedMesh = shaft;
                }
            }

            var plinths = new GameObject("plinths");
            plinths.layer = column.gameObject.layer;
            plinths.transform.SetParent(model, false);
            var mesh = Plinths();
            plinths.AddComponent<MeshFilter>().sharedMesh = mesh;
            plinths.AddComponent<MeshRenderer>().sharedMaterial = material;
            plinths.AddComponent<MeshCollider>().sharedMesh = mesh;

            // The bundle's level-of-detail group only knows the original column; without it every part always draws.
            var lod = prefab.GetComponent<LODGroup>();
            if (lod != null)
            {
                Object.DestroyImmediate(lod);
            }
        }

        /// <summary>The column mesh without its square base and capital: triangles wholly inside the shaft's height.</summary>
        private static Mesh ShaftOnly(Mesh source)
        {
            var vertices = source.vertices;
            var kept = new List<int>();
            var triangles = source.triangles;
            for (var t = 0; t < triangles.Length; t += 3)
            {
                var inside = true;
                for (var k = 0; k < 3; k++)
                {
                    var y = vertices[triangles[t + k]].y;
                    if (y < ShaftBottom || y > ShaftTop)
                    {
                        inside = false;
                        break;
                    }
                }
                if (inside)
                {
                    kept.Add(triangles[t]);
                    kept.Add(triangles[t + 1]);
                    kept.Add(triangles[t + 2]);
                }
            }
            var mesh = new Mesh
            {
                name = "QuanChunkObserver_shaft",
                vertices = vertices,
                normals = source.normals,
                tangents = source.tangents,
                uv = source.uv
            };
            mesh.SetTriangles(kept, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The round base steps and capital as one mesh: each disc's side, top and bottom.</summary>
        private static Mesh Plinths()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            foreach (var d in Discs)
            {
                Disc(d.x, d.y, d.z, v, n, uv, tri);
            }
            var mesh = new Mesh { name = "QuanChunkObserver_plinths" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector2 StoneUv(float u, float v) => new Vector2(Stone.x + Stone.width * u, Stone.y + Stone.height * v);

        private static void Disc(float radius, float bottom, float top, List<Vector3> v, List<Vector3> n,
            List<Vector2> uv, List<int> tri)
        {
            // Side: the stone patch repeats four times around, once per quarter.
            var perQuarter = Segments / 4;
            for (var s = 0; s < Segments; s++)
            {
                var a0 = 2f * Mathf.PI * s / Segments;
                var a1 = 2f * Mathf.PI * (s + 1) / Segments;
                var d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                var u0 = (float)(s % perQuarter) / perQuarter;
                var u1 = (float)(s % perQuarter + 1) / perQuarter;
                var i = v.Count;
                v.Add(d0 * radius + Vector3.up * bottom); n.Add(d0); uv.Add(StoneUv(u0, 0f));
                v.Add(d0 * radius + Vector3.up * top); n.Add(d0); uv.Add(StoneUv(u0, 1f));
                v.Add(d1 * radius + Vector3.up * top); n.Add(d1); uv.Add(StoneUv(u1, 1f));
                v.Add(d1 * radius + Vector3.up * bottom); n.Add(d1); uv.Add(StoneUv(u1, 0f));
                // Clockwise seen from outside (Unity's front faces): from outside, a1 is to the left of a0.
                tri.Add(i); tri.Add(i + 2); tri.Add(i + 1);
                tri.Add(i); tri.Add(i + 3); tri.Add(i + 2);
            }

            // Top and bottom caps: the stone patch laid flat across the disc.
            Cap(radius, top, Vector3.up, v, n, uv, tri);
            Cap(radius, bottom, Vector3.down, v, n, uv, tri);
        }

        private static void Cap(float radius, float y, Vector3 normal, List<Vector3> v, List<Vector3> n,
            List<Vector2> uv, List<int> tri)
        {
            var centre = v.Count;
            v.Add(new Vector3(0f, y, 0f)); n.Add(normal); uv.Add(StoneUv(0.5f, 0.5f));
            for (var s = 0; s <= Segments; s++)
            {
                var a = 2f * Mathf.PI * s / Segments;
                var x = Mathf.Sin(a);
                var z = Mathf.Cos(a);
                v.Add(new Vector3(x * radius, y, z * radius)); n.Add(normal); uv.Add(StoneUv(0.5f + x / 2f, 0.5f + z / 2f));
            }
            for (var s = 0; s < Segments; s++)
            {
                var a = centre + 1 + s;
                var b = centre + 2 + s;
                // Facing up: clockwise seen from above; facing down: the other way.
                if (normal.y > 0f)
                {
                    tri.Add(centre); tri.Add(a); tri.Add(b);
                }
                else
                {
                    tri.Add(centre); tri.Add(b); tri.Add(a);
                }
            }
        }
    }
}
