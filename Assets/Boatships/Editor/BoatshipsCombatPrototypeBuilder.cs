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
    /// Clones the working Milestone 3 shipyard scene. No existing scene/script is modified.
    /// </summary>
    public static class BoatshipsCombatPrototypeBuilder
    {
        private const string Root = "Assets/Boatships";

        [MenuItem("Tools/Boatships/Create Cannon Combat Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string sourceScene = FindShipyardScene();
            if (string.IsNullOrEmpty(sourceScene))
            {
                EditorUtility.DisplayDialog("Boatships", "Please run Tools > Boatships > Create Ship Customization Scene first.", "OK");
                return;
            }
            string targetPath = AssetDatabase.GenerateUniqueAssetPath(Root + "/Scenes/Boatships_CombatPrototype.unity");
            if (!AssetDatabase.CopyAsset(sourceScene, targetPath))
            {
                EditorUtility.DisplayDialog("Boatships", "Unable to copy shipyard scene.", "OK");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(targetPath, OpenSceneMode.Single);
            GameObject player = GameObject.Find("PlayerShip");
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            ShipCustomization shipyard = player != null ? player.GetComponent<ShipCustomization>() : null;
            VoxelHull playerHull = player != null ? player.GetComponentInChildren<VoxelHull>(true) : null;
            OceanWaves ocean = Object.FindFirstObjectByType<OceanWaves>();
            if (player == null || canvas == null || shipyard == null || playerHull == null || ocean == null ||
                playerHull.Definition == null)
            {
                EditorUtility.DisplayDialog("Boatships", "Missing PlayerShip, Canvas, Ocean, Hull, or ShipCustomization.\nPlease recreate and test the Milestone 3 scene first.", "OK");
                return;
            }

            // The old H/click damage harness must not be allowed to destroy ships during combat.
            HullDamageDemo oldDemo = player.GetComponent<HullDamageDemo>();
            if (oldDemo != null) Object.DestroyImmediate(oldDemo);
            SerializedObject serializedShipyard = new SerializedObject(shipyard);
            SerializedProperty demoReference = serializedShipyard.FindProperty("damageDemo");
            if (demoReference != null)
            {
                demoReference.objectReferenceValue = null;
                serializedShipyard.ApplyModifiedPropertiesWithoutUndo();
            }

            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                IsometricFollowCamera oldCamera = mainCam.GetComponent<IsometricFollowCamera>();
                if (oldCamera != null) Object.DestroyImmediate(oldCamera);
                CombatIsometricCamera newCamera = mainCam.gameObject.AddComponent<CombatIsometricCamera>();
                newCamera.Target = player.transform;
            }

            Material targetMaterial = EnsureMaterial("PracticeTargetRed", new Color(0.58f, 0.18f, 0.11f));
            Material cannonballMaterial = EnsureMaterial("CannonballIron", new Color(0.07f, 0.08f, 0.10f));
            Material trajectoryMaterial = EnsureMaterial("TrajectoryCyan", new Color(0.12f, 0.95f, 0.83f));

            ShipCombatTarget playerTarget = player.AddComponent<ShipCombatTarget>();
            playerTarget.Configure(playerHull, false);
            ShipCombatTarget dummy = CreatePracticeTarget(playerHull.Definition, targetMaterial);

            GameObject aimObject = CreateUI("Combat Aim Joystick", canvas.transform, typeof(Image), typeof(VirtualJoystick));
            RectTransform aimBackground = aimObject.GetComponent<RectTransform>();
            aimBackground.anchorMin = new Vector2(1f, 0f);
            aimBackground.anchorMax = new Vector2(1f, 0f);
            aimBackground.pivot = new Vector2(0.5f, 0.5f);
            aimBackground.anchoredPosition = new Vector2(-175f, 170f);
            aimBackground.sizeDelta = new Vector2(225f, 225f);
            aimObject.GetComponent<Image>().color = new Color(0.24f, 0.9f, 0.9f, 0.25f);

            GameObject handleObject = CreateUI("Handle", aimObject.transform, typeof(Image));
            RectTransform handle = handleObject.GetComponent<RectTransform>();
            handle.anchorMin = new Vector2(0.5f, 0.5f);
            handle.anchorMax = handle.anchorMin;
            handle.pivot = handle.anchorMin;
            handle.anchoredPosition = Vector2.zero;
            handle.sizeDelta = new Vector2(84f, 84f);
            Image handleGraphic = handleObject.GetComponent<Image>();
            handleGraphic.color = new Color(0.4f, 1f, 1f, 0.75f);
            handleGraphic.raycastTarget = false;
            VirtualJoystick aimJoystick = aimObject.GetComponent<VirtualJoystick>();
            aimJoystick.Background = aimBackground;
            aimJoystick.Handle = handle;

            GameObject aimPathObject = new GameObject("Cannon Aim Trajectory", typeof(LineRenderer));
            LineRenderer aimLine = aimPathObject.GetComponent<LineRenderer>();
            aimLine.useWorldSpace = true;
            aimLine.positionCount = 0;
            aimLine.startWidth = 0.06f;
            aimLine.endWidth = 0.015f;
            aimLine.sharedMaterial = trajectoryMaterial;
            aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            aimLine.receiveShadows = false;

            WeaponGroupCombat combat = player.AddComponent<WeaponGroupCombat>();
            combat.Configure(shipyard, playerHull, playerTarget, ocean, aimJoystick, aimLine, cannonballMaterial);
            MakeCombatUI(canvas.transform, combat, dummy, aimObject);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;
            Debug.Log("Boatships Milestone 4 scene: " + targetPath);
            EditorUtility.DisplayDialog("Boatships", "Combat test scene created:\n" + targetPath +
                "\n\nPlay > Finish / Sail > Group 2 > aim toward red target with J/L and I/K or right joystick > Space/Fire.", "OK");
        }

        private static ShipCombatTarget CreatePracticeTarget(HullDefinition definition, Material material)
        {
            GameObject target = new GameObject("Practice Target Ship");
            target.transform.position = new Vector3(13f, 0.55f, 0f);
            target.transform.rotation = Quaternion.Euler(0f, -15f, 0f);
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.size = new Vector3(2.4f, 1.0f, 4.8f);

            GameObject visual = new GameObject("VoxelHull", typeof(MeshFilter), typeof(MeshRenderer));
            visual.transform.SetParent(target.transform, false);
            VoxelHull hull = visual.AddComponent<VoxelHull>();
            hull.Configure(definition, material);

            GameObject mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.name = "Target Marker";
            mast.transform.SetParent(target.transform, false);
            mast.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            mast.transform.localScale = new Vector3(0.09f, 1.15f, 0.09f);
            mast.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(mast.GetComponent<Collider>());

            ShipCombatTarget receiver = target.AddComponent<ShipCombatTarget>();
            receiver.Configure(hull, true);
            return receiver;
        }

        private static void MakeCombatUI(Transform canvas, WeaponGroupCombat combat,
            ShipCombatTarget practice, GameObject aimJoystick)
        {
            GameObject fullArea = CreateUI("Combat HUD", canvas);
            RectTransform rect = fullArea.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            GameObject groupRow = CreateUI("Weapon Groups", fullArea.transform,
                typeof(HorizontalLayoutGroup));
            RectTransform rowRect = groupRow.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.5f, 1f);
            rowRect.anchorMax = rowRect.anchorMin;
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.anchoredPosition = new Vector2(-110f, -18f);
            rowRect.sizeDelta = new Vector2(570f, 62f);
            HorizontalLayoutGroup layout = groupRow.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            Button[] groups = new Button[4];
            for (int i = 0; i < groups.Length; ++i)
                groups[i] = MakeButton(groupRow.transform, "Group " + (i + 1), "G" + (i + 1),
                    new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, new Vector2(0f, 0f));

            Text status = MakeLabel(fullArea.transform, "Combat Status", 19);
            RectTransform statusRect = status.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.5f, 1f);
            statusRect.anchorMax = statusRect.anchorMin;
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(-100f, -86f);
            statusRect.sizeDelta = new Vector2(740f, 95f);
            status.alignment = TextAnchor.UpperCenter;

            Button fire = MakeButton(fullArea.transform, "Fire Button", "FIRE",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-420f, 155f), new Vector2(133f, 133f));
            fire.GetComponent<Image>().color = new Color(0.75f, 0.27f, 0.13f, 0.92f);

            Button next = MakeButton(fullArea.transform, "Choose Cannon", "Next Cannon",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 95f), new Vector2(180f, 58f));
            Button assign = MakeButton(fullArea.transform, "Assign Cannon", "Assign to Group",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(195f, 95f), new Vector2(185f, 58f));
            Button reset = MakeButton(fullArea.transform, "Reset Practice Target", "Reset Target",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-195f, 95f), new Vector2(180f, 58f));

            // Place the HUD script on the always-active Canvas, NOT the hidden combat panel.
            // Otherwise it could not re-enable combat UI after exiting the shipyard.
            WeaponCombatHUD hud = canvas.gameObject.AddComponent<WeaponCombatHUD>();
            hud.Configure(combat, practice, groups, fire, next, assign, reset, status, fullArea, aimJoystick);
        }

        private static Text MakeLabel(Transform parent, string name, int fontSize)
        {
            GameObject obj = CreateUI(name, parent, typeof(Text));
            Text label = obj.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.color = Color.white;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private static Button MakeButton(Transform parent, string name, string text,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 dimensions)
        {
            GameObject obj = CreateUI(name, parent, typeof(Image), typeof(Button));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            Image image = obj.GetComponent<Image>();
            image.color = new Color(0.08f, 0.29f, 0.41f, 0.94f);
            Button button = obj.GetComponent<Button>();
            button.targetGraphic = image;
            Text label = MakeLabel(obj.transform, "Label", 22);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(3f, 2f);
            labelRect.offsetMax = new Vector2(-3f, -2f);
            label.alignment = TextAnchor.MiddleCenter;
            label.text = text;
            return button;
        }

        private static GameObject CreateUI(string name, Transform parent, params System.Type[] components)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            foreach (System.Type c in components) obj.AddComponent(c);
            return obj;
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            const string dir = Root + "/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root, "Materials");
            string path = dir + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("Cannot find URP Lit shader.");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static string FindShipyardScene()
        {
            const string normal = Root + "/Scenes/Boatships_ShipyardPrototype.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(normal) != null) return normal;
            foreach (string guid in AssetDatabase.FindAssets("Boatships_ShipyardPrototype t:Scene", new[] { Root + "/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Boatships_ShipyardPrototype")) return path;
            }
            return null;
        }
    }
}
#endif
