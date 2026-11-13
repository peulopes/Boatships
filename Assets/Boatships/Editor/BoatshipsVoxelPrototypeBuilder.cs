#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Boatships.Editor
{
    public static class BoatshipsVoxelPrototypeBuilder
    {
        private const string Root = "Assets/Boatships";
        private const string OriginalScene = Root + "/Scenes/Boatships_Prototype.unity";
        private const string HullAsset = Root + "/Hulls/StarterSloop.asset";

        [MenuItem("Tools/Boatships/Create Voxel Hull Test Scene")]
        public static void CreateVoxelScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(OriginalScene) == null)
            {
                EditorUtility.DisplayDialog("Boatships", "Cannot find original prototype scene at " + OriginalScene +
                    ". Create the first prototype with Tools > Boatships > Create Prototype Scene.", "OK");
                return;
            }

            EnsureFolder("Hulls");
            HullDefinition hullDefinition = AssetDatabase.LoadAssetAtPath<HullDefinition>(HullAsset);
            if (hullDefinition == null)
            {
                hullDefinition = ScriptableObject.CreateInstance<HullDefinition>();
                hullDefinition.ConfigureStarterSloop();
                AssetDatabase.CreateAsset(hullDefinition, HullAsset);
            }

            string scenePath = AssetDatabase.GenerateUniqueAssetPath(
                Root + "/Scenes/Boatships_VoxelPrototype.unity");
            if (!AssetDatabase.CopyAsset(OriginalScene, scenePath))
            {
                EditorUtility.DisplayDialog("Boatships", "Failed to copy original scene.", "OK");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GameObject ship = GameObject.Find("PlayerShip");
            Transform visuals = ship != null ? ship.transform.Find("ShipVisuals") : null;
            if (visuals == null)
            {
                EditorUtility.DisplayDialog("Boatships", "Cannot find PlayerShip/ShipVisuals in copied scene.", "OK");
                return;
            }

            Material hullMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Wood.mat");
            if (hullMaterial == null)
            {
                EditorUtility.DisplayDialog("Boatships", "Cannot find Wood.mat from starter kit.", "OK");
                return;
            }

            // Remove the old visual placeholder only from the NEW copied scene.
            DestroyNamedChild(visuals, "Hull");
            DestroyNamedChild(visuals, "Deck");

            GameObject hullObject = new GameObject("VoxelHull", typeof(MeshFilter), typeof(MeshRenderer));
            hullObject.transform.SetParent(visuals, false);
            VoxelHull hull = hullObject.AddComponent<VoxelHull>();
            hull.Configure(hullDefinition, hullMaterial);

            // Align the remaining cabin with the top surface of the voxel hull.
            Transform cabin = visuals.Find("Cabin");
            if (cabin != null) cabin.localPosition += Vector3.down * 0.12f;

            HullDamageDemo demo = ship.AddComponent<HullDamageDemo>();
            demo.Hull = hull;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = hullObject;
            EditorUtility.DisplayDialog("Boatships", "Voxel test scene created!\n\n" + scenePath +
                "\n\nPress Play, then H or click/tap on the hull to destroy blocks.\nThe original scene is unchanged.", "OK");
            Debug.Log("Boatships: Created voxel hull scene at " + scenePath);
        }

        private static void DestroyNamedChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }

        private static void EnsureFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(Root + "/" + folder))
                AssetDatabase.CreateFolder(Root, folder);
        }
    }
}
#endif
