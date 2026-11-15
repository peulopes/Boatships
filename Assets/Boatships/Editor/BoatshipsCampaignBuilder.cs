#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Boatships.Editor
{
    public static class BoatshipsCampaignBuilder
    {
        [MenuItem("Tools/Boatships/Create Campaign Scene (M15-M22)")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            Scene current = SceneManager.GetActiveScene();
            if (!current.IsValid() || string.IsNullOrEmpty(current.path))
                throw new InvalidOperationException("Open and save the working M14 scene first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Boatships/Scenes/Boatships_Campaign.unity");
            if (!AssetDatabase.CopyAsset(current.path, path)) throw new IOException("Could not copy the current scene.");
            Scene copy = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Install(copy);
            EditorSceneManager.MarkSceneDirty(copy);
            EditorSceneManager.SaveScene(copy);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(scene => scene.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("Boatships campaign created at " + path + ". Open Campaign in Play Mode for the guide.");
        }

        private static void Install(Scene scene)
        {
            if (Object.FindAnyObjectByType<BoatshipsSaveCoordinator>() != null) return;
            ArchipelagoWorld world = Required<ArchipelagoWorld>();
            PortEconomy economy = Required<PortEconomy>();
            PortDocking docking = Required<PortDocking>();
            BoatshipsCrewProgression progression = Required<BoatshipsCrewProgression>();
            GameObject player = progression.gameObject;
            ShipCombatTarget target = player.GetComponent<ShipCombatTarget>();
            BoatshipsCrewManager crew = player.GetComponent<BoatshipsCrewManager>();
            ShipArcadeController movement = player.GetComponent<ShipArcadeController>();
            WeaponGroupCombat weapons = player.GetComponent<WeaponGroupCombat>();
            BoatshipsShipSaveManager shipSave = Object.FindAnyObjectByType<BoatshipsShipSaveManager>();
            EnemyShipAI template = Required<EnemyShipAI>();
            OceanWaves ocean = Required<OceanWaves>();
            Canvas canvas = Required<Canvas>();
            if (target == null || crew == null || movement == null || weapons == null)
                throw new InvalidOperationException("M13 player components are incomplete.");
            if (shipSave == null)
            {
                shipSave = player.AddComponent<BoatshipsShipSaveManager>();
                shipSave.Configure(world, player.GetComponent<ShipCustomization>(), target.Hull, weapons,
                    player.GetComponent<ShipFlooding>(), player.transform);
            }
            world.EnableCampaignBiomes();
            target.Hull.EnableStructuralIntegrity();
            GameObject host = new GameObject("Boatships Campaign");
            BoatshipsRescueSystem rescue = Object.FindAnyObjectByType<BoatshipsRescueSystem>();
            BattleLootSystem battleLoot = Required<BattleLootSystem>();
            battleLoot.EnablePersistentWreck();
            if (rescue == null)
            {
                rescue = host.AddComponent<BoatshipsRescueSystem>();
                rescue.Configure(world, economy, progression, player.transform, ocean, battleLoot, null, null, null);
            }
            BoatshipsConstruction construction = player.GetComponent<BoatshipsConstruction>();
            if (construction == null) construction = player.AddComponent<BoatshipsConstruction>();
            BoatshipsEncounterDirector director = host.AddComponent<BoatshipsEncounterDirector>();
            director.Configure(world, economy, target, template, ocean);
            BoatshipsBoarding boarding = host.AddComponent<BoatshipsBoarding>();
            boarding.Configure(director, progression, crew, movement, weapons, docking);
            BoatshipsCampaignJournal journal = host.AddComponent<BoatshipsCampaignJournal>();
            journal.Configure(world, economy, docking, target, weapons, movement);
            BoatshipsSaveCoordinator saves = host.AddComponent<BoatshipsSaveCoordinator>();
            saves.Configure(economy, shipSave, crew, progression, director);
            BoatshipsMobilePresentation presentation = host.AddComponent<BoatshipsMobilePresentation>();
            presentation.Configure(canvas, ocean, weapons, target);
            BoatshipsCampaignHUD hud = host.AddComponent<BoatshipsCampaignHUD>();
            hud.Configure(presentation, construction, director, boarding, journal, saves, economy, docking);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        }

        private static T Required<T>() where T : Object
        {
            T value = Object.FindAnyObjectByType<T>();
            if (value == null) throw new InvalidOperationException("The scene is missing " + typeof(T).Name + ". Use the working M14 scene.");
            return value;
        }

        [MenuItem("Tools/Boatships/Validate Campaign")]
        public static void ValidateCampaign()
        {
            Required<BoatshipsSaveCoordinator>(); Required<BoatshipsConstruction>();
            Required<BoatshipsEncounterDirector>(); Required<BoatshipsBoarding>();
            Required<BoatshipsCampaignJournal>(); Required<BoatshipsMobilePresentation>(); Required<BoatshipsCampaignHUD>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) > 0)
                        throw new InvalidOperationException(child.name + " contains a missing script.");
            }
            Debug.Log("Boatships campaign component and reference validation passed.");
        }

        [MenuItem("Tools/Boatships/Run Hull Regression Checks")]
        public static void RunHullChecks()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode to run hull checks.");
            VoxelHull source = Required<BoatshipsCrewProgression>().GetComponent<ShipCombatTarget>().Hull;
            GameObject firstObject = new GameObject("Hull validation A", typeof(MeshFilter), typeof(MeshRenderer));
            GameObject secondObject = new GameObject("Hull validation B", typeof(MeshFilter), typeof(MeshRenderer));
            try
            {
                VoxelHull first = firstObject.AddComponent<VoxelHull>();
                VoxelHull second = secondObject.AddComponent<VoxelHull>();
                first.Configure(source.Definition, source.GetComponent<Renderer>().sharedMaterial);
                second.Configure(source.Definition, source.GetComponent<Renderer>().sharedMaterial);
                int baseline = first.TotalBlocks;
                first.EnableStructuralIntegrity(); second.EnableStructuralIntegrity();
                HullDefinition definition = first.Definition;
                bool testedAddition = false;
                for (int x = 0; x < definition.Width && !testedAddition; x++)
                for (int y = 0; y < definition.Height && !testedAddition; y++)
                for (int z = 0; z < definition.Length && !testedAddition; z++)
                {
                    if (definition.ContainsBlock(x, y, z) || !first.CanEditCell(x, y, z, true)) continue;
                    first.TryEditCell(x, y, z, true);
                    second.RestoreVoxelDamage(first.CaptureVoxelDamage());
                    Require(second.HasBlock(x, y, z), "Added hull cell did not survive snapshot restoration.");
                    first.TryEditCell(x, y, z, false);
                    testedAddition = true;
                }
                Require(testedAddition, "No valid buildable cell was found.");
                int cx = definition.Width / 2, cy = definition.Height - 1, cz = definition.Length / 2;
                Require(first.TryEditCell(cx, cy, cz, false), "Could not edit a deck block.");
                second.RestoreVoxelDamage(first.CaptureVoxelDamage()); second.ResetHull();
                Require(!second.IsDesignedCell(cx, cy, cz) && !second.HasBlock(cx, cy, cz),
                    "Port repair replaced a removed design cell.");
                Vector3 removedCell = definition.BlockCenter(cx, cy, cz);
                Require(second.TryNearestDeckPoint(removedCell, out Vector3 station) &&
                    new Vector2(station.x - removedCell.x, station.z - removedCell.z).magnitude >= definition.BlockSize * 0.9f,
                    "A boarding station was placed over a missing deck cell.");
                first.ResetDesign();
                first.ApplyImpactWorld(first.transform.TransformPoint(definition.BlockCenter(cx, cy, cz)), 5f, 0.05f);
                second.RestoreVoxelDamage(first.CaptureVoxelDamage());
                Require(second.CaptureVoxelDamage().Count == first.CaptureVoxelDamage().Count, "Legacy damage restoration mismatch.");
                first.ResetDesign();
                for (int x = 0; x < definition.Width; x++)
                for (int y = 0; y < definition.Height; y++)
                    if (first.HasBlock(x, y, cz)) first.TryEditCell(x, y, cz, false);
                Require(first.RemainingBlocks < baseline * 0.65f, "Disconnected hull sections were retained.");
                Debug.Log("Boatships hull regression checks passed. Added cells, edited layouts, repairs, legacy damage and disconnected sections verified.");
            }
            finally { Object.DestroyImmediate(firstObject); Object.DestroyImmediate(secondObject); }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        [MenuItem("Tools/Boatships/Run Save Regression Checks")]
        public static void RunSaveChecks()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before save checks.");
            string directory = Path.GetFullPath("Temp/BoatshipsSaveChecks/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string account = Path.Combine(directory, "boatships_port_seed_1234.json");
            string ship = Path.Combine(directory, "boatships_ship_v1_1234.json");
            string checkpoint = Path.Combine(directory, "boatships_slot_0_seed_1234.json");
            try
            {
                File.WriteAllText(account, "{\"gold\":100}");
                File.WriteAllText(ship + ".bak", "{\"seed\":1234}");
                BoatshipsSaveStore.Initialize(0, directory);
                Require(BoatshipsSaveStore.Read(account).Contains("100"), "Legacy economy migration failed.");
                Require(BoatshipsSaveStore.Exists(ship + ".bak") && BoatshipsSaveStore.Read(ship + ".bak").Contains("1234"),
                    "Legacy ship backup migration failed.");
                BoatshipsSaveStore.Write(account, "{\"gold\":80}");
                BoatshipsSaveStore.Write(ship, "{\"seed\":1234}");
                Require(!File.Exists(checkpoint), "Checkpoint committed before the frame snapshot completed.");
                Require(BoatshipsSaveStore.Commit(), "Could not commit checkpoint.");
                BoatshipsSaveStore.Initialize(0, directory);
                Require(BoatshipsSaveStore.Read(account).Contains("80") && BoatshipsSaveStore.Exists(ship),
                    "World documents did not reload together.");
                BoatshipsSaveStore.Write(account, "{\"gold\":60}");
                Require(BoatshipsSaveStore.Commit(), "Second checkpoint failed.");
                File.WriteAllText(checkpoint, "corrupt");
                BoatshipsSaveStore.Initialize(0, directory);
                Require(BoatshipsSaveStore.Read(account).Contains("80"), "Backup checkpoint recovery failed.");
                BoatshipsSaveStore.Initialize(1, directory);
                Require(!BoatshipsSaveStore.Exists(account), "New slot imported another slot's account.");
                BoatshipsSaveStore.Write(account, "{\"gold\":500}"); BoatshipsSaveStore.Commit();
                BoatshipsSaveStore.Initialize(0, directory);
                Require(BoatshipsSaveStore.Read(account).Contains("80"), "Slot isolation failed.");
                Debug.Log("Boatships save regression checks passed. Migration, staged checkpoints, reload, backup recovery and slot isolation verified.");
            }
            finally { BoatshipsSaveStore.Shutdown(); }
        }

        public static void ValidateBatch()
        {
            string source = "Assets/Boatships/Scenes/Boatships_M12_CrewManagement.unity";
            EditorSceneManager.OpenScene(source, OpenSceneMode.Single);
            CreateScene();
            ValidateCampaign();
            RunHullChecks();
            RunSaveChecks();
            File.WriteAllText("Library/boatships-validation-passed.txt", SceneManager.GetActiveScene().path);
            Debug.Log("BOATSHIPS_BATCH_VALIDATION_PASSED");
        }

        [MenuItem("Tools/Boatships/Build Android Development APK")]
        public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/Android/Boatships.apk");

        public static void BuildAndroidBatch()
        {
            OpenLatestCampaign();
            BuildAndroid();
        }

        [MenuItem("Tools/Boatships/Export iOS Development Project")]
        public static void BuildIOS() => Build(BuildTarget.iOS, "Builds/iOS");

        public static void BuildIOSBatch()
        {
            OpenLatestCampaign();
            BuildIOS();
        }

        private static void OpenLatestCampaign()
        {
            string selected = null;
            DateTime latest = DateTime.MinValue;
            foreach (string guid in AssetDatabase.FindAssets("Boatships_Campaign t:Scene", new[] { "Assets/Boatships/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                DateTime modified = File.GetLastWriteTimeUtc(path);
                if (modified <= latest) continue;
                selected = path; latest = modified;
            }
            if (selected == null) throw new InvalidOperationException("Create a campaign scene first.");
            EditorSceneManager.OpenScene(selected, OpenSceneMode.Single);
        }

        private static void Build(BuildTarget target, string destination)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building.");
            ValidateCampaign();
            RunHullChecks();
            RunSaveChecks();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string scene = SceneManager.GetActiveScene().path;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            bool customKeystore = PlayerSettings.Android.useCustomKeystore;
            bool bundle = EditorUserBuildSettings.buildAppBundle;
            string productName = PlayerSettings.productName;
            try
            {
                PlayerSettings.productName = "Boatships";
                if (target == BuildTarget.Android)
                {
                    PlayerSettings.Android.useCustomKeystore = false;
                    EditorUserBuildSettings.buildAppBundle = false;
                }
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { scene }, locationPathName = destination,
                    target = target, options = BuildOptions.Development });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException(target + " development build failed. Check the Unity build report.");
                Debug.Log(target + " development build created at " + destination + ". Device validation is required.");
            }
            finally
            {
                PlayerSettings.Android.useCustomKeystore = customKeystore;
                EditorUserBuildSettings.buildAppBundle = bundle;
                PlayerSettings.productName = productName;
            }
        }
    }
}
#endif
