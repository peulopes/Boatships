using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// One hull definition, independent runtime health for each block, and a single
    /// render mesh built from externally visible faces (no GameObject per voxel).
    /// Milestone 2 demo: no flooding, equipment detachment, or saved damage yet.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public sealed class VoxelHull : MonoBehaviour
    {
        [SerializeField] private HullDefinition definition;
        private float[,,] health;
        private bool[,,] design;
        [SerializeField] private bool structuralIntegrity;
        public event System.Action HullChanged;
        public int Revision { get; private set; }
        public float HullHealthMultiplier { get; set; } = 1f;
        private Mesh generatedMesh;
        private int initialBlocks;
        private int activeBlocks;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Vector2> uvs = new List<Vector2>();

        public HullDefinition Definition => definition;
        public int TotalBlocks => initialBlocks;
        public int RemainingBlocks => activeBlocks;

        public void Configure(HullDefinition newDefinition, Material material)
        {
            definition = newDefinition;
            GetComponent<MeshRenderer>().sharedMaterial = material;
            ResetHull();
        }

        private void OnEnable()
        {
            if (definition != null) ResetHull();
        }

        private void OnDisable()
        {
            DestroyGeneratedMesh();
        }

        public void ResetHull()
        {
            if (definition == null) return;
            health = new float[definition.Width, definition.Height, definition.Length];
            bool preserve = structuralIntegrity && design != null && design.GetLength(0) == definition.Width &&
                design.GetLength(1) == definition.Height && design.GetLength(2) == definition.Length;
            if (!preserve) design = new bool[definition.Width, definition.Height, definition.Length];
            initialBlocks = 0;
            for (int x = 0; x < definition.Width; ++x)
            for (int y = 0; y < definition.Height; ++y)
            for (int z = 0; z < definition.Length; ++z)
            {
                if (!(preserve ? design[x, y, z] : definition.ContainsBlock(x, y, z))) continue;
                health[x, y, z] = definition.BlockHealth;
                design[x, y, z] = true;
                initialBlocks++;
            }
            activeBlocks = initialBlocks;
            if (structuralIntegrity) RemoveDisconnectedSections();
            RebuildMesh();
        }

        public void EnableStructuralIntegrity() => structuralIntegrity = true;
        public void ResetDesign() { design = null; ResetHull(); }

        public bool InBounds(int x, int y, int z) => definition != null && x >= 0 && y >= 0 && z >= 0 &&
            x < definition.Width && y < definition.Height && z < definition.Length;

        public bool IsDesignedCell(int x, int y, int z) => design != null && InBounds(x, y, z) && design[x, y, z];

        public bool TryNearestDeckPoint(Vector3 requested, out Vector3 point)
        {
            point = Vector3.zero;
            if (definition == null || health == null) return false;
            float best = float.PositiveInfinity;
            for (int x = 0; x < definition.Width; x++)
            for (int z = 0; z < definition.Length; z++)
            {
                if (!HasBlock(x, definition.Height - 1, z)) continue;
                Vector3 candidate = definition.BlockCenter(x, definition.Height - 1, z) +
                    Vector3.up * (definition.BlockSize * 0.5f + 0.1f);
                float distance = (candidate - requested).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; point = candidate;
            }
            return best < float.PositiveInfinity;
        }

        public bool CanEditCell(int x, int y, int z, bool add)
        {
            if (health == null || !InBounds(x, y, z)) return false;
            if (!add) return HasBlock(x, y, z) && activeBlocks > 1;
            if (HasBlock(x, y, z)) return false;
            return activeBlocks == 0 || HasBlock(x + 1, y, z) || HasBlock(x - 1, y, z) ||
                HasBlock(x, y + 1, z) || HasBlock(x, y - 1, z) ||
                HasBlock(x, y, z + 1) || HasBlock(x, y, z - 1);
        }

        public bool TryEditCell(int x, int y, int z, bool add)
        {
            if (!CanEditCell(x, y, z, add)) return false;
            if (design[x, y, z] != add) initialBlocks += add ? 1 : -1;
            design[x, y, z] = add;
            health[x, y, z] = add ? definition.BlockHealth : 0f;
            activeBlocks += add ? 1 : -1;
            if (structuralIntegrity) RemoveDisconnectedSections();
            RebuildMesh();
            return true;
        }

        private void RemoveDisconnectedSections()
        {
            var visited = new HashSet<Vector3Int>();
            var largest = new List<Vector3Int>();
            var section = new List<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            Vector3Int[] offsets = { Vector3Int.right, Vector3Int.left, Vector3Int.up,
                Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
            for (int x = 0; x < definition.Width; x++)
            for (int y = 0; y < definition.Height; y++)
            for (int z = 0; z < definition.Length; z++)
            {
                var start = new Vector3Int(x, y, z);
                if (!HasBlock(x, y, z) || !visited.Add(start)) continue;
                section.Clear();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Vector3Int cell = queue.Dequeue();
                    section.Add(cell);
                    foreach (Vector3Int offset in offsets)
                    {
                        Vector3Int next = cell + offset;
                        if (HasBlock(next.x, next.y, next.z) && visited.Add(next)) queue.Enqueue(next);
                    }
                }
                if (section.Count > largest.Count) largest = new List<Vector3Int>(section);
            }
            var retained = new HashSet<Vector3Int>(largest);
            foreach (Vector3Int cell in visited)
                if (!retained.Contains(cell)) { health[cell.x, cell.y, cell.z] = 0f; activeBlocks--; }
        }

        public Vector3 LiveCenterOfMass()
        {
            Vector3 sum = Vector3.zero;
            if (health == null || activeBlocks == 0) return sum;
            for (int x = 0; x < definition.Width; x++)
            for (int y = 0; y < definition.Height; y++)
            for (int z = 0; z < definition.Length; z++)
                if (HasBlock(x, y, z)) sum += definition.BlockCenter(x, y, z);
            return sum / activeBlocks;
        }

        public bool HasBlock(int x, int y, int z)
        {
            return health != null &&
                x >= 0 && y >= 0 && z >= 0 &&
                x < definition.Width && y < definition.Height && z < definition.Length &&
                health[x, y, z] > 0f;
        }

        /// <summary>
        /// Snaps the hit to the nearest live voxel; applies radial damage around it.
        /// The caller may provide a hit on the ship's coarse BoxCollider.
        /// </summary>
        public bool ApplyImpactWorld(Vector3 worldPoint, float damage, float radius)
        {
            if (definition == null || health == null || activeBlocks == 0 || damage <= 0f) return false;
            Vector3 localHit = transform.InverseTransformPoint(worldPoint);
            Vector3 nearest = Vector3.zero;
            float bestDistance = float.PositiveInfinity;

            for (int x = 0; x < definition.Width; ++x)
            for (int y = 0; y < definition.Height; ++y)
            for (int z = 0; z < definition.Length; ++z)
            {
                if (!HasBlock(x, y, z)) continue;
                Vector3 center = definition.BlockCenter(x, y, z);
                float sqrDistance = (center - localHit).sqrMagnitude;
                if (sqrDistance >= bestDistance) continue;
                bestDistance = sqrDistance;
                nearest = center;
            }

            float effectiveRadius = Mathf.Max(radius, definition.BlockSize * 0.6f);
            bool hitAny = false;
            bool removedAny = false;
            for (int x = 0; x < definition.Width; ++x)
            for (int y = 0; y < definition.Height; ++y)
            for (int z = 0; z < definition.Length; ++z)
            {
                if (!HasBlock(x, y, z)) continue;
                float distance = Vector3.Distance(definition.BlockCenter(x, y, z), nearest);
                if (distance > effectiveRadius) continue;
                float falloff = Mathf.Lerp(1f, 0.45f, distance / effectiveRadius);
                health[x, y, z] -= damage * falloff / Mathf.Max(1f, HullHealthMultiplier);
                hitAny = true;
                if (health[x, y, z] > 0f) continue;
                health[x, y, z] = 0f;
                activeBlocks--;
                removedAny = true;
            }

            if (removedAny)
            {
                if (structuralIntegrity) RemoveDisconnectedSections();
                RebuildMesh();
            }
            return hitAny;
        }

        public List<ShipVoxelRecord> CaptureVoxelDamage()
        {
            var damaged = new List<ShipVoxelRecord>();
            if (definition == null || health == null) return damaged;
            for (int x = 0; x < definition.Width; ++x)
            for (int y = 0; y < definition.Height; ++y)
            for (int z = 0; z < definition.Length; ++z)
            {
                bool original = definition.ContainsBlock(x, y, z);
                if (!original && !design[x, y, z]) continue;
                if (original && design[x, y, z] && health[x, y, z] >= definition.BlockHealth - 0.001f) continue;
                damaged.Add(new ShipVoxelRecord { x = x, y = y, z = z, health = health[x, y, z],
                    removedFromDesign = !design[x, y, z], addedToDesign = !original && design[x, y, z] });
            }
            return damaged;
        }

        /// <summary>Restores destroyed AND partially damaged voxels from a sparse snapshot.</summary>
        public void RestoreVoxelDamage(List<ShipVoxelRecord> snapshot, bool pristine = false)
        {
            ResetDesign();
            if (definition == null || health == null || snapshot == null) return;
            var seen = new HashSet<int>();
            foreach (ShipVoxelRecord cell in snapshot)
            {
                if (cell == null || !InBounds(cell.x, cell.y, cell.z)) continue;
                int key = (cell.x * definition.Height + cell.y) * definition.Length + cell.z;
                if (!seen.Add(key)) continue;
                bool planned = cell.addedToDesign || definition.ContainsBlock(cell.x, cell.y, cell.z);
                planned &= !cell.removedFromDesign;
                bool previouslyLive = HasBlock(cell.x, cell.y, cell.z);
                if (design[cell.x, cell.y, cell.z] != planned) initialBlocks += planned ? 1 : -1;
                design[cell.x, cell.y, cell.z] = planned;
                float value = Mathf.Clamp(cell.health, 0f, definition.BlockHealth);
                if (pristine && planned) value = definition.BlockHealth;
                if (!planned) value = 0f;
                if (previouslyLive != (value > 0f)) activeBlocks += value > 0f ? 1 : -1;
                health[cell.x, cell.y, cell.z] = value;
            }
            if (structuralIntegrity) RemoveDisconnectedSections();
            RebuildMesh();
        }

        private void RebuildMesh()
        {
            if (definition == null || health == null) return;
            if (generatedMesh == null)
            {
                generatedMesh = new Mesh { name = "Boatships Procedural Hull" };
                generatedMesh.MarkDynamic();
                generatedMesh.hideFlags = HideFlags.DontSave;
                GetComponent<MeshFilter>().sharedMesh = generatedMesh;
            }

            vertices.Clear();
            triangles.Clear();
            uvs.Clear();
            float half = definition.BlockSize * 0.5f;

            for (int x = 0; x < definition.Width; ++x)
            for (int y = 0; y < definition.Height; ++y)
            for (int z = 0; z < definition.Length; ++z)
            {
                if (!HasBlock(x, y, z)) continue;
                Vector3 p = definition.BlockCenter(x, y, z);
                Vector3 a = new Vector3(p.x - half, p.y - half, p.z - half);
                Vector3 b = new Vector3(p.x + half, p.y + half, p.z + half);

                if (!HasBlock(x + 1, y, z)) AddFace(
                    new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, a.z),
                    new Vector3(b.x, b.y, b.z), new Vector3(b.x, a.y, b.z));
                if (!HasBlock(x - 1, y, z)) AddFace(
                    new Vector3(a.x, a.y, b.z), new Vector3(a.x, b.y, b.z),
                    new Vector3(a.x, b.y, a.z), new Vector3(a.x, a.y, a.z));
                if (!HasBlock(x, y + 1, z)) AddFace(
                    new Vector3(a.x, b.y, a.z), new Vector3(a.x, b.y, b.z),
                    new Vector3(b.x, b.y, b.z), new Vector3(b.x, b.y, a.z));
                if (!HasBlock(x, y - 1, z)) AddFace(
                    new Vector3(a.x, a.y, b.z), new Vector3(a.x, a.y, a.z),
                    new Vector3(b.x, a.y, a.z), new Vector3(b.x, a.y, b.z));
                if (!HasBlock(x, y, z + 1)) AddFace(
                    new Vector3(b.x, a.y, b.z), new Vector3(b.x, b.y, b.z),
                    new Vector3(a.x, b.y, b.z), new Vector3(a.x, a.y, b.z));
                if (!HasBlock(x, y, z - 1)) AddFace(
                    new Vector3(a.x, a.y, a.z), new Vector3(a.x, b.y, a.z),
                    new Vector3(b.x, b.y, a.z), new Vector3(b.x, a.y, a.z));
            }

            generatedMesh.Clear();
            generatedMesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 :
                UnityEngine.Rendering.IndexFormat.UInt16;
            generatedMesh.SetVertices(vertices);
            generatedMesh.SetTriangles(triangles, 0);
            generatedMesh.SetUVs(0, uvs);
            generatedMesh.RecalculateNormals();
            generatedMesh.RecalculateBounds();
            Revision++;
            HullChanged?.Invoke();
        }

        private void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));
        }

        private void DestroyGeneratedMesh()
        {
            if (generatedMesh == null) return;
            if (GetComponent<MeshFilter>() != null)
                GetComponent<MeshFilter>().sharedMesh = null;
            if (Application.isPlaying) Destroy(generatedMesh);
            else DestroyImmediate(generatedMesh);
            generatedMesh = null;
        }

        private void OnDrawGizmosSelected()
        {
            if (definition == null) return;
            Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
            foreach (HullHardpoint slot in definition.Hardpoints)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireSphere(slot.localPosition, 0.15f);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
