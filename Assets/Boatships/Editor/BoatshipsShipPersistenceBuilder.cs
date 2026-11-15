#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Boatships.Editor
{
    /// <summary>
    /// M11 hotfix: install in the ACTUAL gameplay scene or create an isolated copy.
    /// Idempotent: rerunning the menu command updates existing references / UI.
    /// </summary>
    public static class BoatshipsShipPersistenceBuilder
    {
        private const string SceneFolder = "Assets/Boatships/Scenes";

        [MenuItem("Tools/Boatships/Attach Ship Save To Current Scene")]
        public static void AttachToCurrentScene()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Boatships M11", "Exit Play Mode before installing the save manager.", "OK");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Boatships M11", "Open and save the gameplay scene you want to use first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!Attach(scene, out string message))
            {
                EditorUtility.DisplayDialog("Boatships M11", message, "OK");
                return;
            }
            EditorUtility.DisplayDialog("Boatships M11",
                "Ship saving attached to your CURRENT scene:\n" + scene.path +
                "\n\nPress Play, install equipment, then F5 / Save Ship. " +
                "Check the HUD for SAVED <count> parts, stop Play Mode, and restart to see RESTORED <count> parts.", "OK");
        }

        [MenuItem("Tools/Boatships/Create Ship Persistence Test Scene")]
        public static void Create()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Boatships M11", "Exit Play Mode before creating the test scene.", "OK");
                return;
            }
            Scene current = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(current.path))
            {
                EditorUtility.DisplayDialog("Boatships M11", "Open and save your gameplay scene first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!AssetDatabase.IsValidFolder(SceneFolder))
            {
                EditorUtility.DisplayDialog("Boatships M11", "Missing Assets/Boatships/Scenes folder.", "OK");
                return;
            }
            string dest = AssetDatabase.GenerateUniqueAssetPath(SceneFolder + "/Boatships_ShipPersistenceTest.unity");
            if (!AssetDatabase.CopyAsset(current.path, dest))
            {
                EditorUtility.DisplayDialog("Boatships M11", "Could not copy the source scene.", "OK");
                return;
            }
            Scene copy = EditorSceneManager.OpenScene(dest, OpenSceneMode.Single);
            if (!Attach(copy, out string message))
            {
                EditorUtility.DisplayDialog("Boatships M11", message, "OK");
                return;
            }
            EditorUtility.DisplayDialog("Boatships M11",
                "Created and opened a SEPARATE ship-persistence scene:\n" + dest +
                "\n\nUse this scene for testing, or run 'Attach Ship Save To Current Scene' " +
                "on the gameplay scene you normally open.", "OK");
        }

        private static bool Attach(Scene scene, out string message)
        {
            GameObject player = GameObject.Find("PlayerShip");
            ArchipelagoWorld world = Object.FindFirstObjectByType<ArchipelagoWorld>();
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            ShipCustomization shipyard = player != null ? player.GetComponent<ShipCustomization>() : null;
            VoxelHull hull = player != null ? player.GetComponentInChildren<VoxelHull>(true) : null;
            if (player == null || world == null || canvas == null || shipyard == null || hull == null)
            {
                message = "Scene missing PlayerShip, ArchipelagoWorld, Canvas, ShipCustomization or VoxelHull. " +
                    "Nothing was installed; open your gameplay scene and retry.";
                return false;
            }

            BoatshipsShipSaveManager manager = Object.FindFirstObjectByType<BoatshipsShipSaveManager>();
            if (manager == null)
            {
                GameObject saveObject = new GameObject("Ship Save Manager (M11)");
                manager = saveObject.AddComponent<BoatshipsShipSaveManager>();
            }
            manager.Configure(world, shipyard, hull, player.GetComponent<WeaponGroupCombat>(),
                player.GetComponent<ShipFlooding>(), player.transform);

            BoatshipsShipSaveHUD hud = Object.FindFirstObjectByType<BoatshipsShipSaveHUD>();
            if (hud == null)
            {
                GameObject root = new GameObject("Ship Save HUD (M11)", typeof(RectTransform));
                root.transform.SetParent(canvas.transform, false);
                RectTransform rect = root.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(-16f, 16f);
                rect.sizeDelta = new Vector2(420f, 150f);

                GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(root.transform, false);
                Stretch(panel.GetComponent<RectTransform>());
                panel.GetComponent<Image>().color = new Color(0.04f, 0.12f, 0.16f, 0.91f);

                Text status = MakeText(panel.transform);
                RectTransform statusRect = status.rectTransform;
                statusRect.anchorMin = new Vector2(0f, 0.30f);
                statusRect.anchorMax = Vector2.one;
                statusRect.offsetMin = new Vector2(10f, 5f);
                statusRect.offsetMax = new Vector2(-10f, -4f);
                status.fontSize = 14;

                GameObject buttonObject = new GameObject("Save", typeof(RectTransform), typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(panel.transform, false);
                RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
                buttonRect.anchorMin = new Vector2(0.54f, 0.03f);
                buttonRect.anchorMax = new Vector2(0.97f, 0.31f);
                buttonRect.offsetMin = Vector2.zero;
                buttonRect.offsetMax = Vector2.zero;
                Image image = buttonObject.GetComponent<Image>();
                image.color = new Color(0.14f, 0.38f, 0.47f);
                Button button = buttonObject.GetComponent<Button>();
                button.targetGraphic = image;
                Text label = MakeText(buttonObject.transform);
                Stretch(label.rectTransform);
                label.text = "Save Ship";
                label.alignment = TextAnchor.MiddleCenter;
                hud = root.AddComponent<BoatshipsShipSaveHUD>();
                hud.Configure(manager, status, button);
            }
            else
            {
                hud.SetManager(manager);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = manager.gameObject;
            message = "Ship save installed successfully.";
            Debug.Log("Boatships M11: " + message + " Scene: " + scene.path);
            return true;
        }

        private static Text MakeText(Transform parent)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text uiText = go.GetComponent<Text>();
            uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            uiText.color = Color.white;
            uiText.alignment = TextAnchor.MiddleLeft;
            uiText.horizontalOverflow = HorizontalWrapMode.Wrap;
            uiText.verticalOverflow = VerticalWrapMode.Truncate;
            uiText.raycastTarget = false;
            return uiText;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
#endif
