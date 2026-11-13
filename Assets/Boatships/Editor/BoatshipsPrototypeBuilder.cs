#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Boatships.Editor
{
    /// <summary>Builds a runnable demonstration scene inside a fresh Universal 3D project.</summary>
    public static class BoatshipsPrototypeBuilder
    {
        private const string Root = "Assets/Boatships";
        private const string ScenePath = Root + "/Scenes/Boatships_Prototype.unity";

        [MenuItem("Tools/Boatships/Create Prototype Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder("Materials");
            EnsureFolder("Scenes");

            Material oceanMaterial = GetOrCreateMaterial("Ocean", new Color(0.035f, 0.37f, 0.47f, 1f));
            Material hullMaterial = GetOrCreateMaterial("Wood", new Color(0.35f, 0.19f, 0.09f, 1f));
            Material deckMaterial = GetOrCreateMaterial("Deck", new Color(0.58f, 0.34f, 0.17f, 1f));
            Material sailMaterial = GetOrCreateMaterial("Sail", new Color(0.95f, 0.90f, 0.72f, 1f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject lightObject = new GameObject("Directional Light");
            Light sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            GameObject oceanObject = new GameObject("Ocean", typeof(MeshFilter), typeof(MeshRenderer));
            oceanObject.GetComponent<MeshRenderer>().sharedMaterial = oceanMaterial;
            OceanWaves ocean = oceanObject.AddComponent<OceanWaves>();

            GameObject ship = new GameObject("PlayerShip");
            ship.transform.position = new Vector3(0f, 0.6f, 0f);
            ship.AddComponent<BoxCollider>().size = new Vector3(2.0f, 0.9f, 4.4f);
            Rigidbody body = ship.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            GameObject visuals = new GameObject("ShipVisuals");
            visuals.transform.SetParent(ship.transform, false);
            MakePrimitive("Hull", PrimitiveType.Cube, visuals.transform,
                new Vector3(0f, 0f, 0f), new Vector3(2f, 0.75f, 4.4f), hullMaterial);
            MakePrimitive("Deck", PrimitiveType.Cube, visuals.transform,
                new Vector3(0f, 0.48f, 0f), new Vector3(1.85f, 0.15f, 3.9f), deckMaterial);
            MakePrimitive("Cabin", PrimitiveType.Cube, visuals.transform,
                new Vector3(0f, 0.95f, -1.1f), new Vector3(1.5f, 0.9f, 1.3f), hullMaterial);
            MakePrimitive("Mast", PrimitiveType.Cylinder, visuals.transform,
                new Vector3(0f, 1.75f, 0.45f), new Vector3(0.12f, 1.45f, 0.12f), hullMaterial);
            MakePrimitive("Sail", PrimitiveType.Cube, visuals.transform,
                new Vector3(0f, 2.2f, 0.45f), new Vector3(1.45f, 1.65f, 0.08f), sailMaterial);

            ShipArcadeController controller = ship.AddComponent<ShipArcadeController>();
            controller.Ocean = ocean;
            controller.VisualPivot = visuals.transform;
            ocean.FollowTarget = ship.transform;

            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(-15f, 17f, -15f);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 400f;
            IsometricFollowCamera follow = cameraObject.AddComponent<IsometricFollowCamera>();
            follow.Target = ship.transform;

            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject joystickObject = new GameObject("Movement Joystick", typeof(RectTransform),
                typeof(Image), typeof(VirtualJoystick));
            joystickObject.transform.SetParent(canvasObject.transform, false);
            RectTransform background = joystickObject.GetComponent<RectTransform>();
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.zero;
            background.pivot = new Vector2(0.5f, 0.5f);
            background.anchoredPosition = new Vector2(175f, 180f);
            background.sizeDelta = new Vector2(230f, 230f);
            Image backgroundImage = joystickObject.GetComponent<Image>();
            backgroundImage.color = new Color(1f, 1f, 1f, 0.27f);

            GameObject handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(joystickObject.transform, false);
            RectTransform handle = handleObject.GetComponent<RectTransform>();
            handle.anchorMin = new Vector2(0.5f, 0.5f);
            handle.anchorMax = new Vector2(0.5f, 0.5f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.anchoredPosition = Vector2.zero;
            handle.sizeDelta = new Vector2(86f, 86f);
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(1f, 1f, 1f, 0.64f);
            // Let the parent receive pointer events even when touching the handle.
            handleImage.raycastTarget = false;

            VirtualJoystick joystick = joystickObject.GetComponent<VirtualJoystick>();
            joystick.Background = background;
            joystick.Handle = handle;
            controller.Joystick = joystick;

            GameObject events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureSceneInBuild(ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = ship;
            UnityEngine.Debug.Log("Boatships prototype created. Open Assets/Boatships/Scenes/Boatships_Prototype.unity and press Play.");
            EditorUtility.DisplayDialog("Boatships", "Prototype scene created! Press Play to test WASD or the on-screen joystick.", "OK");
        }

        private static void EnsureFolder(string name)
        {
            string path = Root + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(Root, name);
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new System.InvalidOperationException("URP/Lit shader not found. Create the project with Universal 3D.");

            Material created = new Material(shader) { name = name };
            created.SetColor("_BaseColor", color);
            created.SetFloat("_Smoothness", name == "Ocean" ? 0.75f : 0.12f);
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void MakePrimitive(string name, PrimitiveType type, Transform parent,
            Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            // The ship has one simplified collider at the root for now.
            Collider primitiveCollider = part.GetComponent<Collider>();
            if (primitiveCollider != null)
                Object.DestroyImmediate(primitiveCollider);
        }

        private static void EnsureSceneInBuild(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene candidate in scenes)
                if (candidate.path == path) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
