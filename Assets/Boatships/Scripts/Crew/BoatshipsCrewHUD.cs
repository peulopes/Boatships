using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Compact touch HUD; F6 selects a crew member, F7 changes duty in the Editor.</summary>
    [DisallowMultipleComponent]
    public sealed class BoatshipsCrewHUD : MonoBehaviour
    {
        [SerializeField] private BoatshipsCrewManager manager;
        [SerializeField] private Text crewText;
        [SerializeField] private Button nextCrewButton;
        [SerializeField] private Button dutyButton;
        private float nextRefresh;

        public void Configure(BoatshipsCrewManager source, Text label, Button nextButton, Button changeDutyButton)
        {
            manager = source;
            crewText = label;
            nextCrewButton = nextButton;
            dutyButton = changeDutyButton;
        }

        private void Start()
        {
            if (nextCrewButton != null) nextCrewButton.onClick.AddListener(SelectNext);
            if (dutyButton != null) dutyButton.onClick.AddListener(ChangeDuty);
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        private void SelectNext()
        {
            if (manager != null) manager.SelectNextCrew();
            Refresh();
        }

        private void ChangeDuty()
        {
            if (manager != null) manager.CycleSelectedDuty();
            Refresh();
        }

        private void Refresh()
        {
            if (manager == null || crewText == null) return;
            crewText.text = "CREW  " + manager.ActiveCrewCount + " visible / " +
                manager.TotalCrewCount + " recruited\n" + manager.SelectedCrewText +
                "\n" + manager.Status;
            bool available = manager.ActiveCrewCount > 0;
            if (nextCrewButton != null) nextCrewButton.interactable = available;
            if (dutyButton != null) dutyButton.interactable = available;
        }
    }
}
