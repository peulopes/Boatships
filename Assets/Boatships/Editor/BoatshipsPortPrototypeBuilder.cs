#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Boatships.Editor
{
    /// <summary>Copies an existing M7 scene into a new M8 scene, then adds port services/UI.</summary>
    public static class BoatshipsPortPrototypeBuilder
    {
        private const string Root = "Assets/Boatships";

        [MenuItem("Tools/Boatships/Create Ports and Services Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string source = FindArchipelagoScene();
            if (string.IsNullOrEmpty(source))
            {
                EditorUtility.DisplayDialog("Boatships Milestone 8",
                    "Please create your Milestone 7 scene first: Tools > Boatships > Create Procedural Archipelago Scene.", "OK");
                return;
            }
            string destination = AssetDatabase.GenerateUniqueAssetPath(
                Root + "/Scenes/Boatships_PortsPrototype.unity");
            if (!AssetDatabase.CopyAsset(source, destination))
            {
                EditorUtility.DisplayDialog("Boatships Milestone 8", "Could not copy the M7 scene.", "OK");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(destination, OpenSceneMode.Single);
            GameObject ship = GameObject.Find("PlayerShip");
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            ArchipelagoWorld world = Object.FindFirstObjectByType<ArchipelagoWorld>();
            if (ship == null || canvas == null || world == null)
            {
                EditorUtility.DisplayDialog("Boatships Milestone 8",
                    "The copied scene is missing PlayerShip, Canvas or ArchipelagoWorld.", "OK");
                return;
            }
            ShipCustomization yard = ship.GetComponent<ShipCustomization>();
            VoxelHull hull = ship.GetComponentInChildren<VoxelHull>(true);
            ShipFlooding flooding = ship.GetComponent<ShipFlooding>();
            if (yard == null || hull == null || flooding == null)
            {
                EditorUtility.DisplayDialog("Boatships Milestone 8",
                    "M8 requires the M3 shipyard and M6 flooding components on PlayerShip. Rebuild M7 from your M6 scene.", "OK");
                return;
            }
            GameObject services = new GameObject("Port Services (M8)");
            PortEconomy economy = services.AddComponent<PortEconomy>();
            PortDocking docking = services.AddComponent<PortDocking>();
            docking.Configure(world, ship.transform, ship.GetComponent<ShipArcadeController>(), yard,
                ship.GetComponent<WeaponGroupCombat>(), flooding, hull, economy);

            GameObject interfaceRoot = NewUI("PortInterface (M8)", canvas.transform);
            Stretch(interfaceRoot.GetComponent<RectTransform>());
            PortDemoUI ui = interfaceRoot.AddComponent<PortDemoUI>();
            ui.Configure(docking, economy);
            MakeVicinityPanel(interfaceRoot.transform);
            MakePortOverlay(interfaceRoot.transform);
            MakeShipyardReturn(interfaceRoot.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuild(destination);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = services;
            EditorUtility.DisplayDialog("Boatships Milestone 8",
                "Created new port scene:\n" + destination +
                "\n\nPlay > Finish / Sail, press O or tap Jump to Port, then E or Dock. " +
                "Visit the Store, Tavern and Shipyard. Your M7 scene remains available.", "OK");
            Debug.Log("Boatships: Created M8 port scene " + destination);
        }

        private static void MakeVicinityPanel(Transform root)
        {
            GameObject panel = NewUI("VicinityPanel", root, typeof(Image));
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(770f, 115f);
            rect.anchoredPosition = new Vector2(0f, 18f);
            panel.GetComponent<Image>().color = new Color(0.04f, 0.12f, 0.15f, 0.87f);
            Text hint = NewLabel("HintText", panel.transform, 22, 0f);
            RectTransform t = hint.rectTransform;
            t.anchorMin = new Vector2(0f, 0f);
            t.anchorMax = new Vector2(0.62f, 1f);
            t.offsetMin = new Vector2(14f, 6f);
            t.offsetMax = new Vector2(-6f, -6f);
            hint.text = "Port nearby...";
            hint.alignment = TextAnchor.MiddleLeft;
            PlaceButton(panel.transform, "DockButton", "Dock (E)", 0.63f, 0.80f);
            PlaceButton(panel.transform, "JumpButton", "Jump to Port (O)", 0.80f, 0.99f);
        }

        private static void PlaceButton(Transform parent, string name, string caption, float left, float right)
        {
            GameObject go = NewUI(name, parent, typeof(Image), typeof(Button));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(left, 0.18f);
            rect.anchorMax = new Vector2(right, 0.82f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.18f, 0.43f, 0.47f, 1f);
            go.GetComponent<Button>().targetGraphic = image;
            Text text = NewLabel("Label", go.transform, 19, 0);
            Stretch(text.rectTransform);
            text.alignment = TextAnchor.MiddleCenter;
            text.text = caption;
        }

        private static void MakePortOverlay(Transform root)
        {
            GameObject overlay = NewUI("PortOverlay", root, typeof(Image));
            Stretch(overlay.GetComponent<RectTransform>());
            overlay.GetComponent<Image>().color = new Color(0.02f, 0.07f, 0.10f, 0.78f);
            GameObject content = NewUI("Content", overlay.transform, typeof(Image), typeof(VerticalLayoutGroup));
            RectTransform rect = content.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(900f, 810f);
            content.GetComponent<Image>().color = new Color(0.07f, 0.17f, 0.21f, 1f);
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            NewLabel("Title", content.transform, 35, 55f).text = "PORT";
            NewLabel("Account", content.transform, 22, 48f).text = "Gold / Supplies / Crew";
            GameObject tabs = NewRow("TabRow", content.transform, 56f);
            NewButton("TabStore", "Store", tabs.transform);
            NewButton("TabTavern", "Tavern", tabs.transform);
            NewButton("TabRepair", "Repairs", tabs.transform);
            Text details = NewLabel("Details", content.transform, 20, 124f);
            details.text = "Port services";
            for (int i = 0; i < 4; i++)
                NewButton("Action" + i, "Action " + i, content.transform, 56f);
            Text messages = NewLabel("Message", content.transform, 20, 98f);
            messages.text = "Welcome!";
            GameObject footer = NewRow("FooterRow", content.transform, 64f);
            NewButton("OpenShipyard", "Customize Ship", footer.transform);
            NewButton("DepartButton", "Depart Harbor (E)", footer.transform);
            overlay.SetActive(false);
        }

        private static void MakeShipyardReturn(Transform root)
        {
            GameObject panel = NewUI("ShipyardReturnPanel", root, typeof(Image));
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(20f, -20f);
            rect.sizeDelta = new Vector2(340f, 75f);
            panel.GetComponent<Image>().color = new Color(0.06f, 0.16f, 0.19f, 0.94f);
            Button back = NewButton("BackButton", "Back to Port", panel.transform);
            Stretch(back.GetComponent<RectTransform>());
            panel.SetActive(false);
        }

        private static GameObject NewRow(string name, Transform parent, float height)
        {
            GameObject go = NewUI(name, parent, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().preferredHeight = height;
            HorizontalLayoutGroup group = go.GetComponent<HorizontalLayoutGroup>();
            group.spacing = 9f;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = true;
            return go;
        }

        private static Text NewLabel(string name, Transform parent, int fontSize, float height)
        {
            GameObject go = height > 0f ?
                NewUI(name, parent, typeof(Text), typeof(LayoutElement)) :
                NewUI(name, parent, typeof(Text));
            if (height > 0f) go.GetComponent<LayoutElement>().preferredHeight = height;
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = new Color(0.95f, 0.97f, 0.92f, 1f);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Button NewButton(string name, string caption, Transform parent, float height = 0f)
        {
            GameObject go = height > 0f ?
                NewUI(name, parent, typeof(Image), typeof(Button), typeof(LayoutElement)) :
                NewUI(name, parent, typeof(Image), typeof(Button));
            if (height > 0f) go.GetComponent<LayoutElement>().preferredHeight = height;
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.16f, 0.39f, 0.43f, 1f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            Text text = NewLabel("Label", go.transform, 21, 0f);
            Stretch(text.rectTransform);
            text.alignment = TextAnchor.MiddleCenter;
            text.text = caption;
            return button;
        }

        private static GameObject NewUI(string name, Transform parent, params System.Type[] types)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            foreach (System.Type type in types) result.AddComponent(type);
            return result;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static string FindArchipelagoScene()
        {
            const string preferred = Root + "/Scenes/Boatships_ArchipelagoPrototype.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(preferred) != null) return preferred;
            foreach (string guid in AssetDatabase.FindAssets("Boatships_ArchipelagoPrototype t:Scene",
                new[] { Root + "/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Boatships_ArchipelagoPrototype")) return path;
            }
            return null;
        }

        private static void AddToBuild(string path)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene s in scenes)
                if (s.path == path) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
