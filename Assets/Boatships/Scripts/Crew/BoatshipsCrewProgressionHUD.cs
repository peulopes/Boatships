using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Basic mobile status/actions for M13 crew experience, injury and recovery.</summary>
    [DisallowMultipleComponent]
    public sealed class BoatshipsCrewProgressionHUD : MonoBehaviour
    {
        [SerializeField] private BoatshipsCrewProgression progression;
        [SerializeField] private Text summary;
        [SerializeField] private Button treat;
        [SerializeField] private Button recover;
        private float nextRefresh;

        public void Configure(BoatshipsCrewProgression source, Text text, Button treatButton, Button rescueButton)
        {
            progression = source;
            summary = text;
            treat = treatButton;
            recover = rescueButton;
        }

        private void Start()
        {
            if (treat != null) treat.onClick.AddListener(Treat);
            if (recover != null) recover.onClick.AddListener(Recover);
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.3f;
            Refresh();
        }

        private void Treat() { if (progression != null) progression.TreatSelectedAtPort(); Refresh(); }
        private void Recover() { if (progression != null) progression.RecoverOverboardAtPort(); Refresh(); }
        private void Refresh()
        {
            if (progression == null || summary == null || !summary.isActiveAndEnabled) return;
            summary.text = "CREW PROGRESSION (M13)\n" + progression.SelectedCrewStatus +
                "\nReload x" + progression.GunReloadMultiplier.ToString("0.00") +
                " | Speed x" + progression.SailingSpeedMultiplier.ToString("0.00") +
                "\n" + progression.Status + "\nF8 Injury test  F10 +40 XP";
        }
    }
}
