using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>One island = one terrain mesh (three material regions) plus a few
    /// lightweight decoration objects. Meshes are disposed when chunks unload.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralIslandChunk : MonoBehaviour
    {
        private Mesh terrainMesh;
        private Mesh treeCrownMesh;

        public void Build(ArchipelagoWorld.IslandSite site, int resolution, int worldSeed,
            Material sand, Material grass, Material rock, Material bark,
            Material leaves, Material stone, Material port, Material pirate, Material treasure)
        {
            resolution = Mathf.Max(8, resolution);
            float radius = site.Radius;
            float total = radius * 2f;
            int row = resolution + 1;
            Vector3[] positions = new Vector3[row * row];
            Vector2[] uv = new Vector2[positions.Length];
            List<int>[] indices = { new List<int>(), new List<int>(), new List<int>() };
            for (int z = 0; z < row; z++)
            for (int x = 0; x < row; x++)
            {
                float lx = (x / (float)resolution - 0.5f) * total;
                float lz = (z / (float)resolution - 0.5f) * total;
                int idx = z * row + x;
                positions[idx] = new Vector3(lx, Height(site, worldSeed, lx, lz), lz);
                uv[idx] = new Vector2(x / (float)resolution, z / (float)resolution);
            }

            // Assign entire triangles to sand, grass, or rock (URP/Lit does not
            // read mesh vertex colors by default).
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int a = z * row + x;
                int b = a + 1;
                int c = a + row;
                int d = c + 1;
                AddTriangle(a, c, b, positions, indices);
                AddTriangle(b, c, d, positions, indices);
            }

            terrainMesh = new Mesh { name = $"Island_{site.Cell.x}_{site.Cell.y}_Terrain" };
            // With resolution <= 48 the default 16-bit index buffer is sufficient.
            terrainMesh.vertices = positions;
            terrainMesh.uv = uv;
            terrainMesh.subMeshCount = 3;
            for (int i = 0; i < 3; i++) terrainMesh.SetTriangles(indices[i], i);
            terrainMesh.RecalculateNormals();
            terrainMesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = terrainMesh;
            GetComponent<MeshRenderer>().sharedMaterials = new[] { sand, grass, rock };

            // Static terrain collider for future on-foot exploration. The arcade
            // ship is kinematic, so shore navigation is handled separately.
            MeshCollider collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = terrainMesh;

            System.Random random = new System.Random(site.DetailSeed);
            treeCrownMesh = CreateCone(8);
            int trees = random.Next(7, 13);
            int rocks = random.Next(2, 5);
            for (int i = 0; i < trees; i++)
            {
                if (!TryDecorationPoint(site, worldSeed, random, out Vector3 point)) continue;
                float scale = Mathf.Lerp(0.8f, 1.5f, (float)random.NextDouble());
                AddTree(point, scale, bark, leaves);
            }
            for (int i = 0; i < rocks; i++)
            {
                if (!TryDecorationPoint(site, worldSeed, random, out Vector3 point)) continue;
                GameObject pebble = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pebble.name = "Low Poly Rock";
                pebble.transform.SetParent(transform, false);
                pebble.transform.localPosition = point + Vector3.up * 0.24f;
                pebble.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 180f, 15f);
                pebble.transform.localScale = new Vector3(0.8f, 0.5f, 0.6f);
                pebble.GetComponent<MeshRenderer>().sharedMaterial = stone;
                Destroy(pebble.GetComponent<Collider>());
            }
            if (site.Kind != IslandKind.Deserted)
                AddLandmark(site, worldSeed, port, pirate, treasure);
        }

        private static void AddTriangle(int a, int b, int c, Vector3[] positions, List<int>[] indices)
        {
            float height = (positions[a].y + positions[b].y + positions[c].y) / 3f;
            int submesh = height < 1.45f ? 0 : (height > 4.3f ? 2 : 1);
            indices[submesh].Add(a);
            indices[submesh].Add(b);
            indices[submesh].Add(c);
        }

        public static float Height(ArchipelagoWorld.IslandSite site, int seed, float localX, float localZ)
        {
            float worldX = site.Center.x + localX;
            float worldZ = site.Center.y + localZ;
            float noise = Mathf.PerlinNoise(
                worldX * 0.065f + seed * 0.00017f + 43.19f,
                worldZ * 0.065f - seed * 0.00013f + 81.71f);
            // Noise perturbs coastlines as well as hills. At the mesh square's
            // corners the elevation stays underwater, hiding its square border.
            float distance = new Vector2(localX, localZ).magnitude;
            float warpedRadius = site.Radius * (0.94f + (noise - 0.5f) * 0.19f);
            float inward = Mathf.Clamp01(1f - distance / warpedRadius);
            float baseHeight = -1.6f + 8.5f * Mathf.Pow(inward, 1.22f);
            float detailed = (noise - 0.5f) * 2.2f * inward;
            return baseHeight + detailed;
        }

        private bool TryDecorationPoint(ArchipelagoWorld.IslandSite site, int seed,
            System.Random rng, out Vector3 local)
        {
            for (int attempts = 0; attempts < 12; attempts++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float distance = Mathf.Sqrt((float)rng.NextDouble()) * site.Radius * 0.60f;
                float x = Mathf.Sin(angle) * distance;
                float z = Mathf.Cos(angle) * distance;
                float y = Height(site, seed, x, z);
                if (y < 1.0f || y > 4.65f) continue;
                local = new Vector3(x, y, z);
                return true;
            }
            local = Vector3.zero;
            return false;
        }

        private void AddTree(Vector3 basePoint, float scale, Material bark, Material leaves)
        {
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Tree Trunk";
            trunk.transform.SetParent(transform, false);
            trunk.transform.localPosition = basePoint + Vector3.up * (0.75f * scale);
            trunk.transform.localScale = new Vector3(0.17f * scale, 0.75f * scale, 0.17f * scale);
            trunk.GetComponent<MeshRenderer>().sharedMaterial = bark;
            Destroy(trunk.GetComponent<Collider>());

            GameObject crown = new GameObject("Low Poly Canopy", typeof(MeshFilter), typeof(MeshRenderer));
            crown.transform.SetParent(transform, false);
            crown.transform.localPosition = basePoint + Vector3.up * (1.18f * scale);
            crown.transform.localScale = Vector3.one * scale;
            crown.GetComponent<MeshFilter>().sharedMesh = treeCrownMesh;
            crown.GetComponent<MeshRenderer>().sharedMaterial = leaves;
        }

        private void AddLandmark(ArchipelagoWorld.IslandSite site, int seed,
            Material port, Material pirate, Material treasure)
        {
            Material chosen = site.Kind == IslandKind.PortSite ? port :
                site.Kind == IslandKind.PirateBaseSite ? pirate : treasure;
            float y = Height(site, seed, 0f, 0f);
            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = site.Kind + " Placeholder Marker";
            pole.transform.SetParent(transform, false);
            pole.transform.localPosition = new Vector3(0f, y + 1.6f, 0f);
            pole.transform.localScale = new Vector3(0.15f, 1.6f, 0.15f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = chosen;
            Destroy(pole.GetComponent<Collider>());
            GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = "Marker Flag";
            flag.transform.SetParent(transform, false);
            flag.transform.localPosition = new Vector3(0.6f, y + 2.7f, 0f);
            flag.transform.localScale = new Vector3(1.2f, 0.6f, 0.12f);
            flag.GetComponent<MeshRenderer>().sharedMaterial = chosen;
            Destroy(flag.GetComponent<Collider>());
        }

        private static Mesh CreateCone(int sides)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Vector3 tip = new Vector3(0f, 2.0f, 0f);
            for (int i = 0; i < sides; i++)
            {
                float a = 2f * Mathf.PI * i / sides;
                float b = 2f * Mathf.PI * (i + 1) / sides;
                vertices.Add(new Vector3(Mathf.Cos(a) * 0.75f, 0f, Mathf.Sin(a) * 0.75f));
                vertices.Add(tip);
                vertices.Add(new Vector3(Mathf.Cos(b) * 0.75f, 0f, Mathf.Sin(b) * 0.75f));
                int idx = vertices.Count - 3;
                triangles.Add(idx);
                triangles.Add(idx + 1);
                triangles.Add(idx + 2);
            }
            Mesh cone = new Mesh { name = "Boatships 8-sided Tree Canopy" };
            cone.SetVertices(vertices);
            cone.SetTriangles(triangles, 0);
            cone.RecalculateNormals();
            return cone;
        }

        private void OnDestroy()
        {
            if (terrainMesh != null) Destroy(terrainMesh);
            if (treeCrownMesh != null) Destroy(treeCrownMesh);
        }
    }
}
