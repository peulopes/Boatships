#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Boatships.Editor
{
    /// <summary>
    /// M12 is additive and never modifies older source files. A copy of the currently
    /// opened scene is the safest place to test; there is also an explicit in-place option.
    /// </summary>
    public static class BoatshipsCrewMilestoneBuilder
    {
        [MenuItem("Tools/Boatships/Create Crew Management Test Scene")]
        public static void CreateCopy()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Boatships M12", "Exit Play Mode first.", "OK");
                return;
            }
            Scene original = SceneManager.GetActiveScene();
            if (!original.IsValid() || string.IsNullOrEmpty(original.path))
            {
                EditorUtility.DisplayDialog("Boatships M12", "Open and save your working M11/M10 gameplay scene first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string folder = "Assets/Boatships/Scenes";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                EditorUtility.DisplayDialog("Boatships M12", "Expected scene folder Assets/Boatships/Scenes is missing.", "OK");
                return;
            }
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Boatships_M12_CrewManagement.unity");
            if (!AssetDatabase.CopyAsset(original.path, path))
            {
                EditorUtility.DisplayDialog("Boatships M12", "Could not copy active scene. No scene modified.", "OK");
                return;
            }
            Scene copy = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            if (!Install(copy, out string error))
            {
                EditorUtility.DisplayDialog("Boatships M12", "Copied scene, but crew setup failed:\n" + error +
                    "\n\nOriginal scene remains untouched.", "OK");
                return;
            }
            EditorUtility.DisplayDialog("Boatships M12", "Created a new Crew Management scene:\n" + path +
                "\n\nPress Play and recruit sailors at the tavern. F6 selects crew; F7 assigns duty.\n" +
                "Existing M11 ship saving and M10 missions are left unchanged.", "OK");
        }

        [MenuItem("Tools/Boatships/Attach Crew Management To Current Scene")]
        public static void InstallCurrent()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Boatships M12", "Exit Play Mode first.", "OK");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Boatships M12", "Open and save the scene first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!Install(scene, out string message))
            {
                EditorUtility.DisplayDialog("Boatships M12", message, "OK");
                return;
            }
            EditorUtility.DisplayDialog("Boatships M12", "Crew management attached to " + scene.path + ".", "OK");
        }

        private static bool Install(Scene scene, out string message)
        {
            GameObject player = GameObject.Find("PlayerShip");
            ArchipelagoWorld world = Object.FindFirstObjectByType<ArchipelagoWorld>();
            PortEconomy economy = Object.FindFirstObjectByType<PortEconomy>();
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            VoxelHull hull = player != null ? player.GetComponentInChildren<VoxelHull>(true) : null;
            ShipCustomization shipyard = player != null ? player.GetComponent<ShipCustomization>() : null;
            ShipFlooding flooding = player != null ? player.GetComponent<ShipFlooding>() : null;
            if (player == null || world == null || economy == null || canvas == null || hull == null)
            {
                message = "Missing PlayerShip, ArchipelagoWorld, PortEconomy, Canvas, or VoxelHull. " +
                    "Open the integrated M10/M11 scene and try again. Nothing installed.";
                return false;
            }

            BoatshipsCrewManager manager = player.GetComponent<BoatshipsCrewManager>();
            if (manager == null) manager = player.AddComponent<BoatshipsCrewManager>();
            manager.Configure(world, economy, hull, shipyard, flooding);

            BoatshipsCrewHUD hud = Object.FindFirstObjectByType<BoatshipsCrewHUD>();
            if (hud == null)
            {
                GameObject root = NewUI("M12 Crew HUD", canvas.transform, typeof(Image));
                RectTransform r = root.GetComponent<RectTransform>();
                r.anchorMin = new Vector2(1f, 1f);
                r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(1f, 1f);
                r.anchoredPosition = new Vector2(-18f, -20f);
                r.sizeDelta = new Vector2(430f, 235f);
                root.GetComponent<Image>().color = new Color(0.04f, 0.12f, 0.16f, 0.91f);

                Text label = NewText("Crew Status", root.transform);
                RectTransform textRect = label.rectTransform;
                textRect.anchorMin = new Vector2(0f, 0.31f);
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(14f, 8f);
                textRect.offsetMax = new Vector2(-12f, -10f);
                label.fontSize = 16;

                Button select = NewButton("Select Crew", "Select Crew (F6)", root.transform,
                    new Vector2(0.03f, 0.03f), new Vector2(0.49f, 0.30f));
                Button assign = NewButton("Change Duty", "Next Duty (F7)", root.transform,
                    new Vector2(0.51f, 0.03f), new Vector2(0.97f, 0.30f));
                hud = root.AddComponent<BoatshipsCrewHUD>();
                hud.Configure(manager, label, select, assign);
            }
            else
            {
                // Existing HUD retains its references; don't create duplicates.
                // The current player should also be the source in an ordinary single-ship scene.
                Text label = hud.transform.Find("Crew Status") != null ?
                    hud.transform.Find("Crew Status").GetComponent<Text>() : null;
                Button select = hud.transform.Find("Select Crew") != null ?
                    hud.transform.Find("Select Crew").GetComponent<Button>() : null;
                Button assign = hud.transform.Find("Change Duty") != null ?
                    hud.transform.Find("Change Duty").GetComponent<Button>() : null;
                hud.Configure(manager, label, select, assign);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;
            message = "Crew manager successfully installed.";
            Debug.Log("Boatships M12: Installed crew management in " + scene.path);
            return true;
        }

        private static GameObject NewUI(string name, Transform parent, params System.Type[] components)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            foreach (System.Type component in components) go.AddComponent(component);
            return go;
        }

        private static Text NewText(string name, Transform parent)
        {
            GameObject go = NewUI(name, parent, typeof(Text));
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Button NewButton(string name, string label, Transform parent,
            Vector2 min, Vector2 max)
        {
            GameObject go = NewUI(name, parent, typeof(Image), typeof(Button));
            RectTransform r = go.GetComponent<RectTransform>();
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = new Color(0.17f, 0.37f, 0.44f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = img;
            Text caption = NewText("Caption", go.transform);
            RectTransform textRect = caption.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            caption.alignment = TextAnchor.MiddleCenter;
            caption.text = label;
            caption.fontSize = 18;
            return button;
        }
    }
}
#endif
