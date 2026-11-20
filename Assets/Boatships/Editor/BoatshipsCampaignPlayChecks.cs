#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Boatships.Editor
{
    [InitializeOnLoad]
    public static class BoatshipsCampaignPlayChecks
    {
        private const string RunningKey = "Boatships.PlayChecks";
        private static bool started;
        private static string runtimeError;

        static BoatshipsCampaignPlayChecks()
        {
            if (SessionState.GetBool(RunningKey, false)) EditorApplication.update += AwaitPlay;
        }

        public static void BeginBatch()
        {
            string source = null;
            if (File.Exists("Library/boatships-validation-passed.txt"))
                source = File.ReadAllText("Library/boatships-validation-passed.txt");
            else
            {
                DateTime latest = DateTime.MinValue;
                foreach (string guid in AssetDatabase.FindAssets("Boatships_Campaign t:Scene", new[] { "Assets/Boatships/Scenes" }))
                {
                    string candidate = AssetDatabase.GUIDToAssetPath(guid);
                    DateTime modified = File.GetLastWriteTimeUtc(candidate);
                    if (modified <= latest) continue;
                    source = candidate; latest = modified;
                }
            }
            if (source == null) throw new InvalidOperationException("Run ValidateBatch first.");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Boatships/Scenes/Boatships_PlayValidation.unity");
            if (!AssetDatabase.CopyAsset(source, path)) throw new IOException("Could not create the play validation scene.");
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            BoatshipsSaveCoordinator coordinator = Object.FindAnyObjectByType<BoatshipsSaveCoordinator>();
            SerializedObject serialized = new SerializedObject(coordinator);
            string saveDirectory = Path.GetFullPath("Temp/PlayChecks/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveDirectory);
            serialized.FindProperty("validationDirectory").stringValue = saveDirectory;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            buildScenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
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

        private static async void Run()
        {
            Application.logMessageReceived += ObserveLog;
            try
            {
                await Task.Delay(2500);
                RenderScreenshot();
                await Task.Delay(500);
                Time.timeScale = 1f;
                BoatshipsSaveCoordinator saves = Object.FindAnyObjectByType<BoatshipsSaveCoordinator>();
                PortEconomy economy = Object.FindAnyObjectByType<PortEconomy>();
                PortDocking docking = Object.FindAnyObjectByType<PortDocking>();
                BoatshipsConstruction construction = Object.FindAnyObjectByType<BoatshipsConstruction>();
                BoatshipsCrewManager crew = construction.GetComponent<BoatshipsCrewManager>();
                BoatshipsCampaignJournal journal = Object.FindAnyObjectByType<BoatshipsCampaignJournal>();
                BoatshipsEncounterDirector director = Object.FindAnyObjectByType<BoatshipsEncounterDirector>();
                BoatshipsBoarding boarding = Object.FindAnyObjectByType<BoatshipsBoarding>();
                Check(saves != null && economy.CanCheckpoint && construction.Hull != null, "Campaign did not initialize.");
                BoatshipsMobilePresentation presentation = Object.FindAnyObjectByType<BoatshipsMobilePresentation>();
                Transform startMenu = presentation.SafeRoot.Find("Boatships Main Menu");
                Check(startMenu != null && startMenu.gameObject.activeSelf, "The startup menu was missing.");
                startMenu.Find("Start Panel/Continue").GetComponent<Button>().onClick.Invoke();
                Check(!startMenu.gameObject.activeSelf, "Continue did not dismiss the startup menu.");
                Transform guide = presentation.SafeRoot.Find("Campaign Overlay/Campaign Panel/Scroll View/Content/Skip Guide");
                if (guide != null && guide.gameObject.activeInHierarchy) guide.GetComponent<Button>().onClick.Invoke();
                presentation.SafeRoot.Find("Campaign Menu").GetComponent<Button>().onClick.Invoke();
                presentation.SafeRoot.Find("Campaign Overlay/Campaign Panel/Navigation/Settings").GetComponent<Button>().onClick.Invoke();
                presentation.SafeRoot.Find("Campaign Overlay/Campaign Panel/Scroll View/Content/Main Menu").GetComponent<Button>().onClick.Invoke();
                int previousSeed = saves.WorldSeed;
                Check(saves.SaveCheckpoint(), "Could not save before starting a new game.");
                string previousAccount = Path.Combine(Application.persistentDataPath, "boatships_port_seed_" + previousSeed + ".json");
                string previousSnapshot = BoatshipsSaveStore.Read(previousAccount);
                startMenu.Find("Start Panel/New Game").GetComponent<Button>().onClick.Invoke();
                BoatshipsCharacterCreation creation = Object.FindAnyObjectByType<BoatshipsCharacterCreation>();
                Check(creation != null && creation.IsOpen, "New Game did not open character creation.");
                creation.BeginForTesting(CharacterBackground.MerchantsChild);
                await Task.Delay(1200);
                saves = Object.FindAnyObjectByType<BoatshipsSaveCoordinator>();
                economy = Object.FindAnyObjectByType<PortEconomy>();
                docking = Object.FindAnyObjectByType<PortDocking>();
                construction = Object.FindAnyObjectByType<BoatshipsConstruction>();
                crew = construction.GetComponent<BoatshipsCrewManager>();
                journal = Object.FindAnyObjectByType<BoatshipsCampaignJournal>();
                director = Object.FindAnyObjectByType<BoatshipsEncounterDirector>();
                boarding = Object.FindAnyObjectByType<BoatshipsBoarding>();
                Check(saves.WorldSeed != previousSeed && economy.ActiveSeed == saves.WorldSeed,
                    "New Game did not initialize a fresh world.");
                Check(economy.Gold == 650 && economy.Crew.Count == 0, "New Game retained earlier inventory or crew.");
                Check(BoatshipsSaveStore.Read(previousAccount) == previousSnapshot, "New Game changed the earlier campaign save.");
                docking.JumpToPortForTesting();
                await Task.Delay(350);
                docking.Dock();
                Check(docking.IsDocked, "Could not dock at the starting port. " + docking.Status +
                    " Distance " + docking.DistanceToPort + " / " + docking.DockingDistance);
                int gold = economy.Gold;
                journal.BuyUpgrade(0);
                Check(economy.Campaign.hullLevel == 1 && economy.Gold == gold - 80, "Upgrade purchase failed.");
                HullDefinition definition = construction.Hull.Definition;
                bool built = false;
                for (int x = 0; x < definition.Width && !built; x++)
                for (int y = 0; y < definition.Height && !built; y++)
                for (int z = 0; z < definition.Length && !built; z++)
                {
                    if (definition.ContainsBlock(x, y, z) || !construction.Hull.CanEditCell(x, y, z, true)) continue;
                    int wood = economy.Wood;
                    built = construction.Edit(x, y, z, true);
                    Check(built && economy.Wood == wood - 1, "Building did not consume exactly one wood.");
                }
                Check(built, "Could not build an adjacent hull cell.");
                Check(economy.Recruit(docking.DockedPort, 0), "Could not recruit a sailor.");
                journal.ContractAction();
                Check(economy.Campaign.contractAccepted, "Could not accept a campaign contract.");
                await Task.Delay(1500);
                Check(crew.ActiveCrewCount > 0 && crew.SelectedCrewKey != null, "Recruited crew did not appear.");
                for (int i = 0; i < 5 && crew.GetCrewDuty(crew.SelectedCrewKey) != CrewDuty.Fighter; i++) crew.CycleSelectedDuty();
                Check(crew.GetCrewDuty(crew.SelectedCrewKey) == CrewDuty.Fighter, "Fighter duty could not be assigned.");
                docking.Undock();
                BoatshipsTravelController travel = Object.FindAnyObjectByType<BoatshipsTravelController>();
                Check(travel != null && travel.Character.HasCharacter, "The created character was not applied.");
                travel.BeginTestBattle();
                await Task.Delay(1000);
                Check(travel.Mode == TravelMode.Battle, "The world-map battle did not start.");
                ShipCombatTarget target = director.NearestEnemy(out EncounterRecord encounter, out _);
                Check(target != null && encounter != null, "Repeatable encounters did not spawn.");
                ShipFlooding flooding = target.GetComponent<ShipFlooding>();
                if (flooding != null) flooding.enabled = false;
                var damage = new List<ShipVoxelRecord>();
                for (int x = 0; x < definition.Width; x++)
                for (int y = 0; y < definition.Height; y++)
                for (int z = 0; z < definition.Length / 2; z++)
                    if (definition.ContainsBlock(x, y, z)) damage.Add(new ShipVoxelRecord { x = x, y = y, z = z, health = 0f });
                target.Hull.RestoreVoxelDamage(damage);
                construction.GetComponent<Rigidbody>().position = target.transform.position + target.transform.right * 5f;
                encounter.defenderHealth = 1f;
                await Task.Delay(350);
                boarding.Begin();
                Check(boarding.InProgress, "Could not begin boarding a weakened nearby enemy. " + boarding.Status);
                await Task.Delay(5500);
                Check(economy.Campaign.captures == 1 && !boarding.InProgress, "Boarding did not capture the enemy vessel.");
                int beforeCargo = economy.Gold;
                director.CollectNearbyCargo();
                Check(economy.Gold > beforeCargo, "Captured cargo could not be collected.");
                int afterCargo = economy.Gold;
                director.CollectNearbyCargo();
                Check(economy.Gold == afterCargo, "Cargo collection duplicated rewards.");
                Check(saves.SaveCheckpoint(), "Unified campaign checkpoint failed: " + saves.Status);
                string shipPath = Object.FindAnyObjectByType<BoatshipsShipSaveManager>().SaveFilePath;
                ShipSaveRecord saved = JsonUtility.FromJson<ShipSaveRecord>(BoatshipsSaveStore.Read(shipPath));
                Check(saved.damagedVoxels.Exists(cell => cell.addedToDesign), "Built hull layout was missing from the checkpoint.");
                ShipFlooding ownFlood = construction.GetComponent<ShipFlooding>();
                construction.Hull.ApplyImpactWorld(construction.Hull.transform.TransformPoint(
                    definition.BlockCenter(definition.Width / 2, 0, definition.Length / 2)), 300f, 0.6f);
                ShipFloodRecord sinking = ownFlood.CaptureFlooding();
                sinking.isSinking = true;
                ownFlood.RestoreFlooding(sinking);
                await Task.Delay(800);
                construction.RecoverAtPort();
                await Task.Delay(350);
                Check(docking.IsDocked && !ownFlood.IsSinking, "A sunken flagship could not recover at a port. " +
                    construction.Status + " " + docking.Status);
                var survivors = new Dictionary<string, Vector3>();
                construction.GetComponent<BoatshipsCrewProgression>().GetSurvivors(survivors);
                Check(survivors.Count > 0, "Overboard crew did not remain at sea after flagship recovery.");
                Check(saves.SaveCheckpoint(), "Could not save the recovered flagship and survivors together.");
                Check(string.IsNullOrEmpty(runtimeError), "Runtime error: " + runtimeError);
                File.WriteAllText("Library/boatships-play-validation-passed.txt", "Upgrade, construction, recruiting, Fighter duty, contracts, encounters, boarding, capture, cargo deduplication, unified checkpoints, flagship recovery and survivor persistence passed.");
                Debug.Log("BOATSHIPS_PLAY_VALIDATION_PASSED");
                SessionState.SetBool(RunningKey, false);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText("Library/boatships-play-validation-failed.txt", ex.ToString() + "\n" + runtimeError);
                Debug.LogException(ex);
                SessionState.SetBool(RunningKey, false);
                EditorApplication.Exit(1);
            }
            finally { Application.logMessageReceived -= ObserveLog; }
        }

        private static void ObserveLog(string condition, string stack, LogType type)
        {
            if (stack.Contains("UnityEditor.Search.")) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                runtimeError = condition + "\n" + stack;
        }

        private static void RenderScreenshot()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            Canvas canvas = Object.FindAnyObjectByType<Canvas>();
            RenderTexture target = new RenderTexture(1280, 720, 24);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderMode previousMode = canvas.renderMode;
            Camera previousCamera = canvas.worldCamera;
            float previousDistance = canvas.planeDistance;
            Texture2D screenshot = null;
            try
            {
                target.Create();
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.5f;
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera,
                    new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                screenshot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                screenshot.Apply();
                File.WriteAllBytes("Library/BoatshipsCampaign.png", screenshot.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = previousMode;
                canvas.worldCamera = previousCamera;
                canvas.planeDistance = previousDistance;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release(); Object.Destroy(target);
                if (screenshot != null) Object.Destroy(screenshot);
            }
        }
    }
}
#endif
