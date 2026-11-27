using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>The player's sea battles, fought on foot aboard their own ship: take the wheel and trim the
    /// sails, load and fire the deck guns by hand, patch holes and bail water, climb across to board the
    /// enemy. Both ships carry full-size crews (<see cref="BoatshipsShipCrew"/>) who do the same jobs.
    /// Downed players and sailors wake up again aboard their own ship.</summary>
    public sealed class BoatshipsShipBattle : MonoBehaviour
    {
        private const float KnockOutSeconds = 6f;
        private const float SailChangePerSecond = 0.6f;

        private sealed class Enemy
        {
            public string Id;
            public ShipCombatTarget Actor;
            public BoatshipsShipCrew Crew;
            public EnemyShipAI AI;
            public float CloseRange, ApproachRange;
            public FpsInteractable ReturnLadder;
            public string Name;
        }

        private BoatshipsFirstPerson player;
        private BoatshipsEncounterDirector director;
        private PortEconomy economy;
        private BoatshipsCharacter character;
        private BoatshipsCrewManager crewManager;
        private OceanWaves ocean;
        private BoatshipsTravelController travel;

        private ShipCombatTarget flagship;
        private ShipArcadeController movement;
        private WeaponGroupCombat weapons;
        private BoatshipsShipCrew own;
        private readonly List<Enemy> enemies = new List<Enemy>();
        private readonly List<FpsInteractable> holeInteractables = new List<FpsInteractable>();
        private readonly List<ShipFlooding.Breach> breachScratch = new List<ShipFlooding.Breach>();
        private readonly List<BoatshipsCrewManager.BattleSailor> roster = new List<BoatshipsCrewManager.BattleSailor>();
        private float sail;
        private float rudder;
        private float startedAt;
        private float sinkingSince = -1f;
        private float nextStatus;
        private float nextCargo;
        private int captures;

        public bool Active { get; private set; }
        /// <summary>True once the battle ended on its own (flagship lost): the command view takes over.</summary>
        public bool Abandoned { get; private set; }

        /// <summary>A new battle may start in first person again.</summary>
        public void ResetAbandoned() => Abandoned = false;

        public void Configure(BoatshipsFirstPerson firstPerson, BoatshipsEncounterDirector encounters, PortEconomy account,
            BoatshipsCharacter captain, BoatshipsCrewManager deckCrew, OceanWaves waves, BoatshipsTravelController controller)
        {
            player = firstPerson; director = encounters; economy = account; character = captain;
            crewManager = deckCrew; ocean = waves; travel = controller;
            player.Downed += OnDowned;
        }

        private void OnDestroy() { if (player != null) player.Downed -= OnDowned; }

        public bool Begin(ShipCombatTarget ownShip, ShipArcadeController shipMovement, WeaponGroupCombat shipWeapons, IList<string> enemyIds)
        {
            if (Active || ownShip == null || player == null || player.Active) return false;
            int gunnery = character != null ? character.Skill(CharacterSkill.Gunnery) : 0;
            float load = Mathf.Max(1.4f, 3.2f * (1f - gunnery * 0.05f));
            float damage = 50f * (1f + gunnery * 0.06f) * (1f + (economy != null ? economy.Campaign.cannonLevel * 0.2f : 0f));
            own = BoatshipsShipCrew.Attach(ownShip, 0, ocean, load, damage, 60f, 9f,
                () => economy != null && economy.TrySpendWood(1), () => economy != null ? economy.Wood : 0);
            if (own == null) return false;
            flagship = ownShip; movement = shipMovement; weapons = shipWeapons;
            Active = true; Abandoned = false;
            BoatshipsAudio.Play2D(Sfx.Bell);
            startedAt = Time.time; sinkingSince = -1f; captures = 0;
            sail = movement != null ? Mathf.Clamp01(movement.CurrentSpeed / 9f) : 0f;
            rudder = 0f;
            if (movement != null) { movement.ExternalControl = true; movement.ExternalThrottle = sail; movement.ExternalRudder = 0f; }
            if (weapons != null) weapons.enabled = false;
            if (crewManager != null)
            {
                crewManager.GetBattleRoster(roster);
                crewManager.AvatarsHidden = true;
                for (int i = 0; i < roster.Count && i < 6; i++) own.AddSailor(roster[i].Name, roster[i].Duty);
            }
            own.BreachesChanged += RebuildHoleInteractables;

            player.BeginDeck(ownShip.Hull, own.HelmLocal, 0f);
            player.SetExit("Sail away (World Map)", () => travel.LeaveLocal());
            RegisterOwnInteractables();
            SyncEnemies(enemyIds);
            player.SetStatus("All hands! Take the wheel, man the guns, keep her afloat.");
            return true;
        }

        /// <summary>Gives every live enemy of this battle a crew; drops sunk or captured ones.</summary>
        public void SyncEnemies(IList<string> ids)
        {
            if (!Active) return;
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                Enemy enemy = enemies[i];
                if (enemy.Actor != null && enemy.Crew != null && enemy.Crew.Afloat && !IsResolved(enemy.Id)) continue;
                DropEnemy(enemy);
                enemies.RemoveAt(i);
            }
            if (ids == null) return;
            foreach (string id in ids)
            {
                if (enemies.Exists(e => e.Id == id) || IsResolved(id)) continue;
                ShipCombatTarget actor = director.ActorFor(id);
                EncounterRecord record = Record(id);
                if (actor == null || record == null || !actor.gameObject.activeInHierarchy) continue;
                AddEnemy(id, actor, record);
            }
        }

        public void End()
        {
            if (!Active) return;
            Active = false;
            foreach (Enemy enemy in enemies) DropEnemy(enemy);
            enemies.Clear();
            if (own != null) { own.BreachesChanged -= RebuildHoleInteractables; Destroy(own); }
            own = null;
            holeInteractables.Clear();
            if (player != null && player.Active) player.End();
            if (movement != null) { movement.ExternalControl = false; movement.ExternalThrottle = 0f; movement.ExternalRudder = 0f; }
            ShipFlooding flood = flagship != null ? flagship.GetComponent<ShipFlooding>() : null;
            if (weapons != null) weapons.enabled = flood == null || !flood.IsSinking;
            if (crewManager != null) crewManager.AvatarsHidden = false;
            flagship = null;
        }

        // ───────────────────────────── Setup ─────────────────────────────

        private void AddEnemy(string id, ShipCombatTarget actor, EncounterRecord record)
        {
            int tier = Mathf.Clamp(record.tier, 1, 5);
            int planks = 3 + tier * 2;
            BoatshipsShipCrew crew = BoatshipsShipCrew.Attach(actor, 1, ocean, Mathf.Max(1.8f, 3.6f - tier * 0.3f), 16f + tier * 5f,
                26f + tier * 8f, 5f + tier * 2f, () => { if (planks <= 0) return false; planks--; return true; }, () => planks);
            if (crew == null) return;
            var enemy = new Enemy { Id = id, Actor = actor, Crew = crew, AI = actor.GetComponent<EnemyShipAI>(),
                Name = record.kind.ToString().ToLowerInvariant() };
            int count = Mathf.Clamp(2 + tier, 3, 7);
            for (int i = 0; i < count; i++)
            {
                CrewDuty duty = i % 4 == 0 ? CrewDuty.Gunner : i % 4 == 1 ? CrewDuty.Carpenter : i % 4 == 2 ? CrewDuty.Fighter : CrewDuty.Gunner;
                if (i == count - 1 && count >= 5) duty = CrewDuty.Sailor;
                crew.AddSailor(record.kind + " hand", duty);
            }
            crew.Opponents.Add(own);
            // Every enemy sailor cut down or shot is a real casualty; how he fell decides whether he lives to be taken prisoner.
            crew.SailorDied += sailor => director.KillCrew(Record(id), 1, sailor.FelledBy);
            own.Opponents.Add(crew);
            if (enemy.AI != null)
            {
                enemy.AI.CrewGunnery = true;
                enemy.CloseRange = enemy.AI.CloseRange;
                enemy.ApproachRange = enemy.AI.ApproachRange;
            }
            // Climbing home from the enemy deck.
            enemy.ReturnLadder = new FpsInteractable
            {
                Position = () => enemy.Crew != null && flagship != null ? enemy.Crew.RailWorldToward(flagship.transform.position) : Vector3.zero,
                Prompt = "Climb back aboard your ship",
                Radius = 0.7f,
                Available = () => enemy.Crew != null && player.CurrentDeck != null && player.CurrentDeck.Hull == enemy.Actor.Hull &&
                    Gap(enemy.Actor) <= BoatshipsShipCrew.BoardRange,
                Use = () => player.TransferToDeck(flagship.Hull, own.RailToward(enemy.Actor.transform.position))
            };
            player.AddInteractable(enemy.ReturnLadder);
            enemies.Add(enemy);
        }

        private void DropEnemy(Enemy enemy)
        {
            if (enemy.ReturnLadder != null && player != null) player.RemoveInteractable(enemy.ReturnLadder);
            if (own != null && enemy.Crew != null) own.Opponents.Remove(enemy.Crew);
            if (enemy.AI != null)
            {
                enemy.AI.CrewGunnery = false;
                enemy.AI.Helmed = true;
                if (enemy.CloseRange > 0f) enemy.AI.SetEngagementRanges(enemy.CloseRange, enemy.ApproachRange);
            }
            if (enemy.Crew != null) Destroy(enemy.Crew);
            enemy.Crew = null;
        }

        private void RegisterOwnInteractables()
        {
            player.AddInteractable(new FpsInteractable
            {
                Position = () => own != null ? own.Deck.ToWorld(own.HelmLocal) : Vector3.zero,
                Prompt = "Take the wheel", Radius = 0.6f,
                Available = () => own != null && player.CurrentDeck != null && player.CurrentDeck.Hull == own.Deck.Hull,
                Use = () => player.MountHelm(own.HelmLocal)
            });
            foreach (BoatshipsDeckCannon cannon in own.Cannons)
            {
                BoatshipsDeckCannon gun = cannon;
                player.AddInteractable(new FpsInteractable
                {
                    Position = () => gun != null ? gun.WorldPosition : Vector3.zero,
                    Prompt = (gun.Side > 0 ? "Man the starboard gun" : gun.Side < 0 ? "Man the port gun" : "Man the chaser gun"),
                    Radius = 0.6f,
                    Available = () => gun != null,
                    Use = () => { own.Release(gun); player.Mount(gun); }
                });
            }
            player.AddInteractable(new FpsInteractable
            {
                Position = () => own != null ? own.Deck.ToWorld(own.BailLocal) : Vector3.zero,
                Prompt = "Bail water with the bucket", Radius = 0.7f,
                HoldSeconds = BoatshipsShipCrew.BailSeconds, Repeat = true,
                Available = () => own != null && own.Water > 0.005f && player.CurrentDeck != null && player.CurrentDeck.Hull == own.Deck.Hull,
                Use = () => own.Bail(BoatshipsShipCrew.BailAmount * 1.2f)
            });
            player.AddInteractable(new FpsInteractable
            {
                Position = () =>
                {
                    BoatshipsShipCrew target = own != null ? own.NearestOpponent(out _) : null;
                    return target != null ? own.RailWorldToward(target.Ship.transform.position) : Vector3.zero;
                },
                Prompt = "Climb across and board the enemy ship", Radius = 0.7f,
                Available = () =>
                {
                    if (own == null || player.CurrentDeck == null || player.CurrentDeck.Hull != own.Deck.Hull) return false;
                    return own.NearestOpponent(out float gap) != null && gap <= BoatshipsShipCrew.BoardRange;
                },
                Use = () =>
                {
                    BoatshipsShipCrew target = own.NearestOpponent(out _);
                    if (target != null) player.TransferToDeck(target.Ship.Hull, target.RailToward(flagship.transform.position));
                }
            });
        }

        /// <summary>One "patch the hole" interaction per open hole in the player's hull.</summary>
        private void RebuildHoleInteractables()
        {
            foreach (FpsInteractable hole in holeInteractables) player.RemoveInteractable(hole);
            holeInteractables.Clear();
            if (own == null) return;
            own.GetBreaches(breachScratch);
            foreach (ShipFlooding.Breach breach in breachScratch)
            {
                if (breach.Patched) continue;
                int key = breach.Key;
                Vector3 deckPoint = own.HoleDeckPoint(breach.HullLocal);
                var hole = new FpsInteractable
                {
                    Position = () => own != null ? own.Deck.ToWorld(deckPoint) : Vector3.zero,
                    Prompt = breach.Submerged ? "Patch the leak with a plank" : "Patch the hole with a plank",
                    Radius = 0.65f,
                    HoldSeconds = BoatshipsShipCrew.PatchSeconds * 0.75f,
                    Available = () => own != null && own.IsOpenHole(key) && own.PlanksLeft > 0 &&
                        player.CurrentDeck != null && player.CurrentDeck.Hull == own.Deck.Hull,
                    Use = () => { if (!own.TryPatch(key)) travel.Notify("No planks left. Buy wood at a port store."); }
                };
                holeInteractables.Add(hole);
                player.AddInteractable(hole);
            }
        }

        // ───────────────────────────── Frame ─────────────────────────────

        private void Update()
        {
            if (!Active) return;
            if (flagship == null || own == null || !player.Active) { End(); return; }
            if (weapons != null && weapons.enabled) weapons.enabled = false;
            Steer();
            UpdateEnemies();
            KeepPlayerAboard();
            ShipFlooding flood = flagship.GetComponent<ShipFlooding>();
            if (flood != null && flood.IsSinking)
            {
                if (sinkingSince < 0f) { sinkingSince = Time.time; player.SetStatus("She's going down! Abandon ship!"); }
                if (Time.time - sinkingSince > 4f) { Abandoned = true; End(); travel.Notify("Your ship is sinking. Abandon ship and limp back to port."); return; }
            }
            if (Time.time >= nextCargo)
            {
                nextCargo = Time.time + 1f;
                // The crew hauls in floating cargo the ship sails through.
                if (economy != null && economy.HasLootNear(flagship.transform.position, 9f))
                {
                    director.CollectNearbyCargo();
                    travel.Notify(director.Status);
                }
            }
            if (Time.time >= nextStatus) { nextStatus = Time.time + 0.25f; UpdateStatus(); }
        }

        private void Steer()
        {
            if (movement == null) return;
            if (player.AtHelm)
            {
                Vector2 input = player.HelmInput;
                sail = Mathf.Clamp(sail + input.y * SailChangePerSecond * Time.deltaTime, -0.35f, 1f);
                rudder = Mathf.MoveTowards(rudder, input.x, 3f * Time.deltaTime);
            }
            // Nobody at the wheel: the rudder centres, the sails keep their trim.
            else rudder = Mathf.MoveTowards(rudder, 0f, 1.5f * Time.deltaTime);
            movement.ExternalControl = true;
            movement.ExternalThrottle = sail;
            movement.ExternalRudder = rudder;
        }

        private void UpdateEnemies()
        {
            float battleTime = Time.time - startedAt;
            float ownHull = Fraction(flagship);
            foreach (Enemy enemy in enemies)
            {
                if (enemy.Crew == null || enemy.Actor == null) continue;
                // Nobody left aboard: the ship drifts; the guns fall silent with their crews.
                bool crewed = enemy.Crew.LivingAboard() > 0;
                if (enemy.AI != null)
                {
                    enemy.AI.Helmed = crewed;
                    // After an opening exchange (or once the player is hurt) the enemy closes to board.
                    bool board = battleTime > 35f || ownHull < 0.6f;
                    enemy.Crew.BoardingAllowed = board;
                    if (board) enemy.AI.SetEngagementRanges(2.6f, 6f);
                    else if (enemy.CloseRange > 0f) enemy.AI.SetEngagementRanges(enemy.CloseRange, enemy.ApproachRange);
                }
                // Going down: our boarders jump back before she takes them with her.
                if (!enemy.Crew.Afloat)
                {
                    foreach (BoatshipsDeckFighter sailor in own.Sailors)
                        if (sailor != null && sailor.Alive && !sailor.Jumping && sailor.Deck != null && sailor.Deck.Hull == enemy.Actor.Hull)
                            sailor.CrossTo(own.Deck, own.RailToward(enemy.Actor.transform.position));
                    continue;
                }
                // Taken: our side stands on her deck and none of her crew is left aboard.
                bool ours = (player.CurrentDeck != null && player.CurrentDeck.Hull == enemy.Actor.Hull) || HasOurSailorAboard(enemy.Actor.Hull);
                if (ours && !crewed && enemy.Crew.Afloat) Capture(enemy);
            }
            SyncEnemies(null);
        }

        private void Capture(Enemy enemy)
        {
            EncounterRecord record = Record(enemy.Id);
            if (record == null || record.captured || record.defeated) return;
            BoatshipsShipCrew crew = enemy.Crew;
            // Bring everyone home before the prize falls in behind the flagship.
            if (player.CurrentDeck != null && player.CurrentDeck.Hull == enemy.Actor.Hull)
                player.TransferToDeck(flagship.Hull, own.RailToward(enemy.Actor.transform.position));
            foreach (BoatshipsDeckFighter sailor in own.Sailors)
                if (sailor != null && sailor.Alive && sailor.Deck != null && sailor.Deck.Hull == enemy.Actor.Hull)
                    sailor.CrossTo(own.Deck, own.RailToward(enemy.Actor.transform.position));
            DropEnemy(enemy);
            if (!director.Capture(record, enemy.Actor)) { travel.Notify("The prize could not be secured."); return; }
            captures++;
            if (character != null) character.AddExperience(CharacterSkill.Leadership, 15);
            travel.Notify("The " + enemy.Name + " strikes her colours — she's yours!");
        }

        /// <summary>Thrown clear of a sinking enemy deck: dragged back aboard our own ship.</summary>
        private void KeepPlayerAboard()
        {
            DeckSpace deck = player.CurrentDeck;
            if (deck == null || player.KnockedOut) return;
            if (deck.Valid && deck.Hull == flagship.Hull) return;
            foreach (Enemy enemy in enemies)
                if (enemy.Actor != null && deck.Valid && deck.Hull == enemy.Actor.Hull && enemy.Crew != null && enemy.Crew.Afloat) return;
            player.TransferToDeck(flagship.Hull, own.Deck.Center);
            travel.Notify("You were hauled back aboard your own ship.");
        }

        private void OnDowned()
        {
            if (!Active) return;
            player.KnockOut(KnockOutSeconds, () =>
            {
                if (Active && flagship != null && own != null) player.TransferToDeck(flagship.Hull, own.Deck.Center);
            });
        }

        private void UpdateStatus()
        {
            float hull = Fraction(flagship);
            int open = 0, leaking = 0;
            own.GetBreaches(breachScratch);
            foreach (ShipFlooding.Breach breach in breachScratch)
            {
                if (breach.Patched) continue;
                open++;
                if (breach.Submerged) leaking++;
            }
            BoatshipsShipCrew target = own.NearestOpponent(out float gap);
            string enemy = target != null
                ? "Enemy " + Mathf.RoundToInt(gap) + "m — hull " + Mathf.RoundToInt(Fraction(target.Ship) * 100f) + "%, crew " + target.LivingAboard()
                : enemies.Count == 0 ? "No enemy afloat" + (captures > 0 ? " — " + captures + " prize(s) taken" : "") : "";
            string sails = sail <= -0.05f ? "backing" : sail < 0.05f ? "furled" : sail < 0.6f ? "half sail" : "full sail";
            player.SetStatus("Hull " + Mathf.RoundToInt(hull * 100f) + "%   Water " + Mathf.RoundToInt(own.Water * 100f / 0.38f) +
                "%   Leaks " + leaking + (open > leaking ? " (+" + (open - leaking) + " above water)" : "") +
                "   Planks " + own.PlanksLeft + "   Sails: " + sails +
                "\n" + enemy + (travel.HasMessage ? "\n" + travel.Message : ""));
        }

        // ───────────────────────────── Helpers ─────────────────────────────

        private bool HasOurSailorAboard(VoxelHull hull)
        {
            foreach (BoatshipsDeckFighter sailor in own.Sailors)
                if (sailor != null && sailor.Alive && sailor.Deck != null && sailor.Deck.Hull == hull) return true;
            return false;
        }

        private float Gap(ShipCombatTarget other) =>
            other == null || flagship == null ? float.PositiveInfinity :
            Vector2.Distance(new Vector2(other.transform.position.x, other.transform.position.z),
                new Vector2(flagship.transform.position.x, flagship.transform.position.z));

        private EncounterRecord Record(string id) =>
            economy != null ? economy.Campaign.encounters.Find(r => r.id == id) : null;

        private bool IsResolved(string id)
        {
            EncounterRecord record = Record(id);
            return record == null || record.defeated || record.captured;
        }

        private static float Fraction(ShipCombatTarget ship) =>
            ship != null && ship.TotalVoxels > 0 ? (float)ship.RemainingVoxels / ship.TotalVoxels : 0f;
    }
}
