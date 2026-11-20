using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Shipyard equipment panel, docked to the left edge while building. Buttons bind at runtime.
    /// In scenes with ports the panel only appears inside the shipyard (Back to Port closes it).</summary>
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
        private CanvasGroup panel; // This component lives on the panel, so it is faded rather than deactivated.
        private bool portManaged;

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

        private void Start()
        {
            if (shipyard == null) return;
            portManaged = FindAnyObjectByType<PortDocking>() != null;
            Transform root = toggleButton != null ? toggleButton.transform.parent :
                equipmentButtons != null ? equipmentButtons.transform.parent : null;
            if (root is RectTransform rect && root.GetComponent<Canvas>() == null)
            {
                panel = rect.GetComponent<CanvasGroup>();
                if (panel == null) panel = rect.gameObject.AddComponent<CanvasGroup>();
                // Left column under the "Back to port" button, clear of the top-right screen buttons.
                BoatshipsUI.Place(rect, new Vector2(0f, 1f), new Vector2(16f, -104f), new Vector2(300f, 720f));
                Image image = rect.GetComponent<Image>();
                if (image != null) { image.color = BoatshipsUI.Panel; BoatshipsUI.Frame(rect.gameObject, 2f); }
            }
            foreach (Button button in new[] { toggleButton, cannonButton, storageButton, armorButton, cabinButton,
                         mastButton, rudderButton, rotateButton, removeButton })
                BoatshipsUI.StyleButton(button);
            if (rotateButton != null)
            {
                Text label = rotateButton.GetComponentInChildren<Text>(true);
                if (label != null && !label.text.Contains("[R]")) label.text += "  [R]";
            }
            if (statusLabel != null) statusLabel.color = BoatshipsUI.TextColor;
            // Port scenes exit through Back to Port; a second Finish button would desync the dock state.
            if (portManaged && toggleButton != null) toggleButton.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (shipyard == null) return;
            if (panel != null && portManaged)
            {
                bool show = shipyard.InShipyard;
                panel.alpha = show ? 1f : 0f;
                panel.interactable = panel.blocksRaycasts = show;
            }
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
