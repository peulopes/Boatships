using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Touch collection for all floating loot, with range guidance and pickup confirmation.</summary>
    public sealed class BattleLootHUD : MonoBehaviour
    {
        [SerializeField] private BattleLootSystem loot;
        [SerializeField] private PortDocking docking;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private Text status;
        [SerializeField] private Button collect;
        [SerializeField] private Button jump;
        private float refreshAt;
        private GameObject panel;
        private Text collectCaption;

        public void Configure(BattleLootSystem lootSystem, PortDocking port,
            PortEconomy wallet, Text label, Button collectButton, Button jumpButton)
        {
            loot = lootSystem; docking = port; economy = wallet;
            status = label; collect = collectButton; jump = jumpButton;
        }

        private void Start()
        {
            BoatshipsDebugMenu.Register(this, jump);
            if (collect != null)
            {
                collect.onClick.AddListener(Collect);
                collectCaption = collect.GetComponentInChildren<Text>(true);
                RectTransform rect = collect.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.15f, 0.04f);
                rect.anchorMax = new Vector2(0.85f, 0.34f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            if (jump != null && loot != null) jump.onClick.AddListener(loot.JumpToEncounterForTesting);
            // Keep this gameplay action above the lower control row, clear of the joysticks.
            RectTransform root = GetComponent<RectTransform>();
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.anchoredPosition = new Vector2(0f, 180f);
            root.sizeDelta = new Vector2(560f, 165f);
            if (status != null)
            {
                panel = status.transform.parent.gameObject;
                status.rectTransform.anchorMin = new Vector2(0.03f, 0.38f);
                status.rectTransform.anchorMax = new Vector2(0.97f, 0.98f);
                status.rectTransform.offsetMin = status.rectTransform.offsetMax = Vector2.zero;
                status.raycastTarget = false;
            }
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + 0.1f;
            Refresh();
        }

        private void Collect()
        {
            if (loot == null) return;
            loot.CollectNearby();
            Refresh();
        }

        private void Refresh()
        {
            if (loot == null || economy == null) return;
            bool docked = docking != null && docking.IsDocked;
            bool visible = !docked && (loot.HasLoot || loot.ShowCollectionFeedback);
            if (panel != null) panel.SetActive(visible);
            if (collect != null)
            {
                collect.interactable = !docked && loot.CanCollect;
                if (collectCaption != null)
                    collectCaption.text = loot.CanCollect ? "Collect Loot" : loot.HasLoot ?
                        "Sail within " + loot.CollectRadius.ToString("0") + " m" : "Loot Collected";
            }
            if (jump != null) jump.gameObject.SetActive(!docked);
            if (status == null || !visible) return;
            status.enabled = true;
            status.text = loot.ShowCollectionFeedback ? loot.LastEvent + "\nGold " + economy.Gold + " | " + economy.StockSummary :
                "FLOATING LOOT — " + loot.DistanceToLoot.ToString("0") + " m away\n" + loot.LootDescription +
                "\n" + (loot.CanCollect ? "In range — tap Collect Loot." : "Sail closer to collect (within " + loot.CollectRadius.ToString("0") + " m).");
        }

        private void OnDestroy()
        {
            if (collect != null) collect.onClick.RemoveListener(Collect);
            if (jump != null && loot != null) jump.onClick.RemoveListener(loot.JumpToEncounterForTesting);
        }
    }
}
