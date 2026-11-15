using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    [Serializable]
    public sealed class ShipEquipmentRecord
    {
        public string instanceKey;
        public string itemId;
        public int x, z, rotationSteps;
        public string hardpointId;
        public bool paidFromInventory;
    }

    [Serializable]
    public sealed class ShipWeaponGroupRecord
    {
        public string instanceKey;
        public int group;
    }

    [Serializable]
    public sealed class ShipVoxelRecord
    {
        public int x, y, z;
        public float health;
        public bool removedFromDesign;
        public bool addedToDesign;
    }

    [Serializable]
    public sealed class ShipFloodRecord
    {
        public List<float> compartments = new List<float>();
        public List<int> patchedBreaches = new List<int>();
        public int repairKits;
        public bool isSinking;
        public float sinkDepth;
    }

    [Serializable]
    public sealed class ShipSaveRecord
    {
        public int saveVersion = 1;
        public int seed;
        public string shipId = "player_flagship";
        public string hullId;
        public Vector3 position;
        public float yaw;
        public List<ShipEquipmentRecord> installedEquipment = new List<ShipEquipmentRecord>();
        public List<ShipWeaponGroupRecord> weaponGroups = new List<ShipWeaponGroupRecord>();
        public List<ShipVoxelRecord> damagedVoxels = new List<ShipVoxelRecord>();
        public ShipFloodRecord flooding = new ShipFloodRecord();
    }
}
