#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Boatships.Editor
{
    /// <summary>Copies the working M8 port scene; all previous scenes are preserved.</summary>
    public static class BoatshipsLootPrototypeBuilder
    {
        private const string Root = "Assets/Boatships";

        [MenuItem("Tools/Boatships/Create Inventory and Loot Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string source = FindPortScene();
            if (string.IsNullOrEmpty(source))
            {
                EditorUtility.DisplayDialog("Boatships Milestone 9",
                    "Create the Milestone 8 Ports and Services scene first.", "OK");
                return;
            }
            string destination = AssetDatabase.GenerateUniqueAssetPath(
                Root + "/Scenes/Boatships_InventoryLootPrototype.unity");
            if (!AssetDatabase.CopyAsset(source, destination))
            {
                EditorUtility.DisplayDialog("Boatships Milestone 9", "Could not copy M8 port scene.", "OK");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(destination, OpenSceneMode.Single);
            GameObject player = GameObject.Find("PlayerShip");
            GameObject pirate = GameObject.Find("Pirate Enemy Ship");
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            ArchipelagoWorld world = Object.FindFirstObjectByType<ArchipelagoWorld>();
            PortEconomy economy = Object.FindFirstObjectByType<PortEconomy>();
            PortDocking docking = Object.FindFirstObjectByType<PortDocking>();
            OceanWaves ocean = Object.FindFirstObjectByType<OceanWaves>();
            if (player == null || pirate == null || canvas == null || world == null ||
                economy == null || docking == null || ocean == null ||
                player.GetComponent<ShipCustomization>() == null ||
                player.GetComponent<ShipCombatTarget>() == null ||
                pirate.GetComponent<ShipCombatTarget>() == null ||
                pirate.GetComponent<ShipFlooding>() == null)
            {
                EditorUtility.DisplayDialog("Boatships Milestone 9",
                    "The source M8 scene lacks required M4–M8 components (player, pirate, " +
                    "combat targets, pirate flooding, port services, ocean, or Canvas).", "OK");
                return;
            }

            // Optional economy is now active for this scene only. It is not bound
            // in previous scenes, so their old free-placement demo still works.
            ShipCustomization customization = player.GetComponent<ShipCustomization>();
            customization.BindInventory(economy, docking);

            GameObject manager = new GameObject("Battle Loot System (M9)");
            BattleLootSystem loot = manager.AddComponent<BattleLootSystem>();
            loot.Configure(world, economy, docking, player.GetComponent<ShipCombatTarget>(),
                pirate.GetComponent<ShipCombatTarget>(), pirate.GetComponent<ShipFlooding>(),
                customization, ocean);

            GameObject uiRoot = new GameObject("Battle Loot HUD (M9)", typeof(RectTransform));
            uiRoot.transform.SetParent(canvas.transform, false);
            RectTransform uiRect = uiRoot.GetComponent<RectTransform>();
            uiRect.anchorMin = new Vector2(0f, 1f);
            uiRect.anchorMax = uiRect.anchorMin;
            uiRect.pivot = new Vector2(0f, 1f);
            uiRect.anchoredPosition = new Vector2(18f, -135f);
            uiRect.sizeDelta = new Vector2(560f, 208f);

            GameObject panel = NewUI("Panel", uiRoot.transform, typeof(Image));
            Stretch(panel.GetComponent<RectTransform>());
            panel.GetComponent<Image>().color = new Color(0.04f, 0.13f, 0.16f, 0.88f);

            Text status = NewText(panel.transform, "LootInfo", 18);
            RectTransform textRect = status.rectTransform;
            textRect.anchorMin = new Vector2(0f, 0.33f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -8f);
            status.text = "Waiting for battle...";

            Button collect = NewButton(panel.transform, "CollectButton", "Collect Loot (C)",
                new Vector2(0.04f, 0.02f), new Vector2(0.49f, 0.31f));
            Button jump = NewButton(panel.transform, "JumpButton", "Jump to Pirate/Loot (Y)",
                new Vector2(0.51f, 0.02f), new Vector2(0.97f, 0.31f));
            BattleLootHUD hud = uiRoot.AddComponent<BattleLootHUD>();
            hud.Configure(loot, docking, economy, status, collect, jump);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuild(destination);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = manager;
            Debug.Log("Boatships: Created Milestone 9 scene " + destination);
            EditorUtility.DisplayDialog("Boatships Milestone 9",
                "Created scene:\n" + destination +
                "\n\nFinish / Sail, press Y to jump near the pirate, defeat it, press Y " +
                "to jump near its salvage, then C to collect. Dock at port to buy " +
                "components and install from stock.", "OK");
        }

        private static GameObject NewUI(string name, Transform parent, params System.Type[] types)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            foreach (System.Type type in types) go.AddComponent(type);
            return go;
        }

        private static Text NewText(Transform parent, string name, int size)
        {
            GameObject go = NewUI(name, parent, typeof(Text));
            Text label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size;
            label.color = new Color(0.95f, 0.97f, 0.92f);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }

        private static Button NewButton(Transform parent, string name, string title,
            Vector2 minAnchor, Vector2 maxAnchor)
        {
            GameObject go = NewUI(name, parent, typeof(Image), typeof(Button));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = minAnchor;
            rect.anchorMax = maxAnchor;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.17f, 0.42f, 0.45f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            Text label = NewText(go.transform, "Label", 18);
            Stretch(label.rectTransform);
            label.alignment = TextAnchor.MiddleCenter;
            label.text = title;
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static string FindPortScene()
        {
            const string preferred = Root + "/Scenes/Boatships_PortsPrototype.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(preferred) != null) return preferred;
            foreach (string guid in AssetDatabase.FindAssets("Boatships_PortsPrototype t:Scene",
                new[] { Root + "/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Boatships_PortsPrototype")) return path;
            }
            return null;
        }

        private static void AddToBuild(string path)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene scene in scenes)
                if (scene.path == path) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
