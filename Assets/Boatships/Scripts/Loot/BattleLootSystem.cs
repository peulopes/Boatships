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
        public bool PirateDefeated => economy != null && economy.HasLootEntry(EnemyLootId);
        public Transform PirateTarget => pirate != null ? pirate.transform : null;
        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private PortDocking docking;
        [SerializeField] private ShipCombatTarget player;
        [SerializeField] private ShipCombatTarget pirate;
        [SerializeField] private ShipFlooding pirateFlooding;
        [SerializeField] private ShipCustomization shipyard;
        [SerializeField] private OceanWaves ocean;
        [SerializeField, Min(2f)] private float collectRadius = 9f;
        [SerializeField] private bool leavePersistentWreck;

        public void EnablePersistentWreck() => leavePersistentWreck = true;

        private GameObject marker;
        private PortEconomy.LootCache pending;
        private int knownSeed = int.MinValue;
        private bool encounterProcessed;
        private string lastEvent = "Defeat the pirate to reveal salvage.";
        private float feedbackUntil;

        public bool HasLoot => economy != null && economy.PendingLoot.Count > 0;
        public string LastEvent => lastEvent;
        public float CollectRadius => collectRadius;
        public bool ShowCollectionFeedback => Time.unscaledTime < feedbackUntil;
        public float DistanceToLoot => TryNearestLoot(out _, out float distance) ? distance : float.PositiveInfinity;
        public bool CanCollect => HasLoot && !IsDocked && DistanceToLoot <= collectRadius;
        public string LootDescription => TryNearestLoot(out PortEconomy.LootCache nearest, out _) ? nearest.Describe() : "No loot remaining";
        private bool IsDocked => docking != null && docking.IsDocked;

        private bool TryNearestLoot(out PortEconomy.LootCache nearest, out float distance)
        {
            nearest = null;
            distance = float.PositiveInfinity;
            return economy != null && player != null && economy.TryNearestLoot(player.transform.position, out nearest, out distance);
        }

        public void Configure(ArchipelagoWorld map, PortEconomy wallet, PortDocking port,
            ShipCombatTarget controlled, ShipCombatTarget enemy, ShipFlooding enemyFlood,
            ShipCustomization customization, OceanWaves waves)
        {
            world = map; economy = wallet; docking = port;
            player = controlled; pirate = enemy; pirateFlooding = enemyFlood;
            shipyard = customization; ocean = waves;
        }

        private void Start()
        {
            if (economy != null) economy.LootCollected += OnLootCollected;
            SynchronizeWorld(true);
        }

        private void Update()
        {
            if (world == null || economy == null || player == null) return;
            SynchronizeWorld(false);
            if (pirate != null && !encounterProcessed && (pirate.RemainingVoxels <= 0 ||
                (pirateFlooding != null && pirateFlooding.IsSinking)))
            {
                if (!economy.HasLootEntry(EnemyLootId))
                {
                    PortEconomy.LootCache reward = RollLoot(world.Seed, pirate.transform.position);
                    reward.shipwreck = leavePersistentWreck;
                    if (economy.TryRegisterLoot(reward))
                    {
                        encounterProcessed = true;
                        pending = reward;
                        CreateMarker();
                        lastEvent = "Pirate defeated! Salvage is floating nearby.";
                    }
                }
                else encounterProcessed = true;
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
                if (BoatshipsDebugMenu.IsOpen && keyboard.yKey.wasPressedThisFrame) JumpToEncounterForTesting();
            }
        }

        private void SynchronizeWorld(bool force)
        {
            if (world == null || economy == null || pirate == null ||
                (!force && knownSeed == world.Seed)) return;
            knownSeed = world.Seed;
            economy.UseWorldSeed(knownSeed);
            pending = economy.GetPendingLoot(EnemyLootId);
            if (leavePersistentWreck && pending != null) economy.PreservePendingWreck(EnemyLootId);
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
                lastEvent = IsDocked ? "Depart the harbour to collect floating loot." : !HasLoot ? "There is no loot to collect." :
                    "Sail within " + collectRadius.ToString("0") + " m of the floating loot.";
                feedbackUntil = Time.unscaledTime + 6f;
                return;
            }
            economy.CollectNearbyLoot(player.transform.position, collectRadius, out lastEvent);
            feedbackUntil = Time.unscaledTime + 6f;
        }

        private void OnLootCollected(PortEconomy.LootCache collected)
        {
            lastEvent = "Collected loot: " + collected.Describe();
            feedbackUntil = Time.unscaledTime + 6f;
            if (pending != null && pending.id == collected.id)
            {
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
            marker.SetActive(false);
            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null) Destroy(renderer.material);
            Destroy(marker);
            marker = null;
        }

        private void OnDestroy()
        {
            if (economy != null) economy.LootCollected -= OnLootCollected;
            DestroyMarker();
        }
    }
}
