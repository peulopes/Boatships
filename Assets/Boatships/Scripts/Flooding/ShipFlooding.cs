using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// Milestone 6 prototype. Six abstract watertight compartments (port/starboard x
    /// stern/mid/bow). Counts holes in the ORIGINAL outside skin of a voxel hull;
    /// checks their height against the SAME ocean surface used to animate the ship.
    /// This is an arcade flooding model, NOT a fluid solver or watertight voxel test.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class ShipFlooding : MonoBehaviour
    {
        private struct Opening
        {
            public int x, y, z, key, compartment;
        }

        [SerializeField] private VoxelHull hull;
        [SerializeField] private OceanWaves ocean;
        [SerializeField] private Transform floodVisualPivot;
        [SerializeField] private ShipArcadeController playerMovement;
        [SerializeField] private EnemyShipAI enemyMovement;
        [SerializeField] private WeaponGroupCombat playerWeapons;
        [SerializeField] private ShipCustomization shipyard;

        [Header("Arcade Flooding")]
        [SerializeField, Min(0.001f)] private float ingressPerOpening = 0.10f;
        [SerializeField, Min(0f)] private float drainWhenPatched = 0.025f;
        [SerializeField, Range(0.1f, 0.95f)] private float criticalFloodFraction = 0.38f;
        [SerializeField, Min(0.1f)] private float sinkingSpeed = 0.95f;
        [SerializeField, Min(0f)] private float maxListDegrees = 19f;
        [SerializeField, Min(0f)] private float maxTrimDegrees = 12f;
        [SerializeField, Range(0, 10)] private int initialRepairKits = 3;

        private readonly float[] compartments = new float[6];
        private readonly List<Opening> openings = new List<Opening>();
        private readonly HashSet<int> patches = new HashSet<int>();
        private Collider[] rootColliders;
        private bool[] initiallyEnabledColliders;
        private float nextScan;
        private float sinkDepth;
        private bool sinking;
        private int repairKits;
        private int submergedOpenings;

        public bool IsSinking => sinking;
        public float FloodFraction
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < compartments.Length; ++i) total += compartments[i];
                return total / compartments.Length;
            }
        }
        public int Breaches => openings.Count;
        public int SubmergedUnpatchedBreaches => submergedOpenings;
        public int RemainingRepairKits => repairKits;
        public float CompartmentFraction(int index) => index >= 0 && index < 6 ? compartments[index] : 0f;

        public void Configure(VoxelHull shipHull, OceanWaves waves, Transform visualRig,
            ShipArcadeController player, EnemyShipAI enemy, WeaponGroupCombat weapons,
            ShipCustomization customization)
        {
            hull = shipHull;
            ocean = waves;
            floodVisualPivot = visualRig;
            playerMovement = player;
            enemyMovement = enemy;
            playerWeapons = weapons;
            shipyard = customization;
        }

        private void Awake()
        {
            if (hull == null) hull = GetComponentInChildren<VoxelHull>(true);
            if (ocean == null) ocean = FindFirstObjectByType<OceanWaves>();
            rootColliders = GetComponents<Collider>();
            initiallyEnabledColliders = new bool[rootColliders.Length];
            for (int i = 0; i < rootColliders.Length; ++i)
                initiallyEnabledColliders[i] = rootColliders[i].enabled;
            repairKits = initialRepairKits;
        }

        private void FixedUpdate()
        {
            if (hull == null || hull.Definition == null || ocean == null) return;
            if (shipyard != null && shipyard.InShipyard) return;

            // Supports the existing M4 "Reset Target" button without modifying it.
            if (hull.TotalBlocks > 0 && hull.RemainingBlocks == hull.TotalBlocks)
            {
                if (openings.Count != 0 || FloodFraction > 0f || patches.Count != 0 || sinking)
                    ClearWaterAndBreaches(sinking);
                return;
            }

            if (Time.time >= nextScan && !sinking)
            {
                nextScan = Time.time + 0.18f;
                ScanDamagedExternalSkin();
            }

            if (sinking)
            {
                sinkDepth = Mathf.Min(9f, sinkDepth + sinkingSpeed * Time.fixedDeltaTime);
                return;
            }

            float[] inflow = new float[6];
            submergedOpenings = 0;
            float halfBlock = hull.Definition.BlockSize * 0.5f;
            foreach (Opening hole in openings)
            {
                if (patches.Contains(hole.key)) continue;
                Vector3 center = hull.transform.TransformPoint(
                    hull.Definition.BlockCenter(hole.x, hole.y, hole.z));
                float sea = ocean.SampleHeight(center.x, center.z, Time.fixedTime);
                float depth = sea - (center.y - halfBlock);
                if (depth <= 0f) continue; // A hole above the actual waterline doesn't leak.
                submergedOpenings++;
                inflow[hole.compartment] += ingressPerOpening *
                    Mathf.Clamp(0.35f + depth * 1.6f, 0.1f, 2f);
            }
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < 6; ++i)
            {
                float delta = inflow[i] > 0f ? inflow[i] : -drainWhenPatched;
                compartments[i] = Mathf.Clamp01(compartments[i] + delta * dt);
            }

            if (FloodFraction >= criticalFloodFraction ||
                (hull.TotalBlocks > 0 && hull.RemainingBlocks < hull.TotalBlocks * 0.1f))
                StartSinking();
        }

        private void ScanDamagedExternalSkin()
        {
            openings.Clear();
            HullDefinition def = hull.Definition;
            for (int x = 0; x < def.Width; ++x)
            for (int y = 0; y < def.Height; ++y)
            for (int z = 0; z < def.Length; ++z)
            {
                if (!def.ContainsBlock(x, y, z) || hull.HasBlock(x, y, z)) continue;
                // Only destroyed originally OUTSIDE hull cells count as breaches.
                // The deck is intentionally excluded; bottom and four sides count.
                bool outer = y == 0 ||
                    !def.ContainsBlock(x - 1, y, z) || !def.ContainsBlock(x + 1, y, z) ||
                    !def.ContainsBlock(x, y, z - 1) || !def.ContainsBlock(x, y, z + 1);
                if (!outer) continue;
                int side = x < def.Width / 2 ? 0 : 1; // 0 port, 1 starboard
                int section = Mathf.Min(2, 3 * z / def.Length); // stern, middle, bow
                openings.Add(new Opening
                {
                    x = x, y = y, z = z,
                    key = (x * def.Height + y) * def.Length + z,
                    compartment = section * 2 + side
                });
            }
        }

        private void LateUpdate()
        {
            if (floodVisualPivot == null) return;
            float port = compartments[0] + compartments[2] + compartments[4];
            float starboard = compartments[1] + compartments[3] + compartments[5];
            float stern = compartments[0] + compartments[1];
            float bow = compartments[4] + compartments[5];
            // Roll right side DOWN if the starboard compartments fill more.
            float list = -Mathf.Clamp((starboard - port) * 0.60f, -1f, 1f) * maxListDegrees;
            float trim = Mathf.Clamp((bow - stern) * 0.60f, -1f, 1f) * maxTrimDegrees;
            Quaternion targetRotation = Quaternion.Euler(trim, 0f, list);
            float immersion = FloodFraction * 1.45f + sinkDepth;
            float blend = 1f - Mathf.Exp(-3.5f * Time.deltaTime);
            floodVisualPivot.localRotation = Quaternion.Slerp(floodVisualPivot.localRotation, targetRotation, blend);
            floodVisualPivot.localPosition = Vector3.Lerp(floodVisualPivot.localPosition,
                Vector3.down * immersion, blend);
        }

        private void StartSinking()
        {
            sinking = true;
            if (playerMovement != null) playerMovement.enabled = false;
            if (enemyMovement != null) enemyMovement.enabled = false;
            if (playerWeapons != null) playerWeapons.enabled = false;
            // Don't leave a ghost collision box above the visible sinking ship.
            if (rootColliders != null)
                foreach (Collider rootCollider in rootColliders)
                    if (rootCollider != null) rootCollider.enabled = false;
            Debug.Log("Boatships: " + gameObject.name + " is sinking.");
        }

        /// <summary>
        /// Emergency action: temporarily seals all CURRENT outer breaches and pumps
        /// water slowly. Does not restore destroyed voxels. Future hits may open new holes.
        /// </summary>
        public bool EmergencyPatch()
        {
            if (sinking || repairKits <= 0) return false;
            ScanDamagedExternalSkin();
            if (openings.Count == 0) return false;
            int added = 0;
            foreach (Opening hole in openings)
                if (patches.Add(hole.key)) added++;
            if (added == 0) return false;
            repairKits--;
            submergedOpenings = 0;
            return true;
        }

        public void ResetShipForDemo()
        {
            if (hull != null) hull.ResetHull();
            ClearWaterAndBreaches(true);
        }

        private void ClearWaterAndBreaches(bool restoreControllers)
        {
            for (int i = 0; i < compartments.Length; ++i) compartments[i] = 0f;
            openings.Clear();
            patches.Clear();
            submergedOpenings = 0;
            sinkDepth = 0f;
            sinking = false;
            repairKits = initialRepairKits;
            nextScan = 0f;
            if (floodVisualPivot != null)
            {
                floodVisualPivot.localPosition = Vector3.zero;
                floodVisualPivot.localRotation = Quaternion.identity;
            }
            if (!restoreControllers) return;
            if (playerMovement != null) playerMovement.enabled = shipyard == null || !shipyard.InShipyard;
            if (enemyMovement != null) enemyMovement.enabled = true;
            if (playerWeapons != null) playerWeapons.enabled = true;
            if (rootColliders != null)
                for (int i = 0; i < rootColliders.Length; ++i)
                    if (rootColliders[i] != null) rootColliders[i].enabled = initiallyEnabledColliders[i];
        }
    }
}
