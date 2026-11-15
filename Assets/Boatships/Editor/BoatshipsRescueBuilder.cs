#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Boatships.Editor
{
    public static class BoatshipsRescueBuilder
    {
        [MenuItem("Tools/Boatships/Create Shipwreck and Rescue Test Scene")]
        public static void CreateCopy()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Boatships M14", "Exit Play Mode first.", "OK");
                return;
            }
            Scene current = SceneManager.GetActiveScene();
            if (!current.IsValid() || string.IsNullOrEmpty(current.path))
            {
                EditorUtility.DisplayDialog("Boatships M14", "Open and save your working M13 scene first.", "OK");
                return;
            }
            BoatshipsCrewProgression progression = Object.FindAnyObjectByType<BoatshipsCrewProgression>();
            BattleLootSystem loot = Object.FindAnyObjectByType<BattleLootSystem>();
            if (progression == null || loot == null || Object.FindAnyObjectByType<Canvas>() == null)
            {
                EditorUtility.DisplayDialog("Boatships M14",
                    "Use the integrated M13 scene with crew progression, battle loot and a Canvas.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/Boatships/Scenes/Boatships_M14_ShipwreckRescue.unity");
            if (!AssetDatabase.CopyAsset(current.path, path))
            {
                EditorUtility.DisplayDialog("Boatships M14", "Could not copy the current scene.", "OK");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            progression = Object.FindAnyObjectByType<BoatshipsCrewProgression>();
            loot = Object.FindAnyObjectByType<BattleLootSystem>();
            loot.EnablePersistentWreck();
            BoatshipsRescueSystem existing = Object.FindAnyObjectByType<BoatshipsRescueSystem>();
            if (existing == null)
            {
                Canvas canvas = Object.FindAnyObjectByType<Canvas>();
                GameObject panel = new GameObject("M14 Shipwreck Rescue HUD", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(canvas.transform, false);
                RectTransform rect = panel.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(-18f, 18f);
                rect.sizeDelta = new Vector2(430f, 150f);
                panel.GetComponent<Image>().color = new Color(0.04f, 0.12f, 0.16f, 0.94f);
                Text label = MakeText("Rescue Status", panel.transform);
                SetRect(label.rectTransform, new Vector2(0.03f, 0.36f), new Vector2(0.97f, 0.97f));
                Button rescue = MakeButton("Rescue Survivor (F11)", panel.transform,
                    new Vector2(0.03f, 0.04f), new Vector2(0.49f, 0.32f));
                Button salvage = MakeButton("Collect Cargo (C)", panel.transform,
                    new Vector2(0.51f, 0.04f), new Vector2(0.97f, 0.32f));
                BoatshipsRescueSystem system = panel.AddComponent<BoatshipsRescueSystem>();
                system.Configure(Object.FindAnyObjectByType<ArchipelagoWorld>(),
                    Object.FindAnyObjectByType<PortEconomy>(), progression, progression.transform,
                    Object.FindAnyObjectByType<OceanWaves>(), loot, label, rescue, salvage);
            }
            BoatshipsCrewProgressionHUD crewHUD = Object.FindAnyObjectByType<BoatshipsCrewProgressionHUD>();
            if (crewHUD != null)
            {
                Transform recovery = crewHUD.transform.Find("Port Recovery");
                if (recovery != null)
                {
                    Text caption = recovery.GetComponentInChildren<Text>();
                    if (caption != null) caption.text = "Rescue Nearby (F11)";
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Boatships M14", "Created " + path +
                "\n\nF12 sends selected crew overboard. Sail within 9m and press F11 to rescue." +
                "\nDefeat the pirate and press C near its wreck to collect cargo." +
                "\nRestart Play Mode with the same world seed to test persistence.", "OK");
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Text MakeText(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 15;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        private static Button MakeButton(string title, Transform parent, Vector2 min, Vector2 max)
        {
            GameObject go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), min, max);
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.18f, 0.38f, 0.45f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            Text caption = MakeText("Caption", go.transform);
            caption.text = title;
            caption.alignment = TextAnchor.MiddleCenter;
            SetRect(caption.rectTransform, Vector2.zero, Vector2.one);
            return button;
        }
    }
}
#endif
