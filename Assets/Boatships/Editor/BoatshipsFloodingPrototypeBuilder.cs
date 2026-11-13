#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Boatships.Editor
{
    /// <summary>
    /// Clones the existing Milestone 5 scene. Does not replace the scripts or
    /// overwrite any previous scenes, hull assets, or installed components.
    /// </summary>
    public static class BoatshipsFloodingPrototypeBuilder
    {
        private const string Root = "Assets/Boatships";

        [MenuItem("Tools/Boatships/Create Flooding and Sinking Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string source = FindEnemyScene();
            if (string.IsNullOrEmpty(source))
            {
                EditorUtility.DisplayDialog("Boatships Milestone 6",
                    "Create the Milestone 5 scene first using Tools > Boatships > Create Enemy AI Combat Scene.", "OK");
                return;
            }
            string destination = AssetDatabase.GenerateUniqueAssetPath(
                Root + "/Scenes/Boatships_FloodingPrototype.unity");
            if (!AssetDatabase.CopyAsset(source, destination))
            {
                EditorUtility.DisplayDialog("Boatships Milestone 6", "Unable to clone the enemy AI scene.", "OK");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(destination, OpenSceneMode.Single);
            GameObject player = GameObject.Find("PlayerShip");
            GameObject pirate = GameObject.Find("Pirate Enemy Ship");
            OceanWaves ocean = Object.FindFirstObjectByType<OceanWaves>();
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (player == null || pirate == null || ocean == null || canvas == null)
            {
                EditorUtility.DisplayDialog("Boatships Milestone 6",
                    "Missing PlayerShip, Pirate Enemy Ship, Ocean, or Canvas in the copied scene.", "OK");
                return;
            }

            ShipCombatTarget playerTarget = player.GetComponent<ShipCombatTarget>();
            ShipCombatTarget pirateTarget = pirate.GetComponent<ShipCombatTarget>();
            ShipArcadeController playerMovement = player.GetComponent<ShipArcadeController>();
            EnemyShipAI enemyMovement = pirate.GetComponent<EnemyShipAI>();
            WeaponGroupCombat weapons = player.GetComponent<WeaponGroupCombat>();
            ShipCustomization shipyard = player.GetComponent<ShipCustomization>();
            if (playerTarget == null || pirateTarget == null ||
                playerTarget.Hull == null || pirateTarget.Hull == null ||
                playerMovement == null || enemyMovement == null || weapons == null || shipyard == null)
            {
                EditorUtility.DisplayDialog("Boatships Milestone 6",
                    "The Milestone 5 ship components are missing. Recreate the Enemy AI scene and retry.", "OK");
                return;
            }

            // The earlier prototypes positioned the entire solid voxel hull just
            // above water. This scene lowers both hulls slightly to test real
            // below-waterline breaches without changing the old scenes/scripts.
            SetFloat(playerMovement, "floatOffset", 0.22f);
            SetFloat(enemyMovement, "heightAboveWaves", 0.22f);

            Transform playerRig = CreateFloodRig(player.transform);
            Transform pirateRig = CreateFloodRig(pirate.transform);
            ShipFlooding playerFlood = player.AddComponent<ShipFlooding>();
            playerFlood.Configure(playerTarget.Hull, ocean, playerRig,
                playerMovement, null, weapons, shipyard);
            ShipFlooding pirateFlood = pirate.AddComponent<ShipFlooding>();
            pirateFlood.Configure(pirateTarget.Hull, ocean, pirateRig,
                null, enemyMovement, null, shipyard);

            Text readout = NewLabel(canvas.transform, "Flooding Readout");
            RectTransform labelRect = readout.rectTransform;
            labelRect.anchorMin = new Vector2(0.5f, 1f);
            labelRect.anchorMax = labelRect.anchorMin;
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -188f);
            labelRect.sizeDelta = new Vector2(1150f, 80f);
            readout.fontSize = 18;
            readout.alignment = TextAnchor.UpperCenter;
            readout.text = "Milestone 6 flooding";

            GameObject controls = new GameObject("Flooding Controls", typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            controls.transform.SetParent(canvas.transform, false);
            RectTransform controlsRect = controls.GetComponent<RectTransform>();
            controlsRect.anchorMin = new Vector2(0.5f, 1f);
            controlsRect.anchorMax = controlsRect.anchorMin;
            controlsRect.pivot = new Vector2(0.5f, 1f);
            controlsRect.anchoredPosition = new Vector2(0f, -272f);
            controlsRect.sizeDelta = new Vector2(850f, 52f);
            HorizontalLayoutGroup layout = controls.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            Button breachPirate = NewButton(controls.transform, "Breach Pirate");
            Button breachPlayer = NewButton(controls.transform, "Breach Player");
            Button patch = NewButton(controls.transform, "Patch Player");
            Button reset = NewButton(controls.transform, "Reset Both Ships");

            ShipFloodingDemoHUD hud = canvas.gameObject.AddComponent<ShipFloodingDemoHUD>();
            hud.Configure(playerFlood, pirateFlood, playerTarget, pirateTarget,
                shipyard, readout, breachPirate, breachPlayer, patch, reset);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;
            Debug.Log("Boatships Milestone 6 scene: " + destination);
            EditorUtility.DisplayDialog("Boatships Milestone 6",
                "Created flooding test scene:\n" + destination +
                "\n\nPlay > Finish / Sail. B: damage pirate. N: damage player. P: emergency patch. R: reset both. " +
                "All previous prototype scenes remain untouched.", "OK");
        }

        private static Transform CreateFloodRig(Transform ship)
        {
            Transform visuals = ship.Find("ShipVisuals");
            if (visuals == null)
                throw new System.InvalidOperationException("Expected ShipVisuals beneath " + ship.name);
            GameObject rig = new GameObject("FloodingVisualRig");
            rig.transform.SetParent(ship, false);
            visuals.SetParent(rig.transform, false);
            return rig.transform;
        }

        private static void SetFloat(MonoBehaviour behaviour, string propertyName, float value)
        {
            SerializedObject serialized = new SerializedObject(behaviour);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.floatValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else Debug.LogWarning("Boatships: expected " + propertyName + " on " + behaviour.name);
        }

        private static string FindEnemyScene()
        {
            const string preferred = Root + "/Scenes/Boatships_EnemyAIPrototype.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(preferred) != null) return preferred;
            foreach (string guid in AssetDatabase.FindAssets("Boatships_EnemyAIPrototype t:Scene",
                new[] { Root + "/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Boatships_EnemyAIPrototype")) return path;
            }
            return null;
        }

        private static Text NewLabel(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.color = Color.white;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private static Button NewButton(Transform parent, string label)
        {
            GameObject go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image background = go.GetComponent<Image>();
            background.color = new Color(0.10f, 0.31f, 0.38f, 0.93f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = background;
            Text text = NewLabel(go.transform, "Label");
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            text.text = label;
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }
    }
}
#endif
