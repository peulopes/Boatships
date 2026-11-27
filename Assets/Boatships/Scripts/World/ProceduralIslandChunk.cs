using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>Overall geography of an island. See <see cref="ProceduralIslandChunk.ShapeOf"/>.</summary>
    public enum IslandShape { Rolling, Peak, Atoll, Crescent, Twin, Ridge, Mesa, Lowland }

    /// <summary>One island = one terrain mesh (three material regions) plus a few
    /// lightweight decoration objects. Meshes are disposed when chunks unload.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralIslandChunk : MonoBehaviour
    {
        private Mesh terrainMesh;
        private Mesh treeCrownMesh;
        // Shared by every island: tinted biome materials and the canopy mesh used to be created per island
        // each time it streamed in.
        private static readonly Dictionary<long, Material> biomeMaterials = new Dictionary<long, Material>();
        private static Mesh sharedCrownMesh;

        public void Build(ArchipelagoWorld.IslandSite site, int resolution, int worldSeed,
            Material sand, Material grass, Material rock, Material bark,
            Material leaves, Material stone, Material port, Material pirate, Material treasure)
        {
            IslandShape shape = ShapeOf(site);
            if (site.Biome != IslandBiome.Tropical)
            {
                sand = BiomeMaterial(sand, site.Biome == IslandBiome.Frozen ? new Color(0.8f, 0.9f, 0.95f) : new Color(0.24f, 0.22f, 0.21f));
                grass = BiomeMaterial(grass, site.Biome == IslandBiome.Frozen ? new Color(0.65f, 0.8f, 0.86f) : new Color(0.34f, 0.17f, 0.12f));
                leaves = BiomeMaterial(leaves, site.Biome == IslandBiome.Frozen ? new Color(0.5f, 0.7f, 0.72f) : new Color(0.3f, 0.27f, 0.18f));
                if (site.Biome == IslandBiome.Frozen) rock = BiomeMaterial(rock, new Color(0.9f, 0.94f, 0.97f)); // snow caps and icy cliffs
            }
            // Cliffs, craters and reefs need a finer grid than gentle hills.
            resolution = Mathf.Clamp(Mathf.Max(resolution, 36), 8, 48);
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
            // read mesh vertex colors by default). Steep faces are always rock.
            float rockLine = RockLine(shape);
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int a = z * row + x;
                int b = a + 1;
                int c = a + row;
                int d = c + 1;
                AddTriangle(a, c, b, positions, indices, rockLine);
                AddTriangle(b, c, d, positions, indices, rockLine);
            }

            terrainMesh = new Mesh { name = $"Island_{site.Cell.x}_{site.Cell.y}_{shape}_Terrain" };
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
            if (sharedCrownMesh == null) sharedCrownMesh = CreateCone(8);
            treeCrownMesh = sharedCrownMesh;
            DecorationRange(shape, out float minY, out float maxY);
            bool palms = site.Biome == IslandBiome.Tropical &&
                (shape == IslandShape.Atoll || shape == IslandShape.Lowland || shape == IslandShape.Crescent || shape == IslandShape.Twin);
            int trees = site.Biome == IslandBiome.Volcanic ? random.Next(1, 4) : TreeCount(shape, random);
            int rocks = shape == IslandShape.Peak || shape == IslandShape.Ridge ? random.Next(5, 10) :
                shape == IslandShape.Atoll || shape == IslandShape.Lowland ? random.Next(0, 3) : random.Next(2, 6);
            for (int i = 0; i < trees; i++)
            {
                if (!TryDecorationPoint(site, worldSeed, random, minY, maxY, out Vector3 point)) continue;
                float scale = Mathf.Lerp(0.8f, 1.5f, (float)random.NextDouble());
                bool palm = palms && (shape != IslandShape.Crescent || random.NextDouble() < 0.6);
                if (palm) AddPalm(point, scale, random, bark, leaves);
                else AddTree(point, scale, bark, leaves);
            }
            for (int i = 0; i < rocks; i++)
            {
                if (!TryDecorationPoint(site, worldSeed, random, 0.6f, 99f, out Vector3 point)) continue;
                float size = Mathf.Lerp(0.6f, shape == IslandShape.Peak || shape == IslandShape.Ridge ? 1.8f : 1.1f, (float)random.NextDouble());
                GameObject pebble = BoatshipsPrimitives.Create(PrimitiveType.Cube);
                pebble.name = "Low Poly Rock";
                pebble.transform.SetParent(transform, false);
                pebble.transform.localPosition = point + Vector3.up * (0.2f * size);
                pebble.transform.localRotation = Quaternion.Euler((float)random.NextDouble() * 30f, (float)random.NextDouble() * 180f, 15f);
                pebble.transform.localScale = new Vector3(0.8f, 0.5f, 0.6f) * size;
                pebble.GetComponent<MeshRenderer>().sharedMaterial = stone;
            }
            if (shape == IslandShape.Mesa || shape == IslandShape.Ridge || shape == IslandShape.Peak ||
                (shape == IslandShape.Crescent && site.Biome != IslandBiome.Tropical))
                AddSeaStacks(site, worldSeed, random, rock);
            if (shape == IslandShape.Peak && site.Biome == IslandBiome.Volcanic) AddLava(site, worldSeed);
            if (site.Kind == IslandKind.PortSite)
                AddPort(site, worldSeed, sand, bark, stone, port);
            else if (site.Kind != IslandKind.Deserted)
                AddLandmark(site, worldSeed, port, pirate, treasure);
            // Hundreds of trunk, frond and rock primitives become one renderer per material.
            BoatshipsTown harbour = GetComponent<BoatshipsTown>();
            Transform townRoot = harbour != null ? harbour.Root : null;
            BoatshipsMeshBatcher.Combine(transform, townRoot != null ? new[] { townRoot } : null);
        }

        private static void AddTriangle(int a, int b, int c, Vector3[] positions, List<int>[] indices, float rockLine)
        {
            float height = (positions[a].y + positions[b].y + positions[c].y) / 3f;
            Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).normalized;
            bool steep = Mathf.Abs(normal.y) < 0.6f && height > 0.9f;
            int submesh = steep || height > rockLine ? 2 : (height < 1.45f ? 0 : 1);
            indices[submesh].Add(a);
            indices[submesh].Add(b);
            indices[submesh].Add(c);
        }

        // ───────────────────────────── Island shapes ─────────────────────────────
        // Every shape keeps its coastline inside about 0.75 × Radius, because ship navigation,
        // docking and arrival all treat an island as a circle of 0.78 × Radius.

        /// <summary>Overall geography of an island, chosen from its seed (and biome).</summary>
        public static IslandShape ShapeOf(ArchipelagoWorld.IslandSite site)
        {
            float roll = Param(site, 1);
            // Ports need a gentle shore for the town and pier.
            if (site.Kind == IslandKind.PortSite)
                return roll < 0.45f ? IslandShape.Rolling : roll < 0.75f ? IslandShape.Crescent : IslandShape.Lowland;
            float[] weights = ShapeWeights[Mathf.Clamp((int)site.Biome, 0, ShapeWeights.Length - 1)];
            float sum = 0f;
            foreach (float w in weights) sum += w;
            roll *= sum;
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll < 0f) return (IslandShape)i;
            }
            return IslandShape.Rolling;
        }

        // Rolling, Peak, Atoll, Crescent, Twin, Ridge, Mesa, Lowland
        private static readonly float[][] ShapeWeights =
        {
            new[] { 1f, 1f, 2f, 1.5f, 1.5f, 0.6f, 0.6f, 1.5f },  // Tropical
            new[] { 0.8f, 3f, 0f, 1f, 1f, 1f, 1.6f, 0f },        // Volcanic
            new[] { 0.8f, 2f, 0f, 1.6f, 1f, 2.4f, 1f, 0.4f }     // Frozen
        };

        /// <summary>Short noun for an island's geography, e.g. "atoll" or "volcano".</summary>
        public static string ShapeName(ArchipelagoWorld.IslandSite site)
        {
            switch (ShapeOf(site))
            {
                case IslandShape.Peak: return site.Biome == IslandBiome.Volcanic ? "volcano" : "mountain island";
                case IslandShape.Atoll: return "atoll";
                case IslandShape.Crescent: return "horseshoe island";
                case IslandShape.Twin: return "cluster of islets";
                case IslandShape.Ridge: return "ridge island";
                case IslandShape.Mesa: return "table-top island";
                case IslandShape.Lowland: return "sandy cay";
                default: return "hill island";
            }
        }

        /// <summary>One sentence describing what a captain sees when approaching the island.</summary>
        public static string ShapeDescription(ArchipelagoWorld.IslandSite site)
        {
            switch (ShapeOf(site))
            {
                case IslandShape.Peak:
                    return site.Biome == IslandBiome.Volcanic ? "A smoking cone rises from the sea, its crater glowing red."
                        : "A lone mountain climbs steeply out of the water.";
                case IslandShape.Atoll: return "A ring of reef and palms encloses a calm, shallow lagoon.";
                case IslandShape.Crescent: return "High headlands curl around a sheltered bay.";
                case IslandShape.Twin: return "Several small islets sit close together, split by narrow channels.";
                case IslandShape.Ridge: return "A long, jagged ridge runs the length of the island.";
                case IslandShape.Mesa: return "Sheer cliffs climb from a narrow beach to a flat summit.";
                case IslandShape.Lowland: return "Low dunes and salt ponds barely rise above the waves.";
                default: return "Green hills roll down to sandy beaches.";
            }
        }

        public static float Height(ArchipelagoWorld.IslandSite site, int seed, float localX, float localZ)
        {
            float raw = ShapeHeight(site, seed, localX, localZ);
            return site.Kind == IslandKind.PortSite ? TownTerraceHeight(site, seed, localX, localZ, raw) : raw;
        }

        // ───────────────────────────── Town terrace ─────────────────────────────
        // Port islands level a shelf of ground behind the harbour beach so the town, its square and the fort
        // stand on (almost) flat land. A short ramp runs from the shelf down to the beach and pier.

        private struct TownTerrace { public Vector2 Centre, Outward; public float Level, Reference, RampStart, RampEnd; }

        private const float TerraceInset = 3.6f;      // shelf centre, inland from the natural shore
        private const float TerraceFlatRadius = 10f;  // fully levelled (covers the square and both house rings)
        private const float TerraceFadeRadius = 14.5f; // back to natural terrain
        private static readonly Dictionary<(int, int, Vector2, float), TownTerrace> Terraces =
            new Dictionary<(int, int, Vector2, float), TownTerrace>();

        /// <summary>Island-local centre of the levelled town shelf on a port island.</summary>
        public static Vector2 TownCentre(ArchipelagoWorld.IslandSite site, int seed) => TerraceOf(site, seed).Centre;

        private static TownTerrace TerraceOf(ArchipelagoWorld.IslandSite site, int seed)
        {
            var key = (site.DetailSeed, seed, site.Center, site.Radius);
            if (Terraces.TryGetValue(key, out TownTerrace terrace)) return terrace;
            if (Terraces.Count > 256) Terraces.Clear();
            Quaternion facing = PortRotation(site);
            Vector3 direction = facing * Vector3.forward;
            Vector2 outward = new Vector2(direction.x, direction.z);
            float shore = PortShoreDistance(site, seed, facing, true);
            Vector2 centre = outward * Mathf.Max(site.Radius * 0.2f, shore - TerraceInset);
            float reference = ShapeHeight(site, seed, centre.x, centre.y);
            float sum = reference;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                sum += ShapeHeight(site, seed, centre.x + Mathf.Sin(a) * 5f, centre.y + Mathf.Cos(a) * 5f);
            }
            terrace = new TownTerrace
            {
                Centre = centre, Outward = outward, Reference = reference,
                Level = Mathf.Clamp(sum / 9f, 1.4f, 2.5f),
                RampStart = 1.4f
            };
            terrace.RampEnd = Mathf.Max(terrace.RampStart + 1.8f, shore - centre.magnitude);
            Terraces[key] = terrace;
            return terrace;
        }

        private static float TownTerraceHeight(ArchipelagoWorld.IslandSite site, int seed, float x, float z, float raw)
        {
            TownTerrace t = TerraceOf(site, seed);
            Vector2 p = new Vector2(x, z);
            float distance = (p - t.Centre).magnitude;
            if (distance >= TerraceFadeRadius) return raw;
            // Nearly flat: keep a whisper of the natural undulation so the shelf does not look machined.
            float shelf = t.Level + Mathf.Clamp((raw - t.Reference) * 0.08f, -0.25f, 0.25f);
            // Seaward of the shelf, slope evenly down to the beach (0.35 at the natural shore) and the sea floor.
            float along = Vector2.Dot(p - t.Centre, t.Outward);
            float slope = (t.Level - 0.35f) / (t.RampEnd - t.RampStart);
            float target = Mathf.Max(-1.6f, shelf - slope * Mathf.Max(0f, along - t.RampStart));
            // Never raise new land out at the ship navigation circle.
            if (target > raw) target = Mathf.Lerp(raw, target, Smooth(site.Radius * 0.8f, site.Radius * 0.7f, p.magnitude));
            return Mathf.Lerp(raw, target, Smooth(TerraceFadeRadius, TerraceFlatRadius, distance));
        }

        private static float ShapeHeight(ArchipelagoWorld.IslandSite site, int seed, float localX, float localZ)
        {
            float worldX = site.Center.x + localX;
            float worldZ = site.Center.y + localZ;
            float noise = Mathf.PerlinNoise(
                worldX * 0.065f + seed * 0.00017f + 43.19f,
                worldZ * 0.065f - seed * 0.00013f + 81.71f);
            float fine = Mathf.PerlinNoise(
                worldX * 0.16f + seed * 0.00011f + 17.3f,
                worldZ * 0.16f - seed * 0.00019f + 5.9f);
            float distance = new Vector2(localX, localZ).magnitude;
            float r = site.Radius;
            switch (ShapeOf(site))
            {
                case IslandShape.Peak: return PeakHeight(site, localX, localZ, distance, r, noise, fine);
                case IslandShape.Atoll: return AtollHeight(site, localX, localZ, distance, r, noise, fine);
                case IslandShape.Crescent: return CrescentHeight(site, localX, localZ, distance, r, noise);
                case IslandShape.Twin: return TwinHeight(site, localX, localZ, r, noise, fine);
                case IslandShape.Ridge: return RidgeHeight(site, seed, localX, localZ, r, noise, fine);
                case IslandShape.Mesa: return MesaHeight(site, distance, r, noise, fine);
                case IslandShape.Lowland: return LowlandHeight(site, seed, worldX, worldZ, distance, r, noise, fine);
                default: return RollingHeight(site, distance, r, noise);
            }
        }

        /// <summary>The original rolling-hills island. Noise perturbs coastlines as well as hills; at the mesh
        /// square's corners the elevation stays underwater, hiding its square border.</summary>
        private static float RollingHeight(ArchipelagoWorld.IslandSite site, float distance, float r, float noise)
        {
            float warpedRadius = r * (0.94f + (noise - 0.5f) * 0.19f);
            float inward = Mathf.Clamp01(1f - distance / warpedRadius);
            float baseHeight = -1.6f + 8.5f * Mathf.Pow(inward, 1.22f);
            float detailed = (noise - 0.5f) * 2.2f * inward;
            float extra = site.Biome == IslandBiome.Volcanic ? 5f * Mathf.Sin(inward * Mathf.PI) * inward : 0f;
            return baseHeight + detailed + extra;
        }

        /// <summary>A single steep mountain with gullies down its flanks; volcanic ones have a crater.</summary>
        private static float PeakHeight(ArchipelagoWorld.IslandSite site, float x, float z, float distance, float r, float noise, float fine)
        {
            float warp = r * (0.86f + (noise - 0.5f) * 0.22f);
            float inward = Mathf.Clamp01(1f - distance / warp);
            float h = -1.8f + 4.6f * Mathf.Pow(inward, 0.9f) + 10.5f * Mathf.Pow(inward, 3.2f) + (fine - 0.5f) * 2.4f * inward;
            float angle = Mathf.Atan2(z, x);
            h += Mathf.Sin(angle * 5f + Param(site, 2) * Mathf.PI * 2f) * 3.6f * inward * (1f - inward);
            if (site.Biome == IslandBiome.Volcanic)
            {
                float crater = Mathf.Clamp01((inward - 0.84f) / 0.16f);
                h -= 8f * crater * crater;
            }
            return h;
        }

        /// <summary>A low ring of reef islands around a shallow lagoon, cut by one or two channels.</summary>
        private static float AtollHeight(ArchipelagoWorld.IslandSite site, float x, float z, float distance, float r, float noise, float fine)
        {
            float ringRadius = r * 0.54f * (1f + (noise - 0.5f) * 0.16f);
            float width = r * 0.11f;
            float t = (distance - ringRadius) / width;
            float ring = Mathf.Exp(-t * t);
            float outside = Smooth(ringRadius - width * 0.5f, ringRadius + width * 1.6f, distance);
            float h = Mathf.Lerp(-0.75f, -2.4f, outside) + 3.5f * ring + (fine - 0.5f) * 1.0f * ring;
            float angle = Mathf.Atan2(z, x) * Mathf.Rad2Deg;
            int channels = Param(site, 13) < 0.5f ? 1 : 2;
            for (int i = 0; i < channels; i++)
            {
                float gap = Mathf.Abs(Mathf.DeltaAngle(angle, Param(site, (uint)(11 + i)) * 360f)) * Mathf.Deg2Rad;
                h -= 2.8f * ring * Mathf.Clamp01(1f - gap / 0.22f);
            }
            // A small motu inside the lagoon.
            float motuAngle = Param(site, 14) * Mathf.PI * 2f;
            Vector2 motu = new Vector2(Mathf.Sin(motuAngle), Mathf.Cos(motuAngle)) * ringRadius * 0.35f;
            float m = 1f - (new Vector2(x, z) - motu).magnitude / (r * 0.12f);
            if (m > 0f) h = Mathf.Max(h, -0.75f + 2.4f * m);
            return h;
        }

        /// <summary>Hills curled around a deep, sheltered bay with raised headlands. On ports the bay is
        /// turned away from the harbour so the town keeps its gentle shore.</summary>
        private static float CrescentHeight(ArchipelagoWorld.IslandSite site, float x, float z, float distance, float r, float noise)
        {
            float warpedRadius = r * (0.94f + (noise - 0.5f) * 0.19f);
            float inward = Mathf.Clamp01(1f - distance / warpedRadius);
            float h = -1.6f + 8.5f * Mathf.Pow(inward, 1.22f) + (noise - 0.5f) * 2.2f * inward;
            bool port = site.Kind == IslandKind.PortSite;
            float yaw = (port ? PortYaw(site) + 200f : Param(site, 21) * 360f) * Mathf.Deg2Rad;
            Vector2 bayDir = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            float bayRadius = r * (port ? 0.27f : 0.33f);
            float bay = (new Vector2(x, z) - bayDir * r * (port ? 0.5f : 0.42f)).magnitude;
            float rimT = (bay - bayRadius * 1.25f) / (r * 0.12f);
            h += 2.6f * Mathf.Exp(-rimT * rimT) * inward * Smooth(0.3f, 0.55f, inward); // rim stays off the outer coast
            return Mathf.Lerp(h, -1.5f, Smooth(bayRadius * 1.05f, bayRadius * 0.55f, bay));
        }

        /// <summary>Two or three separate islets, one taller than the others, sometimes joined by a sandbar.</summary>
        private static float TwinHeight(ArchipelagoWorld.IslandSite site, float x, float z, float r, float noise, float fine)
        {
            int lobes = Param(site, 31) < 0.55f ? 2 : 3;
            float baseAngle = Param(site, 32) * 360f;
            Vector2 p = new Vector2(x, z);
            float h = -2.2f;
            Vector2 first = Vector2.zero, second = Vector2.zero;
            for (int i = 0; i < lobes; i++)
            {
                float a = (baseAngle + i * 360f / lobes + (Param(site, (uint)(33 + i)) - 0.5f) * 40f) * Mathf.Deg2Rad;
                Vector2 c = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r * (lobes == 2 ? 0.34f : 0.36f);
                if (i == 0) first = c; else if (i == 1) second = c;
                float lobeRadius = r * (lobes == 2 ? 0.38f : 0.32f) * (i == 0 ? 1.12f : 0.92f) * (0.92f + (noise - 0.5f) * 0.2f);
                float inward = Mathf.Clamp01(1f - (p - c).magnitude / lobeRadius);
                h = Mathf.Max(h, -1.4f + (i == 0 ? 9f : 6.5f) * Mathf.Pow(inward, 1.25f) + (fine - 0.5f) * 1.6f * inward);
            }
            if (Param(site, 37) < 0.6f)
            {
                Vector2 ab = second - first;
                float t = Mathf.Clamp01(Vector2.Dot(p - first, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                float bar = (p - (first + ab * t)).magnitude / (r * 0.06f);
                h = Mathf.Max(h, -0.9f + 1.3f * Mathf.Exp(-bar * bar));
            }
            return h;
        }

        /// <summary>A long, narrow island with a jagged spine of peaks.</summary>
        private static float RidgeHeight(ArchipelagoWorld.IslandSite site, int seed, float x, float z, float r, float noise, float fine)
        {
            float a = Param(site, 41) * Mathf.PI;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            float u = x * c + z * s, v = -x * s + z * c;
            float e = Mathf.Sqrt((u / (0.92f * r)) * (u / (0.92f * r)) + (v / (0.4f * r)) * (v / (0.4f * r))) * (1f + (noise - 0.5f) * 0.22f);
            float inward = Mathf.Clamp01(1f - e);
            float spine = 0.65f + 0.7f * Mathf.PerlinNoise(u * 0.12f + seed * 0.0003f + site.DetailSeed * 0.00001f + 9.1f, 3.7f);
            return -1.6f + 5.2f * Mathf.Pow(inward, 1.05f) + 8.5f * spine * Mathf.Pow(inward, 2.6f) + (fine - 0.5f) * 2f * inward;
        }

        /// <summary>A flat-topped table island: a narrow beach, sheer cliffs, sometimes a second, higher step.</summary>
        private static float MesaHeight(ArchipelagoWorld.IslandSite site, float distance, float r, float noise, float fine)
        {
            float warp = r * (0.9f + (noise - 0.5f) * 0.22f);
            float inward = Mathf.Clamp01(1f - distance / warp);
            float apron = -1.6f + 5f * inward;
            float cliff = Smooth(0.44f, 0.56f, inward + (fine - 0.5f) * 0.08f);
            float step = Param(site, 51) < 0.6f ? 2.6f * Smooth(0.8f, 0.86f, inward) : 0f;
            float top = 6.2f + (fine - 0.5f) * 0.7f + step;
            return Mathf.Lerp(apron, top, cliff);
        }

        /// <summary>A low, sandy cay with dunes, a ragged shoreline and (away from ports) salt ponds.</summary>
        private static float LowlandHeight(ArchipelagoWorld.IslandSite site, int seed, float worldX, float worldZ, float distance, float r, float noise, float fine)
        {
            float coast = Mathf.PerlinNoise(worldX * 0.11f + seed * 0.0002f + 3.3f, worldZ * 0.11f + 7.7f);
            float warp = r * ((site.Kind == IslandKind.PortSite ? 0.86f : 0.78f) + (noise - 0.5f) * 0.18f + (coast - 0.5f) * 0.22f);
            float inward = Mathf.Clamp01(1f - distance / warp);
            float h = -1.1f + 2.6f * Mathf.Pow(inward, 0.6f) + (fine - 0.5f) * 1.1f * inward;
            if (site.Kind != IslandKind.PortSite)
            {
                float pond = Mathf.PerlinNoise(worldX * 0.09f + 50f, worldZ * 0.09f + 20f);
                h -= Mathf.Max(0f, pond - 0.68f) * 9f * inward;
            }
            return h;
        }

        private static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

        /// <summary>Deterministic 0–1 value per island and purpose (salt).</summary>
        private static float Param(ArchipelagoWorld.IslandSite site, uint salt)
        {
            unchecked
            {
                uint h = (uint)site.DetailSeed ^ (salt * 0x9E3779B9u);
                h ^= h >> 15; h *= 0x2C1B3C6Du;
                h ^= h >> 12; h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        private static float RockLine(IslandShape shape)
        {
            switch (shape)
            {
                case IslandShape.Peak: case IslandShape.Twin: return 5.5f;
                case IslandShape.Ridge: return 5f;
                case IslandShape.Mesa: case IslandShape.Atoll: case IslandShape.Lowland: return 99f; // cliffs still turn to rock
                default: return 4.3f;
            }
        }

        private static void DecorationRange(IslandShape shape, out float minY, out float maxY)
        {
            switch (shape)
            {
                case IslandShape.Atoll: case IslandShape.Lowland: minY = 0.6f; maxY = 3f; break;
                case IslandShape.Mesa: minY = 1f; maxY = 9.5f; break;
                case IslandShape.Peak: case IslandShape.Ridge: minY = 1f; maxY = 6f; break;
                default: minY = 1f; maxY = 4.65f; break;
            }
        }

        private static int TreeCount(IslandShape shape, System.Random random)
        {
            switch (shape)
            {
                case IslandShape.Peak: return random.Next(4, 9);
                case IslandShape.Atoll: return random.Next(9, 15);
                case IslandShape.Lowland: return random.Next(10, 17);
                case IslandShape.Twin: return random.Next(8, 14);
                case IslandShape.Mesa: return random.Next(5, 10);
                case IslandShape.Ridge: return random.Next(6, 11);
                default: return random.Next(7, 13);
            }
        }

        private Material BiomeMaterial(Material source, Color color)
        {
            if (source == null) return null;
            Color32 c = color;
            long key = ((long)source.GetEntityId().GetHashCode() << 32) ^ (((long)c.r << 24) | ((long)c.g << 16) | ((long)c.b << 8) | c.a);
            if (biomeMaterials.TryGetValue(key, out Material cached) && cached != null) return cached;
            Material instance = new Material(source) { color = color };
            biomeMaterials[key] = instance;
            return instance;
        }

        private bool TryDecorationPoint(ArchipelagoWorld.IslandSite site, int seed,
            System.Random rng, float minY, float maxY, out Vector3 local)
        {
            for (int attempts = 0; attempts < 16; attempts++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float distance = Mathf.Sqrt((float)rng.NextDouble()) * site.Radius * 0.68f;
                float x = Mathf.Sin(angle) * distance;
                float z = Mathf.Cos(angle) * distance;
                float y = Height(site, seed, x, z);
                if (y < minY || y > maxY) continue;
                // No trees clinging to cliffs.
                float slope = Mathf.Max(Mathf.Abs(Height(site, seed, x + 0.5f, z) - y), Mathf.Abs(Height(site, seed, x, z + 0.5f) - y));
                if (slope > 0.6f) continue;
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
            GameObject trunk = BoatshipsPrimitives.Create(PrimitiveType.Cylinder);
            trunk.name = "Tree Trunk";
            trunk.transform.SetParent(transform, false);
            trunk.transform.localPosition = basePoint + Vector3.up * (0.75f * scale);
            trunk.transform.localScale = new Vector3(0.17f * scale, 0.75f * scale, 0.17f * scale);
            trunk.GetComponent<MeshRenderer>().sharedMaterial = bark;

            GameObject crown = new GameObject("Low Poly Canopy", typeof(MeshFilter), typeof(MeshRenderer));
            crown.transform.SetParent(transform, false);
            crown.transform.localPosition = basePoint + Vector3.up * (1.18f * scale);
            crown.transform.localScale = Vector3.one * scale;
            crown.GetComponent<MeshFilter>().sharedMesh = treeCrownMesh;
            crown.GetComponent<MeshRenderer>().sharedMaterial = leaves;
        }

        /// <summary>A leaning palm: a curved, segmented trunk with drooping fronds and coconuts.</summary>
        private void AddPalm(Vector3 basePoint, float scale, System.Random random, Material bark, Material leaves)
        {
            Transform palm = new GameObject("Palm Tree").transform;
            palm.SetParent(transform, false);
            palm.localPosition = basePoint;
            palm.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            Vector3 point = Vector3.zero;
            float lean = 6f + (float)random.NextDouble() * 10f;
            const int segments = 5;
            float segment = 0.55f * scale;
            for (int i = 0; i < segments; i++)
            {
                Quaternion tilt = Quaternion.Euler(lean * (i + 1) * 0.6f, 0f, 0f);
                Vector3 axis = tilt * Vector3.up;
                GameObject piece = BoatshipsPrimitives.Create(PrimitiveType.Cylinder);
                piece.name = "Palm Trunk";
                piece.transform.SetParent(palm, false);
                piece.transform.localPosition = point + axis * segment * 0.5f;
                piece.transform.localRotation = tilt;
                float girth = 0.16f * scale * (1f - i * 0.08f);
                piece.transform.localScale = new Vector3(girth, segment * 0.52f, girth);
                piece.GetComponent<MeshRenderer>().sharedMaterial = bark;
                point += axis * segment;
            }
            int fronds = 7;
            for (int i = 0; i < fronds; i++)
            {
                Transform pivot = new GameObject("Frond").transform;
                pivot.SetParent(palm, false);
                pivot.localPosition = point;
                pivot.localRotation = Quaternion.Euler(0f, i * 360f / fronds + (float)random.NextDouble() * 15f, 0f) *
                    Quaternion.Euler(-12f + (float)random.NextDouble() * 30f, 0f, 0f);
                GameObject leaf = BoatshipsPrimitives.Create(PrimitiveType.Cube);
                leaf.name = "Palm Leaf";
                leaf.transform.SetParent(pivot, false);
                leaf.transform.localPosition = new Vector3(0f, -0.12f * scale, 0.55f * scale);
                leaf.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
                leaf.transform.localScale = new Vector3(0.32f * scale, 0.03f * scale, 1.15f * scale);
                leaf.GetComponent<MeshRenderer>().sharedMaterial = leaves;
            }
            for (int i = 0; i < 3; i++)
            {
                GameObject nut = BoatshipsPrimitives.Create(PrimitiveType.Sphere);
                nut.name = "Coconut";
                nut.transform.SetParent(palm, false);
                float a = i * 2.1f;
                nut.transform.localPosition = point + new Vector3(Mathf.Sin(a) * 0.1f, -0.12f, Mathf.Cos(a) * 0.1f) * scale;
                nut.transform.localScale = Vector3.one * 0.13f * scale;
                nut.GetComponent<MeshRenderer>().sharedMaterial = bark;
            }
        }

        /// <summary>Tall rock pillars standing in the water just off the coast (inside the ship exclusion circle).</summary>
        private void AddSeaStacks(ArchipelagoWorld.IslandSite site, int seed, System.Random random, Material rock)
        {
            int count = random.Next(2, 5);
            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float distance = site.Radius * Mathf.Lerp(0.5f, 0.72f, (float)random.NextDouble());
                    float x = Mathf.Sin(angle) * distance, z = Mathf.Cos(angle) * distance;
                    if (Height(site, seed, x, z) > -0.6f) continue;
                    if (site.Kind == IslandKind.PortSite &&
                        (Quaternion.Inverse(PortRotation(site)) * new Vector3(x, 0f, z)).z > 0f) continue;
                    Transform stack = new GameObject("Sea Stack").transform;
                    stack.SetParent(transform, false);
                    stack.localPosition = new Vector3(x, -1.5f, z);
                    stack.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 90f, 0f);
                    float height = Mathf.Lerp(3.5f, 7f, (float)random.NextDouble());
                    float width = Mathf.Lerp(1.2f, 2.2f, (float)random.NextDouble());
                    float y = 0f;
                    for (int tier = 0; tier < 3; tier++)
                    {
                        float tierHeight = height * (tier == 0 ? 0.45f : tier == 1 ? 0.33f : 0.22f);
                        float tierWidth = width * (1f - tier * 0.22f);
                        GameObject block = BoatshipsPrimitives.Create(PrimitiveType.Cube);
                        block.name = "Sea Stack Rock";
                        block.transform.SetParent(stack, false);
                        block.transform.localPosition = new Vector3(((float)random.NextDouble() - 0.5f) * 0.2f, y + tierHeight * 0.5f, 0f);
                        block.transform.localRotation = Quaternion.Euler(((float)random.NextDouble() - 0.5f) * 10f, tier * 25f, ((float)random.NextDouble() - 0.5f) * 10f);
                        block.transform.localScale = new Vector3(tierWidth, tierHeight, tierWidth * 0.85f);
                        block.GetComponent<MeshRenderer>().sharedMaterial = rock;
                        y += tierHeight * 0.92f;
                    }
                    break;
                }
            }
        }

        /// <summary>Glowing lava pool in a volcano's crater.</summary>
        private void AddLava(ArchipelagoWorld.IslandSite site, int seed)
        {
            Material source = GetComponent<MeshRenderer>().sharedMaterials[0];
            if (source == null) return;
            Material lava = BiomeMaterial(source, new Color(1f, 0.38f, 0.08f));
            lava.EnableKeyword("_EMISSION");
            lava.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.05f) * 2.2f);
            GameObject pool = BoatshipsPrimitives.Create(PrimitiveType.Cylinder);
            pool.name = "Crater Lava";
            pool.transform.SetParent(transform, false);
            pool.transform.localPosition = new Vector3(0f, Height(site, seed, 0f, 0f) + 0.25f, 0f);
            pool.transform.localScale = new Vector3(site.Radius * 0.2f, 0.05f, site.Radius * 0.2f);
            pool.GetComponent<MeshRenderer>().sharedMaterial = lava;
        }

        /// <summary>Compass bearing (degrees) from the island centre to its harbour.</summary>
        public static float HarbourYaw(ArchipelagoWorld.IslandSite site) => PortYaw(site);

        private static float PortYaw(ArchipelagoWorld.IslandSite site) =>
            (float)new System.Random(site.DetailSeed).NextDouble() * 360f;

        private static Quaternion PortRotation(ArchipelagoWorld.IslandSite site) => Quaternion.Euler(0f, PortYaw(site), 0f);

        private static float PortShoreDistance(ArchipelagoWorld.IslandSite site, int seed, Quaternion facing, bool natural = false)
        {
            Vector3 direction = facing * Vector3.forward;
            // Search from the sea inland so hills cannot be mistaken for the outer coast.
            for (float distance = site.Radius; distance > 0f; distance -= 0.25f)
            {
                Vector3 sample = direction * distance;
                float height = natural ? ShapeHeight(site, seed, sample.x, sample.z) : Height(site, seed, sample.x, sample.z);
                if (height >= 0.35f) return distance;
            }
            return site.Radius * 0.6f;
        }

        private void AddPort(ArchipelagoWorld.IslandSite site, int seed,
            Material plasterSource, Material timber, Material stone, Material port)
        {
            Quaternion facing = PortRotation(site);
            Vector3 outward = facing * Vector3.forward;
            float shore = PortShoreDistance(site, seed, facing);
            Transform settlement = new GameObject("Port Village and Pier").transform;
            settlement.SetParent(transform, false);
            // Anchor the harbour on the coast, rather than at the island centre.
            settlement.localPosition = outward * shore;
            settlement.localRotation = facing;
            // The walkable harbour town (pier, square, shops, fort, houses, townsfolk) is the port's settlement,
            // seen from the sea and walked in first person alike. Its fort guards the water off the pier.
            BoatshipsTown town = gameObject.AddComponent<BoatshipsTown>();
            town.BuildHarbour(site, seed, this, settlement.TransformPoint(Vector3.forward * 6f));
        }

        private void AddLandmark(ArchipelagoWorld.IslandSite site, int seed,
            Material port, Material pirate, Material treasure)
        {
            Material chosen = site.Kind == IslandKind.PortSite ? port :
                site.Kind == IslandKind.PirateBaseSite ? pirate : treasure;
            Vector3 top = LandmarkPoint(site, seed);
            float y = top.y;
            GameObject pole = BoatshipsPrimitives.Create(PrimitiveType.Cylinder);
            pole.name = site.Kind + " Placeholder Marker";
            pole.transform.SetParent(transform, false);
            pole.transform.localPosition = new Vector3(top.x, y + 1.6f, top.z);
            pole.transform.localScale = new Vector3(0.15f, 1.6f, 0.15f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = chosen;
            GameObject flag = BoatshipsPrimitives.Create(PrimitiveType.Cube);
            flag.name = "Marker Flag";
            flag.transform.SetParent(transform, false);
            flag.transform.localPosition = new Vector3(top.x + 0.6f, y + 2.7f, top.z);
            flag.transform.localScale = new Vector3(1.2f, 0.6f, 0.12f);
            flag.GetComponent<MeshRenderer>().sharedMaterial = chosen;
        }

        /// <summary>Highest ground near the middle of the island, for flags and markers.</summary>
        public static Vector3 LandmarkPoint(ArchipelagoWorld.IslandSite site, int seed)
        {
            Vector3 best = new Vector3(0f, Height(site, seed, 0f, 0f), 0f);
            for (int ring = 1; ring <= 4; ring++)
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6f + ring * 0.3f, d = site.Radius * 0.13f * ring;
                    float x = Mathf.Sin(a) * d, z = Mathf.Cos(a) * d, h = Height(site, seed, x, z);
                    if (h > best.y) best = new Vector3(x, h, z);
                }
            return best;
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
            // The canopy mesh and biome materials are shared between islands and kept for the session.
        }
    }
}
