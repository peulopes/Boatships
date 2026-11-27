using System;
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
            public ShipGunStation station;
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

        // Optional M13 crew performance input; 1.0 preserves original cannon timing.
        public float CrewReloadMultiplier { get; set; } = 1f;
        public float UpgradeDamageMultiplier { get; set; } = 1f;
        public event Action<int> GroupFired;

        // ── Crew gunnery (campaign): the captain steers, the gun crews aim and fire on their own. ──
        private const float GunnerReload = 3.0f, DeckhandReload = 4.6f;
        private const float GunnerSpread = 2.5f, DeckhandSpread = 5.5f;
        private const float PlayerReload = 2.6f;
        private readonly List<ShipCombatTarget> hostiles = new List<ShipCombatTarget>();
        private readonly Dictionary<ShipCombatTarget, (Vector3 position, Vector3 velocity)> tracks =
            new Dictionary<ShipCombatTarget, (Vector3, Vector3)>();
        private BoatshipsCrewManager crewManager;
        private float hostileScanAt;

        /// <summary>When on, the keyboard no longer fires: each gun's crew picks a target in its arc and fires
        /// when loaded. The captain can still take command of any single gun in first person.</summary>
        public bool CrewGunnery { get; set; }
        public float MuzzleSpeed => muzzleSpeed;
        public float ShotGravity => gravity;
        public string CrewStatus { get; private set; } = "";

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

        /// <summary>Save manual weapon assignments by stable equipment instance key.</summary>
        public List<ShipWeaponGroupRecord> CaptureWeaponGroups()
        {
            RefreshCannons();
            var result = new List<ShipWeaponGroupRecord>();
            if (shipyard == null) return result;
            foreach (CannonMount mount in mounts)
            {
                string id = shipyard.GetPersistentId(mount.root);
                if (!string.IsNullOrEmpty(id))
                    result.Add(new ShipWeaponGroupRecord { instanceKey = id, group = mount.group });
            }
            return result;
        }

        public void RestoreWeaponGroups(List<ShipWeaponGroupRecord> saved)
        {
            assignedGroups.Clear();
            nextFireTimes.Clear();
            if (shipyard != null && saved != null)
            foreach (ShipWeaponGroupRecord item in saved)
            {
                if (item == null) continue;
                Transform root = shipyard.FindEquipmentByPersistentId(item.instanceKey);
                if (root != null) assignedGroups[root] = Mathf.Clamp(item.group, 1, 4);
            }
            RefreshCannons();
        }

        private void Awake()
        {
            // PC build: aiming uses the arrow keys; the prototype touch stick is never shown.
            if (aimJoystick != null) aimJoystick.gameObject.SetActive(false);
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
            if (trajectoryLine != null) trajectoryLine.enabled = enabledForCombat && !CrewGunnery;
            if (!enabledForCombat) return;
            if (CrewGunnery) { CrewFire(); return; }

            // On foot (boarding fights, towns) Space jumps and Tab leaves, so the ship's guns ignore the keyboard.
            Keyboard kb = BoatshipsFirstPerson.OnFoot || BoatshipsUI.Typing ? null : Keyboard.current;
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

            Vector2 aim = Vector2.zero;
            if (kb != null)
            {
                aim.x += (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                aim.y += (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
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
                ShipGunStation station = child.GetComponent<ShipGunStation>();
                if (station == null) station = child.gameObject.AddComponent<ShipGunStation>();
                station.Bind(this, barrel);
                mounts.Add(new CannonMount
                {
                    root = child, barrel = barrel, mountYaw = yaw, station = station,
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
            return cannon.root != null && cannon.barrel != null && cannon.root.gameObject.activeInHierarchy &&
                Mathf.Abs(Mathf.DeltaAngle(cannon.mountYaw, bearingDeg)) <= cannonFiringArc * 0.5f;
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
                cannon.nextFireTime = Time.time + reloadSeconds * Mathf.Clamp(CrewReloadMultiplier, 0.5f, 1.5f);
                nextFireTimes[cannon.root] = cannon.nextFireTime;
                SpawnCannonball(Muzzle(cannon.root, direction), direction);
                fired++;
            }
            if (fired > 0) GroupFired?.Invoke(fired);
            if (fired > 0) status = "Fired " + fired + " cannon(s) from group " + group + ".";
            else if (assigned == 0) status = "Group " + group + " is empty. Assign a cannon.";
            else if (inArc == 0) status = "Aim is outside the cannon(s) firing arcs.";
            else status = "Reloading...";
        }

        // ───────────────────────────── Crew gunnery ─────────────────────────────

        public bool IsLoaded(Transform root)
        {
            foreach (CannonMount mount in mounts)
                if (mount.root == root) return Time.time >= mount.nextFireTime;
            return false;
        }

        public Vector3 MuzzlePoint(Transform root, Vector3 direction) => Muzzle(root, direction);

        /// <summary>The captain fires a gun they have taken command of (the crew has loaded it).</summary>
        public bool FireStation(ShipGunStation station)
        {
            if (!CombatEnabled || station == null || owner == null) return false;
            foreach (CannonMount mount in mounts)
            {
                if (mount.station != station) continue;
                if (Time.time < mount.nextFireTime || !mount.root.gameObject.activeInHierarchy) return false;
                Vector3 direction = station.AimDirection;
                mount.nextFireTime = Time.time + PlayerReload * Mathf.Clamp(CrewReloadMultiplier, 0.5f, 1.5f);
                nextFireTimes[mount.root] = mount.nextFireTime;
                SpawnCannonball(Muzzle(mount.root, direction), direction);
                GroupFired?.Invoke(1);
                status = "You fired the gun. The crew is reloading it.";
                return true;
            }
            return false;
        }

        private void CrewFire()
        {
            if (hull == null || owner == null) return;
            if (crewManager == null) crewManager = GetComponent<BoatshipsCrewManager>();
            if (Time.time >= hostileScanAt) { hostileScanAt = Time.time + 0.5f; ScanHostiles(); }
            TrackHostiles();
            int gunners = crewManager != null ? crewManager.CountOnDuty(CrewDuty.Gunner) : 0;
            int fired = 0, loaded = 0, engaged = 0, index = 0;
            float nearest = float.PositiveInfinity;
            foreach (CannonMount mount in mounts)
            {
                bool hasGunner = index++ < gunners;
                if (mount.root == null || !mount.root.gameObject.activeInHierarchy) continue;
                if (Time.time >= mount.nextFireTime) loaded++;
                if (mount.station != null && mount.station.Manned) continue; // the captain has this gun
                if (!SolveCrewShot(mount, out Vector3 direction, out float distance))
                {
                    // Nothing in this gun's arc: run the barrel back to its rest position.
                    if (mount.barrel != null)
                        mount.barrel.localRotation = Quaternion.Slerp(mount.barrel.localRotation,
                            Quaternion.Euler(80f, 0f, 0f), 1f - Mathf.Exp(-3f * Time.deltaTime));
                    continue;
                }
                engaged++;
                nearest = Mathf.Min(nearest, distance);
                Vector3 local = hull.transform.InverseTransformDirection(direction);
                float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                float elevation = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
                Quaternion laid = Quaternion.Euler(90f - elevation, Mathf.DeltaAngle(mount.mountYaw, bearing), 0f);
                if (mount.barrel != null)
                    mount.barrel.localRotation = Quaternion.Slerp(mount.barrel.localRotation, laid, 1f - Mathf.Exp(-6f * Time.deltaTime));
                if (Time.time < mount.nextFireTime) continue;
                // Gunners lay the gun more truly than deckhands pressed into service.
                float spread = hasGunner ? GunnerSpread : DeckhandSpread;
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
                direction = Quaternion.AngleAxis(UnityEngine.Random.Range(-spread, spread), Vector3.up) *
                    Quaternion.AngleAxis(UnityEngine.Random.Range(-spread, spread) * 0.5f, side) * direction;
                mount.nextFireTime = Time.time + (hasGunner ? GunnerReload : DeckhandReload) *
                    Mathf.Clamp(CrewReloadMultiplier, 0.5f, 1.5f) * UnityEngine.Random.Range(0.9f, 1.15f);
                nextFireTimes[mount.root] = mount.nextFireTime;
                SpawnCannonball(Muzzle(mount.root, direction), direction);
                fired++;
            }
            if (fired > 0) GroupFired?.Invoke(fired);
            int guns = mounts.Count;
            CrewStatus = guns == 0 ? "No cannons mounted — fit them at a shipyard." :
                guns + " gun(s), " + Mathf.Min(gunners, guns) + " with a Gunner (the rest worked by deckhands)  ·  loaded " +
                loaded + "/" + guns + "\n" + (engaged > 0 ? "Guns engaging the enemy at " + Mathf.RoundToInt(nearest) + " m" :
                hostiles.Count > 0 ? "Bring the enemy abeam and within " + Mathf.RoundToInt(MaxCrewRange) + " m — the crews fire on their own" :
                "No enemy in sight");
        }

        private float MaxCrewRange => muzzleSpeed * muzzleSpeed / gravity * 0.92f;

        /// <summary>Ships whose captain is hunting us, still afloat. Ships we are boarding have their AI off and are spared.</summary>
        private void ScanHostiles()
        {
            hostiles.Clear();
            foreach (EnemyShipAI ai in FindObjectsByType<EnemyShipAI>(FindObjectsSortMode.None))
            {
                // Hunting us, or hunting a ship of our company.
                if (ai == null || !ai.isActiveAndEnabled || (ai.Target != owner && !BoatshipsAllegiance.Allied(ai.Target, owner))) continue;
                ShipCombatTarget ship = ai.GetComponent<ShipCombatTarget>();
                if (ship == null || ship == owner || BoatshipsAllegiance.Allied(ship, owner) ||
                    ship.RemainingVoxels <= 0 || ship.Hull == null) continue;
                ShipFlooding flood = ship.GetComponent<ShipFlooding>();
                if (flood != null && flood.IsSinking) continue;
                hostiles.Add(ship);
            }
        }

        private void TrackHostiles()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            foreach (ShipCombatTarget ship in hostiles)
            {
                if (ship == null) continue;
                Vector3 now = ship.transform.position;
                if (tracks.TryGetValue(ship, out var track))
                {
                    Vector3 measured = (now - track.position) / dt;
                    measured.y = 0f;
                    tracks[ship] = (now, Vector3.Lerp(track.velocity, Vector3.ClampMagnitude(measured, 14f), 1f - Mathf.Exp(-4f * dt)));
                }
                else tracks[ship] = (now, Vector3.zero);
            }
        }

        /// <summary>Picks the nearest enemy inside this gun's arc and range and solves a low-arc shot that leads it.</summary>
        private bool SolveCrewShot(CannonMount mount, out Vector3 direction, out float bestDistance)
        {
            direction = Vector3.zero;
            bestDistance = float.PositiveInfinity;
            Vector3 mountForward = hull.transform.rotation * (Quaternion.Euler(0f, mount.mountYaw, 0f) * Vector3.forward);
            Vector3 origin = mount.root.position + hull.transform.up * 0.34f;
            foreach (ShipCombatTarget ship in hostiles)
            {
                if (ship == null || ship.Hull == null) continue;
                HullDefinition definition = ship.Hull.Definition;
                // Aim a little low on the hull side so hits open leaks.
                Vector3 aim = ship.Hull.transform.position - Vector3.up * (definition.BlockSize * 0.5f);
                Vector3 velocity = tracks.TryGetValue(ship, out var track) ? track.velocity : Vector3.zero;
                float flight = Vector3.Distance(origin, aim) / muzzleSpeed;
                Vector3 shot = Vector3.zero;
                bool solved = false;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 delta = aim + velocity * flight - origin;
                    Vector3 flat = new Vector3(delta.x, 0f, delta.z);
                    float range = flat.magnitude;
                    if (range < 1f || range > MaxCrewRange) { solved = false; break; }
                    float v2 = muzzleSpeed * muzzleSpeed;
                    float root = v2 * v2 - gravity * (gravity * range * range + 2f * delta.y * v2);
                    if (root < 0f) { solved = false; break; }
                    float angle = Mathf.Atan((v2 - Mathf.Sqrt(root)) / (gravity * range));
                    shot = flat / range * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
                    flight = range / (muzzleSpeed * Mathf.Cos(angle));
                    solved = true;
                }
                if (!solved) continue;
                Vector3 flatShot = new Vector3(shot.x, 0f, shot.z);
                if (Vector3.Angle(Vector3.ProjectOnPlane(mountForward, Vector3.up), flatShot) > cannonFiringArc * 0.5f) continue;
                float distance = Vector3.Distance(origin, aim);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                direction = shot.normalized;
            }
            return bestDistance < float.PositiveInfinity;
        }

        private void SpawnCannonball(Vector3 start, Vector3 direction)
        {
            BoatshipsProjectile projectile = BoatshipsProjectile.Spawn(start, cannonballMaterial);
            projectile.Launch(direction * muzzleSpeed, owner, ocean,
                damage * UpgradeDamageMultiplier, blastRadius, gravity);
        }
    }
}
