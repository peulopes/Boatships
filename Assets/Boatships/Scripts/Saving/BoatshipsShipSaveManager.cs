using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boatships
{
    /// <summary>
    /// Milestone 11: an independent per-seed player-ship save, kept separate from
    /// PortEconomy and Mission data to preserve all prior M8-M10 save files.
    /// Attach to scene via Tools > Boatships > Create Ship Persistence Test Scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoatshipsShipSaveManager : MonoBehaviour
    {
        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private ShipCustomization shipyard;
        [SerializeField] private VoxelHull hull;
        [SerializeField] private WeaponGroupCombat weapons;
        [SerializeField] private ShipFlooding flooding;
        [SerializeField] private Transform shipRoot;
        [SerializeField, Min(5f)] private float autosaveSeconds = 25f;

        private int loadedSeed = int.MinValue;
        private float nextSaveAt;
        private bool ready;
        private bool saveLocked; // Never overwrite unreadable/incompatible/partially restored saves.
        private List<ShipEquipmentRecord> defaultEquipment;
        private string status = "Waiting for ship initialization...";

        public string Status => status;
        public int LoadedSeed => loadedSeed;
        public bool CanCheckpoint => ready && !saveLocked;
        public bool LastSaveSucceeded { get; private set; }
        public string SaveFilePath => loadedSeed == int.MinValue ? "Not initialized" : PathFor(loadedSeed);
        public int InstalledPartCount => shipyard != null ? shipyard.InstalledCount : 0;

        public void Configure(ArchipelagoWorld aWorld, ShipCustomization aShipyard, VoxelHull aHull,
            WeaponGroupCombat aWeapons, ShipFlooding aFlooding, Transform aShipRoot)
        {
            world = aWorld; shipyard = aShipyard; hull = aHull;
            weapons = aWeapons; flooding = aFlooding; shipRoot = aShipRoot;
        }

        private IEnumerator Start()
        {
            // ShipCustomization.Start creates free starter equipment; ArchipelagoWorld.Start seeds the map.
            yield return null;
            yield return null;
            if (world == null || shipyard == null || hull == null || hull.Definition == null || shipRoot == null)
            {
                status = "M11 not configured: missing world, shipyard, hull or player ship.";
                Debug.LogWarning(status, this);
                yield break;
            }
            defaultEquipment = shipyard.CaptureEquipment();
            Debug.Log("Boatships M11: Save system active in scene: " + gameObject.scene.name, this);
            loadedSeed = world.Seed;
            ready = true;
            LoadShip();
            nextSaveAt = Time.unscaledTime + autosaveSeconds;
        }

        private void Update()
        {
            if (!ready) return;
            if (world.Seed != loadedSeed)
            {
                SaveShip(); // Snapshot current ship before changing its seed association.
                loadedSeed = world.Seed;
                saveLocked = false;
                ResetToStarter();
                LoadShip();
                nextSaveAt = Time.unscaledTime + autosaveSeconds;
            }
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f4Key.wasPressedThisFrame) SaveShip();
            }
            if (Time.unscaledTime >= nextSaveAt)
            {
                SaveShip();
                nextSaveAt = Time.unscaledTime + autosaveSeconds;
            }
        }

        private string PathFor(int seed) => System.IO.Path.Combine(Application.persistentDataPath,
            "boatships_ship_v1_" + seed + ".json");

        private void ResetToStarter()
        {
            hull.ResetDesign();
            if (flooding != null) flooding.RestoreFlooding(null);
            shipyard.RestoreEquipment(defaultEquipment);
            if (weapons != null) weapons.RestoreWeaponGroups(null);
            if (shipRoot != null)
            {
                Rigidbody body = shipRoot.GetComponent<Rigidbody>();
                if (body != null) { body.position = Vector3.zero; body.rotation = Quaternion.identity; }
                else { shipRoot.position = Vector3.zero; shipRoot.rotation = Quaternion.identity; }
            }
        }

        private ShipSaveRecord Capture(int seed)
        {
            return new ShipSaveRecord
            {
                seed = seed,
                hullId = hull.Definition.HullId,
                position = shipRoot.position,
                yaw = shipRoot.eulerAngles.y,
                installedEquipment = shipyard.CaptureEquipment(),
                weaponGroups = weapons != null ? weapons.CaptureWeaponGroups() : new List<ShipWeaponGroupRecord>(),
                damagedVoxels = hull.CaptureVoxelDamage(),
                flooding = flooding != null ? flooding.CaptureFlooding() : new ShipFloodRecord()
            };
        }

        public void SaveShip()
        {
            LastSaveSucceeded = false;
            if (!ready)
            {
                Debug.LogWarning("Boatships M11: Save requested before initialization.", this);
                return;
            }
            if (saveLocked)
            {
                status = "SAVE BLOCKED: previous ship file could not be fully restored. Check Console; existing file is protected.";
                Debug.LogError("Boatships M11: " + status + " Path: " + SaveFilePath, this);
                return;
            }
            try
            {
                ShipSaveRecord data = Capture(loadedSeed);
                string path = PathFor(loadedSeed);
                string json = JsonUtility.ToJson(data);
                // Guard against accidentally saving only starter parts or a non-serializable record.
                ShipSaveRecord validation = JsonUtility.FromJson<ShipSaveRecord>(json);
                if (validation == null || validation.installedEquipment == null ||
                    validation.installedEquipment.Count != data.installedEquipment.Count)
                    throw new InvalidDataException("Equipment serialization was incomplete.");
                BoatshipsSaveStore.Write(path, json);
                LastSaveSucceeded = true;
                status = "SAVED " + data.installedEquipment.Count + " parts, " +
                    data.damagedVoxels.Count + " damaged voxels (seed " + loadedSeed + ").";
                if (!BoatshipsSaveStore.Enabled) Debug.Log("Boatships M11: " + status + " File: " + path, this);
            }
            catch (Exception e)
            {
                status = "Ship save FAILED: " + e.Message;
                Debug.LogWarning("Boatships M11: " + status, this);
            }
        }

        public void LoadShip()
        {
            if (!ready) return;
            string path = PathFor(loadedSeed);
            if (!BoatshipsSaveStore.Exists(path) && !BoatshipsSaveStore.Exists(path + ".bak"))
            {
                status = "NEW WORLD: no ship file for seed " + loadedSeed +
                    ". Starter layout has " + shipyard.InstalledCount + " parts. Press F4 to save.";
                if (!BoatshipsSaveStore.Enabled) Debug.Log("Boatships M11: " + status + " File: " + path, this);
                return;
            }
            ShipSaveRecord saved = Read(path) ?? Read(path + ".bak");
            if (saved == null)
            {
                saveLocked = true;
                status = "LOAD FAILED: unreadable ship file; old file protected. See Console.";
                Debug.LogError("Boatships M11: " + status + " File: " + path, this);
                return;
            }
            if (saved.saveVersion != 1 || saved.seed != loadedSeed ||
                saved.shipId != "player_flagship" || saved.hullId != hull.Definition.HullId)
            {
                saveLocked = true;
                status = "LOAD BLOCKED: mismatched seed, hull or schema; old file protected.";
                Debug.LogWarning(status, this);
                return;
            }
            // Restore equipment against pristine support cells, then apply damage.
            int expectedParts = saved.installedEquipment != null ? saved.installedEquipment.Count : 0;
            try
            {
            hull.RestoreVoxelDamage(saved.damagedVoxels, true);
            if (flooding != null) flooding.RestoreFlooding(null);
            shipyard.RestoreEquipment(saved.installedEquipment);
            if (weapons != null) weapons.RestoreWeaponGroups(saved.weaponGroups);
            hull.RestoreVoxelDamage(saved.damagedVoxels);
            if (flooding != null) flooding.RestoreFlooding(saved.flooding);
            if (shipRoot != null)
            {
                Quaternion heading = Quaternion.Euler(0f, saved.yaw, 0f);
                Rigidbody body = shipRoot.GetComponent<Rigidbody>();
                if (body != null) { body.position = saved.position; body.rotation = heading; }
                else { shipRoot.position = saved.position; shipRoot.rotation = heading; }
            }
            int restoredParts = shipyard.InstalledCount;
            if (restoredParts != expectedParts)
            {
                saveLocked = true;
                status = "PARTIAL LOAD: restored " + restoredParts + "/" + expectedParts +
                    " parts. Save locked to protect existing data; check Console warnings.";
                Debug.LogError("Boatships M11: " + status + " File: " + path, this);
            }
            else
            {
                status = "RESTORED " + restoredParts + "/" + expectedParts +
                    " parts, seed " + loadedSeed + ".";
                if (!BoatshipsSaveStore.Enabled) Debug.Log("Boatships M11: " + status + " File: " + path, this);
            }
            }
            catch (Exception ex)
            {
                saveLocked = true;
                status = "RESTORE FAILED: " + ex.Message + "; file protected.";
                Debug.LogError("Boatships M11: " + status + " File: " + path, this);
            }
        }

        private static ShipSaveRecord Read(string path)
        {
            if (!BoatshipsSaveStore.Exists(path)) return null;
            try { return JsonUtility.FromJson<ShipSaveRecord>(BoatshipsSaveStore.Read(path)); }
            catch (Exception e)
            {
                Debug.LogWarning("Boatships M11: Invalid ship file " + path + ": " + e.Message);
                return null;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveShip();
        }

        private void OnApplicationQuit() => SaveShip();

        // Editor Stop can disable scene objects during teardown; save one last snapshot if still active.
        private void OnDisable()
        {
            if (ready && Application.isPlaying) SaveShip();
        }
    }
}
