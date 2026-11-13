#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Boatships.Editor
{
    /// <summary>
    /// Clones the working Milestone 2 scene, adds a modular shipyard and UI,
    /// and leaves previous test scenes and scripts unchanged.
    /// </summary>
    public static class BoatshipsShipyardPrototypeBuilder
    {
        private const string Root = "Assets/Boatships";
        private const string VoxelSource = Root + "/Scenes/Boatships_VoxelPrototype.unity";

        [MenuItem("Tools/Boatships/Create Ship Customization Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string baseScene = FindVoxelScene();
            if (string.IsNullOrEmpty(baseScene))
            {
                EditorUtility.DisplayDialog("Boatships", "No Milestone 2 scene found. Run Tools > Boatships > Create Voxel Hull Test Scene first.", "OK");
                return;
            }
            EnsureFolder("Equipment");
            EnsureFolder("Scenes");
            Material wood = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Wood.mat");
            Material sailMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Sail.mat");
            Material metal = GetOrCreateMaterial("EquipmentMetal", new Color(0.21f, 0.25f, 0.28f));
            Material bronze = GetOrCreateMaterial("EquipmentBronze", new Color(0.59f, 0.43f, 0.16f));
            if (wood == null) { EditorUtility.DisplayDialog("Boatships", "Missing starter material Wood.mat", "OK"); return; }

            ShipEquipmentDefinition cannon = CreateEquipment("Cannon", "deck_cannon", "Deck Cannon", ShipEquipmentKind.Cannon,
                ShipPlacementKind.DeckGrid, HullHardpointType.Mast, 2, 2, 60, metal);
            ShipEquipmentDefinition storage = CreateEquipment("Storage", "storage_crate", "Storage Crate", ShipEquipmentKind.Storage,
                ShipPlacementKind.DeckGrid, HullHardpointType.Mast, 2, 2, 35, wood);
            ShipEquipmentDefinition armor = CreateEquipment("Armor", "deck_armor", "Deck Armor", ShipEquipmentKind.Armor,
                ShipPlacementKind.DeckGrid, HullHardpointType.Mast, 1, 1, 25, bronze);
            ShipEquipmentDefinition cabin = CreateEquipment("Cabin", "deck_cabin", "Cabin", ShipEquipmentKind.Cabin,
                ShipPlacementKind.DeckGrid, HullHardpointType.Mast, 3, 3, 120, wood);
            ShipEquipmentDefinition mast = CreateEquipment("Mast", "starter_mast", "Mast & Sail", ShipEquipmentKind.Mast,
                ShipPlacementKind.Hardpoint, HullHardpointType.Mast, 1, 1, 90, sailMaterial != null ? sailMaterial : wood);
            ShipEquipmentDefinition rudder = CreateEquipment("Rudder", "starter_rudder", "Rudder", ShipEquipmentKind.Rudder,
                ShipPlacementKind.Hardpoint, HullHardpointType.Rudder, 1, 1, 25, wood);

            string target = AssetDatabase.GenerateUniqueAssetPath(Root + "/Scenes/Boatships_ShipyardPrototype.unity");
            if (!AssetDatabase.CopyAsset(baseScene, target))
            {
                EditorUtility.DisplayDialog("Boatships", "Unable to copy Milestone 2 test scene.", "OK");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(target, OpenSceneMode.Single);
            GameObject player = GameObject.Find("PlayerShip");
            Transform visuals = player != null ? player.transform.Find("ShipVisuals") : null;
            VoxelHull voxelHull = visuals != null ? visuals.GetComponentInChildren<VoxelHull>(true) : null;
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (player == null || voxelHull == null || canvas == null)
            {
                EditorUtility.DisplayDialog("Boatships", "Expected PlayerShip/ShipVisuals/VoxelHull and Canvas were not found in " + baseScene, "OK");
                return;
            }

            DestroyChild(visuals, "Mast");
            DestroyChild(visuals, "Sail");
            DestroyChild(visuals, "Cabin");

            ShipCustomization controller = player.AddComponent<ShipCustomization>();
            controller.Configure(voxelHull, player.GetComponent<ShipArcadeController>(),
                player.GetComponent<HullDamageDemo>(), cannon, storage, armor, cabin, mast, rudder);
            BuildUI(canvas.transform, controller);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;
            EditorUtility.DisplayDialog("Boatships", "Shipyard test scene created:\n" + target +
                "\n\nPress Play. Select equipment in the panel and tap the deck to install.\nFinish / Sail returns to your original movement controls.", "OK");
            Debug.Log("Boatships: Shipyard scene created: " + target);
        }

        private static void BuildUI(Transform canvas, ShipCustomization controller)
        {
            GameObject panel = CreateUI("ShipyardPanel", canvas, typeof(Image), typeof(VerticalLayoutGroup));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-20f, -20f);
            panelRect.sizeDelta = new Vector2(285f, 735f);
            panel.GetComponent<Image>().color = new Color(0.055f, 0.11f, 0.15f, 0.88f);
            VerticalLayoutGroup mainLayout = panel.GetComponent<VerticalLayoutGroup>();
            mainLayout.padding = new RectOffset(12, 12, 12, 12);
            mainLayout.spacing = 8f;
            mainLayout.childForceExpandWidth = true;
            mainLayout.childForceExpandHeight = false;
            mainLayout.childControlWidth = true;
            mainLayout.childControlHeight = true;

            Text toggleLabel;
            Button toggle = MakeButton(panel.transform, "Toggle Shipyard", "Finish / Sail", 58f, out toggleLabel);

            GameObject actions = CreateUI("Equipment Actions", panel.transform, typeof(VerticalLayoutGroup), typeof(LayoutElement));
            actions.GetComponent<LayoutElement>().preferredHeight = 450f;
            VerticalLayoutGroup actionLayout = actions.GetComponent<VerticalLayoutGroup>();
            actionLayout.spacing = 5f;
            actionLayout.childForceExpandWidth = true;
            actionLayout.childForceExpandHeight = false;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;

            Text ignored;
            Button cannon = MakeButton(actions.transform, "Cannon Button", "Place Cannon", 51f, out ignored);
            Button storage = MakeButton(actions.transform, "Storage Button", "Place Storage", 51f, out ignored);
            Button armor = MakeButton(actions.transform, "Armor Button", "Place Armor", 51f, out ignored);
            Button cabin = MakeButton(actions.transform, "Cabin Button", "Place Cabin", 51f, out ignored);
            Button mast = MakeButton(actions.transform, "Mast Button", "Toggle Mast", 51f, out ignored);
            Button rudder = MakeButton(actions.transform, "Rudder Button", "Toggle Rudder", 51f, out ignored);
            Button rotate = MakeButton(actions.transform, "Rotate Button", "Rotate 90° (R)", 51f, out ignored);
            Button remove = MakeButton(actions.transform, "Remove Button", "Remove Deck Item", 51f, out ignored);

            GameObject labelObject = CreateUI("Status Text", panel.transform, typeof(Text), typeof(LayoutElement));
            labelObject.GetComponent<LayoutElement>().preferredHeight = 156f;
            Text status = labelObject.GetComponent<Text>();
            status.font = DefaultFont();
            status.fontSize = 19;
            status.color = Color.white;
            status.alignment = TextAnchor.UpperLeft;
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Truncate;
            status.raycastTarget = false;
            status.text = "Boatships Shipyard";

            ShipCustomizationUI ui = panel.AddComponent<ShipCustomizationUI>();
            ui.Configure(controller, toggle, cannon, storage, armor, cabin, mast, rudder,
                rotate, remove, toggleLabel, status, actions);
        }

        private static Button MakeButton(Transform parent, string name, string title, float height, out Text label)
        {
            GameObject gameObject = CreateUI(name, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            gameObject.GetComponent<LayoutElement>().preferredHeight = height;
            Image image = gameObject.GetComponent<Image>();
            image.color = new Color(0.15f, 0.30f, 0.39f, 0.96f);
            Button button = gameObject.GetComponent<Button>();
            button.targetGraphic = image;
            GameObject textObject = CreateUI("Label", gameObject.transform, typeof(Text));
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f, 2f);
            rect.offsetMax = new Vector2(-4f, -2f);
            label = textObject.GetComponent<Text>();
            label.font = DefaultFont();
            label.fontSize = 23;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.text = title;
            return button;
        }

        private static Font DefaultFont()
        {
            // Legacy runtime font is still available for Unity UI Text in Unity 6.
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static GameObject CreateUI(string name, Transform parent, params System.Type[] components)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            foreach (System.Type component in components) obj.AddComponent(component);
            return obj;
        }

        private static ShipEquipmentDefinition CreateEquipment(string fileName, string id, string display,
            ShipEquipmentKind kind, ShipPlacementKind placement, HullHardpointType socket,
            int width, int length, float mass, Material mat)
        {
            string path = Root + "/Equipment/" + fileName + ".asset";
            ShipEquipmentDefinition existing = AssetDatabase.LoadAssetAtPath<ShipEquipmentDefinition>(path);
            if (existing != null) return existing; // preserve user edits and asset identity
            var item = ScriptableObject.CreateInstance<ShipEquipmentDefinition>();
            item.SetDemo(id, display, kind, placement, socket, width, length, mass, mat);
            AssetDatabase.CreateAsset(item, path);
            return item;
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string folder = Root + "/Materials";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root, "Materials");
            string path = folder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("URP/Lit not found. Use Universal 3D.");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static string FindVoxelScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(VoxelSource) != null) return VoxelSource;
            foreach (string guid in AssetDatabase.FindAssets("Boatships_VoxelPrototype t:Scene", new[] { Root + "/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Boatships_VoxelPrototype")) return path;
            }
            return null;
        }

        private static void EnsureFolder(string name)
        {
            if (!AssetDatabase.IsValidFolder(Root + "/" + name)) AssetDatabase.CreateFolder(Root, name);
        }

        private static void DestroyChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }
    }
}
#endif
