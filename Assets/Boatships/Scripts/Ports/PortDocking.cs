using UnityEngine;
using UnityEngine.InputSystem;

namespace Boatships
{
    /// <summary>
    /// Docking at seeded PortSite islands. Manages the existing shipyard/movement systems
    /// without changing any Milestone 1–6 scripts.
    /// </summary>
    [DefaultExecutionOrder(400)]
    [DisallowMultipleComponent]
    public sealed class PortDocking : MonoBehaviour
    {
        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private Transform ship;
        [SerializeField] private ShipArcadeController movement;
        [SerializeField] private ShipCustomization customization;
        [SerializeField] private WeaponGroupCombat weapons;
        [SerializeField] private ShipFlooding flooding;
        [SerializeField] private VoxelHull hull;
        [SerializeField] private PortEconomy economy;
        [SerializeField, Min(5f)] private float dockRangeBeyondShore = 15f;

        private ArchipelagoWorld.IslandSite nearestPort;
        private bool hasPort;
        private bool docked;
        private bool viewingShipyard;
        private int knownSeed = int.MinValue;

        public bool IsDocked => docked;
        public bool ViewingShipyard => viewingShipyard;
        public bool HasPort => hasPort;
        public bool CanDock => !docked && hasPort && DistanceToPort <= DockingDistance;
        public float DockingDistance => nearestPort.Radius * 0.78f + dockRangeBeyondShore;
        public float DistanceToPort => ship != null ?
            Vector2.Distance(new Vector2(ship.position.x, ship.position.z), nearestPort.Center) : float.MaxValue;
        public ArchipelagoWorld.IslandSite NearestPort => nearestPort;
        public ArchipelagoWorld.IslandSite DockedPort { get; private set; }
        public PortEconomy Economy => economy;
        public VoxelHull Hull => hull;
        public ShipFlooding Flooding => flooding;
        public string Status { get; private set; } = "Approach a port to dock.";

        public void Configure(ArchipelagoWorld generatedWorld, Transform controlledShip,
            ShipArcadeController controller, ShipCustomization shipyard,
            WeaponGroupCombat weaponController, ShipFlooding floodingController,
            VoxelHull targetHull, PortEconomy portEconomy)
        {
            world = generatedWorld;
            ship = controlledShip;
            movement = controller;
            customization = shipyard;
            weapons = weaponController;
            flooding = floodingController;
            hull = targetHull;
            economy = portEconomy;
        }

        private void Start()
        {
            SyncSeed();
            RefreshPort();
        }

        private void Update()
        {
            SyncSeed();
            RefreshPort();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.eKey.wasPressedThisFrame)
                {
                    if (docked) Undock();
                    else Dock();
                }
                if (keyboard.oKey.wasPressedThisFrame) JumpToPortForTesting();
            }
            if (docked)
            {
                // The M3 Shipyard UI also has its own Finish/Sail button.
                // Treat that button as "Back to Port" while docked.
                if (customization != null && !customization.InShipyard)
                {
                    // "Finish / Sail" in the existing construction panel returns to harbor.
                    viewingShipyard = false;
                    customization.SetShipyard(true);
                }
                HoldShipStopped();
            }
        }

        private void FixedUpdate()
        {
            if (docked) HoldShipStopped();
        }

        private void HoldShipStopped()
        {
            if (movement != null) movement.enabled = false;
            if (weapons != null) weapons.enabled = false;
        }

        private void SyncSeed()
        {
            if (world == null || economy == null || knownSeed == world.Seed) return;
            if (docked) Undock();
            knownSeed = world.Seed;
            economy.UseWorldSeed(knownSeed);
        }

        private void RefreshPort()
        {
            hasPort = world != null && ship != null &&
                world.TryNearestIslandOfKind(ship.position, IslandKind.PortSite, out nearestPort);
            if (!hasPort) Status = "No ports found in the generated world.";
        }

        public void Dock()
        {
            if (!CanDock)
            {
                Status = "Sail closer to the nearest port, or use Jump to Port to test.";
                return;
            }
            DockedPort = nearestPort;
            docked = true;
            viewingShipyard = false;
            if (customization != null) customization.SetShipyard(true);
            HoldShipStopped();
            Status = "Docked. Use the Store, Tavern or Shipyard, then depart.";
        }

        public void Undock()
        {
            if (!docked) return;
            if (flooding != null && flooding.IsSinking)
            {
                Status = "The ship is sinking. Repair at the port before departing.";
                return;
            }
            docked = false;
            viewingShipyard = false;
            if (customization != null) customization.SetShipyard(false);
            if (movement != null) movement.enabled = true;
            if (weapons != null) weapons.enabled = true;
            Status = "Departed harbor. Safe sailing!";
        }

        public void OpenShipyard()
        {
            if (!docked) return;
            viewingShipyard = true;
            if (customization != null) customization.SetShipyard(true);
            HoldShipStopped();
            Status = "Shipyard construction mode. Press Back to Port to return.";
        }

        public void ReturnToPort()
        {
            if (!docked) return;
            viewingShipyard = false;
            if (customization != null) customization.SetShipyard(true);
            HoldShipStopped();
            Status = "Back at the port services.";
        }

        public void JumpToPortForTesting()
        {
            if (docked || !hasPort || ship == null) return;
            if (customization != null && customization.InShipyard)
                customization.SetShipyard(false);
            Vector2 direction = new Vector2(ship.position.x, ship.position.z) - nearestPort.Center;
            if (direction.sqrMagnitude < 0.001f) direction = Vector2.down;
            direction.Normalize();
            Vector2 point = nearestPort.Center + direction * (nearestPort.Radius * 0.78f + 6f);
            Vector3 target = new Vector3(point.x, 0.8f, point.y);
            Rigidbody body = ship.GetComponent<Rigidbody>();
            if (body != null) body.position = target;
            else ship.position = target;
            Status = "Jumped to port coast (test shortcut). Press E or tap Dock.";
        }
    }
}
