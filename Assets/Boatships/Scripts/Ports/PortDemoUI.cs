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
        private enum Tab { Store, Tavern, Repairs, Missions }
        [SerializeField] private PortDocking docking;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private PortMissions missions;
        [SerializeField] private Text missionTracker;
        [SerializeField] private Button jumpToMission;
        private int missionSlot;
        private string missionMessage;
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

        public void BindMissions(PortMissions board, Text tracker, Button jump)
        {
            missions = board;
            missionTracker = tracker;
            jumpToMission = jump;
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
            if (missions != null)
            {
                Bind("PortOverlay/Content/TabRow/TabMissions", () =>
                {
                    tab = Tab.Missions;
                    missionMessage = null;
                    Refresh(true);
                });
                jumpToMission.onClick.AddListener(missions.JumpToObjectiveForTesting);
            }
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
            BoatshipsDebugMenu.Register(this, jumpToMission, vicinityPanel.transform.Find("JumpButton"));
            ApplyLayout();
            Refresh(true);
        }

        /// <summary>PC layout: a compact dock prompt above the weapon bar, the quest tracker top-left,
        /// and the shared Bannerlord-style button skin on every port control.</summary>
        private void ApplyLayout()
        {
            if (vicinityPanel != null)
            {
                RectTransform panel = (RectTransform)vicinityPanel.transform;
                BoatshipsUI.Place(panel, new Vector2(0.5f, 0f), new Vector2(0f, 172f), new Vector2(620f, 76f));
                Image image = vicinityPanel.GetComponent<Image>();
                if (image != null) { image.color = BoatshipsUI.Panel; BoatshipsUI.Frame(vicinityPanel, 2f); }
                if (nearbyText != null)
                {
                    BoatshipsUI.Anchor(nearbyText.rectTransform, 0.03f, 0.06f, 0.66f, 0.94f);
                    nearbyText.fontSize = 19;
                    nearbyText.color = BoatshipsUI.TextColor;
                }
                if (dockButton != null)
                {
                    BoatshipsUI.Anchor((RectTransform)dockButton.transform, 0.68f, 0.16f, 0.97f, 0.84f);
                    Text label = dockButton.GetComponentInChildren<Text>(true);
                    if (label != null) label.text = "Dock  [E]";
                }
            }
            if (missionTracker != null)
            {
                RectTransform tracker = (RectTransform)missionTracker.transform.parent;
                BoatshipsUI.Place(tracker, new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(460f, 200f));
                Image image = tracker.GetComponent<Image>();
                if (image != null) { image.color = BoatshipsUI.Panel; BoatshipsUI.Frame(tracker.gameObject, 2f); }
                BoatshipsUI.Stretch(missionTracker.rectTransform);
                missionTracker.rectTransform.offsetMin = new Vector2(14f, 10f);
                missionTracker.rectTransform.offsetMax = new Vector2(-14f, -10f);
                missionTracker.fontSize = 18;
                missionTracker.color = BoatshipsUI.TextColor;
            }
            foreach (Button button in GetComponentsInChildren<Button>(true)) BoatshipsUI.StyleButton(button);
            Transform content = overlay != null ? overlay.transform.Find("Content") : null;
            Image card = content != null ? content.GetComponent<Image>() : null;
            if (card != null) { card.color = BoatshipsUI.Panel; BoatshipsUI.Frame(card.gameObject, 2f); }
            if (headingText != null) headingText.color = BoatshipsUI.Title;
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
            // Only prompt when a harbor is actually close, instead of a permanent bottom bar.
            SetVisible(vicinityPanel, !docked && docking.HasPort && (docking.CanDock || docking.DistanceToPort < 150f));
            SetVisible(overlay, docked && !shipyard);
            SetVisible(shipyardReturn, shipyard);
            if (missions != null)
            {
                SetVisible(missionTracker.transform.parent.gameObject, missions.ShowTracker);
                if (missions.ShowTracker) missionTracker.text = missions.TrackerText();
                jumpToMission.interactable = missions.HasTrackedMission;
            }
            if (vicinityPanel != null && nearbyText != null)
            {
                string name = docking.HasPort ?
                    "Port " + docking.NearestPort.Cell.x + "," + docking.NearestPort.Cell.y : "No port";
                nearbyText.text = name + "  —  " + docking.DistanceToPort.ToString("0") + " m\n" +
                    (docking.CanDock ? "Ready to dock" : "Sail closer to dock");
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
                if (actionButtons[i] != null) actionButtons[i].gameObject.SetActive(
                    tab == Tab.Store || tab == Tab.Missions || i <= (tab == Tab.Tavern ? 2 : 0));
            if (tab == Tab.Store)
            {
                if (detailText != null)
                    detailText.text = "STORE — Purchase provisions and ship supplies. Purchased spare cannons can be installed in the shipyard. Other equipment uses wood/iron.";
                SetAction(0, "Buy 5 Wood — " + economy.BuyPrice(0) + " gold", economy.Gold >= economy.BuyPrice(0));
                SetAction(1, "Buy 3 Iron — " + economy.BuyPrice(1) + " gold", economy.Gold >= economy.BuyPrice(1));
                SetAction(2, "Buy 5 Rations — " + economy.BuyPrice(2) + " gold", economy.Gold >= economy.BuyPrice(2));
                SetAction(3, "Buy 1 Spare Cannon — " + economy.BuyPrice(3) + " gold", economy.Gold >= economy.BuyPrice(3));
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
            else if (tab == Tab.Missions)
            {
                detailText.text = missions.Describe(missionSlot);
                if (!string.IsNullOrEmpty(missionMessage))
                    messageText.text = missionMessage + "\n" + economy.LastMessage;
                SetAction(0, "Pirate Hunt", missionSlot != 0);
                SetAction(1, "Island Survey", missionSlot != 1);
                SetAction(2, "Cargo Delivery", missionSlot != 2);
                bool canAct = missions.GetAction(missionSlot, out string label);
                SetAction(3, label, canAct);
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
                case Tab.Missions:
                    if (index < PortMissions.ContractCount)
                    {
                        missionSlot = index;
                        missionMessage = null;
                    }
                    else missionMessage = missions.TakeAction(missionSlot);
                    break;
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
