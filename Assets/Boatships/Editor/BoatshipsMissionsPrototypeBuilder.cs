#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Boatships.Editor
{
    public static class BoatshipsMissionsPrototypeBuilder
    {
        private const string SceneFolder = "Assets/Boatships/Scenes";
        private const string SourceName = "Boatships_InventoryLootPrototype";

        [MenuItem("Tools/Boatships/Create Missions and Rewards Scene")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string source = SceneFolder + "/" + SourceName + ".unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(source) == null)
            {
                string[] matches = AssetDatabase.FindAssets(SourceName + " t:Scene", new[] { SceneFolder });
                Array.Sort(matches, StringComparer.Ordinal);
                source = matches.Length == 0 ? null : AssetDatabase.GUIDToAssetPath(matches[0]);
            }
            if (source == null)
            {
                ReportError("Create the Milestone 9 Inventory and Loot scene first.");
                return;
            }

            string destination = AssetDatabase.GenerateUniqueAssetPath(
                SceneFolder + "/Boatships_MissionsRewardsPrototype.unity");
            if (!AssetDatabase.CopyAsset(source, destination))
            {
                ReportError("Could not copy the Milestone 9 scene.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(destination, OpenSceneMode.Single);
            var world = Object.FindAnyObjectByType<ArchipelagoWorld>();
            var docking = Object.FindAnyObjectByType<PortDocking>();
            var loot = Object.FindAnyObjectByType<BattleLootSystem>();
            var ui = Object.FindAnyObjectByType<PortDemoUI>();
            var shipyard = world != null && world.Player != null ?
                world.Player.GetComponent<ShipCustomization>() : null;
            var storeTab = ui != null ? ui.transform.Find("PortOverlay/Content/TabRow/TabStore") : null;
            var canvas = ui != null ? ui.GetComponentInParent<Canvas>() : null;
            if (world == null || docking == null || docking.Economy == null || loot == null ||
                shipyard == null || storeTab == null || canvas == null)
            {
                EditorSceneManager.OpenScene(source, OpenSceneMode.Single);
                AssetDatabase.DeleteAsset(destination);
                ReportError("The Milestone 9 scene is missing the expected player, port, loot, or UI setup.");
                return;
            }

            var manager = new GameObject("Port Missions (M10)").AddComponent<PortMissions>();
            manager.Configure(world, docking.Economy, docking, loot, shipyard);

            Transform tab = Object.Instantiate(storeTab, storeTab.parent);
            tab.name = "TabMissions";
            tab.GetComponentInChildren<Text>(true).text = "Missions";

            Image panel = MakeUI<Image>("Mission Tracker (M10)", canvas.transform);
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(18f, -358f);
            rect.sizeDelta = new Vector2(570f, 232f);
            panel.color = new Color(0.04f, 0.13f, 0.16f, 0.92f);
            panel.raycastTarget = false;

            Text tracker = MakeLabel("Objectives", panel.transform, TextAnchor.MiddleLeft);
            Place(tracker.rectTransform, new Vector2(0f, 0.26f), Vector2.one);
            Image jumpImage = MakeUI<Image>("Jump", panel.transform);
            Place(jumpImage.rectTransform, Vector2.zero, new Vector2(1f, 0.26f));
            jumpImage.color = new Color(0.17f, 0.42f, 0.45f);
            Button jump = jumpImage.gameObject.AddComponent<Button>();
            jump.targetGraphic = jumpImage;
            Text caption = MakeLabel("Label", jump.transform, TextAnchor.MiddleCenter);
            Place(caption.rectTransform, Vector2.zero, Vector2.one);
            caption.text = "Jump to tracked objective (J, test)";
            panel.gameObject.SetActive(false);
            ui.BindMissions(manager, tracker, jump);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                ReportError("Could not save the Milestone 10 scene.");
                return;
            }
            Selection.activeObject = manager.gameObject;
            Debug.Log("Boatships: Created Milestone 10 scene " + destination);
        }

        private static void ReportError(string message)
        {
            if (Application.isBatchMode) throw new InvalidOperationException(message);
            EditorUtility.DisplayDialog("Boatships Milestone 10", message, "OK");
        }

        private static T MakeUI<T>(string name, Transform parent) where T : Component
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent, false);
            return item.AddComponent<T>();
        }

        private static Text MakeLabel(string name, Transform parent, TextAnchor alignment)
        {
            Text text = MakeUI<Text>(name, parent);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = Color.white;
            text.fontSize = 19;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = new Vector2(10f, 4f);
            rect.offsetMax = new Vector2(-10f, -4f);
        }
    }
}
#endif
