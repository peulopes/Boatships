using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Boatships
{
    /// <summary>Prisoners: survivors taken after a battle, limited by the room aboard the flagship and her company.
    /// How each enemy sailor was put down (blade, shot, cannon fire, sinking) decides how many live to be taken.</summary>
    public sealed partial class BoatshipsTravelController
    {
        private bool prisonersOffered;

        /// <summary>Room for prisoners aboard the flagship: her size, plus every cabin and hold fitted.</summary>
        public int FlagshipPrisonerCapacity
        {
            get
            {
                if (character == null || !character.OwnsShip || ship == null) return 0;
                VoxelHull hull = ship.GetComponentInChildren<VoxelHull>();
                ShipCustomization fitting = ship.GetComponent<ShipCustomization>();
                int capacity = 2 + (hull != null && hull.Definition != null ? hull.Definition.Length / 4 : 2);
                if (fitting != null)
                    capacity += 3 * fitting.CountInstalled(ShipEquipmentKind.Cabin) + fitting.CountInstalled(ShipEquipmentKind.Storage);
                return capacity;
            }
        }

        /// <summary>Room for prisoners across the whole fleet: the flagship plus every captured ship.</summary>
        public int PrisonerCapacityNow()
        {
            int capacity = FlagshipPrisonerCapacity;
            foreach (EncounterRecord vessel in economy.CapturedShips()) capacity += vessel.PrisonerCapacity;
            economy.PrisonerCapacity = capacity;
            return capacity;
        }

        /// <summary>Releases prisoners the fleet no longer has room for (a ship sold or lost, cabins removed).</summary>
        private void EnforcePrisonerCapacity(string reason)
        {
            int released = economy.ReleasePrisoners(PrisonerCapacityNow());
            if (released > 0) Notify(released + " prisoner" + (released == 1 ? "" : "s") + " " + reason + ".");
        }

        private List<EncounterRecord> BattleSurvivors()
        {
            var sources = new List<EncounterRecord>();
            foreach (string id in battleIds)
            {
                EncounterRecord record = economy.Campaign.encounters.Find(item => item.id == id);
                if (record != null && (record.defeated || record.captured) && !record.prisonersResolved && record.Survivors > 0)
                    sources.Add(record);
            }
            return sources;
        }

        /// <summary>After the battle: who survived on the enemy ships, how they fell, and how many you can hold.</summary>
        private void SurvivorsMenu()
        {
            List<EncounterRecord> sources = BattleSurvivors();
            if (sources.Count == 0) return;
            int capacity = PrisonerCapacityNow();
            int free = Mathf.Max(0, capacity - economy.Prisoners.Count);
            int total = 0;
            var body = new StringBuilder("When the smoke clears, survivors are found among the enemy crews:");
            foreach (EncounterRecord record in sources)
            {
                total += record.Survivors;
                body.Append("\n• ").Append(Capitalize(record.DisplayName)).Append(record.captured ? " (taken)" : " (sunk)")
                    .Append(": ").Append(record.Survivors).Append(" of ").Append(Mathf.Max(record.crewMax, record.casualties))
                    .Append(" crew alive — ").Append(record.SurvivorBreakdown);
            }
            body.Append("\n\nCutlasses leave the most men alive; cannon fire and pistol shot the fewest.")
                .Append("\n\nRoom below deck: ").Append(free).Append(" of ").Append(capacity)
                .Append(" (flagship ").Append(FlagshipPrisonerCapacity).Append(", company ").Append(capacity - FlagshipPrisonerCapacity).Append(").")
                .Append(" Prisoners eat rations. Hand them to a harbour for a bounty, or recruit the willing.");
            int take = Mathf.Min(free, total);
            var options = new List<MapMenuOption>
            {
                new MapMenuOption(take == 0 ? "No room for prisoners — free space or add cabins" :
                    take < total ? "Take " + take + " prisoners (no room for the other " + (total - take) + ")" :
                    "Take all " + take + " prisoners", () => TakeSurvivors(sources, take), take > 0),
                new MapMenuOption("Leave them to their fate", () => { foreach (EncounterRecord record in sources) economy.LeaveSurvivors(record.id); })
            };
            view.ShowMenu("Survivors", body.ToString(), options);
        }

        private void TakeSurvivors(List<EncounterRecord> sources, int count)
        {
            int taken = 0;
            foreach (EncounterRecord record in sources)
                taken += economy.TakePrisoners(record.id, count - taken); // leaves the rest of each crew behind
            string leadership = taken > 0 ? character.AddExperience(CharacterSkill.Leadership, 2 * taken) : null;
            Notify(taken + " prisoner" + (taken == 1 ? "" : "s") + " locked below. Hold " + economy.Prisoners.Count + "/" +
                PrisonerCapacityNow() + "." + (leadership != null ? " " + leadership : ""));
        }

        /// <summary>The prisoners held across the fleet: recruit the willing, release them, or (at a port) hand them over.</summary>
        private void PrisonersMenu(bool atPort, Action back)
        {
            IReadOnlyList<Prisoner> held = economy.Prisoners;
            int capacity = PrisonerCapacityNow();
            var body = new StringBuilder("Prisoners ").Append(held.Count).Append("/").Append(capacity).Append(":");
            int bounty = 0;
            for (int i = 0; i < held.Count; i++)
            {
                bounty += held[i].Bounty;
                if (i >= 10) continue;
                body.Append("\n• ").Append(held[i].name).Append(", ").Append(held[i].WoundText)
                    .Append(string.IsNullOrEmpty(held[i].ship) ? "" : ", of the " + held[i].ship)
                    .Append(held[i].willing ? " — willing to serve" : "");
            }
            if (held.Count > 10) body.Append("\n…and ").Append(held.Count - 10).Append(" more.");
            if (!atPort) body.Append("\n\nThe harbour guard at any port pays a bounty for them.");
            var options = new List<MapMenuOption>();
            if (atPort)
                options.Add(new MapMenuOption("Hand them all to the harbour guard — " + bounty + " gold bounty", () =>
                {
                    economy.HandOverPrisoners(out _, out _);
                    Notify(economy.LastMessage);
                    back?.Invoke();
                }, held.Count > 0));
            int offered = 0;
            foreach (Prisoner prisoner in held)
            {
                if (!prisoner.willing || offered >= 6) continue;
                offered++;
                string id = prisoner.id;
                options.Add(new MapMenuOption("Recruit " + prisoner.name + " (skill " + prisoner.skill + ")", () =>
                {
                    economy.RecruitPrisoner(id);
                    Notify(economy.LastMessage);
                    PrisonersMenu(atPort, back);
                }, economy.Crew.Count < 24));
            }
            options.Add(new MapMenuOption(atPort ? "Release them all ashore" : "Set them all adrift in a longboat", () =>
            {
                int released = economy.ReleasePrisoners();
                Notify(released + " prisoner" + (released == 1 ? "" : "s") + " released.");
                back?.Invoke();
            }, held.Count > 0));
            options.Add(new MapMenuOption(back != null ? "Back" : "Close", back));
            view.ShowMenu("Prisoners", body.ToString(), options);
        }
    }
}
