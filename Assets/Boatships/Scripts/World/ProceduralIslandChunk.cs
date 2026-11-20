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
        private readonly List<Material> biomeMaterials = new List<Material>();

        public void Build(ArchipelagoWorld.IslandSite site, int resolution, int worldSeed,
            Material sand, Material grass, Material rock, Material bark,
            Material leaves, Material stone, Material port, Material pirate, Material treasure)
        {
            if (site.Biome != IslandBiome.Tropical)
            {
                sand = BiomeMaterial(sand, site.Biome == IslandBiome.Frozen ? new Color(0.8f, 0.9f, 0.95f) : new Color(0.24f, 0.22f, 0.21f));
                grass = BiomeMaterial(grass, site.Biome == IslandBiome.Frozen ? new Color(0.65f, 0.8f, 0.86f) : new Color(0.34f, 0.17f, 0.12f));
                leaves = BiomeMaterial(leaves, site.Biome == IslandBiome.Frozen ? new Color(0.5f, 0.7f, 0.72f) : new Color(0.3f, 0.27f, 0.18f));
            }
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
            int trees = site.Biome == IslandBiome.Volcanic ? 2 : random.Next(7, 13);
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
            if (site.Kind == IslandKind.PortSite)
                AddPort(site, worldSeed, sand, bark, stone, port);
            else if (site.Kind != IslandKind.Deserted)
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
            float extra = site.Biome == IslandBiome.Volcanic ?
                5f * Mathf.Sin(inward * Mathf.PI) * inward : 0f;
            return baseHeight + detailed + extra;
        }

        private Material BiomeMaterial(Material source, Color color)
        {
            if (source == null) return null;
            Material instance = new Material(source) { color = color };
            biomeMaterials.Add(instance);
            return instance;
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
                // Keep the settlement and its approach clear of trees and rocks.
                Vector3 harbour = Quaternion.Inverse(PortRotation(site)) * new Vector3(x, 0f, z);
                if (site.Kind == IslandKind.PortSite && harbour.z > 0f &&
                    harbour.magnitude > site.Radius * 0.35f) continue;
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

        private static Quaternion PortRotation(ArchipelagoWorld.IslandSite site)
        {
            var random = new System.Random(site.DetailSeed);
            return Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
        }

        private static float PortShoreDistance(ArchipelagoWorld.IslandSite site, int seed, Quaternion facing)
        {
            Vector3 direction = facing * Vector3.forward;
            // Search from the sea inland so hills cannot be mistaken for the outer coast.
            for (float distance = site.Radius; distance > 0f; distance -= 0.25f)
            {
                Vector3 sample = direction * distance;
                if (Height(site, seed, sample.x, sample.z) >= 0.35f) return distance;
            }
            return site.Radius * 0.6f;
        }

        private void AddPort(ArchipelagoWorld.IslandSite site, int seed,
            Material plasterSource, Material timber, Material stone, Material port)
        {
            Quaternion facing = PortRotation(site);
            Vector3 outward = facing * Vector3.forward;
            float shore = PortShoreDistance(site, seed, facing);
            Material plaster = BiomeMaterial(plasterSource, new Color(0.86f, 0.77f, 0.58f));
            Material roof = BiomeMaterial(port != null ? port : timber, new Color(0.55f, 0.19f, 0.1f));
            Material windows = BiomeMaterial(stone, new Color(0.08f, 0.22f, 0.28f));
            Transform settlement = new GameObject("Port Village and Pier").transform;
            settlement.SetParent(transform, false);
            // Anchor the whole harbour on the coast, rather than at the island centre.
            settlement.localPosition = outward * shore;
            settlement.localRotation = facing;
            float buildingScale = Mathf.Clamp(site.Radius / 28f, 0.45f, 1f);

            for (int i = 0; i < 6; i++)
            {
                // A waterfront crescent leaves a clear approach to the pier in the middle.
                float angle = (i < 3 ? -1f : 1f) * (16f + (i % 3) * 25f);
                Quaternion houseFacing = Quaternion.Euler(0f, angle, 0f);
                Quaternion islandFacing = facing * houseFacing;
                float coast = PortShoreDistance(site, seed, islandFacing);
                float width = (i == 1 ? 5.5f : 4.5f) * buildingScale;
                float depth = 5f * buildingScale;
                float houseRadius = Mathf.Max(0f, coast - depth * 0.5f - 0.8f);
                Vector3 houseCentre = islandFacing * (Vector3.forward * houseRadius);
                float low = float.PositiveInfinity;
                float high = float.NegativeInfinity;
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 sample = houseCentre + islandFacing * new Vector3(
                        (corner % 2 == 0 ? -width : width) * 0.5f,
                        0f, (corner < 2 ? -depth : depth) * 0.5f);
                    float height = Height(site, seed, sample.x, sample.z);
                    low = Mathf.Min(low, height);
                    high = Mathf.Max(high, height);
                }
                float floor = high + 0.15f;
                Transform house = new GameObject(i == 1 ? "Harbour Tavern" : "Port House " + (i + 1)).transform;
                house.SetParent(settlement, false);
                Vector3 harbourPosition = Quaternion.Inverse(facing) * houseCentre - Vector3.forward * shore;
                house.localPosition = new Vector3(harbourPosition.x, floor, harbourPosition.z);
                house.localRotation = houseFacing;
                Block("Stone foundation", house, new Vector3(0f, (low - floor) * 0.5f, 0f),
                    new Vector3(width + 0.3f, floor - low + 0.2f, depth + 0.3f), stone);
                Block("Plaster walls", house, new Vector3(0f, 1.6f, 0f), new Vector3(width, 3.2f, depth), plaster);
                for (int side = -1; side <= 1; side += 2)
                {
                    Transform slope = Block("Pitched tile roof", house, new Vector3(side * (width * 0.25f + 0.15f), 3.8f, 0f),
                        new Vector3((width * 0.5f + 0.5f) / Mathf.Cos(30f * Mathf.Deg2Rad), 0.22f, depth + 0.8f), roof);
                    slope.localRotation = Quaternion.Euler(0f, 0f, -side * 30f);
                    Block("Front window", house, new Vector3(side * width * 0.31f, 1.9f, depth * 0.5f + 0.04f),
                        new Vector3(0.8f, 1f, 0.12f), windows);
                }
                Block("Timber door", house, new Vector3(0f, 1f, depth * 0.5f + 0.05f), new Vector3(1f, 2f, 0.15f), timber);
                Block("Chimney", house, new Vector3(width * 0.25f, 4.3f, -1f), new Vector3(0.65f, 1.8f, 0.65f), stone);
            }

            const float deck = 1.7f;
            float start = -2f;
            // Reach beyond the navigation limiter so a ship can approach the pier head.
            float end = Mathf.Max(12f, site.Radius * 0.78f + 8f - shore);
            for (float z = start; z <= end; z += 0.7f)
                Block("Pier deck plank", settlement, new Vector3(0f, deck, z), new Vector3(3.6f, 0.24f, 0.64f), timber);
            for (float z = start; z <= end; z += 3f)
            for (int side = -1; side <= 1; side += 2)
            {
                Block("Pier piling", settlement, new Vector3(side * 1.45f, 0f, z), new Vector3(0.35f, 4.3f, 0.35f), timber);
                Block("Pier side beam", settlement, new Vector3(side * 1.45f, 1.35f, z + 1.5f), new Vector3(0.25f, 0.3f, 3f), timber);
            }
            for (float z = end - 1.8f; z <= end + 1.8f; z += 0.6f)
                Block("Pier head plank", settlement, new Vector3(0f, deck, z), new Vector3(8f, 0.24f, 0.54f), timber);
            for (int side = -1; side <= 1; side += 2)
                Block("Mooring post", settlement, new Vector3(side * 3.4f, 1.1f, end), new Vector3(0.45f, 3.4f, 0.45f), timber);
            AddFort(site, seed, settlement, facing, stone, timber, windows);
        }

        private void AddFort(ArchipelagoWorld.IslandSite site, int seed, Transform settlement,
            Quaternion harbourFacing, Material stone, Material timber, Material dark)
        {
            // Place the bastion beside the waterfront crescent, clear of houses and the pier.
            Quaternion facing = harbourFacing * Quaternion.Euler(0f, 100f, 0f);
            float coast = PortShoreDistance(site, seed, facing);
            Vector3 centre = facing * (Vector3.forward * Mathf.Max(0f, coast - 3.5f));
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 sample = centre + facing * new Vector3(corner % 2 == 0 ? -3f : 3f,
                    0f, corner < 2 ? -3f : 3f);
                float height = Height(site, seed, sample.x, sample.z);
                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);
            }
            Transform fort = new GameObject("Harbour Fort").transform;
            fort.SetParent(settlement, false);
            Vector3 islandPosition = new Vector3(centre.x, high + 0.2f, centre.z);
            fort.position = transform.TransformPoint(islandPosition);
            fort.rotation = transform.rotation * facing;
            Block("Fort foundation", fort, new Vector3(0f, (low - high) * 0.5f - 0.1f, 0f),
                new Vector3(6.6f, high - low + 0.4f, 6.6f), stone);
            Block("Stone bastion", fort, Vector3.up * 1.25f, new Vector3(6f, 2.5f, 6f), stone);
            Block("Battery deck", fort, Vector3.up * 2.6f, new Vector3(6.4f, 0.25f, 6.4f), stone);
            Block("Fort gate", fort, new Vector3(0f, 0.9f, -3.05f), new Vector3(1.3f, 1.8f, 0.15f), timber);
            for (int side = -1; side <= 1; side += 2)
            {
                Block("Fort parapet", fort, new Vector3(side * 3f, 3f, 0f), new Vector3(0.4f, 0.7f, 6.4f), stone);
                Block("Fort parapet", fort, new Vector3(0f, 3f, side * 3f), new Vector3(6.4f, 0.7f, 0.4f), stone);
                for (int i = -2; i <= 2; i++)
                {
                    Block("Battlement", fort, new Vector3(side * 3f, 3.6f, i * 1.3f), new Vector3(0.6f, 0.6f, 0.6f), stone);
                    Block("Battlement", fort, new Vector3(i * 1.3f, 3.6f, side * 3f), new Vector3(0.6f, 0.6f, 0.6f), stone);
                }
            }
            Transform[] muzzles = new Transform[2];
            for (int i = 0; i < muzzles.Length; i++)
            {
                Vector3 position = new Vector3(i == 0 ? -1.4f : 1.4f, 3.5f, 1.2f);
                Block("Cannon carriage", fort, position + Vector3.down * 0.5f, new Vector3(0.8f, 0.5f, 1.4f), timber);
                Transform pivot = new GameObject("Fort Cannon " + (i + 1)).transform;
                pivot.SetParent(fort, false);
                pivot.localPosition = position;
                GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                barrel.name = "Iron barrel";
                barrel.transform.SetParent(pivot, false);
                barrel.transform.localPosition = Vector3.forward * 0.8f;
                barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                barrel.transform.localScale = new Vector3(0.45f, 0.9f, 0.45f);
                barrel.GetComponent<MeshRenderer>().sharedMaterial = dark;
                Destroy(barrel.GetComponent<Collider>());
                muzzles[i] = new GameObject("Muzzle").transform;
                muzzles[i].SetParent(pivot, false);
                muzzles[i].localPosition = Vector3.forward * 1.8f;
            }
            HarbourFort battery = fort.gameObject.AddComponent<HarbourFort>();
            battery.Configure(settlement.TransformPoint(Vector3.forward * 6f), muzzles, FindAnyObjectByType<OceanWaves>());
        }

        private static Transform Block(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = material;
            Destroy(block.GetComponent<Collider>());
            return block.transform;
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
            foreach (Material material in biomeMaterials) if (material != null) Destroy(material);
        }
    }
}
