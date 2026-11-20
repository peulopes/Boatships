using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>PC sailing HUD for the campaign, in the spirit of Bannerlord's battle HUD:
    /// a ship status card bottom-left (hull, flooding, crew) and a crew drawer toggled with P
    /// that collects the always-on crew panels the milestone scenes left on screen.
    /// Installed at scene load by <see cref="BoatshipsVoyageBootstrap"/>.</summary>
    [DefaultExecutionOrder(960)]
    public sealed class BoatshipsSailingHud : MonoBehaviour
    {
        private BoatshipsMobilePresentation presentation;
        private BoatshipsCampaignHUD hud;
        private BoatshipsWorldMapView view;
        private BoatshipsTravelController travel;
        private PortDocking docking;
        private PortEconomy economy;
        private WeaponGroupCombat weapons;
        private ShipCombatTarget flagship;
        private ShipFlooding flooding;
        private BoatshipsCrewManager crew;

        private GameObject statusCard;
        private RectTransform hullFill;
        private RectTransform floodFill;
        private Image floodImage;
        private Text hullText;
        private Text floodText;
        private Text crewText;
        private GameObject drawer;
        private bool crewOpen;
        private float nextRefresh;

        public void Configure(BoatshipsMobilePresentation mobile, BoatshipsCampaignHUD campaign, BoatshipsWorldMapView mapView,
            BoatshipsTravelController controller, PortDocking port, PortEconomy account, GameObject ship)
        {
            presentation = mobile; hud = campaign; view = mapView; travel = controller; docking = port; economy = account;
            weapons = ship.GetComponent<WeaponGroupCombat>();
            flagship = ship.GetComponent<ShipCombatTarget>();
            flooding = ship.GetComponent<ShipFlooding>();
            crew = ship.GetComponent<BoatshipsCrewManager>();
        }

        public void ToggleCrew() { if (drawer != null) crewOpen = !crewOpen; }

        private IEnumerator Start()
        {
            // The presentation moves the scene UI into its safe-area root a few frames after load.
            while (presentation == null || presentation.SafeRoot == null) yield return null;
            yield return null;
            RectTransform root = presentation.SafeRoot;
            BuildStatusCard(root);
            BuildCrewDrawer(root);
        }

        // ───────────────────────────── Construction ─────────────────────────────

        private void BuildStatusCard(RectTransform root)
        {
            Image card = BoatshipsUI.FramedPanel("Ship Status", root, Vector2.zero, new Vector2(16f, 16f), new Vector2(330f, 126f));
            card.raycastTarget = false;
            statusCard = card.gameObject;
            Text title = BoatshipsUI.Label("FLAGSHIP", card.transform, 17);
            title.color = BoatshipsUI.Title;
            BoatshipsUI.Anchor(title.rectTransform, 0.05f, 0.74f, 0.95f, 0.96f);
            hullFill = Bar(card.transform, "Hull", 0.5f, new Color(0.45f, 0.62f, 0.3f), out hullText, out _);
            floodFill = Bar(card.transform, "Water", 0.27f, new Color(0.25f, 0.45f, 0.7f), out floodText, out floodImage);
            crewText = BoatshipsUI.Label("", card.transform, 16);
            crewText.color = BoatshipsUI.Muted;
            BoatshipsUI.Anchor(crewText.rectTransform, 0.05f, 0.03f, 0.95f, 0.24f);
            statusCard.SetActive(false);
        }

        private static RectTransform Bar(Transform parent, string name, float y, Color color, out Text label, out Image fillImage)
        {
            Text caption = BoatshipsUI.Label(name, parent, 15);
            caption.color = BoatshipsUI.Muted;
            BoatshipsUI.Anchor(caption.rectTransform, 0.05f, y, 0.25f, y + 0.2f);
            Image back = BoatshipsUI.Image(name + " Bar", parent, new Color(0f, 0f, 0f, 0.55f));
            back.raycastTarget = false;
            BoatshipsUI.Anchor(back.rectTransform, 0.26f, y + 0.03f, 0.95f, y + 0.17f);
            fillImage = BoatshipsUI.Image("Fill", back.transform, color);
            fillImage.raycastTarget = false;
            BoatshipsUI.Stretch(fillImage.rectTransform);
            label = BoatshipsUI.Label("", back.transform, 13, TextAnchor.MiddleCenter);
            BoatshipsUI.Stretch(label.rectTransform);
            label.gameObject.AddComponent<Shadow>();
            return fillImage.rectTransform;
        }

        private void BuildCrewDrawer(RectTransform root)
        {
            BoatshipsCrewHUD roster = FindAnyObjectByType<BoatshipsCrewHUD>(FindObjectsInactive.Include);
            BoatshipsCrewProgressionHUD progression = FindAnyObjectByType<BoatshipsCrewProgressionHUD>(FindObjectsInactive.Include);
            if (roster == null && progression == null) return;
            Image panel = BoatshipsUI.FramedPanel("Crew Drawer", root, Vector2.one, new Vector2(-16f, -80f), new Vector2(450f, 100f));
            drawer = panel.gameObject;
            VerticalLayoutGroup column = BoatshipsUI.Column(panel.rectTransform, 8f, 10);
            column.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Text title = BoatshipsUI.FixedLabel("CREW  —  P to close", panel.transform, 20, 30f);
            title.color = BoatshipsUI.Title;
            Adopt(roster, panel.transform, 235f);
            Adopt(progression, panel.transform, 224f);
            drawer.SetActive(false);
        }

        /// <summary>Moves a scene-built crew panel into the drawer and reskins it.</summary>
        private static void Adopt(Component owner, Transform drawer, float height)
        {
            if (owner == null) return;
            RectTransform rect = (RectTransform)owner.transform;
            rect.SetParent(drawer, false);
            rect.localScale = Vector3.one;
            LayoutElement layout = owner.GetComponent<LayoutElement>();
            if (layout == null) layout = owner.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = layout.minHeight = height;
            Image image = owner.GetComponent<Image>();
            if (image != null) image.color = BoatshipsUI.PanelLight;
            foreach (Button button in owner.GetComponentsInChildren<Button>(true)) BoatshipsUI.StyleButton(button);
            foreach (Text text in owner.GetComponentsInChildren<Text>(true))
            {
                if (text.color == Color.white) text.color = BoatshipsUI.TextColor;
                text.fontSize = Mathf.Min(text.fontSize, 18);
            }
            owner.gameObject.SetActive(true);
        }

        // ───────────────────────────── Frame ─────────────────────────────

        private bool Sailing =>
            travel != null && travel.Live && travel.Mode != TravelMode.Map && !travel.FirstPersonActive &&
            (hud == null || !hud.BlockingOverlayOpen) && (docking == null || !docking.IsDocked) &&
            weapons != null && weapons.isActiveAndEnabled && weapons.CombatEnabled;

        private void Update()
        {
            ReleaseKeyboardFocus();
            if (statusCard == null) return;
            bool sailing = Sailing;
            Keyboard keyboard = Keyboard.current;
            if (sailing && keyboard != null && keyboard.pKey.wasPressedThisFrame && !BoatshipsUI.Typing &&
                !BoatshipsDebugMenu.IsOpen && (view == null || !view.MenuOpen))
                ToggleCrew();
            if (statusCard.activeSelf != sailing) statusCard.SetActive(sailing);
            bool showCrew = sailing && crewOpen;
            if (drawer != null && drawer.activeSelf != showCrew) drawer.SetActive(showCrew);
            if (!sailing || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        private void Refresh()
        {
            float hull = flagship != null && flagship.TotalVoxels > 0 ? (float)flagship.RemainingVoxels / flagship.TotalVoxels : 1f;
            SetFill(hullFill, hull);
            hullText.text = Mathf.RoundToInt(hull * 100f) + "%";
            float water = flooding != null ? flooding.FloodFraction : 0f;
            bool sinking = flooding != null && flooding.IsSinking;
            SetFill(floodFill, water);
            floodImage.color = sinking ? BoatshipsUI.Danger : new Color(0.25f, 0.45f, 0.7f);
            floodText.text = sinking ? "SINKING" : Mathf.RoundToInt(water * 100f) + "%" +
                (flooding != null && flooding.SubmergedUnpatchedBreaches > 0 ? "  —  " + flooding.SubmergedUnpatchedBreaches + " leaks" : "");
            crewText.text = (crew != null ? "Crew " + crew.ActiveCrewCount + " aboard / " + crew.TotalCrewCount : "Crew —") +
                (economy != null ? "     Gold " + economy.Gold : "");
        }

        private static void SetFill(RectTransform fill, float amount)
        {
            fill.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
            fill.offsetMax = Vector2.zero;
        }

        /// <summary>uGUI keeps the last clicked button/slider selected, so Space (fire) or the arrow keys (aim)
        /// would also press it or move it. Gameplay keys belong to the game, so drop that focus.</summary>
        private static void ReleaseKeyboardFocus()
        {
            EventSystem events = EventSystem.current;
            if (events == null || events.currentSelectedGameObject == null) return;
            if (events.currentSelectedGameObject.GetComponent<InputField>() != null) return;
            events.SetSelectedGameObject(null);
        }
    }
}
