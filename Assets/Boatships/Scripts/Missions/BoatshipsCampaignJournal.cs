using UnityEngine;

namespace Boatships
{
    public sealed class BoatshipsCampaignJournal : MonoBehaviour
    {
        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private PortDocking docking;
        [SerializeField] private ShipCombatTarget player;
        [SerializeField] private WeaponGroupCombat weapons;
        [SerializeField] private ShipArcadeController movement;
        private float nextTick;
        public string Status { get; private set; } = "Dock to accept the next campaign contract.";
        public CampaignObjective Objective => (CampaignObjective)(economy.Campaign.contractStep % 4);
        public string Faction
        {
            get
            {
                if (!docking.IsDocked) return "At sea";
                int faction = FactionIndex();
                return faction == 0 ? "Merchant League" : faction == 1 ? "Royal Navy" : "Free Ports";
            }
        }

        public void Configure(ArchipelagoWorld map, PortEconomy account, PortDocking port,
            ShipCombatTarget flagship, WeaponGroupCombat guns, ShipArcadeController controller)
        { world = map; economy = account; docking = port; player = flagship; weapons = guns; movement = controller; }

        private int FactionIndex() => BoatshipsFactions.OfPort(docking.DockedPort.Cell);

        private void Update()
        {
            if (world == null || economy.ActiveSeed != world.Seed || Time.time < nextTick) return;
            nextTick = Time.time + 0.5f;
            CampaignProgress progress = economy.Campaign;
            weapons.UpgradeDamageMultiplier = 1f + progress.cannonLevel * 0.2f + BoatshipsCharacter.GunneryBonus;
            movement.UpgradeSpeedMultiplier = 1f + progress.sailLevel * 0.1f + BoatshipsCharacter.SailingBonus;
            player.Hull.HullHealthMultiplier = 1f + progress.hullLevel * 0.25f;
            int factionReputation = FactionIndex() == 0 ? progress.merchantReputation :
                FactionIndex() == 1 ? progress.navyReputation : progress.freePortReputation;
            economy.TradingDiscount = docking.IsDocked ?
                Mathf.Min(0.2f, factionReputation * 0.002f + BoatshipsCharacter.TradeBonus) : 0f;
            if (!docking.IsDocked && world.TryNearestIsland(player.transform.position, out var island))
            {
                Vector2 delta = island.Center - new Vector2(player.transform.position.x, player.transform.position.z);
                if (delta.magnitude < island.Radius + 16f && !progress.discovered.Contains(island.Cell))
                {
                    if (economy.TryCampaignChange(data => data.discovered.Add(island.Cell)))
                    {
                        Status = "Discovered " + island.Kind + " in the " + island.Biome + " region.";
                        if (island.Kind == IslandKind.TreasureSite)
                        {
                            Vector3 shore = world.KeepOnWater(new Vector3(island.Center.x, 0f, island.Center.y), 5f);
                            economy.TryRegisterLoot(new PortEconomy.LootCache {
                                id = "campaign_treasure_" + island.Cell.x + "_" + island.Cell.y,
                                x = shore.x, z = shore.z, gold = 90, iron = 3, spareCannons = 1 });
                        }
                    }
                }
            }
        }

        public string ContractText
        {
            get
            {
                CampaignProgress p = economy.Campaign;
                string description;
                switch (Objective)
                {
                    case CampaignObjective.DefeatShips:
                        description = "Defeat or capture 2 campaign vessels. " +
                            Mathf.Clamp(p.victories - p.contractBaseline, 0, 2) + "/2";
                        break;
                    case CampaignObjective.DiscoverIslands:
                        description = "Discover another island. " + p.discovered.Count + " charted.";
                        break;
                    case CampaignObjective.DeliverSupplies:
                        description = "Deliver sealed supplies to port " + p.deliveryPort + ". Loading costs 2 wood and 1 iron.";
                        break;
                    default:
                        description = "Capture a campaign vessel using Fighters.";
                        break;
                }
                return "Contract " + (p.contractStep + 1) + (p.contractAccepted ? " — Active\n" : " — Available\n") +
                    description + "\nReward " + ContractReward().Describe();
            }
        }

        public bool ObjectiveComplete
        {
            get
            {
                CampaignProgress p = economy.Campaign;
                if (!p.contractAccepted) return false;
                switch (Objective)
                {
                    case CampaignObjective.DefeatShips: return p.victories >= p.contractBaseline + 2;
                    case CampaignObjective.DiscoverIslands:
                        return p.discovered.Count > p.contractBaseline || p.discovered.Count >= world.IslandCount;
                    case CampaignObjective.DeliverSupplies:
                        return p.cargoLoaded && docking.IsDocked && docking.DockedPort.Cell == p.deliveryPort;
                    default: return p.captures > p.contractBaseline;
                }
            }
        }

        private PortEconomy.LootCache ContractReward() => new PortEconomy.LootCache
        { gold = 160 + Mathf.Min(20, economy.Campaign.contractStep) * 20, wood = 5, iron = 3, rations = 3 };

        public void ContractAction()
        {
            if (!docking.IsDocked) { Status = "Dock at a port to use the campaign board."; return; }
            CampaignProgress p = economy.Campaign;
            if (!p.contractAccepted)
            {
                Vector2Int destination = docking.DockedPort.Cell;
                if (Objective == CampaignObjective.DeliverSupplies)
                {
                    if (!world.TryNearestOtherIsland(docking.DockedPort, true, out var port))
                    { Status = "No second delivery port in this world."; return; }
                    destination = port.Cell;
                }
                bool delivery = Objective == CampaignObjective.DeliverSupplies;
                int baseline = Objective == CampaignObjective.DefeatShips ? p.victories :
                    Objective == CampaignObjective.CaptureShip ? p.captures : p.discovered.Count;
                bool accepted = economy.TrySpend(0, delivery ? 2 : 0, delivery ? 1 : 0, () =>
                {
                    p.contractAccepted = true; p.contractBaseline = baseline;
                    p.deliveryPort = destination; p.cargoLoaded = delivery;
                });
                Status = accepted ? "Contract accepted." : economy.LastMessage;
            }
            else if (ObjectiveComplete)
            {
                int faction = FactionIndex();
                if (economy.ClaimCampaignContract(ContractReward()))
                {
                    economy.TryCampaignChange(progress =>
                    {
                        if (faction == 0) progress.merchantReputation += 10;
                        else if (faction == 1) progress.navyReputation += 10;
                        else progress.freePortReputation += 10;
                    });
                    Status = "Contract paid. Reputation increased with " + Faction + ".";
                }
            }
            else Status = "Complete the active objective first.";
        }

        public string UpgradeText(int category)
        {
            int level = UpgradeLevel(category);
            string name = category == 0 ? "Hull materials" : category == 1 ? "Cannon damage" : "Sails";
            int next = level + 1;
            return name + " Lv " + level + (level >= 3 ? " — Maximum" :
                " → " + next + " | " + (80 * next) + " gold, " + (4 * next) + " wood, " + (2 * next) +
                " iron | Reputation " + ((next - 1) * 20));
        }

        private int UpgradeLevel(int category) => category == 0 ? economy.Campaign.hullLevel :
            category == 1 ? economy.Campaign.cannonLevel : economy.Campaign.sailLevel;

        public void BuyUpgrade(int category)
        {
            if (category < 0 || category > 2 || !docking.IsDocked) { Status = "Dock to buy upgrades."; return; }
            int next = UpgradeLevel(category) + 1;
            if (next > 3 || economy.Campaign.reputation < (next - 1) * 20)
            { Status = "Maximum upgrade reached, or more reputation required."; return; }
            bool bought = economy.TrySpend(80 * next, 4 * next, 2 * next, () =>
            {
                if (category == 0) economy.Campaign.hullLevel = next;
                else if (category == 1) economy.Campaign.cannonLevel = next;
                else economy.Campaign.sailLevel = next;
            });
            Status = bought ? "Upgrade installed and saved." : economy.LastMessage;
            nextTick = 0f;
        }
    }
}
