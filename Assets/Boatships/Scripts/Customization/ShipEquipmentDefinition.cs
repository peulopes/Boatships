using UnityEngine;

namespace Boatships
{
    public enum ShipEquipmentKind { Cannon, Storage, Armor, Cabin, Mast, Rudder }
    public enum ShipPlacementKind { DeckGrid, Hardpoint }

    /// <summary>Reusable equipment template; runtime placements live in ShipCustomization.</summary>
    [CreateAssetMenu(menuName = "Boatships/Equipment Definition", fileName = "NewEquipment")]
    public sealed class ShipEquipmentDefinition : ScriptableObject
    {
        [SerializeField] private string itemId = "new_item";
        [SerializeField] private string displayName = "New Item";
        [SerializeField] private ShipEquipmentKind kind;
        [SerializeField] private ShipPlacementKind placement = ShipPlacementKind.DeckGrid;
        [SerializeField] private HullHardpointType hardpointType;
        [SerializeField, Min(1)] private int widthCells = 1;
        [SerializeField, Min(1)] private int lengthCells = 1;
        [SerializeField, Min(0f)] private float mass = 10f;
        [SerializeField] private Material material;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public ShipEquipmentKind Kind => kind;
        public ShipPlacementKind Placement => placement;
        public HullHardpointType HardpointType => hardpointType;
        public int WidthCells => Mathf.Max(1, widthCells);
        public int LengthCells => Mathf.Max(1, lengthCells);
        public float Mass => mass;
        public Material Material => material;

#if UNITY_EDITOR
        public void SetDemo(string id, string display, ShipEquipmentKind equipmentKind,
            ShipPlacementKind placementKind, HullHardpointType socket, int width, int length,
            float itemMass, Material itemMaterial)
        {
            itemId = id;
            displayName = display;
            kind = equipmentKind;
            placement = placementKind;
            hardpointType = socket;
            widthCells = Mathf.Max(1, width);
            lengthCells = Mathf.Max(1, length);
            mass = itemMass;
            material = itemMaterial;
        }
#endif
    }
}
