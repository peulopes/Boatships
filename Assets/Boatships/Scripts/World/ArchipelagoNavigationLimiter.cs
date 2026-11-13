using UnityEngine;

namespace Boatships
{
    /// <summary>Small prototype guard for finite-world bounds and coastlines.
    /// Runs after the existing kinematic arcade movement controller.</summary>
    [DefaultExecutionOrder(300)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ArchipelagoNavigationLimiter : MonoBehaviour
    {
        [SerializeField] private ArchipelagoWorld world;
        [SerializeField, Min(0f)] private float clearance = 3.2f;
        private Rigidbody body;

        public void Configure(ArchipelagoWorld generatedWorld) { world = generatedWorld; }

        private void Awake() { body = GetComponent<Rigidbody>(); }

        private void FixedUpdate()
        {
            if (world == null || body == null) return;
            Vector3 current = body.position;
            Vector3 constrained = world.KeepOnWater(current, clearance);
            if ((current - constrained).sqrMagnitude > 0.0001f)
                body.position = constrained;
        }
    }
}
