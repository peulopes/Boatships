#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Boatships.Editor
{
    /// <summary>
    /// Makes an M13 copy of the current M12 scene or attaches M13 explicitly to it.
    /// No prior scene is overwritten by the Create command.
    /// </summary>
    public static class BoatshipsCrewProgressionBuilder
    {
        [MenuItem("Tools/Boatships/Create Advanced Crew Test Scene")]
        public static void CreateCopy()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Boatships M13", "Exit Play Mode first.", "OK");
                return;
            }
            Scene current = SceneManager.GetActiveScene();
            if (!current.IsValid() || string.IsNullOrEmpty(current.path))
            {
                EditorUtility.DisplayDialog("Boatships M13", "Open and save your working M12 scene first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string folder = "Assets/Boatships/Scenes";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                EditorUtility.DisplayDialog("Boatships M13", "Missing Assets/Boatships/Scenes folder.", "OK");
                return;
            }
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(folder + "/Boatships_M13_AdvancedCrew.unity");
            if (!AssetDatabase.CopyAsset(current.path, scenePath))
            {
                EditorUtility.DisplayDialog("Boatships M13", "Scene copy failed; original was not modified.", "OK");
                return;
            }
            Scene copy = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!Install(copy, out string info))
            {
                EditorUtility.DisplayDialog("Boatships M13", "Copied scene, but installation failed:\n" + info +
                    "\nOriginal scene is unchanged.", "OK");
                return;
            }
            EditorUtility.DisplayDialog("Boatships M13", "Advanced crew test scene created:\n" + scenePath +
                "\n\nF6 select crew, F7 duty, F8 injury test, F9 treatment at port, F10 +XP, F11 recovery at port, F12 overboard test.", "OK");
        }

        [MenuItem("Tools/Boatships/Attach Advanced Crew To Current Scene")]
        public static void InstallCurrent()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Boatships M13", "Exit Play Mode first.", "OK");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Boatships M13", "Open and save a valid M12 gameplay scene first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!Install(scene, out string message))
            {
                EditorUtility.DisplayDialog("Boatships M13", message, "OK");
                return;
            }
            EditorUtility.DisplayDialog("Boatships M13", "Advanced crew attached to " + scene.path, "OK");
        }

        private static bool Install(Scene scene, out string message)
        {
            GameObject player = GameObject.Find("PlayerShip");
            ArchipelagoWorld world = Object.FindFirstObjectByType<ArchipelagoWorld>();
            PortEconomy economy = Object.FindFirstObjectByType<PortEconomy>();
            BoatshipsCrewManager existingCrew = player != null ? player.GetComponent<BoatshipsCrewManager>() : null;
            WeaponGroupCombat weapons = player != null ? player.GetComponent<WeaponGroupCombat>() : null;
            ShipArcadeController controller = player != null ? player.GetComponent<ShipArcadeController>() : null;
            ShipCombatTarget target = player != null ? player.GetComponent<ShipCombatTarget>() : null;
            ShipFlooding flooding = player != null ? player.GetComponent<ShipFlooding>() : null;
            PortDocking docking = Object.FindFirstObjectByType<PortDocking>();
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (player == null || world == null || economy == null || existingCrew == null ||
                weapons == null || controller == null || target == null || canvas == null)
            {
                message = "Missing M12 CrewManager, player weapon controller, movement, combat target, " +
                          "world/economy or Canvas. Use your fully integrated M12 gameplay scene.";
                return false;
            }
            BoatshipsCrewProgression progression = player.GetComponent<BoatshipsCrewProgression>();
            if (progression == null) progression = player.AddComponent<BoatshipsCrewProgression>();
            progression.Configure(world, economy, existingCrew, weapons, controller, target, flooding, docking);

            BoatshipsCrewProgressionHUD hud = Object.FindFirstObjectByType<BoatshipsCrewProgressionHUD>();
            if (hud == null)
            {
                GameObject panel = NewUI("M13 Advanced Crew HUD", canvas.transform, typeof(Image));
                RectTransform r = panel.GetComponent<RectTransform>();
                r.anchorMin = Vector2.one;
                r.anchorMax = Vector2.one;
                r.pivot = Vector2.one;
                r.anchoredPosition = new Vector2(-18f, -266f); // Below M12 upper-right controls.
                r.sizeDelta = new Vector2(430f, 224f);
                panel.GetComponent<Image>().color = new Color(0.04f, 0.12f, 0.16f, 0.9f);

                Text label = NewText("Progression Status", panel.transform);
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0f, 0.29f);
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(12f, 5f);
                labelRect.offsetMax = new Vector2(-12f, -8f);
                label.fontSize = 15;

                Button treat = NewButton("Port Treatment", "Treat at Port (F9)", panel.transform,
                    new Vector2(0.03f, 0.03f), new Vector2(0.49f, 0.29f));
                Button rescue = NewButton("Port Recovery", "Recover Crew (F11)", panel.transform,
                    new Vector2(0.51f, 0.03f), new Vector2(0.97f, 0.29f));
                hud = panel.AddComponent<BoatshipsCrewProgressionHUD>();
                hud.Configure(progression, label, treat, rescue);
            }
            else
            {
                Transform root = hud.transform;
                Text label = root.Find("Progression Status") != null ? root.Find("Progression Status").GetComponent<Text>() : null;
                Button treat = root.Find("Port Treatment") != null ? root.Find("Port Treatment").GetComponent<Button>() : null;
                Button rescue = root.Find("Port Recovery") != null ? root.Find("Port Recovery").GetComponent<Button>() : null;
                hud.Configure(progression, label, treat, rescue);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;
            message = "M13 crew progression installed.";
            Debug.Log("Boatships M13 attached to " + scene.path);
            return true;
        }

        private static GameObject NewUI(string name, Transform parent, params System.Type[] types)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            foreach (System.Type type in types) go.AddComponent(type);
            return go;
        }

        private static Text NewText(string name, Transform parent)
        {
            Text text = NewUI(name, parent, typeof(Text)).GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Button NewButton(string name, string title, Transform parent, Vector2 min, Vector2 max)
        {
            GameObject go = NewUI(name, parent, typeof(Image), typeof(Button));
            RectTransform r = go.GetComponent<RectTransform>();
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.18f, 0.38f, 0.45f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            Text caption = NewText("Caption", go.transform);
            RectTransform t = caption.rectTransform;
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.offsetMin = Vector2.zero;
            t.offsetMax = Vector2.zero;
            caption.text = title;
            caption.fontSize = 16;
            caption.alignment = TextAnchor.MiddleCenter;
            return button;
        }
    }
}
#endif
