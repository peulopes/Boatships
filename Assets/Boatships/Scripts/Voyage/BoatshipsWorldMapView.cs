using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Boatships
{
    public struct MapMenuOption
    {
        public string Label;
        public Action Action;
        public bool Enabled;

        public MapMenuOption(string label, Action action, bool enabled = true)
        { Label = label; Action = action; Enabled = enabled; }
    }

    /// <summary>Zoomed-out strategic map: painted archipelago, island and ship markers, pan/zoom/tap input,
    /// Bannerlord-style text menus, plus the small HUD shown while in the local 3D scene.</summary>
    [DefaultExecutionOrder(950)]
    public sealed class BoatshipsWorldMapView : MonoBehaviour
    {
        private const float MapUnits = 1024f;
        private const int TextureSize = 512;

        private BoatshipsTravelController travel;
        private float nextLocalHud;
        private ArchipelagoWorld world;
        private BoatshipsCampaignHUD hud;
        private Canvas mapCanvas;
        private Canvas localCanvas;
        private Canvas menuCanvas;
        private RectTransform viewport;
        private RectTransform content;
        private RawImage terrain;
        private Texture2D terrainTexture;
        private RectTransform islandLayer;
        private RectTransform partyLayer;
        private RectTransform route;
        private RectTransform routeEnd;
        private RectTransform player;
        private Image playerImage;
        private RectTransform sight;
        private Text statusText;
        private Text messageText;
        private Button pauseButton;
        private readonly Button[] speedButtons = new Button[3];
        private static readonly float[] Speeds = { 1f, 2f, 4f };
        private Text clockText;
        private Button enterButton;
        private GameObject selection;
        private Text selectionTitle;
        private Text selectionBody;
        private Button selectionAction;
        private GameObject menuRoot;
        private Text menuTitle;
        private Text menuBody;
        private RectTransform menuOptions;
        private Button leaveButton;
        private Button abandonButton;
        private Text localText;
        private BoatshipsSailingHud sailingHud;
        private readonly List<RectTransform> islandMarkers = new List<RectTransform>();
        private readonly List<PartyMarker> partyMarkers = new List<PartyMarker>();
        private Vector2 worldMin;
        private float worldSize = 1f;
        private int textureSeed = int.MinValue;
        private int markerDiscoveries = -1;
        private float zoom = 2.6f;
        private float appliedZoom = -1f;
        private Vector2 focus;
        private bool follow = true;
        private float pinchDistance;
        private bool hasSelectedIsland;
        private ArchipelagoWorld.IslandSite selectedIsland;
        private string selectedParty;
        private float nextStatus;

        private sealed class PartyMarker
        {
            public RectTransform Root;
            public Image Icon;
            public Text Label;
        }

        public bool MenuOpen => menuRoot != null && menuRoot.activeSelf;

        public void Configure(BoatshipsTravelController controller, ArchipelagoWorld map, BoatshipsCampaignHUD campaignHud)
        {
            travel = controller; world = map; hud = campaignHud;
            travel.BindView(this);
            Build();
        }

        // ───────────────────────────── Construction ─────────────────────────────

        private void Build()
        {
            mapCanvas = BoatshipsUI.CreateCanvas("Boatships World Map", 20, transform);
            RectTransform safe = BoatshipsUI.SafeArea(mapCanvas);
            Image backdrop = BoatshipsUI.Image("Ocean Backdrop", safe, new Color(0.03f, 0.1f, 0.15f));
            backdrop.raycastTarget = false;
            BoatshipsUI.Stretch(backdrop.rectTransform);

            viewport = BoatshipsUI.Rect("Map Viewport", safe, typeof(Image), typeof(RectMask2D));
            BoatshipsUI.Stretch(viewport);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            viewport.gameObject.AddComponent<MapInputSurface>().View = this;

            content = BoatshipsUI.Rect("Map Content", viewport);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = new Vector2(MapUnits, MapUnits);
            terrain = BoatshipsUI.Rect("Archipelago", content, typeof(RawImage)).GetComponent<RawImage>();
            BoatshipsUI.Stretch(terrain.rectTransform);
            terrain.raycastTarget = false;
            sight = MarkerImage("Sight", content, BoatshipsUI.Circle, new Color(1f, 1f, 1f, 0.05f)).rectTransform;
            islandLayer = Layer("Islands");
            route = MarkerImage("Route", content, null, new Color(1f, 1f, 1f, 0.55f)).rectTransform;
            route.pivot = new Vector2(0.5f, 0f);
            routeEnd = MarkerImage("Route End", content, BoatshipsUI.Circle, new Color(1f, 1f, 1f, 0.8f)).rectTransform;
            partyLayer = Layer("Ships");
            playerImage = MarkerImage("Player Ship", content, BoatshipsUI.Triangle, Color.white);
            player = playerImage.rectTransform;
            playerImage.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2f, -2f);

            BuildCampaignBar(safe);

            messageText = BoatshipsUI.Label("", safe, 24, TextAnchor.MiddleCenter);
            RectTransform message = messageText.rectTransform;
            message.anchorMin = new Vector2(0.2f, 1f); message.anchorMax = new Vector2(0.8f, 1f);
            message.pivot = new Vector2(0.5f, 1f);
            message.offsetMin = new Vector2(0f, -110f); message.offsetMax = new Vector2(0f, -20f);
            messageText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

            // Context action sits just above the campaign bar, centred, like Bannerlord's settlement prompt.
            enterButton = BoatshipsUI.Button("Enter", safe, () => travel.OpenCurrentIslandMenu(), 64f, 24);
            BoatshipsUI.Place((RectTransform)enterButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 112f), new Vector2(460f, 60f));
            BoatshipsUI.StyleButton(enterButton, BoatshipsUI.Accent);
            enterButton.GetComponentInChildren<Text>().color = BoatshipsUI.Ink;

            BuildSelection(safe);
            // Menus live on their own canvas so towns, battles and the map can all use them.
            menuCanvas = BoatshipsUI.CreateCanvas("Boatships Menus", 30, transform);
            BuildMenu(BoatshipsUI.SafeArea(menuCanvas));
            BuildLocalHud();
        }

        /// <summary>Bottom strip: party summary (left), date and time controls (centre), screens (right).</summary>
        private void BuildCampaignBar(RectTransform safe)
        {
            Image barImage = BoatshipsUI.Image("Campaign Bar", safe, BoatshipsUI.Panel);
            BoatshipsUI.Frame(barImage.gameObject, 2f);
            RectTransform bar = barImage.rectTransform;
            bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(1f, 0f); bar.pivot = new Vector2(0.5f, 0f);
            bar.offsetMin = Vector2.zero; bar.offsetMax = new Vector2(0f, 92f);

            statusText = BoatshipsUI.Label("", bar, 21);
            BoatshipsUI.Anchor(statusText.rectTransform, 0.012f, 0.08f, 0.36f, 0.92f);

            RectTransform time = BoatshipsUI.Image("Time Controls", bar, BoatshipsUI.PanelLight).rectTransform;
            BoatshipsUI.Frame(time.gameObject, 1f);
            time.anchorMin = time.anchorMax = new Vector2(0.5f, 0.5f);
            time.sizeDelta = new Vector2(500f, 76f);
            clockText = BoatshipsUI.Label("", time, 20, TextAnchor.MiddleCenter);
            clockText.color = BoatshipsUI.Title;
            BoatshipsUI.Anchor(clockText.rectTransform, 0f, 0.56f, 1f, 1f);
            RectTransform timeRow = BoatshipsUI.Rect("Speeds", time, typeof(HorizontalLayoutGroup));
            BoatshipsUI.Anchor(timeRow, 0.03f, 0.08f, 0.97f, 0.56f);
            HorizontalLayoutGroup timeLayout = timeRow.GetComponent<HorizontalLayoutGroup>();
            timeLayout.spacing = 6f; timeLayout.childControlWidth = timeLayout.childControlHeight = true;
            timeLayout.childForceExpandWidth = timeLayout.childForceExpandHeight = true;
            pauseButton = BoatshipsUI.Button("|| Pause  [Space]", timeRow, () => travel.Paused = !travel.Paused, 34f, 16);
            for (int i = 0; i < Speeds.Length; i++)
            {
                float speed = Speeds[i];
                speedButtons[i] = BoatshipsUI.Button(new string('>', i + 1) + " x" + speed.ToString("0") + "  [" + (i + 1) + "]",
                    timeRow, () => SetSpeed(speed), 34f, 16);
            }

            RectTransform screens = BoatshipsUI.Rect("Screens", bar, typeof(HorizontalLayoutGroup));
            BoatshipsUI.Anchor(screens, 0.64f, 0.16f, 0.992f, 0.84f);
            HorizontalLayoutGroup screenLayout = screens.GetComponent<HorizontalLayoutGroup>();
            screenLayout.spacing = 6f; screenLayout.childControlWidth = screenLayout.childControlHeight = true;
            screenLayout.childForceExpandWidth = screenLayout.childForceExpandHeight = true;
            BoatshipsUI.Button("Center  [Home]", screens, () => follow = true, 56f, 17);
            BoatshipsUI.Button("Character  [C]", screens, () => travel.CharacterSheet(null), 56f, 17);
            BoatshipsUI.Button("Contracts  [J]", screens, () => { if (hud != null) hud.OpenCampaignPage(BoatshipsCampaignHUD.ContractsPage); }, 56f, 17);
            BoatshipsUI.Button("Menu  [Esc]", screens, () => { if (hud != null) hud.OpenCampaignPage(BoatshipsCampaignHUD.SettingsPage); }, 56f, 17);
        }

        private void SetSpeed(float speed) { travel.TimeSpeed = speed; travel.Paused = false; }

        private RectTransform Layer(string name)
        {
            RectTransform layer = BoatshipsUI.Rect(name, content);
            BoatshipsUI.Stretch(layer);
            return layer;
        }

        private static Image MarkerImage(string name, Transform parent, Sprite sprite, Color color)
        {
            Image image = BoatshipsUI.Image(name, parent, color);
            image.sprite = sprite;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            return image;
        }

        private void BuildSelection(RectTransform safe)
        {
            RectTransform panel = BoatshipsUI.Image("Selection", safe, BoatshipsUI.Panel).rectTransform;
            panel.anchorMin = panel.anchorMax = Vector2.zero; panel.pivot = Vector2.zero;
            panel.sizeDelta = new Vector2(600f, 240f); panel.anchoredPosition = new Vector2(16f, 108f);
            BoatshipsUI.Frame(panel.gameObject, 2f);
            BoatshipsUI.Column(panel, 6f, 14);
            selectionTitle = BoatshipsUI.FixedLabel("", panel, 28, 40f);
            selectionTitle.color = BoatshipsUI.Title;
            selectionBody = BoatshipsUI.FixedLabel("", panel, 20, 104f, TextAnchor.UpperLeft);
            HorizontalLayoutGroup row = BoatshipsUI.Row(panel, 64f, 10f);
            selectionAction = BoatshipsUI.Button("Set sail", row.transform, SelectionAction, 64f, 22);
            BoatshipsUI.StyleButton(selectionAction, BoatshipsUI.Accent);
            selectionAction.GetComponentInChildren<Text>().color = BoatshipsUI.Ink;
            BoatshipsUI.Button("Close", row.transform, ClearSelection, 64f, 22);
            selection = panel.gameObject;
            selection.SetActive(false);
        }

        private void BuildMenu(RectTransform safe)
        {
            Image dim = BoatshipsUI.Image("Map Menu", safe, new Color(0f, 0.02f, 0.04f, 0.72f));
            BoatshipsUI.Stretch(dim.rectTransform);
            menuRoot = dim.gameObject;
            RectTransform card = BoatshipsUI.Image("Card", dim.transform, BoatshipsUI.Panel).rectTransform;
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f); card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(1060f, 600f);
            BoatshipsUI.Frame(card.gameObject, 2f);
            BoatshipsUI.Column(card, 12f, 26);
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            menuTitle = BoatshipsUI.FixedLabel("", card, 36, 54f, TextAnchor.MiddleCenter);
            menuTitle.color = BoatshipsUI.Title;
            menuBody = BoatshipsUI.Label("", card, 23, TextAnchor.UpperLeft);
            menuBody.verticalOverflow = VerticalWrapMode.Overflow;
            menuOptions = BoatshipsUI.Rect("Options", card);
            BoatshipsUI.Column(menuOptions, 8f, 0);
            menuRoot.SetActive(false);
        }

        private void BuildLocalHud()
        {
            localCanvas = BoatshipsUI.CreateCanvas("Boatships Voyage HUD", 5, transform);
            RectTransform safe = BoatshipsUI.SafeArea(localCanvas);
            // Top-right screen buttons; every one also has a hotkey.
            RectTransform row = BoatshipsUI.Rect("Screen Buttons", safe, typeof(HorizontalLayoutGroup));
            BoatshipsUI.Place(row, Vector2.one, new Vector2(-16f, -16f), new Vector2(640f, 48f));
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f; layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            leaveButton = BoatshipsUI.Button("World Map  [M]", row, () => travel.LeaveLocal(), 48f, 17);
            BoatshipsUI.StyleButton(leaveButton, BoatshipsUI.Accent);
            leaveButton.GetComponentInChildren<Text>().color = BoatshipsUI.Ink;
            BoatshipsUI.Button("Crew  [P]", row, () => { if (sailingHud != null) sailingHud.ToggleCrew(); }, 48f, 17);
            BoatshipsUI.Button("Character  [C]", row, () => travel.CharacterSheet(null), 48f, 17);
            BoatshipsUI.Button("Menu  [Esc]", row, () => { if (hud != null) hud.OpenCampaignPage(BoatshipsCampaignHUD.SettingsPage); }, 48f, 17);

            abandonButton = BoatshipsUI.Button("Abandon ship  [X]", safe, () => travel.AbandonShip(), 52f, 20);
            BoatshipsUI.Place((RectTransform)abandonButton.transform, new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(300f, 52f));
            BoatshipsUI.StyleButton(abandonButton, BoatshipsUI.Danger);

            // Battle / location banner, top centre. Text only, so it never blocks clicks.
            localText = BoatshipsUI.Label("", safe, 22, TextAnchor.UpperCenter);
            BoatshipsUI.Place(localText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(580f, 96f));
            localText.color = BoatshipsUI.Title;
            localText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
        }

        /// <summary>Connects the sailing HUD so the Crew button can open its drawer.</summary>
        public void BindSailingHud(BoatshipsSailingHud sailing) => sailingHud = sailing;

        // ───────────────────────────── Menus ─────────────────────────────

        public void ShowMenu(string title, string body, List<MapMenuOption> options)
        {
            ClearSelection();
            menuTitle.text = title;
            menuBody.text = body;
            for (int i = menuOptions.childCount - 1; i >= 0; i--) Destroy(menuOptions.GetChild(i).gameObject);
            foreach (MapMenuOption option in options)
            {
                MapMenuOption chosen = option;
                // Long lists (markets, inventories) use compact rows so the card still fits the screen.
                bool compact = options.Count > 7;
                Button button = BoatshipsUI.Button(option.Label, menuOptions, () =>
                {
                    menuRoot.SetActive(false);
                    chosen.Action?.Invoke();
                }, compact ? 42f : 62f, compact ? 19 : 22);
                button.interactable = option.Enabled;
            }
            menuRoot.SetActive(true);
            menuRoot.transform.SetAsLastSibling();
        }

        // ───────────────────────────── Frame update ─────────────────────────────

        private void Update()
        {
            if (travel == null || world == null) return;
            bool blocked = hud != null && hud.BlockingOverlayOpen;
            bool onMap = travel.Mode == TravelMode.Map;
            mapCanvas.enabled = onMap && !blocked;
            localCanvas.enabled = !onMap && !blocked && travel.Live && !travel.FirstPersonActive;
            menuCanvas.enabled = !blocked;
            HandleHotkeys(onMap, blocked);
            if (!onMap)
            {
                // Text-only readout: a few refreshes a second is plenty and avoids per-frame string building.
                if (Time.unscaledTime >= nextLocalHud) { nextLocalHud = Time.unscaledTime + 0.1f; UpdateLocalHud(); }
                return;
            }
            if (blocked) return;
            if (textureSeed != world.Seed && world.IslandCount > 0) GenerateTerrain();
            if (textureSeed != world.Seed) return;
            HandlePinch();
            UpdateMarkers();
            if (Time.unscaledTime >= nextStatus) { nextStatus = Time.unscaledTime + 0.2f; UpdateTexts(); }
        }

        private void HandleHotkeys(bool onMap, bool blocked)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || blocked || MenuOpen || !travel.Live || BoatshipsDebugMenu.IsOpen || BoatshipsUI.Typing) return;
            if (keyboard.cKey.wasPressedThisFrame) { travel.CharacterSheet(null); return; }
            if (onMap)
            {
                if (keyboard.spaceKey.wasPressedThisFrame) travel.Paused = !travel.Paused;
                for (int i = 0; i < Speeds.Length; i++)
                    if (keyboard[Key.Digit1 + i].wasPressedThisFrame) SetSpeed(Speeds[i]);
                if (keyboard.homeKey.wasPressedThisFrame) follow = true;
                if (keyboard.eKey.wasPressedThisFrame && enterButton.gameObject.activeSelf) travel.OpenCurrentIslandMenu();
                return;
            }
            if (!localCanvas.enabled) return;
            if (keyboard.mKey.wasPressedThisFrame && leaveButton.gameObject.activeSelf) travel.LeaveLocal();
            if (keyboard.xKey.wasPressedThisFrame && abandonButton.gameObject.activeSelf) travel.AbandonShip();
        }

        private void UpdateLocalHud()
        {
            bool canLeave = travel.CanLeaveLocal(out string reason);
            leaveButton.gameObject.SetActive(travel.Mode != TravelMode.Port);
            leaveButton.interactable = canLeave;
            abandonButton.gameObject.SetActive(travel.ShowAbandonShip);
            string message = travel.HasMessage ? travel.Message : "";
            localText.text = travel.Mode == TravelMode.Port ? message :
                travel.LocalStatus + (canLeave || string.IsNullOrEmpty(reason) ? "" : "\n" + reason) +
                (message.Length > 0 ? "\n" + message : "");
        }

        private void UpdateTexts()
        {
            if (!travel.Live) { statusText.text = "Preparing the world map…"; return; }
            PortEconomy economy = travel.Economy;
            BoatshipsCharacter character = travel.Character;
            string role = travel.OnJob ? "Aboard a ship — in transit" : character.OwnsShip ? "Captain" : "No ship";
            statusText.text = "<color=#F2D99E>" + character.Name + "</color>  —  " + role + "\n" +
                "Gold " + economy.Gold + "    Rations " + economy.Rations + "    Crew " + economy.Crew.Count;
            clockText.text = "Day " + travel.Day + ",  " + travel.Hour.ToString("00") + ":00" + (travel.Paused ? "   —   PAUSED" : "");
            BoatshipsUI.SetButton(pauseButton, travel.Paused ? "> Resume  [Space]" : "|| Pause  [Space]", true);
            pauseButton.targetGraphic.color = travel.Paused ? BoatshipsUI.Selected : BoatshipsUI.ButtonColor;
            for (int i = 0; i < Speeds.Length; i++)
                speedButtons[i].targetGraphic.color = !travel.Paused && Mathf.Approximately(travel.TimeSpeed, Speeds[i])
                    ? BoatshipsUI.Selected : BoatshipsUI.ButtonColor;
            messageText.text = travel.HasMessage ? travel.Message : "";
            bool atIsland = travel.AtIsland(out ArchipelagoWorld.IslandSite here) && !travel.OnJob && !MenuOpen && !travel.HasTarget;
            enterButton.gameObject.SetActive(atIsland);
            if (atIsland) BoatshipsUI.SetButton(enterButton, (here.Kind == IslandKind.PortSite ? "Enter " : "Visit ") +
                BoatshipsNames.Island(here) + "  [E]", true);
            UpdateSelectionText();
        }

        // ───────────────────────────── Terrain ─────────────────────────────

        private void GenerateTerrain()
        {
            textureSeed = world.Seed;
            float cell = world.CellSize;
            worldMin = new Vector2(-world.HalfExtent - cell * 0.5f, -world.HalfExtent - cell * 0.5f);
            worldSize = world.SideLength + cell * 0.5f;
            float[] heights = new float[TextureSize * TextureSize];
            IslandBiome[] biomes = new IslandBiome[heights.Length];
            float step = worldSize / TextureSize;
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float wx = worldMin.x + (x + 0.5f) * step;
                float wz = worldMin.y + (y + 0.5f) * step;
                int cx = Mathf.FloorToInt((wx + cell * 0.5f) / cell);
                int cz = Mathf.FloorToInt((wz + cell * 0.5f) / cell);
                float best = -10f;
                IslandBiome biome = IslandBiome.Tropical;
                for (int oz = -1; oz <= 1; oz++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (!world.TryGetIsland(new Vector2Int(cx + ox, cz + oz), out ArchipelagoWorld.IslandSite site)) continue;
                    float dx = wx - site.Center.x, dz = wz - site.Center.y;
                    float reach = site.Radius * 1.12f;
                    if (dx * dx + dz * dz > reach * reach) continue;
                    float h = ProceduralIslandChunk.Height(site, world.Seed, dx, dz);
                    if (h <= best) continue;
                    best = h; biome = site.Biome;
                }
                heights[y * TextureSize + x] = best;
                biomes[y * TextureSize + x] = biome;
            }
            Color32[] pixels = new Color32[heights.Length];
            float edge = world.HalfExtent;
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                int i = y * TextureSize + x;
                float h = heights[i];
                Color color;
                if (h < 0f)
                {
                    float shallow = Mathf.InverseLerp(-2.2f, 0f, h);
                    float ripple = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.03f;
                    color = Color.Lerp(new Color(0.1f + ripple, 0.3f + ripple, 0.43f), new Color(0.27f, 0.6f, 0.63f), shallow * shallow);
                }
                else
                {
                    IslandBiome biome = biomes[i];
                    if (h < 1.45f) color = biome == IslandBiome.Frozen ? new Color(0.85f, 0.9f, 0.93f) :
                        biome == IslandBiome.Volcanic ? new Color(0.3f, 0.28f, 0.27f) : new Color(0.87f, 0.8f, 0.58f);
                    else if (h < 4.3f) color = biome == IslandBiome.Frozen ? new Color(0.68f, 0.82f, 0.88f) :
                        biome == IslandBiome.Volcanic ? new Color(0.38f, 0.2f, 0.15f) : new Color(0.34f, 0.58f, 0.28f);
                    else color = biome == IslandBiome.Volcanic ? new Color(0.25f, 0.2f, 0.2f) : new Color(0.52f, 0.5f, 0.47f);
                    float left = heights[i - (x > 0 ? 1 : 0)], right = heights[i + (x < TextureSize - 1 ? 1 : 0)];
                    float down = heights[i - (y > 0 ? TextureSize : 0)], up = heights[i + (y < TextureSize - 1 ? TextureSize : 0)];
                    float shade = Mathf.Clamp(1f + ((left - right) + (up - down)) * 0.09f, 0.7f, 1.25f);
                    color *= shade;
                    if (h < 0.35f) color = Color.Lerp(color, new Color(0.95f, 0.92f, 0.8f), 0.35f);
                }
                float wx = worldMin.x + (x + 0.5f) * step;
                float wz = worldMin.y + (y + 0.5f) * step;
                float outside = Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz)) - edge;
                if (outside > 0f) color = Color.Lerp(color, new Color(0.02f, 0.05f, 0.08f), Mathf.Clamp01(0.55f + outside * 0.02f));
                color.a = 1f;
                pixels[i] = color;
            }
            if (terrainTexture == null)
                terrainTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
                    { name = "Boatships World Map", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            terrainTexture.SetPixels32(pixels);
            terrainTexture.Apply(false);
            terrain.texture = terrainTexture;
            markerDiscoveries = -1;
            follow = true;
        }

        private Vector2 WorldToMap(Vector2 point) => ((point - worldMin) / worldSize - new Vector2(0.5f, 0.5f)) * MapUnits;
        private Vector2 MapToWorld(Vector2 local) => worldMin + (local / MapUnits + new Vector2(0.5f, 0.5f)) * worldSize;
        private float WorldToMapLength(float length) => length / worldSize * MapUnits;

        // ───────────────────────────── Markers ─────────────────────────────

        private void UpdateMarkers()
        {
            float minimumZoom = Mathf.Max(0.5f, viewport.rect.height / MapUnits * 0.9f);
            zoom = Mathf.Clamp(zoom, minimumZoom, 7f);
            if (travel.Live && follow) focus = Vector2.Lerp(focus, WorldToMap(travel.Position), 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            float limit = MapUnits * 0.5f;
            focus = new Vector2(Mathf.Clamp(focus.x, -limit, limit), Mathf.Clamp(focus.y, -limit, limit));
            content.localScale = Vector3.one * zoom;
            content.anchoredPosition = -focus * zoom;
            float inverse = 1f / zoom;

            int discoveries = travel.Economy.Campaign.discovered.Count;
            if (discoveries != markerDiscoveries || !Mathf.Approximately(appliedZoom, zoom)) RebuildIslandMarkers(discoveries, inverse);
            appliedZoom = zoom;
            if (!travel.Live) { player.gameObject.SetActive(false); route.gameObject.SetActive(false); routeEnd.gameObject.SetActive(false); return; }

            Vector2 here = WorldToMap(travel.Position);
            player.gameObject.SetActive(true);
            player.anchoredPosition = here;
            player.sizeDelta = new Vector2(30f, 36f) * inverse;
            player.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(travel.Heading.x, travel.Heading.y) * Mathf.Rad2Deg);
            playerImage.color = travel.Character.Profile.flagColor;
            sight.anchoredPosition = here;
            sight.sizeDelta = Vector2.one * WorldToMapLength(travel.SightRadius) * 2f;

            bool routed = travel.HasTarget && travel.Captain;
            route.gameObject.SetActive(routed);
            routeEnd.gameObject.SetActive(routed);
            if (routed)
            {
                Vector2 end = WorldToMap(travel.Target);
                Vector2 delta = end - here;
                route.anchoredPosition = here;
                route.sizeDelta = new Vector2(3f * inverse, delta.magnitude);
                route.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg);
                routeEnd.anchoredPosition = end;
                routeEnd.sizeDelta = Vector2.one * 12f * inverse;
            }

            int used = 0;
            foreach (MapParty party in travel.Parties)
            {
                if (!travel.IsVisible(party)) continue;
                if (used == partyMarkers.Count) partyMarkers.Add(CreatePartyMarker());
                PartyMarker marker = partyMarkers[used++];
                marker.Root.gameObject.SetActive(true);
                marker.Root.anchoredPosition = WorldToMap(party.position);
                marker.Root.localScale = Vector3.one * inverse;
                marker.Icon.rectTransform.localRotation =
                    Quaternion.Euler(0f, 0f, -Mathf.Atan2(party.heading.x, party.heading.y) * Mathf.Rad2Deg);
                marker.Icon.color = party.faction == MapFaction.Merchant ? new Color(0.95f, 0.88f, 0.62f) :
                    party.faction == MapFaction.Navy ? new Color(0.35f, 0.55f, 1f) : new Color(0.9f, 0.18f, 0.16f);
                bool label = zoom > 2.2f || party.chasing || party.id == selectedParty;
                marker.Label.gameObject.SetActive(label);
                if (label) marker.Label.text = party.employer ? party.name + " (your ship)" :
                    party.chasing ? party.name + " — hunting you!" : party.name;
            }
            for (int i = used; i < partyMarkers.Count; i++) partyMarkers[i].Root.gameObject.SetActive(false);
            if (travel.OnJob) player.gameObject.SetActive(false);
        }

        private PartyMarker CreatePartyMarker()
        {
            RectTransform root = BoatshipsUI.Rect("Ship Marker", partyLayer);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(30f, 30f);
            Image icon = MarkerImage("Icon", root, BoatshipsUI.Triangle, Color.white);
            icon.rectTransform.sizeDelta = new Vector2(24f, 30f);
            icon.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
            Text label = BoatshipsUI.Label("", root, 18, TextAnchor.UpperCenter);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.sizeDelta = new Vector2(300f, 26f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1.5f, -1.5f);
            return new PartyMarker { Root = root, Icon = icon, Label = label };
        }

        private void RebuildIslandMarkers(int discoveries, float inverse)
        {
            bool rebuild = discoveries != markerDiscoveries;
            markerDiscoveries = discoveries;
            if (rebuild)
            {
                foreach (RectTransform marker in islandMarkers) if (marker != null) Destroy(marker.gameObject);
                islandMarkers.Clear();
                List<Vector2Int> known = travel.Economy.Campaign.discovered;
                foreach (ArchipelagoWorld.IslandSite site in world.Islands)
                {
                    if (!known.Contains(site.Cell)) continue;
                    RectTransform root = BoatshipsUI.Rect("Island " + site.Cell, islandLayer);
                    root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
                    root.anchoredPosition = WorldToMap(site.Center);
                    root.sizeDelta = new Vector2(20f, 20f);
                    if (site.Kind != IslandKind.Deserted)
                    {
                        Color color = site.Kind == IslandKind.PortSite ? new Color(1f, 0.8f, 0.25f) :
                            site.Kind == IslandKind.PirateBaseSite ? new Color(0.9f, 0.15f, 0.12f) : new Color(1f, 1f, 0.55f);
                        Image icon = MarkerImage("Icon", root, null, color);
                        icon.rectTransform.sizeDelta = new Vector2(16f, 16f);
                        icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                        icon.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
                    }
                    Text label = BoatshipsUI.Label(BoatshipsNames.Island(site), root,
                        site.Kind == IslandKind.PortSite ? 22 : 18, TextAnchor.LowerCenter);
                    label.color = site.Kind == IslandKind.PortSite ? Color.white : new Color(0.9f, 0.92f, 0.85f);
                    label.horizontalOverflow = HorizontalWrapMode.Overflow;
                    label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                    label.rectTransform.pivot = new Vector2(0.5f, 0f);
                    label.rectTransform.sizeDelta = new Vector2(300f, 30f);
                    label.rectTransform.anchoredPosition = new Vector2(0f, 4f);
                    label.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1.5f, -1.5f);
                    islandMarkers.Add(root);
                }
            }
            foreach (RectTransform marker in islandMarkers) marker.localScale = Vector3.one * inverse;
        }

        // ───────────────────────────── Input ─────────────────────────────

        internal void Pan(Vector2 screenDelta)
        {
            if (MenuOpen || ActiveTouches() > 1) return;
            follow = false;
            focus -= screenDelta / Mathf.Max(0.01f, mapCanvas.scaleFactor) / zoom;
        }

        internal void Zoom(float factor, Vector2 screenPoint)
        {
            if (MenuOpen) return;
            float next = Mathf.Clamp(zoom * factor, 0.5f, 7f);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPoint, null, out Vector2 pivot) && !follow)
                focus = pivot - (pivot - focus) * (zoom / next);
            zoom = next;
        }

        private static int ActiveTouches()
        {
            Touchscreen screen = Touchscreen.current;
            if (screen == null) return 0;
            int count = 0;
            foreach (var touch in screen.touches) if (touch.press.isPressed) count++;
            return count;
        }

        private void HandlePinch()
        {
            Touchscreen screen = Touchscreen.current;
            if (screen == null || MenuOpen) { pinchDistance = 0f; return; }
            Vector2 a = Vector2.zero, b = Vector2.zero;
            int count = 0;
            foreach (var touch in screen.touches)
            {
                if (!touch.press.isPressed) continue;
                if (count == 0) a = touch.position.ReadValue(); else if (count == 1) b = touch.position.ReadValue();
                count++;
            }
            if (count < 2) { pinchDistance = 0f; return; }
            float distance = Vector2.Distance(a, b);
            if (pinchDistance > 0f && distance > 1f) Zoom(distance / pinchDistance, (a + b) * 0.5f);
            pinchDistance = distance;
        }

        internal void Tap(Vector2 screenPoint)
        {
            if (MenuOpen || !travel.Live) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPoint, null, out Vector2 local)) return;
            Vector2 point = MapToWorld(local);
            float hit = 34f / zoom / MapUnits * worldSize;
            MapParty bestParty = null;
            float bestDistance = hit;
            foreach (MapParty party in travel.Parties)
            {
                if (!travel.IsVisible(party)) continue;
                float distance = Vector2.Distance(party.position, point);
                if (distance >= bestDistance) continue;
                bestDistance = distance; bestParty = party;
            }
            if (bestParty != null) { SelectParty(bestParty); return; }
            foreach (ArchipelagoWorld.IslandSite site in world.Islands)
            {
                if (Vector2.Distance(site.Center, point) > site.Radius * 0.85f + hit * 0.5f) continue;
                SelectIsland(site);
                return;
            }
            ClearSelection();
            travel.SetCourse(point);
        }

        private void SelectIsland(ArchipelagoWorld.IslandSite site)
        {
            hasSelectedIsland = true; selectedIsland = site; selectedParty = null;
            selection.SetActive(true);
            UpdateSelectionText();
        }

        private void SelectParty(MapParty party)
        {
            hasSelectedIsland = false; selectedParty = party.id;
            selection.SetActive(true);
            UpdateSelectionText();
        }

        private void ClearSelection()
        {
            hasSelectedIsland = false; selectedParty = null;
            if (selection != null) selection.SetActive(false);
        }

        private void UpdateSelectionText()
        {
            if (selection == null || !selection.activeSelf || !travel.Live) return;
            if (hasSelectedIsland)
            {
                ArchipelagoWorld.IslandSite site = selectedIsland;
                bool known = travel.Economy.Campaign.discovered.Contains(site.Cell);
                selectionTitle.text = known ? BoatshipsNames.Island(site) : "Uncharted island";
                string kind = !known ? "Nobody has charted this island yet." :
                    site.Kind == IslandKind.PortSite ? BoatshipsFactions.Name(BoatshipsFactions.OfPort(site.Cell)) + " port" :
                    site.Kind == IslandKind.PirateBaseSite ? "Pirate haven" :
                    site.Kind == IslandKind.TreasureSite ? "Rumoured treasure" : "Deserted " + site.Biome.ToString().ToLowerInvariant() + " " + ProceduralIslandChunk.ShapeName(site);
                float distance = Vector2.Distance(travel.Position, site.Center);
                selectionBody.text = kind + "\nDistance " + distance.ToString("0") + "   •   about " +
                    travel.PlayerTravelDays(site.Center).ToString("0.0") + " days' sailing";
                bool here = travel.CanEnter(site);
                BoatshipsUI.SetButton(selectionAction, here ? "Enter" : "Set sail", here || travel.CanCommand);
                return;
            }
            MapParty selected = null;
            foreach (MapParty party in travel.Parties) if (party.id == selectedParty) selected = party;
            if (selected == null || !travel.IsVisible(selected)) { ClearSelection(); return; }
            selectionTitle.text = selected.name;
            selectionBody.text = BoatshipsTravelController.FactionName(selected.faction) + " — " +
                BoatshipsTravelController.Describe(selected) + "\nTier " + selected.tier + "   •   Distance " +
                Vector2.Distance(travel.Position, selected.position).ToString("0") +
                (selected.employer ? "\nYou are aboard this ship." : selected.chasing ? "\nShe is hunting you!" : "");
            BoatshipsUI.SetButton(selectionAction, "Intercept", travel.CanCommand && !selected.employer);
        }

        private void SelectionAction()
        {
            if (hasSelectedIsland)
            {
                ArchipelagoWorld.IslandSite site = selectedIsland;
                ClearSelection();
                if (travel.CanEnter(site)) travel.OpenCurrentIslandMenu();
                else travel.SetCourse(site);
                return;
            }
            foreach (MapParty party in travel.Parties)
                if (party.id == selectedParty) { travel.Intercept(party); break; }
            ClearSelection();
        }

        private void OnDestroy() { if (terrainTexture != null) Destroy(terrainTexture); }
    }

    /// <summary>Receives drag, scroll and tap events for the world map.</summary>
    public sealed class MapInputSurface : MonoBehaviour, IDragHandler, IScrollHandler, IPointerClickHandler
    {
        internal BoatshipsWorldMapView View;

        public void OnDrag(PointerEventData eventData) => View?.Pan(eventData.delta);

        public void OnScroll(PointerEventData eventData)
        {
            float amount = eventData.scrollDelta.y;
            if (Mathf.Abs(amount) > 0.01f) View?.Zoom(amount > 0f ? 1.15f : 1f / 1.15f, eventData.position);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging || eventData.button != PointerEventData.InputButton.Left) return;
            View?.Tap(eventData.position);
        }
    }
}
