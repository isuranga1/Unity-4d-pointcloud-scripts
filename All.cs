using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class FBXHierarchyBuilder : EditorWindow
{
    private string fbxFolder = "Assets/FBX";
    private string prefabFolder = "Assets/Prefabs/Converted";

    [MenuItem("Tools/FBX Tools/FBX to Prefab Converter")]
    public static void ShowWindow()
    {
        GetWindow<FBXHierarchyBuilder>("FBX to Prefab Converter");
    }

    private void OnGUI()
    {
        GUILayout.Label("FBX to Prefab Converter", EditorStyles.boldLabel);

        fbxFolder = EditorGUILayout.TextField("FBX Folder", fbxFolder);
        prefabFolder = EditorGUILayout.TextField("Prefab Save Folder", prefabFolder);

        if (GUILayout.Button("Convert All FBX Files"))
        {
            ConvertAllFBX();
        }

        GUILayout.Space(10);
        GUILayout.Label("Individual Operations", EditorStyles.boldLabel);

        if (GUILayout.Button("Enable Read/Write on All Scene Assets"))
        {
            EnableReadWriteForSceneObjects();
        }

        if (GUILayout.Button("Center Selected Object(s)"))
        {
            foreach (GameObject obj in Selection.gameObjects)
            {
                CenterObject(obj);
            }
        }
    }

    // ------------------ PIPELINE ------------------
    private void ConvertAllFBX()
    {
        if (!Directory.Exists(prefabFolder))
            Directory.CreateDirectory(prefabFolder);

        string[] fbxFiles = Directory.GetFiles(fbxFolder, "*.fbx", SearchOption.AllDirectories);

        foreach (string fbxFile in fbxFiles)
        {
            string assetPath = fbxFile.Replace("\\", "/");
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            if (fbxAsset == null)
            {
                Debug.LogWarning($"Could not load FBX at {assetPath}");
                continue;
            }

            // STEP 1: Make all meshes/textures readable
            EnableReadWriteForAsset(fbxAsset);

            // STEP 2: Instantiate, center, and save as prefab
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            CenterObject(instance);

            string prefabPath = Path.Combine(prefabFolder, Path.GetFileNameWithoutExtension(assetPath) + ".prefab");
            prefabPath = prefabPath.Replace("\\", "/");

            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            DestroyImmediate(instance);

            Debug.Log($"Converted and saved prefab: {prefabPath}");
        }

        AssetDatabase.Refresh();
        Debug.Log("✅ All FBX files converted successfully!");
    }

    // ------------------ INDIVIDUAL UTILS ------------------

    [MenuItem("Tools/FBX Tools/Enable Read-Write for Scene Objects")]
    public static void EnableReadWriteForSceneObjects()
    {
        HashSet<string> processedPaths = new HashSet<string>();

        Renderer[] allRenderers = GameObject.FindObjectsOfType<Renderer>(true);
        Debug.Log($"Found {allRenderers.Length} renderers in the scene. Processing assets...");

        int modelsChanged = 0;
        int texturesChanged = 0;

        foreach (Renderer renderer in allRenderers)
        {
            Mesh mesh = null;
            if (renderer is MeshRenderer meshRenderer)
            {
                MeshFilter meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (meshFilter != null)
                    mesh = meshFilter.sharedMesh;
            }
            else if (renderer is SkinnedMeshRenderer smr)
            {
                mesh = smr.sharedMesh;
            }

            if (mesh != null && ProcessModel(mesh, processedPaths)) modelsChanged++;

            foreach (Material mat in renderer.sharedMaterials)
            {
                if (!mat) continue;
                foreach (string propName in mat.GetTexturePropertyNames())
                {
                    Texture tex = mat.GetTexture(propName);
                    if (tex != null && ProcessTexture(tex, processedPaths)) texturesChanged++;
                }
            }
        }

        Debug.Log($"✅ Enabled Read/Write for {modelsChanged} model(s) and {texturesChanged} texture(s).");
    }

    private static void EnableReadWriteForAsset(GameObject root)
    {
        HashSet<string> processedPaths = new HashSet<string>();

        Renderer[] allRenderers = root.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in allRenderers)
        {
            Mesh mesh = null;
            if (renderer is MeshRenderer)
            {
                MeshFilter mf = renderer.GetComponent<MeshFilter>();
                if (mf) mesh = mf.sharedMesh;
            }
            else if (renderer is SkinnedMeshRenderer smr)
            {
                mesh = smr.sharedMesh;
            }

            if (mesh) ProcessModel(mesh, processedPaths);

            foreach (Material mat in renderer.sharedMaterials)
            {
                if (!mat) continue;
                foreach (string propName in mat.GetTexturePropertyNames())
                {
                    Texture tex = mat.GetTexture(propName);
                    if (tex) ProcessTexture(tex, processedPaths);
                }
            }
        }
    }

    private static bool ProcessModel(Mesh mesh, HashSet<string> processedPaths)
    {
        string path = AssetDatabase.GetAssetPath(mesh);
        if (string.IsNullOrEmpty(path) || processedPaths.Contains(path)) return false;

        processedPaths.Add(path);
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log("Enabled Read/Write on model: " + path);
            return true;
        }
        return false;
    }

    private static bool ProcessTexture(Texture texture, HashSet<string> processedPaths)
    {
        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(path) || processedPaths.Contains(path)) return false;

        processedPaths.Add(path);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log("Enabled Read/Write on texture: " + path);
            return true;
        }
        return false;
    }

    public static void CenterObject(GameObject root)
    {
        if (root == null) return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }

        Vector3 offset = combined.center - root.transform.position;
        foreach (Transform child in root.transform)
        {
            child.position -= offset;
        }
        root.transform.position = Vector3.zero;
    }
}
