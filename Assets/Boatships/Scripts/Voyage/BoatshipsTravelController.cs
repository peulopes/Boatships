using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Boatships
{
    /// <summary>Map = strategic world map (Bannerlord-style). The other modes are the local 3D scene.</summary>
    /// <summary>Town = walking the port in first person. CrewBattle = first-person fight aboard an AI employer.</summary>
    public enum TravelMode { Map, Port, Island, Battle, Town, CrewBattle }
    public enum MapFaction { Merchant, Navy, Pirate }
    public enum MapJobKind { None, Crew, Passage }

    [Serializable]
    public sealed class MapParty
    {
        public string id;
        public string name;
        public MapFaction faction;
        public int tier = 1;
        public Vector2 position;
        public Vector2 heading = Vector2.up;
        public Vector2Int destination;
        public float speed = 16f;
        public float cooldown;
        public bool employer;
        [NonSerialized] public bool chasing;
    }

    [Serializable]
    public sealed class MapJob
    {
        public MapJobKind kind;
        public MapFaction employer;
        public string ship;
        public string role;
        public CharacterSkill skill;
        public Vector2Int origin;
        public Vector2Int destination;
        public int pay;
        public string partyId;
    }

    [Serializable]
    public sealed class MapRaid
    {
        public Vector2Int cell;
        public float hour;
    }

    [Serializable]
    public sealed class WorldMapState
    {
        public int version = 1;
        public Vector2 position;
        public Vector2 heading = Vector2.up;
        public float hours = 8f;
        public bool hasTarget;
        public Vector2 target;
        public bool targetIsIsland;
        public Vector2Int targetIsland;
        public string interceptId;
        public Vector2Int lastPort;
        public bool starving;
        public int serial;
        public MapJob job = new MapJob();
        public List<MapParty> parties = new List<MapParty>();
        public List<MapRaid> raids = new List<MapRaid>();
    }

    public struct JobOffer
    {
        public MapFaction Employer;
        public string Ship;
        public string Role;
        public CharacterSkill Skill;
        public ArchipelagoWorld.IslandSite Destination;
        public int Pay;
        public float Days;
        public string Risk;
    }

    public struct PassageOffer
    {
        public ArchipelagoWorld.IslandSite Destination;
        public int Cost;
        public float Days;
    }

    /// <summary>Owns the travel loop: world-map movement, AI parties, jobs aboard AI ships, encounters,
    /// and the transitions into the local 3D scene (harbor, island coast, battle) and back.</summary>
    [DefaultExecutionOrder(900)]
    public sealed partial class BoatshipsTravelController : MonoBehaviour
    {
        public const float HoursPerSecond = 1.2f;
        public const float BaseSpeed = 20f;
        public const float LeaveDistance = 120f;
        private const float ContactDistance = 7f;
        private const float ArrivalMargin = 11f;
        private const float RaidCooldownHours = 24f * 5f;

        private static readonly string[] RoleNames = { "Deckhand", "Lookout", "Gunner's mate", "Marine", "Purser", "Bosun" };
        private static readonly CharacterSkill[] RoleSkills = { CharacterSkill.Sailing, CharacterSkill.Sailing,
            CharacterSkill.Gunnery, CharacterSkill.Fighting, CharacterSkill.Trade, CharacterSkill.Leadership };
        private static readonly float[] StepAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 125f, -125f };

        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private PortDocking docking;
        [SerializeField] private BoatshipsEncounterDirector director;
        [SerializeField] private BoatshipsSaveCoordinator saves;
        [SerializeField] private BoatshipsCharacter character;
        [SerializeField] private BoatshipsCampaignHUD hud;
        [SerializeField] private Transform ship;
        [SerializeField] private ShipArcadeController movement;
        [SerializeField] private WeaponGroupCombat weapons;
        [SerializeField] private ShipFlooding flooding;
        [SerializeField] private OceanWaves ocean;
        private BoatshipsWorldMapView view;
        private Rigidbody body;

        private WorldMapState state;
        private int loadedSeed = int.MinValue;
        private int cachedSeed = int.MinValue;
        private readonly List<ArchipelagoWorld.IslandSite> ports = new List<ArchipelagoWorld.IslandSite>();
        private readonly List<ArchipelagoWorld.IslandSite> islands = new List<ArchipelagoWorld.IslandSite>();
        private readonly List<string> battleIds = new List<string>();
        private readonly List<string> pendingPrizes = new List<string>();
        private string battlePartyId;
        private bool battleWon;
        private bool raidBattle;
        private Vector2Int raidCell;
        private ArchipelagoWorld.IslandSite localIsland;
        private bool hasLocalIsland;
        private bool mapPresentation;
        private bool presentationApplied;
        private readonly List<CameraState> cameras = new List<CameraState>();
        private float saveTimer;
        private float spawnTimer;
        private float messageUntil;
        private string welcomeMessage;
        private bool staleEncountersCleared;
        private readonly System.Random random = new System.Random();
        private bool OverlayOpen => hud != null && hud.BlockingOverlayOpen;

        private struct CameraState
        {
            public Camera Camera;
            public int Mask;
            public CameraClearFlags Flags;
            public Color Color;
            public bool PostProcessing;
            public bool HasCameraData;
        }

        public TravelMode Mode { get; private set; } = TravelMode.Map;
        public bool Live => state != null && character != null && character.HasCharacter && saves != null && saves.IsReady;
        public bool Paused { get; set; }
        public float TimeSpeed { get; set; } = 1f;
        public string Message { get; private set; } = "";
        public bool HasMessage => Time.unscaledTime < messageUntil && !string.IsNullOrEmpty(Message);
        public Vector2 Position => state != null ? state.position : Vector2.zero;
        public Vector2 Heading => state != null ? state.heading : Vector2.up;
        public bool HasTarget => state != null && (state.hasTarget || !string.IsNullOrEmpty(state.interceptId));
        public Vector2 Target
        {
            get
            {
                if (state == null) return Vector2.zero;
                MapParty chased = FindParty(state.interceptId);
                return chased != null ? chased.position : state.target;
            }
        }
        public IReadOnlyList<MapParty> Parties => state != null ? (IReadOnlyList<MapParty>)state.parties : Array.Empty<MapParty>();
        public bool OnJob => state != null && state.job != null && state.job.kind != MapJobKind.None;
        public bool Captain => character != null && character.OwnsShip && !OnJob;
        public bool CanCommand => Live && Mode == TravelMode.Map && Captain;
        public int Day => state != null ? Mathf.FloorToInt(state.hours / 24f) + 1 : 1;
        public int Hour => state != null ? Mathf.FloorToInt(state.hours % 24f) : 8;
        public float SightRadius => character != null ? character.SightRadius : 120f;
        public bool ShowAbandonShip => Mode != TravelMode.Map && Mode != TravelMode.Port && flooding != null && flooding.IsSinking;
        public BoatshipsCharacter Character => character;
        public PortEconomy Economy => economy;
        public ArchipelagoWorld World => world;

        public void Configure(ArchipelagoWorld map, PortEconomy account, PortDocking port, BoatshipsEncounterDirector encounters,
            BoatshipsSaveCoordinator checkpoints, BoatshipsCharacter captain, BoatshipsCampaignHUD campaignHud,
            Transform flagship, ShipArcadeController controller, WeaponGroupCombat guns, ShipFlooding flood, OceanWaves waves)
        {
            world = map; economy = account; docking = port; director = encounters; saves = checkpoints;
            character = captain; hud = campaignHud; ship = flagship; movement = controller; weapons = guns;
            flooding = flood; ocean = waves;
            if (director != null) director.PrizeTaken += OnPrizeTaken;
            if (director != null) director.CompanyShipLost += Notify;
            if (hud != null) hud.CompanyHandler = () => CompanyMenu(null);
        }

        public void BindView(BoatshipsWorldMapView mapView) => view = mapView;

        public void BindFirstPerson(BoatshipsFirstPerson firstPerson, BoatshipsTown portTown, BoatshipsCrewBattle crewFight)
        { fps = firstPerson; town = portTown; crewBattle = crewFight; }

        public void BindInventory(BoatshipsInventory items) => inventory = items;
        private BoatshipsInventory inventory;
        public BoatshipsInventory Inventory => inventory;

        /// <summary>Sea battles of the player's own ship are fought in first person on deck.</summary>
        public void BindShipBattle(BoatshipsShipBattle battle) => shipBattle = battle;
        private BoatshipsShipBattle shipBattle;

        public bool FirstPersonActive => fps != null && fps.Active;

        private BoatshipsFirstPerson fps;
        private BoatshipsTown town;
        private BoatshipsTown activeTown;
        private BoatshipsCrewBattle crewBattle;
        private ArchipelagoWorld.IslandSite townSite;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<Collider> hiddenColliders = new List<Collider>();
        private bool flagshipHidden;

        /// <summary>The flagship model is only shown when the player owns it and is not aboard someone else's ship.</summary>
        private void UpdateFlagshipVisibility()
        {
            bool hide = Mode != TravelMode.Map && (!character.OwnsShip || Mode == TravelMode.CrewBattle);
            if (hide == flagshipHidden || ship == null) return;
            flagshipHidden = hide;
            if (hide)
            {
                hiddenRenderers.Clear(); hiddenColliders.Clear();
                foreach (Renderer part in ship.GetComponentsInChildren<Renderer>())
                    if (part.enabled) { part.enabled = false; hiddenRenderers.Add(part); }
                foreach (Collider part in ship.GetComponentsInChildren<Collider>())
                    if (part.enabled) { part.enabled = false; hiddenColliders.Add(part); }
            }
            else
            {
                foreach (Renderer part in hiddenRenderers) if (part != null) part.enabled = true;
                foreach (Collider part in hiddenColliders) if (part != null) part.enabled = true;
                hiddenRenderers.Clear(); hiddenColliders.Clear();
            }
        }

        private void ExitFirstPerson()
        {
            if (shipBattle != null && shipBattle.Active) shipBattle.End();
            if (crewBattle != null && crewBattle.Active) return;
            if (activeTown != null)
            {
                activeTown.Handler = null;
                // Only the stand-in town is torn down; a port's own harbour town stays with its island.
                if (activeTown == town) town.Clear();
                activeTown = null;
            }
            if (fps != null) fps.End();
        }

        private void Awake() { if (ship != null) body = ship.GetComponent<Rigidbody>(); }
        private void OnEnable() { BoatshipsSaveCoordinator.CheckpointCapturing += Save; }
        private void OnDisable() { BoatshipsSaveCoordinator.CheckpointCapturing -= Save; }

        // ───────────────────────────── Main loop ─────────────────────────────

        private void Update()
        {
            if (world == null || character == null || saves == null || view == null) return;
            if (body == null && ship != null) body = ship.GetComponent<Rigidbody>();
            CacheIslands();
            if (!character.HasCharacter || !saves.IsReady || economy.ActiveSeed != world.Seed)
            {
                if (Mode == TravelMode.Map) SetMapPresentation(true);
                return;
            }
            if (loadedSeed != world.Seed) Load();
            if (state == null) return;
            // The company fights only in the captain's own battles (not on the map, nor aboard an employer's ship).
            director.CompanyMayEngage = Captain && (Mode == TravelMode.Battle || Mode == TravelMode.Island);

            if (!staleEncountersCleared && director.IsReady)
            {
                // Waves left over from the old free-sailing mode would otherwise linger at sea.
                staleEncountersCleared = true;
                if (Mode == TravelMode.Map) director.DismissUnresolved();
            }
            if (welcomeMessage != null && !view.MenuOpen && !OverlayOpen)
            {
                string text = welcomeMessage;
                welcomeMessage = null;
                if (world.TryGetIsland(state.lastPort, out ArchipelagoWorld.IslandSite home)) TownMenu(home, text);
            }

            if (Mode == TravelMode.Map) UpdateMap();
            else UpdateLocal();
        }

        private void UpdateMap()
        {
            SetMapPresentation(true);
            if (movement != null) movement.enabled = false;
            if (weapons != null) weapons.enabled = false;
            if (flooding != null && flooding.IsSinking && character.OwnsShip) { TowToPort(); return; }
            if (docking.IsDocked)
            {
                if (Captain && AtIsland(out ArchipelagoWorld.IslandSite port) && port.Kind == IslandKind.PortSite)
                { EnterPort(port); return; }
                docking.Undock();
            }
            bool running = !view.MenuOpen && !Paused && !OverlayOpen && Time.timeScale > 0f;
            if (running) Simulate(Time.deltaTime);
            SyncShipToParty();
        }

        private void UpdateLocal()
        {
            SetMapPresentation(false);
            UpdateFlagshipVisibility();
            if (Mode == TravelMode.CrewBattle)
            {
                if (movement != null) movement.enabled = false;
                if (weapons != null) weapons.enabled = false;
                if (crewBattle == null || !crewBattle.Active) { Mode = TravelMode.Map; SetMapPresentation(true); }
                return;
            }
            if (Mode == TravelMode.Town)
            {
                if (movement != null) movement.enabled = false;
                if (weapons != null) weapons.enabled = false;
                if (docking.IsDocked) { ExitFirstPerson(); Mode = TravelMode.Port; }
                else if (fps == null || !fps.Active) ReturnToMap(false);
                return;
            }
            if (!character.OwnsShip) { ReturnToMap(false); return; }
            if (Mode == TravelMode.Port)
            {
                if (!docking.IsDocked) ReturnToMap(true);
                return;
            }
            if (docking.IsDocked)
            {
                // e.g. the flagship was recovered at a port from the campaign menu.
                battleIds.Clear(); battlePartyId = null; raidBattle = false;
                Mode = TravelMode.Port;
                return;
            }
            if (Mode == TravelMode.Battle && shipBattle != null && fps != null && battleIds.Count > 0)
            {
                if (shipBattle.Active) shipBattle.SyncEnemies(battleIds);
                else if (!shipBattle.Abandoned && !fps.Active && (flooding == null || !flooding.IsSinking) && ship != null)
                    shipBattle.Begin(ship.GetComponent<ShipCombatTarget>(), movement, weapons, battleIds);
            }
            HandleCompanyOrdersKey();
            if (!battleWon && battleIds.Count > 0 && director.RemainingHostiles(battleIds) == 0) WinBattle();
            if (pendingPrizes.Count > 0 && !view.MenuOpen && !OverlayOpen && !FirstPersonActive &&
                (battleIds.Count == 0 || director.RemainingHostiles(battleIds) == 0))
            {
                string id = pendingPrizes[0];
                pendingPrizes.RemoveAt(0);
                PrizeMenu(id);
            }
            // Prizes first (a new company ship adds room in her hold), then the survivors.
            else if (battleWon && !prisonersOffered && pendingPrizes.Count == 0 && !view.MenuOpen && !OverlayOpen && !FirstPersonActive)
            {
                prisonersOffered = true;
                SurvivorsMenu();
            }
        }

        private void Simulate(float deltaTime)
        {
            float scaled = deltaTime * Mathf.Clamp(TimeSpeed, 0.25f, 4f);
            int dayBefore = Day;
            state.hours += scaled * HoursPerSecond;
            if (Day != dayBefore) DailyUpkeep();
            spawnTimer -= scaled;
            if (spawnTimer <= 0f) { spawnTimer = 6f; MaintainPopulation(false); }
            UpdateParties(scaled);
            if (view.MenuOpen || Mode != TravelMode.Map) return;
            MovePlayer(scaled);
            if (view.MenuOpen || Mode != TravelMode.Map) return;
            CheckContacts();
            saveTimer += deltaTime;
            if (saveTimer > 20f) { saveTimer = 0f; Save(); }
        }

        // ───────────────────────────── Persistence ─────────────────────────────

        private void Load()
        {
            loadedSeed = world.Seed;
            state = null;
            string path = SavePath(loadedSeed);
            if (BoatshipsSaveStore.Exists(path))
            {
                try
                {
                    WorldMapState loaded = JsonUtility.FromJson<WorldMapState>(BoatshipsSaveStore.Read(path));
                    if (loaded == null || loaded.version != 1) throw new InvalidDataException("Unsupported world map version.");
                    if (loaded.job == null) loaded.job = new MapJob();
                    if (loaded.parties == null) loaded.parties = new List<MapParty>();
                    if (loaded.raids == null) loaded.raids = new List<MapRaid>();
                    state = loaded;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Boatships world map state could not be loaded, starting at the home port: " + ex.Message, this);
                }
            }
            Mode = TravelMode.Map;
            if (state == null) InitializeState();
            else
            {
                state.position = KeepOnWater(state.position);
                if (OnJob && FindParty(state.job.partyId) == null) state.job = new MapJob();
            }
            director.DismissUnresolved();
            SyncShipToParty();
            character.ApplyShipColors();
        }

        private void InitializeState()
        {
            state = new WorldMapState();
            ArchipelagoWorld.IslandSite home = ChooseHomePort();
            character.AssignHomePort(home.Cell);
            state.lastPort = home.Cell;
            state.position = Coast(home, home.Center + Vector2.down * 100f, 8f);
            state.heading = (home.Center - state.position).normalized;
            for (int i = 0; i < 14; i++) MaintainPopulation(true);
            Vector2Int cell = home.Cell;
            economy.TryCampaignChange(progress => { if (!progress.discovered.Contains(cell)) progress.discovered.Add(cell); });
            welcomeMessage = character.Name + " the " + CharacterBackgrounds.Get(character.Profile.background).Title +
                " — your story begins here, in " + BoatshipsNames.Island(home) + ". " +
                (character.OwnsShip ? "Your ship waits in the harbor. Click the sea or an island on the map to set a course." :
                "You have no ship. Find work aboard a vessel in the tavern, or book passage to another port.");
            Save();
        }

        private ArchipelagoWorld.IslandSite ChooseHomePort()
        {
            CharacterProfile profile = character.Profile;
            if (profile.homePortAssigned && world.TryGetIsland(profile.homePort, out ArchipelagoWorld.IslandSite saved) &&
                saved.Kind == IslandKind.PortSite) return saved;
            ArchipelagoWorld.IslandSite best = default;
            float bestScore = float.MaxValue;
            foreach (ArchipelagoWorld.IslandSite port in ports)
            {
                float score = port.Center.magnitude + (BoatshipsFactions.OfPort(port.Cell) == (int)profile.home ? 0f : 10000f);
                if (score >= bestScore) continue;
                bestScore = score; best = port;
            }
            return best;
        }

        public void Save()
        {
            if (state == null || world == null || loadedSeed != world.Seed || !BoatshipsSaveStore.Enabled) return;
            try { BoatshipsSaveStore.Write(SavePath(loadedSeed), JsonUtility.ToJson(state)); }
            catch (Exception ex) { Debug.LogWarning("Boatships world map save failed: " + ex.Message, this); }
        }

        private static string SavePath(int seed) =>
            Path.Combine(Application.persistentDataPath, "boatships_worldmap_v1_" + seed + ".json");

        private void CacheIslands()
        {
            if (cachedSeed == world.Seed && islands.Count > 0) return;
            cachedSeed = world.Seed;
            ports.Clear(); islands.Clear();
            foreach (ArchipelagoWorld.IslandSite site in world.Islands)
            {
                islands.Add(site);
                if (site.Kind == IslandKind.PortSite) ports.Add(site);
            }
            // Dictionary order is not guaranteed; keep jobs and spawns deterministic.
            Comparison<ArchipelagoWorld.IslandSite> order = (a, b) =>
                a.Cell.x != b.Cell.x ? a.Cell.x.CompareTo(b.Cell.x) : a.Cell.y.CompareTo(b.Cell.y);
            ports.Sort(order); islands.Sort(order);
        }

        // ───────────────────────────── Presentation ─────────────────────────────

        private void SetMapPresentation(bool map)
        {
            if (presentationApplied && mapPresentation == map) return;
            presentationApplied = true;
            mapPresentation = map;
            if (director != null) director.Suspended = map;
            // On the world map the 3D scene is not shown: stop streaming islands (and their towns, forts and
            // townsfolk) and stop deforming the ocean mesh. Ship buoyancy still samples the wave function.
            if (world != null) world.Suspended = map;
            if (ocean != null) ocean.enabled = !map;
            if (map)
            {
                cameras.Clear();
                foreach (Camera camera in Camera.allCameras)
                {
                    UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
                    cameras.Add(new CameraState { Camera = camera, Mask = camera.cullingMask,
                        Flags = camera.clearFlags, Color = camera.backgroundColor,
                        HasCameraData = data != null, PostProcessing = data != null && data.renderPostProcessing });
                    // Only a clear colour is drawn behind the map UI: skip the full-screen post-processing passes.
                    if (data != null) data.renderPostProcessing = false;
                    camera.cullingMask = 0;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = BoatshipsUI.Background;
                }
            }
            else
            {
                foreach (CameraState saved in cameras)
                {
                    if (saved.Camera == null) continue;
                    saved.Camera.cullingMask = saved.Mask;
                    saved.Camera.clearFlags = saved.Flags;
                    saved.Camera.backgroundColor = saved.Color;
                    if (saved.HasCameraData)
                    {
                        UniversalAdditionalCameraData data = saved.Camera.GetComponent<UniversalAdditionalCameraData>();
                        if (data != null) data.renderPostProcessing = saved.PostProcessing;
                    }
                }
                cameras.Clear();
            }
        }

        private void SyncShipToParty()
        {
            if (ship == null || state == null) return;
            Vector3 position = new Vector3(state.position.x, ship.position.y, state.position.y);
            Quaternion rotation = state.heading.sqrMagnitude > 0.001f ?
                Quaternion.Euler(0f, Mathf.Atan2(state.heading.x, state.heading.y) * Mathf.Rad2Deg, 0f) : ship.rotation;
            if (body != null) { body.position = position; body.rotation = rotation; }
            ship.SetPositionAndRotation(position, rotation);
        }

        private void PlaceShip(Vector2 point, Vector2 lookAt)
        {
            state.position = point;
            Vector2 forward = lookAt - point;
            if (forward.sqrMagnitude > 0.01f) state.heading = forward.normalized;
            float y = ocean != null ? ocean.SampleHeight(point.x, point.y, Time.time) + 0.55f : ship.position.y;
            Vector3 position = new Vector3(point.x, y, point.y);
            Quaternion rotation = Quaternion.Euler(0f, Mathf.Atan2(state.heading.x, state.heading.y) * Mathf.Rad2Deg, 0f);
            if (body != null) { body.position = position; body.rotation = rotation; }
            ship.SetPositionAndRotation(position, rotation);
        }

        public void Notify(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Message = text;
            messageUntil = Time.unscaledTime + 6f;
        }

        // ───────────────────────────── Movement ─────────────────────────────

        private float PlayerSpeed
        {
            get
            {
                float speed = BaseSpeed * character.MapSpeedMultiplier * (1f + economy.Campaign.sailLevel * 0.1f) *
                    (inventory != null && inventory.Ready ? inventory.SpeedFactor : 1f);
                return state.starving ? speed * 0.6f : speed;
            }
        }

        private void MovePlayer(float deltaTime)
        {
            if (OnJob)
            {
                MapParty employer = FindParty(state.job.partyId);
                if (employer == null) { state.job = new MapJob(); return; }
                state.position = employer.position;
                state.heading = employer.heading;
                return;
            }
            if (!character.OwnsShip) return;
            Vector2 goal;
            if (!string.IsNullOrEmpty(state.interceptId))
            {
                MapParty chased = FindParty(state.interceptId);
                if (chased == null || !IsVisible(chased)) { state.interceptId = null; Notify("You lost sight of them."); return; }
                goal = chased.position;
            }
            else if (state.hasTarget) goal = state.target;
            else return;

            state.position = Step(state.position, goal, PlayerSpeed * deltaTime, out Vector2 heading);
            state.heading = heading;
            if (!string.IsNullOrEmpty(state.interceptId)) return;
            if (state.targetIsIsland && world.TryGetIsland(state.targetIsland, out ArchipelagoWorld.IslandSite site))
            {
                if (Vector2.Distance(state.position, site.Center) <= site.Radius * 0.78f + ArrivalMargin) Arrive(site, null);
            }
            else if (Vector2.Distance(state.position, goal) < 1.5f) state.hasTarget = false;
        }

        /// <summary>Moves toward a goal, sliding around island coastlines.</summary>
        private Vector2 Step(Vector2 position, Vector2 goal, float step, out Vector2 heading)
        {
            Vector2 toGoal = goal - position;
            float distance = toGoal.magnitude;
            heading = distance > 0.001f ? toGoal / distance : Vector2.up;
            if (distance < 0.001f || step <= 0f) return position;
            step = Mathf.Min(step, distance);
            foreach (float angle in StepAngles)
            {
                Vector2 direction = Rotate(heading, angle);
                Vector2 candidate = position + direction * step;
                if ((KeepOnWater(candidate) - candidate).sqrMagnitude < 0.0004f)
                {
                    heading = direction;
                    return candidate;
                }
            }
            return KeepOnWater(position + heading * step);
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private Vector2 KeepOnWater(Vector2 point)
        {
            Vector3 kept = world.KeepOnWater(new Vector3(point.x, 0f, point.y), 4f);
            return new Vector2(kept.x, kept.z);
        }

        private Vector2 Coast(ArchipelagoWorld.IslandSite site, Vector2 from, float extra)
        {
            Vector2 direction = from - site.Center;
            if (direction.sqrMagnitude < 0.01f) direction = Vector2.down;
            return KeepOnWater(site.Center + direction.normalized * (site.Radius * 0.78f + extra));
        }

        public bool AtIsland(out ArchipelagoWorld.IslandSite site)
        {
            site = default;
            if (state == null || !world.TryNearestIsland(new Vector3(state.position.x, 0f, state.position.y), out site)) return false;
            return Vector2.Distance(site.Center, state.position) <= site.Radius * 0.78f + ArrivalMargin + 3f;
        }

        private bool PlayerSafe() => !OnJob && AtIsland(out ArchipelagoWorld.IslandSite site) && site.Kind == IslandKind.PortSite;

        public bool IsVisible(MapParty party) =>
            party.employer || (party.position - state.position).sqrMagnitude <= SightRadius * SightRadius;

        // ───────────────────────────── Commands from the map ─────────────────────────────

        public void SetCourse(Vector2 point)
        {
            if (!RequireCommand()) return;
            state.hasTarget = true; state.targetIsIsland = false; state.interceptId = null;
            state.target = KeepOnWater(point);
        }

        public void SetCourse(ArchipelagoWorld.IslandSite site)
        {
            if (!RequireCommand()) return;
            if (AtIsland(out ArchipelagoWorld.IslandSite current) && current.Cell == site.Cell) { Arrive(site, null); return; }
            state.hasTarget = true; state.targetIsIsland = true; state.targetIsland = site.Cell; state.interceptId = null;
            state.target = site.Center;
        }

        public void Intercept(MapParty party)
        {
            if (!RequireCommand() || party == null || party.employer) return;
            state.interceptId = party.id; state.hasTarget = false;
        }

        public void StopTravel()
        {
            if (state == null) return;
            state.hasTarget = false; state.interceptId = null;
        }

        private bool RequireCommand()
        {
            if (CanCommand) return true;
            if (OnJob) Notify("You are crew aboard the " + state.job.ship + ". You will be paid when she makes port.");
            else if (character != null && !character.OwnsShip) Notify("You have no ship. Find work or book passage in a port town.");
            return false;
        }

        /// <summary>Opens the menu of the island the party is currently moored at.</summary>
        public void OpenCurrentIslandMenu()
        {
            if (Live && Mode == TravelMode.Map && !OnJob && AtIsland(out ArchipelagoWorld.IslandSite site)) Arrive(site, null);
        }

        public bool CanEnter(ArchipelagoWorld.IslandSite site) =>
            Live && !OnJob && AtIsland(out ArchipelagoWorld.IslandSite current) && current.Cell == site.Cell;

        public float TravelDays(Vector2 to, float speed) =>
            Vector2.Distance(state != null ? state.position : Vector2.zero, to) / Mathf.Max(1f, speed) * HoursPerSecond / 24f;

        public float PlayerTravelDays(Vector2 to) => TravelDays(to, PlayerSpeed);

        // ───────────────────────────── AI parties ─────────────────────────────

        private int CampaignTier() => Mathf.Clamp(1 + economy.Campaign.victories / 3, 1, 5);

        private void MaintainPopulation(bool initial)
        {
            if (ports.Count == 0) return;
            int merchants = 0, navy = 0, pirates = 0;
            foreach (MapParty party in state.parties)
            {
                if (party.employer) continue;
                if (party.faction == MapFaction.Merchant) merchants++;
                else if (party.faction == MapFaction.Navy) navy++;
                else pirates++;
            }
            int merchantTarget = Mathf.Clamp(ports.Count / 2 + 2, 3, 7);
            int navyTarget = Mathf.Clamp(ports.Count / 4 + 1, 2, 4);
            int pirateTarget = Mathf.Clamp(3 + CampaignTier() / 2, 3, 6);
            if (merchants < merchantTarget) SpawnParty(MapFaction.Merchant, initial);
            else if (pirates < pirateTarget) SpawnParty(MapFaction.Pirate, initial);
            else if (navy < navyTarget) SpawnParty(MapFaction.Navy, initial);
        }

        private void SpawnParty(MapFaction faction, bool initial)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                ArchipelagoWorld.IslandSite origin;
                if (faction == MapFaction.Pirate)
                {
                    List<ArchipelagoWorld.IslandSite> havens = islands.FindAll(site => site.Kind == IslandKind.PirateBaseSite);
                    if (havens.Count == 0) havens = islands.FindAll(site => site.Kind != IslandKind.PortSite);
                    if (havens.Count == 0) return;
                    origin = havens[random.Next(havens.Count)];
                }
                else
                {
                    List<ArchipelagoWorld.IslandSite> pool = faction == MapFaction.Navy ?
                        ports.FindAll(site => BoatshipsFactions.OfPort(site.Cell) == 1) : ports;
                    if (pool.Count == 0) pool = ports;
                    origin = pool[random.Next(pool.Count)];
                }
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector2 position = Coast(origin, origin.Center + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 50f, 8f);
                float minimum = faction == MapFaction.Pirate ? 160f : initial ? 30f : 110f;
                if ((position - state.position).sqrMagnitude < minimum * minimum) continue;
                int tier = faction == MapFaction.Merchant ? 1 + random.Next(2) :
                    faction == MapFaction.Navy ? 2 + random.Next(3) :
                    Mathf.Clamp(CampaignTier() + random.Next(-1, 2), 1, 5);
                MapParty party = new MapParty
                {
                    id = "party_" + state.serial++,
                    name = BoatshipsNames.Ship(random),
                    faction = faction,
                    tier = tier,
                    position = position,
                    speed = SpeedOf(faction, tier) * Mathf.Lerp(0.92f, 1.06f, (float)random.NextDouble()),
                    destination = origin.Cell
                };
                Retarget(party);
                state.parties.Add(party);
                return;
            }
        }

        private static float SpeedOf(MapFaction faction, int tier) =>
            faction == MapFaction.Merchant ? 15f : faction == MapFaction.Navy ? 19f : 15.5f + tier * 1.3f;

        private void Retarget(MapParty party)
        {
            if (party.faction == MapFaction.Pirate)
            {
                List<ArchipelagoWorld.IslandSite> nearby = islands.FindAll(site =>
                    site.Cell != party.destination && (site.Center - party.position).sqrMagnitude < 260f * 260f);
                if (nearby.Count == 0) nearby = islands;
                if (nearby.Count > 0) party.destination = nearby[random.Next(nearby.Count)].Cell;
                return;
            }
            if (ports.Count < 2) return;
            Vector2Int current = party.destination;
            for (int i = 0; i < 6 && party.destination == current; i++)
                party.destination = ports[random.Next(ports.Count)].Cell;
        }

        private bool HostileToPlayer(MapParty party)
        {
            if (party.employer || state == null) return false;
            // Pirates prey on merchant and navy ships; the Navy hunts pirate ships.
            if (OnJob) return state.job.employer == MapFaction.Pirate ?
                party.faction == MapFaction.Navy : party.faction == MapFaction.Pirate;
            if (!character.OwnsShip) return false;
            return party.faction == MapFaction.Pirate ||
                (party.faction == MapFaction.Navy && economy.Campaign.navyReputation <= -20);
        }

        private void UpdateParties(float deltaTime)
        {
            bool playerSafe = PlayerSafe();
            for (int i = state.parties.Count - 1; i >= 0; i--)
            {
                if (i >= state.parties.Count) continue;
                MapParty party = state.parties[i];
                party.cooldown = Mathf.Max(0f, party.cooldown - deltaTime);
                party.chasing = false;
                Vector2 goal;
                if (!playerSafe && party.cooldown <= 0f && HostileToPlayer(party))
                {
                    float detect = (party.faction == MapFaction.Pirate ? 78f : 70f) * (OnJob ? 1f : character.PirateDeterrence);
                    party.chasing = (party.position - state.position).sqrMagnitude < detect * detect;
                }
                if (party.chasing) goal = state.position;
                else
                {
                    if (!world.TryGetIsland(party.destination, out ArchipelagoWorld.IslandSite destination))
                    { Retarget(party); continue; }
                    goal = destination.Center;
                    if (Vector2.Distance(party.position, destination.Center) <= destination.Radius * 0.78f + ArrivalMargin)
                    {
                        if (party.employer) { CompleteJob(party, destination); return; }
                        Retarget(party);
                        continue;
                    }
                }
                float speed = party.speed * (party.chasing ? 1.04f : 1f);
                party.position = Step(party.position, goal, speed * deltaTime, out Vector2 heading);
                party.heading = heading;
            }
        }

        private void CheckContacts()
        {
            bool playerSafe = PlayerSafe();
            foreach (MapParty party in state.parties)
            {
                if (party.employer || (party.position - state.position).sqrMagnitude > ContactDistance * ContactDistance) continue;
                if (state.interceptId == party.id) { HailMenu(party); return; }
                if (playerSafe || party.cooldown > 0f || !HostileToPlayer(party)) continue;
                if (OnJob) CrewBattleMenu(party); else ThreatMenu(party);
                return;
            }
        }

        private MapParty FindParty(string id)
        {
            if (state == null || string.IsNullOrEmpty(id)) return null;
            foreach (MapParty party in state.parties) if (party.id == id) return party;
            return null;
        }

        public static string Describe(MapParty party)
        {
            string type = party.faction == MapFaction.Merchant ? "merchant cog" :
                party.faction == MapFaction.Navy ? "navy cutter" : party.tier <= 2 ? "pirate raider" : "pirate brigantine";
            return "the " + type + " '" + party.name + "'";
        }

        public static string FactionName(MapFaction faction) =>
            faction == MapFaction.Merchant ? "Merchant League" : faction == MapFaction.Navy ? "Royal Navy" : "Pirates";

        // ───────────────────────────── Upkeep ─────────────────────────────

        private void DailyUpkeep()
        {
            if (!Captain) { state.starving = false; return; }
            // Prisoners are kept on short rations.
            int need = 1 + economy.Crew.Count / 4 + (economy.Prisoners.Count + 5) / 6;
            EnforcePrisonerCapacity("had no room below deck and were put over the side in a longboat");
            bool fed = economy.ConsumeRations(need);
            if (!fed && !state.starving) Notify("Your crew is out of rations! The ship slows. Buy rations at a port store.");
            state.starving = !fed;
        }

        // ───────────────────────────── Arrival menus ─────────────────────────────

        private void Arrive(ArchipelagoWorld.IslandSite site, string prefix)
        {
            state.hasTarget = false; state.interceptId = null;
            if (site.Kind == IslandKind.PortSite) { state.lastPort = site.Cell; TownMenu(site, prefix); }
            else IslandMenu(site, prefix);
            Save();
        }

        private void TownMenu(ArchipelagoWorld.IslandSite site, string prefix)
        {
            int faction = BoatshipsFactions.OfPort(site.Cell);
            string body = (string.IsNullOrEmpty(prefix) ? "" : prefix + "\n\n") +
                "A " + BoatshipsFactions.Name(faction) + " port. Your standing here: " +
                BoatshipsFactions.Reputation(economy.Campaign, faction) + ".\n" +
                "Gold " + economy.Gold + "   Rations " + economy.Rations + "   Crew " + economy.Crew.Count + "\n" +
                (character.OwnsShip ? "Your sloop is moored in the harbor." : "You have no ship of your own.");
            var options = new List<MapMenuOption>();
            if (fps != null && town != null)
                options.Add(new MapMenuOption("Walk into town (first person)", () => EnterTown(site)));
            if (character.OwnsShip)
                options.Add(new MapMenuOption("Go to the harbor (store, tavern, shipyard)", () => EnterPort(site)));
            else
            {
                options.Add(new MapMenuOption("Tavern: look for work aboard a ship", () => JobsMenu(site, () => TownMenu(site, null))));
                options.Add(new MapMenuOption("Docks: book passage to another port", () => PassageMenu(site, () => TownMenu(site, null))));
                int price = ShipPrice;
                options.Add(new MapMenuOption("Shipwright: buy a used sloop (" + price + " gold)",
                    () => BuyShip(site), economy.Gold >= price));
            }
            if (inventory != null && inventory.Ready)
                options.Add(new MapMenuOption("Market and armourer (trade goods, weapons, armour)", () => StoreMenu(site)));
            int taken = economy.CapturedShips().Count;
            if (taken > 0)
                options.Add(new MapMenuOption("Your company (" + taken + " ship" + (taken == 1 ? "" : "s") + "): captains and sales",
                    () => CompanyMenu(() => TownMenu(site, null))));
            if (economy.Prisoners.Count > 0)
                options.Add(new MapMenuOption("Prisoners (" + economy.Prisoners.Count + "): hand over for bounty, recruit or release",
                    () => PrisonersMenu(true, () => TownMenu(site, null))));
            options.Add(new MapMenuOption("Character", () => CharacterSheet(() => TownMenu(site, null))));
            if (inventory != null && inventory.Ready)
                options.Add(new MapMenuOption("Inventory and ship's hold", () => inventory.InventoryMenu(() => TownMenu(site, null))));
            options.Add(new MapMenuOption("Leave town", null));
            view.ShowMenu(BoatshipsNames.Island(site), body, options);
        }

        private void IslandMenu(ArchipelagoWorld.IslandSite site, string prefix)
        {
            string description;
            switch (site.Kind)
            {
                case IslandKind.PirateBaseSite: description = "A pirate haven. Smoke rises from hidden coves and black sails ride at anchor."; break;
                case IslandKind.TreasureSite: description = "Old charts mark this island. Rumour says treasure washed up along its shore."; break;
                default: description = "A quiet, uninhabited " + site.Biome.ToString().ToLowerInvariant() + " " +
                    ProceduralIslandChunk.ShapeName(site) + ". " + ProceduralIslandChunk.ShapeDescription(site); break;
            }
            var options = new List<MapMenuOption>();
            if (character.OwnsShip && !OnJob)
            {
                options.Add(new MapMenuOption("Sail in close and explore the coast", () => EnterIsland(site, false)));
                if (site.Kind == IslandKind.PirateBaseSite)
                {
                    bool recent = state.raids.Exists(raid => raid.cell == site.Cell && state.hours - raid.hour < RaidCooldownHours);
                    int tier = Mathf.Clamp(CampaignTier() + 1, 1, 5);
                    options.Add(new MapMenuOption(recent ? "The haven is still licking its wounds" :
                        "Raid the pirate haven (tier " + tier + ")", () => EnterIsland(site, true), !recent));
                }
            }
            options.Add(new MapMenuOption("Return to the map", null));
            view.ShowMenu(BoatshipsNames.Island(site), (string.IsNullOrEmpty(prefix) ? "" : prefix + "\n\n") + description, options);
        }

        public void CharacterSheet(Action back)
        {
            if (!character.HasCharacter) return;
            CharacterProfile profile = character.Profile;
            CampaignProgress progress = economy.Campaign;
            string text = CharacterBackgrounds.Get(profile.background).Title + " from the " +
                BoatshipsFactions.Name(profile.home) + "\n" +
                (character.OwnsShip ? "Captain of your own sloop" : OnJob ? "Crew aboard the " + state.job.ship : "No ship") +
                "   •   Voyages worked: " + profile.voyagesWorked + "\n\n";
            for (int i = 0; i < CharacterProfile.SkillCount; i++)
            {
                CharacterSkill skill = (CharacterSkill)i;
                int level = character.Skill(skill);
                text += skill + "  " + level + (level >= BoatshipsCharacter.MaxSkill ? "  (master)" :
                    "   xp " + character.SkillXp(skill) + "/" + BoatshipsCharacter.XpForNextLevel(level)) + "\n";
            }
            text += "\nReputation — Merchant League " + progress.merchantReputation + ", Royal Navy " +
                progress.navyReputation + ", Free Ports " + progress.freePortReputation + "\nRenown " + progress.reputation +
                "   Victories " + progress.victories + "   Captures " + progress.captures;
            view.ShowMenu(character.Name, text, new List<MapMenuOption> { new MapMenuOption("Close", back) });
        }

        // ───────────────────────────── Jobs and passage ─────────────────────────────

        public int ShipPrice => Mathf.RoundToInt(600f * (1f - character.Skill(CharacterSkill.Trade) * 0.03f));

        public List<JobOffer> JobOffers(ArchipelagoWorld.IslandSite port)
        {
            var offers = new List<JobOffer>();
            if (ports.Count < 2) return offers;
            int faction = BoatshipsFactions.OfPort(port.Cell);
            MapFaction[] employers = faction == 0 ? new[] { MapFaction.Merchant, MapFaction.Merchant, MapFaction.Navy } :
                faction == 1 ? new[] { MapFaction.Navy, MapFaction.Merchant, MapFaction.Navy } :
                new[] { MapFaction.Pirate, MapFaction.Merchant, MapFaction.Pirate };
            var rng = new System.Random(Hash(world.Seed, port.Cell.x, port.Cell.y, Day));
            int leadership = character.Skill(CharacterSkill.Leadership);
            foreach (MapFaction employer in employers)
            {
                ArchipelagoWorld.IslandSite destination = port;
                for (int i = 0; i < 8 && destination.Cell == port.Cell; i++) destination = ports[rng.Next(ports.Count)];
                if (destination.Cell == port.Cell) continue;
                int role = rng.Next(RoleNames.Length);
                CharacterSkill skill = RoleSkills[role];
                float distance = Vector2.Distance(port.Center, destination.Center);
                float multiplier = employer == MapFaction.Merchant ? 1f : employer == MapFaction.Navy ? 1.25f : 1.8f;
                offers.Add(new JobOffer
                {
                    Employer = employer,
                    Ship = BoatshipsNames.Ship(rng),
                    Role = RoleNames[role],
                    Skill = skill,
                    Destination = destination,
                    Pay = Mathf.RoundToInt((20f + distance * 0.12f) * multiplier *
                        (1f + character.Skill(skill) * 0.08f + leadership * 0.03f)),
                    Days = distance / SpeedOf(employer, 2) * HoursPerSecond / 24f,
                    Risk = employer == MapFaction.Merchant ? "low" : employer == MapFaction.Navy ? "moderate" : "high"
                });
            }
            return offers;
        }

        private void JobsMenu(ArchipelagoWorld.IslandSite port, Action back)
        {
            var options = new List<MapMenuOption>();
            foreach (JobOffer offer in JobOffers(port))
            {
                JobOffer chosen = offer;
                options.Add(new MapMenuOption(offer.Ship + " (" + FactionName(offer.Employer) + ") to " +
                    BoatshipsNames.Island(offer.Destination) + " — " + offer.Role + " — " + offer.Pay + " gold, risk " + offer.Risk,
                    () => AcceptJob(chosen, port, MapJobKind.Crew, 0)));
            }
            options.Add(new MapMenuOption("Back", back));
            view.ShowMenu("Tavern — Work aboard a ship",
                "Captains are looking for hands. You are paid when the ship reaches its destination, and the role's skill improves with the work. " +
                "If the ship is attacked, you will be asked to fight.\nNew postings appear every day.\n" +
                (options.Count == 1 ? "\nNo ships are hiring today." : ""), options);
        }

        public List<PassageOffer> PassageOffers(ArchipelagoWorld.IslandSite port)
        {
            var offers = new List<PassageOffer>();
            var others = ports.FindAll(site => site.Cell != port.Cell);
            others.Sort((a, b) => (a.Center - port.Center).sqrMagnitude.CompareTo((b.Center - port.Center).sqrMagnitude));
            float discount = 1f - character.Skill(CharacterSkill.Trade) * 0.04f;
            for (int i = 0; i < others.Count && i < 3; i++)
            {
                float distance = Vector2.Distance(port.Center, others[i].Center);
                offers.Add(new PassageOffer
                {
                    Destination = others[i],
                    Cost = Mathf.Max(5, Mathf.RoundToInt((8f + distance * 0.08f) * discount)),
                    Days = distance / SpeedOf(MapFaction.Merchant, 1) * HoursPerSecond / 24f
                });
            }
            return offers;
        }

        private void PassageMenu(ArchipelagoWorld.IslandSite port, Action back)
        {
            var options = new List<MapMenuOption>();
            foreach (PassageOffer offer in PassageOffers(port))
            {
                PassageOffer chosen = offer;
                options.Add(new MapMenuOption("To " + BoatshipsNames.Island(offer.Destination) + " — " + offer.Cost +
                    " gold (" + offer.Days.ToString("0.0") + " days)", () =>
                    {
                        if (!economy.TryAdjustGold(-chosen.Cost)) { Notify("You cannot afford the fare."); PassageMenu(port, back); return; }
                        var job = new JobOffer { Employer = MapFaction.Merchant, Ship = BoatshipsNames.Ship(random),
                            Role = "Passenger", Skill = CharacterSkill.Trade, Destination = chosen.Destination };
                        AcceptJob(job, port, MapJobKind.Passage, chosen.Cost);
                    }, economy.Gold >= offer.Cost));
            }
            options.Add(new MapMenuOption("Back", back));
            view.ShowMenu("Docks — Book passage", "Merchant ships take paying passengers. Your Trade skill lowers the fare.", options);
        }

        private void AcceptJob(JobOffer offer, ArchipelagoWorld.IslandSite origin, MapJobKind kind, int fare)
        {
            if (Mode == TravelMode.Town) ReturnToMap(false);
            MapParty employer = new MapParty
            {
                id = "party_" + state.serial++,
                name = offer.Ship,
                faction = offer.Employer,
                tier = offer.Employer == MapFaction.Merchant ? 1 : offer.Employer == MapFaction.Navy ? 3 : 2,
                speed = SpeedOf(offer.Employer, 2),
                destination = offer.Destination.Cell,
                position = Coast(origin, offer.Destination.Center, 8f),
                employer = true
            };
            state.parties.Add(employer);
            state.job = new MapJob
            {
                kind = kind, employer = offer.Employer, ship = offer.Ship, role = offer.Role, skill = offer.Skill,
                origin = origin.Cell, destination = offer.Destination.Cell, pay = kind == MapJobKind.Crew ? offer.Pay : 0,
                partyId = employer.id
            };
            state.position = employer.position;
            state.hasTarget = false; state.interceptId = null;
            Notify(kind == MapJobKind.Crew ?
                "You signed on as " + offer.Role.ToLowerInvariant() + " aboard the " + offer.Ship + ", bound for " +
                BoatshipsNames.Island(offer.Destination) + "." :
                "You paid " + fare + " gold for passage aboard the " + offer.Ship + ".");
            Save();
        }

        private void CompleteJob(MapParty employer, ArchipelagoWorld.IslandSite destination)
        {
            MapJob job = state.job;
            state.parties.Remove(employer);
            state.position = employer.position;
            state.job = new MapJob();
            string text;
            if (job.kind == MapJobKind.Crew)
            {
                economy.TryAdjustGold(job.pay);
                string level = character.AddExperience(job.skill, 45);
                character.CountVoyage();
                MapFaction worked = job.employer;
                economy.TryCampaignChange(progress =>
                {
                    if (worked == MapFaction.Merchant) progress.merchantReputation += 2;
                    else if (worked == MapFaction.Navy) progress.navyReputation += 3;
                    else { progress.navyReputation -= 4; progress.freePortReputation += 3; }
                });
                text = "The " + job.ship + " made port. You were paid " + job.pay + " gold for your work as " +
                    job.role.ToLowerInvariant() + "." + (level != null ? " " + level : "");
            }
            else text = "You stepped ashore from the " + job.ship + ".";
            Arrive(destination, text);
        }

        private void BuyShip(ArchipelagoWorld.IslandSite port)
        {
            int price = ShipPrice;
            bool walking = Mode == TravelMode.Town;
            if (character.OwnsShip || !economy.TryAdjustGold(-price))
            {
                if (walking) ShipwrightMenu(port, "The shipwright shakes his head."); else TownMenu(port, "The shipwright shakes his head.");
                return;
            }
            if (flooding != null) flooding.ResetShipForDemo();
            ShipCustomization fitting = ship != null ? ship.GetComponent<ShipCustomization>() : null;
            if (fitting != null) fitting.InstallStarterCannons(); // a new sloop comes with all four guns
            character.SetOwnsShip(true);
            character.ApplyShipColors();
            Save();
            const string bought = "You are now the captain of your own sloop! She is moored at the pier. Stock up on rations at the store before you sail.";
            if (walking) ShipwrightMenu(port, bought); else TownMenu(port, bought);
        }

        // ───────────────────────────── Encounters ─────────────────────────────

        private float EscapeChance(MapParty party)
        {
            float chance = 0.3f + character.Skill(CharacterSkill.Sailing) * 0.07f - (party.tier - 1) * 0.08f +
                (PlayerSpeed > party.speed ? 0.15f : 0f);
            return Mathf.Clamp(chance, 0.05f, 0.9f);
        }

        private string ShipSummary =>
            "Your ship: hull " + (docking.Hull != null && docking.Hull.TotalBlocks > 0 ?
                Mathf.RoundToInt(100f * docking.Hull.RemainingBlocks / docking.Hull.TotalBlocks) : 100) +
            "%   Crew " + economy.Crew.Count + "   Gold " + economy.Gold;

        private void ThreatMenu(MapParty party)
        {
            bool navy = party.faction == MapFaction.Navy;
            float chance = EscapeChance(party);
            int bribe = 35 * party.tier + 15;
            var options = new List<MapMenuOption>
            {
                new MapMenuOption("Engage them in battle", () => EnterBattle(party)),
                new MapMenuOption("Try to outrun them (" + Mathf.RoundToInt(chance * 100f) + "% chance)", () =>
                {
                    if (random.NextDouble() < chance)
                    {
                        party.cooldown = 20f;
                        Vector2 away = party.position - state.position;
                        party.position = KeepOnWater(party.position + (away.sqrMagnitude > 0.01f ? away.normalized : Vector2.up) * 12f);
                        string level = character.AddExperience(CharacterSkill.Sailing, 15);
                        Notify("You caught the wind and slipped away." + (level != null ? " " + level : ""));
                    }
                    else { Notify("They cut you off! Prepare for battle."); EnterBattle(party); }
                }),
                new MapMenuOption((navy ? "Surrender and pay a fine (" : "Pay them off (") + bribe + " gold)", () =>
                {
                    if (!economy.TryAdjustGold(-bribe)) { ThreatMenu(party); return; }
                    party.cooldown = 120f;
                    Notify(navy ? "The Navy takes your coin and lets you go — this time." : "The pirates count your gold and sheer off.");
                }, economy.Gold >= bribe)
            };
            view.ShowMenu(navy ? "The Navy has found you" : "Pirates!",
                Capitalize(Describe(party)) + " (tier " + party.tier + ") is bearing down on you" +
                (navy ? ", flying the Royal Navy ensign. They mean to arrest you." : ".") + "\n\n" + ShipSummary, options);
        }

        private void HailMenu(MapParty party)
        {
            state.interceptId = null;
            var options = new List<MapMenuOption>();
            string body;
            if (party.faction == MapFaction.Pirate)
            {
                body = "You have run down " + Describe(party) + " (tier " + party.tier + ").";
                options.Add(new MapMenuOption("Attack the pirates", () => EnterBattle(party)));
            }
            else if (party.faction == MapFaction.Merchant)
            {
                body = "You hail " + Describe(party) + ". Her crew eyes your guns nervously.";
                options.Add(new MapMenuOption("Attack them (Merchant League reputation -10)", () =>
                {
                    economy.TryCampaignChange(progress => progress.merchantReputation -= 10);
                    EnterBattle(party);
                }));
            }
            else
            {
                body = "You hail " + Describe(party) + ". The officers watch you through their spyglasses.";
                options.Add(new MapMenuOption("Attack them (Royal Navy reputation -15)", () =>
                {
                    economy.TryCampaignChange(progress => progress.navyReputation -= 15);
                    EnterBattle(party);
                }));
            }
            options.Add(new MapMenuOption("Leave them be", () => party.cooldown = 10f));
            view.ShowMenu(Capitalize(Describe(party)), body + "\n\n" + ShipSummary, options);
        }

        private void CrewBattleMenu(MapParty attacker)
        {
            MapParty employer = FindParty(state.job.partyId);
            if (employer == null) { state.job = new MapJob(); return; }
            bool passenger = state.job.kind == MapJobKind.Passage;
            view.ShowMenu("Attack on the " + employer.name + "!",
                Capitalize(Describe(attacker)) + " (tier " + attacker.tier + ") closes on " + Describe(employer) +
                ". The bosun hands out cutlasses" + (passenger ? " to passengers too." : ".") +
                "\n\nYour Fighting skill is " + character.Skill(CharacterSkill.Fighting) + ".",
                new List<MapMenuOption>
                {
                    new MapMenuOption("Fight alongside the crew (man the guns, repel boarders)", () => StartCrewBattle(attacker, employer)),
                    new MapMenuOption("Keep your head down", () => ResolveCrewBattle(attacker, employer, false))
                });
        }

        /// <summary>First-person fight aboard the employer's ship. Falls back to an auto-resolve if it cannot start.</summary>
        private void StartCrewBattle(MapParty attacker, MapParty employer)
        {
            if (crewBattle == null || fps == null) { ResolveCrewBattle(attacker, employer, true); return; }
            Mode = TravelMode.CrewBattle;
            SetMapPresentation(false);
            UpdateFlagshipVisibility();
            if (movement != null) movement.enabled = false;
            if (weapons != null) weapons.enabled = false;
            PlaceShip(KeepOnWater(state.position), attacker.position);
            float heading = Mathf.Atan2(employer.heading.x, employer.heading.y) * Mathf.Rad2Deg;
            bool started = crewBattle.Begin(employer.name, employer.tier, heading, attacker.tier, BattleKinds(attacker),
                ship.position, (won, fought, summary) =>
                {
                    Mode = TravelMode.Map;
                    UpdateFlagshipVisibility();
                    SetMapPresentation(true);
                    ApplyCrewBattleOutcome(attacker, employer, won, fought, summary);
                });
            if (started) return;
            Mode = TravelMode.Map;
            UpdateFlagshipVisibility();
            SetMapPresentation(true);
            ResolveCrewBattle(attacker, employer, true);
        }

        private void ResolveCrewBattle(MapParty attacker, MapParty employer, bool fight)
        {
            float own = employer.tier + (fight ? 0.7f * character.CrewCombatPower : 0.15f);
            float enemy = attacker.tier * 1.05f;
            bool won = random.NextDouble() < Mathf.Clamp(own / (own + enemy), 0.1f, 0.9f);
            ApplyCrewBattleOutcome(attacker, employer, won, fight, null);
        }

        private void ApplyCrewBattleOutcome(MapParty attacker, MapParty employer, bool won, bool fight, string summary)
        {
            string opening = string.IsNullOrEmpty(summary) ? "" : summary + " ";
            if (won)
            {
                state.parties.Remove(attacker);
                string text = opening + "The attackers were driven off!";
                if (fight)
                {
                    int bonus = 15 + 10 * attacker.tier;
                    economy.TryAdjustGold(bonus);
                    string level = character.AddExperience(CharacterSkill.Fighting, 30);
                    text += " The captain pays you a " + bonus + " gold bonus for your courage." + (level != null ? " " + level : "");
                }
                else text += " You kept out of harm's way while the crew did the fighting.";
                Save();
                view.ShowMenu("Victory", text, new List<MapMenuOption> { new MapMenuOption("Continue the voyage", null) });
                return;
            }
            state.parties.Remove(employer);
            bool arrested = state.job.employer == MapFaction.Pirate && attacker.faction == MapFaction.Navy;
            state.job = new MapJob();
            int lost = economy.Gold / 2;
            if (lost > 0) economy.TryAdjustGold(-lost);
            string experience = fight ? character.AddExperience(CharacterSkill.Fighting, 15) : null;
            if (!world.TryNearestIslandOfKind(new Vector3(state.position.x, 0f, state.position.y), IslandKind.PortSite,
                out ArchipelagoWorld.IslandSite port)) port = default;
            state.position = Coast(port, state.position, 8f);
            state.lastPort = port.Cell;
            Save();
            string story = opening + (arrested ?
                "The Navy seized the ship. You were fined " + lost + " gold and released at " + BoatshipsNames.Island(port) + "." :
                "The " + employer.name + " was taken. The attackers emptied your pockets (" + lost +
                " gold) and put you ashore at " + BoatshipsNames.Island(port) + ".");
            view.ShowMenu("Defeat", story + (experience != null ? "\n" + experience : ""),
                new List<MapMenuOption> { new MapMenuOption("Continue", () => TownMenu(port, null)) });
        }

        private static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private static List<EncounterKind> BattleKinds(MapParty party)
        {
            var kinds = new List<EncounterKind>();
            if (party.faction == MapFaction.Merchant) kinds.Add(EncounterKind.Brig);
            else if (party.faction == MapFaction.Navy)
            {
                kinds.Add(EncounterKind.Patrol);
                if (party.tier >= 3) kinds.Add(EncounterKind.Patrol);
            }
            else
            {
                kinds.Add(party.tier <= 2 ? EncounterKind.Raider : EncounterKind.Brig);
                if (party.tier >= 3) kinds.Add(EncounterKind.Raider);
                if (party.tier >= 5) kinds.Add(EncounterKind.Raider);
            }
            return kinds;
        }

        // ───────────────────────────── First-person town ─────────────────────────────

        private void EnterTown(ArchipelagoWorld.IslandSite site)
        {
            if (fps == null || town == null || OnJob) return;
            if (docking.IsDocked) docking.Undock();
            // Moor beside the town's pier.
            BoatshipsTown harbour = BoatshipsTown.For(site.Cell);
            Vector2 mooring = harbour != null ? harbour.Mooring : BoatshipsTown.MooringPoint(site);
            PlaceShip(KeepOnWater(mooring), site.Center);
            EnterLocal(TravelMode.Town, site, true);
            if (movement != null) movement.enabled = false;
            if (weapons != null) weapons.enabled = false;
            UpdateFlagshipVisibility();
            townSite = site;
            state.lastPort = site.Cell;
            if (harbour == null) harbour = BoatshipsTown.For(site.Cell); // the island may have streamed in after the move
            Vector3 spawn;
            float facing;
            IReadOnlyList<FpsInteractable> doors;
            if (harbour != null)
            {
                doors = harbour.Doors; spawn = harbour.Spawn; facing = harbour.Facing;
            }
            else
            {
                // Island not loaded: build a stand-in town at the same harbour spot.
                harbour = town;
                doors = town.Build(site, world.Seed, new Vector3(mooring.x, 0f, mooring.y), service => town.Handler?.Invoke(service),
                    out spawn, out facing);
            }
            activeTown = harbour;
            harbour.Handler = service => TownService(service);
            fps.BeginGround(spawn, facing);
            foreach (FpsInteractable door in doors) fps.AddInteractable(door);
            fps.SetExit("Leave town", () => ReturnToMap(false));
            fps.SetStatus(BoatshipsNames.Island(site));
            Notify("Welcome to " + BoatshipsNames.Island(site) + ". Walk up to a door to go inside.");
        }

        private void TownService(TownService service)
        {
            if (Mode != TravelMode.Town) return;
            switch (service)
            {
                case Boatships.TownService.Tavern: TavernMenu(townSite, null); break;
                case Boatships.TownService.Store: StoreMenu(townSite); break;
                case Boatships.TownService.Shipwright: ShipwrightMenu(townSite, null); break;
                default: HarborMenu(townSite); break;
            }
        }

        private void TavernMenu(ArchipelagoWorld.IslandSite site, string message)
        {
            var options = new List<MapMenuOption>();
            string body = (message != null ? message + "\n\n" : "") + "Sailors swap stories over cheap rum. Gold " + economy.Gold + ".";
            if (character.OwnsShip)
            {
                body += "\nHire sailors for your crew (" + economy.Crew.Count + "/24).";
                for (int i = 0; i < 3; i++)
                {
                    int slot = i;
                    PortEconomy.CrewOffer offer = economy.GetOffer(site, i);
                    bool hired = economy.IsRecruited(offer.Id);
                    options.Add(new MapMenuOption(hired ? offer.Name + " — already in your crew" :
                        "Hire " + offer.Name + ", " + offer.Role + " (skill " + offer.Skill + ") — " + offer.Cost + " gold",
                        () => { economy.Recruit(site, slot); TavernMenu(site, economy.LastMessage); },
                        !hired && economy.Gold >= offer.Cost));
                }
            }
            else options.Add(new MapMenuOption("Look for work aboard a ship", () => JobsMenu(site, () => TavernMenu(site, null))));
            options.Add(new MapMenuOption("Leave the tavern", null));
            view.ShowMenu("Tavern", body, options);
        }

        private void StoreMenu(ArchipelagoWorld.IslandSite site)
        {
            string[] goods = { "5 wood", "3 iron", "5 rations", "1 spare cannon" };
            var options = new List<MapMenuOption>();
            for (int i = 0; i < goods.Length; i++)
            {
                int slot = i;
                int price = economy.BuyPrice(i);
                options.Add(new MapMenuOption("Buy " + goods[i] + " — " + price + " gold",
                    () => { economy.Buy(slot); StoreMenu(site); }, economy.Gold >= price));
            }
            if (inventory != null && inventory.Ready)
            {
                options.Add(new MapMenuOption("Market: trade goods", () => inventory.MarketMenu(site, () => StoreMenu(site))));
                options.Add(new MapMenuOption("Armourer: weapons, armour, bandages", () => inventory.ArmourerMenu(site, () => StoreMenu(site))));
            }
            options.Add(new MapMenuOption("Leave the store", null));
            view.ShowMenu("General Store", economy.LastMessage + "\n\nGold " + economy.Gold + "   Wood " + economy.Wood +
                "   Iron " + economy.Iron + "   Rations " + economy.Rations + "   Spare cannons " + economy.SpareCannons, options);
        }

        private void ShipwrightMenu(ArchipelagoWorld.IslandSite site, string message)
        {
            var options = new List<MapMenuOption>();
            string body = (message != null ? message + "\n\n" : "") + "Sawdust, tar and the ring of hammers.";
            if (!character.OwnsShip)
            {
                int price = ShipPrice;
                options.Add(new MapMenuOption("Buy a used sloop (" + price + " gold)", () => BuyShip(site), economy.Gold >= price));
            }
            else
            {
                int gold = economy.RepairGoldCost(docking.Hull, docking.Flooding);
                int wood = economy.RepairWoodCost(docking.Hull);
                options.Add(new MapMenuOption(gold == 0 ? "Your hull needs no repairs" : "Repair the hull — " + gold + " gold, " + wood + " wood",
                    () => { economy.RepairShip(docking.Hull, docking.Flooding); ShipwrightMenu(site, economy.LastMessage); },
                    gold > 0 && economy.Gold >= gold && economy.Wood >= wood));
                options.Add(new MapMenuOption("Open the shipyard (build and refit your ship)", () => EnterPort(site)));
                int taken = economy.CapturedShips().Count;
                options.Add(new MapMenuOption(taken > 0 ? "Your company's ships — appoint captains or sell (" + taken + ")" :
                    "Sell a ship (you have no captured ships)", () => CompanyMenu(() => ShipwrightMenu(site, null)), taken > 0));
            }
            options.Add(new MapMenuOption("Leave", null));
            view.ShowMenu("Shipwright", body, options);
        }

        private void HarborMenu(ArchipelagoWorld.IslandSite site)
        {
            var options = new List<MapMenuOption>();
            if (character.OwnsShip)
            {
                options.Add(new MapMenuOption("Board your ship and set sail", () => ReturnToMap(false)));
                if (economy.Prisoners.Count > 0)
                    options.Add(new MapMenuOption("Hand over or deal with your prisoners (" + economy.Prisoners.Count + ")",
                        () => PrisonersMenu(true, () => HarborMenu(site))));
                options.Add(new MapMenuOption("Harbor office (contracts, missions, full harbor services)", () => EnterPort(site)));
            }
            else
            {
                options.Add(new MapMenuOption("Book passage to another port", () => PassageMenu(site, () => HarborMenu(site))));
                options.Add(new MapMenuOption("Leave town (back to the world map)", () => ReturnToMap(false)));
            }
            options.Add(new MapMenuOption("Stay in town", null));
            view.ShowMenu("Harbor Master", "Ships come and go with the tide. " +
                (character.OwnsShip ? "Your sloop is tied up at the pier." : "You have no ship of your own."), options);
        }

        // ───────────────────────────── Local 3D modes ─────────────────────────────

        private void EnterLocal(TravelMode mode, ArchipelagoWorld.IslandSite site, bool hasSite)
        {
            if (shipBattle != null) { shipBattle.End(); shipBattle.ResetAbandoned(); }
            Mode = mode;
            localIsland = site; hasLocalIsland = hasSite;
            battleIds.Clear(); battlePartyId = null; battleWon = false; raidBattle = false; prisonersOffered = false;
            SetMapPresentation(false);
            ShipCustomization shipyard = ship != null ? ship.GetComponent<ShipCustomization>() : null;
            if (mode != TravelMode.Port && shipyard != null && shipyard.InShipyard) shipyard.SetShipyard(false);
            if (movement != null) movement.enabled = true;
            if (weapons != null) weapons.enabled = true;
            director.RegroupFleet();
            character.ApplyShipColors();
            Save();
        }

        private void EnterPort(ArchipelagoWorld.IslandSite port)
        {
            if (!Captain) return;
            ExitFirstPerson();
            if (!docking.IsDocked) PlaceShip(KeepOnWater(BoatshipsTown.MooringPoint(port)), port.Center); // moor at the town pier
            EnterLocal(TravelMode.Port, port, true);
            if (!docking.IsDocked) docking.Dock();
            if (!docking.IsDocked) { Notify(docking.Status); ReturnToMap(false); }
        }

        private void EnterIsland(ArchipelagoWorld.IslandSite site, bool raid)
        {
            if (!Captain) return;
            PlaceShip(Coast(site, state.position, 14f), site.Center);
            EnterLocal(TravelMode.Island, site, true);
            if (!raid) { Notify("Exploring the coast of " + BoatshipsNames.Island(site) + ". Press M (World Map) to set sail again."); return; }
            int tier = Mathf.Clamp(CampaignTier() + 1, 1, 5);
            var kinds = new List<EncounterKind> { EncounterKind.Raider, EncounterKind.Brig };
            if (tier >= 4) kinds.Add(EncounterKind.Raider);
            battleIds.AddRange(director.SpawnBattle(kinds, tier, 40f));
            raidBattle = battleIds.Count > 0;
            raidCell = site.Cell;
            Mode = TravelMode.Battle;
            Notify(raidBattle ? "The haven's defenders put to sea!" : "The pirates are nowhere to be seen.");
        }

        private void EnterBattle(MapParty party)
        {
            if (!Captain || party == null) return;
            PlaceShip(KeepOnWater(state.position), party.position);
            EnterLocal(TravelMode.Battle, default, false);
            var kinds = new List<EncounterKind>();
            if (party.faction == MapFaction.Merchant) kinds.Add(EncounterKind.Brig);
            else if (party.faction == MapFaction.Navy)
            {
                kinds.Add(EncounterKind.Patrol);
                if (party.tier >= 3) kinds.Add(EncounterKind.Patrol);
            }
            else
            {
                kinds.Add(party.tier <= 2 ? EncounterKind.Raider : EncounterKind.Brig);
                if (party.tier >= 3) kinds.Add(EncounterKind.Raider);
                if (party.tier >= 5) kinds.Add(EncounterKind.Raider);
            }
            battleIds.AddRange(director.SpawnBattle(kinds, party.tier, 40f));
            if (battleIds.Count == 0) { Notify("The battle could not begin."); ReturnToMap(false); return; }
            battlePartyId = party.id;
            Notify("Battle! Sink or board " + Describe(party) + ". Sail 120m away to escape.");
        }

        private void WinBattle()
        {
            battleWon = true;
            MapParty party = FindParty(battlePartyId);
            if (party != null)
            {
                MapFaction beaten = party.faction;
                state.parties.Remove(party);
                if (beaten == MapFaction.Pirate)
                    economy.TryCampaignChange(progress => { progress.navyReputation += 3; progress.merchantReputation += 2; });
            }
            if (raidBattle) state.raids.Add(new MapRaid { cell = raidCell, hour = state.hours });
            string gunnery = character.AddExperience(CharacterSkill.Gunnery, 25);
            string leadership = character.AddExperience(CharacterSkill.Leadership, 10);
            Notify("Victory! Collect the floating cargo, then return to the World Map." +
                (gunnery != null ? " " + gunnery : "") + (leadership != null ? " " + leadership : ""));
            Save();
        }

        public bool CanLeaveLocal(out string reason)
        {
            reason = null;
            if (Mode == TravelMode.Map) return false;
            if (Mode == TravelMode.Port) { reason = "Use Depart in the harbor menu."; return false; }
            if (Mode == TravelMode.Town || Mode == TravelMode.CrewBattle) return false;
            if (flooding != null && flooding.IsSinking) { reason = "Your ship is sinking!"; return false; }
            director.NearestEnemy(out EncounterRecord record, out float distance);
            if (record != null && distance < LeaveDistance)
            {
                reason = "Enemy " + distance.ToString("0") + "m away — sink them or sail " + LeaveDistance.ToString("0") + "m clear.";
                return false;
            }
            return true;
        }

        public void LeaveLocal()
        {
            if (CanLeaveLocal(out string reason)) ReturnToMap(false);
            else Notify(reason);
        }

        public string LocalStatus
        {
            get
            {
                if (Mode == TravelMode.Map) return "";
                string place = hasLocalIsland ? BoatshipsNames.Island(localIsland) : "Open sea";
                if (Mode == TravelMode.Battle)
                {
                    int remaining = battleIds.Count > 0 ? director.RemainingHostiles(battleIds) : 0;
                    if (remaining == 0) return "Battle won — " + place;
                    director.NearestEnemy(out EncounterRecord nearest, out _);
                    int company = economy.CompanySize;
                    return "Battle — " + remaining + " enemy vessel(s) remaining" +
                        (nearest != null && nearest.crewMax > 0 ? "   Nearest crew " + nearest.crew + "/" + nearest.crewMax : "") +
                        (company > 0 ? "   Company " + director.CompanyEngaged + "/" + company + " engaged — orders: " +
                            director.Orders + " [O]" : "");
                }
                return place;
            }
        }

        private void ReturnToMap(bool fromPort)
        {
            ExitFirstPerson();
            bool fled = Mode == TravelMode.Battle && !battleWon && battleIds.Count > 0 && director.RemainingHostiles(battleIds) > 0;
            director.DismissUnresolved();
            MapParty party = FindParty(battlePartyId);
            if (fled && party != null)
            {
                party.cooldown = 25f;
                Vector2 away = party.position - state.position;
                party.position = KeepOnWater(party.position + (away.sqrMagnitude > 0.01f ? away.normalized : Vector2.up) * 15f);
                Notify("You escaped from " + Describe(party) + ".");
            }
            // Survivors nobody went back for are lost once you sail away.
            foreach (string id in battleIds) economy.LeaveSurvivors(id);
            battleIds.Clear(); battlePartyId = null; battleWon = false; raidBattle = false; prisonersOffered = false;
            if (pendingPrizes.Count > 0)
            {
                pendingPrizes.Clear();
                Notify("Your prizes follow under prize crews. Give them orders from Company (Fleet, L) or at any port.");
            }
            if (ship != null) state.position = KeepOnWater(new Vector2(ship.position.x, ship.position.z));
            Vector3 forward = ship != null ? ship.forward : Vector3.forward;
            if (new Vector2(forward.x, forward.z).sqrMagnitude > 0.01f) state.heading = new Vector2(forward.x, forward.z).normalized;
            state.hasTarget = false; state.interceptId = null;
            Mode = TravelMode.Map;
            SetMapPresentation(true);
            if (fromPort && hasLocalIsland) Notify("You set sail from " + BoatshipsNames.Island(localIsland) + ".");
            hasLocalIsland = false;
            Save();
        }

        public void AbandonShip() { if (ShowAbandonShip) TowToPort(); }

        private void TowToPort()
        {
            Vector3 from = ship != null ? ship.position : new Vector3(state.position.x, 0f, state.position.y);
            if (!world.TryNearestIslandOfKind(from, IslandKind.PortSite, out ArchipelagoWorld.IslandSite port)) return;
            director.DismissUnresolved();
            battleIds.Clear(); battlePartyId = null;
            PlaceShip(Coast(port, new Vector2(from.x, from.z), 6f), port.Center);
            EnterLocal(TravelMode.Port, port, true);
            if (!docking.IsDocked) docking.Dock();
            Notify("Your crew limped the stricken ship into " + BoatshipsNames.Island(port) + ". Repair her at the harbor.");
            if (!docking.IsDocked) ReturnToMap(false);
        }

        /// <summary>Editor play-validation helper: spawns a pirate beside the flagship and starts the battle.</summary>
        public void BeginTestBattle()
        {
            if (!Live || !Captain) return;
            if (Mode == TravelMode.Port && docking.IsDocked) docking.Undock();
            if (Mode != TravelMode.Map) ReturnToMap(false);
            var pirate = new MapParty { id = "party_" + state.serial++, name = "Test Raider", faction = MapFaction.Pirate,
                tier = 1, position = state.position + Vector2.up * 5f, speed = 15f, destination = state.lastPort };
            state.parties.Add(pirate);
            EnterBattle(pirate);
        }

        private static int Hash(int seed, int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)seed ^ 0x27D4EB2Du;
                h = (h ^ (uint)x) * 0x85EBCA6Bu;
                h = (h ^ (uint)y) * 0xC2B2AE35u;
                h = (h ^ (uint)z) * 0x9E3779B1u;
                h ^= h >> 15;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        // ───────────────────────────── Prizes and the company ─────────────────────────────

        private void OnPrizeTaken(EncounterRecord record)
        {
            if (record == null || pendingPrizes.Contains(record.id)) return;
            pendingPrizes.Add(record.id);
            string experience = character != null ? character.AddExperience(CharacterSkill.Leadership, 15) : null;
            bool fighting = battleIds.Count > 0 && director.RemainingHostiles(battleIds) > 0;
            Notify("Prize taken! Not one of her crew is left standing — " + record.DisplayName + " is yours." +
                (fighting ? " Give her orders once the battle is over." : "") + (experience != null ? " " + experience : ""));
        }

        private EncounterRecord FindCaptured(string id) =>
            economy.Campaign.encounters.Find(item => item.id == id && item.captured && !item.sold);

        private static string Percent(float fraction) => Mathf.RoundToInt(Mathf.Clamp01(fraction) * 100f) + "%";

        /// <summary>Orders for a freshly taken ship: put a captain aboard, sell her, or decide later.</summary>
        private void PrizeMenu(string id)
        {
            EncounterRecord prize = FindCaptured(id);
            if (prize == null) return;
            int value = prize.PrizeValue;
            string body = "The last of her crew has fallen and " + prize.DisplayName + " is yours, still afloat with " +
                Percent(prize.hullFraction) + " of her hull intact.\n\n" +
                "Appoint one of your sailors as her captain and she sails in your company, or sell her to a prize agent for " +
                value + " gold.\n\nGold " + economy.Gold + "   Company ships " + economy.CompanySize;
            view.ShowMenu("Prize Taken", body, new List<MapMenuOption>
            {
                new MapMenuOption("Take her into your company — appoint a captain", () => CaptainMenu(id, () => PrizeMenu(id)),
                    HasCaptainCandidates()),
                new MapMenuOption("Sell her — " + value + " gold", () => SellShip(id, null)),
                new MapMenuOption("Decide later (a prize crew sails her in your wake)", null)
            });
        }

        private bool HasCaptainCandidates()
        {
            foreach (PortEconomy.CrewMember member in economy.Crew) if (economy.CanCaptain(member)) return true;
            return false;
        }

        /// <summary>Choose which sailor leaves the flagship's crew to command the ship.</summary>
        private void CaptainMenu(string id, Action back)
        {
            EncounterRecord vessel = FindCaptured(id);
            if (vessel == null) return;
            var candidates = new List<PortEconomy.CrewMember>();
            foreach (PortEconomy.CrewMember member in economy.Crew) if (economy.CanCaptain(member)) candidates.Add(member);
            candidates.Sort((a, b) => b.skill.CompareTo(a.skill));
            var options = new List<MapMenuOption>();
            for (int i = 0; i < candidates.Count && i < 8; i++)
            {
                PortEconomy.CrewMember member = candidates[i];
                string key = member.offerKey;
                options.Add(new MapMenuOption(member.displayName + ", " + member.role + " (skill " + member.skill + ")", () =>
                {
                    bool appointed = economy.AppointCaptain(id, key);
                    Notify(economy.LastMessage);
                    if (!appointed) CaptainMenu(id, back);
                }));
            }
            options.Add(new MapMenuOption("Back", back));
            string current = vessel.InCompany ? "She is commanded by Captain " + vessel.captainName + ", who will rejoin your crew if replaced.\n\n" : "";
            view.ShowMenu("Appoint a Captain", current + "Who will command " + vessel.DisplayName + "? The sailor you choose leaves " +
                "your flagship's crew to take her helm. Your crew: " + economy.Crew.Count + ".", options);
        }

        private void SellShip(string id, Action after)
        {
            if (director.SellCaptured(id, out int price))
            {
                Notify(economy.LastMessage);
                EnforcePrisonerCapacity("held aboard her went with the sale");
                after?.Invoke();
            }
            else Notify("The sale fell through. " + economy.LastMessage);
        }

        /// <summary>Every ship taken in battle: captains, condition and value, with orders for each.</summary>
        public void CompanyMenu(Action back)
        {
            if (!Live) return;
            List<EncounterRecord> ships = economy.CapturedShips();
            var body = new System.Text.StringBuilder();
            body.Append("Your flagship and ").Append(ships.Count == 0 ? "no other ships." : ships.Count + " more:");
            foreach (EncounterRecord vessel in ships)
            {
                body.Append("\n• ").Append(Capitalize(vessel.DisplayName)).Append(" — ")
                    .Append(vessel.InCompany ? "Captain " + vessel.captainName + " (skill " + vessel.captainSkill + ")" : "prize crew, awaiting orders")
                    .Append(", hull ").Append(Percent(vessel.hullFraction)).Append(", worth ").Append(vessel.PrizeValue).Append(" gold");
            }
            if (ships.Count == 0) body.Append("\n\nTake an enemy intact by killing her entire crew in a boarding action, then give her a captain.");
            var options = new List<MapMenuOption>();
            foreach (EncounterRecord vessel in ships)
            {
                string id = vessel.id;
                options.Add(new MapMenuOption("Orders for " + vessel.DisplayName, () => ShipOrdersMenu(id, back)));
            }
            body.Append("\n\nBattle orders: ").Append(BoatshipsEncounterDirector.DescribeOrders(director.Orders))
                .Append(". Ships with a captain fight alongside you; prizes without one only follow.");
            body.Append("\n\nPrisoners ").Append(economy.Prisoners.Count).Append("/").Append(PrisonerCapacityNow());
            options.Add(new MapMenuOption("Change battle orders (now: " + director.Orders + ")", () =>
            {
                CycleCompanyOrders();
                CompanyMenu(back);
            }));
            options.Add(new MapMenuOption("Prisoners (" + economy.Prisoners.Count + ")", () => PrisonersMenu(false, () => CompanyMenu(back)),
                economy.Prisoners.Count > 0));
            options.Add(new MapMenuOption(back != null ? "Back" : "Close", back));
            view.ShowMenu("Your Company", body.ToString(), options);
        }

        private void ShipOrdersMenu(string id, Action back)
        {
            EncounterRecord vessel = FindCaptured(id);
            if (vessel == null) { CompanyMenu(back); return; }
            Action again = () => ShipOrdersMenu(id, back);
            int value = vessel.PrizeValue;
            view.ShowMenu(Capitalize(vessel.DisplayName),
                (vessel.InCompany ? "Commanded by Captain " + vessel.captainName + ", " + vessel.captainRole + " (skill " + vessel.captainSkill + ")."
                    : "Sailed by a prize crew. She needs a captain to join your company.") +
                "\nHull " + Percent(vessel.hullFraction) + ". A buyer would pay " + value + " gold.",
                new List<MapMenuOption>
                {
                    new MapMenuOption(vessel.InCompany ? "Replace her captain" : "Appoint a captain", () => CaptainMenu(id, again),
                        HasCaptainCandidates()),
                    new MapMenuOption("Sell her — " + value + " gold" + (vessel.InCompany ? " (her captain rejoins your crew)" : ""),
                        () => SellShip(id, () => CompanyMenu(back))),
                    new MapMenuOption("Back", () => CompanyMenu(back))
                });
        }

        private void OnDestroy()
        {
            if (director != null) { director.PrizeTaken -= OnPrizeTaken; director.CompanyShipLost -= Notify; }
            if (presentationApplied && mapPresentation) SetMapPresentation(false);
        }
    }
}
