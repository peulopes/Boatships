using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Boatships
{
    /// <summary>Merges the many static primitive parts of a generated island or town into one mesh per
    /// material, so a harbour costs a few dozen renderers instead of thousands of GameObjects.
    /// Parts below a scripted component (townsfolk, the fort battery), parts with other components
    /// (sign text) and subtrees listed in <c>skip</c> are left untouched. Colliders stay where they are.</summary>
    public static class BoatshipsMeshBatcher
    {
        /// <summary>Parts whose world size is below this do not cast shadows once merged.</summary>
        public const float SmallPartSize = 0.45f;

        private struct BatchKey
        {
            public Material Material;
            public bool Shadows;
        }

        private sealed class KeyComparer : IEqualityComparer<BatchKey>
        {
            public bool Equals(BatchKey a, BatchKey b) => a.Material == b.Material && a.Shadows == b.Shadows;
            public int GetHashCode(BatchKey k) => (k.Material != null ? k.Material.GetEntityId().GetHashCode() : 0) * 2 + (k.Shadows ? 1 : 0);
        }

        private static readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        private static readonly List<Component> components = new List<Component>();
        private static readonly Dictionary<BatchKey, List<CombineInstance>> batches =
            new Dictionary<BatchKey, List<CombineInstance>>(new KeyComparer());
        private static readonly HashSet<Transform> consumed = new HashSet<Transform>();
        private static readonly Dictionary<Transform, bool> movable = new Dictionary<Transform, bool>();

        /// <summary>Merges the static parts under <paramref name="root"/>. Returns the number of parts merged.</summary>
        public static int Combine(Transform root, ICollection<Transform> skip = null, ICollection<string> keepNames = null)
        {
            if (root == null) return 0;
            renderers.Clear(); batches.Clear(); consumed.Clear(); movable.Clear();
            root.GetComponentsInChildren(false, renderers);
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            int merged = 0;
            foreach (MeshRenderer renderer in renderers)
            {
                Transform t = renderer.transform;
                if (t == root || !renderer.enabled || IsMovable(t, root, skip)) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Material material = renderer.sharedMaterial;
                if (filter == null || filter.sharedMesh == null || material == null ||
                    filter.sharedMesh.subMeshCount != 1 || renderer.sharedMaterials.Length != 1 || HasOtherComponents(t)) continue;
                bool shadows = renderer.shadowCastingMode != ShadowCastingMode.Off &&
                    renderer.bounds.size.magnitude >= SmallPartSize;
                var key = new BatchKey { Material = material, Shadows = shadows };
                if (!batches.TryGetValue(key, out List<CombineInstance> list)) batches[key] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = filter.sharedMesh, transform = toRoot * t.localToWorldMatrix });
                consumed.Add(t);
                merged++;
            }
            if (merged == 0) return 0;

            foreach (var batch in batches)
            {
                GameObject go = new GameObject("Merged " + batch.Key.Material.name + (batch.Key.Shadows ? "" : " (no shadows)"),
                    typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root, false);
                Mesh mesh = new Mesh { name = go.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(batch.Value.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true); // CPU copy is no longer needed: frees memory.
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = batch.Key.Material;
                renderer.shadowCastingMode = batch.Key.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                go.AddComponent<BoatshipsOwnedMesh>().Mesh = mesh;
            }

            // Strip what was merged: whole leaf objects where nothing else needs them, otherwise just the renderer.
            foreach (Transform t in consumed)
            {
                bool keep = t.childCount > 0 || t.GetComponent<Collider>() != null || (keepNames != null && keepNames.Contains(t.name));
                if (keep)
                {
                    Object.Destroy(t.GetComponent<MeshRenderer>());
                    Object.Destroy(t.GetComponent<MeshFilter>());
                }
                else Object.Destroy(t.gameObject);
            }
            renderers.Clear(); batches.Clear(); consumed.Clear(); movable.Clear();
            return merged;
        }

        /// <summary>True when the part, or a parent below root, carries a script (it may move or be looked up).</summary>
        private static bool IsMovable(Transform t, Transform root, ICollection<Transform> skip)
        {
            if (t == null || t == root) return false;
            if (movable.TryGetValue(t, out bool cached)) return cached;
            bool result = (skip != null && skip.Contains(t)) || t.GetComponent<MonoBehaviour>() != null ||
                IsMovable(t.parent, root, skip);
            movable[t] = result;
            return result;
        }

        private static bool HasOtherComponents(Transform t)
        {
            components.Clear();
            t.GetComponents(components);
            foreach (Component component in components)
                if (!(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer) && !(component is Collider))
                    return true;
            return false;
        }
    }

    /// <summary>Destroys a runtime-generated mesh together with the object that shows it.</summary>
    public sealed class BoatshipsOwnedMesh : MonoBehaviour
    {
        public Mesh Mesh;
        private void OnDestroy() { if (Mesh != null) Destroy(Mesh); }
    }
}
