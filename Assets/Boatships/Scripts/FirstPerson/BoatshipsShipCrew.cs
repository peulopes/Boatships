using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>The full-size crew of one ship in a first-person sea battle. Sailors do everything the player
    /// can: load, aim and fire the deck guns by hand, patch holes with planks, bail water, climb across to
    /// board the enemy and fight boarders off. Also owns the ship's battle props (wheel, bucket, ladders,
    /// leak markers and patch planks) and the gun mounts that replace the ship's command-view cannons.</summary>
    public sealed class BoatshipsShipCrew : MonoBehaviour
    {
        /// <summary>Largest gap between ship centres (metres) a sailor can climb across.</summary>
        public const float BoardRange = 5.5f;
        public const float PatchSeconds = 2.2f;
        public const float BailSeconds = 1.3f;
        public const float BailAmount = 0.03f;
        private const float RespawnSeconds = 18f;

        private enum Job { Guard, Gunner, Patch, Bail, Board }

        private sealed class Task
        {
            public Job Job;
            public BoatshipsDeckCannon Cannon;
            public int Hole = int.MinValue;
            public float Progress;
            public float AimError, PitchError;
            public float AimedSince = -1f;
            public Vector3 Spot;
            public string Name;
            public CrewDuty Duty;
        }

        private struct PendingSailor { public float At; public string Name; public CrewDuty Duty; }

        public int Team { get; private set; }
        public ShipCombatTarget Ship { get; private set; }
        public DeckSpace Deck { get; private set; }
        public ShipFlooding Flooding { get; private set; }
        public Vector3 HelmLocal { get; private set; }
        public Vector3 BailLocal { get; private set; }
        public IReadOnlyList<BoatshipsDeckCannon> Cannons => cannons;
        public IReadOnlyList<BoatshipsDeckFighter> Sailors => sailors;
        /// <summary>Raised when one of this crew falls (before they are replaced from below).</summary>
        public event Action<BoatshipsDeckFighter> SailorDied;
        public float Water => Flooding != null ? Flooding.FloodFraction : 0f;
        public int PlanksLeft => planksLeft != null ? planksLeft() : 0;
        public bool Afloat => Ship != null && Ship.RemainingVoxels > 0 && (Flooding == null || !Flooding.IsSinking) && Deck != null && Deck.Valid;
        /// <summary>The ships this crew fights. Set by the battle.</summary>
        public readonly List<BoatshipsShipCrew> Opponents = new List<BoatshipsShipCrew>();
        /// <summary>When false, this crew's fighters stay aboard (e.g. until the enemy is softened up).</summary>
        public bool BoardingAllowed { get; set; } = true;

        private readonly List<BoatshipsDeckCannon> cannons = new List<BoatshipsDeckCannon>();
        private readonly List<BoatshipsDeckFighter> sailors = new List<BoatshipsDeckFighter>();
        private readonly Dictionary<BoatshipsDeckFighter, Task> tasks = new Dictionary<BoatshipsDeckFighter, Task>();
        private readonly List<ShipFlooding.Breach> breaches = new List<ShipFlooding.Breach>();
        private readonly Dictionary<int, GameObject> leaks = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> planks = new Dictionary<int, GameObject>();
        private readonly HashSet<int> seenBreaches = new HashSet<int>();
        private readonly List<GameObject> props = new List<GameObject>();
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<PendingSailor> respawns = new List<PendingSailor>();
        private readonly List<int> scratch = new List<int>();
        private readonly List<BoatshipsDeckFighter> lost = new List<BoatshipsDeckFighter>();
        private Func<bool> takePlank;
        private Func<int> planksLeft;
        private OceanWaves ocean;
        private float sailorHealth, sailorDamage;
        private float nextThink;
        private System.Random random;
        private bool manualBailingWas;

        /// <summary>Raised when a hole opens below the waterline or is patched (the player's HUD lists them).</summary>
        public event Action BreachesChanged;

        /// <summary>Sets up a battle crew on a ship: deck guns at its gun mounts, wheel, bucket and ladders.</summary>
        public static BoatshipsShipCrew Attach(ShipCombatTarget ship, int team, OceanWaves ocean, float loadSeconds,
            float gunDamage, float health, float meleeDamage, Func<bool> takePlank, Func<int> planksLeft)
        {
            if (ship == null || ship.Hull == null) return null;
            DeckSpace deck = DeckSpace.Create(ship.Hull);
            if (deck == null) return null;
            BoatshipsShipCrew crew = ship.gameObject.AddComponent<BoatshipsShipCrew>();
            crew.Team = team; crew.Ship = ship; crew.Deck = deck; crew.ocean = ocean;
            crew.takePlank = takePlank; crew.planksLeft = planksLeft;
            crew.sailorHealth = health; crew.sailorDamage = meleeDamage;
            crew.random = new System.Random(Environment.TickCount ^ ship.GetEntityId().GetHashCode());
            crew.Flooding = ship.GetComponent<ShipFlooding>();
            if (crew.Flooding != null)
            {
                crew.manualBailingWas = crew.Flooding.ManualBailing;
                crew.Flooding.ManualBailing = true;
            }
            crew.BuildCannons(loadSeconds, gunDamage);
            crew.BuildProps();
            return crew;
        }

        public BoatshipsDeckFighter AddSailor(string name, CrewDuty duty)
        {
            if (!Afloat) return null;
            BoatshipsDeckFighter sailor = BoatshipsDeckFighter.Spawn(string.IsNullOrEmpty(name) ? "Sailor" : name, Team, Deck,
                Deck.RandomPoint(random), sailorHealth, sailorDamage);
            if (sailor == null) return null;
            sailor.Crew = this;
            sailor.Duty = duty;
            sailor.Died += OnSailorDied;
            sailors.Add(sailor);
            tasks[sailor] = new Task { Job = Job.Guard, Spot = Deck.RandomPoint(random), Name = sailor.name, Duty = duty };
            return sailor;
        }

        public int LivingAboard()
        {
            int count = 0;
            foreach (BoatshipsDeckFighter fighter in BoatshipsDeckFighter.All)
                if (fighter != null && fighter.Alive && fighter.Team == Team && fighter.Deck != null && fighter.Deck.Hull == Deck.Hull) count++;
            return count;
        }

        /// <summary>Living members of another team standing on this ship.</summary>
        public int HostilesAboard()
        {
            int count = 0;
            foreach (BoatshipsDeckFighter fighter in BoatshipsDeckFighter.All)
                if (fighter != null && fighter.Alive && fighter.Team != Team && fighter.Deck != null && fighter.Deck.Hull == Deck.Hull) count++;
            return count;
        }

        public void GetBreaches(List<ShipFlooding.Breach> result)
        {
            result.Clear();
            result.AddRange(breaches);
        }

        /// <summary>Deck point above a hole, at the rail where it can be reached.</summary>
        public Vector3 HoleDeckPoint(Vector3 hullLocal) =>
            Deck.Nearest(new Vector3(Mathf.Clamp(hullLocal.x, -Deck.HalfWidth * 0.85f, Deck.HalfWidth * 0.85f), Deck.Top, hullLocal.z));

        /// <summary>Nails a plank over a hole. Costs one plank.</summary>
        public bool TryPatch(int key)
        {
            if (Flooding == null || takePlank == null) return false;
            bool open = false;
            foreach (ShipFlooding.Breach breach in breaches) if (breach.Key == key && !breach.Patched) { open = true; break; }
            if (!open || !takePlank()) return false;
            if (!Flooding.PatchBreach(key)) return false;
            Refresh();
            return true;
        }

        public bool IsOpenHole(int key)
        {
            foreach (ShipFlooding.Breach breach in breaches) if (breach.Key == key) return !breach.Patched;
            return false;
        }

        public float Bail(float amount) => Flooding != null ? Flooding.Bail(amount) : 0f;

        /// <summary>Nearest opponent still afloat, and the distance between ship centres.</summary>
        public BoatshipsShipCrew NearestOpponent(out float distance)
        {
            distance = float.PositiveInfinity;
            BoatshipsShipCrew best = null;
            foreach (BoatshipsShipCrew other in Opponents)
            {
                if (other == null || !other.Afloat) continue;
                float d = Vector3.Distance(Flat(other.Ship.transform.position), Flat(Ship.transform.position));
                if (d >= distance) continue;
                distance = d; best = other;
            }
            return best;
        }

        /// <summary>Where to climb across to reach another ship: the rail facing it.</summary>
        public Vector3 RailToward(Vector3 world)
        {
            Vector3 local = Deck.ToLocal(world);
            return Deck.Rail(local.x >= 0f ? 1 : -1, Mathf.Clamp(local.z / Mathf.Max(0.01f, Deck.HalfLength), -0.7f, 0.7f));
        }

        public Vector3 RailWorldToward(Vector3 world) => Deck.ToWorld(RailToward(world));

        /// <summary>Called by the gun when the player takes it over: the sailor on it finds other work.</summary>
        public void Release(BoatshipsDeckCannon cannon)
        {
            if (cannon == null || cannon.Operator == null) return;
            if (tasks.TryGetValue(cannon.Operator, out Task task) && task.Cannon == cannon) { task.Job = Job.Guard; task.Cannon = null; }
            cannon.Operator = null;
        }

        // ───────────────────────────── Sailor brain ─────────────────────────────

        /// <summary>Whether a sailor drops its work to fight the nearest foe on its deck.</summary>
        public bool ShouldFight(BoatshipsDeckFighter sailor, float distance)
        {
            if (sailor.Deck == null || sailor.Deck.Hull != Deck.Hull) return true; // boarding: fight
            if (!tasks.TryGetValue(sailor, out Task task)) return true;
            if (task.Job == Job.Guard || task.Job == Job.Board || sailor.Duty == CrewDuty.Fighter) return true;
            return distance < 1.4f;
        }

        public void Work(BoatshipsDeckFighter sailor, float dt)
        {
            if (!tasks.TryGetValue(sailor, out Task task)) return;
            if (sailor.Deck == null || !sailor.Deck.Valid) return;
            if (sailor.Deck.Hull != Deck.Hull) { WorkAbroad(sailor, dt); return; }
            switch (task.Job)
            {
                case Job.Gunner: WorkGun(sailor, task, dt); break;
                case Job.Patch: WorkPatch(sailor, task, dt); break;
                case Job.Bail:
                    if (!sailor.WalkTo(BailLocal, dt)) return;
                    sailor.Toil();
                    task.Progress += dt / BailSeconds;
                    if (task.Progress >= 1f) { task.Progress = 0f; Bail(BailAmount); }
                    break;
                case Job.Board:
                    BoatshipsShipCrew target = NearestOpponent(out float distance);
                    if (target == null) { task.Job = Job.Guard; return; }
                    if (!sailor.WalkTo(RailToward(target.Ship.transform.position), dt)) return;
                    if (distance > BoardRange) return; // waiting at the rail for the ships to close
                    sailor.CrossTo(target.Deck, target.RailToward(Ship.transform.position));
                    task.Job = Job.Guard;
                    break;
                default:
                    if (sailor.WalkTo(task.Spot, dt) && random.NextDouble() < dt * 0.15) task.Spot = Deck.RandomPoint(random);
                    break;
            }
        }

        /// <summary>On an enemy deck with nobody left to fight: hold it, or climb home if the ships are close.</summary>
        private void WorkAbroad(BoatshipsDeckFighter sailor, float dt)
        {
            float distance = Vector3.Distance(Flat(sailor.Deck.Frame.position), Flat(Ship.transform.position));
            if (distance > BoardRange || !Afloat) return;
            bool captured = true;
            foreach (BoatshipsShipCrew other in Opponents)
                if (other != null && other.Deck.Hull == sailor.Deck.Hull) { captured = false; break; }
            if (!captured) return; // keep holding the deck until the ship is taken
            sailor.CrossTo(Deck, RailToward(sailor.Deck.Frame.position));
        }

        private void WorkGun(BoatshipsDeckFighter sailor, Task task, float dt)
        {
            BoatshipsDeckCannon cannon = task.Cannon;
            if (cannon == null) { task.Job = Job.Guard; return; }
            if (!sailor.WalkTo(cannon.StandLocal, dt)) return;
            sailor.Face(cannon.Local);
            cannon.Operator = sailor;
            if (!cannon.Ready)
            {
                if (cannon.StartLoading() || cannon.Loading) sailor.Toil();
                task.AimedSince = -1f;
                return;
            }
            BoatshipsShipCrew target = NearestOpponent(out float distance);
            if (target == null || distance > 32f) return;
            // Aim at the waterline amidships, with a gunner's (shrinking) error.
            Vector3 aimPoint = target.Ship.transform.position + Vector3.up * 0.1f;
            if (!cannon.SolveAim(aimPoint, out float yaw, out float pitch)) { task.AimedSince = -1f; return; }
            yaw += task.AimError; pitch += task.PitchError;
            cannon.AimYaw = Mathf.MoveTowards(cannon.AimYaw, yaw, 70f * dt);
            cannon.AimPitch = Mathf.MoveTowards(cannon.AimPitch, pitch, 30f * dt);
            if (Mathf.Abs(cannon.AimYaw - yaw) > 1.5f || Mathf.Abs(cannon.AimPitch - pitch) > 1f) { task.AimedSince = -1f; return; }
            if (task.AimedSince < 0f) task.AimedSince = Time.time;
            if (Time.time - task.AimedSince < 0.5f) return;
            if (cannon.Fire())
            {
                task.AimedSince = -1f;
                // Each shot walks the next one in.
                task.AimError = task.AimError * 0.5f + RandomRange(-2.5f, 2.5f);
                task.PitchError = task.PitchError * 0.5f + RandomRange(-1.2f, 1.2f);
            }
        }

        private void WorkPatch(BoatshipsDeckFighter sailor, Task task, float dt)
        {
            if (!TryGetBreach(task.Hole, out ShipFlooding.Breach breach) || breach.Patched) { task.Job = Job.Guard; return; }
            if (!sailor.WalkTo(HoleDeckPoint(breach.HullLocal), dt)) return;
            sailor.Toil();
            task.Progress += dt / PatchSeconds;
            if (task.Progress < 1f) return;
            task.Progress = 0f;
            TryPatch(task.Hole);
            task.Job = Job.Guard;
        }

        private void Update()
        {
            if (Ship == null || Deck == null || !Deck.Valid) return;
            for (int i = respawns.Count - 1; i >= 0; i--)
            {
                if (Time.time < respawns[i].At) continue;
                PendingSailor pending = respawns[i];
                respawns.RemoveAt(i);
                AddSailor(pending.Name, pending.Duty);
            }
            if (Time.time < nextThink) return;
            nextThink = Time.time + 0.4f;
            // Sailors lost with another ship (they were aboard it when it sank) come back later, too.
            lost.Clear();
            foreach (var entry in tasks) if (entry.Key == null) lost.Add(entry.Key);
            foreach (BoatshipsDeckFighter gone in lost)
            {
                Task task = tasks[gone];
                tasks.Remove(gone);
                respawns.Add(new PendingSailor { At = Time.time + RespawnSeconds, Name = task.Name, Duty = task.Duty });
            }
            Refresh();
            Assign();
        }

        /// <summary>Hands out the work: guns facing the enemy, holes below the waterline, water to bail, boarding.</summary>
        private void Assign()
        {
            sailors.RemoveAll(s => s == null);
            BoatshipsShipCrew enemy = NearestOpponent(out float distance);
            float water = Water;
            bool canBoard = BoardingAllowed && enemy != null && distance < BoardRange + 2.5f;
            int bailers = 0, boarders = 0;
            foreach (var entry in tasks)
            {
                if (entry.Key == null) continue;
                if (entry.Value.Job == Job.Bail) bailers++;
                if (entry.Value.Job == Job.Board) boarders++;
            }
            int maxBailers = water > 0.2f ? 3 : water > 0.03f ? 1 : 0;
            int maxBoarders = Mathf.Max(1, sailors.Count / 2);

            foreach (BoatshipsDeckFighter sailor in sailors)
            {
                if (!sailor.Alive || sailor.Jumping || sailor.Deck == null || sailor.Deck.Hull != Deck.Hull) continue;
                Task task = tasks[sailor];
                if (StillValid(task, sailor, enemy, water, canBoard)) continue;
                if (task.Job == Job.Bail) bailers--;
                if (task.Job == Job.Board) boarders--;
                if (task.Cannon != null && task.Cannon.Operator == sailor) task.Cannon.Operator = null;
                task.Cannon = null; task.Hole = int.MinValue; task.Progress = 0f; task.AimedSince = -1f;
                task.Job = Job.Guard;
                foreach (Job job in Preferences(sailor.Duty))
                {
                    if (job == Job.Patch && TryClaimHole(task)) break;
                    if (job == Job.Bail && bailers < maxBailers) { task.Job = Job.Bail; bailers++; break; }
                    if (job == Job.Gunner && enemy != null && TryClaimGun(task, enemy)) break;
                    if (job == Job.Board && canBoard && boarders < maxBoarders) { task.Job = Job.Board; boarders++; break; }
                }
                if (task.Job == Job.Guard) task.Spot = Deck.RandomPoint(random);
            }
        }

        private bool StillValid(Task task, BoatshipsDeckFighter sailor, BoatshipsShipCrew enemy, float water, bool canBoard)
        {
            switch (task.Job)
            {
                case Job.Patch: return TryGetBreach(task.Hole, out ShipFlooding.Breach b) && !b.Patched && PlanksLeft > 0;
                case Job.Bail: return water > 0.01f && !(UnclaimedHole() && sailor.Duty != CrewDuty.Sailor);
                case Job.Gunner:
                    if (task.Cannon == null || enemy == null || Mounted(task.Cannon)) return false;
                    // Heavy flooding pulls everyone but the gun crew off the guns.
                    if (water > 0.25f && sailor.Duty != CrewDuty.Gunner) return false;
                    if (UnclaimedHole() && sailor.Duty == CrewDuty.Carpenter) return false;
                    return task.Cannon.Loading || !task.Cannon.Ready || Facing(task.Cannon, enemy);
                case Job.Board: return canBoard;
                default: return false; // guards always look for work
            }
        }

        private static IEnumerable<Job> Preferences(CrewDuty duty)
        {
            switch (duty)
            {
                case CrewDuty.Carpenter: return new[] { Job.Patch, Job.Bail, Job.Gunner };
                case CrewDuty.Gunner: return new[] { Job.Gunner, Job.Patch, Job.Bail };
                case CrewDuty.Sailor: return new[] { Job.Bail, Job.Patch, Job.Gunner };
                case CrewDuty.Fighter: return new[] { Job.Board, Job.Patch, Job.Gunner, Job.Bail };
                default: return new[] { Job.Patch, Job.Gunner, Job.Bail };
            }
        }

        private bool TryClaimHole(Task task)
        {
            if (PlanksLeft <= 0) return false;
            foreach (ShipFlooding.Breach breach in breaches)
            {
                if (breach.Patched || !breach.Submerged || Claimed(breach.Key)) continue;
                task.Job = Job.Patch; task.Hole = breach.Key;
                return true;
            }
            return false;
        }

        private bool UnclaimedHole()
        {
            if (PlanksLeft <= 0) return false;
            foreach (ShipFlooding.Breach breach in breaches)
                if (!breach.Patched && breach.Submerged && !Claimed(breach.Key)) return true;
            return false;
        }

        private bool Claimed(int key)
        {
            foreach (Task other in tasks.Values) if (other.Job == Job.Patch && other.Hole == key) return true;
            return false;
        }

        private bool TryClaimGun(Task task, BoatshipsShipCrew enemy)
        {
            BoatshipsDeckCannon best = null;
            float bestScore = float.MaxValue;
            foreach (BoatshipsDeckCannon cannon in cannons)
            {
                if (cannon == null || Mounted(cannon) || !Facing(cannon, enemy)) continue;
                bool taken = false;
                foreach (Task other in tasks.Values) if (other != task && other.Cannon == cannon) { taken = true; break; }
                if (taken) continue;
                float score = Vector3.Distance(cannon.Local, Deck.Center);
                if (score >= bestScore) continue;
                bestScore = score; best = cannon;
            }
            if (best == null) return false;
            task.Job = Job.Gunner; task.Cannon = best;
            task.AimError = RandomRange(-6f, 6f); task.PitchError = RandomRange(-3f, 3f);
            return true;
        }

        private static bool Mounted(BoatshipsDeckCannon cannon) =>
            BoatshipsDeckFighter.Player != null && ReferenceEquals(BoatshipsDeckFighter.Player.Mounted, cannon);

        /// <summary>Whether the enemy lies within (or close to) a gun's training arc.</summary>
        private bool Facing(BoatshipsDeckCannon cannon, BoatshipsShipCrew enemy)
        {
            Vector3 local = Deck.Frame.InverseTransformDirection(Flat(enemy.Ship.transform.position) - Flat(cannon.WorldPosition));
            float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float mount = cannon.Side * 90f;
            if (cannon.Side == 0) mount = cannon.Local.z >= 0f ? 0f : 180f;
            return Mathf.Abs(Mathf.DeltaAngle(mount, bearing)) <= BoatshipsDeckCannon.MaxYaw + 15f;
        }

        private bool TryGetBreach(int key, out ShipFlooding.Breach breach)
        {
            foreach (ShipFlooding.Breach candidate in breaches)
                if (candidate.Key == key) { breach = candidate; return true; }
            breach = default;
            return false;
        }

        private void OnSailorDied(BoatshipsDeckFighter sailor)
        {
            SailorDied?.Invoke(sailor);
            if (tasks.TryGetValue(sailor, out Task task) && task.Cannon != null && task.Cannon.Operator == sailor) task.Cannon.Operator = null;
            tasks.Remove(sailor);
            sailors.Remove(sailor);
            // Like the player, sailors come back (from below deck) after a while, as long as the ship floats.
            respawns.Add(new PendingSailor { At = Time.time + RespawnSeconds, Name = sailor.name, Duty = sailor.Duty });
        }

        // ───────────────────────────── Holes, props ─────────────────────────────

        private void Refresh()
        {
            if (Flooding == null) return;
            Flooding.GetBreaches(breaches);
            bool changed = false;
            scratch.Clear();
            foreach (ShipFlooding.Breach breach in breaches)
            {
                scratch.Add(breach.Key);
                if (breach.Patched)
                {
                    if (leaks.TryGetValue(breach.Key, out GameObject leak)) { Destroy(leak); leaks.Remove(breach.Key); changed = true; }
                    if (!planks.ContainsKey(breach.Key)) planks[breach.Key] = CreatePlank(breach.HullLocal);
                }
                else if (breach.Submerged && !leaks.ContainsKey(breach.Key))
                {
                    leaks[breach.Key] = CreateLeak(HoleDeckPoint(breach.HullLocal));
                    changed = true;
                }
                if (seenBreaches.Add(breach.Key)) changed = true;
            }
            // Holes that closed (hull repaired at port, ship reset) lose their props.
            foreach (int key in new List<int>(leaks.Keys)) if (!scratch.Contains(key)) { Destroy(leaks[key]); leaks.Remove(key); changed = true; }
            foreach (int key in new List<int>(planks.Keys)) if (!scratch.Contains(key)) { Destroy(planks[key]); planks.Remove(key); }
            seenBreaches.IntersectWith(scratch);
            float bob = Mathf.Sin(Time.time * 9f) * 0.02f;
            foreach (GameObject leak in leaks.Values)
                if (leak != null) leak.transform.localScale = new Vector3(0.08f, 0.16f + bob, 0.08f) / Deck.Scale;
            if (changed) BreachesChanged?.Invoke();
        }

        private GameObject CreateLeak(Vector3 deckPoint)
        {
            // A spout of sea water at the rail above the hole: easy to spot from anywhere on deck.
            GameObject leak = BoatshipsHumanoid.Part(PrimitiveType.Cylinder, "Leak", Deck.Frame, deckPoint + Vector3.up * (0.12f / Deck.Scale),
                new Vector3(0.08f, 0.16f, 0.08f) / Deck.Scale, new Color(0.55f, 0.8f, 0.95f));
            return leak;
        }

        private GameObject CreatePlank(Vector3 hullLocal)
        {
            float size = Ship.Hull.Definition.BlockSize;
            Vector3 outward = Mathf.Abs(hullLocal.x) > 0.01f ? Vector3.right * Mathf.Sign(hullLocal.x) * size * 0.08f : Vector3.down * size * 0.08f;
            GameObject plank = BoatshipsHumanoid.Part(PrimitiveType.Cube, "Patch Plank", Deck.Frame, hullLocal + outward,
                new Vector3(size * 1.05f, size * 0.9f, size * 1.15f), new Color(0.55f, 0.4f, 0.22f));
            return plank;
        }

        private void BuildCannons(float loadSeconds, float damage)
        {
            Transform hull = Ship.Hull.transform;
            // Command-view gun mounts (the player's shipyard cannons) become hand-worked deck guns.
            foreach (Transform child in hull)
            {
                if (cannons.Count >= 8) break;
                if (!child.gameObject.activeInHierarchy || child.Find("Barrel") == null || child.GetComponent<BoatshipsDeckCannon>() != null) continue;
                HideRenderers(child);
                cannons.Add(BoatshipsDeckCannon.CreateAt(Deck, Deck.Nearest(Deck.ToLocal(child.position)),
                    child.localEulerAngles.y, Ship, ocean, loadSeconds, damage));
            }
            // AI ships: one gun at each broadside muzzle.
            EnemyShipAI ai = Ship.GetComponent<EnemyShipAI>();
            if (cannons.Count == 0 && ai != null)
            {
                // The broadside guns are models with a "Barrel" child somewhere under the ship's visuals
                // (the muzzles themselves share that root with the hull, so only the gun models are hidden).
                foreach (Transform part in Ship.GetComponentsInChildren<Transform>())
                    if (part != Ship.transform && part != hull && part.Find("Barrel") != null && part.GetComponentInChildren<VoxelHull>() == null)
                        HideRenderers(part);
                foreach (Transform muzzle in ai.Muzzles)
                {
                    if (muzzle == null || cannons.Count >= 8) continue;
                    Vector3 local = Deck.ToLocal(muzzle.position);
                    int side = local.x >= 0f ? 1 : -1;
                    Vector3 rail = Deck.Nearest(new Vector3(local.x, Deck.Top, local.z));
                    cannons.Add(BoatshipsDeckCannon.CreateAt(Deck, Deck.Constrain(rail, rail - Vector3.right * side * (0.25f / Deck.Scale)),
                        side * 90f, Ship, ocean, loadSeconds, damage));
                }
            }
            if (cannons.Count == 0)
                foreach (int side in new[] { -1, 1 })
                    cannons.Add(BoatshipsDeckCannon.Create(Deck, side, Ship, ocean, loadSeconds, damage));
        }

        private void HideRenderers(Transform root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                if (renderer.enabled) { renderer.enabled = false; hiddenRenderers.Add(renderer); }
        }

        private void BuildProps()
        {
            float s = 1f / Deck.Scale;
            HelmLocal = Deck.Nearest(new Vector3(0f, Deck.Top, -Deck.HalfLength * 0.72f));
            BailLocal = Deck.Nearest(new Vector3(0f, Deck.Top, Deck.HalfLength * 0.1f));

            Transform helm = Prop("Ship's Wheel", HelmLocal + Vector3.forward * (0.12f * s), 0f);
            BoatshipsHumanoid.Part(PrimitiveType.Cube, "Wheel Post", helm, new Vector3(0f, 0.2f, 0f), new Vector3(0.06f, 0.4f, 0.06f), new Color(0.32f, 0.2f, 0.1f));
            GameObject wheel = BoatshipsHumanoid.Part(PrimitiveType.Cylinder, "Wheel", helm, new Vector3(0f, 0.42f, -0.04f),
                new Vector3(0.3f, 0.015f, 0.3f), new Color(0.45f, 0.3f, 0.15f));
            wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                GameObject spoke = BoatshipsHumanoid.Part(PrimitiveType.Cube, "Spoke", helm, new Vector3(0f, 0.42f, -0.06f),
                    new Vector3(0.38f, 0.02f, 0.02f), new Color(0.6f, 0.45f, 0.2f));
                spoke.transform.localRotation = Quaternion.Euler(0f, 0f, i * 45f);
            }

            Transform bucket = Prop("Bailing Bucket", BailLocal + Vector3.right * (0.15f * s), 0f);
            BoatshipsHumanoid.Part(PrimitiveType.Cylinder, "Bucket", bucket, new Vector3(0f, 0.06f, 0f), new Vector3(0.12f, 0.06f, 0.12f), new Color(0.42f, 0.3f, 0.16f));
            BoatshipsHumanoid.Part(PrimitiveType.Cylinder, "Hoop", bucket, new Vector3(0f, 0.09f, 0f), new Vector3(0.125f, 0.008f, 0.125f), new Color(0.2f, 0.2f, 0.22f));

            foreach (int side in new[] { -1, 1 })
            {
                // Rope ladders over both sides, for climbing across to a ship alongside.
                Transform ladder = Prop(side > 0 ? "Starboard Ladder" : "Port Ladder", Deck.Rail(side, 0.15f), side * 90f);
                foreach (float x in new[] { -0.09f, 0.09f })
                    BoatshipsHumanoid.Part(PrimitiveType.Cube, "Ladder Rope", ladder, new Vector3(x, -0.15f, 0.04f), new Vector3(0.015f, 0.55f, 0.015f), new Color(0.7f, 0.6f, 0.42f));
                for (int i = 0; i < 5; i++)
                    BoatshipsHumanoid.Part(PrimitiveType.Cube, "Rung", ladder, new Vector3(0f, 0.08f - i * 0.11f, 0.04f), new Vector3(0.2f, 0.015f, 0.02f), new Color(0.5f, 0.35f, 0.18f));
            }
        }

        private Transform Prop(string name, Vector3 local, float yaw)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(Deck.Frame, false);
            root.transform.localScale = Vector3.one / Deck.Scale;
            root.transform.localPosition = local;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            props.Add(root);
            return root.transform;
        }

        private float RandomRange(float a, float b) => a + (float)random.NextDouble() * (b - a);
        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void OnDestroy()
        {
            foreach (BoatshipsDeckFighter sailor in sailors) if (sailor != null) Destroy(sailor.gameObject);
            foreach (BoatshipsDeckCannon cannon in cannons) if (cannon != null) Destroy(cannon.gameObject);
            foreach (GameObject prop in props) if (prop != null) Destroy(prop);
            foreach (GameObject leak in leaks.Values) if (leak != null) Destroy(leak);
            foreach (GameObject plank in planks.Values) if (plank != null) Destroy(plank);
            foreach (Renderer renderer in hiddenRenderers) if (renderer != null) renderer.enabled = true;
            if (Flooding != null) Flooding.ManualBailing = manualBailingWas;
            sailors.Clear(); cannons.Clear(); props.Clear(); leaks.Clear(); planks.Clear(); hiddenRenderers.Clear(); tasks.Clear();
        }
    }
}
