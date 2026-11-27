using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>What a hull slot accepts. Values are serialized: append new types, never reorder.</summary>
    public enum HullHardpointType { Mast, Rudder, Propulsion, Cannon, Helm, Upgrade }

    /// <summary>A fixed location on a ship style: where its mast, helm, rudder, cannon ports and upgrades go.</summary>
    [Serializable]
    public sealed class HullHardpoint
    {
        public string id;
        public HullHardpointType type;
        public Vector3 localPosition;
        [Tooltip("Facing in degrees around the hull's up axis: 0 = bow, 90 = starboard, -90 = port, 180 = stern.")]
        public float yaw;

        public HullHardpoint(string id, HullHardpointType type, Vector3 localPosition, float yaw = 0f)
        {
            this.id = id;
            this.type = type;
            this.localPosition = localPosition;
            this.yaw = yaw;
        }

        /// <summary>Slots that sit on the deck (and so keep deck-grid equipment clear of them).</summary>
        public bool OnDeck => type == HullHardpointType.Mast || type == HullHardpointType.Cannon ||
            type == HullHardpointType.Helm || type == HullHardpointType.Upgrade;
    }

    /// <summary>
    /// Read-only template for a predefined hull. This asset is NOT damaged at runtime;
    /// each VoxelHull maintains its own independent damage state.
    /// </summary>
    [CreateAssetMenu(fileName = "NewHull", menuName = "Boatships/Hull Definition")]
    public sealed class HullDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string hullId = "starter_sloop";
        [SerializeField] private string displayName = "Starter Sloop";

        [Header("Voxel Layout")]
        [SerializeField, Range(3, 16)] private int widthBlocks = 9;
        [SerializeField, Range(2, 10)] private int heightBlocks = 4;
        [SerializeField, Range(5, 40)] private int lengthBlocks = 19;
        [SerializeField, Min(0.05f)] private float blockSize = 0.25f;
        [SerializeField, Min(1f)] private float blockHealth = 40f;

        [Header("Fixed Equipment Positions")]
        [SerializeField] private List<HullHardpoint> hardpoints = new List<HullHardpoint>();

        public string HullId => hullId;
        public string DisplayName => displayName;
        public int Width => widthBlocks;
        public int Height => heightBlocks;
        public int Length => lengthBlocks;
        public float BlockSize => blockSize;
        public float BlockHealth => blockHealth;
        public IReadOnlyList<HullHardpoint> Hardpoints => hardpoints;

        public int CountSlots(HullHardpointType type)
        {
            int count = 0;
            foreach (HullHardpoint slot in hardpoints) if (slot.type == type) count++;
            return count;
        }

        /// <summary>The basic ship style: a single-masted sloop with a stern helm and four cannon ports,
        /// two on each side. Deck top is at y = 0.5.</summary>
        public static List<HullHardpoint> StarterSloopLayout() => new List<HullHardpoint>
        {
            new HullHardpoint("main_mast", HullHardpointType.Mast, new Vector3(0f, 0.40f, 0.45f)),
            new HullHardpoint("stern_rudder", HullHardpointType.Rudder, new Vector3(0f, -0.10f, -2.30f)),
            new HullHardpoint("helm", HullHardpointType.Helm, new Vector3(0f, 0.5f, -1.7f)),
            new HullHardpoint("cannon_port_fore", HullHardpointType.Cannon, new Vector3(-0.6f, 0.5f, 1.15f), -90f),
            new HullHardpoint("cannon_port_aft", HullHardpointType.Cannon, new Vector3(-0.6f, 0.5f, -0.55f), -90f),
            new HullHardpoint("cannon_starboard_fore", HullHardpointType.Cannon, new Vector3(0.6f, 0.5f, 1.15f), 90f),
            new HullHardpoint("cannon_starboard_aft", HullHardpointType.Cannon, new Vector3(0.6f, 0.5f, -0.55f), 90f),
            // Upgrade slots (storage, armour, cabin) on the foredeck and between the aft guns and the helm;
            // both have room for the 3×3 cabin.
            new HullHardpoint("upgrade_foredeck", HullHardpointType.Upgrade, new Vector3(0f, 0.5f, 1.6f)),
            new HullHardpoint("upgrade_afterdeck", HullHardpointType.Upgrade, new Vector3(0f, 0.5f, -0.95f))
        };

        /// <summary>Predefined curved/tapered volume; can later be replaced with imported voxel layouts.</summary>
        public bool ContainsBlock(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= Width || y >= Height || z >= Length)
                return false;

            float longitudinal = Mathf.Abs((z - (Length - 1) * 0.5f) / (Length * 0.5f));
            float beam = 1f - 0.72f * Mathf.Pow(longitudinal, 3f);
            float layerWidth = y == 0 ? 0.63f : (y == Height - 1 ? 0.91f : 1f);
            float lateral = Mathf.Abs(x - (Width - 1) * 0.5f);
            float maxLateral = (Width - 1) * 0.5f * beam * layerWidth + 0.01f;
            return lateral <= maxLateral;
        }

        public Vector3 BlockCenter(int x, int y, int z)
        {
            return new Vector3(
                (x - (Width - 1) * 0.5f) * BlockSize,
                (y - (Height - 1) * 0.5f) * BlockSize,
                (z - (Length - 1) * 0.5f) * BlockSize);
        }

        /// <summary>Potential top-deck placement cell; availability also depends on runtime damage.</summary>
        public bool IsDeckCell(int x, int z) => ContainsBlock(x, Height - 1, z);

#if UNITY_EDITOR
        public void ConfigureStarterSloop()
        {
            hullId = "starter_sloop";
            displayName = "Starter Sloop";
            widthBlocks = 9;
            heightBlocks = 4;
            lengthBlocks = 19;
            blockSize = 0.25f;
            blockHealth = 40f;
            hardpoints = StarterSloopLayout();
        }
#endif

        private void OnEnable()
        {
            // Sloops made before ship styles (or before upgrade slots) get the current basic layout.
            if (hullId == "starter_sloop" &&
                (CountSlots(HullHardpointType.Cannon) == 0 || CountSlots(HullHardpointType.Upgrade) == 0))
                hardpoints = StarterSloopLayout();
        }

        private void OnValidate()
        {
            widthBlocks = Mathf.Max(3, widthBlocks);
            heightBlocks = Mathf.Max(2, heightBlocks);
            lengthBlocks = Mathf.Max(5, lengthBlocks);
            blockSize = Mathf.Max(0.05f, blockSize);
            blockHealth = Mathf.Max(1f, blockHealth);
        }
    }
}
