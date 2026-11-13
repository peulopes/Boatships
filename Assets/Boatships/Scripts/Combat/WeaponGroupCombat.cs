using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boatships
{
    /// <summary>
    /// Milestone 4 weapon controller. Discovers the primitive cannon mounts produced
    /// by Milestone 3 without modifying the existing shipyard implementation.
    /// Group assignment lives in memory for this prototype (no save system yet).
    /// </summary>
    public sealed class WeaponGroupCombat : MonoBehaviour
    {
        private sealed class CannonMount
        {
            public Transform root;
            public Transform barrel;
            public int group;
            public float nextFireTime;
            public float mountYaw;
        }

        [SerializeField] private ShipCustomization shipyard;
        [SerializeField] private VoxelHull hull;
        [SerializeField] private ShipCombatTarget owner;
        [SerializeField] private OceanWaves ocean;
        [SerializeField] private VirtualJoystick aimJoystick;
        [SerializeField] private LineRenderer trajectoryLine;
        [SerializeField] private Material cannonballMaterial;

        [Header("Cannon Tuning")]
        [SerializeField, Min(1f)] private float muzzleSpeed = 22f;
        [SerializeField, Min(0.1f)] private float gravity = 18f;
        [SerializeField, Min(1f)] private float damage = 90f;
        [SerializeField, Min(0.05f)] private float blastRadius = 0.45f;
        [SerializeField, Min(0.1f)] private float reloadSeconds = 1.8f;
        [SerializeField, Range(10f, 180f)] private float cannonFiringArc = 120f;

        private readonly List<CannonMount> mounts = new List<CannonMount>();
        private readonly Dictionary<Transform, int> assignedGroups = new Dictionary<Transform, int>();
        private readonly Dictionary<Transform, float> nextFireTimes = new Dictionary<Transform, float>();
        private float scanAt;
        private int selectedCannonIndex;
        private int group = 2; // The starter deck cannon is oriented to starboard.
        private float bearingDeg = 90f;
        private float elevationDeg = 10f;
        private string status = "Manual cannon aim ready.";

        public int ActiveGroup => group;
        public float Bearing => bearingDeg;
        public float Elevation => elevationDeg;
        public string Status => status;
        public bool CombatEnabled => shipyard == null || !shipyard.InShipyard;
        public int CannonCount => mounts.Count;
        public string SelectedCannonDescription => mounts.Count == 0 ? "None" :
            (selectedCannonIndex + 1) + "/" + mounts.Count + " (Group " + mounts[selectedCannonIndex].group + ")";

        public void Configure(ShipCustomization shipyardController, VoxelHull targetHull,
            ShipCombatTarget firingShip, OceanWaves waveModel, VirtualJoystick joystick,
            LineRenderer aimVisual, Material ballMaterial)
        {
            shipyard = shipyardController;
            hull = targetHull;
            owner = firingShip;
            ocean = waveModel;
            aimJoystick = joystick;
            trajectoryLine = aimVisual;
            cannonballMaterial = ballMaterial;
        }

        private static float GroupBearing(int selected)
        {
            switch (selected)
            {
                case 1: return -90f;  // Port
                case 2: return 90f;   // Starboard
                case 3: return 0f;    // Bow
                default: return 180f; // Stern
            }
        }

        public void SelectGroup(int newGroup)
        {
            group = Mathf.Clamp(newGroup, 1, 4);
            bearingDeg = GroupBearing(group);
            status = "Selected group " + group + ". Aim manually, then fire.";
        }

        public void CycleCannon()
        {
            if (mounts.Count == 0) { status = "Install a deck cannon in the shipyard first."; return; }
            selectedCannonIndex = (selectedCannonIndex + 1) % mounts.Count;
            status = "Selected cannon " + SelectedCannonDescription + ".";
        }

        public void AssignSelectedCannonToActiveGroup()
        {
            if (mounts.Count == 0) { status = "There are no installed cannons."; return; }
            CannonMount mount = mounts[selectedCannonIndex];
            assignedGroups[mount.root] = group;
            mount.group = group;
            status = "Cannon " + (selectedCannonIndex + 1) + " assigned to group " + group + ".";
        }

        public int CountInGroup(int g)
        {
            int count = 0;
            foreach (CannonMount mount in mounts)
                if (mount.group == g) count++;
            return count;
        }

        private void Update()
        {
            if (hull == null) return;
            if (Time.time >= scanAt)
            {
                scanAt = Time.time + 0.25f;
                RefreshCannons();
            }
            bool enabledForCombat = CombatEnabled;
            if (trajectoryLine != null) trajectoryLine.enabled = enabledForCombat;
            if (!enabledForCombat) return;

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) SelectGroup(1);
                if (kb.digit2Key.wasPressedThisFrame) SelectGroup(2);
                if (kb.digit3Key.wasPressedThisFrame) SelectGroup(3);
                if (kb.digit4Key.wasPressedThisFrame) SelectGroup(4);
                if (kb.tabKey.wasPressedThisFrame) CycleCannon();
                if (kb.gKey.wasPressedThisFrame) AssignSelectedCannonToActiveGroup();
                if (kb.spaceKey.isPressed) FireSelectedGroup();
            }

            Vector2 aim = aimJoystick != null ? aimJoystick.Direction : Vector2.zero;
            if (kb != null)
            {
                aim.x += (kb.lKey.isPressed ? 1f : 0f) - (kb.jKey.isPressed ? 1f : 0f);
                aim.y += (kb.iKey.isPressed ? 1f : 0f) - (kb.kKey.isPressed ? 1f : 0f);
            }
            aim = Vector2.ClampMagnitude(aim, 1f);
            bearingDeg = Mathf.Clamp(bearingDeg + aim.x * 55f * Time.deltaTime,
                GroupBearing(group) - 80f, GroupBearing(group) + 80f);
            elevationDeg = Mathf.Clamp(elevationDeg + aim.y * 28f * Time.deltaTime, 0f, 45f);
            UpdateBarrels();
            UpdateTrajectory();
        }

        private void RefreshCannons()
        {
            // Capture reload times across rescans. The shipyard creates/removes mount GameObjects.
            foreach (CannonMount previous in mounts)
                if (previous.root != null) nextFireTimes[previous.root] = previous.nextFireTime;
            mounts.Clear();
            string cannonName = shipyard != null && shipyard.Cannon != null
                ? shipyard.Cannon.DisplayName : "Deck Cannon";
            foreach (Transform child in hull.transform)
            {
                if (!child.gameObject.activeInHierarchy || child.name != cannonName) continue;
                Transform barrel = child.Find("Barrel");
                if (barrel == null) continue;
                float yaw = Mathf.Repeat(child.localEulerAngles.y + 360f, 360f);
                int inferred = Mathf.Abs(Mathf.DeltaAngle(90f, yaw)) < 46f ? 2 :
                    Mathf.Abs(Mathf.DeltaAngle(-90f, yaw)) < 46f ? 1 :
                    Mathf.Abs(Mathf.DeltaAngle(0f, yaw)) < 46f ? 3 : 4;
                mounts.Add(new CannonMount
                {
                    root = child, barrel = barrel, mountYaw = yaw,
                    group = assignedGroups.TryGetValue(child, out int setGroup) ? setGroup : inferred,
                    nextFireTime = nextFireTimes.TryGetValue(child, out float next) ? next : 0f
                });
            }
            if (mounts.Count == 0) selectedCannonIndex = 0;
            else selectedCannonIndex = Mathf.Clamp(selectedCannonIndex, 0, mounts.Count - 1);
        }

        private Vector3 DirectionWorld()
        {
            return hull.transform.TransformDirection(
                Quaternion.Euler(-elevationDeg, bearingDeg, 0f) * Vector3.forward).normalized;
        }

        private Vector3 Muzzle(Transform cannon, Vector3 direction)
        {
            // Position slightly beyond the visual carriage, to avoid the coarse owner collider.
            return cannon.position + hull.transform.up * 0.34f + direction * 0.7f;
        }

        private bool IsWithinArc(CannonMount cannon)
        {
            return Mathf.Abs(Mathf.DeltaAngle(cannon.mountYaw, bearingDeg)) <= cannonFiringArc * 0.5f;
        }

        private void UpdateBarrels()
        {
            foreach (CannonMount cannon in mounts)
            {
                if (cannon.barrel == null) continue;
                float localYaw = Mathf.DeltaAngle(cannon.mountYaw, bearingDeg);
                if (IsWithinArc(cannon))
                    cannon.barrel.localRotation = Quaternion.Euler(90f - elevationDeg, localYaw, 0f);
            }
        }

        private void UpdateTrajectory()
        {
            if (trajectoryLine == null) return;
            CannonMount example = null;
            foreach (CannonMount cannon in mounts)
                if (cannon.group == group && IsWithinArc(cannon)) { example = cannon; break; }
            if (example == null)
            {
                trajectoryLine.positionCount = 0;
                return;
            }
            Vector3 direction = DirectionWorld();
            Vector3 start = Muzzle(example.root, direction);
            const int steps = 22;
            trajectoryLine.positionCount = steps;
            for (int i = 0; i < steps; ++i)
            {
                float t = i * 0.085f;
                Vector3 position = start + muzzleSpeed * direction * t +
                    Vector3.down * (0.5f * gravity * t * t);
                trajectoryLine.SetPosition(i, position);
            }
        }

        public void FireSelectedGroup()
        {
            if (!CombatEnabled || hull == null || owner == null) return;
            int fired = 0;
            int assigned = 0;
            int inArc = 0;
            Vector3 direction = DirectionWorld();
            foreach (CannonMount cannon in mounts)
            {
                if (cannon.group != group) continue;
                assigned++;
                if (!IsWithinArc(cannon)) continue;
                inArc++;
                if (Time.time < cannon.nextFireTime) continue;
                cannon.nextFireTime = Time.time + reloadSeconds;
                nextFireTimes[cannon.root] = cannon.nextFireTime;
                SpawnCannonball(Muzzle(cannon.root, direction), direction);
                fired++;
            }
            if (fired > 0) status = "Fired " + fired + " cannon(s) from group " + group + ".";
            else if (assigned == 0) status = "Group " + group + " is empty. Assign a cannon.";
            else if (inArc == 0) status = "Aim is outside the cannon(s) firing arcs.";
            else status = "Reloading...";
        }

        private void SpawnCannonball(Vector3 start, Vector3 direction)
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Cannonball (prototype)";
            ball.transform.position = start;
            ball.transform.localScale = Vector3.one * 0.22f;
            if (cannonballMaterial != null)
                ball.GetComponent<MeshRenderer>().sharedMaterial = cannonballMaterial;
            Collider sphere = ball.GetComponent<Collider>();
            if (sphere != null) Destroy(sphere);
            BoatshipsProjectile projectile = ball.AddComponent<BoatshipsProjectile>();
            projectile.Launch(direction * muzzleSpeed, owner, ocean, damage, blastRadius, gravity);
        }
    }
}
