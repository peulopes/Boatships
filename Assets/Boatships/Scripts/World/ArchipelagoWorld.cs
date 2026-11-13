using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    public enum IslandKind { Deserted, PortSite, PirateBaseSite, TreasureSite }

    /// <summary>Seeded finite archipelago. Generated chunks are disposable views;
    /// island locations and kinds are deterministic from seed + cell coordinates.
    /// No changes to the existing OceanWaves or Ship controllers are required.</summary>
    [DisallowMultipleComponent]
    public sealed class ArchipelagoWorld : MonoBehaviour
    {
        [Serializable]
        public struct IslandSite
        {
            public Vector2Int Cell;
            public Vector2 Center;
            public float Radius;
            public IslandKind Kind;
            public int DetailSeed;
        }

        [Header("World (fixed number of cells)")]
        [SerializeField] private int seed = 847291;
        [SerializeField, Range(4, 16)] private int cellsPerAxis = 8;
        [SerializeField, Min(40f)] private float cellSize = 90f;
        [SerializeField, Range(1, 3)] private int streamingRadius = 1;
        [SerializeField, Range(12, 48)] private int terrainResolution = 28;
        [SerializeField] private Transform player;

        [Header("Island Materials")]
        [SerializeField] private Material sand;
        [SerializeField] private Material grass;
        [SerializeField] private Material rock;
        [SerializeField] private Material bark;
        [SerializeField] private Material leaves;
        [SerializeField] private Material stone;
        [SerializeField] private Material portMarker;
        [SerializeField] private Material pirateMarker;
        [SerializeField] private Material treasureMarker;

        private readonly Dictionary<Vector2Int, IslandSite> sites = new Dictionary<Vector2Int, IslandSite>();
        private readonly Dictionary<Vector2Int, GameObject> activeChunks = new Dictionary<Vector2Int, GameObject>();
        private readonly List<Vector2Int> stale = new List<Vector2Int>();
        private Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);
        private bool started;

        public int Seed => seed;
        public int LoadedChunkCount => activeChunks.Count;
        public int IslandCount => sites.Count;
        public float SideLength => cellsPerAxis * cellSize;
        public float HalfExtent => SideLength * 0.5f;
        public Transform Player => player;

        public void Configure(Transform follow, Material sandMat, Material grassMat,
            Material rockMat, Material barkMat, Material leafMat, Material stoneMat,
            Material portMat, Material pirateMat, Material treasureMat)
        {
            player = follow;
            sand = sandMat;
            grass = grassMat;
            rock = rockMat;
            bark = barkMat;
            leaves = leafMat;
            stone = stoneMat;
            portMarker = portMat;
            pirateMarker = pirateMat;
            treasureMarker = treasureMat;
        }

        private void Start()
        {
            started = true;
            Regenerate(seed);
        }

        public void Regenerate(int newSeed)
        {
            seed = newSeed;
            foreach (GameObject chunk in activeChunks.Values)
                if (chunk != null) Destroy(chunk);
            activeChunks.Clear();
            sites.Clear();
            stale.Clear();
            lastCell = new Vector2Int(int.MinValue, int.MinValue);
            int lower = -cellsPerAxis / 2;
            int upper = lower + cellsPerAxis;
            for (int z = lower; z < upper; z++)
            for (int x = lower; x < upper; x++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                // Safe starting cell; at least one island is guaranteed immediately north.
                if (x == 0 && z == 0) continue;
                System.Random rng = new System.Random(StableHash(seed, x, z));
                bool guaranteed = x == 0 && z == 1;
                if (!guaranteed && rng.NextDouble() > 0.72) continue;
                float jitterX = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.22f;
                float jitterZ = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.22f;
                float radius = cellSize * Mathf.Lerp(0.23f, 0.32f, (float)rng.NextDouble());
                double kindRoll = rng.NextDouble();
                IslandKind kind = guaranteed || kindRoll < 0.16 ? IslandKind.PortSite :
                    kindRoll < 0.29 ? IslandKind.PirateBaseSite :
                    kindRoll < 0.41 ? IslandKind.TreasureSite : IslandKind.Deserted;
                sites.Add(cell, new IslandSite
                {
                    Cell = cell,
                    Center = new Vector2(x * cellSize + jitterX, z * cellSize + jitterZ),
                    Radius = radius,
                    Kind = kind,
                    DetailSeed = StableHash(seed + 71, x, z)
                });
            }
            if (started) UpdateLoadedChunks();
        }

        private void Update()
        {
            if (player == null) return;
            Vector2Int cell = PositionToCell(player.position);
            if (cell == lastCell) return;
            UpdateLoadedChunks();
        }

        private void UpdateLoadedChunks()
        {
            Vector2Int cell = player != null ? PositionToCell(player.position) : Vector2Int.zero;
            lastCell = cell;
            stale.Clear();
            foreach (var entry in activeChunks)
            {
                Vector2Int c = entry.Key;
                if (Mathf.Abs(c.x - cell.x) > streamingRadius ||
                    Mathf.Abs(c.y - cell.y) > streamingRadius)
                    stale.Add(c);
            }
            foreach (Vector2Int c in stale)
            {
                GameObject removed = activeChunks[c];
                activeChunks.Remove(c);
                if (removed != null) Destroy(removed);
            }
            for (int z = cell.y - streamingRadius; z <= cell.y + streamingRadius; z++)
            for (int x = cell.x - streamingRadius; x <= cell.x + streamingRadius; x++)
            {
                Vector2Int c = new Vector2Int(x, z);
                if (activeChunks.ContainsKey(c) || !sites.TryGetValue(c, out IslandSite island)) continue;
                GameObject go = new GameObject($"Island_{x}_{z}_{island.Kind}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(island.Center.x, 0f, island.Center.y);
                ProceduralIslandChunk chunk = go.AddComponent<ProceduralIslandChunk>();
                chunk.Build(island, terrainResolution, seed, sand, grass, rock,
                    bark, leaves, stone, portMarker, pirateMarker, treasureMarker);
                activeChunks.Add(c, go);
            }
        }

        public bool TryNearestIsland(Vector3 position, out IslandSite best)
        {
            best = default(IslandSite);
            float shortest = float.MaxValue;
            bool found = false;
            foreach (IslandSite site in sites.Values)
            {
                float squared = (site.Center - new Vector2(position.x, position.z)).sqrMagnitude;
                if (squared >= shortest) continue;
                best = site;
                shortest = squared;
                found = true;
            }
            return found;
        }

        /// <summary>Find the nearest generated island of a specific type, including unloaded islands.
        /// The list is seed-based logical world state; no GameObject/chunk needs to be active.</summary>
        public bool TryNearestIslandOfKind(Vector3 position, IslandKind kind, out IslandSite best)
        {
            best = default(IslandSite);
            float shortest = float.MaxValue;
            bool found = false;
            Vector2 point = new Vector2(position.x, position.z);
            foreach (IslandSite site in sites.Values)
            {
                if (site.Kind != kind) continue;
                float distance = (site.Center - point).sqrMagnitude;
                if (distance >= shortest) continue;
                shortest = distance;
                best = site;
                found = true;
            }
            return found;
        }

        /// <summary>Used only as a lightweight shore-avoidance approximation for kinematic ships.</summary>
        public Vector3 KeepOnWater(Vector3 position, float clearance)
        {
            float edge = HalfExtent - clearance;
            position.x = Mathf.Clamp(position.x, -edge, edge);
            position.z = Mathf.Clamp(position.z, -edge, edge);
            Vector2 p = new Vector2(position.x, position.z);
            foreach (IslandSite island in sites.Values)
            {
                Vector2 difference = p - island.Center;
                float radius = island.Radius * 0.78f + clearance;
                if (difference.sqrMagnitude >= radius * radius) continue;
                Vector2 direction = difference.sqrMagnitude < 0.0001f ? Vector2.down : difference.normalized;
                p = island.Center + direction * radius;
            }
            position.x = Mathf.Clamp(p.x, -edge, edge);
            position.z = Mathf.Clamp(p.y, -edge, edge);
            return position;
        }

        private Vector2Int PositionToCell(Vector3 worldPosition)
        {
            return new Vector2Int(
                Mathf.FloorToInt((worldPosition.x + cellSize * 0.5f) / cellSize),
                Mathf.FloorToInt((worldPosition.z + cellSize * 0.5f) / cellSize));
        }

        private static int StableHash(int baseSeed, int x, int z)
        {
            // Do not use string.GetHashCode / UnityEngine.Random: these are not
            // suitable for reproducible world generation between sessions.
            unchecked
            {
                uint h = (uint)baseSeed ^ 0x9E3779B9u;
                h = (h ^ (uint)x) * 0x85EBCA6Bu;
                h = (h ^ (uint)z) * 0xC2B2AE35u;
                h ^= h >> 16;
                return (int)h;
            }
        }
    }
}
