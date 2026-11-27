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
        private HullDressing dressing;

        // Submeshes: 0 = lower plank of each strake, 1 = upper (lighter) plank, 2/3 = alternating deck planks,
        // 4 = charred wood showing through shot holes.
        private const int Submeshes = 5, Charred = 4;
        private static readonly Color CharredWood = new Color(0.11f, 0.075f, 0.05f);
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        private readonly List<Vector2> uvs = new List<Vector2>();
        private Vector3[,,] corners;
        private float[,] extentPlus, extentMinus, originalPlus, originalMinus;

        public HullDefinition Definition => definition;
        public int TotalBlocks => initialBlocks;
        public int RemainingBlocks => activeBlocks;

        public void Configure(HullDefinition newDefinition, Material material)
        {
            definition = newDefinition;
            corners = null;
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

        // Scratch collections for the connectivity flood fill, reused across hits.
        private static readonly HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
        private static readonly HashSet<Vector3Int> retained = new HashSet<Vector3Int>();
        private static readonly List<Vector3Int> largest = new List<Vector3Int>();
        private static readonly List<Vector3Int> section = new List<Vector3Int>();
        private static readonly Queue<Vector3Int> queue = new Queue<Vector3Int>();
        private static readonly Vector3Int[] offsets = { Vector3Int.right, Vector3Int.left, Vector3Int.up,
            Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };

        private void RemoveDisconnectedSections(List<Vector3> debris = null)
        {
            visited.Clear(); largest.Clear(); section.Clear(); queue.Clear();
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
                if (section.Count > largest.Count) { largest.Clear(); largest.AddRange(section); }
            }
            retained.Clear();
            foreach (Vector3Int cell in largest) retained.Add(cell);
            foreach (Vector3Int cell in visited)
                if (!retained.Contains(cell))
                {
                    RecordDebris(debris, cell.x, cell.y, cell.z);
                    health[cell.x, cell.y, cell.z] = 0f;
                    activeBlocks--;
                }
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
        public bool ApplyImpactWorld(Vector3 worldPoint, float damage, float radius, List<Vector3> debris = null)
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
                RecordDebris(debris, x, y, z);
                removedAny = true;
            }

            if (removedAny)
            {
                if (structuralIntegrity) RemoveDisconnectedSections(debris);
                RebuildMesh();
            }
            return hitAny;
        }

        private void RecordDebris(List<Vector3> debris, int x, int y, int z)
        {
            if (debris != null && debris.Count < 48)
                debris.Add(transform.TransformPoint(definition.BlockCenter(x, y, z)));
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
            foreach (List<int> list in triangles) list.Clear();
            uvs.Clear();
            BuildCorners();
            int top = definition.Height - 1;

            // Faces use the sculpted corner lattice, so the blocks render as a smooth low-poly hull.
            for (int x = 0; x < definition.Width; ++x)
            for (int y = 0; y < definition.Height; ++y)
            for (int z = 0; z < definition.Length; ++z)
            {
                if (!HasBlock(x, y, z)) continue;
                // A face whose neighbour was part of the hull but has been shot away lines a hole: draw it charred.
                if (!HasBlock(x + 1, y, z)) AddSide(
                    corners[x + 1, y, z], corners[x + 1, y + 1, z], corners[x + 1, y + 1, z + 1], corners[x + 1, y, z + 1],
                    IsDesignedCell(x + 1, y, z));
                if (!HasBlock(x - 1, y, z)) AddSide(
                    corners[x, y, z + 1], corners[x, y + 1, z + 1], corners[x, y + 1, z], corners[x, y, z],
                    IsDesignedCell(x - 1, y, z));
                if (!HasBlock(x, y + 1, z)) AddFace(
                    corners[x, y + 1, z], corners[x, y + 1, z + 1], corners[x + 1, y + 1, z + 1], corners[x + 1, y + 1, z],
                    IsDesignedCell(x, y + 1, z) ? Charred : y == top ? 2 + x % 2 : 0);
                if (!HasBlock(x, y - 1, z)) AddFace(
                    corners[x, y, z + 1], corners[x, y, z], corners[x + 1, y, z], corners[x + 1, y, z + 1],
                    IsDesignedCell(x, y - 1, z) ? Charred : 0);
                if (!HasBlock(x, y, z + 1)) AddSide(
                    corners[x + 1, y, z + 1], corners[x + 1, y + 1, z + 1], corners[x, y + 1, z + 1], corners[x, y, z + 1],
                    IsDesignedCell(x, y, z + 1));
                if (!HasBlock(x, y, z - 1)) AddSide(
                    corners[x, y, z], corners[x, y + 1, z], corners[x + 1, y + 1, z], corners[x + 1, y, z],
                    IsDesignedCell(x, y, z - 1));
            }

            generatedMesh.Clear();
            generatedMesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 :
                UnityEngine.Rendering.IndexFormat.UInt16;
            generatedMesh.SetVertices(vertices);
            generatedMesh.subMeshCount = Submeshes;
            for (int i = 0; i < Submeshes; i++) generatedMesh.SetTriangles(triangles[i], i, false);
            generatedMesh.SetUVs(0, uvs);
            generatedMesh.RecalculateNormals();
            generatedMesh.RecalculateBounds();
            ApplyMaterials();
            Revision++;
            if (Application.isPlaying)
            {
                if (dressing == null) dressing = HullDressing.Attach(this);
                dressing.Refresh();
            }
            HullChanged?.Invoke();
        }

        /// <summary>Planked hull, two-tone deck. Outside Play mode every slot keeps the assigned material
        /// so scenes never reference runtime-only materials.</summary>
        private void ApplyMaterials()
        {
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            Material source = meshRenderer.sharedMaterial;
            if (source == null) return;
            Material[] wanted = Application.isPlaying
                ? new[] { source, ShipPalette.Lighter(source), ShipPalette.Solid(ShipPalette.Deck),
                    ShipPalette.Solid(ShipPalette.DeckAlt), ShipPalette.Solid(CharredWood) }
                : new[] { source, source, source, source, source };
            Material[] current = meshRenderer.sharedMaterials;
            bool same = current.Length == wanted.Length;
            for (int i = 0; same && i < wanted.Length; i++) same = current[i] == wanted[i];
            if (!same) meshRenderer.sharedMaterials = wanted;
        }

        /// <summary>
        /// Places every voxel corner on the sculpted hull. Each corner row's outermost designed corner is
        /// moved onto the HullShape outline and inner corners scale with it; corners that only stick out
        /// past a narrower neighbouring row fold onto the outline, which smooths the voxel steps away.
        /// Blocks a player adds beyond the original design push the outline out by the same amount.
        /// </summary>
        private void BuildCorners()
        {
            HullDefinition d = definition;
            int w = d.Width, h = d.Height, l = d.Length;
            if (corners == null || corners.GetLength(0) != w + 1 || corners.GetLength(1) != h + 1 || corners.GetLength(2) != l + 1)
            {
                corners = new Vector3[w + 1, h + 1, l + 1];
                extentPlus = new float[h, l]; extentMinus = new float[h, l];
                originalPlus = new float[h, l]; originalMinus = new float[h, l];
                MeasureRows(originalPlus, originalMinus, (x, y, z) => d.ContainsBlock(x, y, z));
            }
            MeasureRows(extentPlus, extentMinus, (x, y, z) => design[x, y, z]);
            float size = d.BlockSize;
            for (int i = 0; i <= w; i++)
            for (int j = 0; j <= h; j++)
            for (int k = 0; k <= l; k++)
            {
                float x0 = i - w * 0.5f;
                bool plus = x0 > 0f;
                float along = Mathf.Abs(x0), narrowest = float.MaxValue, original = float.MaxValue;
                for (int y = j - 1; y <= j; y++)
                for (int z = k - 1; z <= k; z++)
                {
                    if (y < 0 || z < 0 || y >= h || z >= l) continue;
                    float extent = plus ? extentPlus[y, z] : extentMinus[y, z];
                    if (extent <= 0f) continue;
                    float designed = plus ? originalPlus[y, z] : originalMinus[y, z];
                    narrowest = Mathf.Min(narrowest, extent);
                    original = Mathf.Min(original, designed > 0f ? designed : extent);
                }
                float t = (float)j / h, zc = HullShape.Station(d, k), lateral = along;
                if (narrowest < float.MaxValue)
                {
                    float outline = HullShape.HalfWidthBlocks(d, t, zc) + Mathf.Max(0f, narrowest - original);
                    lateral = along >= narrowest ? outline : along * outline / narrowest;
                }
                corners[i, j, k] = new Vector3((plus ? lateral : -lateral) * size,
                    (j - h * 0.5f) * size + HullShape.Rocker(d, t, zc),
                    (k - l * 0.5f) * size + HullShape.Rake(d, t, zc));
            }
        }

        /// <summary>Per block row (layer y, row z): how far the outermost block reaches, in blocks from the centreline.</summary>
        private void MeasureRows(float[,] plus, float[,] minus, System.Func<int, int, int, bool> filled)
        {
            int w = definition.Width;
            for (int y = 0; y < definition.Height; y++)
            for (int z = 0; z < definition.Length; z++)
            {
                plus[y, z] = 0f; minus[y, z] = 0f;
                for (int x = 0; x < w; x++)
                {
                    if (!filled(x, y, z)) continue;
                    plus[y, z] = Mathf.Max(plus[y, z], x + 1 - w * 0.5f);
                    minus[y, z] = Mathf.Max(minus[y, z], w * 0.5f - x);
                }
            }
        }

        /// <summary>A side face split into two planks: v0/v3 along the bottom edge, v1/v2 along the top.</summary>
        private void AddSide(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, bool charred)
        {
            if (charred) { AddFace(v0, v1, v2, v3, Charred); return; }
            Vector3 left = (v0 + v1) * 0.5f, right = (v3 + v2) * 0.5f;
            AddFace(v0, left, right, v3, 0);
            AddFace(left, v1, v2, right, 1);
        }

        private void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int submesh)
        {
            // Faces folded flat by the sculpting have no area; skip them.
            if (Vector3.Cross(b - a, c - a).sqrMagnitude + Vector3.Cross(c - a, d - a).sqrMagnitude < 1e-10f) return;
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            List<int> list = triangles[submesh];
            list.Add(start);
            list.Add(start + 1);
            list.Add(start + 2);
            list.Add(start);
            list.Add(start + 2);
            list.Add(start + 3);
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
