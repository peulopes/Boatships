using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Boatships
{
    [Serializable]
    public sealed class OwnedCity
    {
        public Vector2Int cell;
        public int formerFaction;
        public int garrison;
        public int walls, market, docks;
        public int project = -1;          // building under construction (0 walls, 1 market, 2 docks)
        public float projectDoneHours;
        public string besiegerId;         // enemy war fleet blockading the city
        public int besiegerTier;
        public float besiegedSince = -1f;
    }

    [Serializable]
    public sealed class CityDefence
    {
        public Vector2Int cell;
        public int garrison;
        public float fort = 100f;
    }

    [Serializable]
    public sealed class ConquestState
    {
        public int version = 1;
        public bool siegeActive;
        public Vector2Int siegeCell;
        public float siegeStartedHours;
        public List<OwnedCity> cities = new List<OwnedCity>();
        public List<CityDefence> defences = new List<CityDefence>();
        public List<int> wars = new List<int>();
        public float nextCounterHours = -1f;
        public int lastDay;
        public float lastHours;
    }

    /// <summary>Sieges and conquest, after Bannerlord: blockade a port to starve its garrison, bombard its harbour
    /// fort from the sea (a first-person sea battle against its guard ships), then storm the town in first
    /// person to take the square. Conquered ports pay taxes, keep a garrison, can be improved, fly the player's
    /// colours and draw counter-sieges from the faction that lost them.</summary>
    public sealed class BoatshipsConquest : MonoBehaviour
    {
        private const int MaxGarrison = 40;
        private const float CounterSiegeHours = 48f;
        private static readonly string[] Buildings = { "Walls", "Market", "Docks" };

        private ArchipelagoWorld world;
        private PortEconomy economy;
        private BoatshipsCharacter character;
        private BoatshipsTravelController travel;
        private BoatshipsWorldMapView view;
        private BoatshipsFirstPerson player;
        private BoatshipsCrewManager crewManager;
        private ShipCombatTarget flagship;

        private ConquestState state;
        private int loadedSeed = int.MinValue;
        private bool loadFailed;
        private readonly System.Random random = new System.Random(Environment.TickCount);
        private float nextSlowTick;
        private bool skipOverrideOnce;

        // Bombardment and assault in progress (not saved: they end when the player leaves the scene).
        private bool bombarding;
        private Vector2Int bombardCell;
        private HarbourFort bombardFort;
        private Assault assault;

        private sealed class Assault
        {
            public ArchipelagoWorld.IslandSite Site;
            public BoatshipsTown Town;
            public Transform Root;
            public int Reserve;
            public readonly List<BoatshipsGroundFighter> Defenders = new List<BoatshipsGroundFighter>();
            public readonly List<BoatshipsGroundFighter> Attackers = new List<BoatshipsGroundFighter>();
            public float Tier;
            public bool Over;
        }

        public bool Ready => state != null && world != null && loadedSeed == world.Seed;

        public void Configure(ArchipelagoWorld map, PortEconomy account, BoatshipsCharacter captain, BoatshipsTravelController controller,
            BoatshipsWorldMapView mapView, BoatshipsFirstPerson fps, BoatshipsCrewManager deckCrew, Transform ship)
        {
            world = map; economy = account; character = captain; travel = controller; view = mapView; player = fps; crewManager = deckCrew;
            flagship = ship != null ? ship.GetComponent<ShipCombatTarget>() : null;
            BoatshipsWorldMapView.PortColorOverride = cell => Owns(cell) && character != null && character.Profile != null ?
                character.Profile.flagColor : (Color?)null;
            PortEconomy.RecruitCostMultiplier = cell => Owns(cell) ? 0.7f : 1f;
            BoatshipsProjectile.ShotImpacted += OnShotImpacted;
            player.Downed += OnDowned;
        }

        private void OnEnable() { BoatshipsSaveCoordinator.CheckpointCapturing += Save; }
        private void OnDisable() { BoatshipsSaveCoordinator.CheckpointCapturing -= Save; }

        private void OnDestroy()
        {
            BoatshipsProjectile.ShotImpacted -= OnShotImpacted;
            if (player != null) player.Downed -= OnDowned;
            BoatshipsWorldMapView.PortColorOverride = null;
            PortEconomy.RecruitCostMultiplier = null;
        }

        // ───────────────────────────── Factions ─────────────────────────────

        /// <summary>Port faction (0 Merchant League, 1 Royal Navy, 2 Free Ports) of a fleet faction.</summary>
        public static int PortFactionOf(MapFaction faction) =>
            faction == MapFaction.Merchant ? 0 : faction == MapFaction.Navy ? 1 : 2;

        public static MapFaction MapFactionOf(int portFaction) =>
            portFaction == 0 ? MapFaction.Merchant : portFaction == 1 ? MapFaction.Navy : MapFaction.Pirate;

        public bool Owns(Vector2Int cell) => City(cell) != null;
        public bool AtWar(int portFaction) => Ready && state.wars.Contains(portFaction);
        // Free Ports field no fleets of their own; the pirates are hostile regardless.
        public bool AtWarWith(MapFaction faction) => faction != MapFaction.Pirate && AtWar(PortFactionOf(faction));

        private OwnedCity City(Vector2Int cell)
        {
            if (!Ready) return null;
            foreach (OwnedCity city in state.cities) if (city.cell == cell) return city;
            return null;
        }

        private CityDefence Defence(ArchipelagoWorld.IslandSite site)
        {
            foreach (CityDefence defence in state.defences) if (defence.cell == site.Cell) return defence;
            // A port's garrison depends on who holds it; the Navy keeps strong ones.
            int faction = BoatshipsFactions.OfPort(site.Cell);
            var created = new CityDefence { cell = site.Cell, garrison = 10 + (int)((uint)site.DetailSeed % 8u) + (faction == 1 ? 6 : 0) };
            state.defences.Add(created);
            return created;
        }

        // ───────────────────────────── Persistence ─────────────────────────────

        private void Update()
        {
            if (world == null || !BoatshipsSaveStore.Enabled) return;
            if (loadedSeed != world.Seed) Load();
            if (state == null && !loadFailed && character != null && character.HasCharacter && travel.Live)
            {
                state = new ConquestState { lastDay = travel.Day, lastHours = travel.Hours };
                Save();
            }
            if (!Ready || !travel.Live) return;
            if (travel.Day != state.lastDay) { state.lastDay = travel.Day; Daily(); }
            if (assault != null) UpdateAssault();
            if (Time.unscaledTime < nextSlowTick) return;
            nextSlowTick = Time.unscaledTime + 1f;
            Hourly();
            KeepBlockade();
            ApplyFortsAndBanners();
        }

        private void Load()
        {
            loadedSeed = world.Seed;
            state = null;
            loadFailed = false;
            string path = SavePath(loadedSeed);
            if (!BoatshipsSaveStore.Exists(path)) return;
            try
            {
                ConquestState loaded = JsonUtility.FromJson<ConquestState>(BoatshipsSaveStore.Read(path));
                if (loaded == null || loaded.version != 1) throw new InvalidDataException("Unsupported conquest version.");
                if (loaded.cities == null) loaded.cities = new List<OwnedCity>();
                if (loaded.defences == null) loaded.defences = new List<CityDefence>();
                if (loaded.wars == null) loaded.wars = new List<int>();
                state = loaded;
                view.RefreshIslandMarkers();
            }
            catch (Exception ex)
            {
                loadFailed = true;
                Debug.LogWarning("Boatships conquest state could not be loaded: " + ex.Message, this);
            }
        }

        public void Save()
        {
            if (state == null || loadFailed || world == null || loadedSeed != world.Seed) return;
            try { BoatshipsSaveStore.Write(SavePath(loadedSeed), JsonUtility.ToJson(state)); }
            catch (Exception ex) { Debug.LogWarning("Boatships conquest save failed: " + ex.Message, this); }
        }

        private static string SavePath(int seed) =>
            Path.Combine(Application.persistentDataPath, "boatships_conquest_v1_" + seed + ".json");

        // ───────────────────────────── Time ─────────────────────────────

        private void Daily()
        {
            // Taxes and wages.
            if (state.cities.Count > 0)
            {
                int total = 0;
                foreach (OwnedCity city in state.cities) total += Income(city);
                economy.TryAdjustGold(total);
                travel.Notify("Your " + state.cities.Count + " port(s) paid " + total + " gold in taxes after garrison wages.");
            }
            // The besieged garrison goes hungry while the blockade holds.
            if (state.siegeActive && world.TryGetIsland(state.siegeCell, out ArchipelagoWorld.IslandSite besieged))
            {
                CityDefence defence = Defence(besieged);
                defence.garrison = Mathf.Max(0, defence.garrison - Mathf.Max(1, defence.garrison / 8));
                if (defence.garrison == 0) { travel.Notify("Starving, the garrison of " + BoatshipsNames.Island(besieged) + " surrenders!"); TakeCity(besieged, 3); }
            }
            // The factions you are at war with send fleets to win back what they lost.
            if (state.wars.Count > 0 && state.cities.Count > 0)
            {
                if (state.nextCounterHours < 0f) state.nextCounterHours = travel.Hours + 24f * (3 + random.Next(3));
                if (travel.Hours >= state.nextCounterHours) LaunchCounterSiege();
            }
            Save();
        }

        private int Income(OwnedCity city) => 40 + city.market * 30 + city.docks * 10 - city.garrison;

        private void Hourly()
        {
            float now = travel.Hours;
            foreach (OwnedCity city in state.cities.ToArray())
            {
                if (city.project >= 0 && now >= city.projectDoneHours)
                {
                    if (city.project == 0) city.walls++; else if (city.project == 1) city.market++; else city.docks++;
                    travel.Notify(Buildings[city.project] + " of " + Name(city.cell) + " completed.");
                    city.project = -1;
                    Save();
                }
                if (string.IsNullOrEmpty(city.besiegerId)) continue;
                if (!travel.HasParty(city.besiegerId))
                {
                    city.besiegerId = null; city.besiegedSince = -1f;
                    travel.Notify("The war fleet besieging " + Name(city.cell) + " is gone. The siege is broken!");
                    Save();
                }
                else if (city.besiegedSince >= 0f && now - city.besiegedSince >= CounterSiegeHours) ResolveCounterSiege(city);
            }
        }

        private void LaunchCounterSiege()
        {
            state.nextCounterHours = travel.Hours + 24f * (5 + random.Next(4));
            var targets = state.cities.FindAll(c => string.IsNullOrEmpty(c.besiegerId) && state.wars.Contains(c.formerFaction));
            if (targets.Count == 0) targets = state.cities.FindAll(c => string.IsNullOrEmpty(c.besiegerId));
            if (targets.Count == 0) return;
            OwnedCity city = targets[random.Next(targets.Count)];
            int faction = state.wars.Contains(city.formerFaction) ? city.formerFaction : state.wars[random.Next(state.wars.Count)];
            if (faction == 2) faction = city.formerFaction == 2 ? 1 : city.formerFaction; // the Free Ports hire Navy deserters
            int tier = Mathf.Clamp(2 + state.cities.Count / 2 + random.Next(2), 2, 5);
            string id = travel.SpawnSiegeFleet(MapFactionOf(faction), city.cell, tier);
            if (id == null) return;
            city.besiegerId = id; city.besiegerTier = tier; city.besiegedSince = -1f;
            travel.Notify("A " + BoatshipsFactions.Name(faction) + " war fleet sails to besiege " + Name(city.cell) + "!");
            Save();
        }

        /// <summary>Called by the map when a siege fleet reaches its target city.</summary>
        public void OnSiegeFleetArrived(MapParty party)
        {
            if (!Ready) return;
            foreach (OwnedCity city in state.cities)
            {
                if (city.besiegerId != party.id || city.besiegedSince >= 0f) continue;
                city.besiegedSince = travel.Hours;
                travel.Notify(Name(city.cell) + " is under siege! Sink the blockading fleet within two days, or the garrison must hold alone.");
                Save();
            }
        }

        private void ResolveCounterSiege(OwnedCity city)
        {
            float defence = city.garrison * (1f + city.walls * 0.5f) + 10f;
            float attack = city.besiegerTier * 14f + 10f;
            travel.RemoveParty(city.besiegerId);
            city.besiegerId = null; city.besiegedSince = -1f;
            if (attack > defence)
            {
                state.cities.Remove(city);
                state.defences.RemoveAll(d => d.cell == city.cell);
                state.defences.Add(new CityDefence { cell = city.cell, garrison = 8 + city.besiegerTier * 2, fort = 100f });
                travel.Notify(Name(city.cell) + " has fallen! Its garrison was overwhelmed.");
                view.RefreshIslandMarkers();
            }
            else
            {
                city.garrison = Mathf.Max(1, city.garrison - Mathf.RoundToInt(attack / 4f));
                travel.Notify("The garrison of " + Name(city.cell) + " held the walls. The enemy fleet withdrew.");
            }
            Save();
        }

        /// <summary>The blockade only holds while the fleet stays off the port.</summary>
        private void KeepBlockade()
        {
            if (!state.siegeActive || travel.CurrentMode != TravelMode.Map || travel.OnJob) return;
            if (!world.TryGetIsland(state.siegeCell, out ArchipelagoWorld.IslandSite site)) { state.siegeActive = false; return; }
            if (Vector2.Distance(travel.Position, site.Center) <= site.Radius * 0.78f + 55f) return;
            state.siegeActive = false;
            travel.Notify("You sailed away: the siege of " + BoatshipsNames.Island(site) + " is lifted.");
            Save();
        }

        // ───────────────────────────── Menus ─────────────────────────────

        /// <summary>Replaces the town menu while besieging the port, when its gates are closed to us, or when it is under attack.</summary>
        public bool OverrideTownMenu(ArchipelagoWorld.IslandSite site, string prefix, Action back)
        {
            if (!Ready || !travel.Captain) return false;
            if (skipOverrideOnce) { skipOverrideOnce = false; return false; }
            if (state.siegeActive && state.siegeCell == site.Cell) { SiegeMenu(site, prefix); return true; }
            OwnedCity owned = City(site.Cell);
            if (owned != null && !string.IsNullOrEmpty(owned.besiegerId) && owned.besiegedSince >= 0f) { RelieveMenu(site, owned, prefix, back); return true; }
            int faction = BoatshipsFactions.OfPort(site.Cell);
            if (owned == null && AtWar(faction)) { EnemyPortMenu(site, prefix); return true; }
            return false;
        }

        public void AddTownOptions(ArchipelagoWorld.IslandSite site, List<MapMenuOption> options, Action back)
        {
            if (!Ready || !travel.Captain) return;
            OwnedCity owned = City(site.Cell);
            if (owned != null)
            {
                options.Insert(0, new MapMenuOption("Govern " + Name(site.Cell) + " (garrison " + owned.garrison + ", +" + Income(owned) + " gold a day)",
                    () => GovernMenu(site, back, null)));
                return;
            }
            int faction = BoatshipsFactions.OfPort(site.Cell);
            options.Add(new MapMenuOption("Lay siege to " + Name(site.Cell) + " (war with the " + BoatshipsFactions.Name(faction) + ")",
                () => ConfirmSiege(site, back), !state.siegeActive));
        }

        private void ConfirmSiege(ArchipelagoWorld.IslandSite site, Action back)
        {
            int faction = BoatshipsFactions.OfPort(site.Cell);
            CityDefence defence = Defence(site);
            var options = new List<MapMenuOption>
            {
                new MapMenuOption("Blockade the harbour and begin the siege", () => BeginSiege(site)),
                new MapMenuOption("Think better of it", back)
            };
            view.ShowMenu("Lay siege to " + Name(site.Cell),
                "This means war with the " + BoatshipsFactions.Name(faction) + ": their ships will hunt you and their ports will close to you.\n\n" +
                "Garrison about " + defence.garrison + " soldiers. Harbour fort " + Mathf.RoundToInt(defence.fort) + "%.\n" +
                "Hold the blockade to starve them, bombard the fort from the sea, then storm the town.", options);
        }

        private void BeginSiege(ArchipelagoWorld.IslandSite site)
        {
            int faction = BoatshipsFactions.OfPort(site.Cell);
            DeclareWar(faction);
            state.siegeActive = true;
            state.siegeCell = site.Cell;
            state.siegeStartedHours = travel.Hours;
            Save();
            SiegeMenu(site, "Your ships close the harbour mouth. The siege of " + Name(site.Cell) + " has begun.");
        }

        private void DeclareWar(int faction)
        {
            if (state.wars.Contains(faction)) return;
            state.wars.Add(faction);
            economy.TryCampaignChange(progress => BoatshipsFactions.AddReputation(progress, faction, -40));
            travel.Notify("You are now at war with the " + BoatshipsFactions.Name(faction) + ".");
        }

        private void SiegeMenu(ArchipelagoWorld.IslandSite site, string prefix)
        {
            CityDefence defence = Defence(site);
            float days = (travel.Hours - state.siegeStartedHours) / 24f;
            bool breach = defence.fort <= 0f || defence.garrison <= 6;
            var options = new List<MapMenuOption>
            {
                new MapMenuOption(defence.fort > 0f ? "Bombard the harbour fort (sea battle with its guard ships)" : "Sink the harbour guard (sea battle)",
                    () => StartBombardment(site)),
                new MapMenuOption(breach ? "Storm the town (first-person assault)" : "Storm the town — silence the fort first",
                    () => StartAssault(site), breach),
                new MapMenuOption("Demand their surrender", () => DemandSurrender(site)),
                new MapMenuOption("Hold the blockade (let them starve)", null),
                new MapMenuOption("Lift the siege", () => { state.siegeActive = false; Save(); travel.Notify("You lift the siege of " + Name(site.Cell) + "."); })
            };
            view.ShowMenu("Siege of " + Name(site.Cell),
                (string.IsNullOrEmpty(prefix) ? "" : prefix + "\n\n") +
                "Day " + Mathf.FloorToInt(days + 1f) + " of the siege. Garrison " + defence.garrison + ". Harbour fort " + Mathf.RoundToInt(defence.fort) + "%.\n" +
                "Each day of blockade the garrison loses men to hunger. Sailing away lifts the siege.\n" +
                "Rations " + economy.Rations + "   Crew " + economy.Crew.Count, options);
        }

        private void DemandSurrender(ArchipelagoWorld.IslandSite site)
        {
            CityDefence defence = Defence(site);
            int leadership = character != null ? character.Skill(CharacterSkill.Leadership) : 0;
            float odds = (defence.fort <= 0f ? 0.35f : 0.05f) + leadership * 0.04f + Mathf.Clamp01((10f - defence.garrison) / 10f) * 0.5f;
            if (random.NextDouble() < odds) { travel.Notify("The governor of " + Name(site.Cell) + " hands over the keys."); TakeCity(site, Mathf.Max(3, defence.garrison / 2)); return; }
            SiegeMenu(site, "The governor laughs at your terms.");
        }

        private void EnemyPortMenu(ArchipelagoWorld.IslandSite site, string prefix)
        {
            int faction = BoatshipsFactions.OfPort(site.Cell);
            int price = PeacePrice(faction);
            var options = new List<MapMenuOption>
            {
                new MapMenuOption("Lay siege to " + Name(site.Cell), () => BeginSiege(site), !state.siegeActive),
                new MapMenuOption("Send an envoy: make peace with the " + BoatshipsFactions.Name(faction) + " (" + price + " gold)",
                    () => MakePeace(faction, site), economy.Gold >= price && !state.siegeActive),
                new MapMenuOption("Leave", null)
            };
            view.ShowMenu(Name(site.Cell), (string.IsNullOrEmpty(prefix) ? "" : prefix + "\n\n") +
                "The harbour chain is up and the fort's guns are run out. You are at war with the " + BoatshipsFactions.Name(faction) +
                ": there is no trade or rest for you here.", options);
        }

        private int PeacePrice(int faction)
        {
            int taken = state.cities.FindAll(c => c.formerFaction == faction).Count;
            return 300 + taken * 200;
        }

        private void MakePeace(int faction, ArchipelagoWorld.IslandSite site)
        {
            if (!economy.TryAdjustGold(-PeacePrice(faction))) return;
            state.wars.Remove(faction);
            // Their war fleets go home.
            foreach (OwnedCity city in state.cities)
            {
                if (string.IsNullOrEmpty(city.besiegerId)) continue;
                travel.RemoveParty(city.besiegerId);
                city.besiegerId = null; city.besiegedSince = -1f;
            }
            economy.TryCampaignChange(progress => BoatshipsFactions.AddReputation(progress, faction, 15));
            Save();
            travel.Notify("Peace with the " + BoatshipsFactions.Name(faction) + ". They grudgingly accept your conquests.");
        }

        private void RelieveMenu(ArchipelagoWorld.IslandSite site, OwnedCity city, string prefix, Action back)
        {
            float left = Mathf.Max(0f, CounterSiegeHours - (travel.Hours - city.besiegedSince));
            var options = new List<MapMenuOption>
            {
                new MapMenuOption("Attack the blockading fleet", () =>
                {
                    MapFaction faction = MapFactionOf(city.formerFaction);
                    string fleet = city.besiegerId;
                    travel.BeginPortBattle(site, travel.FleetOf(faction, city.besiegerTier), city.besiegerTier, () =>
                    {
                        travel.RemoveParty(fleet);
                        city.besiegerId = null; city.besiegedSince = -1f;
                        Save();
                        travel.Notify("The siege of " + Name(site.Cell) + " is broken!");
                    });
                }),
                new MapMenuOption("Slip into the town", () => { skipOverrideOnce = true; back?.Invoke(); }),
                new MapMenuOption("Leave", null)
            };
            view.ShowMenu(Name(site.Cell) + " under siege", (string.IsNullOrEmpty(prefix) ? "" : prefix + "\n\n") +
                "A war fleet blockades your port. The garrison (" + city.garrison + ", walls " + city.walls + ") can hold about " +
                Mathf.CeilToInt(left) + " more hours before it must fight alone.", options);
        }

        private void GovernMenu(ArchipelagoWorld.IslandSite site, Action back, string message)
        {
            OwnedCity city = City(site.Cell);
            if (city == null) { back?.Invoke(); return; }
            var options = new List<MapMenuOption>
            {
                new MapMenuOption("Hire 5 soldiers for the garrison — 60 gold", () =>
                {
                    if (economy.TryAdjustGold(-60)) { city.garrison = Mathf.Min(MaxGarrison, city.garrison + 5); Save(); }
                    GovernMenu(site, back, null);
                }, economy.Gold >= 60 && city.garrison < MaxGarrison),
                new MapMenuOption("Dismiss 5 soldiers (saves 5 gold a day)", () => { city.garrison = Mathf.Max(0, city.garrison - 5); Save(); GovernMenu(site, back, null); },
                    city.garrison > 0)
            };
            int[] levels = { city.walls, city.market, city.docks };
            string[] effects = { "stronger defence against sieges", "+30 gold a day", "+10 gold a day, cheaper hull repairs" };
            for (int i = 0; i < Buildings.Length; i++)
            {
                int building = i;
                int level = levels[i];
                int gold = (i == 0 ? 200 : i == 1 ? 180 : 150) * (level + 1);
                int wood = (i == 0 ? 10 : i == 1 ? 8 : 12) + level * 4;
                int iron = (i == 0 ? 6 : i == 1 ? 2 : 3) + level * 2;
                int daysToBuild = 2 + level;
                bool can = city.project < 0 && level < 3 && economy.Gold >= gold && economy.Wood >= wood && economy.Iron >= iron;
                options.Add(new MapMenuOption(level >= 3 ? Buildings[i] + " — fully built" :
                    "Build " + Buildings[i] + " " + (level + 1) + " — " + gold + " gold, " + wood + " wood, " + iron + " iron, " + daysToBuild + " days (" + effects[i] + ")",
                    () =>
                    {
                        bool ok = economy.TrySpend(gold, wood, iron, () => { });
                        if (ok) { city.project = building; city.projectDoneHours = travel.Hours + daysToBuild * 24f; Save(); }
                        GovernMenu(site, back, ok ? "Builders hired. Work on the " + Buildings[building].ToLowerInvariant() + " begins." : "You cannot afford that.");
                    }, can));
            }
            options.Add(new MapMenuOption("Back", back));
            string project = city.project >= 0 ? "Building: " + Buildings[city.project] + ", " +
                Mathf.CeilToInt(Mathf.Max(0f, city.projectDoneHours - travel.Hours) / 24f) + " day(s) left.\n" : "";
            view.ShowMenu("Govern " + Name(site.Cell), (message != null ? message + "\n\n" : "") +
                "Garrison " + city.garrison + " (1 gold a day each). Walls " + city.walls + "   Market " + city.market + "   Docks " + city.docks + "\n" +
                project + "Daily income after wages: " + Income(city) + " gold. Recruits here cost 30% less.\n" +
                (state.wars.Contains(city.formerFaction) ? "The " + BoatshipsFactions.Name(city.formerFaction) + " want it back." : ""), options);
        }

        private string Name(Vector2Int cell) =>
            world.TryGetIsland(cell, out ArchipelagoWorld.IslandSite site) ? BoatshipsNames.Island(site) : "the port";

        // ───────────────────────────── Bombardment ─────────────────────────────

        private void StartBombardment(ArchipelagoWorld.IslandSite site)
        {
            CityDefence defence = Defence(site);
            int faction = BoatshipsFactions.OfPort(site.Cell);
            int tier = Mathf.Clamp(2 + defence.garrison / 10 + (faction == 1 ? 1 : 0), 2, 5);
            var guard = new List<EncounterKind> { EncounterKind.Patrol };
            if (defence.garrison >= 16) guard.Add(EncounterKind.Patrol);
            if (!travel.BeginPortBattle(site, guard, tier, () =>
            {
                defence.garrison = Mathf.Max(0, defence.garrison - 3);
                Save();
                travel.Notify("The harbour guard is sunk" + (defence.fort > 0f ? ". Keep pounding the fort!" : ". The way into the harbour is open."));
            })) return;
            bombarding = true;
            bombardCell = site.Cell;
            bombardFort = null;
            travel.Notify("Bombard the harbour fort with your deck guns while fighting off its guard ships.");
        }

        private void OnShotImpacted(ShipCombatTarget shooter, Vector3 point)
        {
            if (!Ready || !bombarding || shooter == null || shooter != flagship || bombardFort == null) return;
            if (travel.CurrentMode != TravelMode.Battle) return;
            if (!world.TryGetIsland(bombardCell, out ArchipelagoWorld.IslandSite site)) return;
            Vector3 fort = bombardFort.transform.position;
            if (new Vector2(point.x - fort.x, point.z - fort.z).sqrMagnitude > 6f * 6f) return;
            CityDefence defence = Defence(site);
            if (defence.fort <= 0f) return;
            defence.fort = Mathf.Max(0f, defence.fort - 6f);
            if (defence.fort <= 0f)
            {
                bombardFort.enabled = false;
                travel.Notify("The fort's guns fall silent! Its walls are breached.");
                Save();
            }
            else if (random.NextDouble() < 0.3) travel.Notify("Hit on the fort! (" + Mathf.RoundToInt(defence.fort) + "% left)");
        }

        // ───────────────────────────── Forts and banners ─────────────────────────────

        private void ApplyFortsAndBanners()
        {
            if (bombarding && (travel.CurrentMode != TravelMode.Battle || !state.siegeActive)) bombarding = false;
            if (bombarding && bombardFort == null)
            {
                BoatshipsTown harbour = BoatshipsTown.For(bombardCell);
                bombardFort = harbour != null ? harbour.GetComponentInChildren<HarbourFort>(true) : null;
                if (bombardFort != null && bombardFort.GetComponent<Collider>() == null)
                {
                    // Solid stone for cannonballs to strike.
                    BoxCollider walls = bombardFort.gameObject.AddComponent<BoxCollider>();
                    walls.center = new Vector3(0f, 1.3f, 0f);
                    walls.size = new Vector3(6.6f, 3.6f, 6.6f);
                }
            }
            // Silenced forts stay silent while the siege lasts.
            if (state.siegeActive && world.TryGetIsland(state.siegeCell, out ArchipelagoWorld.IslandSite besieged) && Defence(besieged).fort <= 0f)
            {
                BoatshipsTown harbour = BoatshipsTown.For(besieged.Cell);
                HarbourFort fort = harbour != null ? harbour.GetComponentInChildren<HarbourFort>(true) : null;
                if (fort != null && fort.enabled) fort.enabled = false;
            }
            if (character == null || character.Profile == null) return;
            foreach (OwnedCity city in state.cities)
            {
                BoatshipsTown harbour = BoatshipsTown.For(city.cell);
                if (harbour == null || harbour.Root == null || harbour.Root.Find("Captain's Banner") != null) continue;
                RaiseBanner(harbour, character.Profile.flagColor);
            }
        }

        private void RaiseBanner(BoatshipsTown harbour, Color color)
        {
            if (!world.TryGetIsland(harbour.Cell, out ArchipelagoWorld.IslandSite site)) return;
            Vector2 spot = harbour.Plaza + (harbour.Plaza - harbour.Landing).normalized * 1.1f;
            float ground = BoatshipsTown.Ground(site, world.Seed, spot);
            GameObject banner = new GameObject("Captain's Banner");
            banner.transform.SetParent(harbour.Root, true);
            banner.transform.position = new Vector3(spot.x, ground, spot.y);
            BoatshipsHumanoid.Part(PrimitiveType.Cylinder, "Banner Pole", banner.transform, new Vector3(0f, 1.1f, 0f), new Vector3(0.05f, 1.1f, 0.05f), new Color(0.3f, 0.2f, 0.1f));
            BoatshipsHumanoid.Part(PrimitiveType.Cube, "Banner", banner.transform, new Vector3(0.32f, 1.85f, 0f), new Vector3(0.6f, 0.4f, 0.02f), color);
            BoatshipsHumanoid.Part(PrimitiveType.Sphere, "Finial", banner.transform, new Vector3(0f, 2.22f, 0f), Vector3.one * 0.08f, new Color(0.85f, 0.7f, 0.3f));
        }

        // ───────────────────────────── Assault ─────────────────────────────

        private void StartAssault(ArchipelagoWorld.IslandSite site)
        {
            CityDefence defence = Defence(site);
            BoatshipsTown harbour = travel.BeginTownAssault(site, () => EndAssault(false, "You sound the retreat and fall back to the ships."));
            if (harbour == null) { travel.Notify("Your ship cannot reach the town's pier."); return; }
            int faction = BoatshipsFactions.OfPort(site.Cell);
            assault = new Assault { Site = site, Town = harbour, Root = harbour.Root, Reserve = defence.garrison, Tier = 1f + defence.garrison / 12f };
            Color uniform = faction == 0 ? new Color(0.55f, 0.4f, 0.15f) : faction == 1 ? new Color(0.15f, 0.2f, 0.55f) : new Color(0.45f, 0.15f, 0.15f);
            // Defenders hold the square, the rest wait in the houses as reinforcements.
            for (int i = 0; i < 10 && assault.Reserve > 0; i++) SpawnDefender(uniform);
            // Your crew lands with you.
            var roster = new List<BoatshipsCrewManager.BattleSailor>();
            if (crewManager != null) crewManager.GetBattleRoster(roster);
            for (int i = 0; i < roster.Count && i < 8; i++)
            {
                Vector2 at = harbour.Landing + UnityEngine.Random.insideUnitCircle * 0.8f;
                var mate = BoatshipsGroundFighter.Spawn(roster[i].Name, 0, site, world.Seed, at, 60f + roster[i].Skill * 2f,
                    8f + (roster[i].Duty == CrewDuty.Fighter ? 4f : 0f), new Color(0.2f, 0.35f, 0.75f), HumanoidHat.Tricorn, new Color(0.12f, 0.12f, 0.16f), assault.Root);
                assault.Attackers.Add(mate);
            }
            player.SetStatus("Take the town square! Cut down the garrison.");
            travel.Notify("Storm the town: fight your way up from the pier to the square.");
        }

        private void SpawnDefender(Color uniform)
        {
            Vector2 at = assault.Town.Plaza + UnityEngine.Random.insideUnitCircle * 2.2f;
            BoatshipsGroundFighter defender = BoatshipsGroundFighter.Spawn("Garrison Soldier", 1, assault.Site, world.Seed, at,
                30f + assault.Tier * 8f, 6f + assault.Tier * 2f, uniform, HumanoidHat.Tricorn, new Color(0.1f, 0.1f, 0.12f), assault.Root);
            defender.Guard(assault.Town.Plaza, 6f);
            assault.Defenders.Add(defender);
            assault.Reserve--;
        }

        private void UpdateAssault()
        {
            if (assault.Over) return;
            if (travel.CurrentMode != TravelMode.Town || !player.Active) { EndAssault(false, null); return; }
            assault.Defenders.RemoveAll(d => d == null || !d.Alive);
            assault.Attackers.RemoveAll(a => a == null || !a.Alive);
            int faction = BoatshipsFactions.OfPort(assault.Site.Cell);
            Color uniform = faction == 0 ? new Color(0.55f, 0.4f, 0.15f) : faction == 1 ? new Color(0.15f, 0.2f, 0.55f) : new Color(0.45f, 0.15f, 0.15f);
            while (assault.Defenders.Count < 10 && assault.Reserve > 0) SpawnDefender(uniform);
            int remaining = assault.Defenders.Count + assault.Reserve;
            if (remaining == 0) { EndAssault(true, null); return; }
            if (player.KnockedOut && assault.Attackers.Count == 0) { EndAssault(false, "Your landing party is beaten back to the boats."); return; }
            player.SetStatus("Assault on " + BoatshipsNames.Island(assault.Site) + "   Defenders left " + remaining +
                "   Your crew " + assault.Attackers.Count + "\n[Left click] attack   [Right mouse] block   [H] bandage" + (travel.HasMessage ? "\n" + travel.Message : ""));
        }

        private void OnDowned()
        {
            if (assault == null || assault.Over) return;
            BoatshipsTown town = assault.Town;
            ArchipelagoWorld.IslandSite site = assault.Site;
            player.KnockOut(8f, () =>
            {
                if (assault == null || town == null) return;
                Vector2 landing = town.Landing;
                player.TeleportGround(new Vector3(landing.x, BoatshipsTown.Ground(site, world.Seed, landing) + 0.1f, landing.y), town.Facing);
            });
            travel.Notify("You are struck down! Your crew drags you back to the pier.");
        }

        private void EndAssault(bool won, string message)
        {
            if (assault == null || assault.Over) return;
            Assault ended = assault;
            ended.Over = true;
            int survivors = ended.Attackers.Count;
            int defenders = 0;
            foreach (BoatshipsGroundFighter d in ended.Defenders) if (d != null && d.Alive) defenders++;
            foreach (BoatshipsGroundFighter d in ended.Defenders) if (d != null) Destroy(d.gameObject);
            foreach (BoatshipsGroundFighter a in ended.Attackers) if (a != null) Destroy(a.gameObject);
            assault = null;
            if (won)
            {
                TakeCity(ended.Site, Mathf.Max(3, survivors));
                player.CombatMode = false;
                player.SetExit("Leave town", () => travel.LeaveTown());
                player.SetStatus(BoatshipsNames.Island(ended.Site) + " is yours!");
                return;
            }
            Defence(ended.Site).garrison = defenders + ended.Reserve;
            Save();
            if (!string.IsNullOrEmpty(message)) travel.Notify(message);
            travel.LeaveTown();
        }

        private void TakeCity(ArchipelagoWorld.IslandSite site, int garrison)
        {
            int faction = BoatshipsFactions.OfPort(site.Cell);
            DeclareWar(faction);
            state.siegeActive = false;
            state.defences.RemoveAll(d => d.cell == site.Cell);
            if (City(site.Cell) == null)
                state.cities.Add(new OwnedCity { cell = site.Cell, formerFaction = faction, garrison = Mathf.Clamp(garrison, 1, MaxGarrison) });
            economy.TryCampaignChange(progress => { BoatshipsFactions.AddReputation(progress, faction, -20); progress.reputation += 15; });
            if (character != null) character.AddExperience(CharacterSkill.Leadership, 40);
            HarbourFort fort = BoatshipsTown.For(site.Cell) != null ? BoatshipsTown.For(site.Cell).GetComponentInChildren<HarbourFort>(true) : null;
            if (fort != null) fort.enabled = true; // the fort now guards your harbour
            if (state.nextCounterHours < 0f) state.nextCounterHours = travel.Hours + 24f * 4f;
            Save();
            view.RefreshIslandMarkers();
            travel.Notify(BoatshipsNames.Island(site) + " is yours! Its people now pay their taxes to you.");
        }
    }
}
