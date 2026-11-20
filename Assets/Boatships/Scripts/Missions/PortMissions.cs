using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boatships
{
    internal enum MissionKind { Hunt, Explore, Deliver }
    internal enum MissionStage { Active, Ready, Claimed }

    [Serializable]
    internal sealed class MissionContract
    {
        public MissionKind kind;
        public MissionStage stage;
        public Vector2Int origin;
        public Vector2Int destination;
    }

    [DefaultExecutionOrder(600)]
    [DisallowMultipleComponent]
    public sealed class PortMissions : MonoBehaviour
    {
        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private PortDocking docking;
        [SerializeField] private BattleLootSystem loot;
        [SerializeField] private ShipCustomization shipyard;
        private float nextObjectiveCheck;
        internal const int ContractCount = 3;

        public void Configure(ArchipelagoWorld map, PortEconomy wallet, PortDocking port,
            BattleLootSystem battleLoot, ShipCustomization customization)
        {
            world = map;
            economy = wallet;
            docking = port;
            loot = battleLoot;
            shipyard = customization;
        }

        private bool HasAccount => world != null && economy != null && economy.ActiveSeed == world.Seed;
        internal bool ShowTracker => HasAccount && !docking.IsDocked && !shipyard.InShipyard;
        internal bool HasTrackedMission
        {
            get
            {
                if (!HasAccount) return false;
                foreach (MissionContract mission in economy.Missions)
                    if (mission.stage != MissionStage.Claimed) return true;
                return false;
            }
        }
        private bool AtBoard => HasAccount && docking.IsDocked && !docking.ViewingShipyard &&
            world.TryGetIsland(docking.DockedPort.Cell, out var port) && port.Kind == IslandKind.PortSite;

        private MissionContract Find(MissionKind kind)
        {
            foreach (MissionContract mission in economy.Missions)
                if (mission.kind == kind) return mission;
            return null;
        }

        private MissionContract Offer(MissionKind kind)
        {
            var port = docking.DockedPort;
            var target = port;
            if (kind != MissionKind.Hunt &&
                !world.TryNearestOtherIsland(port, kind == MissionKind.Deliver, out target))
                return null;
            return new MissionContract { kind = kind, origin = port.Cell, destination = target.Cell };
        }

        private static string Title(MissionKind kind)
        {
            switch (kind)
            {
                case MissionKind.Hunt: return "Pirate Hunt";
                case MissionKind.Explore: return "Island Survey";
                default: return "Sealed Cargo Delivery";
            }
        }

        private static PortEconomy.LootCache Reward(MissionKind kind)
        {
            switch (kind)
            {
                case MissionKind.Hunt:
                    return new PortEconomy.LootCache { gold = 180, iron = 3, spareCannons = 1 };
                case MissionKind.Explore:
                    return new PortEconomy.LootCache { gold = 90, wood = 5 };
                default:
                    return new PortEconomy.LootCache { gold = 120, rations = 6 };
            }
        }

        private static string Cell(Vector2Int cell) => "(" + cell.x + ", " + cell.y + ")";

        private static Vector2Int TurnInPort(MissionContract mission) =>
            mission.kind == MissionKind.Deliver ? mission.destination : mission.origin;

        private string Objective(MissionContract mission)
        {
            if (mission.stage == MissionStage.Claimed) return "Reward claimed.";
            if (mission.stage == MissionStage.Ready)
                return "Claim reward at port " + Cell(TurnInPort(mission)) + ".";
            switch (mission.kind)
            {
                case MissionKind.Hunt:
                    return loot.PirateDefeated ? "The pirate is already defeated in this seed." :
                        "Defeat the designated pirate. Turn in at port " + Cell(mission.origin) + ".";
                case MissionKind.Explore:
                    return "Survey island " + Cell(mission.destination) + " from offshore. Return to " +
                        Cell(mission.origin) + ".";
                default:
                    return "Carry 1 sealed crate and dock at port " + Cell(mission.destination) + ".";
            }
        }

        internal string Describe(int slot)
        {
            if (!AtBoard) return "Dock to access the mission board.";
            var kind = (MissionKind)slot;
            MissionContract accepted = Find(kind);
            MissionContract mission = accepted ?? Offer(kind);
            if (mission == null) return Title(kind) + "\nNo eligible destination in this world.";
            string stage = accepted == null ? "Available" : accepted.stage.ToString();
            return Title(kind) + " / " + stage + "\n" + Objective(mission) + "\nReward " +
                Reward(kind).Describe();
        }

        internal bool GetAction(int slot, out string label)
        {
            label = "Unavailable";
            if (!AtBoard) return false;
            var kind = (MissionKind)slot;
            MissionContract mission = Find(kind);
            if (mission == null)
            {
                label = "Accept Contract";
                return Offer(kind) != null;
            }
            label = mission.stage == MissionStage.Claimed ? "Reward Claimed" : "Claim Reward";
            if (mission.stage == MissionStage.Active) label = "Objective In Progress";
            return mission.stage == MissionStage.Ready && docking.DockedPort.Cell == TurnInPort(mission);
        }

        internal string TakeAction(int slot)
        {
            if (!GetAction(slot, out _)) return "";
            var kind = (MissionKind)slot;
            MissionContract mission = Find(kind);
            if (mission != null)
                return economy.ClaimMission(mission, Reward(kind)) ? "Reward added to your account." :
                    "Could not save the reward. Try again.";
            if (!economy.AcceptMission(Offer(kind))) return "Could not save the contract. Try again.";
            UpdateObjectives();
            return "Contract accepted.";
        }

        private void Update()
        {
            if (!HasAccount) return;
            if (Time.unscaledTime >= nextObjectiveCheck)
            {
                nextObjectiveCheck = Time.unscaledTime + 0.25f;
                UpdateObjectives();
            }
            if (BoatshipsDebugMenu.IsOpen && Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame)
                JumpToObjectiveForTesting();
        }

        private void UpdateObjectives()
        {
            foreach (MissionContract mission in economy.Missions)
            {
                if (mission.stage == MissionStage.Active && IsObjectiveMet(mission))
                    economy.MarkMissionReady(mission);
            }
        }

        private bool IsObjectiveMet(MissionContract mission)
        {
            switch (mission.kind)
            {
                case MissionKind.Hunt: return loot.PirateDefeated;
                case MissionKind.Deliver:
                    return AtBoard && docking.DockedPort.Cell == mission.destination;
                default:
                    return !docking.IsDocked && !shipyard.InShipyard &&
                        world.TryGetIsland(mission.destination, out var site) &&
                        ToSite(site).magnitude <= site.Radius * 0.78f + 18f;
            }
        }

        private Vector2 ToSite(ArchipelagoWorld.IslandSite site) => site.Center -
            new Vector2(world.Player.position.x, world.Player.position.z);

        private bool GetTargetPortOrIsland(MissionContract mission, out ArchipelagoWorld.IslandSite site)
        {
            return world.TryGetIsland(mission.stage == MissionStage.Ready ?
                TurnInPort(mission) : mission.destination, out site);
        }

        internal string TrackerText()
        {
            string text = "";
            if (HasAccount)
            {
                foreach (MissionContract mission in economy.Missions)
                {
                    if (mission.stage == MissionStage.Claimed) continue;
                    string navigation = "";
                    if (mission.kind == MissionKind.Hunt && mission.stage == MissionStage.Active &&
                        loot.PirateTarget != null)
                    {
                        Vector3 delta = loot.PirateTarget.position - world.Player.position;
                        navigation = Bearing(new Vector2(delta.x, delta.z));
                    }
                    else if (GetTargetPortOrIsland(mission, out var site))
                        navigation = Bearing(ToSite(site));
                    if (text.Length > 0) text += "\n";
                    text += Objective(mission) + navigation;
                }
            }
            return text.Length > 0 ? text : "Dock and open Missions to take a contract.";
        }

        private static string Bearing(Vector2 delta)
        {
            int degrees = Mathf.RoundToInt((Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg + 360f) % 360f) % 360;
            return $" [{delta.magnitude:0} m / {degrees}°]";
        }

        public void JumpToObjectiveForTesting()
        {
            if (!ShowTracker) return;
            foreach (MissionContract mission in economy.Missions)
            {
                if (mission.stage == MissionStage.Claimed) continue;
                if (mission.kind == MissionKind.Hunt && mission.stage == MissionStage.Active)
                {
                    loot.JumpToEncounterForTesting();
                    return;
                }
                if (!GetTargetPortOrIsland(mission, out var site)) continue;
                Vector2 approach = -ToSite(site).normalized;
                if (approach == Vector2.zero) approach = Vector2.down;
                Vector2 point = site.Center + approach * (site.Radius * 0.78f + 7f);
                Vector3 target = new Vector3(point.x, 0.8f, point.y);
                if (world.Player.TryGetComponent(out Rigidbody body))
                {
                    body.position = target;
                    body.linearVelocity = Vector3.zero;
                }
                else world.Player.position = target;
                return;
            }
        }
    }
}
