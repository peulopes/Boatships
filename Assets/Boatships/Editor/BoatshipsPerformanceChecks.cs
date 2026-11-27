using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Boatships.Editor
{
    /// <summary>Batch performance benchmark for the generated world, ocean, shore checks and checkpoints.
    /// It only uses APIs that predate the performance pass, so the same file can be run on an older commit
    /// for a before/after comparison. Run in an isolated project copy: it replaces the scene and exits:
    /// <c>Unity -batchmode -projectPath . -executeMethod Boatships.Editor.BoatshipsPerformanceChecks.BeginBatch -logFile -</c>
    /// The report is written to Library/boatships-performance-report.txt.</summary>
    [InitializeOnLoad]
    public static class BoatshipsPerformanceChecks
    {
        private const string RunningKey = "Boatships.PerformanceChecks";
        private const string ReportPath = "Library/boatships-performance-report.txt";
        private static bool started;
        private static readonly StringBuilder report = new StringBuilder();

        static BoatshipsPerformanceChecks()
        {
            if (SessionState.GetBool(RunningKey, false)) EditorApplication.update += AwaitPlay;
        }

        [MenuItem("Boatships/Run Performance Benchmark")]
        public static void BeginBatch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            started = false;
            EditorApplication.update -= AwaitPlay;
            EditorApplication.update += AwaitPlay;
            EditorApplication.EnterPlaymode();
        }

        private static void AwaitPlay()
        {
            if (!EditorApplication.isPlaying || started) return;
            started = true;
            EditorApplication.update -= AwaitPlay;
            Run();
        }

        private static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>("Assets/Boatships/Materials/" + name + ".mat");

        private static async void Run()
        {
            report.Clear();
            Line("Boatships performance benchmark — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            Line("Unity " + Application.unityVersion + ", " + SystemInfo.processorType + ", batch " + Application.isBatchMode);
            bool ok = true;
            try
            {
                await NextFrame();
                await IslandBuilds();
                await StreamingThrash();
                ShoreChecks();
                OceanUpdate();
                Checkpoints();
            }
            catch (Exception ex)
            {
                ok = false;
                Line("FAILED: " + ex);
                Debug.LogException(ex);
            }
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log("BOATSHIPS_PERFORMANCE_REPORT\n" + report);
            SessionState.SetBool(RunningKey, false);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        private static void Line(string text) => report.AppendLine(text);

        private static async Task NextFrame()
        {
            int frame = Time.frameCount;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.frameCount == frame && Time.realtimeSinceStartup < deadline) await Task.Delay(1);
        }

        private static ArchipelagoWorld.IslandSite Site(int i, IslandKind kind) => new ArchipelagoWorld.IslandSite
        {
            Cell = new Vector2Int(i, 40), Center = new Vector2(i * 120f, 4800f), Radius = 21f + i % 7,
            Kind = kind, DetailSeed = 847291 + i * 7919, Biome = (IslandBiome)(i % 3)
        };

        private static GameObject BuildIsland(ArchipelagoWorld.IslandSite site)
        {
            GameObject island = new GameObject("Benchmark island " + site.Cell);
            island.transform.position = new Vector3(site.Center.x, 0f, site.Center.y);
            island.AddComponent<ProceduralIslandChunk>().Build(site, 28, 847291,
                Material("Island_Sand"), Material("Island_Grass"), Material("Island_Rock"),
                Material("Island_Bark"), Material("Island_Leaves"), Material("Island_Stone"),
                Material("Island_PortMarker"), Material("Island_PirateMarker"), Material("Island_TreasureMarker"));
            return island;
        }

        /// <summary>Build cost and the resulting scene weight of port and wild islands.</summary>
        private static async Task IslandBuilds()
        {
            foreach (IslandKind kind in new[] { IslandKind.PortSite, IslandKind.Deserted })
            {
                const int count = 8;
                var islands = new List<GameObject>();
                var times = new List<double>();
                for (int i = 0; i < count; i++)
                {
                    var watch = Stopwatch.StartNew();
                    islands.Add(BuildIsland(Site(i + (kind == IslandKind.PortSite ? 0 : 20), kind)));
                    times.Add(watch.Elapsed.TotalMilliseconds);
                }
                await NextFrame(); // deferred Destroy() calls land here
                await NextFrame();
                int objects = 0, renderers = 0, casters = 0, colliders = 0;
                var materials = new HashSet<Material>();
                foreach (GameObject island in islands)
                {
                    objects += island.GetComponentsInChildren<Transform>(true).Length;
                    colliders += island.GetComponentsInChildren<Collider>(true).Length;
                    foreach (Renderer renderer in island.GetComponentsInChildren<Renderer>(true))
                    {
                        renderers++;
                        if (renderer.shadowCastingMode != ShadowCastingMode.Off) casters++;
                        foreach (Material material in renderer.sharedMaterials) if (material != null) materials.Add(material);
                    }
                }
                times.Sort();
                Line(string.Format("Islands [{0}] x{1}: build median {2:0.0} ms, max {3:0.0} ms | per island: {4} objects, {5} renderers, {6} shadow casters, {7} colliders | {8} distinct materials",
                    kind, count, times[count / 2], times[count - 1], objects / count, renderers / count, casters / count, colliders / count, materials.Count));
                foreach (GameObject island in islands) Object.Destroy(island);
                await NextFrame();
            }
        }

        /// <summary>Sails a player marker back and forth over a cell edge and records the frame-time spikes.</summary>
        private static async Task StreamingThrash()
        {
            GameObject worldObject = new GameObject("Benchmark archipelago");
            worldObject.SetActive(false);
            ArchipelagoWorld world = worldObject.AddComponent<ArchipelagoWorld>();
            Transform player = new GameObject("Benchmark player").transform;
            world.Configure(player, Material("Island_Sand"), Material("Island_Grass"), Material("Island_Rock"),
                Material("Island_Bark"), Material("Island_Leaves"), Material("Island_Stone"),
                Material("Island_PortMarker"), Material("Island_PirateMarker"), Material("Island_TreasureMarker"));
            worldObject.SetActive(true);
            await NextFrame();
            await NextFrame();
            // Let the initial area finish streaming in before measuring.
            for (int i = 0; i < 20; i++) await NextFrame();
            float edge = world.CellSize * 0.5f;
            var frames = new List<float>();
            float total = 0f;
            for (int crossing = 0; crossing < 24; crossing++)
            {
                player.position = new Vector3(crossing % 2 == 0 ? edge + 2f : edge - 2f, 0f, 0f);
                for (int f = 0; f < 3; f++)
                {
                    float before = Time.realtimeSinceStartup;
                    await NextFrame();
                    float ms = (Time.realtimeSinceStartup - before) * 1000f;
                    frames.Add(ms);
                    total += ms;
                }
            }
            frames.Sort();
            Line(string.Format("Streaming: 24 cell-edge crossings: total {0:0} ms, worst frame {1:0.0} ms, 95th pct {2:0.0} ms, median {3:0.0} ms, loaded chunks {4}",
                total, frames[frames.Count - 1], frames[(int)(frames.Count * 0.95f)], frames[frames.Count / 2], world.LoadedChunkCount));
            Object.Destroy(worldObject);
            Object.Destroy(player.gameObject);
            await NextFrame();
            await NextFrame();
        }

        private static void ShoreChecks()
        {
            GameObject worldObject = new GameObject("Benchmark shore world");
            worldObject.SetActive(false);
            ArchipelagoWorld world = worldObject.AddComponent<ArchipelagoWorld>();
            world.Regenerate(847291); // islands only: no player, nothing streams
            var random = new System.Random(5);
            const int calls = 200000;
            float half = world.HalfExtent;
            var watch = Stopwatch.StartNew();
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < calls; i++)
            {
                var p = new Vector3(((float)random.NextDouble() * 2f - 1f) * half, 0f, ((float)random.NextDouble() * 2f - 1f) * half);
                sum += world.KeepOnWater(p, 4f);
            }
            Line(string.Format("Shore checks: {0} KeepOnWater calls over {1} islands: {2:0.0} ms ({3:0.000} µs/call) [checksum {4:0}]",
                calls, world.IslandCount, watch.Elapsed.TotalMilliseconds, watch.Elapsed.TotalMilliseconds * 1000.0 / calls, sum.x + sum.z));
            Object.Destroy(worldObject);
        }

        private static void OceanUpdate()
        {
            GameObject oceanObject = new GameObject("Benchmark ocean", typeof(MeshFilter), typeof(MeshRenderer));
            OceanWaves ocean = oceanObject.AddComponent<OceanWaves>();
            ocean.SetResolution(48);
            ocean.MeshUpdateInterval = 0f;
            MethodInfo update = typeof(OceanWaves).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            const int frames = 300;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) update.Invoke(ocean, null);
            Line(string.Format("Ocean: 48x48 mesh update: {0:0.000} ms per frame", watch.Elapsed.TotalMilliseconds / frames));
            Object.Destroy(oceanObject);
        }

        /// <summary>Main-thread cost of one checkpoint commit with campaign-sized documents.</summary>
        private static void Checkpoints()
        {
            string directory = Path.Combine(Path.GetTempPath(), "boatships-perf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                BoatshipsSaveStore.Initialize(0, directory);
                const int seed = 424242;
                var campaign = new CampaignProgress();
                for (int i = 0; i < 40; i++)
                {
                    var record = new EncounterRecord { id = "enc_" + i, tier = 1 + i % 4, defeated = false };
                    for (int v = 0; v < 30; v++) record.damage.Add(new ShipVoxelRecord { x = v % 9, y = v % 4, z = v % 19, health = 0.5f });
                    campaign.encounters.Add(record);
                }
                string economy = JsonUtility.ToJson(campaign, true);
                string[] names = { "boatships_port_seed_", "boatships_ship_v1_", "boatships_crew_v1_",
                    "boatships_crew_progression_v1_", "boatships_character_v1_", "boatships_worldmap_v1_" };
                MethodInfo commit = typeof(BoatshipsSaveStore).GetMethod("Commit", BindingFlags.Public | BindingFlags.Static);
                bool hasBackground = commit.GetParameters().Length == 1;
                MethodInfo poll = typeof(BoatshipsSaveStore).GetMethod("PollBackgroundWrite", BindingFlags.Public | BindingFlags.Static);
                foreach (bool background in hasBackground ? new[] { false, true } : new[] { false })
                {
                    const int commits = 20;
                    double mainThread = 0;
                    for (int c = 0; c < commits; c++)
                    {
                        foreach (string name in names)
                            BoatshipsSaveStore.Write(Path.Combine(directory, name + seed + ".json"), economy + c);
                        var watch = Stopwatch.StartNew();
                        bool result = (bool)commit.Invoke(null, hasBackground ? new object[] { background } : null);
                        mainThread += watch.Elapsed.TotalMilliseconds;
                        if (!result) throw new InvalidOperationException("Checkpoint commit failed: " + BoatshipsSaveStore.Status);
                        if (background)
                            while (!(bool)poll.Invoke(null, null)) System.Threading.Thread.Sleep(1);
                    }
                    long bytes = new FileInfo(Path.Combine(directory, "boatships_slot_0_seed_" + seed + ".json")).Length;
                    Line(string.Format("Checkpoint commit ({0}): {1:0.00} ms on the main thread per commit, file {2:0} KB",
                        background ? "background write" : "synchronous", mainThread / commits, bytes / 1024.0));
                }
                Line(hasBackground
                    ? "Checkpoint policy: one commit per burst (3 s debounce) + 25 s autosave"
                    : "Checkpoint policy: one commit on the frame after every transaction + 25 s autosave");
            }
            finally
            {
                BoatshipsSaveStore.Shutdown();
                try { Directory.Delete(directory, true); } catch (IOException) { }
            }
        }
    }
}
