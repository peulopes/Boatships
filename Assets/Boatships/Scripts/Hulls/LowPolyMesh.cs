using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// Collects flat-shaded low-poly shapes (boxes, beams, boards, prisms) into one mesh with a submesh
    /// per material, so a detailed ship part is one renderer instead of dozens of primitives.
    /// Faces follow Unity's winding: Cross(b - a, c - a) points out of the visible side.
    /// </summary>
    public sealed class LowPolyMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int>[] triangles;

        public LowPolyMesh(int submeshes)
        {
            triangles = new List<int>[Mathf.Max(1, submeshes)];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>();
        }

        public int SubmeshCount => triangles.Length;

        public void Clear()
        {
            vertices.Clear();
            normals.Clear();
            foreach (List<int> list in triangles) list.Clear();
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, int sub)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f) return;
            normal.Normalize();
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            List<int> list = triangles[sub];
            list.Add(start); list.Add(start + 1); list.Add(start + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int sub)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a);
            if (normal.sqrMagnitude < 1e-12f) return;
            normal.Normalize();
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            List<int> list = triangles[sub];
            list.Add(start); list.Add(start + 1); list.Add(start + 2);
            list.Add(start); list.Add(start + 2); list.Add(start + 3);
        }

        /// <summary>Six-sided solid. Corner index bits: 1 = +x, 2 = +y, 4 = +z (in the solid's own frame).</summary>
        public void Hexa(Vector3[] p, int sub)
        {
            Quad(p[1], p[3], p[7], p[5], sub);
            Quad(p[4], p[6], p[2], p[0], sub);
            Quad(p[2], p[6], p[7], p[3], sub);
            Quad(p[4], p[0], p[1], p[5], sub);
            Quad(p[5], p[7], p[6], p[4], sub);
            Quad(p[0], p[2], p[3], p[1], sub);
        }

        private readonly Vector3[] corners = new Vector3[8];

        public void Box(Vector3 center, Quaternion rotation, Vector3 size, int sub)
        {
            for (int i = 0; i < 8; i++)
                corners[i] = center + rotation * Vector3.Scale(size,
                    new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f));
            Hexa(corners, sub);
        }

        public void Box(Vector3 center, Vector3 size, int sub) => Box(center, Quaternion.identity, size, sub);

        /// <summary>A square-section timber or rope from a to b, overlapping its ends so joints close.</summary>
        public void Beam(Vector3 a, Vector3 b, float width, float height, int sub, Vector3 upHint = default)
        {
            Vector3 along = b - a;
            if (along.sqrMagnitude < 1e-8f) return;
            if (upHint == default) upHint = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(along.normalized, upHint.normalized)) > 0.98f)
                upHint = Mathf.Abs(along.normalized.y) > 0.9f ? Vector3.forward : Vector3.up;
            Box((a + b) * 0.5f, Quaternion.LookRotation(along, upHint),
                new Vector3(width, height, along.magnitude + Mathf.Min(width, height)), sub);
        }

        /// <summary>A plank standing on the line a-b, heightA tall at a and heightB tall at b.</summary>
        public void Board(Vector3 a, Vector3 b, float heightA, float heightB, float thickness, int sub,
            Vector3 up = default)
        {
            if (up == default) up = Vector3.up;
            Vector3 side = Vector3.Cross(up, b - a);
            if (side.sqrMagnitude < 1e-10f) return;
            side = side.normalized * (thickness * 0.5f);
            for (int i = 0; i < 8; i++)
            {
                bool atB = (i & 4) != 0;
                corners[i] = (atB ? b : a) + ((i & 2) != 0 ? up * (atB ? heightB : heightA) : Vector3.zero) +
                    ((i & 1) != 0 ? side : -side);
            }
            Hexa(corners, sub);
        }

        /// <summary>A faceted cylinder or cone frustum standing on baseCenter along rotation's up axis.</summary>
        public void Prism(Vector3 baseCenter, Quaternion rotation, float bottomRadius, float topRadius, float height,
            int sides, int sub, bool caps = true)
        {
            Vector3 axis = rotation * Vector3.up * height;
            Vector3 top = baseCenter + axis;
            for (int i = 0; i < sides; i++)
            {
                Vector3 d0 = rotation * Direction(i, sides), d1 = rotation * Direction(i + 1, sides);
                Vector3 b0 = baseCenter + d0 * bottomRadius, b1 = baseCenter + d1 * bottomRadius;
                Vector3 t0 = top + d0 * topRadius, t1 = top + d1 * topRadius;
                Quad(b0, t0, t1, b1, sub);
                if (!caps) continue;
                if (topRadius > 0.0001f) Triangle(top, t1, t0, sub);
                if (bottomRadius > 0.0001f) Triangle(baseCenter, b0, b1, sub);
            }
        }

        public void Prism(Vector3 baseCenter, float bottomRadius, float topRadius, float height, int sides, int sub) =>
            Prism(baseCenter, Quaternion.identity, bottomRadius, topRadius, height, sides, sub);

        /// <summary>A faceted cylinder lying between two points (spars, axles, barrels).</summary>
        public void Rod(Vector3 a, Vector3 b, float radiusA, float radiusB, int sides, int sub)
        {
            Vector3 along = b - a;
            if (along.sqrMagnitude < 1e-8f) return;
            Prism(a, Quaternion.FromToRotation(Vector3.up, along), radiusA, radiusB, along.magnitude, sides, sub);
        }

        private static Vector3 Direction(int i, int sides)
        {
            float angle = (i + 0.5f) * Mathf.PI * 2f / sides;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        public void AppendTo(LowPolyMesh target)
        {
            int offset = target.vertices.Count;
            target.vertices.AddRange(vertices);
            target.normals.AddRange(normals);
            for (int s = 0; s < triangles.Length && s < target.triangles.Length; s++)
                foreach (int index in triangles[s]) target.triangles[s].Add(index + offset);
        }

        public void WriteTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 :
                UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = triangles.Length;
            for (int s = 0; s < triangles.Length; s++) mesh.SetTriangles(triangles[s], s, false);
            mesh.RecalculateBounds();
        }

        /// <summary>Makes a child object rendering this mesh; the mesh is destroyed with the object.</summary>
        public GameObject Create(string name, Transform parent, Vector3 localPosition, Quaternion localRotation,
            Material[] materials)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            var mesh = new Mesh { name = name };
            WriteTo(mesh);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = materials;
            go.AddComponent<LowPolyMeshOwner>().Own(mesh);
            return go;
        }

        private static Mesh sail;

        /// <summary>Shared unit sail (1 × 1, bellied towards +z by up to 1): scale it to the sail's size and depth.
        /// Both faces are drawn so the canvas is visible from either side.</summary>
        public static Mesh SailMesh
        {
            get
            {
                if (sail != null) return sail;
                const int cols = 4, rows = 3;
                var builder = new LowPolyMesh(1);
                Vector3 Point(int c, int r)
                {
                    float x = (float)c / cols - 0.5f, y = 0.5f - (float)r / rows, down = (float)r / rows;
                    float belly = (1f - 0.8f * 4f * x * x) * (0.25f + 0.75f * Mathf.Sin(down * Mathf.PI * 0.5f));
                    return new Vector3(x, y, belly);
                }
                for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                {
                    Vector3 a = Point(c, r + 1), b = Point(c, r), e = Point(c + 1, r), f = Point(c + 1, r + 1);
                    builder.Triangle(a, b, e, 0); builder.Triangle(a, e, f, 0);
                    builder.Triangle(a, e, b, 0); builder.Triangle(a, f, e, 0);
                }
                sail = new Mesh { name = "Boatships Sail", hideFlags = HideFlags.DontSave };
                builder.WriteTo(sail);
                return sail;
            }
        }
    }

    /// <summary>Shared materials for the low-poly ship style.</summary>
    public static class ShipPalette
    {
        public static readonly Color Trim = new Color(0.86f, 0.6f, 0.3f);
        public static readonly Color Spar = new Color(0.8f, 0.55f, 0.27f);
        public static readonly Color Deck = new Color(0.68f, 0.61f, 0.52f);
        public static readonly Color DeckAlt = new Color(0.58f, 0.52f, 0.44f);
        public static readonly Color DarkWood = new Color(0.33f, 0.2f, 0.11f);
        public static readonly Color Iron = new Color(0.32f, 0.33f, 0.35f);
        public static readonly Color Rope = new Color(0.55f, 0.43f, 0.27f);
        public static readonly Color Lamp = new Color(1f, 0.74f, 0.34f);

        private static readonly Dictionary<long, Material> solids = new Dictionary<long, Material>();
        private static readonly Dictionary<Material, Material> lighter = new Dictionary<Material, Material>();

        public static Material Solid(Color color) => Make(color, false);
        public static Material Glow(Color color) => Make(color, true);

        private static Material Make(Color color, bool glow)
        {
            Color32 c = color;
            long key = ((long)c.r << 16) | ((long)c.g << 8) | c.b | (glow ? 1L << 32 : 0L);
            if (solids.TryGetValue(key, out Material cached) && cached != null) return cached;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"))
                { name = glow ? "Ship Lamp" : "Ship Paint", color = color };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.12f);
            if (glow)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.6f);
            }
            solids[key] = material;
            return material;
        }

        /// <summary>A lighter shade of a hull material, for alternating planks.</summary>
        public static Material Lighter(Material source)
        {
            if (source == null) return null;
            if (lighter.TryGetValue(source, out Material cached) && cached != null) return cached;
            Color c = source.color;
            var material = new Material(source)
            {
                name = source.name + " Strake",
                color = new Color(Mathf.Min(1f, c.r * 1.28f + 0.02f), Mathf.Min(1f, c.g * 1.28f + 0.015f),
                    Mathf.Min(1f, c.b * 1.28f + 0.01f), c.a)
            };
            lighter[source] = material;
            return material;
        }
    }
}
