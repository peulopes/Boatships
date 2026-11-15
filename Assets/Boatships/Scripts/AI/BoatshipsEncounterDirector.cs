using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    [DefaultExecutionOrder(700)]
    public sealed class BoatshipsEncounterDirector : MonoBehaviour
    {
        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private ShipCombatTarget player;
        [SerializeField] private EnemyShipAI template;
        [SerializeField] private OceanWaves ocean;
        private readonly Dictionary<string, ShipCombatTarget> actors = new Dictionary<string, ShipCombatTarget>();
        private readonly Dictionary<string, GameObject> cargo = new Dictionary<string, GameObject>();
        private readonly List<string> staleCargo = new List<string>();
        private bool ready;
        private int seed = int.MinValue;
        private float nextTick;
        private float nextWave;
        private PortDocking docking;
        private Material cargoMaterial;
        public string Status { get; private set; } = "Preparing encounters";
        public ArchipelagoWorld World => world;

        public void Configure(ArchipelagoWorld map, PortEconomy account, ShipCombatTarget controlled,
            EnemyShipAI source, OceanWaves waves)
        { world = map; economy = account; player = controlled; template = source; ocean = waves; }

        private IEnumerator Start()
        {
            for (int i = 0; i < 8; i++) yield return null;
            docking = FindAnyObjectByType<PortDocking>();
            if (world == null || economy == null || player == null || template == null) yield break;
            cargoMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(1f, 0.72f, 0.12f) };
            ready = true;
        }

        private void Update()
        {
            if (!ready || economy.ActiveSeed != world.Seed || Time.time < nextTick) return;
            nextTick = Time.time + 0.5f;
            if (seed != world.Seed)
            {
                ClearActors();
                seed = world.Seed;
                nextWave = 0f;
                RestoreActors();
            }
            bool unresolved = false;
            int follower = 0;
            foreach (EncounterRecord record in economy.Campaign.encounters)
            {
                if (!actors.TryGetValue(record.id, out ShipCombatTarget actor) || actor == null) continue;
                if (record.captured)
                {
                    ShipFlooding capturedFlood = actor.GetComponent<ShipFlooding>();
                    if (actor.RemainingVoxels <= 0 || (capturedFlood != null && capturedFlood.IsSinking))
                    {
                        if (economy.LoseCapturedVessel(record.id, actor.transform.position))
                        { actors.Remove(record.id); Destroy(actor.gameObject); }
                        continue;
                    }
                    follower++;
                    Vector3 target = player.transform.TransformPoint(new Vector3(follower % 2 == 0 ? -7f : 7f,
                        0f, -7f * ((follower + 1) / 2)));
                    Vector3 position = Vector3.MoveTowards(actor.transform.position, target, 3.5f);
                    position = world.KeepOnWater(position, 3.5f);
                    position.y = ocean.SampleHeight(position.x, position.z, Time.time) + 0.55f;
                    actor.GetComponent<Rigidbody>().position = position;
                    actor.transform.rotation = player.transform.rotation;
                    continue;
                }
                if (record.defeated) continue;
                ShipFlooding flood = actor.GetComponent<ShipFlooding>();
                if (actor.RemainingVoxels <= 0 || (flood != null && flood.IsSinking))
                {
                    if (economy.ResolveEncounter(record.id, false, actor.transform.position))
                    {
                        actors.Remove(record.id);
                        Destroy(actor.gameObject);
                        Status = "Enemy defeated. Cargo is floating at the wreck.";
                    }
                }
                else unresolved = true;
            }
            if (unresolved) nextWave = Time.time + 60f;
            else if (Time.time >= nextWave && !docking.IsDocked) SpawnWave();
            SynchronizeCargo();
        }

        public void CaptureEncounterState()
        {
            if (!ready || economy.ActiveSeed != seed) return;
            foreach (EncounterRecord record in economy.Campaign.encounters)
            {
                if (!actors.TryGetValue(record.id, out ShipCombatTarget actor) || actor == null || record.defeated) continue;
                record.position = actor.transform.position;
                record.yaw = actor.transform.eulerAngles.y;
                record.damage = actor.Hull.CaptureVoxelDamage();
                ShipFlooding flood = actor.GetComponent<ShipFlooding>();
                record.flooding = flood != null ? flood.CaptureFlooding() : null;
            }
        }

        private void RestoreActors()
        {
            int capturedVisible = 0;
            foreach (EncounterRecord record in economy.Campaign.encounters)
            {
                if (record.defeated || (record.captured && ++capturedVisible > 2)) continue;
                Spawn(record);
            }
        }

        private void SpawnWave()
        {
            int tier = Mathf.Clamp(1 + economy.Campaign.victories / 3, 1, 5);
            var records = new List<EncounterRecord>();
            int serial = economy.Campaign.encounterSerial;
            var random = new System.Random(unchecked(world.Seed * 397 ^ serial));
            for (int i = 0; i < 3; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector3 position = world.KeepOnWater(player.transform.position +
                    new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (75f + i * 25f), 5f);
                records.Add(new EncounterRecord { id = "campaign_" + serial + "_" + i,
                    kind = (EncounterKind)i, tier = tier, position = position,
                    defenderHealth = 45f + tier * 20f });
            }
            if (!economy.TryCampaignChange(progress =>
                { progress.encounterSerial++; progress.encounters.AddRange(records); })) return;
            foreach (EncounterRecord record in records) Spawn(record);
            Status = "New raider, brig and patrol encounters. Tier " + tier + ".";
            nextWave = Time.time + 60f;
        }

        private void Spawn(EncounterRecord record)
        {
            GameObject clone = Instantiate(template.gameObject, record.position,
                Quaternion.Euler(0f, record.yaw, 0f));
            clone.name = record.kind + " " + record.id;
            clone.SetActive(true);
            ShipCombatTarget target = clone.GetComponent<ShipCombatTarget>();
            target.Hull.EnableStructuralIntegrity();
            target.Hull.HullHealthMultiplier = 1f + record.tier * 0.12f;
            target.Hull.RestoreVoxelDamage(record.damage);
            ShipFlooding savedFlood = clone.GetComponent<ShipFlooding>();
            if (savedFlood != null && record.flooding != null) savedFlood.RestoreFlooding(record.flooding);
            EnemyShipAI ai = clone.GetComponent<EnemyShipAI>();
            ai.ConfigureEncounter(record.kind, record.tier, record.position);
            ai.enabled = !record.captured;
            float scale = record.kind == EncounterKind.Brig ? 1.25f : record.kind == EncounterKind.Raider ? 0.85f : 1f;
            clone.transform.localScale = Vector3.one * scale;
            ArchipelagoNavigationLimiter limiter = clone.GetComponent<ArchipelagoNavigationLimiter>();
            if (limiter == null) limiter = clone.AddComponent<ArchipelagoNavigationLimiter>();
            limiter.Configure(world);
            actors.Add(record.id, target);
        }

        public ShipCombatTarget NearestEnemy(out EncounterRecord record, out float distance)
        {
            record = null;
            distance = float.PositiveInfinity;
            ShipCombatTarget nearest = null;
            foreach (EncounterRecord candidate in economy.Campaign.encounters)
            {
                if (candidate.defeated || candidate.captured || !actors.TryGetValue(candidate.id, out ShipCombatTarget actor) ||
                    actor == null || !actor.gameObject.activeInHierarchy) continue;
                Vector3 offset = actor.transform.position - player.transform.position;
                offset.y = 0f;
                float d = offset.magnitude;
                if (d >= distance) continue;
                nearest = actor; record = candidate; distance = d;
            }
            return nearest;
        }

        public bool Capture(EncounterRecord record, ShipCombatTarget target)
        {
            if (!economy.ResolveEncounter(record.id, true, target.transform.position)) return false;
            record.damage = target.Hull.CaptureVoxelDamage();
            ShipFlooding flood = target.GetComponent<ShipFlooding>();
            record.flooding = flood != null ? flood.CaptureFlooding() : null;
            target.GetComponent<EnemyShipAI>().enabled = false;
            int visible = 0;
            foreach (EncounterRecord vessel in economy.Campaign.encounters)
                if (vessel.captured && actors.TryGetValue(vessel.id, out ShipCombatTarget actor) &&
                    actor != null && actor.gameObject.activeSelf) visible++;
            if (visible > 2) { actors.Remove(record.id); Destroy(target.gameObject); }
            Status = visible > 2 ? "Vessel captured and added to the reserve fleet." :
                "Vessel captured. It joins your fleet and follows your flagship.";
            return true;
        }

        public void CollectNearbyCargo()
        {
            if (docking == null || docking.IsDocked) return;
            var pending = new List<PortEconomy.LootCache>(economy.PendingLoot);
            int collected = 0;
            foreach (PortEconomy.LootCache cache in pending)
            {
                if (!cache.id.StartsWith("campaign_", System.StringComparison.Ordinal)) continue;
                Vector2 delta = new Vector2(player.transform.position.x - cache.x, player.transform.position.z - cache.z);
                if (delta.sqrMagnitude <= 81f && economy.TryCollectLoot(cache.id, out _)) collected++;
            }
            Status = collected > 0 ? "Recovered cargo from " + collected + " vessel(s)." : "Sail within 9m of cargo.";
            SynchronizeCargo();
        }

        private void SynchronizeCargo()
        {
            staleCargo.Clear();
            foreach (string id in cargo.Keys)
            {
                PortEconomy.LootCache pending = economy.GetPendingLoot(id);
                if (pending == null || new Vector2(player.transform.position.x - pending.x,
                    player.transform.position.z - pending.z).sqrMagnitude > 14400f) staleCargo.Add(id);
            }
            foreach (string id in staleCargo) { Destroy(cargo[id]); cargo.Remove(id); }
            foreach (PortEconomy.LootCache cache in economy.PendingLoot)
            {
                if (!cache.id.StartsWith("campaign_", System.StringComparison.Ordinal)) continue;
                if (new Vector2(player.transform.position.x - cache.x,
                    player.transform.position.z - cache.z).sqrMagnitude > 14400f) continue;
                if (!cargo.TryGetValue(cache.id, out GameObject marker))
                {
                    marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marker.name = "Campaign cargo";
                    Destroy(marker.GetComponent<Collider>());
                    marker.GetComponent<Renderer>().sharedMaterial = cargoMaterial;
                    marker.transform.localScale = Vector3.one * 0.8f;
                    cargo.Add(cache.id, marker);
                }
                marker.transform.position = new Vector3(cache.x,
                    ocean.SampleHeight(cache.x, cache.z, Time.time) + 0.8f, cache.z);
            }
        }

        private void ClearActors()
        {
            foreach (ShipCombatTarget actor in actors.Values) if (actor != null) Destroy(actor.gameObject);
            foreach (GameObject marker in cargo.Values) if (marker != null) Destroy(marker);
            actors.Clear(); cargo.Clear();
        }
        private void OnDestroy() { ClearActors(); if (cargoMaterial != null) Destroy(cargoMaterial); }
    }
}
