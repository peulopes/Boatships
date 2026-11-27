using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Bottom-centre weapon bar (Bannerlord-style): four group slots with hotkeys, aim readout above.
    /// Scene-built uGUI controls are re-laid out at runtime; the touch Fire button and aim stick are hidden.</summary>
    public sealed class WeaponCombatHUD : MonoBehaviour
    {
        [SerializeField] private WeaponGroupCombat weapons;
        [SerializeField] private ShipCombatTarget practiceTarget;
        [SerializeField] private Button[] groupButtons;
        [SerializeField] private Button fireButton;
        [SerializeField] private Button nextCannonButton;
        [SerializeField] private Button assignButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Text statusText;
        [SerializeField] private GameObject combatPanel;
        [SerializeField] private GameObject aimJoystick;

        private PortDocking docking;
        private Text[] groupLabels;
        private int[] groupCounts;
        private int shownBearing = int.MinValue, shownElevation = int.MinValue;
        private string shownCannon, shownStatus;
        private static readonly string[] GroupNames = { "Port", "Starboard", "Bow", "Stern" };
        private const float BarWidth = 600f, SideButton = 150f, Gap = 8f, BarHeight = 52f, Margin = 16f;

        public void Configure(WeaponGroupCombat controller, ShipCombatTarget target, Button[] groups,
            Button fire, Button nextCannon, Button assign, Button reset,
            Text status, GameObject panel, GameObject joystick)
        {
            weapons = controller;
            practiceTarget = target;
            groupButtons = groups;
            fireButton = fire;
            nextCannonButton = nextCannon;
            assignButton = assign;
            resetButton = reset;
            statusText = status;
            combatPanel = panel;
            aimJoystick = joystick;
        }

        private void Start()
        {
            BoatshipsDebugMenu.Register(this, resetButton);
            if (aimJoystick != null) aimJoystick.SetActive(false);
            if (fireButton != null) fireButton.gameObject.SetActive(false); // Hold Space to fire.
            ApplyLayout();
            docking = FindAnyObjectByType<PortDocking>();
            if (weapons == null) return;
            for (int i = 0; i < groupButtons.Length; ++i)
            {
                int requested = i + 1;
                if (groupButtons[i] != null) groupButtons[i].onClick.AddListener(() => weapons.SelectGroup(requested));
            }
            if (fireButton != null) fireButton.onClick.AddListener(weapons.FireSelectedGroup);
            if (nextCannonButton != null) nextCannonButton.onClick.AddListener(weapons.CycleCannon);
            if (assignButton != null) assignButton.onClick.AddListener(weapons.AssignSelectedCannonToActiveGroup);
            if (resetButton != null && practiceTarget != null) resetButton.onClick.AddListener(practiceTarget.ResetPracticeTarget);
        }

        private void ApplyLayout()
        {
            float total = BarWidth + (SideButton + Gap) * 2f;
            float left = -total * 0.5f;
            Vector2 bottom = new Vector2(0.5f, 0f);
            Transform row = null;
            foreach (Button group in groupButtons)
                if (group != null) { row = group.transform.parent; BoatshipsUI.StyleButton(group); }
            if (row is RectTransform rowRect)
            {
                rowRect.pivot = Vector2.zero;
                rowRect.anchorMin = rowRect.anchorMax = bottom;
                rowRect.sizeDelta = new Vector2(BarWidth, BarHeight);
                rowRect.anchoredPosition = new Vector2(left, Margin);
            }
            PlaceSide(nextCannonButton, "Next cannon  [Tab]", left + BarWidth + Gap);
            PlaceSide(assignButton, "Assign  [G]", left + BarWidth + Gap * 2f + SideButton);
            if (statusText != null)
            {
                Image backing = BoatshipsUI.FramedPanel("Aim Readout", statusText.transform.parent, bottom,
                    new Vector2(0f, Margin + BarHeight + 6f), new Vector2(total, 74f));
                backing.raycastTarget = false;
                backing.transform.SetSiblingIndex(statusText.transform.GetSiblingIndex());
                BoatshipsUI.Place(statusText.rectTransform, bottom, new Vector2(0f, Margin + BarHeight + 8f), new Vector2(total - 24f, 70f));
                statusText.alignment = TextAnchor.MiddleCenter;
                statusText.fontSize = 18;
                statusText.color = BoatshipsUI.TextColor;
                statusText.supportRichText = true;
                statusText.raycastTarget = false;
                statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
                statusText.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        private static void PlaceSide(Button button, string caption, float x)
        {
            if (button == null) return;
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(SideButton, BarHeight);
            rect.anchoredPosition = new Vector2(x, Margin);
            BoatshipsUI.StyleButton(button);
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null) { label.text = caption; label.fontSize = 16; }
        }

        private void Update()
        {
            if (weapons == null) return;
            bool active = weapons.CombatEnabled && weapons.isActiveAndEnabled && (docking == null || !docking.IsDocked);
            if (combatPanel != null && combatPanel.activeSelf != active) combatPanel.SetActive(active);
            if (!active) return;
            // Campaign: the gun crews aim and fire; the captain only steers, or leaves the helm to command a gun.
            bool crew = weapons.CrewGunnery;
            foreach (Button group in groupButtons) if (group != null && group.gameObject.activeSelf == crew) group.gameObject.SetActive(!crew);
            if (nextCannonButton != null && nextCannonButton.gameObject.activeSelf == crew) nextCannonButton.gameObject.SetActive(!crew);
            if (assignButton != null && assignButton.gameObject.activeSelf == crew) assignButton.gameObject.SetActive(!crew);
            if (crew)
            {
                if (statusText != null)
                    statusText.text = weapons.CrewStatus +
                        "\n<color=#B8A27A>WASD steer  ·  [Q] leave the helm: walk the deck, take command of a gun, board the enemy</color>";
                return;
            }
            if (groupLabels == null || groupLabels.Length != groupButtons.Length)
            {
                // Looked up once (was four GetComponentInChildren calls a frame).
                groupLabels = new Text[groupButtons.Length];
                groupCounts = new int[groupButtons.Length];
                for (int i = 0; i < groupButtons.Length; ++i)
                {
                    groupCounts[i] = -1;
                    groupLabels[i] = groupButtons[i] != null ? groupButtons[i].GetComponentInChildren<Text>() : null;
                    if (groupLabels[i] == null) continue;
                    groupLabels[i].supportRichText = true;
                    groupLabels[i].fontSize = 17;
                }
            }
            for (int i = 0; i < groupButtons.Length; ++i)
            {
                if (groupButtons[i] == null) continue;
                bool selected = i == weapons.ActiveGroup - 1;
                if (groupButtons[i].targetGraphic != null)
                    groupButtons[i].targetGraphic.color = selected ? BoatshipsUI.Selected : BoatshipsUI.ButtonColor;
                int count = weapons.CountInGroup(i + 1);
                if (groupLabels[i] == null || groupCounts[i] == count) continue;
                groupCounts[i] = count;
                groupLabels[i].text = "<color=#F2D99E>[" + (i + 1) + "]</color> " +
                    (i < GroupNames.Length ? GroupNames[i] : "Group " + (i + 1)) + "  ×" + count;
            }
            if (statusText == null) return;
            // Rebuild the readout only when something it shows has changed.
            int bearing = Mathf.RoundToInt(weapons.Bearing), elevation = Mathf.RoundToInt(weapons.Elevation);
            string cannon = weapons.SelectedCannonDescription, status = weapons.Status;
            if (bearing == shownBearing && elevation == shownElevation && cannon == shownCannon && status == shownStatus) return;
            shownBearing = bearing; shownElevation = elevation; shownCannon = cannon; shownStatus = status;
            statusText.text = "Bearing " + bearing + "°   Elevation " + elevation + "°   Cannon " + cannon + "\n" +
                status + "\n<color=#B8A27A>Hold Space to fire  ·  ← → bearing  ·  ↑ ↓ elevation</color>";
        }
    }
}
