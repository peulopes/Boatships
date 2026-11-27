using System;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Boatships
{
    [DefaultExecutionOrder(-20000)]
    public sealed class BoatshipsSaveCoordinator : MonoBehaviour
    {
        [SerializeField] private PortEconomy economy;
        [SerializeField] private BoatshipsShipSaveManager ship;
        [SerializeField] private BoatshipsCrewManager crew;
        [SerializeField] private BoatshipsCrewProgression progression;
        [SerializeField] private BoatshipsEncounterDirector encounters;
        private bool ready;
        private float nextCheckpoint;
        private float retryAt;
        private float dirtySince = -1f;
        /// <summary>Transactions arrive in bursts (buying, recruiting); one checkpoint covers the whole burst.</summary>
        private const float DirtyCommitDelay = 3f;
        private static int? pendingNewGameSeed;
        private static bool launchNewGame;
#if UNITY_EDITOR
        [SerializeField, HideInInspector] private string validationDirectory;
#endif
        /// <summary>Raised at the start of every checkpoint so additional systems (character, world map)
        /// can stage their documents into the same atomic commit.</summary>
        public static event Action CheckpointCapturing;
        public string Status { get; private set; } = "Initializing save slot";
        public int Slot => BoatshipsSaveStore.Slot;
        public bool IsReady => ready && !pendingNewGameSeed.HasValue && economy != null && economy.CanCheckpoint &&
            ship != null && ship.CanCheckpoint && crew != null && crew.CanCheckpoint &&
            progression != null && progression.CanCheckpoint;
        public int WorldSeed => encounters.World.Seed;

        public void Configure(PortEconomy account, BoatshipsShipSaveManager shipSave,
            BoatshipsCrewManager deckCrew, BoatshipsCrewProgression sailors, BoatshipsEncounterDirector director)
        { economy = account; ship = shipSave; crew = deckCrew; progression = sailors; encounters = director; }

        private void Awake()
        {
            string saveDirectory = null;
#if UNITY_EDITOR
            saveDirectory = string.IsNullOrEmpty(validationDirectory) ? null : validationDirectory;
#endif
            BoatshipsSaveStore.Initialize(saveDirectory != null ? 0 : PlayerPrefs.GetInt("Boatships.SaveSlot", 0), saveDirectory);
            ArchipelagoWorld world = encounters.World;
            world.UseSeed(pendingNewGameSeed ?? (saveDirectory != null ? world.Seed :
                PlayerPrefs.GetInt("Boatships.WorldSeed." + Slot, world.Seed)));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLaunch() { pendingNewGameSeed = null; launchNewGame = false; }

        public static bool ConsumeNewGameLaunch()
        {
            bool launching = launchNewGame;
            launchNewGame = false;
            pendingNewGameSeed = null;
            return launching;
        }

        private IEnumerator Start()
        {
            for (int i = 0; i < 12; i++) yield return null;
            ready = true;
            nextCheckpoint = Time.unscaledTime + 25f;
        }

        private void LateUpdate()
        {
            if (BoatshipsSaveStore.PollBackgroundWrite()) Status = BoatshipsSaveStore.Status;
            if (!ready || Time.unscaledTime < retryAt || BoatshipsSaveStore.Writing) return;
            if (!BoatshipsSaveStore.Dirty) dirtySince = -1f;
            else if (dirtySince < 0f) dirtySince = Time.unscaledTime;
            bool dirtyDue = dirtySince >= 0f && Time.unscaledTime - dirtySince >= DirtyCommitDelay;
            if (dirtyDue || Time.unscaledTime >= nextCheckpoint) SaveCheckpoint(true);
        }

        public bool SaveCheckpoint() => SaveCheckpoint(false);

        /// <summary>Captures every subsystem into one checkpoint. With <paramref name="background"/> the file write
        /// happens off the main thread (autosaves); otherwise it completes before returning (slot switch, quit).</summary>
        public bool SaveCheckpoint(bool background)
        {
            dirtySince = -1f;
            if (!ready || economy == null || !economy.CanCheckpoint || ship == null || !ship.CanCheckpoint ||
                crew == null || !crew.CanCheckpoint || progression == null || !progression.CanCheckpoint)
            {
                Status = "Checkpoint unavailable. Check ship, crew and economy load status.";
                retryAt = Time.unscaledTime + 5f;
                return false;
            }
            encounters.CaptureEncounterState();
            try { CheckpointCapturing?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("Boatships checkpoint extension failed: " + ex.Message, this); }
            ship.SaveShip();
            crew.SaveAssignments();
            progression.SaveStates();
            if (!ship.LastSaveSucceeded || !crew.LastSaveSucceeded || !progression.LastSaveSucceeded ||
                !economy.SaveNow() || ship.LoadedSeed != economy.ActiveSeed || crew.ActiveSeed != economy.ActiveSeed)
            {
                Status = "Checkpoint capture failed. Previous checkpoint retained.";
                retryAt = Time.unscaledTime + 5f;
                return false;
            }
            bool result = BoatshipsSaveStore.Commit(background);
            if (result) RememberWorld(WorldSeed);
            Status = BoatshipsSaveStore.Status;
            retryAt = Time.unscaledTime + (result ? 0f : 5f);
            nextCheckpoint = Time.unscaledTime + 25f;
            return result;
        }

        public void SwitchSlot(int slot)
        {
            if (slot < 0 || slot > 2 || slot == Slot || !SaveCheckpoint()) return;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.buildIndex < 0)
            { Status = "Add this campaign scene to Build Profiles before switching slots."; return; }
            PlayerPrefs.SetInt("Boatships.SaveSlot", slot);
            PlayerPrefs.Save();
            SceneManager.LoadScene(scene.buildIndex);
        }

        public bool StartNewGame()
        {
            if (pendingNewGameSeed.HasValue) return false;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.buildIndex < 0)
            { Status = "Add this campaign scene to Build Profiles before starting a new game."; return false; }
            if (!SaveCheckpoint()) return false;
            int newSeed;
            do { newSeed = Guid.NewGuid().GetHashCode() & int.MaxValue; }
            while (newSeed == WorldSeed || BoatshipsSaveStore.WorldExists(newSeed));
            pendingNewGameSeed = newSeed;
            launchNewGame = true;
            RememberWorld(newSeed);
            Status = "Started a new world with seed " + newSeed + ".";
            try
            {
                SceneManager.LoadScene(scene.buildIndex);
                return true;
            }
            catch (Exception ex)
            {
                pendingNewGameSeed = null;
                launchNewGame = false;
                RememberWorld(WorldSeed);
                Status = "Could not start the new game. " + ex.Message;
                Debug.LogWarning(Status, this);
                return false;
            }
        }

        private void RememberWorld(int seed)
        {
            if (pendingNewGameSeed.HasValue && pendingNewGameSeed.Value != seed) return;
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(validationDirectory)) return;
#endif
            string key = "Boatships.WorldSeed." + Slot;
            if (PlayerPrefs.GetInt(key, int.MinValue) == seed) return;
            PlayerPrefs.SetInt(key, seed);
            PlayerPrefs.Save();
        }

        private void OnApplicationPause(bool paused) { if (paused && ready) SaveCheckpoint(); }
        private void OnApplicationQuit() { if (ready) SaveCheckpoint(); }
        private void OnDisable() { if (ready && Application.isPlaying) SaveCheckpoint(); }
    }

    /// <summary>Stages subsystem documents and commits an entire world checkpoint atomically.</summary>
    public static class BoatshipsSaveStore
    {
        [Serializable] private sealed class Document { public string name; public string json; }
        [Serializable] private sealed class Checkpoint
        {
            public int version = 1;
            public int seed;
            public int slot;
            public List<Document> documents = new List<Document>();
        }
        private static readonly Dictionary<int, Checkpoint> checkpoints = new Dictionary<int, Checkpoint>();
        private static readonly HashSet<int> dirty = new HashSet<int>();
        private static string directory;
        public static bool Enabled { get; private set; }
        public static int Slot { get; private set; }
        public static bool Dirty => dirty.Count > 0;
        private static Task pendingWrite;
        private static List<int> pendingSeeds;
        /// <summary>True while a background checkpoint write is still on disk I/O.</summary>
        public static bool Writing => pendingWrite != null && !pendingWrite.IsCompleted;
        public static string Status { get; private set; } = "Ready";

        public static void Initialize(int slot, string saveDirectory = null)
        {
            WaitForBackgroundWrite();
            Enabled = true;
            Slot = Mathf.Clamp(slot, 0, 2);
            directory = saveDirectory ?? Application.persistentDataPath;
            checkpoints.Clear();
            dirty.Clear();
            Status = "Save slot " + (Slot + 1);
        }

        public static void Shutdown() { WaitForBackgroundWrite(); Enabled = false; checkpoints.Clear(); dirty.Clear(); }

        /// <summary>Completes a finished background write on the main thread. Returns true when one completed.</summary>
        public static bool PollBackgroundWrite()
        {
            if (pendingWrite == null || !pendingWrite.IsCompleted) return false;
            Task finished = pendingWrite;
            pendingWrite = null;
            if (finished.IsFaulted || finished.IsCanceled)
            {
                // Keep the documents staged so the next checkpoint retries the write.
                foreach (int seed in pendingSeeds) dirty.Add(seed);
                Exception error = finished.Exception != null ? finished.Exception.GetBaseException() : null;
                Status = "Checkpoint save failed. Retry required. " + (error != null ? error.Message : "Write cancelled.");
                Debug.LogWarning(Status);
            }
            else Status = "Checkpoint saved to slot " + (Slot + 1);
            pendingSeeds = null;
            return true;
        }

        private static void WaitForBackgroundWrite()
        {
            if (pendingWrite == null) return;
            try { pendingWrite.Wait(); } catch (AggregateException) { }
            PollBackgroundWrite();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => Shutdown();

        private static int SeedFor(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            if (!int.TryParse(name.Substring(name.LastIndexOf('_') + 1), out int seed))
                throw new InvalidDataException("Save filename has no world seed.");
            return seed;
        }

        private static string CheckpointPath(int seed) => Path.Combine(directory,
            "boatships_slot_" + Slot + "_seed_" + seed + ".json");

        public static bool WorldExists(int seed)
        {
            if (checkpoints.ContainsKey(seed) || File.Exists(CheckpointPath(seed)) ||
                File.Exists(CheckpointPath(seed) + ".bak")) return true;
            if (Slot != 0) return false;
            foreach (string prefix in new[] { "boatships_port_seed_", "boatships_ship_v1_",
                "boatships_crew_v1_", "boatships_crew_progression_v1_" })
                if (File.Exists(Path.Combine(directory, prefix + seed + ".json")) ||
                    File.Exists(Path.Combine(directory, prefix + seed + ".json.bak"))) return true;
            return false;
        }

        private static Checkpoint Load(int seed)
        {
            if (checkpoints.TryGetValue(seed, out Checkpoint cached)) return cached;
            string path = CheckpointPath(seed);
            Checkpoint loaded = null;
            if (File.Exists(path) || File.Exists(path + ".bak"))
            {
                Exception failure = null;
                foreach (string candidate in new[] { path, path + ".bak" })
                {
                    if (!File.Exists(candidate)) continue;
                    try
                    {
                        loaded = JsonUtility.FromJson<Checkpoint>(File.ReadAllText(candidate));
                        if (loaded == null || loaded.version != 1 || loaded.seed != seed ||
                            loaded.slot != Slot || loaded.documents == null)
                            throw new InvalidDataException("Checkpoint version, seed or slot mismatch.");
                        var names = new HashSet<string>();
                        foreach (Document document in loaded.documents)
                            if (document == null || string.IsNullOrEmpty(document.json) || !names.Add(document.name))
                                throw new InvalidDataException("Checkpoint contains invalid documents.");
                        failure = null;
                        break;
                    }
                    catch (Exception ex) { failure = ex; loaded = null; }
                }
                if (failure != null) throw new InvalidDataException("Checkpoint and backup cannot be read.", failure);
            }
            if (loaded == null)
            {
                loaded = new Checkpoint { seed = seed, slot = Slot };
                // Slot 1 imports existing M8–M14 worlds once. Other slots start independently.
                if (Slot == 0)
                    foreach (string prefix in new[] { "boatships_port_seed_", "boatships_ship_v1_",
                        "boatships_crew_v1_", "boatships_crew_progression_v1_" })
                    {
                        foreach (string suffix in new[] { ".json", ".json.bak" })
                        {
                            string legacy = Path.Combine(directory, prefix + seed + suffix);
                            if (File.Exists(legacy)) loaded.documents.Add(new Document
                                { name = Path.GetFileName(legacy), json = File.ReadAllText(legacy) });
                        }
                    }
            }
            checkpoints.Add(seed, loaded);
            return loaded;
        }

        public static bool Exists(string path)
        {
            if (!Enabled) return File.Exists(path);
            try
            {
                foreach (Document document in Load(SeedFor(path)).documents)
                    if (document.name == Path.GetFileName(path)) return true;
                return false;
            }
            catch (Exception)
            {
                // Let the caller's guarded Read report an unreadable save and lock its subsystem.
                return true;
            }
        }

        public static string Read(string path)
        {
            if (!Enabled) return File.ReadAllText(path);
            foreach (Document document in Load(SeedFor(path)).documents)
                if (document.name == Path.GetFileName(path)) return document.json;
            throw new FileNotFoundException("No document in this save slot.", path);
        }

        public static void Write(string path, string json)
        {
            if (!Enabled) { AtomicWrite(path, json); return; }
            int seed = SeedFor(path);
            Checkpoint checkpoint = Load(seed);
            string name = Path.GetFileName(path);
            Document target = checkpoint.documents.Find(document => document.name == name);
            if (target == null) { target = new Document { name = name }; checkpoint.documents.Add(target); }
            target.json = json;
            dirty.Add(seed);
        }

        public static bool Commit(bool background = false)
        {
            WaitForBackgroundWrite();
            try
            {
                // Serialize on the main thread (JsonUtility is not thread safe); only disk I/O moves off it.
                var paths = new List<string>(dirty.Count);
                var contents = new List<string>(dirty.Count);
                var seeds = new List<int>(dirty);
                foreach (int seed in seeds)
                {
                    paths.Add(CheckpointPath(seed));
                    contents.Add(JsonUtility.ToJson(checkpoints[seed]));
                }
                dirty.Clear();
                if (background && seeds.Count > 0)
                {
                    pendingSeeds = seeds;
                    pendingWrite = Task.Run(() => { for (int i = 0; i < paths.Count; i++) AtomicWrite(paths[i], contents[i]); });
                    Status = "Saving checkpoint to slot " + (Slot + 1) + "…";
                    return true;
                }
                try { for (int i = 0; i < paths.Count; i++) AtomicWrite(paths[i], contents[i]); }
                catch { foreach (int seed in seeds) dirty.Add(seed); throw; }
                Status = "Checkpoint saved to slot " + (Slot + 1);
                return true;
            }
            catch (Exception ex)
            {
                Status = "Checkpoint save failed. Retry required. " + ex.Message;
                Debug.LogWarning(Status);
                return false;
            }
        }

        private static void AtomicWrite(string path, string json)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
    }
}
