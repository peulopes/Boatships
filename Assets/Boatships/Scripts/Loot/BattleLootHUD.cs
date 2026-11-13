using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Small low-overhead debug HUD. Existing M8 port UI remains unchanged.</summary>
    public sealed class BattleLootHUD : MonoBehaviour
    {
        [SerializeField] private BattleLootSystem loot;
        [SerializeField] private PortDocking docking;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private Text status;
        [SerializeField] private Button collect;
        [SerializeField] private Button jump;
        private float refreshAt;

        public void Configure(BattleLootSystem lootSystem, PortDocking port,
            PortEconomy wallet, Text label, Button collectButton, Button jumpButton)
        {
            loot = lootSystem; docking = port; economy = wallet;
            status = label; collect = collectButton; jump = jumpButton;
        }

        private void Start()
        {
            if (collect != null) collect.onClick.AddListener(loot.CollectNearby);
            if (jump != null) jump.onClick.AddListener(loot.JumpToEncounterForTesting);
        }

        private void Update()
        {
            if (loot == null || economy == null || Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + 0.2f;
            bool docked = docking != null && docking.IsDocked;
            if (collect != null)
            {
                collect.gameObject.SetActive(!docked);
                collect.interactable = loot.CanCollect;
            }
            if (jump != null) jump.gameObject.SetActive(!docked);
            if (status != null)
            {
                status.enabled = !docked;
                if (docked) return;
                status.text = "SALVAGE | " + economy.StockSummary + " | Gold " + economy.Gold + "\n" +
                    loot.LastEvent + "\n" + (loot.HasLoot ?
                    "Loot " + loot.DistanceToLoot.ToString("0") + "m away: " + loot.LootDescription :
                    "Fight the pirate; recover its loot after it sinks.");
            }
        }
    }
}
