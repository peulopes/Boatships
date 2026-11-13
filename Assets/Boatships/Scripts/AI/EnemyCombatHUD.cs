using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Additional readout for the enemy AI scene; no changes to Milestone 4 HUD.</summary>
    public sealed class EnemyCombatHUD : MonoBehaviour
    {
        [SerializeField] private EnemyShipAI enemyAI;
        [SerializeField] private ShipCombatTarget player;
        [SerializeField] private ShipCombatTarget enemy;
        [SerializeField] private ShipCustomization shipyard;
        [SerializeField] private Text label;
        private float nextRefresh;

        public void Configure(EnemyShipAI ai, ShipCombatTarget playerShip,
            ShipCombatTarget enemyShip, ShipCustomization customization, Text statusLabel)
        {
            enemyAI = ai;
            player = playerShip;
            enemy = enemyShip;
            shipyard = customization;
            label = statusLabel;
        }

        private void Update()
        {
            if (label == null || Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.15f;
            bool sailing = shipyard == null || !shipyard.InShipyard;
            label.text = sailing
                ? "PIRATE: " + (enemyAI != null ? enemyAI.State.ToString() : "Unknown") +
                  "  Hull: " + HullPercent(enemy) + "%\n" +
                  "YOUR HULL: " + HullPercent(player) + "%\n" +
                  "Fire at red ship. Reset Target restores its voxels."
                : "Finish / Sail to start enemy combat.";
        }

        private static int HullPercent(ShipCombatTarget target) =>
            target == null || target.TotalVoxels == 0 ? 0 :
            Mathf.RoundToInt(100f * target.RemainingVoxels / target.TotalVoxels);
    }
}
