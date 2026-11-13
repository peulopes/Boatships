using UnityEngine;

namespace Boatships
{
    /// <summary>Receiver on a ship root. Mesh damage is applied to its child VoxelHull.</summary>
    [DisallowMultipleComponent]
    public sealed class ShipCombatTarget : MonoBehaviour
    {
        [SerializeField] private VoxelHull hull;
        [SerializeField] private bool isPracticeTarget;

        public VoxelHull Hull => hull;
        public bool IsPracticeTarget => isPracticeTarget;
        public int RemainingVoxels => hull != null ? hull.RemainingBlocks : 0;
        public int TotalVoxels => hull != null ? hull.TotalBlocks : 0;

        public void Configure(VoxelHull targetHull, bool practice)
        {
            hull = targetHull;
            isPracticeTarget = practice;
        }

        private void Awake()
        {
            if (hull == null) hull = GetComponentInChildren<VoxelHull>();
        }

        public bool ReceiveImpact(Vector3 worldPoint, float damage, float radius)
        {
            if (hull == null) return false;
            return hull.ApplyImpactWorld(worldPoint, damage, radius);
        }

        public void ResetPracticeTarget()
        {
            if (isPracticeTarget && hull != null) hull.ResetHull();
        }
    }
}
