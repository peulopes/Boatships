using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace Boatships
{
    /// <summary>
    /// Milestone 3 shipyard: predefined hardpoints and deck-grid equipment.
    /// This is runtime-only proof of concept: no currency, persistence or prefab inventory yet.
    /// All equipment is a child of the animated VoxelHull transform.
    /// </summary>
    public sealed class ShipCustomization : MonoBehaviour
    {
        [Serializable]
        private sealed class Placement
        {
            public ShipEquipmentDefinition definition;
            public int x, z, rotationSteps;
            public string hardpointId;
            [NonSerialized] public GameObject instance;
            public bool paidFromInventory;
            public string persistentId;
        }

        [SerializeField] private VoxelHull hull;
        [SerializeField] private ShipArcadeController shipController;
        [SerializeField] private HullDamageDemo damageDemo;
        [SerializeField] private PortEconomy inventory;
        [SerializeField] private PortDocking inventoryDock;
        private bool chargeForNewPlacements;
        private bool restoringPlacements;
        [Header("Demo equipment")]
        [SerializeField] private ShipEquipmentDefinition cannon;
        [SerializeField] private ShipEquipmentDefinition storage;
        [SerializeField] private ShipEquipmentDefinition armor;
        [SerializeField] private ShipEquipmentDefinition cabin;
        [SerializeField] private ShipEquipmentDefinition mast;
        [SerializeField] private ShipEquipmentDefinition rudder;

        private readonly List<Placement> installed = new List<Placement>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private ShipEquipmentDefinition selected;
        private bool removeMode;
        private int rotationSteps;
        private bool pointerStarted;
        private bool pointerIsTouch;
        private Vector2 pointerStart;
        private float supportCheckAt;

        public bool InShipyard { get; private set; } = true;
        public bool Removing => removeMode;
        public string Status { get; private set; } = "Select equipment then tap a deck cell.";
        public int InstalledCount => installed.Count;
        public ShipEquipmentDefinition Cannon => cannon;
        public ShipEquipmentDefinition Storage => storage;
        public ShipEquipmentDefinition Armor => armor;
        public ShipEquipmentDefinition Cabin => cabin;
        public ShipEquipmentDefinition Mast => mast;
        public ShipEquipmentDefinition Rudder => rudder;
        public string SelectedName => removeMode ? "Remove" : selected != null ? selected.DisplayName : "None";
        public int RotationDegrees => rotationSteps * 90;
        public string InventoryStatus => inventory != null ? inventory.StockSummary : "";
        public void BindInventory(PortEconomy source, PortDocking port)
        {
            inventory = source;
            inventoryDock = port;
        }

        private bool AuthorizedToBuild => restoringPlacements || !chargeForNewPlacements ||
            (inventoryDock != null && inventoryDock.IsDocked && inventoryDock.ViewingShipyard);
        public float EquipmentMass
        {
            get
            {
                float sum = 0f;
                foreach (Placement p in installed) sum += p.definition.Mass;
                return sum;
            }
        }

        public void Configure(VoxelHull targetHull, ShipArcadeController controller, HullDamageDemo demo,
            ShipEquipmentDefinition cannonPart, ShipEquipmentDefinition storagePart,
            ShipEquipmentDefinition armorPart, ShipEquipmentDefinition cabinPart,
            ShipEquipmentDefinition mastPart, ShipEquipmentDefinition rudderPart)
        {
            hull = targetHull;
            shipController = controller;
            damageDemo = demo;
            cannon = cannonPart;
            storage = storagePart;
            armor = armorPart;
            cabin = cabinPart;
            mast = mastPart;
            rudder = rudderPart;
        }

        private void Start()
        {
            if (hull == null || hull.Definition == null)
            {
                enabled = false;
                Debug.LogError("Boatships shipyard has no initialized VoxelHull.", this);
                return;
            }
            selected = cannon;
            // Start with the sloop's fixed equipment installed, plus a small deck cannon.
            TryInstallHardpoint(mast, "main_mast");
            TryInstallHardpoint(rudder, "stern_rudder");
            TryInstallDeck(cannon, 6, 9, 1);
            // Starter equipment is free; charges only apply to subsequent placements.
            chargeForNewPlacements = inventory != null;
            SetShipyard(true);
            Status = "Shipyard ready. Tap deck to place equipment; finish to sail.";
        }

        public void SetShipyard(bool isOpen)
        {
            InShipyard = isOpen;
            if (shipController != null) shipController.enabled = !isOpen;
            if (damageDemo != null) damageDemo.enabled = !isOpen;
            Status = isOpen ? "Shipyard: select an item, then tap deck." : "Sailing. H or click hull to test damage.";
        }

        public void ToggleShipyard() => SetShipyard(!InShipyard);

        public void Select(ShipEquipmentDefinition definition)
        {
            if (definition == null) return;
            if (!InShipyard) SetShipyard(true);
            if (!AuthorizedToBuild)
            {
                Status = "New equipment can only be installed at a docked shipyard.";
                return;
            }
            removeMode = false;
            selected = definition;
            if (definition.Placement == ShipPlacementKind.Hardpoint)
            {
                HullHardpoint socket = FindFirstHardpoint(definition.HardpointType);
                if (socket == null)
                {
                    Status = "This hull has no compatible hardpoint.";
                    return;
                }
                if (RemoveHardpoint(socket.id))
                    Status = definition.DisplayName + " removed. Tap button again to reinstall.";
                else if (TryInstallHardpoint(definition, socket.id))
                    Status = definition.DisplayName + " installed at " + socket.id + ".";
                else
                    Status = "Unable to install " + definition.DisplayName + ".";
            }
            else
                Status = "Selected " + definition.DisplayName + ". Tap deck to place (R rotates).";
        }

        /// <summary>Stable, save-friendly shipyard layout. Never serializes scene references.</summary>
        public List<ShipEquipmentRecord> CaptureEquipment()
        {
            var records = new List<ShipEquipmentRecord>(installed.Count);
            foreach (Placement p in installed)
            {
                if (p.definition == null) continue;
                if (string.IsNullOrEmpty(p.persistentId)) p.persistentId = Guid.NewGuid().ToString("N");
                records.Add(new ShipEquipmentRecord
                {
                    instanceKey = p.persistentId, itemId = p.definition.ItemId,
                    x = p.x, z = p.z, rotationSteps = p.rotationSteps,
                    hardpointId = p.hardpointId, paidFromInventory = p.paidFromInventory
                });
            }
            return records;
        }

        public string GetPersistentId(Transform root)
        {
            foreach (Placement p in installed)
                if (p.instance != null && p.instance.transform == root)
                {
                    if (string.IsNullOrEmpty(p.persistentId)) p.persistentId = Guid.NewGuid().ToString("N");
                    return p.persistentId;
                }
            return null;
        }

        public Transform FindEquipmentByPersistentId(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (Placement p in installed)
                if (p.persistentId == key && p.instance != null) return p.instance.transform;
            return null;
        }

        private ShipEquipmentDefinition FindDefinition(string id)
        {
            ShipEquipmentDefinition[] all = { cannon, storage, armor, cabin, mast, rudder };
            foreach (ShipEquipmentDefinition def in all)
                if (def != null && def.ItemId == id) return def;
            return null;
        }

        /// <summary>Restoration never charges gold/materials or refunds existing equipment.</summary>
        public void RestoreEquipment(List<ShipEquipmentRecord> data)
        {
            if (hull == null || hull.Definition == null) return;
            restoringPlacements = true;
            try
            {
                for (int i = installed.Count - 1; i >= 0; --i) RemoveIndex(i, false);
                if (data == null) return;
                // Place hardpoints first so the normal mast clearance rule remains effective.
                for (int pass = 0; pass < 2; ++pass)
                foreach (ShipEquipmentRecord item in data)
                {
                    if (item == null) continue;
                    ShipEquipmentDefinition def = FindDefinition(item.itemId);
                    if (def == null) { Debug.LogWarning("Boatships M11: Missing equipment definition " + item.itemId); continue; }
                    bool hardpoint = def.Placement == ShipPlacementKind.Hardpoint;
                    if (hardpoint != (pass == 0)) continue;
                    bool ok = hardpoint ? TryInstallHardpoint(def, item.hardpointId) :
                        TryInstallDeck(def, item.x, item.z, item.rotationSteps);
                    if (!ok) { Debug.LogWarning("Boatships M11: Skipped invalid placement: " + item.itemId); continue; }
                    Placement p = installed[installed.Count - 1];
                    p.persistentId = string.IsNullOrEmpty(item.instanceKey) ? Guid.NewGuid().ToString("N") : item.instanceKey;
                    p.paidFromInventory = item.paidFromInventory;
                }
            }
            finally { restoringPlacements = false; }
        }

        public void SelectRemoval()
        {
            if (!InShipyard) SetShipyard(true);
            if (!AuthorizedToBuild)
            {
                Status = "Dock at a port and open its shipyard to modify equipment.";
                return;
            }
            removeMode = true;
            Status = "Remove mode: tap a placed deck component.";
        }

        public void Rotate()
        {
            rotationSteps = (rotationSteps + 1) % 4;
            Status = "Rotation: " + RotationDegrees + "°. Tap deck to place.";
        }

        public bool TryInstallDeck(ShipEquipmentDefinition definition, int x, int z, int quarterTurns)
        {
            if (hull == null || hull.Definition == null || definition == null ||
                definition.Placement != ShipPlacementKind.DeckGrid) return false;
            if (!AuthorizedToBuild)
            {
                Status = "Dock at a port before placing equipment.";
                return false;
            }

            int w = WidthOf(definition, quarterTurns);
            int l = LengthOf(definition, quarterTurns);
            if (!CanPlaceDeck(x, z, w, l)) return false;

            bool paid = !restoringPlacements && chargeForNewPlacements && inventory != null;
            if (paid && !inventory.TryConsumeEquipment(definition.Kind))
            {
                Status = inventory.LastMessage;
                return false;
            }
            var newPlacement = new Placement
            {
                definition = definition, x = x, z = z,
                rotationSteps = quarterTurns % 4, paidFromInventory = paid,
                persistentId = Guid.NewGuid().ToString("N")
            };
            Vector3 localPosition = DeckOrigin(x, z, w, l);
            newPlacement.instance = MakeEquipment(definition, localPosition, quarterTurns);
            installed.Add(newPlacement);
            Status = definition.DisplayName + " installed (" + x + ", " + z + ").";
            return true;
        }

        public bool TryInstallHardpoint(ShipEquipmentDefinition definition, string socketId)
        {
            if (hull == null || hull.Definition == null || definition == null ||
                definition.Placement != ShipPlacementKind.Hardpoint) return false;
            if (!AuthorizedToBuild)
            {
                Status = "Dock at a port before changing hardpoints.";
                return false;
            }
            HullHardpoint socket = null;
            foreach (HullHardpoint s in hull.Definition.Hardpoints)
                if (s.id == socketId && s.type == definition.HardpointType) socket = s;
            if (socket == null) return false;
            foreach (Placement p in installed)
                if (p.hardpointId == socketId) return false;

            bool paid = !restoringPlacements && chargeForNewPlacements && inventory != null;
            if (paid && !inventory.TryConsumeEquipment(definition.Kind))
            {
                Status = inventory.LastMessage;
                return false;
            }
            var newPlacement = new Placement { definition = definition,
                hardpointId = socketId, paidFromInventory = paid,
                persistentId = Guid.NewGuid().ToString("N") };
            newPlacement.instance = MakeEquipment(definition, socket.localPosition, 0);
            installed.Add(newPlacement);
            return true;
        }

        private HullHardpoint FindFirstHardpoint(HullHardpointType type)
        {
            if (hull == null || hull.Definition == null) return null;
            foreach (HullHardpoint socket in hull.Definition.Hardpoints)
                if (socket.type == type) return socket;
            return null;
        }

        private bool RemoveHardpoint(string socketId)
        {
            for (int i = installed.Count - 1; i >= 0; --i)
            {
                if (installed[i].hardpointId != socketId) continue;
                RemoveIndex(i, true);
                return true;
            }
            return false;
        }

        private bool RemoveAt(int x, int z)
        {
            for (int i = installed.Count - 1; i >= 0; --i)
            {
                Placement p = installed[i];
                if (p.definition.Placement != ShipPlacementKind.DeckGrid) continue;
                int w = WidthOf(p.definition, p.rotationSteps);
                int l = LengthOf(p.definition, p.rotationSteps);
                if (x < p.x || x >= p.x + w || z < p.z || z >= p.z + l) continue;
                string removedName = p.definition.DisplayName;
                RemoveIndex(i, true);
                Status = removedName + " removed.";
                return true;
            }
            return false;
        }

        private void RemoveIndex(int i, bool playerRemoved = false)
        {
            Placement placement = installed[i];
            if (playerRemoved && placement.paidFromInventory && inventory != null)
                inventory.RefundEquipment(placement.definition.Kind);
            if (placement.instance != null)
            {
                placement.instance.SetActive(false); // Prevent combat rescans seeing pending-destroy mounts.
                Destroy(placement.instance);
            }
            installed.RemoveAt(i);
        }

        private bool CanPlaceDeck(int x, int z, int w, int l)
        {
            HullDefinition d = hull.Definition;
            if (x < 0 || z < 0 || x + w > d.Width || z + l > d.Length) return false;
            for (int ix = x; ix < x + w; ++ix)
            for (int iz = z; iz < z + l; ++iz)
            {
                if (!d.IsDeckCell(ix, iz) || !hull.HasBlock(ix, d.Height - 1, iz)) return false;
                foreach (Placement p in installed)
                {
                    // Reserve a small zone around the fixed mast hardpoint.
                    if (p.definition.Kind == ShipEquipmentKind.Mast && p.instance != null)
                    {
                        Vector3 deckCell = d.BlockCenter(ix, d.Height - 1, iz);
                        Vector3 mastPoint = p.instance.transform.localPosition;
                        if (Vector2.Distance(new Vector2(deckCell.x, deckCell.z),
                            new Vector2(mastPoint.x, mastPoint.z)) < d.BlockSize * 0.9f) return false;
                    }
                    if (p.definition.Placement != ShipPlacementKind.DeckGrid) continue;
                    if (ix >= p.x && ix < p.x + WidthOf(p.definition, p.rotationSteps) &&
                        iz >= p.z && iz < p.z + LengthOf(p.definition, p.rotationSteps)) return false;
                }
            }
            return true;
        }

        private static int WidthOf(ShipEquipmentDefinition d, int rotation) =>
            rotation % 2 == 0 ? d.WidthCells : d.LengthCells;
        private static int LengthOf(ShipEquipmentDefinition d, int rotation) =>
            rotation % 2 == 0 ? d.LengthCells : d.WidthCells;

        private Vector3 DeckOrigin(int x, int z, int w, int l)
        {
            HullDefinition d = hull.Definition;
            float top = d.BlockCenter(0, d.Height - 1, 0).y + d.BlockSize * 0.5f;
            return new Vector3(
                (x + (w - 1) * 0.5f - (d.Width - 1) * 0.5f) * d.BlockSize,
                top,
                (z + (l - 1) * 0.5f - (d.Length - 1) * 0.5f) * d.BlockSize);
        }

        private GameObject MakeEquipment(ShipEquipmentDefinition definition, Vector3 localPosition, int turns)
        {
            var item = new GameObject(definition.DisplayName);
            item.transform.SetParent(hull.transform, false);
            item.transform.localPosition = localPosition;
            item.transform.localRotation = Quaternion.Euler(0f, turns * 90f, 0f);
            float unit = hull.Definition.BlockSize;
            float w = definition.WidthCells * unit;
            float l = definition.LengthCells * unit;
            Material mat = definition.Material;
            switch (definition.Kind)
            {
                case ShipEquipmentKind.Cannon:
                    AddPrimitive(item.transform, "Carriage", PrimitiveType.Cube, new Vector3(0f, 0.12f, 0f), new Vector3(w * 0.80f, 0.20f, l * 0.8f), mat);
                    AddPrimitive(item.transform, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.27f, l * 0.25f), new Vector3(0.11f, 0.34f, 0.11f), mat, Quaternion.Euler(90f, 0f, 0f));
                    break;
                case ShipEquipmentKind.Storage:
                    AddPrimitive(item.transform, "Crate", PrimitiveType.Cube, new Vector3(0f, 0.22f, 0f), new Vector3(w * 0.90f, 0.42f, l * 0.90f), mat);
                    break;
                case ShipEquipmentKind.Armor:
                    AddPrimitive(item.transform, "Plate", PrimitiveType.Cube, new Vector3(0f, 0.045f, 0f), new Vector3(w * 0.96f, 0.08f, l * 0.96f), mat);
                    break;
                case ShipEquipmentKind.Cabin:
                    AddPrimitive(item.transform, "Cabin", PrimitiveType.Cube, new Vector3(0f, 0.4f, 0f), new Vector3(w * 0.96f, 0.78f, l * 0.96f), mat);
                    break;
                case ShipEquipmentKind.Mast:
                    AddPrimitive(item.transform, "Mast", PrimitiveType.Cylinder, new Vector3(0f, 1.13f, 0f), new Vector3(0.09f, 1.13f, 0.09f), mat);
                    AddPrimitive(item.transform, "Sail", PrimitiveType.Cube, new Vector3(0f, 1.46f, 0.1f), new Vector3(1.1f, 1.4f, 0.055f), mat);
                    break;
                case ShipEquipmentKind.Rudder:
                    AddPrimitive(item.transform, "Rudder", PrimitiveType.Cube, new Vector3(0f, -0.12f, -0.08f), new Vector3(0.21f, 0.50f, 0.13f), mat);
                    break;
            }
            return item;
        }

        private static void AddPrimitive(Transform parent, string name, PrimitiveType primitive,
            Vector3 position, Vector3 scale, Material material, Quaternion rotation = default)
        {
            GameObject mesh = GameObject.CreatePrimitive(primitive);
            mesh.name = name;
            mesh.transform.SetParent(parent, false);
            mesh.transform.localPosition = position;
            mesh.transform.localScale = scale;
            mesh.transform.localRotation = rotation == default(Quaternion) ? Quaternion.identity : rotation;
            Renderer renderer = mesh.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            Collider collider = mesh.GetComponent<Collider>();
            if (collider != null) Destroy(collider); // Ship root keeps one coarse collider.
        }

        private void Update()
        {
            if (!InShipyard || hull == null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame) Rotate();

            // Release-only tap recognition avoids building when orbiting the camera.
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                pointerStart = touchscreen.primaryTouch.position.ReadValue();
                pointerStarted = true;
                pointerIsTouch = true;
            }
            if (touchscreen != null && touchscreen.primaryTouch.press.wasReleasedThisFrame)
            {
                Vector2 release = touchscreen.primaryTouch.position.ReadValue();
                if (pointerStarted && Vector2.Distance(pointerStart, release) < 18f) TryScreenTap(release);
                pointerStarted = false;
                pointerIsTouch = false;
                return;
            }
            // Ignore synthetic mouse events generated during real touch input.
            Mouse mouse = Mouse.current;
            if (mouse != null && !pointerIsTouch)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    pointerStart = mouse.position.ReadValue();
                    pointerStarted = true;
                }
                else if (mouse.leftButton.wasReleasedThisFrame && pointerStarted)
                {
                    Vector2 release = mouse.position.ReadValue();
                    if (Vector2.Distance(pointerStart, release) < 12f) TryScreenTap(release);
                    pointerStarted = false;
                }
            }
        }

        private void TryScreenTap(Vector2 screen)
        {
            if (Camera.main == null || IsOverUI(screen)) return;
            if (!AuthorizedToBuild)
            {
                Status = "Dock at a port and select Customize Ship to build.";
                return;
            }
            HullDefinition d = hull.Definition;
            float top = d.BlockCenter(0, d.Height - 1, 0).y + d.BlockSize * 0.5f;
            Plane deck = new Plane(hull.transform.up, hull.transform.TransformPoint(new Vector3(0f, top, 0f)));
            Ray ray = Camera.main.ScreenPointToRay(screen);
            if (!deck.Raycast(ray, out float distance)) return;
            Vector3 local = hull.transform.InverseTransformPoint(ray.GetPoint(distance));
            int x = Mathf.RoundToInt(local.x / d.BlockSize + (d.Width - 1) * 0.5f);
            int z = Mathf.RoundToInt(local.z / d.BlockSize + (d.Length - 1) * 0.5f);
            if (removeMode)
            {
                if (!RemoveAt(x, z)) Status = "No deck equipment at that location.";
            }
            else if (selected != null && selected.Placement == ShipPlacementKind.DeckGrid)
            {
                if (!TryInstallDeck(selected, x, z, rotationSteps))
                    Status = inventory != null && !inventory.CanAffordEquipment(selected.Kind) ?
                        inventory.LastMessage : "Invalid cell: deck missing, occupied, or out of bounds.";
            }
        }

        private bool IsOverUI(Vector2 screen)
        {
            if (EventSystem.current == null) return false;
            uiHits.Clear();
            var pointer = new PointerEventData(EventSystem.current) { position = screen };
            EventSystem.current.RaycastAll(pointer, uiHits);
            return uiHits.Count > 0;
        }

        private void LateUpdate()
        {
            if (hull == null || hull.Definition == null || Time.time < supportCheckAt) return;
            supportCheckAt = Time.time + 0.25f;
            int deckY = hull.Definition.Height - 1;
            for (int i = installed.Count - 1; i >= 0; --i)
            {
                Placement p = installed[i];
                if (p.definition.Placement != ShipPlacementKind.DeckGrid) continue;
                bool supported = true;
                for (int x = p.x; x < p.x + WidthOf(p.definition, p.rotationSteps) && supported; ++x)
                for (int z = p.z; z < p.z + LengthOf(p.definition, p.rotationSteps); ++z)
                    if (!hull.HasBlock(x, deckY, z)) { supported = false; break; }
                if (supported) continue;
                string name = p.definition.DisplayName;
                RemoveIndex(i);
                Status = name + " detached after deck damage!";
            }
        }
    }
}
