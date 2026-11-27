using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boatships
{
    [Serializable]
    public sealed class InventoryState
    {
        public int version = 1;
        public string weapon = BoatshipsItems.StarterWeapon;
        public string armor = "linen_shirt";
        public string helmet = "bandana";
        /// <summary>The player's own bag: spare equipment, consumables, treasure.</summary>
        public List<ItemStack> bag = new List<ItemStack>();
        /// <summary>Trade goods in the fleet's hold (supplies such as wood live in the port account).</summary>
        public List<ItemStack> cargo = new List<ItemStack>();
    }

    /// <summary>The player's equipment and bag, and the ship's cargo hold. Equipment sets the first-person
    /// combat stats; the hold has a capacity from the hull, storage crates and captured ships, and an
    /// overloaded fleet sails slower. Trade goods are bought and sold at port markets.</summary>
    public sealed class BoatshipsInventory : MonoBehaviour
    {
        private const int SuppliesPerCannon = 4;

        private ArchipelagoWorld world;
        private PortEconomy economy;
        private BoatshipsCharacter character;
        private BoatshipsTravelController travel;
        private BoatshipsWorldMapView view;
        private BoatshipsFirstPerson firstPerson;
        private ShipCombatTarget flagship;
        private ShipCustomization shipyard;
        private ShipArcadeController movement;

        private InventoryState state;
        private int loadedSeed = int.MinValue;
        private bool loadFailed;
        private readonly System.Random random = new System.Random(Environment.TickCount);

        public bool Ready => state != null && world != null && loadedSeed == world.Seed;
        public InventoryState State => state;

        public void Configure(ArchipelagoWorld map, PortEconomy account, BoatshipsCharacter captain, BoatshipsTravelController controller,
            BoatshipsWorldMapView mapView, BoatshipsFirstPerson fps, Transform ship)
        {
            world = map; economy = account; character = captain; travel = controller; view = mapView; firstPerson = fps;
            flagship = ship != null ? ship.GetComponent<ShipCombatTarget>() : null;
            shipyard = ship != null ? ship.GetComponent<ShipCustomization>() : null;
            movement = ship != null ? ship.GetComponent<ShipArcadeController>() : null;
            economy.LootCollected += OnLootCollected;
            fps.HealRequested = UseHealingItem;
        }

        private void OnEnable() { BoatshipsSaveCoordinator.CheckpointCapturing += Save; }
        private void OnDisable() { BoatshipsSaveCoordinator.CheckpointCapturing -= Save; }
        private void OnDestroy() { if (economy != null) economy.LootCollected -= OnLootCollected; }

        // ───────────────────────────── Persistence ─────────────────────────────

        private void Update()
        {
            if (world == null || !BoatshipsSaveStore.Enabled) return;
            if (loadedSeed != world.Seed) Load();
            if (state == null && !loadFailed && character != null && character.HasCharacter) CreateStarterKit();
            if (!Ready) return;
            ApplyLoadout();
            if (movement != null) movement.CargoSpeedMultiplier = SpeedFactor;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.iKey.wasPressedThisFrame && travel.Live && !view.MenuOpen && !BoatshipsUI.Typing &&
                !BoatshipsDebugMenu.IsOpen && !travel.FirstPersonActive)
                InventoryMenu(null);
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
                InventoryState loaded = JsonUtility.FromJson<InventoryState>(BoatshipsSaveStore.Read(path));
                if (loaded == null || loaded.version != 1) throw new InvalidDataException("Unsupported inventory version.");
                if (loaded.bag == null) loaded.bag = new List<ItemStack>();
                if (loaded.cargo == null) loaded.cargo = new List<ItemStack>();
                state = loaded;
            }
            catch (Exception ex)
            {
                loadFailed = true;
                Debug.LogWarning("Boatships inventory could not be loaded: " + ex.Message, this);
            }
        }

        private void CreateStarterKit()
        {
            state = new InventoryState();
            Add(state.bag, "bandage", 2);
            Add(state.bag, "grog", 2);
            Save();
        }

        public void Save()
        {
            if (state == null || loadFailed || world == null || loadedSeed != world.Seed) return;
            try { BoatshipsSaveStore.Write(SavePath(loadedSeed), JsonUtility.ToJson(state)); }
            catch (Exception ex) { Debug.LogWarning("Boatships inventory save failed: " + ex.Message, this); }
        }

        private static string SavePath(int seed) =>
            Path.Combine(Application.persistentDataPath, "boatships_inventory_v1_" + seed + ".json");

        // ───────────────────────────── Stacks ─────────────────────────────

        public static int Count(List<ItemStack> stacks, string id)
        {
            foreach (ItemStack stack in stacks) if (stack.id == id) return stack.count;
            return 0;
        }

        public static void Add(List<ItemStack> stacks, string id, int count)
        {
            if (count <= 0 || BoatshipsItems.Get(id) == null) return;
            foreach (ItemStack stack in stacks) if (stack.id == id) { stack.count += count; return; }
            stacks.Add(new ItemStack { id = id, count = count });
        }

        public static bool Remove(List<ItemStack> stacks, string id, int count)
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].id != id) continue;
                if (stacks[i].count < count) return false;
                stacks[i].count -= count;
                if (stacks[i].count <= 0) stacks.RemoveAt(i);
                return true;
            }
            return false;
        }

        public void AddToBag(string id, int count) { if (Ready) { Add(state.bag, id, count); Save(); } }
        public void AddCargo(string id, int count) { if (Ready) { Add(state.cargo, id, count); Save(); } }
        public int CargoCount(string id) => Ready ? Count(state.cargo, id) : 0;

        // ───────────────────────────── Equipment ─────────────────────────────

        public ItemDef Weapon => BoatshipsItems.Get(state != null ? state.weapon : null) ?? BoatshipsItems.Get(BoatshipsItems.StarterWeapon);
        public ItemDef Armor => BoatshipsItems.Get(state != null ? state.armor : null);
        public ItemDef Helmet => BoatshipsItems.Get(state != null ? state.helmet : null);
        public int MaxHealth => 100 + (Armor != null ? Armor.Health : 0) + (Helmet != null ? Helmet.Health : 0);

        private string appliedLoadout;

        private void ApplyLoadout()
        {
            if (firstPerson == null) return;
            string key = state.weapon + "|" + state.armor + "|" + state.helmet;
            if (key == appliedLoadout) return;
            appliedLoadout = key;
            ItemDef weapon = Weapon;
            firstPerson.MaxHealth = MaxHealth;
            firstPerson.DamageMultiplier = weapon.Damage;
            firstPerson.ReachMultiplier = weapon.Reach;
            firstPerson.SwingSpeedMultiplier = weapon.Speed;
            firstPerson.BlockBonus = (Armor != null ? Armor.Block : 0f) + (Helmet != null ? Helmet.Block : 0f);
            Color blade = weapon.Damage > 1.3f ? new Color(0.62f, 0.64f, 0.68f) : weapon.Reach > 1.15f ? new Color(0.88f, 0.9f, 0.95f) : new Color(0.78f, 0.8f, 0.84f);
            firstPerson.SetWeaponLook(weapon.Reach, blade);
        }

        public bool Equip(string id)
        {
            ItemDef item = BoatshipsItems.Get(id);
            if (!Ready || item == null || !item.Equipment || !Remove(state.bag, id, 1)) return false;
            string old = item.Kind == ItemKind.Weapon ? state.weapon : item.Kind == ItemKind.Armor ? state.armor : state.helmet;
            if (item.Kind == ItemKind.Weapon) state.weapon = id;
            else if (item.Kind == ItemKind.Armor) state.armor = id;
            else state.helmet = id;
            if (!string.IsNullOrEmpty(old)) Add(state.bag, old, 1);
            Save();
            return true;
        }

        private float UseHealingItem()
        {
            if (!Ready) return 0f;
            foreach (string id in new[] { "bandage", "grog" })
                if (Remove(state.bag, id, 1)) { Save(); return BoatshipsItems.Get(id).Heal; }
            return 0f;
        }

        // ───────────────────────────── Hold ─────────────────────────────

        /// <summary>Hold space of the whole fleet: the flagship's hull and storage crates, plus every captured ship.</summary>
        public int Capacity
        {
            get
            {
                int hull = flagship != null && flagship.Hull != null ? flagship.Hull.TotalBlocks / 12 : 30;
                int crates = shipyard != null ? shipyard.CountInstalled(ShipEquipmentKind.Storage) : 0;
                int prizes = 0;
                if (economy != null) foreach (EncounterRecord record in economy.Campaign.encounters) if (record.captured) prizes++;
                return 30 + hull + crates * 20 + prizes * 25;
            }
        }

        public int SuppliesLoad => economy == null ? 0 : economy.Wood + economy.Iron + economy.Rations + economy.SpareCannons * SuppliesPerCannon;

        public int CargoLoad
        {
            get
            {
                int load = 0;
                if (state != null) foreach (ItemStack stack in state.cargo) load += stack.count * BoatshipsItems.Get(stack.id).Weight;
                return load;
            }
        }

        public int Used => SuppliesLoad + CargoLoad;
        public int Free => Mathf.Max(0, Capacity - Used);

        /// <summary>1 when within capacity; an overloaded fleet loses up to 60% of its speed.</summary>
        public float SpeedFactor
        {
            get
            {
                int capacity = Mathf.Max(1, Capacity);
                int over = Used - capacity;
                return over <= 0 ? 1f : Mathf.Max(0.4f, 1f - over / (float)capacity * 1.5f);
            }
        }

        // ───────────────────────────── Loot ─────────────────────────────

        private void OnLootCollected(PortEconomy.LootCache loot)
        {
            if (!Ready || loot == null) return;
            // Prize cargo: a few kinds of trade goods, and now and then something for the captain's bag.
            int richness = Mathf.Clamp(loot.gold / 40, 1, 6);
            var goods = new List<ItemDef>(BoatshipsItems.OfKind(ItemKind.TradeGood));
            var found = new List<string>();
            int kinds = 1 + random.Next(Mathf.Min(3, richness));
            for (int i = 0; i < kinds && goods.Count > 0; i++)
            {
                ItemDef good = goods[random.Next(goods.Count)];
                goods.Remove(good);
                int count = Mathf.Max(1, (2 + random.Next(4)) * richness * 12 / Mathf.Max(8, good.Price));
                Add(state.cargo, good.Id, count);
                found.Add(count + " " + good.Name);
            }
            if (random.NextDouble() < 0.12 + richness * 0.04)
            {
                var valuables = new List<ItemDef>(BoatshipsItems.OfKind(ItemKind.Treasure));
                foreach (ItemDef item in BoatshipsItems.All) if (item.Equipment && item.Price >= 90) valuables.Add(item);
                ItemDef prize = valuables[random.Next(valuables.Count)];
                Add(state.bag, prize.Id, 1);
                found.Add(prize.Name + " (to your bag)");
            }
            if (random.NextDouble() < 0.5) { Add(state.bag, "bandage", 1); found.Add("Bandages"); }
            Save();
            if (found.Count > 0) travel.Notify("Salvaged: " + string.Join(", ", found) + "." + (Used > Capacity ? " The hold is overloaded!" : ""));
        }

        // ───────────────────────────── Menus ─────────────────────────────

        public void InventoryMenu(Action back)
        {
            if (!Ready) return;
            ItemDef weapon = Weapon;
            string body =
                "<color=#F2D99E>Equipped</color>\n" +
                "Weapon: " + weapon.Name + "  (damage ×" + weapon.Damage.ToString("0.00") + ", reach ×" + weapon.Reach.ToString("0.00") + ", speed ×" + weapon.Speed.ToString("0.00") + ")\n" +
                "Armour: " + (Armor != null ? Armor.Name : "none") + "   Helmet: " + (Helmet != null ? Helmet.Name : "none") +
                "   Health " + MaxHealth + "\n\n" +
                "<color=#F2D99E>Bag</color>\n" + Describe(state.bag, "Empty.") + "\n\n" +
                "<color=#F2D99E>Ship's hold</color>  " + Used + " / " + Capacity + (Used > Capacity ? "  <color=#FF7A6A>OVERLOADED — the fleet sails slower</color>" : "") + "\n" +
                "Supplies: wood " + economy.Wood + ", iron " + economy.Iron + ", rations " + economy.Rations + ", spare cannons " + economy.SpareCannons + "\n" +
                "Cargo: " + Describe(state.cargo, "none");
            var options = new List<MapMenuOption>();
            foreach (ItemKind kind in new[] { ItemKind.Weapon, ItemKind.Armor, ItemKind.Helmet })
            {
                ItemKind k = kind;
                bool spare = false;
                foreach (ItemStack stack in state.bag) if (BoatshipsItems.Get(stack.id).Kind == kind) spare = true;
                options.Add(new MapMenuOption("Change " + (kind == ItemKind.Weapon ? "weapon" : kind == ItemKind.Armor ? "armour" : "helmet"),
                    () => EquipMenu(k, back), spare));
            }
            options.Add(new MapMenuOption("Throw cargo overboard…", () => JettisonMenu(back), state.cargo.Count > 0));
            options.Add(new MapMenuOption(back != null ? "Back" : "Close  [I]", back));
            view.ShowMenu("Inventory — " + character.Name, body, options);
        }

        private void EquipMenu(ItemKind kind, Action back)
        {
            var options = new List<MapMenuOption>();
            foreach (ItemStack stack in state.bag)
            {
                ItemDef item = BoatshipsItems.Get(stack.id);
                if (item.Kind != kind) continue;
                string id = item.Id;
                options.Add(new MapMenuOption("Equip " + item.Name + " — " + Stats(item), () => { Equip(id); InventoryMenu(back); }));
            }
            options.Add(new MapMenuOption("Back", () => InventoryMenu(back)));
            view.ShowMenu("Change equipment", "Your current item goes into your bag.", options);
        }

        private void JettisonMenu(Action back)
        {
            var options = new List<MapMenuOption>();
            foreach (ItemStack stack in state.cargo)
            {
                ItemDef item = BoatshipsItems.Get(stack.id);
                string id = item.Id;
                int count = stack.count;
                options.Add(new MapMenuOption("Throw all " + count + " " + item.Name + " overboard",
                    () => { Remove(state.cargo, id, count); Save(); JettisonMenu(back); }));
            }
            options.Add(new MapMenuOption("Back", () => InventoryMenu(back)));
            view.ShowMenu("Lighten the hold", "Hold " + Used + " / " + Capacity + ".", options);
        }

        private float Discount => character != null ? character.Skill(CharacterSkill.Trade) * 0.015f : 0f;
        private float SellBonus => character != null ? character.Skill(CharacterSkill.Trade) * 0.01f : 0f;

        /// <summary>Trade goods market of a port: buy low here, sell high elsewhere.</summary>
        public void MarketMenu(ArchipelagoWorld.IslandSite port, Action back, string message = null)
        {
            if (!Ready) return;
            int day = travel.Day;
            var options = new List<MapMenuOption>();
            foreach (ItemDef good in BoatshipsItems.OfKind(ItemKind.TradeGood))
            {
                ItemDef item = good;
                int buy = BoatshipsItems.BuyPrice(item, port.Cell, day, Discount);
                int sell = BoatshipsItems.SellPrice(item, port.Cell, day, SellBonus);
                float factor = BoatshipsItems.PortFactor(port.Cell, item.Id, day);
                string tag = factor < 0.85f ? "  <color=#9EE29E>cheap here</color>" : factor > 1.3f ? "  <color=#FFB36A>in demand</color>" : "";
                options.Add(new MapMenuOption(item.Name + " — buy " + buy + " / sell " + sell + "  (hold " + Count(state.cargo, item.Id) + ")" + tag,
                    () => TradeMenu(port, item, back)));
            }
            options.Add(new MapMenuOption("Back", back));
            view.ShowMenu("Market of " + BoatshipsNames.Island(port),
                (message != null ? message + "\n\n" : "") + "Gold " + economy.Gold + "   Hold " + Used + " / " + Capacity +
                "\nPrices change from port to port and day to day. Your Trade skill gets you better deals.", options);
        }

        private void TradeMenu(ArchipelagoWorld.IslandSite port, ItemDef item, Action back)
        {
            int day = travel.Day;
            int buy = BoatshipsItems.BuyPrice(item, port.Cell, day, Discount);
            int sell = BoatshipsItems.SellPrice(item, port.Cell, day, SellBonus);
            int held = Count(state.cargo, item.Id);
            int affordable = Mathf.Min(economy.Gold / Mathf.Max(1, buy), Free / Mathf.Max(1, item.Weight));
            var options = new List<MapMenuOption>();
            foreach (int amount in new[] { 1, 5, 20 })
            {
                int n = amount;
                options.Add(new MapMenuOption("Buy " + n + " for " + n * buy + " gold", () => Trade(port, item, n, buy, true, back), affordable >= n));
            }
            options.Add(new MapMenuOption("Buy as much as fits (" + affordable + ")", () => Trade(port, item, affordable, buy, true, back), affordable > 0));
            foreach (int amount in new[] { 1, 5 })
            {
                int n = amount;
                options.Add(new MapMenuOption("Sell " + n + " for " + n * sell + " gold", () => Trade(port, item, n, sell, false, back), held >= n));
            }
            options.Add(new MapMenuOption("Sell all " + held + " for " + held * sell + " gold", () => Trade(port, item, held, sell, false, back), held > 0));
            options.Add(new MapMenuOption("Back to the market", () => MarketMenu(port, back)));
            view.ShowMenu(item.Name, item.Description + "\n\nBuy " + buy + "   Sell " + sell + "   In your hold " + held +
                "\nGold " + economy.Gold + "   Free hold space " + Free, options);
        }

        private void Trade(ArchipelagoWorld.IslandSite port, ItemDef item, int count, int unitPrice, bool buying, Action back)
        {
            if (count <= 0) { TradeMenu(port, item, back); return; }
            if (buying)
            {
                if (Free < count * item.Weight || !economy.TryAdjustGold(-count * unitPrice)) { MarketMenu(port, back, "You cannot afford or carry that."); return; }
                Add(state.cargo, item.Id, count);
            }
            else
            {
                if (!Remove(state.cargo, item.Id, count)) { MarketMenu(port, back); return; }
                if (!economy.TryAdjustGold(count * unitPrice)) { Add(state.cargo, item.Id, count); MarketMenu(port, back, "The sale fell through."); return; }
                if (character != null) character.AddExperience(CharacterSkill.Trade, Mathf.Clamp(count * unitPrice / 40, 1, 20));
            }
            Save();
            TradeMenu(port, item, back);
        }

        /// <summary>Weapons, armour, helmets and medical supplies; buys treasure and spare equipment.</summary>
        public void ArmourerMenu(ArchipelagoWorld.IslandSite port, Action back, string message = null)
        {
            if (!Ready) return;
            var options = new List<MapMenuOption>();
            int day = travel.Day;
            uint stockSeed = (uint)(port.DetailSeed ^ (day / 7) * 7919);
            foreach (ItemDef def in BoatshipsItems.All)
            {
                if (!(def.Equipment || def.Kind == ItemKind.Consumable) || def.Price <= 10) continue;
                // Each port stocks a changing selection of arms (restocked weekly); medicine is always on sale.
                if (def.Kind != ItemKind.Consumable && (stockSeed + (uint)def.Id.Length * 131u + def.Id[0]) % 3u == 0u) continue;
                ItemDef item = def;
                int price = BoatshipsItems.BuyPrice(item, port.Cell, day, Discount);
                options.Add(new MapMenuOption("Buy " + item.Name + " — " + price + " gold  (" + Stats(item) + ")",
                    () =>
                    {
                        if (!economy.TryAdjustGold(-price)) { ArmourerMenu(port, back, "Not enough gold."); return; }
                        Add(state.bag, item.Id, 1); Save();
                        ArmourerMenu(port, back, "Bought " + item.Name + (item.Equipment ? ". Equip it from your inventory [I]." : "."));
                    }, economy.Gold >= price));
            }
            options.Add(new MapMenuOption("Sell from your bag…", () => SellMenu(port, back)));
            options.Add(new MapMenuOption("Back", back));
            view.ShowMenu("Armourer", (message != null ? message + "\n\n" : "") + "Gold " + economy.Gold, options);
        }

        private void SellMenu(ArchipelagoWorld.IslandSite port, Action back)
        {
            var options = new List<MapMenuOption>();
            foreach (ItemStack stack in state.bag)
            {
                ItemDef item = BoatshipsItems.Get(stack.id);
                int price = BoatshipsItems.SellPrice(item, port.Cell, travel.Day, SellBonus);
                options.Add(new MapMenuOption("Sell " + item.Name + (stack.count > 1 ? " (" + stack.count + ")" : "") + " — " + price + " gold",
                    () => { if (Remove(state.bag, item.Id, 1) && economy.TryAdjustGold(price)) Save(); SellMenu(port, back); }));
            }
            options.Add(new MapMenuOption("Back", () => ArmourerMenu(port, back)));
            view.ShowMenu("Sell", "Treasure fetches a good price; used gear does not. Gold " + economy.Gold, options);
        }

        private static string Stats(ItemDef item)
        {
            switch (item.Kind)
            {
                case ItemKind.Weapon: return "dmg ×" + item.Damage.ToString("0.00") + ", reach ×" + item.Reach.ToString("0.00") + ", speed ×" + item.Speed.ToString("0.00");
                case ItemKind.Armor:
                case ItemKind.Helmet: return "+" + item.Health + " health" + (item.Block > 0f ? ", +" + Mathf.RoundToInt(item.Block * 100f) + "% guard" : "");
                case ItemKind.Consumable: return "+" + item.Heal + " health";
                default: return "worth ~" + item.Price;
            }
        }

        private static string Describe(List<ItemStack> stacks, string empty)
        {
            if (stacks.Count == 0) return empty;
            var parts = new List<string>();
            foreach (ItemStack stack in stacks) parts.Add(BoatshipsItems.Get(stack.id).Name + (stack.count > 1 ? " ×" + stack.count : ""));
            return string.Join(", ", parts);
        }
    }
}
