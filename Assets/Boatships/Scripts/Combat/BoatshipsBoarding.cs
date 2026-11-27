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

        private float pressure;

        /// <summary>Enemy sailors still standing aboard the boarded ship.</summary>
        public int DefendersLeft => InProgress ? Mathf.Max(0, encounter.crew) : 0;

        /// <summary>Fighting strength one defender needs to be overcome by the Fighters.</summary>
        private float DefenderToughness => 8f + encounter.tier * 4f;

        public string Status { get; private set; } = "Assign Fighters, batter an enemy (hull below 65% or half her crew), then approach within 8m.";

        public void Configure(BoatshipsEncounterDirector director, BoatshipsCrewProgression sailors,
            BoatshipsCrewManager deckCrew, ShipArcadeController controller, WeaponGroupCombat guns, PortDocking port)
        { encounters = director; progression = sailors; crew = deckCrew; movement = controller; weapons = guns; docking = port; }

        /// <summary>True when the captain personally leads the boarding (no Fighters needed).</summary>
        public bool PlayerLed { get; private set; }

        public void Begin() => Begin(false);

        public void Begin(bool captainLeads)
        {
            if (InProgress || docking.IsDocked || !movement.enabled) return;
            ShipCombatTarget nearest = encounters.NearestEnemy(out EncounterRecord record, out float distance);
            progression.GetBoardingParty(party);
            if (nearest == null || distance > 8f || (party.Count == 0 && !captainLeads))
            { Status = captainLeads ? "No enemy ship within 8 m to board." : "Need an active Fighter and an enemy within 8m."; return; }
            if (!BoatshipsEncounterDirector.CanBoard(nearest, record, out string reason, captainLeads)) { Status = reason; return; }
            if (!nearest.Hull.TryNearestDeckPoint(Vector3.zero, out _))
            { Status = "No intact deck remains for the boarding party."; return; }
            PlayerLed = captainLeads;
            target = nearest;
            encounter = record;
            pressure = 0f;
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
            if (encounter.captured || encounter.defeated) { Finish(false); return; }
            if (encounter.crew <= 0) { TakeShip(); return; }
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
            if ((active == 0 && !PlayerLed) || Time.time - beganAt > (PlayerLed ? 180f : 90f)) { Abort(); return; }
            // The Fighters cut down one defender for every DefenderToughness of fighting strength they bring.
            pressure += power;
            int killed = Mathf.FloorToInt(pressure / DefenderToughness);
            if (killed > 0) { pressure -= killed * DefenderToughness; encounters.KillCrew(encounter, killed); }
            if (encounter.crew <= 0) { TakeShip(); return; }
            // Defenders hit back harder while they still have numbers.
            float wound = (2f + encounter.tier) * Mathf.Clamp(encounter.crew / 5f, 0.3f, 1.5f);
            foreach (string key in party)
                if (progression.BoardingPower(key) > 0f) progression.ApplyBoardingInjury(key, wound);
            Status = "Boarding battle. Enemy crew " + encounter.crew + "/" + encounter.crewMax + ", Fighters " + active + ".";
        }

        /// <summary>The last defender has fallen: the ship is taken intact.</summary>
        private void TakeShip()
        {
            if (encounters.Capture(encounter, target))
            {
                foreach (string key in party) progression.AwardBoardingExperience(key);
                Finish(false);
                Status = "Every defender is down — the ship is yours, still afloat. Surviving fighters earned 25 XP.";
                return;
            }
            // Could not record the capture (save unavailable): the boarders fall back.
            Abort();
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
            PlayerLed = false;
            if (plank != null) Destroy(plank);
            if (plankMaterial != null) Destroy(plankMaterial);
            Ended?.Invoke();
        }

        private void OnDisable() { if (InProgress) Finish(true); }
    }
}
