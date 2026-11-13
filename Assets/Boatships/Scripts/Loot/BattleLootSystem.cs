using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boatships
{
    /// <summary>
    /// One persistent battle reward for the M9 pirate encounter. Loot is generated
    /// after actual destruction/sinking, floats at the site and requires collection.
    /// Its pending/claimed status is stored in the existing per-world-seed port save.
    /// </summary>
    [DefaultExecutionOrder(550)]
    [DisallowMultipleComponent]
    public sealed class BattleLootSystem : MonoBehaviour
    {
        private const string EnemyLootId = "prototype_pirate_01";
        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private PortDocking docking;
        [SerializeField] private ShipCombatTarget player;
        [SerializeField] private ShipCombatTarget pirate;
        [SerializeField] private ShipFlooding pirateFlooding;
        [SerializeField] private ShipCustomization shipyard;
        [SerializeField] private OceanWaves ocean;
        [SerializeField, Min(2f)] private float collectRadius = 9f;

        private GameObject marker;
        private PortEconomy.LootCache pending;
        private int knownSeed = int.MinValue;
        private bool encounterProcessed;
        private string lastEvent = "Defeat the pirate to reveal salvage.";

        public bool HasLoot => pending != null;
        public string LastEvent => lastEvent;
        public float DistanceToLoot => pending != null && player != null ?
            Vector2.Distance(new Vector2(player.transform.position.x, player.transform.position.z),
                new Vector2(pending.x, pending.z)) : float.PositiveInfinity;
        public bool CanCollect => pending != null && !IsDocked && DistanceToLoot <= collectRadius;
        public string LootDescription => pending != null ? pending.Describe() : "No salvage floating nearby";
        private bool IsDocked => docking != null && docking.IsDocked;

        public void Configure(ArchipelagoWorld map, PortEconomy wallet, PortDocking port,
            ShipCombatTarget controlled, ShipCombatTarget enemy, ShipFlooding enemyFlood,
            ShipCustomization customization, OceanWaves waves)
        {
            world = map; economy = wallet; docking = port;
            player = controlled; pirate = enemy; pirateFlooding = enemyFlood;
            shipyard = customization; ocean = waves;
        }

        private void Start() => SynchronizeWorld(true);

        private void Update()
        {
            if (world == null || economy == null || pirate == null || player == null) return;
            SynchronizeWorld(false);
            if (!encounterProcessed && (pirate.RemainingVoxels <= 0 ||
                (pirateFlooding != null && pirateFlooding.IsSinking)))
            {
                encounterProcessed = true;
                if (!economy.HasLootEntry(EnemyLootId))
                {
                    PortEconomy.LootCache reward = RollLoot(world.Seed, pirate.transform.position);
                    if (economy.TryRegisterLoot(reward))
                    {
                        pending = reward;
                        CreateMarker();
                        lastEvent = "Pirate defeated! Salvage is floating nearby.";
                    }
                }
            }
            if (pending != null && marker != null)
            {
                Vector3 p = marker.transform.position;
                float sea = ocean != null ? ocean.SampleHeight(p.x, p.z, Time.time) : 0f;
                marker.transform.position = new Vector3(p.x, sea + 1.4f, p.z);
                marker.transform.Rotate(Vector3.up, 65f * Time.deltaTime, Space.World);
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.cKey.wasPressedThisFrame) CollectNearby();
                if (keyboard.yKey.wasPressedThisFrame) JumpToEncounterForTesting();
            }
        }

        private void SynchronizeWorld(bool force)
        {
            if (world == null || economy == null || pirate == null ||
                (!force && knownSeed == world.Seed)) return;
            knownSeed = world.Seed;
            economy.UseWorldSeed(knownSeed);
            pending = economy.GetPendingLoot(EnemyLootId);
            encounterProcessed = economy.HasLootEntry(EnemyLootId);
            DestroyMarker();
            if (encounterProcessed)
            {
                // Prevent repeatedly farming the same persistent enemy after reloading.
                pirate.gameObject.SetActive(false);
                if (pending != null) CreateMarker();
                lastEvent = pending != null ? "Saved salvage recovered from this world." :
                    "This pirate has already been defeated in this world.";
            }
            else
            {
                pirate.gameObject.SetActive(true);
                if (pirateFlooding != null) pirateFlooding.ResetShipForDemo();
                lastEvent = "Defeat the pirate to reveal salvage.";
            }
        }

        private static PortEconomy.LootCache RollLoot(int seed, Vector3 position)
        {
            // Deterministic for a given world seed and this one-off encounter.
            System.Random rng = new System.Random(unchecked(seed * 487 + 912911));
            return new PortEconomy.LootCache
            {
                id = EnemyLootId,
                x = position.x, z = position.z,
                gold = rng.Next(75, 161),
                wood = rng.Next(2, 7),
                iron = rng.Next(1, 5),
                rations = rng.Next(0, 4),
                spareCannons = rng.NextDouble() < 0.35 ? 1 : 0
            };
        }

        public void CollectNearby()
        {
            if (!CanCollect)
            {
                lastEvent = pending == null ? "There is no salvage to collect." :
                    "Move closer to the salvage marker (within " + collectRadius + "m).";
                return;
            }
            if (economy.TryCollectLoot(pending.id, out PortEconomy.LootCache collected))
            {
                lastEvent = "Recovered " + collected.Describe() + ".";
                pending = null;
                DestroyMarker();
            }
        }

        public void JumpToEncounterForTesting()
        {
            if (player == null || IsDocked) return;
            if (shipyard != null && shipyard.InShipyard) shipyard.SetShipyard(false);
            Vector3 destination = pending != null ? new Vector3(pending.x + 5f, 0.8f, pending.z) :
                pirate != null && pirate.gameObject.activeInHierarchy ?
                    pirate.transform.position + new Vector3(12f, 0.2f, 0f) : player.transform.position;
            destination.y = 0.8f;
            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null) rb.position = destination;
            else player.transform.position = destination;
            lastEvent = pending != null ? "Jumped near salvage (test only)." :
                "Jumped near pirate combat zone (test only).";
        }

        private void CreateMarker()
        {
            if (pending == null) return;
            DestroyMarker();
            marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Floating Pirate Salvage (M9)";
            marker.transform.position = new Vector3(pending.x, 1.4f, pending.z);
            marker.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);
            marker.transform.rotation = Quaternion.Euler(25f, 45f, 20f);
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Standard runtime material is only a simple placeholder; no external assets.
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    Material material = new Material(shader);
                    material.SetColor("_BaseColor", new Color(1f, 0.75f, 0.13f));
                    renderer.material = material;
                }
            }
        }

        private void DestroyMarker()
        {
            if (marker == null) return;
            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null) Destroy(renderer.material);
            Destroy(marker);
            marker = null;
        }

        private void OnDestroy() => DestroyMarker();
    }
}
