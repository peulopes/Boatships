using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    public enum EncounterKind { Raider, Brig, Patrol }

    /// <summary>How an enemy sailor was put out of the fight. It decides how likely they are to live to be taken prisoner.</summary>
    public enum Incapacitation { Cannon, Blade, Firearm, Sinking }

    /// <summary>An enemy sailor held aboard one of the player's ships.</summary>
    [Serializable]
    public sealed class Prisoner
    {
        public string id;
        public string name;
        public int tier;
        public string ship;
        public Incapacitation wound;
        public int skill;
        /// <summary>Would sign on with the player's crew if asked.</summary>
        public bool willing;

        public string WoundText => DescribeWound(wound);

        public static string DescribeWound(Incapacitation how)
        {
            switch (how)
            {
                case Incapacitation.Cannon: return "wounded by cannon fire";
                case Incapacitation.Firearm: return "shot";
                case Incapacitation.Sinking: return "pulled from the water";
                default: return "beaten down by blades";
            }
        }

        /// <summary>Gold the harbour authorities pay to take them off your hands.</summary>
        public int Bounty => 15 + Mathf.Clamp(tier, 1, 5) * 10;
    }
    /// <summary>Standing battle orders for the company's ships.
    /// Engage: fight to sink. Cripple: cease fire on any enemy below 40% hull, so she can be boarded. HoldBack: keep station.</summary>
    public enum CompanyOrders { Engage, Cripple, HoldBack }

    public enum CampaignObjective { DefeatShips, DiscoverIslands, DeliverSupplies, CaptureShip }

    [Serializable]
    public sealed class EncounterRecord
    {
        public string id;
        public EncounterKind kind;
        public int tier;
        public Vector3 position;
        public float yaw;
        public bool defeated;
        public bool captured;
        public float defenderHealth = 100f;
        public List<ShipVoxelRecord> damage = new List<ShipVoxelRecord>();
        public ShipFloodRecord flooding;

        // ── Crew and prize state ──
        /// <summary>Sailors still alive aboard. -1 = not yet mustered (older saves). Killing all of them takes the ship intact.</summary>
        public int crew = -1;
        public int crewMax;
        /// <summary>Hull integrity (0..1) recorded when the ship was taken, used to price her.</summary>
        public float hullFraction = 1f;
        public string shipName;
        /// <summary>Crew key (PortEconomy.CrewMember.offerKey) of the sailor appointed to command this company ship.</summary>
        public string captainKey;
        public string captainName;
        public string captainRole;
        public int captainSkill;
        public bool sold;

        // ── Casualties and survivors ──
        /// <summary>Sailors put out of the fight, for any reason.</summary>
        public int casualties;
        /// <summary>Wounded survivors who can still be taken prisoner, by how they were incapacitated.</summary>
        public int survivedCannon, survivedBlade, survivedFirearm, survivedSinking;
        /// <summary>The survivors have been dealt with (taken, or left behind).</summary>
        public bool prisonersResolved;

        public int Survivors => survivedCannon + survivedBlade + survivedFirearm + survivedSinking;

        public static float SurvivalChance(Incapacitation how)
        {
            switch (how)
            {
                case Incapacitation.Cannon: return 0.2f;   // round shot and flying splinters rarely leave a man alive
                case Incapacitation.Firearm: return 0.35f;
                case Incapacitation.Sinking: return 0.45f; // those who can swim, and are found in time
                default: return 0.65f;                     // a sailor beaten down by cutlasses usually lives
            }
        }

        /// <summary>Records sailors put out of the fight and rolls who survives to be taken prisoner. Returns the survivors.</summary>
        public int Incapacitate(int count, Incapacitation how)
        {
            int survived = 0;
            for (int i = 0; i < count; i++)
            {
                casualties++;
                if (UnityEngine.Random.value >= SurvivalChance(how)) continue;
                survived++;
                switch (how)
                {
                    case Incapacitation.Cannon: survivedCannon++; break;
                    case Incapacitation.Firearm: survivedFirearm++; break;
                    case Incapacitation.Sinking: survivedSinking++; break;
                    default: survivedBlade++; break;
                }
            }
            return survived;
        }

        /// <summary>Removes one survivor (the fittest first) to become a prisoner, returning how they were wounded.</summary>
        public bool TakeSurvivor(out Incapacitation how)
        {
            if (survivedBlade > 0) { survivedBlade--; how = Incapacitation.Blade; return true; }
            if (survivedSinking > 0) { survivedSinking--; how = Incapacitation.Sinking; return true; }
            if (survivedFirearm > 0) { survivedFirearm--; how = Incapacitation.Firearm; return true; }
            if (survivedCannon > 0) { survivedCannon--; how = Incapacitation.Cannon; return true; }
            how = Incapacitation.Blade;
            return false;
        }

        public string SurvivorBreakdown
        {
            get
            {
                var parts = new List<string>();
                if (survivedBlade > 0) parts.Add(survivedBlade + " " + Prisoner.DescribeWound(Incapacitation.Blade));
                if (survivedFirearm > 0) parts.Add(survivedFirearm + " " + Prisoner.DescribeWound(Incapacitation.Firearm));
                if (survivedSinking > 0) parts.Add(survivedSinking + " " + Prisoner.DescribeWound(Incapacitation.Sinking));
                if (survivedCannon > 0) parts.Add(survivedCannon + " " + Prisoner.DescribeWound(Incapacitation.Cannon));
                return parts.Count == 0 ? "no survivors" : string.Join(", ", parts);
            }
        }

        /// <summary>Prisoners this ship can hold once she sails in the player's company.</summary>
        public int PrisonerCapacity =>
            (kind == EncounterKind.Brig ? 8 : kind == EncounterKind.Patrol ? 5 : 3) + Mathf.Clamp(tier, 1, 5);

        /// <summary>Captured and given a captain: a ship of the player's company.</summary>
        public bool InCompany => captured && !string.IsNullOrEmpty(captainKey);
        /// <summary>Captured, sailed by a prize crew, waiting for the player to appoint a captain or sell her.</summary>
        public bool AwaitingOrders => captured && string.IsNullOrEmpty(captainKey);

        public string DisplayName => (string.IsNullOrEmpty(shipName) ? "" : "the " + shipName + ", ") + KindName(kind) + " (tier " + tier + ")";

        public static string KindName(EncounterKind kind) =>
            kind == EncounterKind.Brig ? "brig" : kind == EncounterKind.Patrol ? "patrol cutter" : "raider";

        public static int DefaultCrew(EncounterKind kind, int tier)
        {
            int baseCrew = kind == EncounterKind.Brig ? 9 : kind == EncounterKind.Patrol ? 7 : 5;
            return baseCrew + Mathf.Clamp(tier, 1, 5) * 2;
        }

        /// <summary>Gives older records (and freshly created ones) a crew and a name.</summary>
        public void Muster(System.Random random)
        {
            if (crew < 0)
            {
                crewMax = DefaultCrew(kind, tier);
                crew = crewMax;
            }
            if (crewMax < crew) crewMax = crew;
            if (string.IsNullOrEmpty(shipName)) shipName = BoatshipsNames.Ship(random);
        }

        /// <summary>What a prize agent or shipwright pays for her, by type, tier and condition.</summary>
        public int PrizeValue
        {
            get
            {
                int basePrice = kind == EncounterKind.Brig ? 420 : kind == EncounterKind.Patrol ? 340 : 260;
                float condition = 0.4f + 0.6f * Mathf.Clamp01(hullFraction);
                return Mathf.RoundToInt(basePrice * (1f + 0.35f * (Mathf.Clamp(tier, 1, 5) - 1)) * condition / 5f) * 5;
            }
        }
    }

    [Serializable]
    public sealed class CampaignProgress
    {
        public int version = 1;
        public int victories;
        public int captures;
        public int reputation;
        public int merchantReputation;
        public int navyReputation;
        public int freePortReputation;
        public int hullLevel;
        public int cannonLevel;
        public int sailLevel;
        public int encounterSerial;
        public CompanyOrders companyOrders;
        public int contractStep;
        public bool contractAccepted;
        public int contractBaseline;
        public Vector2Int deliveryPort;
        public bool cargoLoaded;
        public List<Vector2Int> discovered = new List<Vector2Int>();
        public List<EncounterRecord> encounters = new List<EncounterRecord>();
    }
}
