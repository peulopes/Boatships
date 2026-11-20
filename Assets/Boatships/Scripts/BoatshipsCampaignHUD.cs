using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Boatships
{
    public sealed class BoatshipsCampaignHUD : MonoBehaviour
    {
        [SerializeField] private BoatshipsMobilePresentation presentation;
        [SerializeField] private BoatshipsConstruction construction;
        [SerializeField] private BoatshipsEncounterDirector encounters;
        [SerializeField] private BoatshipsBoarding boarding;
        [SerializeField] private BoatshipsCampaignJournal journal;
        [SerializeField] private BoatshipsSaveCoordinator saves;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private PortDocking docking;
        private GameObject backdrop;
        private RectTransform card;
        private Transform content;
        private Text detail;
        private Font font;
        private int page = 5;
        private Vector3Int cell;
        private float nextRefresh;
        private float previousTimeScale = 1f;
        private bool open;
        private GameObject mainMenu;
        private Text mainMenuInfo;
        private Text mainMenuStatus;
        private Button newGameButton;
        private Button continueButton;
        private float mainMenuTimeScale = 1f;
        private BoatshipsCharacter character;
        private BoatshipsCharacterCreation creation;
        private BoatshipsWorldMapView view;
        private Button[] tabButtons;

        public const int HullPage = 0, FleetPage = 1, ContractsPage = 2, UpgradesPage = 3, SettingsPage = 4, HelpPage = 5;
        private static readonly string[] pageNames = { "Hull", "Fleet", "Contracts", "Upgrades", "Settings", "Help" };
        private static readonly string[] pageKeys = { "H", "L", "J", "U", "Esc", "F1" };
        private static readonly string[] tutorial = {
            "Welcome to Boatships. You travel on the world map: left-click the sea or an island to set a course, drag to pan and scroll to zoom. Space pauses, 1 / 2 / 3 set the time speed, Home re-centres on your ship. Time passes while you sail, and your crew eats rations every day.",
            "Arriving at a port opens the town menu (E). With a ship, go to the harbor: the Store sells supplies, the Tavern recruits crew, and the Shipyard fits equipment. Without a ship, work aboard AI ships from the tavern, book passage, or save up for a sloop at the shipwright.",
            "Choose Walk into town to explore a port in first person: WASD to walk, Space to jump, click to capture the mouse and look around, E to use a door (tavern, store, shipwright, harbor master). Tab leaves the town; Esc opens the menu.",
            "Working aboard AI ships pays wages when the voyage ends and trains the skill your role uses. If pirates attack your employer, you choose whether to fight alongside the crew.",
            "Pirates hunt captains on the map. When one reaches you, fight, try to outrun them, or pay them off. Battles, coasts and pirate havens are played in 3D: steer with WASD, select a weapon group with 1-4, aim with the arrow keys and hold Space to fire. Right-drag rotates the camera, scroll zooms. Press M for the world map once no enemy is within 120m.",
            "Campaign → Hull (H): choose a grid cell and add or remove blocks. Each addition costs 1 wood. Unsupported sections detach. Heavy equipment increases draft and reduces speed.",
            "Crew (P) opens your crew roster: cycle a sailor's duty to Fighter for boarding, Gunner for reload bonuses, Carpenter for repairs, or Sailor for handling. Treat injuries at a port.",
            "Contracts (J) form a repeating chain of naval combat, exploration, delivery, and capture. Accept and claim them while docked. Reputation unlocks Upgrades (U) and lowers faction store prices.",
            "Weaken an enemy hull below 65%, approach within 8m, then choose Board Nearby in Fleet (L). Fighters cross to its deck and fight. Captured vessels join your fleet.",
            "Sail within 9m of floating cargo and press F to collect it. Wrecks, cargo, crew, missions, and ship layouts persist per world seed.",
            "Settings (Esc) provides three save slots, checkpoints, graphics quality, frame rate and volume. C opens your character sheet at any time."
        };

        public void Configure(BoatshipsMobilePresentation mobile, BoatshipsConstruction builder,
            BoatshipsEncounterDirector director, BoatshipsBoarding combat, BoatshipsCampaignJournal missions,
            BoatshipsSaveCoordinator checkpoints, PortEconomy account, PortDocking port)
        {
            presentation = mobile; construction = builder; encounters = director; boarding = combat;
            journal = missions; saves = checkpoints; economy = account; docking = port;
        }

        /// <summary>Connects the world-map voyage layer (installed at scene load by BoatshipsVoyageBootstrap).</summary>
        public void BindVoyage(BoatshipsCharacter captain, BoatshipsCharacterCreation creator, BoatshipsWorldMapView mapView = null)
        { character = captain; creation = creator; view = mapView; }

        /// <summary>True while the main menu or the campaign panel covers the screen.</summary>
        public bool BlockingOverlayOpen => open || (mainMenu != null && mainMenu.activeSelf);

        public void OpenCampaignMenu() { if (!open && mainMenu != null && !mainMenu.activeSelf) Toggle(); }

        /// <summary>Opens the campaign screen on a page, or switches page if it is already open.</summary>
        public void OpenCampaignPage(int requested)
        {
            if (mainMenu == null || mainMenu.activeSelf) return;
            page = Mathf.Clamp(requested, 0, pageNames.Length - 1);
            if (!open) Toggle(); else BuildPage();
        }

        private IEnumerator Start()
        {
            while (presentation.SafeRoot == null) yield return null;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            backdrop = MakeObject("Campaign Overlay", presentation.SafeRoot, typeof(Image)).gameObject;
            Stretch(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = new Color(0.03f, 0.025f, 0.02f, 0.82f);
            card = MakeObject("Campaign Panel", backdrop.transform, typeof(Image));
            card.GetComponent<Image>().color = BoatshipsUI.Panel;
            BoatshipsUI.Frame(card.gameObject, 2f);
            RectTransform tabs = MakeObject("Navigation", card, typeof(HorizontalLayoutGroup));
            tabs.anchorMin = new Vector2(0.02f, 0.89f); tabs.anchorMax = new Vector2(0.98f, 0.98f);
            tabs.offsetMin = tabs.offsetMax = Vector2.zero;
            HorizontalLayoutGroup row = tabs.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 4f; row.childForceExpandWidth = true; row.childForceExpandHeight = true;
            tabButtons = new Button[pageNames.Length];
            for (int i = 0; i < pageNames.Length; i++)
            {
                int requested = i;
                tabButtons[i] = MakeButton(pageNames[i] + "  [" + pageKeys[i] + "]", tabs, () => { page = requested; BuildPage(); }, 44f, 17);
                tabButtons[i].name = pageNames[i]; // Stable object path for the play checks.
            }
            RectTransform viewport = MakeObject("Scroll View", card, typeof(Image), typeof(Mask), typeof(ScrollRect));
            viewport.anchorMin = new Vector2(0.03f, 0.11f); viewport.anchorMax = new Vector2(0.97f, 0.87f);
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = BoatshipsUI.PanelLight;
            viewport.GetComponent<Mask>().showMaskGraphic = true;
            RectTransform body = MakeObject("Content", viewport, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            body.anchorMin = new Vector2(0f, 1f); body.anchorMax = Vector2.one; body.pivot = new Vector2(0.5f, 1f);
            body.offsetMin = body.offsetMax = Vector2.zero;
            VerticalLayoutGroup column = body.GetComponent<VerticalLayoutGroup>();
            column.spacing = 12f; column.padding = new RectOffset(12, 12, 12, 12);
            column.childControlHeight = true; column.childForceExpandHeight = false;
            body.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = viewport.GetComponent<ScrollRect>();
            scroll.content = body; scroll.viewport = viewport; scroll.horizontal = false;
            content = body;
            RectTransform close = MakeButton("Return to game  [Esc]", card, Toggle, 54f).GetComponent<RectTransform>();
            close.anchorMin = new Vector2(0.25f, 0.02f); close.anchorMax = new Vector2(0.75f, 0.09f);
            close.offsetMin = close.offsetMax = Vector2.zero;
            backdrop.SetActive(false);
            CreateMainMenu();
            if (BoatshipsSaveCoordinator.ConsumeNewGameLaunch())
            {
                if (PlayerPrefs.GetInt("Boatships.TutorialComplete", 0) == 0) Toggle();
            }
            else ShowMainMenu();
            StartCoroutine(RequireCharacter());
        }

        /// <summary>A world without a character (first launch, or a save from before character creation)
        /// starts in the character creation screen.</summary>
        private IEnumerator RequireCharacter()
        {
            if (character == null || creation == null) yield break;
            while (!saves.IsReady || !character.Loaded) yield return null;
            if (character.HasCharacter) yield break;
            if (!mainMenu.activeSelf) ShowMainMenu();
            OpenCreation(false);
        }

        private void OpenCreation(bool allowBack)
        {
            creation.Open(allowBack, null, profile =>
            {
                if (!saves.StartNewGame()) return saves.Status;
                // Applied by BoatshipsCharacter once the freshly generated world has loaded.
                BoatshipsCharacter.SetPendingProfile(profile);
                boarding.Abort();
                Time.timeScale = mainMenuTimeScale;
                return null;
            });
        }

        private void CreateMainMenu()
        {
            mainMenu = MakeObject("Boatships Main Menu", presentation.SafeRoot, typeof(Image)).gameObject;
            Stretch(mainMenu.GetComponent<RectTransform>());
            mainMenu.GetComponent<Image>().color = BoatshipsUI.Background;
            RectTransform panel = MakeObject("Start Panel", mainMenu.transform, typeof(VerticalLayoutGroup));
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(720f, 520f);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            Text title = MakeLabel("BOATSHIPS", panel, 64);
            title.alignment = TextAnchor.MiddleCenter;
            title.color = BoatshipsUI.Title;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;
            mainMenuInfo = MakeLabel("Preparing world…", panel, 24);
            mainMenuInfo.alignment = TextAnchor.MiddleCenter;
            mainMenuInfo.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
            newGameButton = MakeButton("New Game", panel, StartNewGame, 86f, 30);
            newGameButton.GetComponent<Image>().color = BoatshipsUI.Accent;
            newGameButton.GetComponentInChildren<Text>().color = BoatshipsUI.Ink;
            continueButton = MakeButton("Continue", panel, ContinueGame, 76f, 28);
            mainMenuStatus = MakeLabel("", panel, 22);
            mainMenuStatus.alignment = TextAnchor.MiddleCenter;
            mainMenuStatus.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
            mainMenu.SetActive(false);
        }

        private void ShowMainMenu()
        {
            Close();
            mainMenuTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            mainMenuStatus.text = "";
            mainMenu.SetActive(true);
            mainMenu.transform.SetAsLastSibling();
        }

        private void HideMainMenu()
        {
            mainMenu.SetActive(false);
            Time.timeScale = mainMenuTimeScale;
            if (PlayerPrefs.GetInt("Boatships.TutorialComplete", 0) == 0)
            { page = 5; Toggle(); }
        }

        private void StartNewGame()
        {
            if (creation != null && character != null) { OpenCreation(true); return; }
            if (!saves.StartNewGame()) { mainMenuStatus.text = saves.Status; return; }
            boarding.Abort();
            HideMainMenu();
        }

        private void ContinueGame() { if (saves.IsReady) HideMainMenu(); }

        private void Toggle()
        {
            open = !open;
            backdrop.SetActive(open);
            if (open) { previousTimeScale = Time.timeScale; Time.timeScale = 0f; BuildPage(); }
            else { Time.timeScale = previousTimeScale; construction.ShowCursor(false, cell); }
        }

        private void Close() { if (open) Toggle(); }

        private void BuildPage()
        {
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            bool building = page == 0;
            card.anchorMin = building ? new Vector2(0.64f, 0.03f) : new Vector2(0.15f, 0.06f);
            card.anchorMax = new Vector2(0.98f, 0.94f); card.offsetMin = card.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = new Color(0.03f, 0.025f, 0.02f, building ? 0.12f : 0.82f);
            for (int i = 0; tabButtons != null && i < tabButtons.Length; i++)
                tabButtons[i].targetGraphic.color = i == page ? BoatshipsUI.Selected : BoatshipsUI.ButtonColor;
            detail = MakeLabel("", content, 22);
            detail.gameObject.AddComponent<LayoutElement>().preferredHeight = building ? 150f : 190f;
            if (page == 0)
            {
                HullDefinition definition = construction.Hull.Definition;
                MakeSlider("Across hull (X)", 0, definition.Width - 1, cell.x, true, v => cell.x = Mathf.RoundToInt(v));
                MakeSlider("Hull layer (Y)", 0, definition.Height - 1, cell.y, true, v => cell.y = Mathf.RoundToInt(v));
                MakeSlider("Along hull (Z)", 0, definition.Length - 1, cell.z, true, v => cell.z = Mathf.RoundToInt(v));
                MakeButton("Open Shipyard", content, () => { docking.OpenShipyard(); });
                MakeButton("Add block — 1 wood", content, () => construction.Edit(cell.x, cell.y, cell.z, true));
                MakeButton("Remove block", content, () => construction.Edit(cell.x, cell.y, cell.z, false));
            }
            else if (page == 1)
            {
                MakeButton("Board Nearby", content, () => { boarding.Begin(); if (boarding.InProgress) Close(); });
                MakeButton("Recall Boarding Party", content, boarding.Abort);
                MakeButton("Collect Nearby Cargo", content, encounters.CollectNearbyCargo);
                MakeButton("Rescue Nearby Survivor", content, () =>
                    construction.GetComponent<BoatshipsCrewProgression>().RecoverOverboardAtPort());
                MakeButton("Recover Sunken Flagship — up to 150 gold", content, construction.RecoverAtPort, 76f);
            }
            else if (page == 2) MakeButton("Accept / Claim Contract", content, journal.ContractAction);
            else if (page == 3)
                for (int i = 0; i < 3; i++)
                {
                    int category = i;
                    MakeButton(journal.UpgradeText(i), content, () => { journal.BuyUpgrade(category); BuildPage(); }, 92f, 20);
                }
            else if (page == 4)
            {
                MakeButton("Main Menu", content, ShowMainMenu);
                MakeButton("Save Checkpoint", content, () => saves.SaveCheckpoint());
                for (int i = 0; i < 3; i++)
                {
                    int slot = i;
                    MakeButton("Continue in Save Slot " + (i + 1) + (saves.Slot == i ? " — Current" : ""),
                        content, () => saves.SwitchSlot(slot));
                }
                MakeButton("Quality — " + new[] { "Low", "Medium", "High" }[presentation.Quality], content,
                    () => { presentation.CycleQuality(); BuildPage(); });
                MakeButton("Frame rate — " + (presentation.FrameRate > 0 ? presentation.FrameRate + " FPS" : "Unlimited"), content,
                    () => { presentation.CycleFrameRate(); BuildPage(); });
                MakeSlider("Audio Volume", 0f, 1f, presentation.Volume, false, presentation.SetVolume);
            }
            else
            {
                int step = Mathf.Clamp(PlayerPrefs.GetInt("Boatships.TutorialStep", 0), 0, tutorial.Length - 1);
                MakeButton(step == tutorial.Length - 1 ? "Finish Guide" : "Next", content, () =>
                {
                    if (step == tutorial.Length - 1)
                    { PlayerPrefs.SetInt("Boatships.TutorialComplete", 1); PlayerPrefs.Save(); Close(); }
                    else { PlayerPrefs.SetInt("Boatships.TutorialStep", step + 1); BuildPage(); }
                });
                MakeButton("Restart Guide", content, () => { PlayerPrefs.SetInt("Boatships.TutorialStep", 0); BuildPage(); });
                MakeButton("Skip Guide", content, () =>
                    { PlayerPrefs.SetInt("Boatships.TutorialComplete", 1); PlayerPrefs.Save(); Close(); });
            }
            nextRefresh = 0f;
        }

        private void HandleHotkeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || backdrop == null || mainMenu == null || mainMenu.activeSelf) return;
            if (BoatshipsDebugMenu.IsOpen || BoatshipsUI.Typing) return;
            if (creation != null && creation.IsOpen) return;
            if (!open && view != null && view.MenuOpen) return; // Story menus need an explicit choice.
            if (keyboard.escapeKey.wasPressedThisFrame) { if (open) Toggle(); else OpenCampaignPage(SettingsPage); return; }
            if (keyboard.hKey.wasPressedThisFrame) OpenCampaignPage(HullPage);
            else if (keyboard.lKey.wasPressedThisFrame) OpenCampaignPage(FleetPage);
            else if (keyboard.jKey.wasPressedThisFrame) OpenCampaignPage(ContractsPage);
            else if (keyboard.uKey.wasPressedThisFrame) OpenCampaignPage(UpgradesPage);
            else if (keyboard.f1Key.wasPressedThisFrame) OpenCampaignPage(HelpPage);
        }

        private void Update()
        {
            HandleHotkeys();
            if (mainMenu != null && mainMenu.activeSelf)
            {
                newGameButton.interactable = saves.IsReady;
                continueButton.interactable = saves.IsReady;
                mainMenuInfo.text = saves.IsReady ? "Save slot " + (saves.Slot + 1) + "  •  World " + saves.WorldSeed : "Preparing world…";
                return;
            }
            if (!open || detail == null) return;
            construction.ShowCursor(page == 0 && docking.IsDocked, cell);
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            CampaignProgress p = economy.Campaign;
            if (page == 0) detail.text = "HULL — Cell " + cell + "\n" + construction.Status +
                "\n" + construction.Hull.RemainingBlocks + "/" + construction.Hull.TotalBlocks + " blocks | Wood " + economy.Wood;
            else if (page == 1)
            {
                encounters.NearestEnemy(out EncounterRecord nearest, out float distance);
                detail.text = "FLEET — " + p.captures + " captured, " + p.victories + " victories\n" +
                    (nearest != null ? nearest.kind + " Tier " + nearest.tier + " — " + distance.ToString("0") + "m\n" : "No nearby enemy\n") +
                    boarding.Status + "\n" + encounters.Status;
            }
            else if (page == 2) detail.text = journal.ContractText + "\n" + journal.Status;
            else if (page == 3) detail.text = "Reputation " + p.reputation + " | Gold " + economy.Gold +
                " | Wood " + economy.Wood + " | Iron " + economy.Iron + "\n" + journal.Status;
            else if (page == 4) detail.text = saves.Status + "\nSlot " + (saves.Slot + 1) +
                " | Frame time " + presentation.AverageFrameMilliseconds.ToString("0.0") + "ms\n" +
                "Faction — " + journal.Faction + " | Store discount " + (economy.TradingDiscount * 100f).ToString("0") + "%";
            else detail.text = tutorial[Mathf.Clamp(PlayerPrefs.GetInt("Boatships.TutorialStep", 0), 0, tutorial.Length - 1)];
        }

        private RectTransform MakeObject(string name, Transform parent, params Type[] components)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            foreach (Type component in components) go.AddComponent(component);
            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }

        private Text MakeLabel(string text, Transform parent, int size)
        {
            Text label = MakeObject("Label", parent, typeof(Text)).GetComponent<Text>();
            label.font = font; label.fontSize = size; label.color = BoatshipsUI.TextColor;
            label.text = text; label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
            return label;
        }

        private Button MakeButton(string title, Transform parent, Action action, float height = 58f, int size = 22)
        {
            RectTransform rect = MakeObject(title, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().preferredHeight = height;
            rect.GetComponent<Image>().color = BoatshipsUI.ButtonColor;
            Button button = rect.GetComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() => { presentation.Click(); action(); });
            Text label = MakeLabel(title, rect, size); label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform);
            BoatshipsUI.StyleButton(button);
            return button;
        }

        private void MakeSlider(string label, float min, float max, float value, bool integer, Action<float> change)
        {
            Text caption = MakeLabel(label, content, 20);
            caption.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            RectTransform rect = MakeObject(label, content, typeof(Image), typeof(Slider), typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().preferredHeight = 42f;
            rect.GetComponent<Image>().color = BoatshipsUI.ButtonColor;
            RectTransform handle = MakeObject("Handle", rect, typeof(Image));
            handle.sizeDelta = new Vector2(28f, 42f); handle.GetComponent<Image>().color = BoatshipsUI.Accent;
            Slider slider = rect.GetComponent<Slider>(); slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>(); slider.minValue = min; slider.maxValue = max;
            slider.wholeNumbers = integer; slider.value = value;
            slider.onValueChanged.AddListener(v => change(v));
        }

        private void OnDestroy()
        {
            if (mainMenu != null && mainMenu.activeSelf) Time.timeScale = mainMenuTimeScale;
            else if (open) Time.timeScale = previousTimeScale;
        }
    }
}
