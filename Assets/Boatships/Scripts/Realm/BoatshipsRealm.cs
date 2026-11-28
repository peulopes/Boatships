using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Boatships
{
    public enum LordStatus { AtSea, Captive, Resting, Gone }
    public enum RequestKind { Bounty, Escort, Smuggling, HuntLord }

    /// <summary>A named sea lord: an admiral, a League commodore or a pirate lord, sailing with a man-o'-war.</summary>
    [Serializable]
    public sealed class SeaLord
    {
        public string id;
        public string name;
        public string title;
        public string trait;
        public MapFaction faction;
        public LordStatus status;
        public string partyId;
        public float returnHours;
        public int relation;
        public int defeats;
        public string FullName => title + " " + name;
    }

    /// <summary>A request taken from a port's notables (Bannerlord's "issues").</summary>
    [Serializable]
    public sealed class RealmRequest
    {
        public string id;
        public RequestKind kind;
        public Vector2Int giver;
        public int giverFaction;
        public Vector2Int target;
        public string partyId;
        public string lordId;
        public string targetName;
        public int reward;
        public int renown;
        public float deadlineHours;
        public float startedHours;
        public bool ambushed;
    }

    [Serializable]
    public sealed class RealmState
    {
        public int version = 1;
        public List<SeaLord> lords = new List<SeaLord>();
        public List<RealmRequest> requests = new List<RealmRequest>();
        public List<Vector2Int> maps = new List<Vector2Int>();
        public List<Vector2Int> dug = new List<Vector2Int>();
        public List<string> takenOffers = new List<string>();
        public int lastTier;
        public int lastDay;
        public int serial;
        public string kingdom;
        public bool victory;
        public int lordsDefeated, requestsDone, treasuresFound;
    }

    /// <summary>
    /// The campaign's long game, after Mount &amp; Blade II: Bannerlord. Renown (the campaign's reputation score)
    /// raises your clan tier, which lets you lead more sailors and ships. Named sea lords roam with heavy fleets:
    /// beat one and they are your prisoner, to ransom, release or hand to the gallows. Port notables offer
    /// requests (bounties, escorts, smuggling). Treasure maps lead to buried hoards. Hold a port and enough
    /// renown and you can proclaim your own kingdom; rule half the archipelago's ports and the isles are yours.
    /// </summary>
    public sealed class BoatshipsRealm : MonoBehaviour
    {
        public static BoatshipsRealm Instance { get; private set; }

        private static readonly int[] TierRenown = { 0, 25, 75, 150, 300, 500, 800 };
        private static readonly string[] TierTitles =
            { "Unknown Captain", "Captain of Note", "Commodore", "Sea Lord", "Admiral of the Isles", "Grand Admiral", "Legend of the Isles" };
        private static readonly string[] Traits = { "honourable", "cruel", "cunning", "bold", "greedy" };
        private static readonly string[] Epithets = { "the Red", "Blackheart", "the Drowned", "Iron-Jaw", "the Gull", "Grinning", "Saltblood", "the Widowmaker" };
        private const int MaxRequests = 3;
        private const int MaxMaps = 3;
        private const int MapPrice = 90;

        private ArchipelagoWorld world;
        private PortEconomy economy;
        private BoatshipsCharacter character;
        private BoatshipsTravelController travel;
        private BoatshipsConquest conquest;
        private BoatshipsFirstPerson player;

        private RealmState state;
        private int loadedSeed = int.MinValue;
        private bool loadFailed;
        private readonly System.Random random = new System.Random(Environment.TickCount);
        private float nextSlowTick, nextHintAt;

        public bool Ready => state != null && world != null && loadedSeed == world.Seed;

        public void Configure(ArchipelagoWorld map, PortEconomy account, BoatshipsCharacter captain, BoatshipsTravelController controller,
            BoatshipsConquest wars, BoatshipsFirstPerson fps)
        {
            world = map; economy = account; character = captain; travel = controller; conquest = wars; player = fps;
            Instance = this;
            PortEconomy.CrewLimitRule = () => CrewLimitFor(Tier);
            PortEconomy.CompanyLimitRule = () => CompanyLimitFor(Tier);
            travel.PartyDefeated += OnPartyDefeated;
            travel.BattleWon += OnBattleWon;
            travel.HostilityOverride = Hostility;
            travel.FleetOverride = Fleet;
            travel.ArrivalHandler = OnArrival;
            travel.TownOptions = AddTownOptions;
            travel.IslandOptions = AddIslandOptions;
            if (conquest != null) conquest.CityTaken += OnCityTaken;
        }

        private void OnEnable() { BoatshipsSaveCoordinator.CheckpointCapturing += Save; }
        private void OnDisable() { BoatshipsSaveCoordinator.CheckpointCapturing -= Save; }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            PortEconomy.CrewLimitRule = null;
            PortEconomy.CompanyLimitRule = null;
            if (travel != null)
            {
                travel.PartyDefeated -= OnPartyDefeated;
                travel.BattleWon -= OnBattleWon;
                travel.HostilityOverride = null; travel.FleetOverride = null; travel.ArrivalHandler = null;
                travel.TownOptions = null; travel.IslandOptions = null;
            }
            if (conquest != null) conquest.CityTaken -= OnCityTaken;
        }

        // ───────────────────────────── Renown and clan tier ─────────────────────────────

        public int Renown => economy != null && economy.Campaign != null ? economy.Campaign.reputation : 0;

        public int Tier
        {
            get
            {
                int renown = Renown, tier = 0;
                for (int i = 0; i < TierRenown.Length; i++) if (renown >= TierRenown[i]) tier = i;
                return tier;
            }
        }

        public string Title => Ready && !string.IsNullOrEmpty(state.kingdom) ? "Sovereign of the " + state.kingdom : TierTitles[Tier];
        public static int CrewLimitFor(int tier) => Mathf.Max(24, 16 + tier * 4);
        public static int CompanyLimitFor(int tier) => 2 + tier;

        private void AddRenown(int amount)
        {
            if (amount <= 0) return;
            economy.TryCampaignChange(progress => progress.reputation += amount);
        }

        private void AddReputation(int portFaction, int amount) =>
            economy.TryCampaignChange(progress => BoatshipsFactions.AddReputation(progress, portFaction, amount));

        private void CheckTier()
        {
            int tier = Tier;
            if (tier <= state.lastTier) return;
            state.lastTier = tier;
            travel.Notify("Your clan rises to tier " + tier + ": " + TierTitles[tier] + "! You can now lead " + CrewLimitFor(tier) +
                " sailors and " + CompanyLimitFor(tier) + " ships." + (tier == 3 ? " Hold a port and you may proclaim a kingdom." : ""));
            Save();
        }

        // ───────────────────────────── Persistence and time ─────────────────────────────

        private void Update()
        {
            if (world == null || !BoatshipsSaveStore.Enabled) return;
            if (loadedSeed != world.Seed) Load();
            if (state == null && !loadFailed && character != null && character.HasCharacter && travel.Live)
            {
                state = new RealmState { lastTier = Tier, lastDay = travel.Day };
                CreateLords();
                Save();
            }
            if (!Ready || !travel.Live) return;
            ShowHints();
            if (Time.unscaledTime < nextSlowTick) return;
            nextSlowTick = Time.unscaledTime + 1f;
            if (travel.Day != state.lastDay) { state.lastDay = travel.Day; Daily(); }
            KeepLordsAtSea();
            CheckRequests();
            CheckTier();
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
                RealmState loaded = JsonUtility.FromJson<RealmState>(BoatshipsSaveStore.Read(path));
                if (loaded == null || loaded.version != 1) throw new InvalidDataException("Unsupported realm version.");
                if (loaded.lords == null) loaded.lords = new List<SeaLord>();
                if (loaded.requests == null) loaded.requests = new List<RealmRequest>();
                if (loaded.maps == null) loaded.maps = new List<Vector2Int>();
                if (loaded.dug == null) loaded.dug = new List<Vector2Int>();
                if (loaded.takenOffers == null) loaded.takenOffers = new List<string>();
                state = loaded;
            }
            catch (Exception ex)
            {
                loadFailed = true;
                Debug.LogWarning("Boatships realm state could not be loaded: " + ex.Message, this);
            }
        }

        public void Save()
        {
            if (state == null || loadFailed || world == null || loadedSeed != world.Seed) return;
            try { BoatshipsSaveStore.Write(SavePath(loadedSeed), JsonUtility.ToJson(state)); }
            catch (Exception ex) { Debug.LogWarning("Boatships realm save failed: " + ex.Message, this); }
        }

        private static string SavePath(int seed) => Path.Combine(Application.persistentDataPath, "boatships_realm_v1_" + seed + ".json");

        private void Daily()
        {
            // A sovereign's court spreads their fame.
            if (!string.IsNullOrEmpty(state.kingdom) && conquest != null) AddRenown(conquest.CityCount);
            CheckVictory();
            Save();
        }

        // ───────────────────────────── Sea lords ─────────────────────────────

        private void CreateLords()
        {
            AddLord(MapFaction.Navy); AddLord(MapFaction.Navy);
            AddLord(MapFaction.Merchant); AddLord(MapFaction.Merchant);
            AddLord(MapFaction.Pirate); AddLord(MapFaction.Pirate); AddLord(MapFaction.Pirate);
        }

        private SeaLord AddLord(MapFaction faction)
        {
            string name = BoatshipsNames.RandomCaptain(random);
            if (faction == MapFaction.Pirate) name += " " + Epithets[random.Next(Epithets.Length)];
            var lord = new SeaLord
            {
                id = "lord_" + state.serial++, name = name, faction = faction, trait = Traits[random.Next(Traits.Length)],
                title = faction == MapFaction.Navy ? "Admiral" : faction == MapFaction.Merchant ? "Commodore" : "Pirate Lord",
                status = LordStatus.AtSea
            };
            state.lords.Add(lord);
            return lord;
        }

        private SeaLord Lord(string id) => string.IsNullOrEmpty(id) ? null : state.lords.Find(l => l.id == id);

        /// <summary>Lords at sea keep a fleet on the map; resting lords return when their time is up; the hanged are replaced.</summary>
        private void KeepLordsAtSea()
        {
            float now = travel.Hours;
            foreach (SeaLord lord in state.lords.ToArray())
            {
                if (lord.status == LordStatus.Resting && now >= lord.returnHours) lord.status = LordStatus.AtSea;
                if (lord.status == LordStatus.Gone && now >= lord.returnHours)
                {
                    state.lords.Remove(lord);
                    SeaLord successor = AddLord(lord.faction);
                    travel.Notify("A new " + successor.title.ToLowerInvariant() + ", " + successor.name + ", has risen to take " + lord.name + "'s place.");
                    continue;
                }
                if (lord.status != LordStatus.AtSea || travel.HasParty(lord.partyId)) continue;
                if (!SpawnLordFleet(lord)) continue;
                Save();
            }
        }

        private bool SpawnLordFleet(SeaLord lord)
        {
            ArchipelagoWorld.IslandSite origin;
            if (lord.faction == MapFaction.Pirate)
            {
                if (!Pick(travel.IslandSites, s => s.Kind == IslandKind.PirateBaseSite, out origin) &&
                    !Pick(travel.IslandSites, s => s.Kind != IslandKind.PortSite, out origin)) return false;
            }
            else
            {
                int portFaction = BoatshipsConquest.PortFactionOf(lord.faction);
                if (!Pick(travel.PortSites, s => BoatshipsFactions.OfPort(s.Cell) == portFaction && (conquest == null || !conquest.Owns(s.Cell)), out origin) &&
                    !Pick(travel.PortSites, s => true, out origin)) return false;
            }
            int tier = lord.faction == MapFaction.Merchant ? 4 : 5;
            MapParty party = travel.SpawnPartyAt(lord.faction, tier, origin, origin.Cell, BoatshipsNames.Ship(random));
            if (party == null) return false;
            party.lordId = lord.id;
            party.captain = lord.FullName;
            party.speed *= 0.95f; // a fleet sails at the pace of its slowest ship
            lord.partyId = party.id;
            return true;
        }

        private bool Pick(IReadOnlyList<ArchipelagoWorld.IslandSite> sites, Predicate<ArchipelagoWorld.IslandSite> match, out ArchipelagoWorld.IslandSite site)
        {
            var pool = new List<ArchipelagoWorld.IslandSite>();
            foreach (ArchipelagoWorld.IslandSite s in sites) if (match(s)) pool.Add(s);
            site = pool.Count > 0 ? pool[random.Next(pool.Count)] : default;
            return pool.Count > 0;
        }

        private List<EncounterKind> Fleet(MapParty party)
        {
            if (!Ready || party == null) return null;
            if (!string.IsNullOrEmpty(party.lordId))
            {
                var kinds = new List<EncounterKind> { EncounterKind.ManOfWar };
                kinds.AddRange(travel.FleetOf(party.faction, party.tier));
                if (kinds.Count > 4) kinds.RemoveRange(4, kinds.Count - 4);
                return kinds;
            }
            RealmRequest request = RequestFor(party);
            if (request != null && request.kind == RequestKind.Bounty)
                return new List<EncounterKind> { EncounterKind.Brig, EncounterKind.Raider };
            return null;
        }

        private bool? Hostility(MapParty party)
        {
            if (!Ready || party == null) return null;
            SeaLord lord = Lord(party.lordId);
            // A pirate lord you once spared does not forget it.
            if (lord != null && lord.faction == MapFaction.Pirate && lord.relation >= 25) return false;
            if (RequestFor(party) != null && party.faction == MapFaction.Merchant) return false;
            // Carrying contraband, every navy cutter wants a look in your hold.
            if (party.faction == MapFaction.Navy && state.requests.Exists(r => r.kind == RequestKind.Smuggling)) return true;
            return null;
        }

        // ───────────────────────────── Victories ─────────────────────────────

        private void OnPartyDefeated(MapParty party)
        {
            if (!Ready) return;
            AddRenown(4 + party.tier * 3);
            SeaLord lord = Lord(party.lordId);
            if (lord != null)
            {
                lord.status = LordStatus.Captive;
                lord.partyId = null;
                lord.defeats++;
                state.lordsDefeated++;
                AddRenown(40);
                if (lord.faction == MapFaction.Pirate) { AddReputation(1, 8); AddReputation(0, 5); }
                else AddReputation(BoatshipsConquest.PortFactionOf(lord.faction), -10);
                travel.Notify(lord.FullName + " strikes their colours and is your prisoner! Ransom, release or hand them over at a port.");
                foreach (RealmRequest hunt in state.requests.FindAll(r => r.kind == RequestKind.HuntLord && r.lordId == lord.id)) Complete(hunt);
            }
            RealmRequest request = RequestFor(party);
            if (request != null)
            {
                if (request.kind == RequestKind.Bounty) Complete(request);
                // Beating the ambushers is the job; sinking the merchant herself is not.
                else if (request.kind == RequestKind.Escort && party.faction == MapFaction.Merchant)
                    Fail(request, "You attacked the very ship you were hired to protect.");
            }
            // Pirates sometimes carry old charts.
            if (party.faction == MapFaction.Pirate && random.NextDouble() < 0.25) GiveMap("Among the pirates' papers you find an old chart");
            Save();
        }

        private void OnBattleWon(MapParty party, bool raid)
        {
            if (!Ready || party != null) return; // party battles are handled in OnPartyDefeated
            AddRenown(raid ? 12 : 6);
            if (raid && random.NextDouble() < 0.4) GiveMap("In the haven's strongroom lies a treasure chart");
            Save();
        }

        private void OnCityTaken(ArchipelagoWorld.IslandSite site)
        {
            if (!Ready) return;
            AddRenown(40);
            CheckVictory();
            Save();
        }

        // ───────────────────────────── Requests ─────────────────────────────

        private struct Offer
        {
            public string Id;
            public RequestKind Kind;
            public Vector2Int Target;
            public string LordId;
            public int Reward, Renown;
            public float Days;
            public string Text;
        }

        private RealmRequest RequestFor(MapParty party) =>
            party == null || string.IsNullOrEmpty(party.questId) ? null : state.requests.Find(r => r.id == party.questId);

        /// <summary>This week's requests from a port's notables. The same port offers the same work all week.</summary>
        private List<Offer> OffersAt(ArchipelagoWorld.IslandSite port)
        {
            var offers = new List<Offer>();
            int week = (travel.Day - 1) / 7;
            var r = new System.Random(unchecked(world.Seed * 31 + port.Cell.x * 7919 + port.Cell.y * 104729 + week * 15485863));
            int faction = BoatshipsFactions.OfPort(port.Cell);
            int tier = Mathf.Clamp(1 + Tier, 2, 5);
            for (int i = 0; i < 2; i++)
            {
                RequestKind kind = faction == 0 ? (i == 0 ? RequestKind.Escort : RequestKind.Bounty) :
                    faction == 1 ? (i == 0 ? RequestKind.Bounty : RequestKind.HuntLord) :
                    (i == 0 ? RequestKind.Smuggling : RequestKind.Escort);
                var offer = new Offer { Id = "offer_" + port.Cell.x + "_" + port.Cell.y + "_" + week + "_" + i, Kind = kind };
                if (state.takenOffers.Contains(offer.Id)) continue;
                switch (kind)
                {
                    case RequestKind.Bounty:
                        offer.Reward = 120 + tier * 60; offer.Renown = 12; offer.Days = 12;
                        offer.Text = "The harbour master posts a bounty on a pirate captain raiding nearby waters";
                        break;
                    case RequestKind.HuntLord:
                        SeaLord lord = state.lords.Find(l => l.faction == MapFaction.Pirate && l.status == LordStatus.AtSea);
                        if (lord == null) continue;
                        offer.LordId = lord.id; offer.Reward = 550; offer.Renown = 20; offer.Days = 20;
                        offer.Text = "The governor will pay handsomely to see " + lord.FullName + " brought to justice";
                        break;
                    case RequestKind.Escort:
                    case RequestKind.Smuggling:
                        if (!FarPort(port, r, kind == RequestKind.Smuggling, out ArchipelagoWorld.IslandSite target)) continue;
                        offer.Target = target.Cell;
                        float days = travel.TravelDays(target.Center, 15f);
                        offer.Days = Mathf.Ceil(days * 1.8f) + 3f;
                        offer.Reward = Mathf.RoundToInt((kind == RequestKind.Escort ? 90f : 160f) + days * 45f) / 5 * 5;
                        offer.Renown = kind == RequestKind.Escort ? 10 : 6;
                        offer.Text = kind == RequestKind.Escort
                            ? "A League merchant needs an escort to " + BoatshipsNames.Island(target) + " — pirates have been sighted on the route"
                            : "A Free Ports trader wants a cargo run to " + BoatshipsNames.Island(target) + ", no questions asked (the Navy will hunt you)";
                        break;
                }
                offers.Add(offer);
            }
            return offers;
        }

        private bool FarPort(ArchipelagoWorld.IslandSite from, System.Random r, bool avoidNavy, out ArchipelagoWorld.IslandSite target)
        {
            var pool = new List<ArchipelagoWorld.IslandSite>();
            foreach (ArchipelagoWorld.IslandSite port in travel.PortSites)
            {
                float distance = Vector2.Distance(port.Center, from.Center);
                if (port.Cell == from.Cell || distance < 220f || distance > 800f) continue;
                if (avoidNavy && BoatshipsFactions.OfPort(port.Cell) == 1) continue;
                pool.Add(port);
            }
            target = pool.Count > 0 ? pool[r.Next(pool.Count)] : default;
            return pool.Count > 0;
        }

        private void RequestsMenu(ArchipelagoWorld.IslandSite port, Action back, string message)
        {
            var options = new List<MapMenuOption>();
            List<Offer> offers = OffersAt(port);
            bool room = state.requests.Count < MaxRequests;
            foreach (Offer offer in offers)
            {
                Offer chosen = offer;
                options.Add(new MapMenuOption(offer.Text + " — " + offer.Reward + " gold, " + offer.Days + " days",
                    () => { string result = Accept(port, chosen); RequestsMenu(port, back, result); }, room));
            }
            options.Add(new MapMenuOption("Back", back));
            int faction = BoatshipsFactions.OfPort(port.Cell);
            view().ShowMenu("Notables of " + BoatshipsNames.Island(port),
                (message != null ? message + "\n\n" : "") +
                (offers.Count == 0 ? "Nobody here has work for you this week." : "The " + BoatshipsFactions.Name(faction) + "'s people have work for a capable captain.") +
                "\nActive requests " + state.requests.Count + "/" + MaxRequests + ". New work is posted every week.", options);
        }

        private string Accept(ArchipelagoWorld.IslandSite port, Offer offer)
        {
            if (state.requests.Count >= MaxRequests) return "You already have as much work as you can handle.";
            var request = new RealmRequest
            {
                id = "request_" + state.serial++, kind = offer.Kind, giver = port.Cell, giverFaction = BoatshipsFactions.OfPort(port.Cell),
                target = offer.Target, lordId = offer.LordId, reward = offer.Reward, renown = offer.Renown,
                startedHours = travel.Hours, deadlineHours = travel.Hours + offer.Days * 24f
            };
            string result;
            switch (offer.Kind)
            {
                case RequestKind.Bounty:
                    if (!Pick(travel.IslandSites, s => s.Kind != IslandKind.PortSite && Vector2.Distance(s.Center, port.Center) is > 150f and < 480f, out ArchipelagoWorld.IslandSite lair) &&
                        !Pick(travel.IslandSites, s => s.Kind != IslandKind.PortSite, out lair)) return "Nobody knows where the pirate is hiding.";
                    MapParty pirate = travel.SpawnPartyAt(MapFaction.Pirate, Mathf.Clamp(2 + Tier, 2, 5), lair, lair.Cell);
                    if (pirate == null) return "The trail has gone cold.";
                    request.targetName = "Captain " + BoatshipsNames.RandomCaptain(random) + " " + Epithets[random.Next(Epithets.Length)];
                    pirate.captain = request.targetName; pirate.questId = request.id; request.partyId = pirate.id;
                    request.target = lair.Cell;
                    result = "Bounty taken: " + request.targetName + " was last seen near " + BoatshipsNames.Island(lair) + " (" + Bearing(lair.Center) + ").";
                    break;
                case RequestKind.HuntLord:
                    SeaLord lord = Lord(offer.LordId);
                    if (lord == null || lord.status != LordStatus.AtSea) return "That lord is no longer at sea.";
                    request.targetName = lord.FullName;
                    result = "You swear to bring down " + lord.FullName + ". Their fleet sails with a man-o'-war.";
                    break;
                case RequestKind.Escort:
                    if (!world.TryGetIsland(offer.Target, out ArchipelagoWorld.IslandSite destination)) return "The merchant changed her mind.";
                    MapParty merchant = travel.SpawnPartyAt(MapFaction.Merchant, 2, port, destination.Cell);
                    if (merchant == null) return "The merchant changed her mind.";
                    request.targetName = "Master " + BoatshipsNames.RandomCaptain(random);
                    merchant.captain = request.targetName; merchant.questId = request.id; request.partyId = merchant.id;
                    result = request.targetName + "'s cog '" + merchant.name + "' weighs anchor for " + BoatshipsNames.Island(destination) +
                        ". Stay close: you must be near her when she makes port.";
                    break;
                default:
                    if (!world.TryGetIsland(offer.Target, out ArchipelagoWorld.IslandSite drop)) return "The trader thinks better of it.";
                    result = "Casks of untaxed rum are lowered into your hold. Deliver them to " + BoatshipsNames.Island(drop) +
                        " — and keep clear of the Navy.";
                    break;
            }
            state.requests.Add(request);
            state.takenOffers.Add(offer.Id);
            if (state.takenOffers.Count > 200) state.takenOffers.RemoveRange(0, state.takenOffers.Count - 200);
            Save();
            return result;
        }

        private void CheckRequests()
        {
            float now = travel.Hours;
            foreach (RealmRequest request in state.requests.ToArray())
            {
                if (now > request.deadlineHours) { Fail(request, "Time has run out."); continue; }
                if (request.kind == RequestKind.Bounty && !travel.HasParty(request.partyId)) { Fail(request, "The pirate slipped away for good."); continue; }
                if (request.kind == RequestKind.HuntLord && (Lord(request.lordId) == null || Lord(request.lordId).status == LordStatus.Gone))
                { Fail(request, "Someone else got to them first."); continue; }
                if (request.kind == RequestKind.Escort) UpdateEscort(request);
            }
        }

        /// <summary>Partway along the route, pirates fall on the convoy.</summary>
        private void UpdateEscort(RealmRequest request)
        {
            MapParty merchant = travel.Party(request.partyId);
            if (merchant == null) { Fail(request, "The merchant's cog was lost."); return; }
            if (request.ambushed || !world.TryGetIsland(request.giver, out ArchipelagoWorld.IslandSite from) ||
                !world.TryGetIsland(request.target, out ArchipelagoWorld.IslandSite to)) return;
            float total = Vector2.Distance(from.Center, to.Center), travelled = Vector2.Distance(from.Center, merchant.position);
            if (total <= 1f || travelled / total < 0.4f) return;
            request.ambushed = true;
            if (!Pick(travel.IslandSites, s => true, out ArchipelagoWorld.IslandSite nearest)) return;
            MapParty raiders = travel.SpawnPartyAt(MapFaction.Pirate, Mathf.Clamp(1 + Tier, 2, 5), nearest, nearest.Cell);
            if (raiders == null) return;
            Vector2 offset = new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f).normalized * 28f;
            Vector3 water = world.KeepOnWater(new Vector3(merchant.position.x + offset.x, 0f, merchant.position.y + offset.y), 5f);
            raiders.position = new Vector2(water.x, water.z);
            raiders.questId = request.id;
            travel.Notify("Sails on the horizon! Pirates are closing on " + request.targetName + "'s cog.");
            Save();
        }

        private bool OnArrival(MapParty party, ArchipelagoWorld.IslandSite site)
        {
            if (!Ready) return false;
            RealmRequest request = RequestFor(party);
            if (request == null || request.kind != RequestKind.Escort || party.faction != MapFaction.Merchant) return false;
            if (site.Cell != request.target) return false;
            travel.RemoveParty(party.id);
            if (Vector2.Distance(travel.Position, site.Center) <= site.Radius + 180f) Complete(request);
            else Fail(request, request.targetName + " made port without you and refuses to pay.");
            return true;
        }

        private void Complete(RealmRequest request)
        {
            if (!state.requests.Remove(request)) return;
            economy.TryAdjustGold(request.reward);
            AddRenown(request.renown);
            AddReputation(request.giverFaction, 8);
            if (request.kind == RequestKind.Smuggling) AddReputation(1, -3);
            state.requestsDone++;
            string level = character != null ? character.AddExperience(CharacterSkill.Leadership, 15) : null;
            travel.Notify(Describe(request) + " — done! " + request.reward + " gold paid, +" + request.renown + " renown." + (level != null ? " " + level : ""));
            Save();
        }

        private void Fail(RealmRequest request, string why)
        {
            if (!state.requests.Remove(request)) return;
            if (request.kind == RequestKind.Bounty && travel.HasParty(request.partyId)) travel.RemoveParty(request.partyId);
            if (request.kind == RequestKind.Escort && travel.HasParty(request.partyId)) travel.RemoveParty(request.partyId);
            AddReputation(request.giverFaction, -5);
            travel.Notify(Describe(request) + " failed. " + why);
            Save();
        }

        private string Describe(RealmRequest request)
        {
            string target = world.TryGetIsland(request.target, out ArchipelagoWorld.IslandSite site) ? BoatshipsNames.Island(site) : "port";
            switch (request.kind)
            {
                case RequestKind.Bounty: return "Bounty on " + request.targetName;
                case RequestKind.HuntLord: return "The hunt for " + request.targetName;
                case RequestKind.Escort: return "Escorting " + request.targetName + " to " + target;
                default: return "Contraband run to " + target;
            }
        }

        // ───────────────────────────── Treasure maps ─────────────────────────────

        private void GiveMap(string how)
        {
            if (state.maps.Count >= MaxMaps) return;
            if (!Pick(travel.IslandSites, s => s.Kind == IslandKind.TreasureSite && !state.dug.Contains(s.Cell) && !state.maps.Contains(s.Cell), out ArchipelagoWorld.IslandSite site) &&
                !Pick(travel.IslandSites, s => s.Kind == IslandKind.Deserted && !state.dug.Contains(s.Cell) && !state.maps.Contains(s.Cell), out site)) return;
            state.maps.Add(site.Cell);
            travel.Notify(how + ": an X marks " + BoatshipsNames.Island(site) + ", " + Bearing(site.Center) + ".");
            Save();
        }

        private void Dig(ArchipelagoWorld.IslandSite site, Action back)
        {
            state.maps.Remove(site.Cell);
            state.dug.Add(site.Cell);
            state.treasuresFound++;
            int gold = 220 + random.Next(0, 300) + Tier * 40;
            economy.TryAdjustGold(gold);
            string[] finds = { "pearl", "gold_idol", "silver_cup" };
            string item = finds[random.Next(finds.Length)];
            if (travel.Inventory != null) travel.Inventory.AddToBag(item, 1);
            AddRenown(8);
            Save();
            ItemDef found = BoatshipsItems.Get(item);
            view().ShowMenu("Buried treasure!",
                "Your crew digs where the chart's X falls. Six feet down, a spade rings on iron: a sea chest, rotten at the corners.\n\n" +
                gold + " gold" + (found != null ? " and a " + found.Name.ToLowerInvariant() : "") + ". +8 renown.",
                new List<MapMenuOption> { new MapMenuOption("Back", back) });
        }

        // ───────────────────────────── Menus ─────────────────────────────

        private BoatshipsWorldMapView view() => travel.View;

        private void AddTownOptions(ArchipelagoWorld.IslandSite site, List<MapMenuOption> options, Action back)
        {
            if (!Ready || !travel.Captain) return;
            int faction = BoatshipsFactions.OfPort(site.Cell);
            options.Add(new MapMenuOption("Ask the notables for work (" + state.requests.Count + "/" + MaxRequests + " requests)", () => RequestsMenu(site, back, null)));
            foreach (RealmRequest smuggling in state.requests.FindAll(r => r.kind == RequestKind.Smuggling && r.target == site.Cell))
            {
                RealmRequest run = smuggling;
                options.Insert(0, new MapMenuOption("Deliver the contraband (" + run.reward + " gold)", () => { Complete(run); back?.Invoke(); }));
            }
            int week = (travel.Day - 1) / 7;
            string mapOffer = "map_" + site.Cell.x + "_" + site.Cell.y + "_" + week;
            if (!state.takenOffers.Contains(mapOffer) && state.maps.Count < MaxMaps)
                options.Add(new MapMenuOption("Buy a treasure chart from an old sailor in the tavern (" + MapPrice + " gold)", () =>
                {
                    if (!economy.TryAdjustGold(-MapPrice)) { back?.Invoke(); return; }
                    state.takenOffers.Add(mapOffer);
                    GiveMap("The old sailor spreads a stained chart on the table");
                    back?.Invoke();
                }, economy.Gold >= MapPrice));
            foreach (SeaLord captive in state.lords.FindAll(l => l.status == LordStatus.Captive))
            {
                SeaLord lord = captive;
                options.Add(new MapMenuOption("Your prisoner " + lord.FullName + "…", () => CaptiveMenu(lord, site, back)));
            }
            if (conquest != null && conquest.Owns(site.Cell) && string.IsNullOrEmpty(state.kingdom))
            {
                bool can = Tier >= 3;
                options.Add(new MapMenuOption(can ? "Proclaim the Kingdom of " + BoatshipsNames.Island(site) :
                    "Proclaim a kingdom — needs clan tier 3 (" + TierRenown[3] + " renown)", () => Proclaim(site, back), can));
            }
        }

        private void CaptiveMenu(SeaLord lord, ArchipelagoWorld.IslandSite site, Action back)
        {
            int faction = BoatshipsFactions.OfPort(site.Cell);
            int lordFaction = BoatshipsConquest.PortFactionOf(lord.faction);
            int ransom = 350 + (lord.faction == MapFaction.Merchant ? 250 : 100) + (lord.trait == "greedy" ? 150 : 0);
            bool theirPort = faction == lordFaction;
            var options = new List<MapMenuOption>
            {
                new MapMenuOption(theirPort ? "Accept the " + BoatshipsFactions.Name(faction) + "'s ransom (" + ransom + " gold)" :
                    "Ransom — only at a " + BoatshipsFactions.Name(lordFaction) + " port", () =>
                {
                    economy.TryAdjustGold(ransom);
                    Free(lord, 8f, -10);
                    travel.Notify(lord.FullName + " is ransomed for " + ransom + " gold. They swear to meet you again at sea.");
                    back?.Invoke();
                }, theirPort),
                new MapMenuOption("Release them without ransom (they will remember it)", () =>
                {
                    Free(lord, 6f, 25);
                    AddReputation(lordFaction, 10);
                    travel.Notify("You set " + lord.FullName + " ashore with their sword. Word of your mercy spreads.");
                    back?.Invoke();
                })
            };
            if (lord.faction == MapFaction.Pirate)
                options.Add(new MapMenuOption(faction == 1 ? "Hand them to the Royal Navy for the gallows (600 gold)" :
                    "Hand over to the Navy — only at a Royal Navy port", () =>
                {
                    economy.TryAdjustGold(600);
                    AddReputation(1, 15);
                    lord.status = LordStatus.Gone;
                    lord.returnHours = travel.Hours + 20f * 24f;
                    travel.Notify(lord.FullName + " hangs at " + BoatshipsNames.Island(site) + ". The Navy pays 600 gold and toasts your name.");
                    Save();
                    back?.Invoke();
                }, faction == 1));
            options.Add(new MapMenuOption("Keep them in irons for now", back));
            view().ShowMenu(lord.FullName, lord.FullName + " of the " + BoatshipsTravelController.FactionName(lord.faction) + ", a " + lord.trait +
                " " + (lord.faction == MapFaction.Pirate ? "rogue" : "officer") + ", sits in your hold in irons.\n" +
                "You have beaten them " + lord.defeats + " time(s). Their regard for you: " + lord.relation + ".", options);
        }

        private void Free(SeaLord lord, float days, int relation)
        {
            lord.status = LordStatus.Resting;
            lord.returnHours = travel.Hours + days * 24f;
            lord.relation += relation;
            Save();
        }

        private void AddIslandOptions(ArchipelagoWorld.IslandSite site, List<MapMenuOption> options, Action back)
        {
            if (!Ready || !travel.Captain) return;
            if (state.maps.Contains(site.Cell))
                options.Insert(0, new MapMenuOption("Dig where your chart's X marks the spot", () => Dig(site, back)));
            else if (site.Kind == IslandKind.TreasureSite && state.dug.Contains(site.Cell))
                options.Add(new MapMenuOption("(Your old dig is still here, an empty pit in the sand)", null, false));
        }

        // ───────────────────────────── Kingdom and victory ─────────────────────────────

        public int PortsToWin => Mathf.Max(3, Mathf.CeilToInt(travel.PortSites.Count * 0.5f));

        private void Proclaim(ArchipelagoWorld.IslandSite site, Action back)
        {
            if (Tier < 3 || conquest == null || !conquest.Owns(site.Cell)) { back?.Invoke(); return; }
            state.kingdom = "Kingdom of " + BoatshipsNames.Island(site);
            AddRenown(50);
            AddReputation(2, 10);  // the Free Ports admire a free crown
            AddReputation(1, -20); // the Crown calls it rebellion
            Save();
            view().ShowMenu(state.kingdom,
                "From the steps of the harbour fort you raise your banner and declare a free crown over these waters. " +
                "Your captains cheer; the Royal Navy calls it rebellion.\n\n" +
                "Rule " + PortsToWin + " of the archipelago's " + travel.PortSites.Count + " ports and the isles are yours. (You hold " + conquest.CityCount + ".)",
                new List<MapMenuOption> { new MapMenuOption("Long may you reign", back) });
            CheckVictory();
        }

        private void CheckVictory()
        {
            if (state.victory || string.IsNullOrEmpty(state.kingdom) || conquest == null || conquest.CityCount < PortsToWin) return;
            state.victory = true;
            Save();
            view().ShowMenu("Sovereign of the Archipelago",
                "The last harbour chain falls before your fleet. From one sloop and a hungry crew you have built the " + state.kingdom +
                ", and no flag flies in these waters without your leave.\n\n" +
                "Day " + travel.Day + "   Renown " + Renown + "   Ports " + conquest.CityCount + "/" + travel.PortSites.Count + "\n" +
                "Sea lords defeated " + state.lordsDefeated + "   Requests done " + state.requestsDone + "   Treasures found " + state.treasuresFound + "\n\n" +
                "Your story is complete — but the sea is still yours to sail.",
                new List<MapMenuOption> { new MapMenuOption("Continue your reign", null) });
        }

        /// <summary>The clan screen [K]: renown, tier, what it unlocks, sea lords, requests, charts and the road to victory.</summary>
        public void ClanMenu(Action back)
        {
            if (!Ready) return;
            int tier = Tier;
            var text = new System.Text.StringBuilder();
            text.Append("Clan of ").Append(character != null && character.Profile != null ? character.Profile.name : "the captain")
                .Append(" — tier ").Append(tier).Append(": ").Append(Title).Append("\nRenown ").Append(Renown);
            if (tier + 1 < TierRenown.Length) text.Append("   (tier ").Append(tier + 1).Append(" at ").Append(TierRenown[tier + 1]).Append(")");
            text.Append("\nYou can lead ").Append(CrewLimitFor(tier)).Append(" sailors and ").Append(CompanyLimitFor(tier)).Append(" ships under captains.\n\n");
            int held = conquest != null ? conquest.CityCount : 0;
            if (state.victory) text.Append("The archipelago is yours. ");
            text.Append(string.IsNullOrEmpty(state.kingdom)
                ? "Road to a crown: reach tier 3 and hold a port, then proclaim your kingdom there. "
                : state.kingdom + ". ");
            text.Append("Ports held ").Append(held).Append("/").Append(PortsToWin).Append(" needed to rule the isles.\n\n");
            text.Append("Sea lords:\n");
            foreach (SeaLord lord in state.lords)
            {
                string where = lord.status == LordStatus.AtSea ? "at sea" : lord.status == LordStatus.Captive ? "YOUR PRISONER" :
                    lord.status == LordStatus.Resting ? "ashore, back in " + Mathf.CeilToInt(Mathf.Max(0f, lord.returnHours - travel.Hours) / 24f) + " day(s)" : "hanged";
                text.Append("  ").Append(lord.FullName).Append(" (").Append(BoatshipsTravelController.FactionName(lord.faction)).Append(", ")
                    .Append(lord.trait).Append(") — ").Append(where).Append("\n");
            }
            text.Append("\nRequests:\n");
            if (state.requests.Count == 0) text.Append("  None. Ask the notables in any port.\n");
            foreach (RealmRequest request in state.requests)
                text.Append("  ").Append(Describe(request)).Append(" — ").Append(request.reward).Append(" gold, ")
                    .Append(Mathf.CeilToInt(Mathf.Max(0f, request.deadlineHours - travel.Hours) / 24f)).Append(" day(s) left\n");
            text.Append("\nTreasure charts:\n");
            if (state.maps.Count == 0) text.Append("  None. Old sailors in tavern corners sell them; pirates carry them.\n");
            var options = new List<MapMenuOption>();
            foreach (Vector2Int cell in state.maps)
            {
                if (!world.TryGetIsland(cell, out ArchipelagoWorld.IslandSite site)) continue;
                text.Append("  X marks ").Append(BoatshipsNames.Island(site)).Append(", ").Append(Bearing(site.Center)).Append("\n");
                ArchipelagoWorld.IslandSite target = site;
                options.Add(new MapMenuOption("Set course for " + BoatshipsNames.Island(site) + " (treasure)", () => travel.SetCourse(target),
                    travel.CurrentMode == TravelMode.Map));
            }
            foreach (RealmRequest request in state.requests)
            {
                if (request.kind == RequestKind.Escort || request.kind == RequestKind.HuntLord) continue;
                if (!world.TryGetIsland(request.target, out ArchipelagoWorld.IslandSite site)) continue;
                ArchipelagoWorld.IslandSite target = site;
                options.Add(new MapMenuOption("Set course for " + BoatshipsNames.Island(site) + " (" + Describe(request).ToLowerInvariant() + ")",
                    () => travel.SetCourse(target), travel.CurrentMode == TravelMode.Map));
            }
            options.Add(new MapMenuOption(back != null ? "Back" : "Close  [K]", back));
            view().ShowMenu("Your clan", text.ToString(), options);
        }

        /// <summary>Compass bearing and sailing time from the flagship to a point.</summary>
        private string Bearing(Vector2 point)
        {
            Vector2 d = point - travel.Position;
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            float angle = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            string direction = names[Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 45f) % 8];
            float days = travel.PlayerTravelDays(point);
            return direction + ", about " + (days < 1f ? "less than a day" : Mathf.CeilToInt(days) + " day(s)") + "' sail";
        }

        // ───────────────────────────── First-time hints ─────────────────────────────

        private void ShowHints()
        {
            if (Time.unscaledTime < nextHintAt) return;
            TravelMode mode = travel.CurrentMode;
            if (mode == TravelMode.Map && Hint("map", "Click the sea to set a course, click a ship to intercept it. [K] opens your clan: renown, sea lords, requests and treasure charts.")) return;
            if (player != null && player.AtHelm && Hint("helm", "At the wheel: [A]/[D] steer, [W]/[S] raise or lower sail. [E] lets go — she holds her course.")) return;
            if (mode == TravelMode.Battle && player != null && player.Active && Hint("deck", "On deck: [E] takes the wheel or a gun, [R] loads it, click fires. Hold [E] on a leak to nail a plank. [1] cutlass, [2] pistol.")) return;
            if (mode == TravelMode.Town && Hint("town", "Ashore: walk up to people and doors and press [E]. [Tab] takes you back to your ship.")) return;
            if (mode == TravelMode.Port && Hint("port", "In port: ask the notables for work, buy treasure charts in the tavern and recruit sailors. Renown lets you lead more of them.")) return;
        }

        private bool Hint(string key, string text)
        {
            string pref = "Boatships.Hint." + key;
            if (PlayerPrefs.GetInt(pref, 0) == 1) return false;
            PlayerPrefs.SetInt(pref, 1);
            PlayerPrefs.Save();
            travel.Notify(text);
            nextHintAt = Time.unscaledTime + 9f;
            return true;
        }
    }
}
