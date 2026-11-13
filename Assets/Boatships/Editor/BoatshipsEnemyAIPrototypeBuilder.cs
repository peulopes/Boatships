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
    /// Copies the working Milestone 4 scene; replaces the stationary red target
    /// with an armed AI vessel. Never changes existing scenes or combat scripts.
    /// </summary>
    public static class BoatshipsEnemyAIPrototypeBuilder
    {
        private const string Root = "Assets/Boatships";

        [MenuItem("Tools/Boatships/Create Enemy AI Combat Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string source = FindCombatScene();
            if (string.IsNullOrEmpty(source))
            {
                EditorUtility.DisplayDialog("Boatships", "First create the Milestone 4 scene using Tools > Boatships > Create Cannon Combat Scene.", "OK");
                return;
            }

            string destination = AssetDatabase.GenerateUniqueAssetPath(
                Root + "/Scenes/Boatships_EnemyAIPrototype.unity");
            if (!AssetDatabase.CopyAsset(source, destination))
            {
                EditorUtility.DisplayDialog("Boatships", "Could not copy the Milestone 4 scene.", "OK");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(destination, OpenSceneMode.Single);
            GameObject player = GameObject.Find("PlayerShip");
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            OceanWaves ocean = Object.FindFirstObjectByType<OceanWaves>();
            ShipCombatTarget playerTarget = player != null ? player.GetComponent<ShipCombatTarget>() : null;
            ShipCustomization shipyard = player != null ? player.GetComponent<ShipCustomization>() : null;
            WeaponCombatHUD existingHUD = canvas != null ? canvas.GetComponent<WeaponCombatHUD>() : null;
            VoxelHull playerHull = playerTarget != null ? playerTarget.Hull : null;

            if (playerTarget == null || shipyard == null || existingHUD == null || ocean == null ||
                canvas == null || playerHull == null || playerHull.Definition == null)
            {
                EditorUtility.DisplayDialog("Boatships", "The copied scene is missing the Milestone 4 player, combat HUD, shipyard, or hull. Please recreate the Milestone 4 scene.", "OK");
                return;
            }

            Material enemyMaterial = EnsureMaterial("PirateHullRed", new Color(0.54f, 0.17f, 0.13f));
            Material cannonMaterial = EnsureMaterial("PirateCannonDark", new Color(0.13f, 0.13f, 0.17f));
            Material sailMaterial = EnsureMaterial("PirateSail", new Color(0.80f, 0.72f, 0.63f));
            Material projectileMaterial = EnsureMaterial("CannonballIron", new Color(0.07f, 0.08f, 0.10f));

            ShipCombatTarget formerTarget = GameObject.Find("Practice Target Ship") != null
                ? GameObject.Find("Practice Target Ship").GetComponent<ShipCombatTarget>() : null;
            ShipCombatTarget enemyTarget = CreateEnemy(playerHull.Definition, ocean, shipyard,
                playerTarget, enemyMaterial, cannonMaterial, sailMaterial, projectileMaterial,
                out EnemyShipAI enemyAI);

            // Preserve the existing group buttons, fire button, and reset button;
            // only retarget the original HUD's practice-target reference.
            var hudSerialized = new SerializedObject(existingHUD);
            SerializedProperty targetProperty = hudSerialized.FindProperty("practiceTarget");
            if (targetProperty != null)
            {
                targetProperty.objectReferenceValue = enemyTarget;
                hudSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
            if (formerTarget != null) Object.DestroyImmediate(formerTarget.gameObject);

            CreateEnemyHud(canvas.transform, enemyAI, playerTarget, enemyTarget, shipyard);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = enemyAI.gameObject;
            Debug.Log("Boatships Milestone 5 scene: " + destination);
            EditorUtility.DisplayDialog("Boatships", "Created enemy AI scene:\n" + destination +
                "\n\nPress Play, install a starboard cannon if needed, click Finish / Sail, and engage the moving pirate vessel. The original Milestone 4 scene remains intact.", "OK");
        }

        private static ShipCombatTarget CreateEnemy(HullDefinition hullDefinition, OceanWaves ocean,
            ShipCustomization shipyard, ShipCombatTarget player, Material hullMat,
            Material cannonMat, Material sailMat, Material ballMat, out EnemyShipAI ai)
        {
            GameObject enemy = new GameObject("Pirate Enemy Ship");
            enemy.transform.position = new Vector3(17f, 0.55f, 3f);
            enemy.transform.rotation = Quaternion.Euler(0f, -50f, 0f);

            BoxCollider box = enemy.AddComponent<BoxCollider>();
            box.size = new Vector3(2.4f, 1f, 4.8f);
            Rigidbody body = enemy.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            GameObject visuals = new GameObject("ShipVisuals");
            visuals.transform.SetParent(enemy.transform, false);
            GameObject hullObject = new GameObject("VoxelHull", typeof(MeshFilter), typeof(MeshRenderer));
            hullObject.transform.SetParent(visuals.transform, false);
            VoxelHull hull = hullObject.AddComponent<VoxelHull>();
            hull.Configure(hullDefinition, hullMat);

            AddPrimitive(visuals.transform, "Pirate Mast", PrimitiveType.Cylinder,
                new Vector3(0f, 1.0f, 0.1f), new Vector3(0.07f, 1.12f, 0.07f), cannonMat);
            AddPrimitive(visuals.transform, "Pirate Sail", PrimitiveType.Cube,
                new Vector3(0f, 1.4f, 0.08f), new Vector3(1.0f, 1.05f, 0.06f), sailMat);

            List<Transform> portMuzzles = new List<Transform>();
            List<Transform> starboardMuzzles = new List<Transform>();
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 2; ++i)
            {
                float z = i == 0 ? -1.05f : 1.05f;
                GameObject cannon = new GameObject((side < 0 ? "Port" : "Starboard") + " Cannon " + (i + 1));
                cannon.transform.SetParent(visuals.transform, false);
                cannon.transform.localPosition = new Vector3(side * 0.94f, 0.39f, z);
                AddPrimitive(cannon.transform, "Barrel", PrimitiveType.Cylinder,
                    new Vector3(side * 0.11f, 0f, 0f), new Vector3(0.13f, 0.33f, 0.13f),
                    cannonMat, Quaternion.Euler(0f, 0f, 90f));
                GameObject muzzle = new GameObject("Muzzle");
                muzzle.transform.SetParent(visuals.transform, false);
                muzzle.transform.localPosition = new Vector3(side * 1.55f, 0.44f, z);
                if (side < 0) portMuzzles.Add(muzzle.transform);
                else starboardMuzzles.Add(muzzle.transform);
            }

            ShipCombatTarget receiver = enemy.AddComponent<ShipCombatTarget>();
            // Retain the Reset Target button from M4 for a repeatable AI test.
            receiver.Configure(hull, true);
            ai = enemy.AddComponent<EnemyShipAI>();
            ai.Configure(player, receiver, ocean, shipyard, visuals.transform,
                portMuzzles.ToArray(), starboardMuzzles.ToArray(), ballMat);
            return receiver;
        }

        private static void CreateEnemyHud(Transform canvas, EnemyShipAI ai,
            ShipCombatTarget player, ShipCombatTarget enemy, ShipCustomization shipyard)
        {
            GameObject panel = new GameObject("Enemy AI Readout", typeof(RectTransform), typeof(Text));
            panel.transform.SetParent(canvas, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(20f, -24f);
            rect.sizeDelta = new Vector2(515f, 125f);
            Text label = panel.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 21;
            label.color = new Color(1f, 0.93f, 0.78f);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            EnemyCombatHUD hud = canvas.gameObject.AddComponent<EnemyCombatHUD>();
            hud.Configure(ai, player, enemy, shipyard, label);
        }

        private static void AddPrimitive(Transform parent, string name, PrimitiveType type,
            Vector3 localPosition, Vector3 scale, Material material, Quaternion rotation = default(Quaternion))
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPosition;
            obj.transform.localScale = scale;
            obj.transform.localRotation = rotation == default(Quaternion) ? Quaternion.identity : rotation;
            obj.GetComponent<MeshRenderer>().sharedMaterial = material;
            Collider collider = obj.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }

        private static string FindCombatScene()
        {
            const string preferred = Root + "/Scenes/Boatships_CombatPrototype.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(preferred) != null) return preferred;
            foreach (string guid in AssetDatabase.FindAssets("Boatships_CombatPrototype t:Scene", new[] { Root + "/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Boatships_CombatPrototype")) return path;
            }
            return null;
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            const string dir = Root + "/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root, "Materials");
            string path = dir + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("URP Lit shader not found.");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
#endif
