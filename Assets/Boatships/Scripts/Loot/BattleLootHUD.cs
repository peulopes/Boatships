using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Floating-loot card (right edge) with range guidance, pickup confirmation and the F hotkey.</summary>
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
                rect.anchorMin = new Vector2(0.05f, 0.07f);
                rect.anchorMax = new Vector2(0.95f, 0.36f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                BoatshipsUI.StyleButton(collect, BoatshipsUI.Accent);
                if (collectCaption != null) { collectCaption.color = BoatshipsUI.Ink; collectCaption.fontSize = 19; }
            }
            if (jump != null && loot != null) jump.onClick.AddListener(loot.JumpToEncounterForTesting);
            // Right edge, above the bottom HUD row and below the crew drawer.
            BoatshipsUI.Place(GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-16f, 200f), new Vector2(440f, 160f));
            if (status != null)
            {
                panel = status.transform.parent.gameObject;
                Image backing = panel.GetComponent<Image>();
                if (backing != null) { backing.color = BoatshipsUI.Panel; BoatshipsUI.Frame(panel, 2f); }
                status.fontSize = 18;
                status.color = BoatshipsUI.TextColor;
                status.rectTransform.anchorMin = new Vector2(0.04f, 0.4f);
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
                    collectCaption.text = loot.CanCollect ? "Collect loot  [F]" : loot.HasLoot ?
                        "Sail within " + loot.CollectRadius.ToString("0") + " m" : "Loot Collected";
            }
            if (jump != null) jump.gameObject.SetActive(!docked);
            if (status == null || !visible) return;
            status.enabled = true;
            status.text = loot.ShowCollectionFeedback ? loot.LastEvent + "\nGold " + economy.Gold + " | " + economy.StockSummary :
                "FLOATING LOOT — " + loot.DistanceToLoot.ToString("0") + " m away\n" + loot.LootDescription +
                "\n" + (loot.CanCollect ? "In range — press F to collect." : "Sail closer to collect (within " + loot.CollectRadius.ToString("0") + " m).");
        }

        private void OnDestroy()
        {
            if (collect != null) collect.onClick.RemoveListener(Collect);
            if (jump != null && loot != null) jump.onClick.RemoveListener(loot.JumpToEncounterForTesting);
        }
    }
}
