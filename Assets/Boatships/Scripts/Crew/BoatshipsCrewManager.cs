using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boatships
{
    public enum CrewDuty { Idle = 0, Gunner = 1, Carpenter = 2, Sailor = 3, Fighter = 4 }

    /// <summary>
    /// Milestone 12: physical deck crew driven by the saved M8 tavern roster.
    /// Completely additive: M8/M9 economy, M10 missions and M11 ship saves are not modified.
    /// This is light-weight local grid walking, not NavMesh navigation or individual physics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoatshipsCrewManager : MonoBehaviour
    {
        [Serializable]
        private sealed class Assignment
        {
            public string crewKey;
            public CrewDuty duty;
        }

        [Serializable]
        private sealed class AssignmentFile
        {
            public int version = 1;
            public int seed;
            public List<Assignment> assignments = new List<Assignment>();
        }

        private sealed class Agent
        {
            public string key;
            public PortEconomy.CrewMember member;
            public CrewDuty duty;
            public Transform visual;
            public Vector3 home;
            public Vector3 target;
            public float workProgress;
            public float nextRepairAt;
            public string activity;
            public bool unavailable;
            public Transform boardingTarget;
            public readonly List<Vector3> path = new List<Vector3>();
            public int nextWaypoint;
            public Vector3 previousGoal;
            public int previousBlockCount = -1;
        }

        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private VoxelHull hull;
        [SerializeField] private ShipCustomization shipyard;
        [SerializeField] private ShipFlooding flooding;
        [SerializeField, Range(1, 16)] private int maximumVisibleCrew = 8;
        [SerializeField, Range(0.2f, 3f)] private float walkingSpeed = 1.1f;
        [SerializeField, Range(0.2f, 2f)] private float crewScanSeconds = 0.6f;

        private readonly List<Agent> agents = new List<Agent>();
        private readonly Dictionary<string, CrewDuty> dutyByKey = new Dictionary<string, CrewDuty>();
        private readonly List<Transform> nearbyCannons = new List<Transform>();
        private readonly Dictionary<CrewDuty, Material> roleMaterials = new Dictionary<CrewDuty, Material>();
        private int activeSeed = int.MinValue;
        private int selectedIndex;
        private float nextScan;
        private bool ready;
        private bool assignmentSaveLocked;
        private Vector3 cachedRepairStation;
        private string status = "Waiting for tavern roster...";

        public int ActiveCrewCount => agents.Count;
        public void SetBoardingTarget(string key, Transform destination)
        {
            foreach (Agent agent in agents)
                if (agent.key == key)
                {
                    agent.boardingTarget = destination;
                    if (destination == null && agent.visual != null)
                    {
                        agent.visual.localPosition = agent.home;
                        agent.previousBlockCount = -1;
                    }
                }
        }
        public event Action<string> CrewCompletedRepair;
        public string SelectedCrewKey => agents.Count == 0 ? null : agents[Mathf.Clamp(selectedIndex, 0, agents.Count - 1)].key;

        public CrewDuty GetCrewDuty(string crewKey)
        {
            foreach (Agent a in agents) if (a.key == crewKey) return a.duty;
            return dutyByKey.TryGetValue(crewKey, out CrewDuty duty) ? duty : CrewDuty.Idle;
        }

        public bool IsCrewActive(string crewKey)
        {
            foreach (Agent a in agents) if (a.key == crewKey) return !a.unavailable && a.visual != null;
            return false;
        }

        public Vector3 CrewWorldPosition(string crewKey)
        {
            foreach (Agent a in agents) if (a.key == crewKey && a.visual != null) return a.visual.position;
            return hull != null ? hull.transform.position : transform.position;
        }

        public void SetCrewUnavailable(string crewKey, bool unavailable)
        {
            foreach (Agent a in agents)
            {
                if (a.key != crewKey) continue;
                a.unavailable = unavailable;
                if (a.visual != null) a.visual.gameObject.SetActive(!unavailable);
            }
        }
        public int TotalCrewCount => economy != null && economy.Crew != null ? economy.Crew.Count : 0;
        public int ActiveSeed => activeSeed;
        public bool CanCheckpoint => ready && !assignmentSaveLocked;
        public bool LastSaveSucceeded { get; private set; }
        public string Status => status;
        public int SelectedIndex => selectedIndex;
        public string AssignmentSavePath => activeSeed == int.MinValue ? "Not initialized" : SavePath(activeSeed);
        public string SelectedCrewText
        {
            get
            {
                if (agents.Count == 0) return "No active crew. Recruit at a tavern.";
                Agent a = agents[Mathf.Clamp(selectedIndex, 0, agents.Count - 1)];
                return a.member.displayName + " | " + a.duty + " | skill " + a.member.skill + "\n" + a.activity;
            }
        }

        public void Configure(ArchipelagoWorld sceneWorld, PortEconomy portEconomy, VoxelHull shipHull,
            ShipCustomization customization, ShipFlooding shipFlooding)
        {
            world = sceneWorld;
            economy = portEconomy;
            hull = shipHull;
            shipyard = customization;
            flooding = shipFlooding;
        }

        private IEnumerator Start()
        {
            // M8 PortDocking.Start loads the correct seed; M11 loads the ship after two frames.
            yield return null;
            yield return null;
            yield return null;
            if (world == null || economy == null || hull == null || hull.Definition == null)
            {
                status = "Crew setup missing world, PortEconomy or initialized VoxelHull.";
                Debug.LogError("Boatships M12: " + status, this);
                yield break;
            }
            ready = true;
            ChangeSeed(world.Seed);
            ScanRoster();
            Debug.Log("Boatships M12 crew enabled for world seed " + activeSeed + ".", this);
        }

        private void Update()
        {
            if (!ready) return;
            if (world != null && world.Seed != activeSeed)
            {
                SaveAssignments();
                ChangeSeed(world.Seed);
            }
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + crewScanSeconds;
                ScanRoster();
                ScanCannons();
                if (flooding != null && flooding.SubmergedUnpatchedBreaches > 0)
                    cachedRepairStation = RepairStation();
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f6Key.wasPressedThisFrame) SelectNextCrew();
                if (keyboard.f7Key.wasPressedThisFrame) CycleSelectedDuty();
            }
            for (int i = 0; i < agents.Count; ++i)
                StepAgent(agents[i], i, Time.deltaTime);
        }

        public void SelectNextCrew()
        {
            if (agents.Count == 0) return;
            selectedIndex = (selectedIndex + 1) % agents.Count;
            status = "Selected " + agents[selectedIndex].member.displayName + ".";
        }

        public void CycleSelectedDuty()
        {
            if (agents.Count == 0) return;
            Agent agent = agents[Mathf.Clamp(selectedIndex, 0, agents.Count - 1)];
            agent.duty = (CrewDuty)(((int)agent.duty + 1) % 5);
            agent.workProgress = 0f;
            dutyByKey[agent.key] = agent.duty;
            SetRoleColor(agent);
            status = agent.member.displayName + " assigned to " + agent.duty + ".";
            SaveAssignments();
        }

        private void ChangeSeed(int newSeed)
        {
            activeSeed = newSeed;
            assignmentSaveLocked = false;
            dutyByKey.Clear();
            ClearAgents();
            LoadAssignments();
            selectedIndex = 0;
            nextScan = 0f;
            status = "Crew assignments loaded for seed " + newSeed + ".";
        }

        private static string CrewKey(PortEconomy.CrewMember member, int index)
        {
            // M8 offerKey is deterministic, persisted and unique in a world.
            if (!string.IsNullOrEmpty(member.offerKey)) return member.offerKey;
            return "legacy:" + index + ":" + member.displayName + ":" + member.role;
        }

        private static CrewDuty DefaultDuty(string role)
        {
            if (string.Equals(role, "Gunner", StringComparison.OrdinalIgnoreCase)) return CrewDuty.Gunner;
            if (string.Equals(role, "Carpenter", StringComparison.OrdinalIgnoreCase)) return CrewDuty.Carpenter;
            if (string.Equals(role, "Sailor", StringComparison.OrdinalIgnoreCase)) return CrewDuty.Sailor;
            if (string.Equals(role, "Fighter", StringComparison.OrdinalIgnoreCase)) return CrewDuty.Fighter;
            return CrewDuty.Idle;
        }

        private void ScanRoster()
        {
            if (economy == null || economy.Crew == null || economy.ActiveSeed != activeSeed) return;
            var desired = new HashSet<string>();
            int limit = Mathf.Min(maximumVisibleCrew, economy.Crew.Count);
            for (int i = 0; i < limit; ++i)
            {
                PortEconomy.CrewMember recruit = economy.Crew[i];
                if (recruit == null) continue;
                string key = CrewKey(recruit, i);
                if (!desired.Add(key)) continue;
                Agent existing = null;
                foreach (Agent a in agents) if (a.key == key) { existing = a; break; }
                if (existing != null)
                {
                    existing.member = recruit;
                    continue;
                }
                var agent = new Agent
                {
                    key = key,
                    member = recruit,
                    duty = dutyByKey.TryGetValue(key, out CrewDuty savedDuty) ? savedDuty : DefaultDuty(recruit.role),
                    activity = "Reporting for duty"
                };
                Vector3 preferred = new Vector3((i % 3 - 1) * 0.38f, 0f, -0.65f + i / 3 * 0.35f);
                agent.home = NearestDeckPoint(preferred);
                agent.target = agent.home;
                agent.visual = CreateAvatar(agent, i);
                agents.Add(agent);
                status = recruit.displayName + " joined the deck crew.";
            }
            for (int i = agents.Count - 1; i >= 0; --i)
            {
                if (desired.Contains(agents[i].key)) continue;
                DestroyAvatar(agents[i]);
                agents.RemoveAt(i);
            }
            selectedIndex = agents.Count == 0 ? 0 : Mathf.Clamp(selectedIndex, 0, agents.Count - 1);
        }

        private void ScanCannons()
        {
            nearbyCannons.Clear();
            if (hull == null) return;
            string cannonName = shipyard != null && shipyard.Cannon != null
                ? shipyard.Cannon.DisplayName : "Deck Cannon";
            foreach (Transform child in hull.transform)
            {
                if (child.gameObject.activeInHierarchy && child.name == cannonName && child.Find("Barrel") != null)
                    nearbyCannons.Add(child);
            }
        }

        private Vector3 NearestDeckPoint(Vector3 preferred)
        {
            HullDefinition def = hull != null ? hull.Definition : null;
            if (def == null) return preferred;
            float best = float.PositiveInfinity;
            Vector3 nearest = Vector3.zero;
            int y = def.Height - 1;
            for (int x = 0; x < def.Width; ++x)
            for (int z = 0; z < def.Length; ++z)
            {
                if (!hull.HasBlock(x, y, z)) continue;
                Vector3 center = def.BlockCenter(x, y, z);
                float distance = (new Vector2(center.x - preferred.x, center.z - preferred.z)).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = center + Vector3.up * (def.BlockSize * 0.5f + 0.012f);
            }
            return float.IsPositiveInfinity(best) ? preferred : nearest;
        }

        private Vector3 GunnerStation(int index)
        {
            if (nearbyCannons.Count == 0) return NearestDeckPoint(new Vector3(0.4f, 0f, 0f));
            Transform cannon = nearbyCannons[index % nearbyCannons.Count];
            Vector3 local = hull.transform.InverseTransformPoint(cannon.position);
            // Stand beside the carriage; snap to intact deck cells.
            local.x += local.x > 0f ? -0.24f : 0.24f;
            return NearestDeckPoint(local);
        }

        private Vector3 SailorStation()
        {
            if (hull != null && hull.Definition != null)
            foreach (HullHardpoint point in hull.Definition.Hardpoints)
                if (point.type == HullHardpointType.Mast) return NearestDeckPoint(point.localPosition + Vector3.right * 0.35f);
            return NearestDeckPoint(new Vector3(0f, 0f, 0.5f));
        }

        private Vector3 RepairStation()
        {
            if (hull == null) return Vector3.zero;
            List<ShipVoxelRecord> damaged = hull.CaptureVoxelDamage();
            if (damaged != null && damaged.Count > 0)
            {
                ShipVoxelRecord first = damaged[0];
                return NearestDeckPoint(hull.Definition.BlockCenter(first.x, first.y, first.z));
            }
            return NearestDeckPoint(new Vector3(0f, 0f, -0.45f));
        }

        private void StepAgent(Agent a, int index, float dt)
        {
            if (a.visual == null || a.unavailable || hull == null || hull.Definition == null) return;
            if (a.boardingTarget != null)
            {
                VoxelHull enemyHull = a.boardingTarget.GetComponent<VoxelHull>();
                Vector3 requested = new Vector3((index % 2 - 0.5f) * 0.5f, 0f, index * 0.3f - 0.7f);
                if (enemyHull == null || !enemyHull.TryNearestDeckPoint(requested, out Vector3 station)) return;
                Vector3 destination = a.boardingTarget.TransformPoint(station);
                a.visual.position = Vector3.MoveTowards(a.visual.position, destination, walkingSpeed * dt);
                a.activity = "Boarding enemy vessel";
                return;
            }
            if (flooding != null && flooding.IsSinking)
            {
                a.activity = "Abandoning sinking vessel (visual placeholder)";
                return;
            }
            bool workingDamage = a.duty == CrewDuty.Carpenter && flooding != null &&
                !flooding.IsSinking && flooding.SubmergedUnpatchedBreaches > 0 && flooding.RemainingRepairKits > 0;
            switch (a.duty)
            {
                case CrewDuty.Gunner:
                    a.target = GunnerStation(index);
                    a.activity = nearbyCannons.Count > 0 ? "Operating cannon (visual)" : "Waiting for cannon";
                    break;
                case CrewDuty.Carpenter:
                    a.target = workingDamage ? cachedRepairStation : a.home;
                    a.activity = workingDamage ? "Moving to hull breach" : "Standing by for repairs";
                    break;
                case CrewDuty.Sailor:
                    a.target = SailorStation();
                    a.activity = "Tending sails (visual)";
                    break;
                default:
                    a.target = a.home;
                    a.activity = "On standby";
                    break;
            }
            // Recalculate a short four-neighbor path over living top-deck voxels.
            // This avoids traversing missing deck cells without a runtime NavMesh rebuild.
            a.target = NearestDeckPoint(a.target);
            if (a.previousBlockCount != hull.RemainingBlocks ||
                (a.previousGoal - a.target).sqrMagnitude > 0.02f)
            {
                RebuildPath(a, a.target);
                a.previousGoal = a.target;
                a.previousBlockCount = hull.RemainingBlocks;
            }
            bool isMoving = a.nextWaypoint < a.path.Count;
            if (isMoving)
            {
                Vector3 current = a.visual.localPosition;
                Vector3 waypoint = a.path[a.nextWaypoint];
                Vector3 move = waypoint - current;
                a.visual.localPosition = Vector3.MoveTowards(current, waypoint, walkingSpeed * dt);
                a.workProgress = 0f;
                if (move.sqrMagnitude > 0.002f)
                {
                    Vector3 horizontal = new Vector3(move.x, 0f, move.z);
                    if (horizontal.sqrMagnitude > 0.0001f)
                        a.visual.localRotation = Quaternion.Slerp(a.visual.localRotation,
                            Quaternion.LookRotation(horizontal), dt * 8f);
                }
                if ((a.visual.localPosition - waypoint).sqrMagnitude < 0.001f)
                    a.nextWaypoint++;
            }
            else if (workingDamage && (a.visual.localPosition - a.target).sqrMagnitude < 0.02f &&
                Time.time >= a.nextRepairAt)
            {
                a.workProgress += dt;
                a.activity = "Patching breach: " + Mathf.CeilToInt(Mathf.Max(0f,
                    RepairSeconds(a.member.skill) - a.workProgress)) + "s";
                if (a.workProgress >= RepairSeconds(a.member.skill))
                {
                    bool repaired = flooding.EmergencyPatch();
                    a.workProgress = 0f;
                    a.nextRepairAt = Time.time + 8f;
                    if (repaired)
                    {
                        status = a.member.displayName + " sealed breaches using one ship repair kit.";
                        a.activity = "Breach patched";
                        CrewCompletedRepair?.Invoke(a.key);
                    }
                }
            }
            else if (!workingDamage) a.workProgress = 0f;
        }

        private void RebuildPath(Agent agent, Vector3 goal)
        {
            agent.path.Clear();
            agent.nextWaypoint = 0;
            HullDefinition def = hull.Definition;
            int w = def.Width, l = def.Length;
            int top = def.Height - 1;
            int startCell = ClosestDeckCell(agent.visual.localPosition);
            int goalCell = ClosestDeckCell(goal);
            if (startCell < 0 || goalCell < 0) return;
            int total = w * l;
            int[] previous = new int[total];
            for (int i = 0; i < total; i++) previous[i] = -1;
            var queue = new Queue<int>();
            previous[startCell] = startCell;
            queue.Enqueue(startCell);
            // Four-connected cells prevent crew walking diagonally across a broken corner.
            int[] dx = { 1, -1, 0, 0 };
            int[] dz = { 0, 0, 1, -1 };
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                if (cell == goalCell) break;
                int x = cell / l;
                int z = cell % l;
                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i], nz = z + dz[i];
                    if (nx < 0 || nx >= w || nz < 0 || nz >= l || !hull.HasBlock(nx, top, nz)) continue;
                    int next = nx * l + nz;
                    if (previous[next] != -1) continue;
                    previous[next] = cell;
                    queue.Enqueue(next);
                }
            }
            if (previous[goalCell] == -1) return; // Deck was severed; crew cannot cross.
            var reversed = new List<Vector3>();
            for (int cursor = goalCell; cursor != startCell; cursor = previous[cursor])
            {
                int x = cursor / l;
                int z = cursor % l;
                reversed.Add(def.BlockCenter(x, top, z) + Vector3.up * (def.BlockSize * 0.5f + 0.012f));
            }
            reversed.Reverse();
            agent.path.AddRange(reversed);
        }

        private int ClosestDeckCell(Vector3 local)
        {
            HullDefinition def = hull.Definition;
            float best = float.PositiveInfinity;
            int result = -1;
            for (int x = 0; x < def.Width; x++)
            for (int z = 0; z < def.Length; z++)
            {
                if (!hull.HasBlock(x, def.Height - 1, z)) continue;
                Vector3 point = def.BlockCenter(x, def.Height - 1, z);
                float distance = (new Vector2(point.x - local.x, point.z - local.z)).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                result = x * def.Length + z;
            }
            return result;
        }

        private static float RepairSeconds(int skill)
        {
            return Mathf.Lerp(10f, 4f, Mathf.Clamp01(skill / 100f));
        }

        private Transform CreateAvatar(Agent a, int index)
        {
            GameObject root = new GameObject("Crew - " + a.member.displayName);
            root.transform.SetParent(hull.transform, false);
            root.transform.localPosition = a.home;
            // Same footprint as the old capsule crewman (about 0.42 hull units tall); the shirt shows the duty.
            BoatshipsHumanoid.Build(root.transform, DutyColor(a.duty), 0.42f, false, a.key != null ? a.key.GetHashCode() & 0x7fffffff : index);
            return root.transform;
        }

        private void SetRoleColor(Agent a)
        {
            if (a.visual == null) return;
            BoatshipsHumanoid.SetShirt(a.visual, DutyColor(a.duty));
        }

        private static void AddPart(Transform parent, PrimitiveType type, string name,
            Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        private static Color DutyColor(CrewDuty duty)
        {
            switch (duty)
            {
                case CrewDuty.Gunner: return new Color(0.84f, 0.28f, 0.18f);
                case CrewDuty.Carpenter: return new Color(0.90f, 0.72f, 0.28f);
                case CrewDuty.Sailor: return new Color(0.28f, 0.67f, 0.88f);
                default: return new Color(0.80f, 0.83f, 0.75f);
            }
        }

        private static void DestroyAvatar(Agent a)
        {
            if (a != null && a.visual != null) Destroy(a.visual.gameObject);
        }

        private void ClearAgents()
        {
            foreach (Agent a in agents) DestroyAvatar(a);
            agents.Clear();
        }

        private static string SavePath(int seed)
        {
            return Path.Combine(Application.persistentDataPath, "boatships_crew_v1_" + seed + ".json");
        }

        private void LoadAssignments()
        {
            string path = SavePath(activeSeed);
            if (!BoatshipsSaveStore.Exists(path)) return;
            try
            {
                AssignmentFile data = JsonUtility.FromJson<AssignmentFile>(BoatshipsSaveStore.Read(path));
                if (data == null || data.version != 1 || data.seed != activeSeed || data.assignments == null)
                    throw new InvalidDataException("Crew save version/seed mismatch.");
                foreach (Assignment assignment in data.assignments)
                {
                    if (assignment == null || string.IsNullOrEmpty(assignment.crewKey)) continue;
                    if ((int)assignment.duty < 0 || (int)assignment.duty > 4) continue;
                    dutyByKey[assignment.crewKey] = assignment.duty;
                }
            }
            catch (Exception ex)
            {
                assignmentSaveLocked = true;
                status = "Crew assignments could not be loaded (file protected): " + ex.Message;
                Debug.LogWarning("Boatships M12: " + status + " (existing save untouched)", this);
            }
        }

        public void SaveAssignments()
        {
            LastSaveSucceeded = false;
            if (!ready || activeSeed == int.MinValue) return;
            if (assignmentSaveLocked)
            {
                Debug.LogWarning("Boatships M12: refusing to overwrite unreadable crew assignments for seed " + activeSeed, this);
                return;
            }
            try
            {
                var data = new AssignmentFile { seed = activeSeed };
                foreach (KeyValuePair<string, CrewDuty> entry in dutyByKey)
                    data.assignments.Add(new Assignment { crewKey = entry.Key, duty = entry.Value });
                string path = SavePath(activeSeed);
                BoatshipsSaveStore.Write(path, JsonUtility.ToJson(data));
                LastSaveSucceeded = true;
            }
            catch (Exception ex)
            {
                status = "Crew assignment save failed: " + ex.Message;
                Debug.LogWarning("Boatships M12: " + status, this);
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause) SaveAssignments();
        }

        private void OnApplicationQuit() => SaveAssignments();

        private void OnDestroy()
        {
            ClearAgents();
            foreach (Material material in roleMaterials.Values)
                if (material != null) Destroy(material);
            roleMaterials.Clear();
        }
    }
}
