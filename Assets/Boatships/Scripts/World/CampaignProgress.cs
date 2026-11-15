using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    public enum EncounterKind { Raider, Brig, Patrol }
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
        public int contractStep;
        public bool contractAccepted;
        public int contractBaseline;
        public Vector2Int deliveryPort;
        public bool cargoLoaded;
        public List<Vector2Int> discovered = new List<Vector2Int>();
        public List<EncounterRecord> encounters = new List<EncounterRecord>();
    }
}
