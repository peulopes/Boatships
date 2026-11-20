using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Boatships
{
    public enum CharacterBackground { NobleHeir, MerchantsChild, Fisherman, Dockhand, EscapedPrisoner }
    public enum CharacterSkill { Sailing, Gunnery, Fighting, Trade, Leadership }
    /// <summary>Matches the port faction index used by the campaign journal.</summary>
    public enum HomeRegion { MerchantLeague = 0, RoyalNavy = 1, FreePorts = 2 }

    [Serializable]
    public sealed class CharacterProfile
    {
        public const int SkillCount = 5;
        public int version = 1;
        public string name = "";
        public CharacterBackground background;
        public HomeRegion home;
        public int[] skills = new int[SkillCount];
        public int[] skillXp = new int[SkillCount];
        public Color sailColor = new Color(0.95f, 0.9f, 0.72f);
        public Color flagColor = new Color(0.95f, 0.75f, 0.2f);
        public bool ownsShip;
        public bool homePortAssigned;
        public Vector2Int homePort;
        public int voyagesWorked;
    }

    public struct BackgroundInfo
    {
        public string Title;
        public string Description;
        public int Gold, Wood, Iron, Rations, Cannons, Crew;
        public bool Ship;
        public int[] SkillBonus;
        public int HomeReputation;
        public int NavyReputation;
    }

    public static class CharacterBackgrounds
    {
        public static readonly CharacterBackground[] All =
        {
            CharacterBackground.NobleHeir, CharacterBackground.MerchantsChild, CharacterBackground.Fisherman,
            CharacterBackground.Dockhand, CharacterBackground.EscapedPrisoner
        };

        public static BackgroundInfo Get(CharacterBackground background)
        {
            switch (background)
            {
                case CharacterBackground.NobleHeir:
                    return new BackgroundInfo
                    {
                        Title = "Noble Heir",
                        Description = "The second child of a proud house. You inherit a fitted sloop, a few loyal sailors and a heavy purse. Everyone expects you to live up to the family name.",
                        Gold = 1100, Wood = 14, Iron = 6, Rations = 14, Cannons = 1, Crew = 3, Ship = true,
                        SkillBonus = new[] { 0, 1, 0, 0, 2 }, HomeReputation = 15
                    };
                case CharacterBackground.MerchantsChild:
                    return new BackgroundInfo
                    {
                        Title = "Merchant's Child",
                        Description = "You grew up among ledgers and cargo holds. Your family gave you a small trading sloop to make your own fortune.",
                        Gold = 650, Wood = 10, Iron = 3, Rations = 10, Ship = true,
                        SkillBonus = new[] { 1, 0, 0, 2, 0 }, HomeReputation = 5
                    };
                case CharacterBackground.Fisherman:
                    return new BackgroundInfo
                    {
                        Title = "Fisherman",
                        Description = "You have worked the shoals since you could walk. Your old fishing sloop smells of tar and herring, but she floats, and you know every current.",
                        Gold = 180, Wood = 5, Iron = 1, Rations = 8, Ship = true,
                        SkillBonus = new[] { 3, 0, 0, 0, 0 }
                    };
                case CharacterBackground.Dockhand:
                    return new BackgroundInfo
                    {
                        Title = "Dockhand",
                        Description = "For years you loaded other people's ships. You have no vessel of your own, just strong arms and a few coins. Sign on as crew and work your way up to a deck of your own.",
                        Gold = 60, Rations = 0, Ship = false,
                        SkillBonus = new[] { 1, 0, 1, 1, 0 }
                    };
                default:
                    return new BackgroundInfo
                    {
                        Title = "Escaped Prisoner",
                        Description = "Three nights ago you slipped your chains and washed ashore with nothing. The Navy wants you back, but nobody asks questions aboard a pirate ship.",
                        Gold = 0, Rations = 0, Ship = false,
                        SkillBonus = new[] { 0, 0, 3, 0, 0 }, NavyReputation = -15
                    };
            }
        }
    }

    public static class BoatshipsFactions
    {
        public static int OfPort(Vector2Int cell) => Mathf.Abs(cell.x % 3 + cell.y % 3) % 3;
        public static string Name(int faction) =>
            faction == 0 ? "Merchant League" : faction == 1 ? "Royal Navy" : "Free Ports";
        public static string Name(HomeRegion region) => Name((int)region);

        public static int Reputation(CampaignProgress progress, int faction) =>
            faction == 0 ? progress.merchantReputation : faction == 1 ? progress.navyReputation : progress.freePortReputation;

        public static void AddReputation(CampaignProgress progress, int faction, int amount)
        {
            if (faction == 0) progress.merchantReputation += amount;
            else if (faction == 1) progress.navyReputation += amount;
            else progress.freePortReputation += amount;
        }
    }

    public static class BoatshipsNames
    {
        private static readonly string[] Front = { "Salt", "Gull", "Mar", "Thorn", "Brine", "Coral", "Raven", "Kest",
            "Ash", "Wyn", "Tide", "Mist", "Oak", "Fen", "Storm", "Cinder", "Frost", "Amber", "Heron", "Drift" };
        private static readonly string[] Back = { "mere", "haven", "wick", "holm", "reach", "ford", "cay", "port",
            "stead", "rock", "fall", "moor", "mouth", "key" };
        private static readonly string[] Ships = { "Gull", "Sea Lark", "Prosperity", "Grey Heron", "Saltwind",
            "Black Wake", "Red Maw", "Vigilant", "Resolute", "Kingfisher", "Marlin", "Driftwood", "Fortune",
            "Widow's Grin", "Iron Tide", "Morning Star", "Sable", "Petrel", "Cutlass", "Albatross" };
        private static readonly string[] Captains = { "Ada", "Nora", "Finn", "Isla", "Milo", "Rosa", "Kai", "Elin",
            "Jules", "Theo", "Mara", "Bram", "Wren", "Silas", "Tamsin", "Corin" };
        private static readonly string[] Surnames = { "Reef", "Storm", "Drake", "Marsh", "Stern", "Waves", "Hart",
            "Vale", "Moor", "Cross", "Blackwood", "Halyard", "Gale" };

        public static string Island(ArchipelagoWorld.IslandSite site)
        {
            uint h = (uint)site.DetailSeed;
            string name = Front[h % (uint)Front.Length] + Back[(h / 31u) % (uint)Back.Length];
            return site.Kind == IslandKind.PortSite ? "Port " + name : name;
        }

        public static string Ship(System.Random random) => Ships[random.Next(Ships.Length)];

        public static string RandomCaptain(System.Random random) =>
            Captains[random.Next(Captains.Length)] + " " + Surnames[random.Next(Surnames.Length)];
    }

    /// <summary>The player character: background, skills, home waters, colors and ship ownership.
    /// Stored per world in the unified checkpoint as "boatships_character_v1_{seed}.json".</summary>
    [DefaultExecutionOrder(650)]
    public sealed class BoatshipsCharacter : MonoBehaviour
    {
        public const int MaxSkill = 10;
        private static CharacterProfile pendingProfile;

        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private BoatshipsSaveCoordinator saves;
        [SerializeField] private Transform ship;
        private CharacterProfile profile;
        private int loadedSeed = int.MinValue;
        private bool loadFailed;
        private GameObject flag;
        private readonly List<Material> tintMaterials = new List<Material>();

        public static BoatshipsCharacter Active { get; private set; }
        public static bool HasPendingProfile => pendingProfile != null;
        public static void SetPendingProfile(CharacterProfile created) => pendingProfile = created;

        public bool Loaded => world != null && loadedSeed == world.Seed && pendingProfile == null;
        public bool HasCharacter => Loaded && profile != null;
        public CharacterProfile Profile => profile;
        public bool OwnsShip => profile != null && profile.ownsShip;
        public string Name => profile != null && !string.IsNullOrEmpty(profile.name) ? profile.name : "Captain";

        public static float TradeBonus => Active != null && Active.HasCharacter ? Active.Skill(CharacterSkill.Trade) * 0.01f : 0f;
        public static float SailingBonus => Active != null && Active.HasCharacter ? Active.Skill(CharacterSkill.Sailing) * 0.02f : 0f;
        public static float GunneryBonus => Active != null && Active.HasCharacter ? Active.Skill(CharacterSkill.Gunnery) * 0.03f : 0f;

        public float MapSpeedMultiplier => 1f + Skill(CharacterSkill.Sailing) * 0.05f;
        public float CrewCombatPower => 1f + Skill(CharacterSkill.Fighting) * 0.12f + Skill(CharacterSkill.Gunnery) * 0.05f;
        public float SightRadius => 120f + Skill(CharacterSkill.Sailing) * 8f;
        public float PirateDeterrence => Mathf.Clamp01(1f - Skill(CharacterSkill.Leadership) * 0.04f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { pendingProfile = null; Active = null; }

        public void Configure(ArchipelagoWorld map, PortEconomy account, BoatshipsSaveCoordinator checkpoints, Transform flagship)
        { world = map; economy = account; saves = checkpoints; ship = flagship; }

        private void Awake() { Active = this; }
        private void OnEnable() { BoatshipsSaveCoordinator.CheckpointCapturing += Save; }
        private void OnDisable() { BoatshipsSaveCoordinator.CheckpointCapturing -= Save; }

        public int Skill(CharacterSkill skill)
        {
            if (profile == null || profile.skills == null || (int)skill >= profile.skills.Length) return 0;
            return Mathf.Clamp(profile.skills[(int)skill], 0, MaxSkill);
        }

        public int SkillXp(CharacterSkill skill) =>
            profile != null && profile.skillXp != null && (int)skill < profile.skillXp.Length ? profile.skillXp[(int)skill] : 0;

        public static int XpForNextLevel(int level) => 50 + level * 30;

        private void Update()
        {
            if (world == null || !BoatshipsSaveStore.Enabled) return;
            if (loadedSeed != world.Seed && pendingProfile == null) Load();
            if (pendingProfile != null && saves != null && saves.IsReady && economy.ActiveSeed == world.Seed)
                ApplyPendingProfile();
        }

        private void Load()
        {
            loadedSeed = world.Seed;
            profile = null;
            loadFailed = false;
            string path = SavePath(loadedSeed);
            if (!BoatshipsSaveStore.Exists(path)) return;
            try
            {
                CharacterProfile loaded = JsonUtility.FromJson<CharacterProfile>(BoatshipsSaveStore.Read(path));
                if (loaded == null || loaded.version != 1) throw new InvalidDataException("Unsupported character version.");
                Normalize(loaded);
                profile = loaded;
                ApplyShipColors();
            }
            catch (Exception ex)
            {
                loadFailed = true;
                Debug.LogWarning("Boatships character could not be loaded: " + ex.Message, this);
            }
        }

        private static void Normalize(CharacterProfile value)
        {
            if (value.skills == null || value.skills.Length != CharacterProfile.SkillCount)
                Array.Resize(ref value.skills, CharacterProfile.SkillCount);
            if (value.skillXp == null || value.skillXp.Length != CharacterProfile.SkillCount)
                Array.Resize(ref value.skillXp, CharacterProfile.SkillCount);
        }

        private void ApplyPendingProfile()
        {
            CharacterProfile created = pendingProfile;
            pendingProfile = null;
            Normalize(created);
            loadedSeed = world.Seed;
            profile = created;
            BackgroundInfo info = CharacterBackgrounds.Get(created.background);
            created.ownsShip = info.Ship;
            economy.ApplyStartingKit(info.Gold, info.Wood, info.Iron, info.Rations, info.Cannons, info.Crew,
                new System.Random(world.Seed ^ 0x51ED));
            int home = (int)created.home;
            economy.TryCampaignChange(progress =>
            {
                BoatshipsFactions.AddReputation(progress, home, info.HomeReputation);
                progress.navyReputation += info.NavyReputation;
            });
            Save();
            ApplyShipColors();
        }

        public void Save()
        {
            if (profile == null || loadFailed || world == null || loadedSeed != world.Seed) return;
            try { BoatshipsSaveStore.Write(SavePath(loadedSeed), JsonUtility.ToJson(profile, true)); }
            catch (Exception ex) { Debug.LogWarning("Boatships character save failed: " + ex.Message, this); }
        }

        public void SetOwnsShip(bool owns)
        {
            if (profile == null) return;
            profile.ownsShip = owns;
            Save();
        }

        public void AssignHomePort(Vector2Int cell)
        {
            if (profile == null) return;
            profile.homePort = cell;
            profile.homePortAssigned = true;
            Save();
        }

        public void CountVoyage()
        {
            if (profile == null) return;
            profile.voyagesWorked++;
            Save();
        }

        /// <summary>Awards experience and returns a level-up message, or null.</summary>
        public string AddExperience(CharacterSkill skill, int amount)
        {
            if (profile == null || amount <= 0) return null;
            int index = (int)skill;
            string result = null;
            profile.skillXp[index] += amount;
            while (profile.skills[index] < MaxSkill && profile.skillXp[index] >= XpForNextLevel(profile.skills[index]))
            {
                profile.skillXp[index] -= XpForNextLevel(profile.skills[index]);
                profile.skills[index]++;
                result = skill + " increased to " + profile.skills[index] + "!";
            }
            if (profile.skills[index] >= MaxSkill) profile.skillXp[index] = 0;
            Save();
            return result;
        }

        /// <summary>Tints the flagship sails and raises a flag in the character's colors.</summary>
        public void ApplyShipColors()
        {
            if (profile == null || ship == null) return;
            Transform sail = null;
            foreach (Renderer renderer in ship.GetComponentsInChildren<Renderer>(true))
            {
                Material shared = renderer.sharedMaterial;
                if (shared == null || shared.name.IndexOf("Sail", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!tintMaterials.Contains(shared))
                {
                    Material tinted = new Material(shared) { name = "Captain Sail" };
                    tintMaterials.Add(tinted);
                    renderer.sharedMaterial = tinted;
                    shared = tinted;
                }
                shared.color = profile.sailColor;
                if (sail == null && renderer.name == "Sail") sail = renderer.transform;
            }
            if (sail == null) return;
            if (flag == null)
            {
                Renderer sailRenderer = sail.GetComponent<Renderer>();
                flag = new GameObject("Captain Flag");
                flag.transform.SetParent(sail.parent, false);
                flag.transform.localPosition = sail.localPosition + Vector3.up * (sail.localScale.y * 0.5f + 0.55f);
                GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pole.name = "Flag Pole";
                Destroy(pole.GetComponent<Collider>());
                pole.transform.SetParent(flag.transform, false);
                pole.transform.localScale = new Vector3(0.05f, 0.5f, 0.05f);
                GameObject cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cloth.name = "Flag Cloth";
                Destroy(cloth.GetComponent<Collider>());
                cloth.transform.SetParent(flag.transform, false);
                cloth.transform.localPosition = new Vector3(0f, 0.32f, -0.3f);
                cloth.transform.localScale = new Vector3(0.04f, 0.32f, 0.55f);
                Material flagMaterial = new Material(sailRenderer.sharedMaterial) { name = "Captain Flag" };
                tintMaterials.Add(flagMaterial);
                pole.GetComponent<Renderer>().sharedMaterial = flagMaterial;
                cloth.GetComponent<Renderer>().sharedMaterial = flagMaterial;
            }
            foreach (Renderer part in flag.GetComponentsInChildren<Renderer>())
            {
                part.sharedMaterial.color = profile.flagColor;
                break;
            }
        }

        private static string SavePath(int seed) =>
            Path.Combine(Application.persistentDataPath, "boatships_character_v1_" + seed + ".json");

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            foreach (Material material in tintMaterials) if (material != null) Destroy(material);
        }
    }
}
