using UnityEngine;

namespace Boatships
{
    [DisallowMultipleComponent]
    public sealed class BoatshipsConstruction : MonoBehaviour
    {
        private VoxelHull hull;
        private ShipCustomization equipment;
        private ShipArcadeController movement;
        private PortDocking docking;
        private PortEconomy economy;
        private Rigidbody body;
        private float nextRefresh;
        private int revision = -1;
        private Vector3 center;
        private LineRenderer cursor;
        private Material cursorMaterial;
        public string Status { get; private set; } = "Dock to edit the hull. Each new block costs 1 wood.";
        public VoxelHull Hull => hull;

        private void Start()
        {
            hull = GetComponent<ShipCombatTarget>().Hull;
            hull.EnableStructuralIntegrity();
            equipment = GetComponent<ShipCustomization>();
            movement = GetComponent<ShipArcadeController>();
            body = GetComponent<Rigidbody>();
            docking = FindAnyObjectByType<PortDocking>();
            economy = docking.Economy;
        }

        private void Update()
        {
            if (hull == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.3f;
            if (revision != hull.Revision) { center = hull.LiveCenterOfMass(); revision = hull.Revision; }
            float volume = hull.RemainingBlocks * Mathf.Pow(hull.Definition.BlockSize, 3f);
            body.mass = Mathf.Max(1f, volume * 350f + equipment.EquipmentMass);
            body.centerOfMass = transform.InverseTransformPoint(hull.transform.TransformPoint(center));
            float load = body.mass / Mathf.Max(1f, volume * 1000f);
            movement.HullDraft = Mathf.Clamp((load - 0.35f) * 0.35f, 0f, 0.65f);
            movement.HullSpeedMultiplier = Mathf.Clamp(1.15f - load * 0.3f, 0.35f, 1f);
        }

        public bool Edit(int x, int y, int z, bool add)
        {
            if (hull == null || docking == null || !docking.IsDocked || !equipment.InShipyard)
            { Status = "Dock and open the shipyard first."; return false; }
            if (!hull.CanEditCell(x, y, z, add))
            { Status = "Choose an empty adjacent cell to add, or a live cell to remove."; return false; }
            var before = hull.CaptureVoxelDamage();
            bool result = economy.TrySpend(0, add ? 1 : 0, 0, () => hull.TryEditCell(x, y, z, add));
            if (!result) hull.RestoreVoxelDamage(before);
            Status = result ? (add ? "Hull block installed." : "Block removed. Disconnected sections detached.") : economy.LastMessage;
            return result;
        }

        public void ShowCursor(bool visible, Vector3Int cell)
        {
            if (equipment != null) equipment.HullEditing = visible;
            if (!visible) { if (cursor != null) cursor.enabled = false; return; }
            if (hull == null || !hull.InBounds(cell.x, cell.y, cell.z)) return;
            if (cursor == null)
            {
                GameObject go = new GameObject("Hull Cell Selection");
                go.transform.SetParent(hull.transform, false);
                cursor = go.AddComponent<LineRenderer>();
                cursor.useWorldSpace = false;
                cursor.widthMultiplier = 0.018f;
                cursor.generateLightingData = true;
                cursorMaterial = new Material(hull.GetComponent<Renderer>().sharedMaterial);
                cursorMaterial.color = Color.cyan;
                cursor.sharedMaterial = cursorMaterial;
            }
            cursor.enabled = true;
            Vector3 p = hull.Definition.BlockCenter(cell.x, cell.y, cell.z);
            float h = hull.Definition.BlockSize * 0.52f;
            Vector3[] corners = { new Vector3(-h,-h,-h), new Vector3(h,-h,-h), new Vector3(h,-h,h),
                new Vector3(-h,-h,h), new Vector3(-h,h,-h), new Vector3(h,h,-h),
                new Vector3(h,h,h), new Vector3(-h,h,h) };
            int[] route = { 0,1,2,3,0,4,5,1,5,6,2,6,7,3,7,4 };
            cursor.positionCount = route.Length;
            for (int i = 0; i < route.Length; i++) cursor.SetPosition(i, p + corners[route[i]]);
        }

        public void RecoverAtPort()
        {
            ShipFlooding flooding = GetComponent<ShipFlooding>();
            if (flooding == null || !flooding.IsSinking || docking == null || !docking.HasPort)
            { Status = "Port recovery is available after the flagship sinks."; return; }
            var damage = hull.CaptureVoxelDamage();
            ShipFloodRecord flood = flooding.CaptureFlooding();
            Vector3 position = body.position;
            int fee = Mathf.Min(150, economy.Gold);
            bool recovered = economy.TrySpend(fee, 0, 0, () =>
            {
                flooding.ResetShipForDemo();
                equipment.InstallStarterCannons(); // the recovered ship is re-armed at every port
                docking.JumpToPortForTesting();
                transform.position = body.position;
                Physics.SyncTransforms();
            });
            if (!recovered)
            {
                hull.RestoreVoxelDamage(damage);
                flooding.RestoreFlooding(flood);
                body.position = position;
                Status = "Recovery could not be saved. " + economy.LastMessage;
                return;
            }
            docking.Dock();
            Status = "Flagship recovered for " + fee + " gold. Sail back to rescue overboard crew.";
        }

        private void OnDisable() { if (equipment != null) equipment.HullEditing = false; }
        private void OnDestroy() { if (cursorMaterial != null) Destroy(cursorMaterial); }
    }
}
