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
            initialBlocks = 0;
            for (int x = 0; x < definition.Width; ++x)
            for (int y = 0; y < definition.Height; ++y)
            for (int z = 0; z < definition.Length; ++z)
            {
                if (!definition.ContainsBlock(x, y, z)) continue;
                health[x, y, z] = definition.BlockHealth;
                initialBlocks++;
            }
            activeBlocks = initialBlocks;
            RebuildMesh();
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
                health[x, y, z] -= damage * falloff;
                hitAny = true;
                if (health[x, y, z] > 0f) continue;
                health[x, y, z] = 0f;
                activeBlocks--;
                removedAny = true;
            }

            if (removedAny) RebuildMesh();
            return hitAny;
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
            generatedMesh.SetVertices(vertices);
            generatedMesh.SetTriangles(triangles, 0);
            generatedMesh.SetUVs(0, uvs);
            generatedMesh.RecalculateNormals();
            generatedMesh.RecalculateBounds();
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
