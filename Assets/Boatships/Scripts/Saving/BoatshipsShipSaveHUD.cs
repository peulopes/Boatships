using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>UI only. Does not load saves mid-session to avoid inventory duplication.</summary>
    public sealed class BoatshipsShipSaveHUD : MonoBehaviour
    {
        [SerializeField] private BoatshipsShipSaveManager saveManager;
        [SerializeField] private Text statusText;
        [SerializeField] private Button saveButton;
        private float nextRefresh;

        public void Configure(BoatshipsShipSaveManager manager, Text status, Button button)
        {
            saveManager = manager;
            statusText = status;
            saveButton = button;
            if (saveButton != null) saveButton.onClick.AddListener(Save);
        }

        public void SetManager(BoatshipsShipSaveManager manager)
        {
            saveManager = manager;
        }

        private void Start()
        {
            Transform oldPanel = statusText != null ? statusText.transform.parent : null;
            BoatshipsDebugMenu.Register(this, statusText, saveButton);
            if (oldPanel != null && oldPanel.childCount == 0) oldPanel.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (statusText == null || saveManager == null || !statusText.isActiveAndEnabled || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            statusText.text = "SHIP SAVE (M11) — " + saveManager.InstalledPartCount +
                " parts currently installed\n" + saveManager.Status + "\nF4: Save | File path in Console";
        }

        private void Save()
        {
            if (saveManager != null) saveManager.SaveShip();
        }
    }
}
