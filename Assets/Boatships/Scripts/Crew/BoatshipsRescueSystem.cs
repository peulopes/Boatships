using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>World-seed wreck markers and physical recovery of M13 survivors.</summary>
    [DisallowMultipleComponent]
    public sealed class BoatshipsRescueSystem : MonoBehaviour
    {
        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private PortEconomy economy;
        [SerializeField] private BoatshipsCrewProgression progression;
        [SerializeField] private Transform player;
        [SerializeField] private OceanWaves ocean;
        [SerializeField] private BattleLootSystem loot;
        [SerializeField] private Text summary;
        [SerializeField] private Button rescue;
        [SerializeField] private Button salvage;
        [SerializeField, Min(1f)] private float rescueRadius = 9f;

        private readonly Dictionary<string, Vector3> survivors = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, GameObject> swimmers = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> wrecks = new Dictionary<string, GameObject>();
        private readonly HashSet<string> currentWreckIds = new HashSet<string>();
        private readonly List<string> removed = new List<string>();
        private Transform markers;
        private Material timber;
        private Material survivorMaterial;
        private int seed = int.MinValue;
        private float nextRefresh;
        private string status = "Sail near survivors to rescue them. C collects wreck cargo.";

        public void Configure(ArchipelagoWorld map, PortEconomy account,
            BoatshipsCrewProgression crewProgression, Transform ship, OceanWaves waves,
            BattleLootSystem battleLoot, Text label, Button rescueButton, Button salvageButton)
        {
            world = map;
            economy = account;
            progression = crewProgression;
            player = ship;
            ocean = waves;
            loot = battleLoot;
            summary = label;
            rescue = rescueButton;
            salvage = salvageButton;
        }

        private void OnEnable()
        {
            if (progression != null) progression.SetRescueSystem(this);
            if (rescue != null) rescue.onClick.AddListener(RescueNearby);
            if (salvage != null) salvage.onClick.AddListener(CollectCargo);
        }

        private void OnDisable()
        {
            if (progression != null) progression.SetRescueSystem(null);
            if (rescue != null) rescue.onClick.RemoveListener(RescueNearby);
            if (salvage != null) salvage.onClick.RemoveListener(CollectCargo);
            ClearMarkers();
        }

        private void Start()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("Boatships M14 requires a supported Lit shader.", this);
                enabled = false;
                return;
            }
            timber = new Material(shader) { color = new Color(0.28f, 0.14f, 0.06f) };
            survivorMaterial = new Material(shader) { color = new Color(1f, 0.5f, 0.12f) };
        }

        private void Update()
        {
            if (world == null || economy == null || progression == null || player == null ||
                !progression.IsReady || economy.ActiveSeed != world.Seed) return;
            if (seed != world.Seed)
            {
                ClearMarkers();
                seed = world.Seed;
                nextRefresh = 0f;
            }
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.25f;
                SynchronizeMarkers();
                RefreshHUD();
            }
            foreach (GameObject swimmer in swimmers.Values)
            {
                Vector3 p = swimmer.transform.position;
                p.y = ocean != null ? ocean.SampleHeight(p.x, p.z, Time.time) : 0f;
                swimmer.transform.position = p;
            }
        }

        private void SynchronizeMarkers()
        {
            if (markers == null) markers = new GameObject("M14 Ocean Rescue and Wrecks").transform;
            progression.GetSurvivors(survivors);
            removed.Clear();
            foreach (string key in swimmers.Keys)
                if (!survivors.ContainsKey(key)) removed.Add(key);
            foreach (string key in removed)
            {
                Destroy(swimmers[key]);
                swimmers.Remove(key);
            }
            foreach (KeyValuePair<string, Vector3> survivor in survivors)
            {
                if (swimmers.ContainsKey(survivor.Key)) continue;
                GameObject swimmer = new GameObject("Overboard survivor");
                swimmer.transform.SetParent(markers, false);
                swimmer.transform.position = survivor.Value;
                AddPart(swimmer.transform, PrimitiveType.Cube, new Vector3(0f, -0.12f, 0f),
                    new Vector3(1.6f, 0.3f, 1.1f), timber);
                // A castaway in a bright orange shirt standing on the plank, waving for rescue.
                Transform castaway = new GameObject("Castaway").transform;
                castaway.SetParent(swimmer.transform, false);
                castaway.localPosition = new Vector3(0f, 0.03f, 0f);
                castaway.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                BoatshipsHumanoid.Build(castaway, new Color(1f, 0.5f, 0.12f), 1.25f, false, survivor.Key.GetHashCode() & 0x7fffffff);
                castaway.GetComponent<BoatshipsHumanoidRig>().Waving = true;
                swimmers.Add(survivor.Key, swimmer);
            }
            currentWreckIds.Clear();
            foreach (PortEconomy.LootCache wreck in economy.Wrecks)
            {
                Vector2 offset = new Vector2(player.position.x - wreck.x, player.position.z - wreck.z);
                if (offset.sqrMagnitude > 14400f) continue;
                currentWreckIds.Add(wreck.id);
                if (wrecks.ContainsKey(wreck.id)) continue;
                Transform root = new GameObject("Persistent shipwreck").transform;
                root.SetParent(markers, false);
                root.position = new Vector3(wreck.x, -0.45f, wreck.z);
                root.rotation = Quaternion.Euler(8f, 25f, 22f);
                AddPart(root, PrimitiveType.Cube, Vector3.zero, new Vector3(4f, 0.5f, 8f), timber);
                for (int i = 0; i < 4; i++)
                {
                    AddPart(root, PrimitiveType.Cube, new Vector3(-2f, 0.7f, i * 2f - 3f),
                        new Vector3(0.4f, 1.6f, 0.5f), timber);
                    AddPart(root, PrimitiveType.Cube, new Vector3(2f, 0.4f, i * 2f - 3f),
                        new Vector3(0.4f, 1f, 0.5f), timber);
                }
                AddPart(root, PrimitiveType.Cube, new Vector3(0f, 1.4f, 0f),
                    new Vector3(0.4f, 3f, 0.4f), timber);
                wrecks.Add(wreck.id, root.gameObject);
            }
            removed.Clear();
            foreach (string id in wrecks.Keys)
                if (!currentWreckIds.Contains(id)) removed.Add(id);
            foreach (string id in removed)
            {
                Destroy(wrecks[id]);
                wrecks.Remove(id);
            }
        }

        private static void AddPart(Transform parent, PrimitiveType shape, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(part.GetComponent<Collider>());
        }

        private string NearestSurvivor(out float distance)
        {
            distance = float.PositiveInfinity;
            string nearest = null;
            foreach (KeyValuePair<string, Vector3> survivor in survivors)
            {
                Vector3 offset = player.position - survivor.Value;
                offset.y = 0f;
                float candidate = offset.magnitude;
                if (candidate >= distance) continue;
                nearest = survivor.Key;
                distance = candidate;
            }
            return nearest;
        }

        public void RescueNearby()
        {
            if (progression == null || player == null || !progression.IsReady) return;
            progression.GetSurvivors(survivors);
            string key = NearestSurvivor(out float distance);
            if (key == null) status = "There are no survivors awaiting rescue.";
            else if (distance > rescueRadius) status = "Sail within " + rescueRadius + "m of the survivor.";
            else status = progression.TryRescue(key, rescueRadius) ? "Survivor recovered aboard your ship." :
                "Rescue unavailable. Undock and use a seaworthy ship. Check crew save status.";
            SynchronizeMarkers();
            RefreshHUD();
        }

        private void CollectCargo()
        {
            if (loot == null) return;
            loot.CollectNearby();
            status = loot.LastEvent;
            RefreshHUD();
        }

        private void RefreshHUD()
        {
            NearestSurvivor(out float distance);
            if (summary != null)
                summary.text = "SHIPWRECKS AND RESCUE (M14)\n" + economy.Wrecks.Count +
                    " wreck(s) | " + survivors.Count + " survivor(s)" +
                    (survivors.Count > 0 ? " | Nearest " + distance.ToString("0") + "m" : "") +
                    "\n" + status;
            if (rescue != null) rescue.interactable = distance <= rescueRadius;
            if (salvage != null) salvage.interactable = loot != null && loot.CanCollect;
        }

        private void ClearMarkers()
        {
            if (markers != null) Destroy(markers.gameObject);
            markers = null;
            swimmers.Clear();
            survivors.Clear();
            wrecks.Clear();
            currentWreckIds.Clear();
        }

        private void OnDestroy()
        {
            ClearMarkers();
            if (timber != null) Destroy(timber);
            if (survivorMaterial != null) Destroy(survivorMaterial);
        }
    }
}
