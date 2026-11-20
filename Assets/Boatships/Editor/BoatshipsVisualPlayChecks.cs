using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Boatships.Editor
{
    /// <summary>Batch regression checks for generated harbours and actual projectile impact visuals.</summary>
    [InitializeOnLoad]
    public static class BoatshipsVisualPlayChecks
    {
        private const string RunningKey = "Boatships.VisualPlayChecks";
        private const string LootOnlyKey = "Boatships.LootOnlyChecks";
        private static bool started;
        private static string runtimeError;

        static BoatshipsVisualPlayChecks()
        {
            if (SessionState.GetBool(RunningKey, false)) EditorApplication.update += AwaitPlay;
        }

        // Run in an isolated project: this replaces the current scene and exits the Editor.
        public static void BeginBatch() => Begin(false);
        public static void BeginLootBatch() => Begin(true);

        private static void Begin(bool lootOnly)
        {
            SessionState.SetBool(LootOnlyKey, lootOnly);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            started = false;
            runtimeError = null;
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

        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>("Assets/Boatships/Materials/" + name + ".mat");

        private static async void Run()
        {
            Application.logMessageReceived += ObserveLog;
            try
            {
                Time.timeScale = 1f;
                await WaitForSimulation(0.15f);
                bool lootOnly = SessionState.GetBool(LootOnlyKey, false);
                if (!lootOnly)
                {
                    CheckHarbours();
                    await CheckProjectiles();
                    await HarbourFortPlayChecks.Run();
                }
                await BoatshipsLootPlayChecks.Run();
                Check(string.IsNullOrEmpty(runtimeError), "Runtime error: " + runtimeError);
                File.WriteAllText(lootOnly ? "Library/boatships-loot-validation-passed.txt" : "Library/boatships-visual-validation-passed.txt",
                    lootOnly ? "Gameplay loot button, both loot sources, reward confirmation, immediate marker removal, range and docking gates, and saved duplicate protection." :
                    "Coastal towns and piers, impact particles and debris, fort retaliation and sinking, and on-screen loot collection passed.");
                Debug.Log(lootOnly ? "BOATSHIPS_LOOT_VALIDATION_PASSED" : "BOATSHIPS_VISUAL_VALIDATION_PASSED");
                SessionState.SetBool(RunningKey, false);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText("Library/boatships-visual-validation-failed.txt", ex + "\n" + runtimeError);
                Debug.LogException(ex);
                SessionState.SetBool(RunningKey, false);
                EditorApplication.Exit(1);
            }
            finally { Application.logMessageReceived -= ObserveLog; }
        }

        private static async Task WaitForSimulation(float seconds)
        {
            float target = Time.time + seconds;
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.time < target && Time.realtimeSinceStartup < deadline) await Task.Delay(25);
            Check(Time.time >= target, "Unity simulation stopped advancing.");
        }

        private static void CheckHarbours()
        {
            var bearings = new HashSet<int>();
            for (int i = 0; i < 9; i++)
            {
                var site = new ArchipelagoWorld.IslandSite {
                    Center = new Vector2(i * 120f, 120f), Radius = 21f + i,
                    Kind = IslandKind.PortSite, DetailSeed = 847291 + i * 7919,
                    Biome = (IslandBiome)(i % 3)
                };
                GameObject island = new GameObject("Port regression " + i);
                island.transform.position = new Vector3(site.Center.x, 0f, site.Center.y);
                island.AddComponent<ProceduralIslandChunk>().Build(site, 24, 847291,
                    Material("Island_Sand"), Material("Island_Grass"), Material("Island_Rock"),
                    Material("Island_Bark"), Material("Island_Leaves"), Material("Island_Stone"),
                    Material("Island_PortMarker"), Material("Island_PirateMarker"), Material("Island_TreasureMarker"));
                Transform harbour = island.transform.Find("Port Village and Pier");
                Check(harbour != null, "Port island has no harbour.");
                Check(harbour.localPosition.magnitude > site.Radius * 0.45f, "Harbour is in the island centre.");
                HarbourFort fort = harbour.GetComponentInChildren<HarbourFort>();
                Check(fort != null && fort.transform.Find("Fort Cannon 1/Muzzle") != null &&
                    fort.transform.Find("Fort Cannon 2/Muzzle") != null, "Port has no armed coastal fort.");
                bearings.Add(Mathf.RoundToInt(harbour.localEulerAngles.y));
                // The port's settlement is the walkable harbour town (it replaced the old block houses and pier).
                BoatshipsTown town = island.GetComponent<BoatshipsTown>();
                Check(town != null && town.Built && town.Doors.Count == 5, "Port has no walkable harbour town.");
                Transform townRoot = island.transform.Find("Port Town " + site.Cell);
                Check(townRoot != null, "Harbour town is not part of the island.");
                foreach (string shop in new[] { "Tavern", "General Store", "Shipwright", "Harbor Master" })
                {
                    Transform building = townRoot.Find(shop);
                    Check(building != null && building.Find("Walls") != null && building.Find("Roof") != null,
                        "Harbour town is missing its " + shop + ".");
                    Vector3 local = island.transform.InverseTransformPoint(building.position);
                    Check(new Vector2(local.x, local.z).magnitude > site.Radius * 0.15f,
                        "Town building placed in the island centre: island " + i + ", distance " +
                        new Vector2(local.x, local.z).magnitude + ", radius " + site.Radius);
                }
                Transform pier = townRoot.Find("Pier");
                Check(pier != null && pier.position.y > 0.3f, "Harbour town has no pier above the water.");
                Check(Vector2.Distance(town.Mooring, site.Center) > site.Radius * 0.78f + 3.2f,
                    "Town mooring is inside the ship navigation exclusion area.");
                Object.Destroy(island);
            }
            Check(bearings.Count >= 7, "Port sides are not varying with the island seed.");
            Debug.Log("BOATSHIPS_HARBOUR_CHECKS_PASSED");
        }

        private static async Task CheckProjectiles()
        {
            HullDefinition definition = AssetDatabase.LoadAssetAtPath<HullDefinition>("Assets/Boatships/Hulls/StarterSloop.asset");
            ShipCombatTarget first = CreateShip(definition, Vector3.up * 2f);
            ShipCombatTarget second = CreateShip(definition, new Vector3(12f, 2f, 0f));
            Check(Resources.Load<Material>("Boatships/CannonImpact") != null, "Impact material is missing from Resources.");
            int firstBlocks = first.RemainingVoxels;
            int secondBlocks = second.RemainingVoxels;
            Physics.SyncTransforms();
            Fire(new Vector3(-4f, 2f, 0f), second);
            Fire(new Vector3(8f, 2f, 0f), first);
            for (int attempt = 0; attempt < 40 && (first.RemainingVoxels == firstBlocks || second.RemainingVoxels == secondBlocks); attempt++)
                await Task.Delay(20);
            Check(first.RemainingVoxels < firstBlocks && second.RemainingVoxels < secondBlocks,
                "Swept cannon projectiles did not damage both hulls.");
            BoatshipsImpactEffect[] effects = Object.FindObjectsByType<BoatshipsImpactEffect>(FindObjectsSortMode.None);
            Check(effects.Length == 2, "Actual cannon impacts did not create explosion effects on both ships.");
            foreach (BoatshipsImpactEffect effect in effects)
            {
                ParticleSystem blast = effect.transform.Find("Fire and smoke")?.GetComponent<ParticleSystem>();
                ParticleSystem debris = effect.transform.Find("Flying hull fragments")?.GetComponent<ParticleSystem>();
                Check(blast != null && blast.particleCount > 0, "Impact has no live explosion particles.");
                Check(debris != null && debris.particleCount > 0, "Destroyed hull blocks produced no debris.");
                var particles = new ParticleSystem.Particle[48];
                int count = debris.GetParticles(particles);
                Check(count > 0 && particles[0].velocity.sqrMagnitude > 1f, "Debris is not flying away.");
                Vector3 before = particles[0].position;
                debris.Simulate(0.1f, false, false);
                debris.GetParticles(particles);
                Check(Vector3.Distance(before, particles[0].position) > 0.05f, "Debris particles do not move in world space.");
            }
            await WaitForSimulation(3.8f);
            Check(Object.FindObjectsByType<BoatshipsImpactEffect>(FindObjectsSortMode.None).Length == 0,
                "Impact effects did not clean themselves up.");
            Object.Destroy(first.gameObject);
            Object.Destroy(second.gameObject);
            Debug.Log("BOATSHIPS_PROJECTILE_VISUAL_CHECKS_PASSED");
        }

        private static ShipCombatTarget CreateShip(HullDefinition definition, Vector3 position)
        {
            GameObject ship = new GameObject("Visual test hull");
            ship.transform.position = position;
            VoxelHull hull = ship.AddComponent<VoxelHull>();
            hull.Configure(definition, Material("Wood"));
            BoxCollider collider = ship.AddComponent<BoxCollider>();
            collider.size = new Vector3(definition.Width, definition.Height, definition.Length) * definition.BlockSize;
            ShipCombatTarget target = ship.AddComponent<ShipCombatTarget>();
            target.Configure(hull, false);
            return target;
        }

        private static void Fire(Vector3 start, ShipCombatTarget owner)
        {
            BoatshipsProjectile projectile = BoatshipsProjectile.Spawn(start, Material("CannonballIron"));
            projectile.Launch(Vector3.right * 22f, owner, null, 90f, 0.45f, 0f);
        }

        private static void ObserveLog(string condition, string stack, LogType type)
        {
            if (stack.Contains("UnityEditor.Search.")) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                runtimeError = condition + "\n" + stack;
        }
    }
}
