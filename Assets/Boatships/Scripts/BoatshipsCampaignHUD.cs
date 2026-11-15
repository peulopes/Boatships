using System;
using System.Collections;
using UnityEngine;
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
        private static readonly string[] tutorial = {
            "Welcome aboard Boatships. Steer with the left joystick or WASD. Aim with the right joystick, select a weapon group, then Fire.",
            "Approach a port and tap Dock. The Store sells supplies, the Tavern recruits crew, and the Shipyard fits equipment. Depart when ready.",
            "In Campaign → Hull, choose a grid cell and add or remove blocks. Each addition costs 1 wood. Unsupported sections detach. Heavy equipment increases draft and reduces speed.",
            "Recruit sailors and cycle their duty to Fighter for boarding, Gunner for reload bonuses, Carpenter for repairs, or Sailor for handling. Treat injuries at a port.",
            "Campaign contracts form a repeating chain of naval combat, exploration, delivery, and capture. Accept and claim them while docked. Reputation unlocks upgrades and lowers faction store prices.",
            "Weaken an enemy hull below 65%, approach within 8m, then choose Board Nearby in Fleet. Fighters cross to its deck and fight. Captured vessels join your fleet.",
            "Sail within 9m of floating cargo to collect it. Rescue overboard sailors with the Rescue button. Wrecks, cargo, crew, missions, and ship layouts persist per world seed.",
            "Settings provides three save slots, checkpoint status, graphics quality, 30/60 FPS, and volume. Slot 1 imports your earlier saves. Device performance must be checked on your phone."
        };

        public void Configure(BoatshipsMobilePresentation mobile, BoatshipsConstruction builder,
            BoatshipsEncounterDirector director, BoatshipsBoarding combat, BoatshipsCampaignJournal missions,
            BoatshipsSaveCoordinator checkpoints, PortEconomy account, PortDocking port)
        {
            presentation = mobile; construction = builder; encounters = director; boarding = combat;
            journal = missions; saves = checkpoints; economy = account; docking = port;
        }

        private IEnumerator Start()
        {
            while (presentation.SafeRoot == null) yield return null;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform menu = MakeObject("Campaign Menu", presentation.SafeRoot, typeof(Image), typeof(Button));
            menu.anchorMin = menu.anchorMax = new Vector2(0.5f, 1f);
            menu.pivot = new Vector2(0.5f, 1f); menu.anchoredPosition = new Vector2(0f, -12f);
            menu.sizeDelta = new Vector2(230f, 54f);
            menu.GetComponent<Image>().color = new Color(0.08f, 0.25f, 0.3f);
            menu.GetComponent<Button>().targetGraphic = menu.GetComponent<Image>();
            Text caption = MakeLabel("Campaign", menu, 24); Stretch(caption.rectTransform);
            menu.GetComponent<Button>().onClick.AddListener(Toggle);
            backdrop = MakeObject("Campaign Overlay", presentation.SafeRoot, typeof(Image)).gameObject;
            Stretch(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = new Color(0.02f, 0.06f, 0.08f, 0.85f);
            card = MakeObject("Campaign Panel", backdrop.transform, typeof(Image));
            card.GetComponent<Image>().color = new Color(0.04f, 0.12f, 0.16f, 0.98f);
            RectTransform tabs = MakeObject("Navigation", card, typeof(HorizontalLayoutGroup));
            tabs.anchorMin = new Vector2(0.02f, 0.89f); tabs.anchorMax = new Vector2(0.98f, 0.98f);
            tabs.offsetMin = tabs.offsetMax = Vector2.zero;
            HorizontalLayoutGroup row = tabs.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 4f; row.childForceExpandWidth = true; row.childForceExpandHeight = true;
            string[] names = { "Hull", "Fleet", "Contracts", "Upgrades", "Settings", "Help" };
            for (int i = 0; i < names.Length; i++)
            {
                int requested = i;
                MakeButton(names[i], tabs, () => { page = requested; BuildPage(); }, 44f, 15);
            }
            RectTransform viewport = MakeObject("Scroll View", card, typeof(Image), typeof(Mask), typeof(ScrollRect));
            viewport.anchorMin = new Vector2(0.03f, 0.11f); viewport.anchorMax = new Vector2(0.97f, 0.87f);
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0.03f, 0.1f, 0.13f);
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
            RectTransform close = MakeButton("Return to game", card, Toggle, 54f).GetComponent<RectTransform>();
            close.anchorMin = new Vector2(0.25f, 0.02f); close.anchorMax = new Vector2(0.75f, 0.09f);
            close.offsetMin = close.offsetMax = Vector2.zero;
            backdrop.SetActive(false);
            CreateMainMenu();
            if (BoatshipsSaveCoordinator.ConsumeNewGameLaunch())
            {
                if (PlayerPrefs.GetInt("Boatships.TutorialComplete", 0) == 0) Toggle();
            }
            else ShowMainMenu();
        }

        private void CreateMainMenu()
        {
            mainMenu = MakeObject("Boatships Main Menu", presentation.SafeRoot, typeof(Image)).gameObject;
            Stretch(mainMenu.GetComponent<RectTransform>());
            mainMenu.GetComponent<Image>().color = new Color(0.02f, 0.07f, 0.1f);
            RectTransform panel = MakeObject("Start Panel", mainMenu.transform, typeof(VerticalLayoutGroup));
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(720f, 520f);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            Text title = MakeLabel("BOATSHIPS", panel, 64);
            title.alignment = TextAnchor.MiddleCenter;
            title.color = new Color(0.65f, 0.9f, 0.88f);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;
            mainMenuInfo = MakeLabel("Preparing world…", panel, 24);
            mainMenuInfo.alignment = TextAnchor.MiddleCenter;
            mainMenuInfo.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
            newGameButton = MakeButton("New Game", panel, StartNewGame, 86f, 30);
            newGameButton.GetComponent<Image>().color = new Color(0.75f, 0.5f, 0.2f);
            newGameButton.GetComponentInChildren<Text>().color = new Color(0.03f, 0.07f, 0.1f);
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
            backdrop.GetComponent<Image>().color = new Color(0.02f, 0.06f, 0.08f, building ? 0.12f : 0.85f);
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
                MakeButton("Frame rate — " + presentation.FrameRate, content,
                    () => { presentation.ToggleFrameRate(); BuildPage(); });
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

        private void Update()
        {
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
            label.font = font; label.fontSize = size; label.color = Color.white;
            label.text = text; label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
            return label;
        }

        private Button MakeButton(string title, Transform parent, Action action, float height = 58f, int size = 22)
        {
            RectTransform rect = MakeObject(title, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().preferredHeight = height;
            rect.GetComponent<Image>().color = new Color(0.12f, 0.32f, 0.39f);
            Button button = rect.GetComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() => { presentation.Click(); action(); });
            Text label = MakeLabel(title, rect, size); label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform);
            return button;
        }

        private void MakeSlider(string label, float min, float max, float value, bool integer, Action<float> change)
        {
            Text caption = MakeLabel(label, content, 20);
            caption.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            RectTransform rect = MakeObject(label, content, typeof(Image), typeof(Slider), typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().preferredHeight = 42f;
            rect.GetComponent<Image>().color = new Color(0.16f, 0.3f, 0.35f);
            RectTransform handle = MakeObject("Handle", rect, typeof(Image));
            handle.sizeDelta = new Vector2(28f, 42f); handle.GetComponent<Image>().color = new Color(0.6f, 0.85f, 0.85f);
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
