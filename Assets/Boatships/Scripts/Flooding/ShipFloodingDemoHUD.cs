using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Simple demo-only HUD and test buttons (desktop + mobile).</summary>
    public sealed class ShipFloodingDemoHUD : MonoBehaviour
    {
        [SerializeField] private ShipFlooding player;
        [SerializeField] private ShipFlooding pirate;
        [SerializeField] private ShipCombatTarget playerTarget;
        [SerializeField] private ShipCombatTarget pirateTarget;
        [SerializeField] private ShipCustomization shipyard;
        [SerializeField] private Text readout;
        [SerializeField] private Button breachPirateButton;
        [SerializeField] private Button breachPlayerButton;
        [SerializeField] private Button repairPlayerButton;
        [SerializeField] private Button resetButton;
        private int playerHoleIndex;
        private int pirateHoleIndex;
        private float nextReadout;

        public void Configure(ShipFlooding playerFlood, ShipFlooding enemyFlood,
            ShipCombatTarget playerShip, ShipCombatTarget enemyShip, ShipCustomization customization,
            Text label, Button enemyBreach, Button playerBreach, Button repair, Button reset)
        {
            player = playerFlood;
            pirate = enemyFlood;
            playerTarget = playerShip;
            pirateTarget = enemyShip;
            shipyard = customization;
            readout = label;
            breachPirateButton = enemyBreach;
            breachPlayerButton = playerBreach;
            repairPlayerButton = repair;
            resetButton = reset;
        }

        private void Start()
        {
            if (breachPirateButton != null) breachPirateButton.onClick.AddListener(BreachPirate);
            if (breachPlayerButton != null) breachPlayerButton.onClick.AddListener(BreachPlayer);
            if (repairPlayerButton != null) repairPlayerButton.onClick.AddListener(PatchPlayer);
            if (resetButton != null) resetButton.onClick.AddListener(ResetShips);
        }

        private void Update()
        {
            bool sailing = shipyard == null || !shipyard.InShipyard;
            if (readout != null && Time.time >= nextReadout)
            {
                nextReadout = Time.time + 0.12f;
                readout.text = sailing
                    ? "FLOODING | You: " + Summary(player) + "    Pirate: " + Summary(pirate) +
                      "\nB = hole pirate  N = hole player  P = patch your hull  R = reset both"
                    : "FLOODING | Finish / Sail to start flooding tests.";
            }
            if (!sailing) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.bKey.wasPressedThisFrame) BreachPirate();
            if (kb.nKey.wasPressedThisFrame) BreachPlayer();
            if (kb.pKey.wasPressedThisFrame) PatchPlayer();
            if (kb.rKey.wasPressedThisFrame) ResetShips();
        }

        private static string Summary(ShipFlooding flooding)
        {
            if (flooding == null) return "missing";
            return Mathf.RoundToInt(flooding.FloodFraction * 100f) + "%" +
                (flooding.IsSinking ? " SINKING" : "") +
                " (leaks " + flooding.SubmergedUnpatchedBreaches +
                ", kits " + flooding.RemainingRepairKits + ")";
        }

        public void BreachPirate()
        {
            if (shipyard != null && shipyard.InShipyard) return;
            DamageExposedSide(pirateTarget, ref pirateHoleIndex);
        }

        public void BreachPlayer()
        {
            if (shipyard != null && shipyard.InShipyard) return;
            DamageExposedSide(playerTarget, ref playerHoleIndex);
        }

        private static void DamageExposedSide(ShipCombatTarget target, ref int counter)
        {
            if (target == null || target.Hull == null || target.Hull.Definition == null) return;
            VoxelHull hull = target.Hull;
            HullDefinition def = hull.Definition;
            // Spread test shots along the STARBOARD waterline to demonstrate compartments.
            int z = Mathf.Clamp(Mathf.RoundToInt((counter % 9 + 0.5f) * (def.Length - 1) / 9f), 1, def.Length - 2);
            int y = 0;
            counter++;
            int exteriorX = -1;
            for (int x = def.Width - 1; x >= 0; --x)
            {
                if (def.ContainsBlock(x, y, z) && hull.HasBlock(x, y, z))
                {
                    exteriorX = x;
                    break;
                }
            }
            if (exteriorX < 0) return;
            Vector3 center = hull.transform.TransformPoint(def.BlockCenter(exteriorX, y, z));
            target.ReceiveImpact(center, def.BlockHealth * 4f, def.BlockSize * 0.7f);
        }

        public void PatchPlayer()
        {
            if (player != null && player.EmergencyPatch())
                Debug.Log("Boatships: emergency repairs sealed current external breaches (hull voxels still damaged).");
        }

        public void ResetShips()
        {
            if (shipyard != null && shipyard.InShipyard) return;
            if (player != null) player.ResetShipForDemo();
            if (pirate != null) pirate.ResetShipForDemo();
            playerHoleIndex = 0;
            pirateHoleIndex = 0;
        }
    }
}
