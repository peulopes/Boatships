using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boatships
{
    /// <summary>
    /// M13: persistent personal crew experience / health. The M8 tavern roster is the
    /// authoritative list of recruits; this component never rewrites economy or M11 saves.
    /// Injuries affect speed/reload modifiers. M14 optionally replaces port recovery
    /// with physical survivors whose locations share this crew save.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoatshipsCrewProgression : MonoBehaviour
    {
        [Serializable]
        private sealed class SailorState
        {
            public string key;
            public int experience;
            public float health = 100f;
            public bool dead;
            public bool overboard;
            public bool hasRescuePosition;
            public Vector3 rescuePosition;
        }

        [Serializable]
        private sealed class CrewProgressFile
        {
            public int version = 1;
            public int seed;
            public List<SailorState> sailors = new List<SailorState>();
        }

        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private BoatshipsCrewManager crew;
        [SerializeField] private WeaponGroupCombat weapons;
        [SerializeField] private ShipArcadeController movement;
        [SerializeField] private ShipCombatTarget playerTarget;
        [SerializeField] private ShipFlooding flooding;
        [SerializeField] private PortDocking docking;

        [Header("Gameplay Tuning")]
        [SerializeField, Range(0f, 1f)] private float impactInjuryChance = 0.4f;
        [SerializeField, Range(5f, 45f)] private float impactHealthLoss = 22f;
        [SerializeField, Min(5f)] private float saveInterval = 25f;
        [SerializeField, Min(4f)] private float sailingExperienceInterval = 10f;

        private readonly Dictionary<string, SailorState> states = new Dictionary<string, SailorState>();
        private int seed = int.MinValue;
        private bool ready;
        private bool saveLocked;
        private bool sinkingProcessed;
        private float nextAutosave;
        private float nextBonusTick;
        private float nextSailingXp;
        private Vector3 previousPosition;
        private string status = "Waiting for crew initialization...";
        private BoatshipsRescueSystem rescueSystem;

        public bool IsReady => ready && world != null && world.Seed == seed && economy.ActiveSeed == seed;
        public bool CanCheckpoint => IsReady && !saveLocked;
        public bool LastSaveSucceeded { get; private set; }

        public void GetBoardingParty(List<string> party)
        {
            party.Clear();
            if (!IsReady) return;
            foreach (KeyValuePair<string, SailorState> entry in states)
                if (!entry.Value.dead && !entry.Value.overboard && entry.Value.health > 0f &&
                    crew.IsCrewActive(entry.Key) && crew.GetCrewDuty(entry.Key) == CrewDuty.Fighter)
                    party.Add(entry.Key);
        }

        public float BoardingPower(string key)
        {
            if (!states.TryGetValue(key, out SailorState sailor)) return 0f;
            if (sailor.dead || sailor.overboard || sailor.health <= 0f) return 0f;
            return (8f + Mathf.Min(9, sailor.experience / 40) * 2f) * Mathf.Clamp01(sailor.health / 100f);
        }

        public void ApplyBoardingInjury(string key, float damage) => Injure(key, damage);

        public void AwardBoardingExperience(string key)
        {
            if (states.TryGetValue(key, out SailorState sailor) && !sailor.dead)
                sailor.experience = Mathf.Min(50000, sailor.experience + 25);
            SaveStates();
        }

        public void SetRescueSystem(BoatshipsRescueSystem source) => rescueSystem = source;

        public void GetSurvivors(Dictionary<string, Vector3> survivors)
        {
            survivors.Clear();
            if (!IsReady) return;
            foreach (SailorState sailor in states.Values)
                if (sailor.overboard && !sailor.dead && sailor.hasRescuePosition)
                    survivors.Add(sailor.key, sailor.rescuePosition);
        }

        public bool TryRescue(string key, float radius)
        {
            if (!IsReady || saveLocked || IsDocked() || (flooding != null && flooding.IsSinking) ||
                !states.TryGetValue(key, out SailorState sailor) || sailor.dead || !sailor.overboard ||
                !sailor.hasRescuePosition) return false;
            Vector3 offset = movement.transform.position - sailor.rescuePosition;
            offset.y = 0f;
            if (offset.sqrMagnitude > radius * radius) return false;
            float previousHealth = sailor.health;
            sailor.overboard = false;
            sailor.health = Mathf.Max(35f, sailor.health);
            if (!TrySaveStates())
            {
                sailor.overboard = true;
                sailor.health = previousHealth;
                return false;
            }
            ApplyAvailability();
            ApplyBonuses();
            status = "Survivor rescued and returned to the crew. Port treatment is available.";
            return true;
        }

        public string Status => status;
        public string SaveFilePath => seed == int.MinValue ? "Not initialized" : SavePath(seed);
        public float GunReloadMultiplier { get; private set; } = 1f;
        public float SailingSpeedMultiplier { get; private set; } = 1f;
        public string SelectedCrewStatus
        {
            get
            {
                string key = crew != null ? crew.SelectedCrewKey : null;
                if (key == null || !states.TryGetValue(key, out SailorState state))
                    return "Recruit and select a crew member with F6.";
                int level = 1 + Mathf.Min(9, Mathf.Max(0, state.experience) / 40);
                string condition = state.dead ? "DEAD" : state.overboard ? "OVERBOARD" :
                    state.health <= 0f ? "INCAPACITATED" : state.health < 70f ? "INJURED" : "HEALTHY";
                return condition + " | HP " + Mathf.CeilToInt(Mathf.Max(0f, state.health)) + "/100" +
                    " | Lv " + level + " | XP " + state.experience;
            }
        }

        public void Configure(ArchipelagoWorld sceneWorld, PortEconomy account,
            BoatshipsCrewManager deckCrew, WeaponGroupCombat weaponController,
            ShipArcadeController shipMovement, ShipCombatTarget shipTarget,
            ShipFlooding floodController, PortDocking port)
        {
            world = sceneWorld;
            economy = account;
            crew = deckCrew;
            weapons = weaponController;
            movement = shipMovement;
            playerTarget = shipTarget;
            flooding = floodController;
            docking = port;
        }

        private IEnumerator Start()
        {
            // M8 and M11 initialize world/ship; M12 creates avatars after three frames.
            for (int i = 0; i < 5; i++) yield return null;
            if (world == null || economy == null || crew == null || movement == null ||
                weapons == null || playerTarget == null)
            {
                status = "Missing world, economy, M12 crew, player movement, weapons or combat target.";
                Debug.LogError("Boatships M13: " + status, this);
                yield break;
            }
            seed = world.Seed;
            LoadStates();
            ready = true;
            EnsureRoster();
            ApplyAvailability();
            Subscribe();
            previousPosition = movement.transform.position;
            nextSailingXp = Time.time + sailingExperienceInterval;
            nextAutosave = Time.unscaledTime + saveInterval;
            status = "Crew experience and injuries active for seed " + seed + ".";
        }

        private void Subscribe()
        {
            playerTarget.HullImpactReceived += OnHullImpact;
            weapons.GroupFired += OnCannonsFired;
            crew.CrewCompletedRepair += OnRepaired;
        }

        private void Unsubscribe()
        {
            if (playerTarget != null) playerTarget.HullImpactReceived -= OnHullImpact;
            if (weapons != null) weapons.GroupFired -= OnCannonsFired;
            if (crew != null) crew.CrewCompletedRepair -= OnRepaired;
        }

        private void Update()
        {
            if (!ready) return;
            if (world.Seed != seed)
            {
                SaveStates();
                seed = world.Seed;
                sinkingProcessed = false;
                states.Clear();
                saveLocked = false;
                LoadStates();
                previousPosition = movement.transform.position;
                EnsureRoster();
                ApplyAvailability();
            }

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (BoatshipsDebugMenu.IsOpen && kb.f8Key.wasPressedThisFrame) InjureSelectedForTest();
                if (kb.f9Key.wasPressedThisFrame) TreatSelectedAtPort();
                if (BoatshipsDebugMenu.IsOpen && kb.f10Key.wasPressedThisFrame) GrantSelectedTrainingXp();
                if (kb.f11Key.wasPressedThisFrame) RecoverOverboardAtPort();
                if (BoatshipsDebugMenu.IsOpen && kb.f12Key.wasPressedThisFrame) SendSelectedOverboardForTest();
            }

            if (flooding != null && !flooding.IsSinking) sinkingProcessed = false;
            if (flooding != null && flooding.IsSinking && !sinkingProcessed)
            {
                sinkingProcessed = true;
                OnShipSank();
            }
            if (Time.time >= nextBonusTick)
            {
                nextBonusTick = Time.time + 0.5f;
                EnsureRoster();
                ApplyAvailability();
                ApplyBonuses();
            }
            if (Time.time >= nextSailingXp)
            {
                nextSailingXp = Time.time + sailingExperienceInterval;
                Vector3 now = movement.transform.position;
                if (Vector3.Distance(now, previousPosition) > 1.2f && !IsDocked())
                    GrantDutyExperience(CrewDuty.Sailor, 2);
                previousPosition = now;
            }
            if (Time.unscaledTime >= nextAutosave)
            {
                nextAutosave = Time.unscaledTime + saveInterval;
                SaveStates();
            }
        }

        private bool IsDocked() => docking != null && docking.IsDocked;

        private static string Key(PortEconomy.CrewMember person, int index)
        {
            if (!string.IsNullOrEmpty(person.offerKey)) return person.offerKey;
            return "legacy:" + index + ":" + person.displayName + ":" + person.role;
        }

        private void EnsureRoster()
        {
            if (economy.ActiveSeed != seed || economy.Crew == null) return;
            for (int i = 0; i < economy.Crew.Count; i++)
            {
                PortEconomy.CrewMember member = economy.Crew[i];
                if (member == null) continue;
                string id = Key(member, i);
                if (states.TryGetValue(id, out SailorState existing))
                {
                    // Older M13 files recorded overboard status without a sea position.
                    if (existing.overboard && !existing.hasRescuePosition)
                        SetRescuePosition(existing);
                    continue;
                }
                states.Add(id, new SailorState { key = id });
            }
        }

        private void ApplyAvailability()
        {
            foreach (KeyValuePair<string, SailorState> entry in states)
            {
                SailorState s = entry.Value;
                crew.SetCrewUnavailable(entry.Key, s.dead || s.overboard || s.health <= 0f);
            }
        }

        private void ApplyBonuses()
        {
            float bestGunnery = 0f;
            float bestSailing = 0f;
            if (economy.ActiveSeed == seed && economy.Crew != null)
            for (int i = 0; i < economy.Crew.Count; i++)
            {
                PortEconomy.CrewMember member = economy.Crew[i];
                if (member == null) continue;
                string key = Key(member, i);
                if (!states.TryGetValue(key, out SailorState s) || s.dead || s.overboard ||
                    s.health <= 0f || !crew.IsCrewActive(key)) continue;
                int level = 1 + Mathf.Min(9, s.experience / 40);
                float skill = Mathf.Clamp(member.skill + (level - 1) * 2, 0, 100) / 100f;
                // Injured sailors and gunners retain reduced effectiveness.
                if (s.health < 70f) skill *= 0.55f;
                CrewDuty duty = crew.GetCrewDuty(key);
                if (duty == CrewDuty.Gunner) bestGunnery = Mathf.Max(bestGunnery, skill);
                else if (duty == CrewDuty.Sailor) bestSailing = Mathf.Max(bestSailing, skill);
            }
            GunReloadMultiplier = 1f - bestGunnery * 0.25f;
            SailingSpeedMultiplier = 1f + bestSailing * 0.16f;
            weapons.CrewReloadMultiplier = GunReloadMultiplier;
            movement.CrewSpeedMultiplier = SailingSpeedMultiplier;
            movement.CrewTurnMultiplier = 1f + bestSailing * 0.12f;
        }

        private void OnCannonsFired(int numberFired)
        {
            GrantDutyExperience(CrewDuty.Gunner, Mathf.Clamp(numberFired * 3, 3, 12));
        }

        private void OnRepaired(string crewKey)
        {
            if (!states.TryGetValue(crewKey, out SailorState s) || s.dead || s.overboard) return;
            s.experience += 12;
            status = "Carpenter earned 12 XP for patching a hull breach.";
            SaveStates();
        }

        private void GrantDutyExperience(CrewDuty duty, int points)
        {
            if (economy.ActiveSeed != seed || economy.Crew == null) return;
            foreach (PortEconomy.CrewMember member in economy.Crew)
            {
                if (member == null || string.IsNullOrEmpty(member.offerKey)) continue;
                if (!states.TryGetValue(member.offerKey, out SailorState s) ||
                    s.dead || s.overboard || s.health <= 0f || !crew.IsCrewActive(member.offerKey) ||
                    crew.GetCrewDuty(member.offerKey) != duty) continue;
                s.experience = Mathf.Min(50000, s.experience + points);
            }
        }

        private void OnHullImpact(Vector3 impactPosition, float damage)
        {
            if (UnityEngine.Random.value > impactInjuryChance || economy.ActiveSeed != seed) return;
            string nearestKey = null;
            float best = float.PositiveInfinity;
            foreach (KeyValuePair<string, SailorState> entry in states)
            {
                SailorState s = entry.Value;
                if (s.dead || s.overboard || !crew.IsCrewActive(entry.Key)) continue;
                float d = (crew.CrewWorldPosition(entry.Key) - impactPosition).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                nearestKey = entry.Key;
            }
            if (nearestKey == null) return;
            Injure(nearestKey, Mathf.Clamp(impactHealthLoss * (0.6f + damage / 120f), 8f, 40f));
        }

        private void Injure(string key, float amount)
        {
            if (!states.TryGetValue(key, out SailorState s) || s.dead || s.overboard) return;
            float before = s.health;
            s.health = Mathf.Max(-50f, s.health - Mathf.Max(1f, amount));
            // Repeated damage to a previously incapacitated sailor can be fatal.
            if (before <= 0f && s.health < -15f) s.dead = true;
            status = s.dead ? "A crew member died from injuries." :
                s.health <= 0f ? "A crew member is incapacitated." : "A crew member was injured.";
            ApplyAvailability();
            SaveStates();
        }

        private void OnShipSank()
        {
            int overboard = 0;
            foreach (KeyValuePair<string, SailorState> entry in states)
            {
                SailorState s = entry.Value;
                if (s.dead || s.overboard || !crew.IsCrewActive(entry.Key)) continue;
                // Critical injuries reduce the chance of escaping the sinking ship.
                if (s.health < 25f && UnityEngine.Random.value < 0.3f) s.dead = true;
                else
                {
                    SetRescuePosition(s);
                    s.overboard = true;
                    overboard++;
                }
            }
            status = "Ship sank: " + overboard + " sailors overboard. " +
                (rescueSystem != null ? "Sail close to rescue survivors." : "Recover at a port (prototype).");
            ApplyAvailability();
            SaveStates();
        }

        public void InjureSelectedForTest()
        {
            string key = crew != null ? crew.SelectedCrewKey : null;
            if (key == null) return;
            Injure(key, 40f);
        }

        public void GrantSelectedTrainingXp()
        {
            string key = crew != null ? crew.SelectedCrewKey : null;
            if (key == null || !states.TryGetValue(key, out SailorState s) || s.dead) return;
            s.experience += 40;
            status = "Training test: +40 XP for selected sailor.";
            SaveStates();
        }

        public void TreatSelectedAtPort()
        {
            if (!IsDocked()) { status = "Dock first to treat injuries."; return; }
            string key = crew != null ? crew.SelectedCrewKey : null;
            if (key == null || !states.TryGetValue(key, out SailorState s)) return;
            if (s.dead) { status = "Deceased crew cannot be treated."; return; }
            if (s.overboard) { status = "Recover this sailor before treatment."; return; }
            s.health = Mathf.Min(100f, Mathf.Max(35f, s.health) + 35f);
            status = "Port medic treated selected sailor (prototype: no fee).";
            ApplyAvailability();
            SaveStates();
        }

        public void SendSelectedOverboardForTest()
        {
            string key = crew != null ? crew.SelectedCrewKey : null;
            if (key == null || !states.TryGetValue(key, out SailorState s) || s.dead || s.overboard) return;
            SetRescuePosition(s);
            s.overboard = true;
            status = rescueSystem != null ? "TEST: Sail near the survivor and press F11 to rescue." :
                "TEST: Selected sailor overboard. Dock and press F11 to recover.";
            ApplyAvailability();
            SaveStates();
        }

        public void RecoverOverboardAtPort()
        {
            if (rescueSystem != null)
            {
                rescueSystem.RescueNearby();
                return;
            }
            if (!IsDocked()) { status = "Dock first to recover missing survivors."; return; }
            int recovered = 0;
            foreach (SailorState s in states.Values)
            {
                if (!s.overboard || s.dead) continue;
                s.overboard = false;
                s.health = Mathf.Max(35f, s.health);
                recovered++;
            }
            status = "Port recovery completed: " + recovered + " survivor(s) returned (prototype).";
            ApplyAvailability();
            SaveStates();
        }

        private void SetRescuePosition(SailorState sailor)
        {
            Vector3 position = crew.CrewWorldPosition(sailor.key);
            Vector3 side = movement.transform.right * ((states.Count % 2 == 0) ? 5f : -5f);
            sailor.rescuePosition = new Vector3(position.x + side.x, 0f, position.z + side.z);
            sailor.hasRescuePosition = true;
        }

        private static string SavePath(int saveSeed) => Path.Combine(Application.persistentDataPath,
            "boatships_crew_progression_v1_" + saveSeed + ".json");

        private void LoadStates()
        {
            string path = SavePath(seed);
            if (!BoatshipsSaveStore.Exists(path)) return;
            try
            {
                CrewProgressFile file = JsonUtility.FromJson<CrewProgressFile>(BoatshipsSaveStore.Read(path));
                if (file == null || file.version != 1 || file.seed != seed || file.sailors == null)
                    throw new InvalidDataException("Invalid crew progression file version or seed.");
                foreach (SailorState saved in file.sailors)
                {
                    if (saved == null || string.IsNullOrEmpty(saved.key) || states.ContainsKey(saved.key)) continue;
                    saved.health = Mathf.Clamp(saved.health, -50f, 100f);
                    saved.experience = Mathf.Max(0, saved.experience);
                    states[saved.key] = saved;
                }
            }
            catch (Exception ex)
            {
                saveLocked = true;
                status = "Cannot load crew progression; existing file protected: " + ex.Message;
                Debug.LogWarning("Boatships M13: " + status + " at " + path, this);
            }
        }

        public void SaveStates() => TrySaveStates();

        private bool TrySaveStates()
        {
            LastSaveSucceeded = false;
            if (!ready || seed == int.MinValue || saveLocked) return false;
            try
            {
                var file = new CrewProgressFile { seed = seed };
                foreach (SailorState sailor in states.Values) file.sailors.Add(sailor);
                string path = SavePath(seed);
                BoatshipsSaveStore.Write(path, JsonUtility.ToJson(file));
                LastSaveSucceeded = true;
                return true;
            }
            catch (Exception ex)
            {
                status = "Crew progression save failed: " + ex.Message;
                Debug.LogWarning("Boatships M13: " + status, this);
                return false;
            }
        }

        private void OnApplicationPause(bool pause) { if (pause) SaveStates(); }
        private void OnApplicationQuit() { SaveStates(); }

        private void OnDestroy()
        {
            if (ready) SaveStates();
            Unsubscribe();
            if (weapons != null) weapons.CrewReloadMultiplier = 1f;
            if (movement != null)
            {
                movement.CrewSpeedMultiplier = 1f;
                movement.CrewTurnMultiplier = 1f;
            }
        }
    }
}
