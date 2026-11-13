using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Small Canvas-based mobile/editor shipyard panel. Buttons bind at runtime.</summary>
    public sealed class ShipCustomizationUI : MonoBehaviour
    {
        [SerializeField] private ShipCustomization shipyard;
        [SerializeField] private Button toggleButton;
        [SerializeField] private Button cannonButton;
        [SerializeField] private Button storageButton;
        [SerializeField] private Button armorButton;
        [SerializeField] private Button cabinButton;
        [SerializeField] private Button mastButton;
        [SerializeField] private Button rudderButton;
        [SerializeField] private Button rotateButton;
        [SerializeField] private Button removeButton;
        [SerializeField] private Text modeLabel;
        [SerializeField] private Text statusLabel;
        [SerializeField] private GameObject equipmentButtons;

        public void Configure(ShipCustomization customization, Button toggle, Button cannon, Button storage,
            Button armor, Button cabin, Button mast, Button rudder, Button rotate, Button remove,
            Text mode, Text status, GameObject actions)
        {
            shipyard = customization;
            toggleButton = toggle;
            cannonButton = cannon;
            storageButton = storage;
            armorButton = armor;
            cabinButton = cabin;
            mastButton = mast;
            rudderButton = rudder;
            rotateButton = rotate;
            removeButton = remove;
            modeLabel = mode;
            statusLabel = status;
            equipmentButtons = actions;
        }

        private void Awake()
        {
            if (shipyard == null) { enabled = false; return; }
            if (toggleButton != null) toggleButton.onClick.AddListener(shipyard.ToggleShipyard);
            if (cannonButton != null) cannonButton.onClick.AddListener(() => shipyard.Select(shipyard.Cannon));
            if (storageButton != null) storageButton.onClick.AddListener(() => shipyard.Select(shipyard.Storage));
            if (armorButton != null) armorButton.onClick.AddListener(() => shipyard.Select(shipyard.Armor));
            if (cabinButton != null) cabinButton.onClick.AddListener(() => shipyard.Select(shipyard.Cabin));
            if (mastButton != null) mastButton.onClick.AddListener(() => shipyard.Select(shipyard.Mast));
            if (rudderButton != null) rudderButton.onClick.AddListener(() => shipyard.Select(shipyard.Rudder));
            if (rotateButton != null) rotateButton.onClick.AddListener(shipyard.Rotate);
            if (removeButton != null) removeButton.onClick.AddListener(shipyard.SelectRemoval);
        }

        private void Update()
        {
            if (shipyard == null) return;
            if (modeLabel != null) modeLabel.text = shipyard.InShipyard ? "Finish / Sail" : "Open Shipyard";
            if (equipmentButtons != null && equipmentButtons.activeSelf != shipyard.InShipyard)
                equipmentButtons.SetActive(shipyard.InShipyard);
            if (statusLabel != null)
            {
                statusLabel.text = "Selected: " + shipyard.SelectedName +
                    "   |   Rotation: " + shipyard.RotationDegrees + "°\n" +
                    shipyard.InventoryStatus + "\n" +
                    "Items: " + shipyard.InstalledCount +
                    "   |   Added mass: " + shipyard.EquipmentMass.ToString("0") + " kg\n" +
                    shipyard.Status;
            }
        }
    }
}
