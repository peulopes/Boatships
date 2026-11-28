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
        private float nextStatusAt;
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
            AddSailorDuties();
            player.SetExit("Hide below deck", () => Finish(UnityEngine.Random.value < 0.45f, "You hid below deck until the fighting stopped."));
            return true;
        }

        /// <summary>A hand on another captain's ship works the guns, plugs leaks and fights boarders, but never steers.</summary>
        private void AddSailorDuties()
        {
            VoxelHull hull = employerShip.Hull;
            Vector3 helm = new Vector3(0f, deck.Top, -HullShape.HalfLength(hull.Definition) * 0.7f);
            foreach (HullHardpoint slot in hull.Definition.Hardpoints)
                if (slot.type == HullHardpointType.Helm) helm = slot.localPosition;
            player.AddInteractable(new FpsInteractable
            {
                Position = () => deck != null && deck.Valid ? deck.ToWorld(helm) : Vector3.zero,
                Prompt = "The helm — only the captain steers this ship",
                Radius = 0.7f,
                Use = () => Say("\"Back to your post, sailor!\" The " + employerName + "'s captain keeps the wheel.")
            });
            Vector3 pumps = deck.Nearest(new Vector3(0f, deck.Top, -0.3f));
            player.AddInteractable(new FpsInteractable
            {
                Position = () => deck != null && deck.Valid ? deck.ToWorld(pumps) : Vector3.zero,
                Prompt = "Hatch to the hold — plug the leaks",
                Radius = 0.7f,
                Use = PlugLeaks
            });
        }

        private void PlugLeaks()
        {
            ShipFlooding flood = employerShip != null ? employerShip.GetComponent<ShipFlooding>() : null;
            if (flood == null || flood.SubmergedUnpatchedBreaches == 0) { Say("The hold is dry — no leaks below the waterline."); return; }
            if (!flood.EmergencyPatch()) { Say("No more plugs and patches in the hold."); return; }
            player.MarkContributed();
            Say("You plugged the leaks below the waterline.");
            if (character != null) character.AddExperience(CharacterSkill.Fighting, 2);
        }

        private string notice;
        private float noticeUntil;

        private void Say(string text)
        {
            notice = text;
            noticeUntil = Time.time + 3.5f;
            nextStatusAt = 0f;
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
            int boardersAlive = 0;
            foreach (BoatshipsDeckFighter b in boarders) if (b != null && b.Alive) boardersAlive++;
            if (boarded && boardersAlive == 0) { Finish(true, "The boarders were thrown back and the attackers fled!"); return; }
            if (distance > 95f) { Finish(true, "The attackers fell away astern."); return; }
            if (Time.time - startedAt > TimeLimit)
            { Finish(employerHull >= Fraction(attacker), "Both ships disengaged as the light failed."); return; }

            if (Time.time >= nextStatusAt)
            {
                nextStatusAt = Time.time + 0.25f;
                int leaks = employerFlood != null ? employerFlood.SubmergedUnpatchedBreaches : 0;
                player.SetStatus("Sailor aboard the " + employerName + " — the captain steers; you man the guns, plug leaks and repel boarders" +
                    "\nEnemy hull " + Mathf.RoundToInt(Fraction(attacker) * 100f) + "%   Our hull " + Mathf.RoundToInt(employerHull * 100f) + "%" +
                    (leaks > 0 ? "   LEAKING — get to the hatch!" : "") +
                    (boarded ? "   Boarders " + boardersAlive : distance < 22f ? "   Prepare to repel boarders!" : "") +
                    (Time.time < noticeUntil ? "\n" + notice : ""));
            }
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

    /// <summary>When a boarding starts, the player crosses over in first person with two of the boarding party
    /// and fights her defenders on deck (they muster as the party lands). Cut them all down and the ship is taken intact.</summary>
    public sealed class BoatshipsBoardingFirstPerson : MonoBehaviour
    {
        private BoatshipsBoarding boarding;
        private BoatshipsFirstPerson player;
        private BoatshipsTravelController travel;
        private BoatshipsEncounterDirector director;
        private Transform flagship;
        private readonly List<BoatshipsDeckFighter> fighters = new List<BoatshipsDeckFighter>();
        private bool session;

        public void Configure(BoatshipsBoarding combat, BoatshipsFirstPerson firstPerson, BoatshipsTravelController controller,
            Transform ownShip)
        {
            boarding = combat; player = firstPerson; travel = controller; flagship = ownShip;
            boarding.Started += OnStarted;
            boarding.Ended += OnEnded;
            player.Downed += OnDowned;
        }

        private void OnStarted(ShipCombatTarget target, EncounterRecord record)
        {
            if (target == null) return;
            // Board from the helm, or straight from our own deck; never out of a town or another ship's fight.
            VoxelHull ownHull = flagship != null ? flagship.GetComponentInChildren<VoxelHull>() : null;
            if (player.Active && !player.OnDeck(ownHull)) return;
            DeckSpace deck = DeckSpace.Create(target.Hull);
            if (deck == null) return;
            Vector3 near = deck.Nearest(deck.ToLocal(flagship.position));
            for (int i = 0; i < 2; i++)
            {
                BoatshipsDeckFighter mate = BoatshipsDeckFighter.Spawn("Boarding Party", 0, deck, near, 60f, 9f);
                if (mate == null) continue;
                mate.JumpFrom(flagship.position + Vector3.up * 0.6f);
                fighters.Add(mate);
            }
            // Her defenders muster on deck as the party lands.
            if (record != null)
            {
                director = director != null ? director : FindAnyObjectByType<BoatshipsEncounterDirector>();
                var random = new System.Random(Environment.TickCount);
                int count = Mathf.Min(Mathf.Max(0, record.crew), 6);
                for (int i = 0; i < count; i++)
                {
                    BoatshipsDeckFighter defender = BoatshipsDeckFighter.Spawn("Defender", 1, deck, deck.RandomPoint(random),
                        26f + record.tier * 8f, 5f + record.tier * 2f);
                    if (defender == null) continue;
                    defender.Died += OnDefenderDied;
                    fighters.Add(defender);
                }
            }
            Vector3 toCenter = deck.Center - near;
            player.BeginDeck(target.Hull, near, Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg);
            player.SetExit("Back to your ship", EndSession);
            player.SetStatus("Boarding! Kill every last one of her crew to take the ship intact.\n[1] cutlass  ·  [2] flintlock pistol");
            session = true;
        }

        private void Update()
        {
            if (!session || !boarding.InProgress) return;
            int crew = boarding.DefendersLeft;
            int onDeck = fighters.FindAll(f => f != null && f.Team == 1 && f.Alive).Count;
            EncounterRecord record = boarding.Encounter;
            player.SetStatus("Boarding — kill them all to take her intact" +
                "\nEnemy crew " + crew + (record != null && record.crewMax > 0 ? "/" + record.crewMax : "") +
                "   On deck " + onDeck + (crew > onDeck ? "   (more below)" : "") +
                (crew <= 0 ? "\nThe last of her crew falls — the ship is yours!" : ""));
        }

        private void OnDefenderDied(BoatshipsDeckFighter defender)
        {
            if (!session || director == null || !boarding.InProgress) return;
            director.KillCrew(boarding.Encounter, 1, defender.FelledBy);
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
            if (!session) return;
            session = false;
            foreach (BoatshipsDeckFighter fighter in fighters) if (fighter != null) Destroy(fighter.gameObject);
            fighters.Clear();
            player.End();
            // A boarding the captain led alone cannot go on without them.
            if (boarding.InProgress && boarding.PlayerLed) boarding.Abort();
        }

        private void OnDestroy()
        {
            if (boarding != null) { boarding.Started -= OnStarted; boarding.Ended -= OnEnded; }
            if (player != null) player.Downed -= OnDowned;
        }
    }
}
