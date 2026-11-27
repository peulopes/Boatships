using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    public enum ItemKind { Weapon, Armor, Helmet, Consumable, Treasure, TradeGood }

    /// <summary>One kind of item. Equipment changes first-person combat; trade goods ride in the ship's hold.</summary>
    public sealed class ItemDef
    {
        public string Id;
        public string Name;
        public ItemKind Kind;
        public int Price;          // base value in gold
        public int Weight = 1;     // hold space per unit (trade goods) or carried weight (personal items)
        public float Damage = 1f;  // weapon damage multiplier
        public float Reach = 1f;   // weapon reach multiplier
        public float Speed = 1f;   // weapon swing speed multiplier (higher is faster)
        public int Health;         // armour / helmet extra health
        public float Block;        // extra share of damage blocked (0..1)
        public int Heal;           // consumable: health restored
        public string Description = "";

        public bool Equipment => Kind == ItemKind.Weapon || Kind == ItemKind.Armor || Kind == ItemKind.Helmet;
    }

    [Serializable]
    public sealed class ItemStack
    {
        public string id;
        public int count;
    }

    /// <summary>The item catalogue and market prices. Each port produces some goods cheaply and pays well
    /// for others (seeded by its cell), and prices drift from day to day, so buying low in one port and
    /// selling high in another pays, as in Bannerlord.</summary>
    public static class BoatshipsItems
    {
        public const string StarterWeapon = "cutlass";

        private static readonly List<ItemDef> all = new List<ItemDef>
        {
            // Weapons
            new ItemDef { Id = "cutlass", Name = "Cutlass", Kind = ItemKind.Weapon, Price = 40, Description = "A sailor's short blade. Quick and reliable." },
            new ItemDef { Id = "rapier", Name = "Rapier", Kind = ItemKind.Weapon, Price = 120, Damage = 0.9f, Reach = 1.25f, Speed = 1.25f, Description = "Long, light and fast." },
            new ItemDef { Id = "boarding_axe", Name = "Boarding Axe", Kind = ItemKind.Weapon, Price = 95, Damage = 1.35f, Reach = 0.9f, Speed = 0.85f, Description = "Heavy blows at close quarters." },
            new ItemDef { Id = "sabre", Name = "Naval Sabre", Kind = ItemKind.Weapon, Price = 180, Damage = 1.25f, Reach = 1.1f, Speed = 1.05f, Description = "An officer's sword of fine steel." },
            new ItemDef { Id = "broadsword", Name = "Broadsword", Kind = ItemKind.Weapon, Price = 260, Damage = 1.55f, Reach = 1.2f, Speed = 0.8f, Description = "Slow, but it splits shields." },
            // Armour
            new ItemDef { Id = "linen_shirt", Name = "Linen Shirt", Kind = ItemKind.Armor, Price = 10, Description = "No protection at all." },
            new ItemDef { Id = "leather_jerkin", Name = "Leather Jerkin", Kind = ItemKind.Armor, Price = 70, Health = 20, Description = "Turns a glancing cut." },
            new ItemDef { Id = "padded_coat", Name = "Padded Coat", Kind = ItemKind.Armor, Price = 140, Health = 35, Description = "Thick quilted cloth." },
            new ItemDef { Id = "officer_coat", Name = "Officer's Coat", Kind = ItemKind.Armor, Price = 220, Health = 45, Block = 0.05f, Description = "Mail sewn under fine wool." },
            new ItemDef { Id = "breastplate", Name = "Steel Breastplate", Kind = ItemKind.Armor, Price = 380, Health = 70, Description = "Heavy, and very hard to cut through." },
            // Helmets
            new ItemDef { Id = "bandana", Name = "Bandana", Kind = ItemKind.Helmet, Price = 8, Health = 2, Description = "Keeps the sweat out of your eyes." },
            new ItemDef { Id = "tricorn", Name = "Tricorn Hat", Kind = ItemKind.Helmet, Price = 45, Health = 8, Description = "A captain's hat." },
            new ItemDef { Id = "morion", Name = "Morion Helmet", Kind = ItemKind.Helmet, Price = 160, Health = 25, Block = 0.05f, Description = "A crested steel helmet." },
            // Consumables
            new ItemDef { Id = "bandage", Name = "Bandages", Kind = ItemKind.Consumable, Price = 12, Heal = 40, Description = "Press [H] in a fight to bind your wounds (+40 health)." },
            new ItemDef { Id = "grog", Name = "Bottle of Grog", Kind = ItemKind.Consumable, Price = 6, Heal = 15, Description = "Press [H] in a fight for a swig (+15 health)." },
            // Treasure (sell at any store)
            new ItemDef { Id = "silver_cup", Name = "Silver Cup", Kind = ItemKind.Treasure, Price = 60, Description = "Taken from a captain's cabin." },
            new ItemDef { Id = "pearl", Name = "Black Pearl", Kind = ItemKind.Treasure, Price = 120, Description = "Worth a small fortune to a jeweller." },
            new ItemDef { Id = "gold_idol", Name = "Gold Idol", Kind = ItemKind.Treasure, Price = 300, Description = "Heavy, old and very valuable." },
            // Trade goods (ship's hold)
            new ItemDef { Id = "fish", Name = "Salt Fish", Kind = ItemKind.TradeGood, Price = 8, Description = "Cheap everywhere, needed everywhere." },
            new ItemDef { Id = "grain", Name = "Grain", Kind = ItemKind.TradeGood, Price = 10, Description = "Sacks of island wheat." },
            new ItemDef { Id = "rum", Name = "Rum", Kind = ItemKind.TradeGood, Price = 22, Description = "Distilled where cane grows." },
            new ItemDef { Id = "sugar", Name = "Sugar", Kind = ItemKind.TradeGood, Price = 26, Description = "Crates of cane sugar." },
            new ItemDef { Id = "tobacco", Name = "Tobacco", Kind = ItemKind.TradeGood, Price = 34, Description = "Cured leaf from the volcanic isles." },
            new ItemDef { Id = "cloth", Name = "Cloth", Kind = ItemKind.TradeGood, Price = 40, Description = "Bolts of dyed cotton." },
            new ItemDef { Id = "spices", Name = "Spices", Kind = ItemKind.TradeGood, Price = 65, Description = "Pepper and clove: light and precious." },
            new ItemDef { Id = "silk", Name = "Silk", Kind = ItemKind.TradeGood, Price = 110, Description = "The most valuable cargo afloat." },
        };

        private static readonly Dictionary<string, ItemDef> byId = new Dictionary<string, ItemDef>();

        static BoatshipsItems()
        {
            foreach (ItemDef item in all) byId[item.Id] = item;
        }

        public static IReadOnlyList<ItemDef> All => all;
        public static ItemDef Get(string id) => id != null && byId.TryGetValue(id, out ItemDef item) ? item : null;

        public static IEnumerable<ItemDef> OfKind(ItemKind kind)
        {
            foreach (ItemDef item in all) if (item.Kind == kind) yield return item;
        }

        /// <summary>How a port values a good, 0.6 (it makes plenty) … 1.6 (it craves it), drifting by day.</summary>
        public static float PortFactor(Vector2Int port, string itemId, int day)
        {
            uint h = Hash(port.x, port.y, itemId);
            float baseFactor = 0.6f + (h % 1000u) / 1000f;            // fixed local supply and demand
            float drift = Mathf.Sin(day * 0.7f + (h >> 10) % 628u / 100f) * 0.12f; // day-to-day market swings
            return Mathf.Clamp(baseFactor + drift, 0.5f, 1.75f);
        }

        /// <summary>Price to buy one unit at a port. Selling fetches less (merchants take a cut).</summary>
        public static int BuyPrice(ItemDef item, Vector2Int port, int day, float discount)
        {
            float factor = item.Kind == ItemKind.TradeGood ? PortFactor(port, item.Id, day) : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(item.Price * factor * (1f - Mathf.Clamp(discount, 0f, 0.5f))));
        }

        public static int SellPrice(ItemDef item, Vector2Int port, int day, float bonus)
        {
            float factor = item.Kind == ItemKind.TradeGood ? PortFactor(port, item.Id, day) : 1f;
            float cut = item.Kind == ItemKind.Treasure ? 0.9f : item.Kind == ItemKind.TradeGood ? 0.88f : 0.5f;
            return Mathf.Max(1, Mathf.RoundToInt(item.Price * factor * cut * (1f + Mathf.Clamp(bonus, 0f, 0.5f))));
        }

        private static uint Hash(int x, int y, string id)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char c in id) h = (h ^ c) * 16777619u;
                h = (h ^ (uint)x) * 0x85EBCA6Bu;
                h = (h ^ (uint)y) * 0xC2B2AE35u;
                h ^= h >> 13;
                return h;
            }
        }
    }
}
