using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// Small persistent single-player economy for the port prototype.
    /// Stores supplies, crew, salvage, and missions in a per-seed account.
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
            public bool shipwreck;

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
            public List<LootCache> wrecks = new List<LootCache>();
            public CampaignProgress campaign = new CampaignProgress();
            public List<MissionContract> missions = new List<MissionContract>();
            public List<Prisoner> prisoners = new List<Prisoner>();
            public int prisonerSerial;
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
        private bool hasActiveSeed;
        private bool saveLocked;

        public int Gold => state.gold;
        public int Wood => state.wood;
        public int Iron => state.iron;
        public int Rations => state.rations;
        public int SpareCannons => state.spareCannons;
        public IReadOnlyList<CrewMember> Crew => state.crew;
        public int ActiveSeed => activeSeed;
        public IReadOnlyList<LootCache> Wrecks => state.wrecks;
        public IReadOnlyList<LootCache> PendingLoot => state.pendingLoot;
        public event Action<LootCache> LootCollected;
        public CampaignProgress Campaign => state.campaign;
        public bool CanCheckpoint => hasActiveSeed && !saveLocked;
        public float TradingDiscount { get; set; }
        public int BuyPrice(int slot)
        {
            int[] prices = { 40, 65, 25, 140 };
            return slot >= 0 && slot < prices.Length ? Mathf.CeilToInt(prices[slot] *
                (1f - Mathf.Clamp(TradingDiscount, 0f, 0.2f))) : int.MaxValue;
        }
        public string LastMessage { get; private set; } = "Welcome to the harbor.";

        public void UseWorldSeed(int seed)
        {
            if (hasActiveSeed && activeSeed == seed) return;
            activeSeed = seed;
            hasActiveSeed = true;
            saveLocked = false;
            state = new SaveState();
            string path = SavePath(seed);
            if (BoatshipsSaveStore.Exists(path))
            {
                try
                {
                    SaveState loaded = JsonUtility.FromJson<SaveState>(BoatshipsSaveStore.Read(path));
                    if (loaded == null) throw new InvalidDataException("Invalid port account file.");
                    state = loaded;
                    if (state.crew == null) state.crew = new List<CrewMember>();
                    if (state.recruitedOfferKeys == null) state.recruitedOfferKeys = new List<string>();
                    if (state.pendingLoot == null) state.pendingLoot = new List<LootCache>();
                    if (state.collectedLootIds == null) state.collectedLootIds = new List<string>();
                    if (state.wrecks == null) state.wrecks = new List<LootCache>();
                    if (state.campaign == null) state.campaign = new CampaignProgress();
                    if (state.campaign.version != 1) throw new InvalidDataException("Unsupported campaign version.");
                    if (state.campaign.encounters == null) state.campaign.encounters = new List<EncounterRecord>();
                    // Sunk encounters carry nothing the game reads again (counters and loot live elsewhere).
                    state.campaign.encounters.RemoveAll(item => item == null || item.defeated);
                    if (state.campaign.discovered == null) state.campaign.discovered = new List<Vector2Int>();
                    if (state.missions == null) state.missions = new List<MissionContract>();
                    if (state.prisoners == null) state.prisoners = new List<Prisoner>();
                }
                catch (Exception ex)
                {
                    saveLocked = true;
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
            cost = BuyPrice(slot);
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
                Cost = Mathf.RoundToInt((45 + skill * 2) * (RecruitCostMultiplier != null ? RecruitCostMultiplier(port.Cell) : 1f)),
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
            if (!hasActiveSeed || saveLocked || loot == null || string.IsNullOrEmpty(loot.id) ||
                HasLootEntry(loot.id)) return false;
            state.pendingLoot.Add(loot);
            if (loot.shipwreck) state.wrecks.Add(loot);
            if (!Save())
            {
                state.pendingLoot.Remove(loot);
                if (loot.shipwreck) state.wrecks.Remove(loot);
                return false;
            }
            LastMessage = "Pirate defeated: salvage dropped at sea.";
            return true;
        }

        public LootCache GetPendingLoot(string lootId)
        {
            for (int i = 0; i < state.pendingLoot.Count; ++i)
                if (state.pendingLoot[i].id == lootId) return state.pendingLoot[i];
            return null;
        }

        public void PreservePendingWreck(string lootId)
        {
            LootCache pending = GetPendingLoot(lootId);
            if (pending == null || saveLocked) return;
            foreach (LootCache wreck in state.wrecks)
                if (wreck.id == lootId) return;
            bool previouslyWreck = pending.shipwreck;
            pending.shipwreck = true;
            state.wrecks.Add(pending);
            if (Save()) return;
            state.wrecks.Remove(pending);
            pending.shipwreck = previouslyWreck;
        }

        public bool TryCollectLoot(string lootId, out LootCache granted)
        {
            granted = null;
            if (!hasActiveSeed || saveLocked) return false;
            if (state.collectedLootIds.Contains(lootId)) return false;
            for (int i = 0; i < state.pendingLoot.Count; ++i)
            {
                LootCache loot = state.pendingLoot[i];
                if (loot.id != lootId) continue;
                ApplyReward(loot, 1);
                state.collectedLootIds.Add(lootId);
                state.pendingLoot.RemoveAt(i);
                if (!Save())
                {
                    ApplyReward(loot, -1);
                    state.collectedLootIds.Remove(lootId);
                    state.pendingLoot.Insert(i, loot);
                    return false;
                }
                granted = loot;
                LastMessage = "Salvage recovered: " + loot.Describe() + ".";
                LootCollected?.Invoke(loot);
                return true;
            }
            return false;
        }

        public bool TryNearestLoot(Vector3 shipPosition, out LootCache nearest, out float distance)
        {
            nearest = null;
            distance = float.PositiveInfinity;
            foreach (LootCache cache in state.pendingLoot)
            {
                float candidate = Vector2.Distance(new Vector2(shipPosition.x, shipPosition.z), new Vector2(cache.x, cache.z));
                if (candidate >= distance) continue;
                nearest = cache;
                distance = candidate;
            }
            return nearest != null;
        }

        /// <summary>Collect every kind of floating reward through the same saved, duplicate-safe path.</summary>
        /// <summary>Whether uncollected cargo floats within <paramref name="radius"/> of a point.</summary>
        /// <summary>Recruiting price multiplier per port (cheaper in the player's own cities).</summary>
        public static Func<Vector2Int, float> RecruitCostMultiplier;

        public bool HasLootNear(Vector3 position, float radius)
        {
            foreach (LootCache cache in state.pendingLoot)
            {
                Vector2 delta = new Vector2(position.x - cache.x, position.z - cache.z);
                if (delta.sqrMagnitude <= radius * radius) return true;
            }
            return false;
        }

        public int CollectNearbyLoot(Vector3 shipPosition, float radius, out string description)
        {
            int count = 0;
            var total = new LootCache();
            var pending = new List<LootCache>(state.pendingLoot);
            foreach (LootCache cache in pending)
            {
                Vector2 delta = new Vector2(shipPosition.x - cache.x, shipPosition.z - cache.z);
                if (delta.sqrMagnitude > radius * radius || !TryCollectLoot(cache.id, out LootCache granted)) continue;
                count++;
                total.gold += granted.gold;
                total.wood += granted.wood;
                total.iron += granted.iron;
                total.rations += granted.rations;
                total.spareCannons += granted.spareCannons;
            }
            description = count > 0 ? "Collected " + count + (count == 1 ? " loot crate: " : " loot crates: ") + total.Describe() :
                "No loot collected. Sail within " + radius.ToString("0") + " m of the floating loot and try again.";
            return count;
        }

        public void ResetEconomyForTesting()
        {
            state = new SaveState();
            LastMessage = "Economy reset for this test world.";
            Save();
        }

        internal IReadOnlyList<MissionContract> Missions => state.missions;

        internal bool AcceptMission(MissionContract mission)
        {
            state.missions.Add(mission);
            if (Save()) return true;
            state.missions.Remove(mission);
            return false;
        }

        internal bool MarkMissionReady(MissionContract mission)
        {
            mission.stage = MissionStage.Ready;
            if (Save()) return true;
            mission.stage = MissionStage.Active;
            return false;
        }

        internal bool ClaimMission(MissionContract mission, LootCache reward)
        {
            if (mission.stage != MissionStage.Ready) return false;
            mission.stage = MissionStage.Claimed;
            ApplyReward(reward, 1);
            if (Save())
            {
                LastMessage = "Contract paid: " + reward.Describe() + ".";
                return true;
            }
            ApplyReward(reward, -1);
            mission.stage = MissionStage.Ready;
            return false;
        }

        private void ApplyReward(LootCache reward, int sign)
        {
            state.gold += sign * Mathf.Max(0, reward.gold);
            state.wood += sign * Mathf.Max(0, reward.wood);
            state.iron += sign * Mathf.Max(0, reward.iron);
            state.rations += sign * Mathf.Max(0, reward.rations);
            state.spareCannons += sign * Mathf.Max(0, reward.spareCannons);
        }

        public bool SaveNow() => Save();

        /// <summary>Character creation: replaces the fresh world's default supplies with the background kit.</summary>
        public bool ApplyStartingKit(int gold, int wood, int iron, int rations, int cannons, int crewCount, System.Random random)
        {
            if (!CanCheckpoint) return false;
            string before = JsonUtility.ToJson(state);
            state.gold = Mathf.Max(0, gold); state.wood = Mathf.Max(0, wood); state.iron = Mathf.Max(0, iron);
            state.rations = Mathf.Max(0, rations); state.spareCannons = Mathf.Max(0, cannons);
            for (int i = 0; i < crewCount && state.crew.Count < 24; i++)
            {
                int skill = random.Next(30, 56);
                string key = activeSeed + ":start:" + i;
                state.crew.Add(new CrewMember
                {
                    offerKey = key,
                    displayName = FirstNames[random.Next(FirstNames.Length)] + " " + LastNames[random.Next(LastNames.Length)],
                    role = Roles[random.Next(Roles.Length)],
                    skill = skill,
                    wage = 3 + skill / 15
                });
                state.recruitedOfferKeys.Add(key);
            }
            LastMessage = "Your story begins.";
            if (Save()) return true;
            state = JsonUtility.FromJson<SaveState>(before);
            return false;
        }

        /// <summary>Adds (positive) or removes (negative) gold. Fails rather than going below zero.</summary>
        public bool TryAdjustGold(int delta)
        {
            if (!CanCheckpoint || state.gold + delta < 0) return false;
            state.gold += delta;
            if (Save()) return true;
            state.gold -= delta;
            return false;
        }

        /// <summary>Daily ship upkeep. Returns false (and empties the stores) when there is not enough food.</summary>
        public bool ConsumeRations(int amount)
        {
            if (!CanCheckpoint || amount <= 0) return true;
            bool enough = state.rations >= amount;
            state.rations = Mathf.Max(0, state.rations - amount);
            Save();
            return enough;
        }

        /// <summary>Drops unresolved encounters (enemies the player escaped from on the world map).</summary>
        public bool RemoveUnresolvedEncounters(ICollection<string> ids)
        {
            if (saveLocked || !hasActiveSeed || ids == null || ids.Count == 0) return false;
            string before = JsonUtility.ToJson(state);
            state.campaign.encounters.RemoveAll(item => ids.Contains(item.id) && !item.defeated && !item.captured);
            if (Save()) return true;
            state = JsonUtility.FromJson<SaveState>(before);
            return false;
        }

        public bool TryCampaignChange(Action<CampaignProgress> change)
        {
            if (!hasActiveSeed || saveLocked) return false;
            string before = JsonUtility.ToJson(state);
            change(state.campaign);
            if (Save()) return true;
            state = JsonUtility.FromJson<SaveState>(before);
            return false;
        }

        public bool TrySpend(int gold, int wood, int iron, Action action)
        {
            if (saveLocked || !hasActiveSeed || gold < 0 || wood < 0 || iron < 0 ||
                state.gold < gold || state.wood < wood || state.iron < iron)
            {
                LastMessage = "Insufficient supplies, or account unavailable.";
                return false;
            }
            string before = JsonUtility.ToJson(state);
            state.gold -= gold; state.wood -= wood; state.iron -= iron;
            try { action(); if (Save()) return true; }
            catch (Exception ex) { Debug.LogWarning("Purchase failed: " + ex.Message); }
            state = JsonUtility.FromJson<SaveState>(before);
            return false;
        }

        public bool ClaimCampaignContract(LootCache reward)
        {
            if (!CanCheckpoint || !state.campaign.contractAccepted) return false;
            string before = JsonUtility.ToJson(state);
            state.campaign.contractAccepted = false;
            state.campaign.cargoLoaded = false;
            state.campaign.contractStep++;
            state.campaign.reputation += 10;
            ApplyReward(reward, 1);
            if (Save()) return true;
            state = JsonUtility.FromJson<SaveState>(before);
            return false;
        }

        public bool ResolveEncounter(string encounterId, bool captured, Vector3 position)
        {
            EncounterRecord encounter = state.campaign.encounters.Find(item => item.id == encounterId);
            if (saveLocked || encounter == null || encounter.defeated || encounter.captured) return false;
            string before = JsonUtility.ToJson(state);
            encounter.defeated = !captured;
            encounter.captured = captured;
            if (!captured) { encounter.damage.Clear(); encounter.flooding = null; }
            encounter.position = position;
            state.campaign.victories++;
            state.campaign.reputation += captured ? 5 : 2;
            if (captured) state.campaign.captures++;
            LootCache reward = new LootCache { id = encounterId, x = position.x, z = position.z,
                gold = 45 + encounter.tier * 30, wood = 3 + encounter.tier,
                iron = encounter.tier, rations = 2, spareCannons = encounter.tier >= 3 ? 1 : 0,
                shipwreck = !captured };
            state.pendingLoot.Add(reward);
            if (!captured) state.wrecks.Add(reward);
            if (Save()) return true;
            state = JsonUtility.FromJson<SaveState>(before);
            return false;
        }

        /// <summary>Drops records of sunk encounters so the campaign list (iterated several times a second and
        /// written in every checkpoint) does not grow for the whole campaign. Call outside any loop over
        /// <see cref="Campaign"/>.encounters.</summary>
        /// <summary>Uses wood from the hold (e.g. a plank to patch a hole in battle).</summary>
        public bool TrySpendWood(int amount)
        {
            if (saveLocked || amount <= 0 || state.wood < amount) return false;
            state.wood -= amount;
            if (Save()) return true;
            state.wood += amount;
            return false;
        }

        // Sunk ships stay listed until their survivors are taken prisoner or left behind.
        private static bool Prunable(EncounterRecord item) => item.defeated && (item.prisonersResolved || item.Survivors == 0);

        public bool PruneDefeatedEncounters()
        {
            if (saveLocked || state == null || state.campaign == null || state.campaign.encounters == null) return false;
            if (!state.campaign.encounters.Exists(Prunable)) return false;
            string before = JsonUtility.ToJson(state);
            state.campaign.encounters.RemoveAll(Prunable);
            if (Save()) return true;
            state = JsonUtility.FromJson<SaveState>(before);
            return false;
        }

        // ───────────────────────────── Prisoners ─────────────────────────────

        public IReadOnlyList<Prisoner> Prisoners => state.prisoners;

        /// <summary>Room for prisoners across the flagship and company, as last computed by the voyage layer (for display).</summary>
        public int PrisonerCapacity { get; set; }

        /// <summary>Takes up to count survivors of an encounter aboard as prisoners, the fittest first.
        /// Whatever is not taken is left behind for good. Returns how many were taken.</summary>
        public int TakePrisoners(string encounterId, int count)
        {
            EncounterRecord record = state.campaign.encounters.Find(item => item.id == encounterId);
            if (saveLocked || !hasActiveSeed || record == null || record.prisonersResolved) return 0;
            string before = JsonUtility.ToJson(state);
            var random = new System.Random(unchecked(activeSeed * 31 + state.prisonerSerial * 7919 + encounterId.GetHashCode()));
            int taken = 0;
            while (taken < count && record.TakeSurvivor(out Incapacitation wound))
            {
                state.prisoners.Add(new Prisoner
                {
                    id = activeSeed + ":prisoner:" + state.prisonerSerial++,
                    name = FirstNames[random.Next(FirstNames.Length)] + " " + LastNames[random.Next(LastNames.Length)],
                    tier = record.tier,
                    ship = record.shipName,
                    wound = wound,
                    skill = random.Next(15, 51),
                    // Pressed men from a merchant brig sign on more readily than navy hands.
                    willing = random.NextDouble() < (record.kind == EncounterKind.Patrol ? 0.25 : 0.45)
                });
                taken++;
            }
            LeaveSurvivors(record);
            if (Save())
            {
                LastMessage = taken > 0 ? taken + " prisoner" + (taken == 1 ? "" : "s") + " taken from " + record.DisplayName + "." :
                    "No prisoners taken from " + record.DisplayName + ".";
                return taken;
            }
            state = JsonUtility.FromJson<SaveState>(before);
            return 0;
        }

        private static void LeaveSurvivors(EncounterRecord record)
        {
            record.prisonersResolved = true;
            record.survivedBlade = record.survivedCannon = record.survivedFirearm = record.survivedSinking = 0;
        }

        /// <summary>Leaves an encounter's survivors behind (adrift, or to the victors' mercy elsewhere).</summary>
        public void LeaveSurvivors(string encounterId)
        {
            EncounterRecord record = state.campaign.encounters.Find(item => item.id == encounterId);
            if (record == null || record.prisonersResolved) return;
            LeaveSurvivors(record);
            Save();
        }

        /// <summary>Hands every prisoner to the harbour authorities for the bounty on them.</summary>
        public bool HandOverPrisoners(out int count, out int bounty)
        {
            count = state.prisoners.Count; bounty = 0;
            if (saveLocked || !hasActiveSeed || count == 0) return false;
            string before = JsonUtility.ToJson(state);
            foreach (Prisoner prisoner in state.prisoners) bounty += prisoner.Bounty;
            state.prisoners.Clear();
            state.gold += bounty;
            state.campaign.reputation += Mathf.Max(1, count / 3);
            if (Save()) { LastMessage = "The harbour guard took " + count + " prisoners and paid a bounty of " + bounty + " gold."; return true; }
            state = JsonUtility.FromJson<SaveState>(before);
            count = 0; bounty = 0;
            return false;
        }

        /// <summary>A willing prisoner signs on as a member of the crew.</summary>
        public bool RecruitPrisoner(string prisonerId)
        {
            Prisoner prisoner = state.prisoners.Find(item => item.id == prisonerId);
            if (saveLocked || !hasActiveSeed || prisoner == null) return false;
            if (!prisoner.willing) { LastMessage = prisoner.name + " spits on the deck and refuses to serve."; return false; }
            if (state.crew.Count >= 24) { LastMessage = "Crew roster is full (24)."; return false; }
            string before = JsonUtility.ToJson(state);
            state.prisoners.Remove(prisoner);
            string key = "prisoner:" + prisoner.id;
            var random = new System.Random(prisoner.id.GetHashCode());
            state.crew.Add(new CrewMember
            {
                offerKey = key,
                displayName = prisoner.name,
                role = Roles[random.Next(Roles.Length)],
                skill = prisoner.skill,
                wage = 2 + prisoner.skill / 20
            });
            state.recruitedOfferKeys.Add(key);
            if (Save()) { LastMessage = prisoner.name + " swore to serve and joined your crew."; return true; }
            state = JsonUtility.FromJson<SaveState>(before);
            return false;
        }

        /// <summary>Releases prisoners: all of them, or only enough to bring the number down to keep.</summary>
        public int ReleasePrisoners(int keep = 0)
        {
            int released = Mathf.Max(0, state.prisoners.Count - Mathf.Max(0, keep));
            if (released == 0 || saveLocked || !hasActiveSeed) return 0;
            // The most recently taken go first.
            state.prisoners.RemoveRange(state.prisoners.Count - released, released);
            Save();
            return released;
        }

        /// <summary>Ships taken in battle: the company (with a captain) and prizes still awaiting orders.</summary>
        public List<EncounterRecord> CapturedShips()
        {
            var ships = new List<EncounterRecord>();
            foreach (EncounterRecord record in state.campaign.encounters)
                if (record.captured && !record.sold) ships.Add(record);
            return ships;
        }

        public int CompanySize
        {
            get
            {
                int count = 0;
                foreach (EncounterRecord record in state.campaign.encounters) if (record.InCompany) count++;
                return count;
            }
        }

        /// <summary>Crew members who can be given command of a ship (healthy enough to be on the roster with a stable key).</summary>
        public bool CanCaptain(CrewMember member) => member != null && !string.IsNullOrEmpty(member.offerKey);

        /// <summary>Promotes a sailor from the flagship's crew to captain of a captured ship.
        /// A captain being replaced returns to the flagship's crew if there is room.</summary>
        public bool AppointCaptain(string encounterId, string crewKey)
        {
            EncounterRecord record = state.campaign.encounters.Find(item => item.id == encounterId);
            CrewMember member = state.crew.Find(item => item.offerKey == crewKey);
            if (saveLocked || !hasActiveSeed || record == null || !record.captured || record.sold || !CanCaptain(member))
            {
                LastMessage = "That sailor cannot take command of this ship.";
                return false;
            }
            string before = JsonUtility.ToJson(state);
            string replaced = ReturnCaptainToCrew(record);
            state.crew.Remove(member);
            record.captainKey = member.offerKey;
            record.captainName = member.displayName;
            record.captainRole = member.role;
            record.captainSkill = member.skill;
            if (Save())
            {
                LastMessage = "Captain " + member.displayName + " takes command of " + record.DisplayName + "." +
                    (replaced != null ? " " + replaced : "");
                return true;
            }
            state = JsonUtility.FromJson<SaveState>(before);
            LastMessage = "Could not save the appointment.";
            return false;
        }

        /// <summary>Sells a captured ship (at sea to a prize agent, or to a shipwright). Her captain rejoins the crew.</summary>
        public bool SellCapturedShip(string encounterId, out int price)
        {
            price = 0;
            EncounterRecord record = state.campaign.encounters.Find(item => item.id == encounterId);
            if (saveLocked || !hasActiveSeed || record == null || !record.captured || record.sold) return false;
            string before = JsonUtility.ToJson(state);
            price = record.PrizeValue;
            string returned = ReturnCaptainToCrew(record);
            record.captured = false;
            record.defeated = true;
            record.sold = true;
            record.damage.Clear();
            record.flooding = null;
            state.gold += price;
            if (Save())
            {
                LastMessage = "Sold " + record.DisplayName + " for " + price + " gold." + (returned != null ? " " + returned : "");
                return true;
            }
            state = JsonUtility.FromJson<SaveState>(before);
            price = 0;
            LastMessage = "Could not save the sale.";
            return false;
        }

        /// <summary>Puts a ship's captain back on the flagship's roster. Returns a message, or null if she had no captain.</summary>
        private string ReturnCaptainToCrew(EncounterRecord record)
        {
            if (string.IsNullOrEmpty(record.captainKey)) return null;
            string name = record.captainName;
            string message;
            if (state.crew.Count < 24 && !state.crew.Exists(item => item.offerKey == record.captainKey))
            {
                state.crew.Add(new CrewMember
                {
                    offerKey = record.captainKey,
                    displayName = record.captainName,
                    role = string.IsNullOrEmpty(record.captainRole) ? "Sailor" : record.captainRole,
                    skill = record.captainSkill,
                    wage = 3 + record.captainSkill / 15
                });
                message = name + " rejoins your crew.";
            }
            else message = name + " takes their share and goes ashore (your crew is full).";
            record.captainKey = null; record.captainName = null; record.captainRole = null; record.captainSkill = 0;
            return message;
        }

        public bool LoseCapturedVessel(string encounterId, Vector3 position)
        {
            EncounterRecord record = state.campaign.encounters.Find(item => item.id == encounterId);
            if (record == null || !record.captured || saveLocked) return false;
            string before = JsonUtility.ToJson(state);
            if (!string.IsNullOrEmpty(record.captainName))
                LastMessage = "Captain " + record.captainName + " went down with " + record.DisplayName + ".";
            record.captainKey = null; record.captainName = null;
            record.captured = false; record.defeated = true;
            record.damage.Clear();
            record.flooding = null;
            var salvage = new LootCache { id = encounterId + "_fleetloss", x = position.x, z = position.z,
                shipwreck = true, wood = 2, iron = 1 };
            state.pendingLoot.Add(salvage); state.wrecks.Add(salvage);
            if (Save()) return true;
            state = JsonUtility.FromJson<SaveState>(before);
            return false;
        }

        private bool Save()
        {
            if (!hasActiveSeed || saveLocked) return false;
            string path = SavePath(activeSeed);
            try
            {
                BoatshipsSaveStore.Write(path, JsonUtility.ToJson(state));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Boatships port save failed: " + ex.Message);
                LastMessage = "Could not save the port account: " + ex.Message;
                return false;
            }
        }

        private static string SavePath(int seed)
        {
            return Path.Combine(Application.persistentDataPath, "boatships_port_seed_" + seed + ".json");
        }
    }
}
