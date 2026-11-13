using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Wires prototype uGUI actions to the weapon manager; hides combat controls in shipyard.</summary>
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

        private void Update()
        {
            if (weapons == null) return;
            bool active = weapons.CombatEnabled;
            if (combatPanel != null && combatPanel.activeSelf != active) combatPanel.SetActive(active);
            if (aimJoystick != null && aimJoystick.activeSelf != active) aimJoystick.SetActive(active);
            if (!active) return;
            for (int i = 0; i < groupButtons.Length; ++i)
            {
                if (groupButtons[i] == null) continue;
                Text label = groupButtons[i].GetComponentInChildren<Text>();
                if (label != null)
                    label.text = (i == weapons.ActiveGroup - 1 ? "[" : "") +
                        "G" + (i + 1) + ": " + weapons.CountInGroup(i + 1) +
                        (i == weapons.ActiveGroup - 1 ? "]" : "");
            }
            if (statusText != null)
                statusText.text = "Group " + weapons.ActiveGroup +
                    " | Bearing " + weapons.Bearing.ToString("0") + "°" +
                    " | Elevation " + weapons.Elevation.ToString("0") + "°\n" +
                    "Selected cannon: " + weapons.SelectedCannonDescription +
                    " | Target voxels: " + (practiceTarget != null ? practiceTarget.RemainingVoxels : 0) +
                    "/" + (practiceTarget != null ? practiceTarget.TotalVoxels : 0) + "\n" +
                    weapons.Status;
        }
    }
}
