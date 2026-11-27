using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>Like <see cref="GameObject.CreatePrimitive"/>, but without the collider every primitive otherwise
    /// gets (and that generated scenery immediately destroyed again, churning the physics scene).</summary>
    public static class BoatshipsPrimitives
    {
        private static readonly Dictionary<PrimitiveType, Mesh> meshes = new Dictionary<PrimitiveType, Mesh>();

        public static Mesh MeshOf(PrimitiveType type)
        {
            if (meshes.TryGetValue(type, out Mesh mesh) && mesh != null) return mesh;
            GameObject probe = GameObject.CreatePrimitive(type);
            mesh = probe.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(probe);
            meshes[type] = mesh;
            return mesh;
        }

        public static GameObject Create(PrimitiveType type, bool collider = false)
        {
            GameObject go = new GameObject(type.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = MeshOf(type);
            if (!collider) return go;
            switch (type)
            {
                case PrimitiveType.Cube: go.AddComponent<BoxCollider>(); break;
                case PrimitiveType.Sphere: go.AddComponent<SphereCollider>(); break;
                case PrimitiveType.Capsule:
                case PrimitiveType.Cylinder: go.AddComponent<CapsuleCollider>(); break;
                default: go.AddComponent<MeshCollider>().sharedMesh = MeshOf(type); break;
            }
            return go;
        }
    }
}
