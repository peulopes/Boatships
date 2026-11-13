using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>
    /// Small touch-friendly prototype menu. Scene builder creates named buttons;
    /// callbacks are connected at runtime so the generated scene serializes correctly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortDemoUI : MonoBehaviour
    {
        private enum Tab { Store, Tavern, Repairs }
        [SerializeField] private PortDocking docking;
        [SerializeField] private PortEconomy economy;
        private Tab tab;
        private GameObject vicinityPanel;
        private GameObject overlay;
        private GameObject shipyardReturn;
        private Text nearbyText;
        private Text headingText;
        private Text accountText;
        private Text detailText;
        private Text messageText;
        private Button dockButton;
        private readonly Button[] actionButtons = new Button[4];
        private readonly Text[] actionLabels = new Text[4];
        private float nextRefresh;

        public void Configure(PortDocking port, PortEconomy portEconomy)
        {
            docking = port;
            economy = portEconomy;
        }

        private void Start()
        {
            vicinityPanel = Find("VicinityPanel");
            overlay = Find("PortOverlay");
            shipyardReturn = Find("ShipyardReturnPanel");
            nearbyText = TextAt("VicinityPanel/HintText");
            headingText = TextAt("PortOverlay/Content/Title");
            accountText = TextAt("PortOverlay/Content/Account");
            detailText = TextAt("PortOverlay/Content/Details");
            messageText = TextAt("PortOverlay/Content/Message");
            dockButton = Bind("VicinityPanel/DockButton", () => docking.Dock());
            Bind("VicinityPanel/JumpButton", () => docking.JumpToPortForTesting());
            Bind("PortOverlay/Content/TabRow/TabStore", () => { tab = Tab.Store; Refresh(true); });
            Bind("PortOverlay/Content/TabRow/TabTavern", () => { tab = Tab.Tavern; Refresh(true); });
            Bind("PortOverlay/Content/TabRow/TabRepair", () => { tab = Tab.Repairs; Refresh(true); });
            Bind("PortOverlay/Content/FooterRow/OpenShipyard", () => docking.OpenShipyard());
            Bind("PortOverlay/Content/FooterRow/DepartButton", () => docking.Undock());
            Bind("ShipyardReturnPanel/BackButton", () => docking.ReturnToPort());
            for (int i = 0; i < actionButtons.Length; ++i)
            {
                int selected = i;
                actionButtons[i] = Bind("PortOverlay/Content/Action" + i,
                    () => DoAction(selected));
                if (actionButtons[i] != null)
                    actionLabels[i] = actionButtons[i].GetComponentInChildren<Text>(true);
            }
            Refresh(true);
        }

        private GameObject Find(string path)
        {
            Transform found = transform.Find(path);
            if (found == null) Debug.LogWarning("Boatships port UI element missing: " + path, this);
            return found != null ? found.gameObject : null;
        }

        private Text TextAt(string path)
        {
            GameObject go = Find(path);
            return go != null ? go.GetComponent<Text>() : null;
        }

        private Button Bind(string path, UnityEngine.Events.UnityAction action)
        {
            GameObject go = Find(path);
            if (go == null) return null;
            Button button = go.GetComponent<Button>();
            if (button != null) button.onClick.AddListener(action);
            return button;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.15f;
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            if (docking == null || economy == null) return;
            bool docked = docking.IsDocked;
            bool shipyard = docked && docking.ViewingShipyard;
            SetVisible(vicinityPanel, !docked);
            SetVisible(overlay, docked && !shipyard);
            SetVisible(shipyardReturn, shipyard);
            if (vicinityPanel != null && nearbyText != null)
            {
                string name = docking.HasPort ?
                    "Port " + docking.NearestPort.Cell.x + "," + docking.NearestPort.Cell.y : "No port";
                nearbyText.text = name + " | Distance " + docking.DistanceToPort.ToString("0") + " m\n" +
                    (docking.CanDock ? "Ready to dock (E)" : "Sail toward port, or use test jump (O)");
            }
            if (dockButton != null) dockButton.interactable = docking.CanDock;
            if (!docked || shipyard) return;
            if (headingText != null)
                headingText.text = "HARBOR — " + docking.DockedPort.Cell.x + "," + docking.DockedPort.Cell.y;
            if (accountText != null)
                accountText.text = "Gold " + economy.Gold + "  |  Wood " + economy.Wood +
                    "  |  Iron " + economy.Iron + "  |  Rations " + economy.Rations +
                    "  |  Spare cannons " + economy.SpareCannons + "  |  Crew " + economy.Crew.Count;
            if (messageText != null) messageText.text = docking.Status + "\n" + economy.LastMessage;
            for (int i = 0; i < actionButtons.Length; ++i)
                if (actionButtons[i] != null) actionButtons[i].gameObject.SetActive(tab == Tab.Store || i <= (tab == Tab.Tavern ? 2 : 0));
            if (tab == Tab.Store)
            {
                if (detailText != null)
                    detailText.text = "STORE — Purchase provisions and ship supplies. Purchased spare cannons can be installed in the shipyard. Other equipment uses wood/iron.";
                SetAction(0, "Buy 5 Wood — 40 gold", economy.Gold >= 40);
                SetAction(1, "Buy 3 Iron — 65 gold", economy.Gold >= 65);
                SetAction(2, "Buy 5 Rations — 25 gold", economy.Gold >= 25);
                SetAction(3, "Buy 1 Spare Cannon — 140 gold", economy.Gold >= 140);
            }
            else if (tab == Tab.Tavern)
            {
                if (detailText != null)
                {
                    string roster = "TAVERN — Hire sailors (crew roster is saved; deck AI comes later).\n";
                    int count = Mathf.Min(3, economy.Crew.Count);
                    for (int i = 0; i < count; i++)
                    {
                        PortEconomy.CrewMember c = economy.Crew[economy.Crew.Count - count + i];
                        roster += c.displayName + " / " + c.role + " (skill " + c.skill + ")  ";
                    }
                    detailText.text = roster;
                }
                for (int i = 0; i < 3; i++)
                {
                    PortEconomy.CrewOffer offer = economy.GetOffer(docking.DockedPort, i);
                    bool hired = economy.IsRecruited(offer.Id);
                    SetAction(i, hired ? offer.Name + " — Hired" :
                        "Hire " + offer.Name + " / " + offer.Role + " / skill " + offer.Skill +
                        " — " + offer.Cost + " gold", !hired && economy.Gold >= offer.Cost);
                }
            }
            else
            {
                int gold = economy.RepairGoldCost(docking.Hull, docking.Flooding);
                int wood = economy.RepairWoodCost(docking.Hull);
                if (detailText != null)
                    detailText.text = "SHIPYARD REPAIRS — Rebuild missing hull voxels, clear flooding and restore flotation. " +
                        "Destroyed equipment is not replaced.\n" +
                        "Hull " + (docking.Hull != null ? docking.Hull.RemainingBlocks : 0) + "/" +
                        (docking.Hull != null ? docking.Hull.TotalBlocks : 0) +
                        " | Flooding " + (docking.Flooding != null ? (docking.Flooding.FloodFraction * 100f).ToString("0") : "0") + "%";
                SetAction(0, gold == 0 ? "No hull repairs required" :
                    "Repair Hull — " + gold + " gold + " + wood + " wood",
                    gold > 0 && economy.Gold >= gold && economy.Wood >= wood);
            }
        }

        private void SetAction(int index, string label, bool available)
        {
            if (actionLabels[index] != null) actionLabels[index].text = label;
            if (actionButtons[index] != null) actionButtons[index].interactable = available;
        }

        private void DoAction(int index)
        {
            if (docking == null || !docking.IsDocked) return;
            switch (tab)
            {
                case Tab.Store: economy.Buy(index); break;
                case Tab.Tavern: economy.Recruit(docking.DockedPort, index); break;
                case Tab.Repairs:
                    if (index == 0) economy.RepairShip(docking.Hull, docking.Flooding);
                    break;
            }
            Refresh(true);
        }

        private static void SetVisible(GameObject go, bool visible)
        {
            if (go != null && go.activeSelf != visible) go.SetActive(visible);
        }
    }
}
