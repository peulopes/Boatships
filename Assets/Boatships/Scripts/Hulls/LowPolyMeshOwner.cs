using UnityEngine;

namespace Boatships
{
    /// <summary>Destroys a generated mesh together with the object that renders it.</summary>
    [DisallowMultipleComponent]
    public sealed class LowPolyMeshOwner : MonoBehaviour
    {
        private Mesh owned;

        public void Own(Mesh mesh) => owned = mesh;

        private void OnDestroy()
        {
            if (owned != null) Destroy(owned);
        }
    }
}
