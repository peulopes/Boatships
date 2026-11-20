using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>First-person battle aboard an AI ship the player works on. The AI captain steers and fires;
    /// the player can man the deck guns and fight off boarders alongside the crew.</summary>
    public sealed class BoatshipsCrewBattle : MonoBehaviour
    {
        private const float TimeLimit = 240f;

        private BoatshipsEncounterDirector director;
        private BoatshipsFirstPerson player;
        private BoatshipsCharacter character;
        private OceanWaves ocean;

        private ShipCombatTarget employerShip;
        private DeckSpace deck;
        private readonly List<string> attackerIds = new List<string>();
        private readonly List<BoatshipsDeckFighter> boarders = new List<BoatshipsDeckFighter>();
        private readonly List<BoatshipsDeckFighter> crew = new List<BoatshipsDeckFighter>();
        private readonly List<BoatshipsDeckCannon> cannons = new List<BoatshipsDeckCannon>();
        private Action<bool, bool, string> finished;
        private int attackerTier;
        private bool boarded;
        private float startedAt;
        private string employerName;

        public bool Active { get; private set; }

        public void Configure(BoatshipsEncounterDirector encounters, BoatshipsFirstPerson firstPerson,
            BoatshipsCharacter captain, OceanWaves waves)
        {
            director = encounters; player = firstPerson; character = captain; ocean = waves;
            player.Downed += OnDowned;
        }

        private void OnDestroy() { if (player != null) player.Downed -= OnDowned; }

        /// <summary>Starts the battle at the flagship's position. finished(won, fought, summary) is called once.</summary>
        public bool Begin(string shipName, int employerTier, float heading, int tier, IList<EncounterKind> kinds,
            Vector3 center, Action<bool, bool, string> onFinished)
        {
            if (Active || director == null || !director.IsReady) return false;
            employerShip = director.SpawnAlly(shipName, center, heading, employerTier);
            if (employerShip == null) return false;
            attackerIds.Clear();
            attackerIds.AddRange(director.SpawnBattle(kinds, tier, 30f));
            if (attackerIds.Count == 0) { Destroy(employerShip.gameObject); employerShip = null; return false; }
            ShipCombatTarget firstAttacker = null;
            foreach (string id in attackerIds)
            {
                ShipCombatTarget actor = director.ActorFor(id);
                if (actor == null) continue;
                if (firstAttacker == null) firstAttacker = actor;
                EnemyShipAI ai = actor.GetComponent<EnemyShipAI>();
                if (ai != null) ai.SetTarget(employerShip);
            }
            EnemyShipAI captain = employerShip.GetComponent<EnemyShipAI>();
            if (captain != null && firstAttacker != null) captain.SetTarget(firstAttacker);

            deck = DeckSpace.Create(employerShip.Hull);
            finished = onFinished;
            attackerTier = tier;
            employerName = shipName;
            boarded = false;
            startedAt = Time.time;
            Active = true;

            var random = new System.Random(Environment.TickCount);
            for (int i = 0; i < 3; i++)
            {
                BoatshipsDeckFighter sailor = BoatshipsDeckFighter.Spawn("Crewmate", 0, deck, deck.RandomPoint(random),
                    55f + employerTier * 10f, 8f + employerTier * 2f);
                if (sailor != null) crew.Add(sailor);
            }
            int gunnery = character != null ? character.Skill(CharacterSkill.Gunnery) : 0;
            float reload = Mathf.Max(1.6f, 3.6f * (1f - gunnery * 0.05f));
            float damage = 55f * (1f + gunnery * 0.06f);
            player.BeginDeck(employerShip.Hull, deck.Center, 0f);
            foreach (int side in new[] { -1, 1 })
            {
                BoatshipsDeckCannon cannon = BoatshipsDeckCannon.Create(deck, side, employerShip, ocean, reload, damage);
                cannons.Add(cannon);
                player.AddInteractable(new FpsInteractable
                {
                    Position = () => cannon != null ? cannon.WorldPosition : Vector3.zero,
                    Prompt = "Man the " + (side > 0 ? "starboard" : "port") + " gun",
                    Radius = 0.7f,
                    Use = () => player.Mount(cannon)
                });
            }
            player.SetExit("Hide below deck", () => Finish(UnityEngine.Random.value < 0.45f, "You hid below deck until the fighting stopped."));
            return true;
        }

        private ShipCombatTarget LiveAttacker()
        {
            foreach (string id in attackerIds)
            {
                ShipCombatTarget actor = director.ActorFor(id);
                if (actor != null && actor.gameObject.activeInHierarchy && actor.RemainingVoxels > 0) return actor;
            }
            return null;
        }

        private void Update()
        {
            if (!Active) return;
            if (employerShip == null || deck == null || !deck.Valid) { Finish(false, "The ship went down."); return; }
            float employerHull = Fraction(employerShip);
            ShipFlooding employerFlood = employerShip.GetComponent<ShipFlooding>();
            if (employerHull < 0.25f || (employerFlood != null && employerFlood.IsSinking))
            { Finish(false, "The " + employerName + " is going down — the attackers have won."); return; }
            if (director.RemainingHostiles(attackerIds) == 0) { Finish(true, "The attackers' ship was sunk!"); return; }
            ShipCombatTarget attacker = LiveAttacker();
            if (attacker == null) { Finish(true, "The attackers broke off."); return; }

            EnemyShipAI captain = employerShip.GetComponent<EnemyShipAI>();
            if (captain != null && captain.Target != attacker) captain.SetTarget(attacker);
            float distance = Vector3.Distance(attacker.transform.position, employerShip.transform.position);
            if (!boarded && distance < 16f && Time.time - startedAt > 12f) Board(attacker);
            int boardersAlive = boarders.FindAll(b => b != null && b.Alive).Count;
            if (boarded && boardersAlive == 0) { Finish(true, "The boarders were thrown back and the attackers fled!"); return; }
            if (distance > 95f) { Finish(true, "The attackers fell away astern."); return; }
            if (Time.time - startedAt > TimeLimit)
            { Finish(employerHull >= Fraction(attacker), "Both ships disengaged as the light failed."); return; }

            player.SetStatus("Battle aboard the " + employerName + "\nEnemy hull " + Mathf.RoundToInt(Fraction(attacker) * 100f) +
                "%   Our hull " + Mathf.RoundToInt(employerHull * 100f) + "%" +
                (boarded ? "   Boarders " + boardersAlive : distance < 22f ? "   Prepare to repel boarders!" : ""));
        }

        private static float Fraction(ShipCombatTarget ship) =>
            ship != null && ship.TotalVoxels > 0 ? (float)ship.RemainingVoxels / ship.TotalVoxels : 0f;

        private void Board(ShipCombatTarget attacker)
        {
            boarded = true;
            var random = new System.Random(Environment.TickCount);
            int count = Mathf.Clamp(1 + attackerTier, 2, 5);
            Vector3 side = deck.ToLocal(attacker.transform.position);
            int railSide = side.x >= 0f ? 1 : -1;
            for (int i = 0; i < count; i++)
            {
                Vector3 landing = deck.Rail(railSide, (float)random.NextDouble() * 1.6f - 0.8f);
                BoatshipsDeckFighter boarder = BoatshipsDeckFighter.Spawn("Boarder", 1, deck, landing,
                    28f + attackerTier * 9f, 6f + attackerTier * 2f);
                if (boarder == null) continue;
                boarder.JumpFrom(attacker.transform.position + Vector3.up * 0.6f + UnityEngine.Random.insideUnitSphere * 0.4f);
                boarders.Add(boarder);
            }
        }

        private void OnDowned()
        {
            if (Active) Finish(false, "You were struck down on the deck.");
        }

        private void Finish(bool won, string summary)
        {
            if (!Active) return;
            Active = false;
            bool fought = player.Contributed || player.Kills > 0;
            player.End();
            foreach (BoatshipsDeckFighter fighter in boarders) if (fighter != null) Destroy(fighter.gameObject);
            foreach (BoatshipsDeckFighter fighter in crew) if (fighter != null) Destroy(fighter.gameObject);
            foreach (BoatshipsDeckCannon cannon in cannons) if (cannon != null) Destroy(cannon.gameObject);
            boarders.Clear(); crew.Clear(); cannons.Clear();
            if (employerShip != null) Destroy(employerShip.gameObject);
            employerShip = null; deck = null;
            director.DismissUnresolved();
            attackerIds.Clear();
            Action<bool, bool, string> callback = finished;
            finished = null;
            callback?.Invoke(won, fought, summary);
        }
    }

    /// <summary>When the player's Fighters board an enemy, the player crosses over in first person
    /// and fights the defenders. Every defender cut down weakens the defence of the enemy ship.</summary>
    public sealed class BoatshipsBoardingFirstPerson : MonoBehaviour
    {
        private BoatshipsBoarding boarding;
        private BoatshipsFirstPerson player;
        private BoatshipsTravelController travel;
        private Transform flagship;
        private readonly List<BoatshipsDeckFighter> fighters = new List<BoatshipsDeckFighter>();
        private float damagePerKill;
        private bool session;

        public void Configure(BoatshipsBoarding combat, BoatshipsFirstPerson firstPerson, BoatshipsTravelController controller, Transform ownShip)
        {
            boarding = combat; player = firstPerson; travel = controller; flagship = ownShip;
            boarding.Started += OnStarted;
            boarding.Ended += OnEnded;
            player.Downed += OnDowned;
        }

        private void OnStarted(ShipCombatTarget target, EncounterRecord record)
        {
            if (player.Active || target == null) return;
            DeckSpace deck = DeckSpace.Create(target.Hull);
            if (deck == null) return;
            int count = Mathf.Clamp(2 + record.tier, 3, 6);
            damagePerKill = record.defenderHealth / count + 1f;
            var random = new System.Random(Environment.TickCount);
            for (int i = 0; i < count; i++)
            {
                BoatshipsDeckFighter defender = BoatshipsDeckFighter.Spawn("Defender", 1, deck, deck.RandomPoint(random),
                    26f + record.tier * 8f, 5f + record.tier * 2f);
                if (defender == null) continue;
                defender.Died += OnDefenderDied;
                fighters.Add(defender);
            }
            Vector3 near = deck.Nearest(deck.ToLocal(flagship.position));
            for (int i = 0; i < 2; i++)
            {
                BoatshipsDeckFighter mate = BoatshipsDeckFighter.Spawn("Boarding Party", 0, deck, near, 60f, 9f);
                if (mate == null) continue;
                mate.JumpFrom(flagship.position + Vector3.up * 0.6f);
                fighters.Add(mate);
            }
            Vector3 toCenter = deck.Center - near;
            player.BeginDeck(target.Hull, near, Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg);
            player.SetExit("Back to your ship", EndSession);
            player.SetStatus("Boarding! Cut down the defenders to take the ship.");
            session = true;
        }

        private void OnDefenderDied(BoatshipsDeckFighter defender)
        {
            if (!session) return;
            boarding.DamageDefenders(damagePerKill);
            if (fighters.Exists(f => f != null && f.Team == 1 && f.Alive)) return;
            boarding.DamageDefenders(9999f);
            player.SetStatus("The last defender falls — the ship is yours!");
        }

        private void Update()
        {
            if (!session || !boarding.InProgress) return;
            int defenders = fighters.FindAll(f => f != null && f.Team == 1 && f.Alive).Count;
            player.SetStatus("Boarding — defenders " + defenders + "   Defence " +
                Mathf.CeilToInt(boarding.Encounter != null ? boarding.Encounter.defenderHealth : 0f));
        }

        private void OnDowned()
        {
            if (!session) return;
            EndSession();
            travel.Notify("You were wounded and carried back aboard. Your Fighters fight on.");
        }

        private void OnEnded()
        {
            if (session) EndSession();
        }

        private void EndSession()
        {
            session = false;
            foreach (BoatshipsDeckFighter fighter in fighters) if (fighter != null) Destroy(fighter.gameObject);
            fighters.Clear();
            player.End();
        }

        private void OnDestroy()
        {
            if (boarding != null) { boarding.Started -= OnStarted; boarding.Ended -= OnEnded; }
            if (player != null) player.Downed -= OnDowned;
        }
    }
}
