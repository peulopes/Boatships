using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    public sealed class BoatshipsBoarding : MonoBehaviour
    {
        [SerializeField] private BoatshipsEncounterDirector encounters;
        [SerializeField] private BoatshipsCrewProgression progression;
        [SerializeField] private BoatshipsCrewManager crew;
        [SerializeField] private ShipArcadeController movement;
        [SerializeField] private WeaponGroupCombat weapons;
        [SerializeField] private PortDocking docking;
        private readonly List<string> party = new List<string>();
        private ShipCombatTarget target;
        private EncounterRecord encounter;
        private EnemyShipAI enemyAI;
        private float nextRound;
        private float beganAt;
        private bool previousMovement;
        private bool previousWeapons;
        private GameObject plank;
        private Material plankMaterial;
        public bool InProgress => target != null;
        public ShipCombatTarget Target => target;
        public EncounterRecord Encounter => encounter;
        /// <summary>Raised when a boarding action starts (first-person boarding hooks in here).</summary>
        public event System.Action<ShipCombatTarget, EncounterRecord> Started;
        /// <summary>Raised when a boarding action ends for any reason (capture or retreat).</summary>
        public event System.Action Ended;

        /// <summary>Extra damage to the defenders, e.g. from the player fighting in first person.</summary>
        public void DamageDefenders(float amount)
        {
            if (InProgress && amount > 0f) encounter.defenderHealth = Mathf.Max(0f, encounter.defenderHealth - amount);
        }
        public string Status { get; private set; } = "Assign Fighters, weaken an enemy hull below 65%, then approach within 8m.";

        public void Configure(BoatshipsEncounterDirector director, BoatshipsCrewProgression sailors,
            BoatshipsCrewManager deckCrew, ShipArcadeController controller, WeaponGroupCombat guns, PortDocking port)
        { encounters = director; progression = sailors; crew = deckCrew; movement = controller; weapons = guns; docking = port; }

        public void Begin()
        {
            if (InProgress || docking.IsDocked || !movement.enabled) return;
            ShipCombatTarget nearest = encounters.NearestEnemy(out EncounterRecord record, out float distance);
            progression.GetBoardingParty(party);
            if (nearest == null || distance > 8f || party.Count == 0)
            { Status = "Need an active Fighter and an enemy within 8m."; return; }
            float fraction = (float)nearest.RemainingVoxels / Mathf.Max(1, nearest.TotalVoxels);
            ShipFlooding flooding = nearest.GetComponent<ShipFlooding>();
            if (fraction > 0.65f || fraction <= 0.1f || (flooding != null && flooding.IsSinking))
            { Status = "Weaken the hull below 65% and board before it sinks."; return; }
            if (!nearest.Hull.TryNearestDeckPoint(Vector3.zero, out _))
            { Status = "No intact deck remains for the boarding party."; return; }
            target = nearest;
            encounter = record;
            encounter.defenderHealth = Mathf.Min(encounter.defenderHealth, (45f + record.tier * 20f) * fraction);
            enemyAI = target.GetComponent<EnemyShipAI>();
            enemyAI.enabled = false;
            previousMovement = movement.enabled; previousWeapons = weapons.enabled;
            movement.enabled = false; weapons.enabled = false;
            foreach (string key in party) crew.SetBoardingTarget(key, target.Hull.transform);
            plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = "Boarding Gangplank";
            Destroy(plank.GetComponent<Collider>());
            plankMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.35f, 0.22f, 0.1f) };
            plank.GetComponent<Renderer>().sharedMaterial = plankMaterial;
            beganAt = Time.time;
            nextRound = Time.time + 4f;
            Status = "Fighters crossing to the enemy deck.";
            Started?.Invoke(target, encounter);
        }

        private void Update()
        {
            if (!InProgress) return;
            ShipFlooding ownFlood = movement.GetComponent<ShipFlooding>();
            ShipFlooding otherFlood = target.GetComponent<ShipFlooding>();
            if (docking.IsDocked || target.RemainingVoxels <= 0 || !target.gameObject.activeInHierarchy ||
                !target.Hull.TryNearestDeckPoint(Vector3.zero, out _) ||
                (ownFlood != null && ownFlood.IsSinking) || (otherFlood != null && otherFlood.IsSinking))
            { Abort(); return; }
            Vector3 start = movement.transform.position + Vector3.up * 0.6f;
            Vector3 end = target.transform.position + Vector3.up * 0.6f;
            plank.transform.position = (start + end) * 0.5f;
            plank.transform.rotation = Quaternion.LookRotation(end - start);
            plank.transform.localScale = new Vector3(0.65f, 0.12f, Vector3.Distance(start, end));
            if (Time.time < nextRound) return;
            nextRound = Time.time + 1f;
            float power = 0f;
            int active = 0;
            foreach (string key in party)
            {
                float strength = progression.BoardingPower(key);
                power += strength;
                if (strength > 0f) active++;
            }
            if (active == 0 || Time.time - beganAt > 90f) { Abort(); return; }
            encounter.defenderHealth = Mathf.Max(0f, encounter.defenderHealth - power);
            if (encounter.defenderHealth <= 0f)
            {
                if (encounters.Capture(encounter, target))
                {
                    foreach (string key in party) progression.AwardBoardingExperience(key);
                    Finish(false);
                    Status = "Enemy captured. Surviving fighters earned 25 XP.";
                }
                return;
            }
            foreach (string key in party)
                if (progression.BoardingPower(key) > 0f) progression.ApplyBoardingInjury(key, 3f + encounter.tier);
            Status = "Boarding battle. Defenders " + Mathf.CeilToInt(encounter.defenderHealth) + " HP, Fighters " + active + ".";
        }

        public void Abort()
        {
            if (!InProgress) return;
            Finish(true);
            Status = "Boarding party returned. Treat injured fighters at a port.";
        }

        private void Finish(bool resumeEnemy)
        {
            foreach (string key in party) crew.SetBoardingTarget(key, null);
            ShipFlooding flood = movement.GetComponent<ShipFlooding>();
            bool seaworthy = flood == null || !flood.IsSinking;
            if (seaworthy && !docking.IsDocked)
            { movement.enabled = previousMovement; weapons.enabled = previousWeapons; }
            if (enemyAI != null && resumeEnemy)
            {
                ShipFlooding enemyFlood = enemyAI.GetComponent<ShipFlooding>();
                enemyAI.enabled = enemyFlood == null || !enemyFlood.IsSinking;
            }
            target = null; encounter = null; enemyAI = null;
            if (plank != null) Destroy(plank);
            if (plankMaterial != null) Destroy(plankMaterial);
            Ended?.Invoke();
        }

        private void OnDisable() { if (InProgress) Finish(true); }
    }
}
