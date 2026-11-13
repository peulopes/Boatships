#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Boatships.Editor
{
    /// <summary>Copies the known-good Milestone 6 scene and adds an archipelago.
    /// Nothing in M1–M6 is overwritten. Requires the generated M6 demo scene.</summary>
    public static class BoatshipsArchipelagoPrototypeBuilder
    {
        private const string Root = "Assets/Boatships";

        [MenuItem("Tools/Boatships/Create Procedural Archipelago Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string source = FindFloodingScene();
            if (string.IsNullOrEmpty(source))
            {
                EditorUtility.DisplayDialog("Boatships Milestone 7",
                    "First create a Milestone 6 scene using Tools > Boatships > Create Flooding and Sinking Scene.", "OK");
                return;
            }
            string destination = AssetDatabase.GenerateUniqueAssetPath(
                Root + "/Scenes/Boatships_ArchipelagoPrototype.unity");
            if (!AssetDatabase.CopyAsset(source, destination))
            {
                EditorUtility.DisplayDialog("Boatships Milestone 7", "Unable to copy the Milestone 6 scene.", "OK");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(destination, OpenSceneMode.Single);
            GameObject player = GameObject.Find("PlayerShip");
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (player == null || canvas == null)
            {
                EditorUtility.DisplayDialog("Boatships Milestone 7",
                    "The copied scene needs PlayerShip and Canvas. Recreate Milestone 6 and retry.", "OK");
                return;
            }

            Material sand = GetOrCreateMaterial("Island_Sand", new Color(0.78f, 0.66f, 0.42f));
            Material grass = GetOrCreateMaterial("Island_Grass", new Color(0.21f, 0.48f, 0.22f));
            Material rock = GetOrCreateMaterial("Island_Rock", new Color(0.43f, 0.43f, 0.39f));
            Material bark = GetOrCreateMaterial("Island_Bark", new Color(0.32f, 0.22f, 0.13f));
            Material leaves = GetOrCreateMaterial("Island_Leaves", new Color(0.13f, 0.40f, 0.20f));
            Material stone = GetOrCreateMaterial("Island_Stone", new Color(0.37f, 0.36f, 0.34f));
            Material port = GetOrCreateMaterial("Island_PortMarker", new Color(0.95f, 0.85f, 0.25f));
            Material pirate = GetOrCreateMaterial("Island_PirateMarker", new Color(0.78f, 0.13f, 0.12f));
            Material treasure = GetOrCreateMaterial("Island_TreasureMarker", new Color(0.63f, 0.27f, 0.81f));

            GameObject root = new GameObject("Archipelago (seed-generated / streamed)");
            ArchipelagoWorld world = root.AddComponent<ArchipelagoWorld>();
            world.Configure(player.transform, sand, grass, rock, bark, leaves, stone,
                port, pirate, treasure);
            ArchipelagoNavigationLimiter limiter = player.AddComponent<ArchipelagoNavigationLimiter>();
            limiter.Configure(world);

            // Keep M6 combat, hull damage, and flooding in the new scene, but
            // move its pirate away from the spawn so exploration isn't disrupted.
            GameObject pirateShip = GameObject.Find("Pirate Enemy Ship");
            if (pirateShip != null)
                pirateShip.transform.position = new Vector3(155f, 0.65f, -120f);

            // Reference the new world via a small on-screen debug UI; all controls
            // work with touch and desktop (G / T / Home).
            Text readout = NewLabel(canvas.transform, "Archipelago Status");
            RectTransform hud = readout.rectTransform;
            hud.anchorMin = new Vector2(1f, 1f);
            hud.anchorMax = hud.anchorMin;
            hud.pivot = new Vector2(1f, 1f);
            hud.anchoredPosition = new Vector2(-22f, -25f);
            hud.sizeDelta = new Vector2(730f, 183f);
            readout.fontSize = 20;
            readout.alignment = TextAnchor.UpperRight;
            readout.text = "Seed-generated archipelago initializing…";

            GameObject row = new GameObject("Archipelago Debug Controls", typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            row.transform.SetParent(canvas.transform, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(1f, 1f);
            rowRect.anchorMax = rowRect.anchorMin;
            rowRect.pivot = new Vector2(1f, 1f);
            rowRect.anchoredPosition = new Vector2(-22f, -200f);
            rowRect.sizeDelta = new Vector2(710f, 48f);
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            Button reseed = NewButton(row.transform, "New Seed (+1)");
            Button jump = NewButton(row.transform, "Jump to Island");
            Button home = NewButton(row.transform, "Return Home");
            ArchipelagoDebugHUD debug = canvas.gameObject.AddComponent<ArchipelagoDebugHUD>();
            debug.Configure(world, player.transform, readout, reseed, jump, home);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddSceneToBuild(destination);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            Debug.Log("Boatships Milestone 7 scene created: " + destination);
            EditorUtility.DisplayDialog("Boatships Milestone 7",
                "Created procedural archipelago scene:\n" + destination +
                "\n\nPlay > Finish / Sail. Sail north to find the first island. " +
                "G: new seed; T: jump to nearest shoreline; Home: return to start. " +
                "Previous scenes remain untouched.", "OK");
        }

        private static string FindFloodingScene()
        {
            const string preferred = Root + "/Scenes/Boatships_FloodingPrototype.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(preferred) != null) return preferred;
            foreach (string guid in AssetDatabase.FindAssets("Boatships_FloodingPrototype t:Scene",
                new[] { Root + "/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Boatships_FloodingPrototype")) return path;
            }
            return null;
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result != null) return result;
            if (!AssetDatabase.IsValidFolder(Root + "/Materials"))
                AssetDatabase.CreateFolder(Root, "Materials");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new System.InvalidOperationException("URP/Lit shader missing. Use a Unity Universal 3D project.");
            result = new Material(shader) { name = name };
            result.SetColor("_BaseColor", color);
            result.SetFloat("_Smoothness", 0.05f);
            AssetDatabase.CreateAsset(result, path);
            return result;
        }

        private static Text NewLabel(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = new Color(0.96f, 0.97f, 0.91f);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button NewButton(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.11f, 0.35f, 0.38f, 0.94f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            Text caption = NewLabel(go.transform, "Label");
            caption.alignment = TextAnchor.MiddleCenter;
            caption.text = name;
            caption.fontSize = 17;
            RectTransform r = caption.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            return button;
        }

        private static void AddSceneToBuild(string path)
        {
            List<EditorBuildSettingsScene> all = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene s in all)
                if (s.path == path) return;
            all.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = all.ToArray();
        }
    }
}
#endif
