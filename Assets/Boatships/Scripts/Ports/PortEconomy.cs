using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// Small persistent single-player economy for the port prototype.
    /// Only gold/supplies/crew are saved; this is NOT yet a full ship/world save system.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortEconomy : MonoBehaviour
    {
        [Serializable]
        public sealed class CrewMember
        {
            public string offerKey;
            public string displayName;
            public string role;
            public int skill;
            public int wage;
        }

        [Serializable]
        public sealed class LootCache
        {
            public string id;
            public float x;
            public float z;
            public int gold;
            public int wood;
            public int iron;
            public int rations;
            public int spareCannons;

            public string Describe() => "+" + gold + " gold, +" + wood + " wood, +" +
                iron + " iron, +" + rations + " rations, +" + spareCannons + " cannons";
        }

        [Serializable]
        private sealed class SaveState
        {
            public int gold = 650;
            public int wood = 10;
            public int iron = 3;
            public int rations = 8;
            public int spareCannons;
            public List<CrewMember> crew = new List<CrewMember>();
            public List<string> recruitedOfferKeys = new List<string>();
            public List<LootCache> pendingLoot = new List<LootCache>();
            public List<string> collectedLootIds = new List<string>();
        }

        public struct CrewOffer
        {
            public string Id;
            public string Name;
            public string Role;
            public int Skill;
            public int Cost;
            public int Wage;
        }

        private static readonly string[] FirstNames = { "Ada", "Nora", "Finn", "Isla", "Milo", "Rosa", "Kai", "Elin", "Jules", "Theo" };
        private static readonly string[] LastNames = { "Reef", "Storm", "Drake", "Marsh", "Stern", "Waves", "Hart", "Vale", "Moor", "Cross" };
        private static readonly string[] Roles = { "Gunner", "Carpenter", "Sailor", "Navigator", "Medic", "Fighter" };

        private SaveState state = new SaveState();
        private int activeSeed = int.MinValue;

        public int Gold => state.gold;
        public int Wood => state.wood;
        public int Iron => state.iron;
        public int Rations => state.rations;
        public int SpareCannons => state.spareCannons;
        public IReadOnlyList<CrewMember> Crew => state.crew;
        public int ActiveSeed => activeSeed;
        public string LastMessage { get; private set; } = "Welcome to the harbor.";

        public void UseWorldSeed(int seed)
        {
            if (activeSeed == seed) return;
            activeSeed = seed;
            state = new SaveState();
            string path = SavePath(seed);
            if (File.Exists(path))
            {
                try
                {
                    SaveState loaded = JsonUtility.FromJson<SaveState>(File.ReadAllText(path));
                    if (loaded != null) state = loaded;
                    if (state.crew == null) state.crew = new List<CrewMember>();
                    if (state.recruitedOfferKeys == null) state.recruitedOfferKeys = new List<string>();
                    if (state.pendingLoot == null) state.pendingLoot = new List<LootCache>();
                    if (state.collectedLootIds == null) state.collectedLootIds = new List<string>();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Boatships port save could not be loaded: " + ex.Message);
                }
            }
            LastMessage = "Port account loaded for world seed " + seed + ".";
        }

        public bool Buy(int slot)
        {
            int cost;
            string purchased;
            switch (slot)
            {
                case 0: cost = 40; purchased = "5 wood"; break;
                case 1: cost = 65; purchased = "3 iron"; break;
                case 2: cost = 25; purchased = "5 rations"; break;
                case 3: cost = 140; purchased = "1 spare cannon"; break;
                default: return false;
            }
            if (state.gold < cost)
            {
                LastMessage = "Not enough gold. You need " + cost + " gold.";
                return false;
            }
            state.gold -= cost;
            switch (slot)
            {
                case 0: state.wood += 5; break;
                case 1: state.iron += 3; break;
                case 2: state.rations += 5; break;
                case 3: state.spareCannons++; break;
            }
            LastMessage = "Purchased " + purchased + " for " + cost + " gold.";
            Save();
            return true;
        }

        public CrewOffer GetOffer(ArchipelagoWorld.IslandSite port, int index)
        {
            index = Mathf.Clamp(index, 0, 2);
            int seed;
            unchecked
            {
                seed = activeSeed * 397 ^ port.Cell.x * 73856093 ^ port.Cell.y * 19349663 ^ index * 83492791;
            }
            System.Random rng = new System.Random(seed);
            int skill = rng.Next(20, 81);
            CrewOffer offer = new CrewOffer
            {
                Id = activeSeed + ":" + port.Cell.x + ":" + port.Cell.y + ":" + index,
                Name = FirstNames[rng.Next(FirstNames.Length)] + " " + LastNames[rng.Next(LastNames.Length)],
                Role = Roles[rng.Next(Roles.Length)],
                Skill = skill,
                Cost = 45 + skill * 2,
                Wage = 3 + skill / 15
            };
            return offer;
        }

        public bool IsRecruited(string offerId) => state.recruitedOfferKeys.Contains(offerId);

        public bool Recruit(ArchipelagoWorld.IslandSite port, int slot)
        {
            if (slot < 0 || slot > 2) return false;
            CrewOffer offer = GetOffer(port, slot);
            if (IsRecruited(offer.Id))
            {
                LastMessage = offer.Name + " is already in your crew.";
                return false;
            }
            if (state.crew.Count >= 24)
            {
                LastMessage = "Crew roster is full (24).";
                return false;
            }
            if (state.gold < offer.Cost)
            {
                LastMessage = "Not enough gold to hire " + offer.Name + ".";
                return false;
            }
            state.gold -= offer.Cost;
            state.crew.Add(new CrewMember
            {
                offerKey = offer.Id,
                displayName = offer.Name,
                role = offer.Role,
                skill = offer.Skill,
                wage = offer.Wage
            });
            state.recruitedOfferKeys.Add(offer.Id);
            LastMessage = offer.Name + " joined as " + offer.Role + ".";
            Save();
            return true;
        }

        public int RepairGoldCost(VoxelHull hull, ShipFlooding flooding)
        {
            if (hull == null) return 0;
            int missing = Mathf.Max(0, hull.TotalBlocks - hull.RemainingBlocks);
            bool flooded = flooding != null && (flooding.FloodFraction > 0.001f || flooding.IsSinking);
            return missing == 0 && !flooded ? 0 : 20 + missing * 2 + (flooded ? 25 : 0);
        }

        public int RepairWoodCost(VoxelHull hull)
        {
            if (hull == null) return 0;
            return Mathf.CeilToInt(Mathf.Max(0, hull.TotalBlocks - hull.RemainingBlocks) / 12f);
        }

        public bool RepairShip(VoxelHull hull, ShipFlooding flooding)
        {
            if (hull == null || flooding == null)
            {
                LastMessage = "Hull or flooding controller is missing.";
                return false;
            }
            int goldCost = RepairGoldCost(hull, flooding);
            int woodCost = RepairWoodCost(hull);
            if (goldCost == 0)
            {
                LastMessage = "The hull is already in good condition.";
                return false;
            }
            if (state.gold < goldCost || state.wood < woodCost)
            {
                LastMessage = "Repairs need " + goldCost + " gold and " + woodCost + " wood.";
                return false;
            }
            state.gold -= goldCost;
            state.wood -= woodCost;
            flooding.ResetShipForDemo(); // Existing M6 API restores hull and clears flooding.
            LastMessage = "Ship repaired for " + goldCost + " gold and " + woodCost + " wood.";
            Save();
            return true;
        }

        // Milestone 9: equipment is charged ONLY in the integrated shipyard scene.
        // Earlier prototype scenes retain free sandbox placement.
        public bool CanAffordEquipment(ShipEquipmentKind kind)
        {
            EquipmentCost(kind, out int wood, out int iron, out int cannons);
            return state.wood >= wood && state.iron >= iron && state.spareCannons >= cannons;
        }

        public string EquipmentCostText(ShipEquipmentKind kind)
        {
            EquipmentCost(kind, out int wood, out int iron, out int cannons);
            return kind + " costs " + wood + " wood, " + iron + " iron, " + cannons + " cannons";
        }

        public string StockSummary => "Stock: " + state.wood + " wood / " + state.iron +
            " iron / " + state.spareCannons + " cannons";

        private static void EquipmentCost(ShipEquipmentKind kind, out int wood, out int iron, out int cannons)
        {
            wood = 0; iron = 0; cannons = 0;
            switch (kind)
            {
                case ShipEquipmentKind.Cannon: cannons = 1; break;
                case ShipEquipmentKind.Storage: wood = 2; break;
                case ShipEquipmentKind.Armor: iron = 2; break;
                case ShipEquipmentKind.Cabin: wood = 4; iron = 1; break;
                case ShipEquipmentKind.Mast: wood = 6; iron = 2; break;
                case ShipEquipmentKind.Rudder: wood = 2; iron = 1; break;
            }
        }

        public bool TryConsumeEquipment(ShipEquipmentKind kind)
        {
            if (!CanAffordEquipment(kind))
            {
                LastMessage = "Insufficient supplies. " + EquipmentCostText(kind) + ".";
                return false;
            }
            EquipmentCost(kind, out int wood, out int iron, out int cannons);
            state.wood -= wood;
            state.iron -= iron;
            state.spareCannons -= cannons;
            LastMessage = kind + " installed. " + StockSummary;
            Save();
            return true;
        }

        // Only a consciously removed, inventory-paid component can be refunded.
        // Damage-detached equipment is lost, and M3 starter equipment was free.
        public void RefundEquipment(ShipEquipmentKind kind)
        {
            EquipmentCost(kind, out int wood, out int iron, out int cannons);
            state.wood += wood;
            state.iron += iron;
            state.spareCannons += cannons;
            LastMessage = kind + " returned to stock.";
            Save();
        }

        public bool HasLootEntry(string lootId)
        {
            if (state.collectedLootIds.Contains(lootId)) return true;
            for (int i = 0; i < state.pendingLoot.Count; ++i)
                if (state.pendingLoot[i].id == lootId) return true;
            return false;
        }

        public bool TryRegisterLoot(LootCache loot)
        {
            if (loot == null || string.IsNullOrEmpty(loot.id) || HasLootEntry(loot.id)) return false;
            state.pendingLoot.Add(loot);
            LastMessage = "Pirate defeated: salvage dropped at sea.";
            Save();
            return true;
        }

        public LootCache GetPendingLoot(string lootId)
        {
            for (int i = 0; i < state.pendingLoot.Count; ++i)
                if (state.pendingLoot[i].id == lootId) return state.pendingLoot[i];
            return null;
        }

        public bool TryCollectLoot(string lootId, out LootCache granted)
        {
            granted = null;
            if (state.collectedLootIds.Contains(lootId)) return false;
            for (int i = 0; i < state.pendingLoot.Count; ++i)
            {
                LootCache loot = state.pendingLoot[i];
                if (loot.id != lootId) continue;
                state.gold += Mathf.Max(0, loot.gold);
                state.wood += Mathf.Max(0, loot.wood);
                state.iron += Mathf.Max(0, loot.iron);
                state.rations += Mathf.Max(0, loot.rations);
                state.spareCannons += Mathf.Max(0, loot.spareCannons);
                state.collectedLootIds.Add(lootId);
                state.pendingLoot.RemoveAt(i);
                granted = loot;
                LastMessage = "Salvage recovered: " + loot.Describe() + ".";
                Save();
                return true;
            }
            return false;
        }

        public void ResetEconomyForTesting()
        {
            state = new SaveState();
            LastMessage = "Economy reset for this test world.";
            Save();
        }

        private void Save()
        {
            if (activeSeed == int.MinValue) return;
            try
            {
                File.WriteAllText(SavePath(activeSeed), JsonUtility.ToJson(state, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Boatships port save failed: " + ex.Message);
                LastMessage = "Action completed, but could not save: " + ex.Message;
            }
        }

        private static string SavePath(int seed)
        {
            return Path.Combine(Application.persistentDataPath, "boatships_port_seed_" + seed + ".json");
        }
    }
}
