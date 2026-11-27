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
        public bool HullEditing { get; set; }
        public string Status { get; private set; } = "Select equipment then click a deck cell.";
        public int InstalledCount => installed.Count;

        public int CountInstalled(ShipEquipmentKind kind)
        {
            int count = 0;
            foreach (Placement placement in installed) if (placement.definition != null && placement.definition.Kind == kind) count++;
            return count;
        }
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
            // Start with the ship style's mast, helm and rudder, and one cannon on each side.
            // The remaining cannon ports are left open for the shipyard.
            BuildFittings();
            TryInstallHardpoint(mast, FirstSlotId(HullHardpointType.Mast));
            TryInstallHardpoint(rudder, FirstSlotId(HullHardpointType.Rudder));
            InstallStarterCannons();
            // Starter equipment is free; charges only apply to subsequent placements.
            chargeForNewPlacements = inventory != null;
            SetShipyard(true);
            Status = "Shipyard ready. Click the deck to place equipment.";
        }

        /// <summary>A new or recovered ship comes fully armed: every empty cannon port (with intact deck)
        /// gets a free cannon. Cannons already mounted are kept.</summary>
        public int InstallStarterCannons()
        {
            if (hull == null || hull.Definition == null || cannon == null) return 0;
            bool wasRestoring = restoringPlacements;
            restoringPlacements = true; // free of charge, and allowed away from a shipyard
            int added = 0;
            try
            {
                foreach (HullHardpoint slot in hull.Definition.Hardpoints)
                    if (slot.type == HullHardpointType.Cannon && !SlotFilled(slot.id) && TryInstallHardpoint(cannon, slot.id))
                        added++;
            }
            finally { restoringPlacements = wasRestoring; }
            return added;
        }

        public void SetShipyard(bool isOpen)
        {
            InShipyard = isOpen;
            if (shipController != null) shipController.enabled = !isOpen;
            if (damageDemo != null) damageDemo.enabled = !isOpen;
            Status = isOpen ? "Shipyard: select an item, then click the deck." : "Sailing. H or click hull to test damage.";
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
            if (IsSlotted(definition) && hull.Definition.CountSlots(SlotType(definition)) > 1)
            {
                HullHardpointType type = SlotType(definition);
                Status = "Selected " + definition.DisplayName + " (" + CountInstalled(type) + "/" +
                    hull.Definition.CountSlots(type) + " " + SlotNoun(type) + "s filled). Click an open " +
                    SlotNoun(type) + " to mount it, or a mounted one to remove it.";
            }
            else if (IsSlotted(definition))
            {
                HullHardpoint socket = FindFirstHardpoint(SlotType(definition));
                if (socket == null)
                {
                    Status = "This hull has no compatible hardpoint.";
                    return;
                }
                if (RemoveHardpoint(socket.id))
                    Status = definition.DisplayName + " removed. Click the button again to reinstall.";
                else if (TryInstallHardpoint(definition, socket.id))
                    Status = definition.DisplayName + " installed at " + socket.id + ".";
                else
                    Status = "Unable to install " + definition.DisplayName + ".";
            }
            else
                Status = "Selected " + definition.DisplayName + ". Click the deck to place (R rotates).";
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
                    bool hardpoint = IsSlotted(def);
                    if (hardpoint != (pass == 0)) continue;
                    // Saves from before ship styles kept cannons on the free deck grid: move them to an open port.
                    bool ok = hardpoint ? TryInstallHardpoint(def, item.hardpointId) ||
                            TryInstallHardpoint(def, FirstFreeSlotId(SlotType(def))) :
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
            Status = "Remove mode: click a placed deck component.";
        }

        public void Rotate()
        {
            rotationSteps = (rotationSteps + 1) % 4;
            Status = "Rotation: " + RotationDegrees + "°. Click the deck to place.";
        }

        public bool TryInstallDeck(ShipEquipmentDefinition definition, int x, int z, int quarterTurns)
        {
            if (hull == null || hull.Definition == null || definition == null ||
                IsSlotted(definition)) return false;
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
            newPlacement.instance = MakeEquipment(definition, localPosition, quarterTurns * 90f);
            installed.Add(newPlacement);
            Status = definition.DisplayName + " installed (" + x + ", " + z + ").";
            return true;
        }

        public bool TryInstallHardpoint(ShipEquipmentDefinition definition, string socketId)
        {
            if (hull == null || hull.Definition == null || definition == null ||
                !IsSlotted(definition)) return false;
            if (!AuthorizedToBuild)
            {
                Status = "Dock at a port before changing hardpoints.";
                return false;
            }
            HullHardpoint socket = null;
            foreach (HullHardpoint s in hull.Definition.Hardpoints)
                if (s.id == socketId && s.type == SlotType(definition)) socket = s;
            if (socket == null || (DeckMounted(socket.type) && !DeckIntact(socket))) return false;
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
            newPlacement.instance = MakeEquipment(definition, socket.localPosition, socket.yaw);
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

        // ───────────────────────── Ship style slots ─────────────────────────
        // A hull's style fixes where its equipment goes. Cannons always mount in cannon ports and storage,
        // armour and cabins in upgrade slots, whatever their asset says, so older equipment assets keep working.

        public static bool IsUpgrade(ShipEquipmentDefinition d) =>
            d != null && (d.Kind == ShipEquipmentKind.Storage || d.Kind == ShipEquipmentKind.Armor || d.Kind == ShipEquipmentKind.Cabin);

        public static bool IsSlotted(ShipEquipmentDefinition d) =>
            d != null && (d.Kind == ShipEquipmentKind.Cannon || IsUpgrade(d) || d.Placement == ShipPlacementKind.Hardpoint);

        public static HullHardpointType SlotType(ShipEquipmentDefinition d) =>
            d.Kind == ShipEquipmentKind.Cannon ? HullHardpointType.Cannon : IsUpgrade(d) ? HullHardpointType.Upgrade : d.HardpointType;

        /// <summary>Slots that hold deck equipment and are lost with the deck beneath them.</summary>
        private static bool DeckMounted(HullHardpointType type) => type == HullHardpointType.Cannon || type == HullHardpointType.Upgrade;

        private static string SlotNoun(HullHardpointType type) =>
            type == HullHardpointType.Cannon ? "cannon port" : type == HullHardpointType.Upgrade ? "upgrade slot" : "slot";

        private string FirstSlotId(HullHardpointType type) => FindFirstHardpoint(type)?.id;

        private HullHardpoint FindSlot(string id)
        {
            foreach (HullHardpoint slot in hull.Definition.Hardpoints) if (slot.id == id) return slot;
            return null;
        }

        private bool SlotFilled(string id)
        {
            foreach (Placement p in installed) if (p.hardpointId == id) return true;
            return false;
        }

        private string FirstFreeSlotId(HullHardpointType type)
        {
            foreach (HullHardpoint slot in hull.Definition.Hardpoints)
                if (slot.type == type && !SlotFilled(slot.id)) return slot.id;
            return null;
        }

        /// <summary>How many of this ship style's slots of a type are filled.</summary>
        public int CountInstalled(HullHardpointType type)
        {
            int count = 0;
            foreach (HullHardpoint slot in hull.Definition.Hardpoints)
                if (slot.type == type && SlotFilled(slot.id)) count++;
            return count;
        }

        /// <summary>Nearest deck slot of a type to a hull-local deck point, within half a metre.</summary>
        private HullHardpoint SlotNear(Vector3 local, HullHardpointType type)
        {
            HullHardpoint best = null;
            float bestDistance = 0.5f;
            foreach (HullHardpoint slot in hull.Definition.Hardpoints)
            {
                if (slot.type != type) continue;
                float distance = Vector2.Distance(new Vector2(local.x, local.z), new Vector2(slot.localPosition.x, slot.localPosition.z));
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = slot;
            }
            return best;
        }

        /// <summary>Shipyard click on a slotted item: fill an open slot, empty one holding the same item,
        /// or swap out a different item (an upgrade slot takes storage, armour or a cabin).</summary>
        private bool ToggleSlotAt(Vector3 local, ShipEquipmentDefinition definition)
        {
            HullHardpointType type = SlotType(definition);
            HullHardpoint slot = SlotNear(local, type);
            if (slot == null) return false;
            ShipEquipmentDefinition current = null;
            foreach (Placement p in installed) if (p.hardpointId == slot.id) current = p.definition;
            if (current == definition)
            {
                // Never remove on a plain click (the cannon is selected by default, so this lost guns by accident).
                Status = definition.DisplayName + " is already mounted there. Use Remove to take it off.";
                return true;
            }
            if (current != null && inventory != null && chargeForNewPlacements && !inventory.CanAffordEquipment(definition.Kind))
            {
                Status = inventory.LastMessage; // keep what is there rather than leave the slot empty
                return true;
            }
            if (current != null) RemoveHardpoint(slot.id);
            if (TryInstallHardpoint(definition, slot.id))
                Status = definition.DisplayName + " mounted at " + slot.id + " (" + CountInstalled(type) + "/" +
                    hull.Definition.CountSlots(type) + ").";
            else if (inventory != null && !inventory.CanAffordEquipment(definition.Kind))
                Status = inventory.LastMessage;
            else
                Status = "Unable to mount " + definition.DisplayName + " there.";
            return true;
        }

        private readonly List<(HullHardpoint slot, GameObject marker)> portMarkers = new List<(HullHardpoint, GameObject)>();

        /// <summary>Fixed parts of the ship style that are not equipment: the helm, and shipyard markers
        /// showing open cannon ports and upgrade slots.</summary>
        private void BuildFittings()
        {
            Material wood = rudder != null && rudder.Material != null ? rudder.Material : mast != null ? mast.Material : null;
            Material iron = cannon != null ? cannon.Material : wood;
            foreach (HullHardpoint slot in hull.Definition.Hardpoints)
            {
                if (slot.type == HullHardpointType.Helm) BuildHelm(slot, wood, iron);
                if (slot.type != HullHardpointType.Cannon && slot.type != HullHardpointType.Upgrade) continue;
                var marker = new GameObject(slot.type == HullHardpointType.Cannon ? "Open Cannon Port" : "Open Upgrade Slot");
                marker.transform.SetParent(hull.transform, false);
                marker.transform.localPosition = slot.localPosition;
                marker.transform.localRotation = Quaternion.Euler(0f, slot.yaw, 0f);
                if (slot.type == HullHardpointType.Cannon)
                {
                    // Round port marker with an arrow showing which way the gun will fire.
                    AddPrimitive(marker.transform, "Port Marker", PrimitiveType.Cylinder, new Vector3(0f, 0.012f, 0f),
                        new Vector3(0.36f, 0.01f, 0.36f), iron);
                    AddPrimitive(marker.transform, "Port Arrow", PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.16f),
                        new Vector3(0.06f, 0.012f, 0.2f), iron);
                }
                else
                {
                    // Square frame outlining the largest upgrade (the 3×3 cabin).
                    float half = hull.Definition.BlockSize * 1.5f;
                    for (int side = 0; side < 4; side++)
                    {
                        Quaternion turn = Quaternion.Euler(0f, side * 90f, 0f);
                        AddPrimitive(marker.transform, "Slot Frame", PrimitiveType.Cube, turn * new Vector3(0f, 0.012f, half),
                            new Vector3(half * 2f, 0.012f, 0.04f), wood, turn);
                    }
                }
                portMarkers.Add((slot, marker));
            }
        }

        private void BuildHelm(HullHardpoint slot, Material wood, Material iron)
        {
            Transform helm = new GameObject("Ship's Helm").transform;
            helm.SetParent(hull.transform, false);
            helm.localPosition = slot.localPosition;
            helm.localRotation = Quaternion.Euler(0f, slot.yaw, 0f);
            AddPrimitive(helm, "Helm Base", PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), new Vector3(0.2f, 0.04f, 0.16f), wood);
            AddPrimitive(helm, "Helm Pedestal", PrimitiveType.Cube, new Vector3(0f, 0.22f, 0f), new Vector3(0.09f, 0.4f, 0.09f), wood);
            // Spoked wheel facing aft, so the helmsman stands behind it looking at the bow.
            Vector3 hub = new Vector3(0f, 0.46f, -0.07f);
            AddPrimitive(helm, "Wheel Hub", PrimitiveType.Cylinder, hub, new Vector3(0.07f, 0.03f, 0.07f), iron, Quaternion.Euler(90f, 0f, 0f));
            const float radius = 0.19f;
            for (int i = 0; i < 4; i++)
                AddPrimitive(helm, "Wheel Spoke", PrimitiveType.Cube, hub, new Vector3(0.022f, radius * 2f + 0.12f, 0.022f), wood,
                    Quaternion.Euler(0f, 0f, i * 45f));
            for (int i = 0; i < 8; i++)
            {
                float angle = (i + 0.5f) * 45f;
                Vector3 at = hub + Quaternion.Euler(0f, 0f, angle) * new Vector3(0f, radius, 0f);
                AddPrimitive(helm, "Wheel Rim", PrimitiveType.Cube, at, new Vector3(2f * radius * Mathf.Sin(22.5f * Mathf.Deg2Rad) + 0.01f, 0.03f, 0.03f),
                    wood, Quaternion.Euler(0f, 0f, angle));
            }
        }

        private void RefreshPortMarkers()
        {
            foreach ((HullHardpoint slot, GameObject marker) in portMarkers)
                if (marker != null) marker.SetActive(InShipyard && !SlotFilled(slot.id) && DeckIntact(slot));
        }

        private bool DeckIntact(HullHardpoint slot)
        {
            HullDefinition d = hull.Definition;
            int x = Mathf.RoundToInt(slot.localPosition.x / d.BlockSize + (d.Width - 1) * 0.5f);
            int z = Mathf.RoundToInt(slot.localPosition.z / d.BlockSize + (d.Length - 1) * 0.5f);
            return hull.HasBlock(x, d.Height - 1, z);
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
                if (IsSlotted(p.definition)) continue;
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
                if (!hull.HasBlock(ix, d.Height - 1, iz)) return false;
                // The ship style's mast, helm, cannon ports and upgrade slots stay clear, filled or not.
                Vector3 deckCell = d.BlockCenter(ix, d.Height - 1, iz);
                foreach (HullHardpoint slot in d.Hardpoints)
                {
                    if (!slot.OnDeck) continue;
                    float clearance = d.BlockSize * (slot.type == HullHardpointType.Mast ? 0.9f : 1.3f);
                    if (Vector2.Distance(new Vector2(deckCell.x, deckCell.z),
                        new Vector2(slot.localPosition.x, slot.localPosition.z)) < clearance) return false;
                }
                foreach (Placement p in installed)
                {
                    if (IsSlotted(p.definition)) continue;
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

        private GameObject MakeEquipment(ShipEquipmentDefinition definition, Vector3 localPosition, float yaw)
        {
            var item = new GameObject(definition.DisplayName);
            item.transform.SetParent(hull.transform, false);
            item.transform.localPosition = localPosition;
            item.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            ShipEquipmentModels.Build(item.transform, definition, hull.Definition);
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
            RefreshPortMarkers();
            if (!InShipyard || HullEditing || hull == null) return;
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
                HullHardpoint port = SlotNear(local, HullHardpointType.Cannon) ?? SlotNear(local, HullHardpointType.Upgrade);
                if (RemoveAt(x, z)) return;
                if (port != null && RemoveHardpoint(port.id)) Status = "Equipment removed from " + port.id + ".";
                else Status = "No removable equipment at that location.";
            }
            else if (selected != null && IsSlotted(selected) && hull.Definition.CountSlots(SlotType(selected)) > 1)
            {
                if (!ToggleSlotAt(local, selected))
                    Status = "Click one of the ship's " + SlotNoun(SlotType(selected)) + "s.";
            }
            else if (selected != null && !IsSlotted(selected))
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
                if (IsSlotted(p.definition))
                {
                    // Cannons and upgrades go over the side when the deck under their slot is shot away.
                    HullHardpoint slot = FindSlot(p.hardpointId);
                    if (slot == null || !DeckMounted(slot.type) || DeckIntact(slot)) continue;
                    string gun = p.definition.DisplayName;
                    RemoveIndex(i);
                    Status = gun + " lost overboard after deck damage!";
                    continue;
                }
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
