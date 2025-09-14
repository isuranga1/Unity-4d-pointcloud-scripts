using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// This editor window automates the process of setting up scene hierarchies
/// by loading FBX models from a specific asset path and creating a corresponding
/// parent structure in the currently open scene, with an option to center the models.
/// </summary>
public class SceneHierarchyBuilder : EditorWindow
{
    private bool centerInstances = true;
    private string basePath = "Assets/InfingenScenes/V1";

    /// <summary>
    /// Creates the menu item that opens this editor window.
    /// </summary>
    [MenuItem("Tools/Scene Hierarchy Builder")]
    public static void ShowWindow()
    {
        // Get existing open window or if none, make a new one.
        GetWindow<SceneHierarchyBuilder>("Scene Builder");
    }

    /// <summary>
    /// Draws the UI for the editor window.
    /// </summary>
    void OnGUI()
    {
        GUILayout.Label("Hierarchy Build Settings", EditorStyles.boldLabel);
        
        // Add a text field for the user to specify the path.
        basePath = EditorGUILayout.TextField("FBX Source Path", basePath);
        
        centerInstances = EditorGUILayout.Toggle("Center Instances at Origin", centerInstances);

        EditorGUILayout.Space();

        if (GUILayout.Button("Build Scene Hierarchies"))
        {
            BuildSceneHierarchies();
        }
    }

    /// <summary>
    /// Finds all FBX files in the specified directory, and for each one, it creates the
    /// appropriate GameObject hierarchy and instantiates the model into the scene.
    /// </summary>
    private void BuildSceneHierarchies()
    {
        if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
        {
            Debug.LogError($"The specified base path is invalid or does not exist: {basePath}");
            return;
        }

        string[] fbxFiles = Directory.GetFiles(basePath, "*.fbx", SearchOption.AllDirectories);

        if (fbxFiles.Length == 0)
        {
            Debug.LogWarning($"No .fbx files found in {basePath}.");
            return;
        }

        Debug.Log($"Found {fbxFiles.Length} FBX files to process. Starting hierarchy build...");
        int processedCount = 0;
        var topLevelParents = new HashSet<GameObject>();

        foreach (string filePath in fbxFiles)
        {
            string normalizedPath = filePath.Replace('\\', '/');
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(normalizedPath);

            if (fbxAsset == null)
            {
                Debug.LogWarning($"Could not load asset at path: {normalizedPath}. Skipping.");
                continue;
            }

            // To get the hierarchy names, we need the path relative to the *base* path.
            string relativePath = normalizedPath.Substring(basePath.Length + 1);
            string[] pathParts = relativePath.Split('/');

            // We expect at least 4 parts for the relative hierarchy (Level1/Level2/Level3/model.fbx)
            if (pathParts.Length < 4)
            {
                Debug.LogWarning($"The path '{normalizedPath}' does not match the expected structure within the base path. Skipping.");
                continue;
            }

            string topLevelName = pathParts[0];
            string secondLevelName = pathParts[1];
            string thirdLevelName = pathParts[2];

            Transform topParent = FindOrCreateParent(topLevelName, null);
            topLevelParents.Add(topParent.gameObject);
            
            Transform secondParent = FindOrCreateParent(secondLevelName, topParent);
            Transform thirdParent = FindOrCreateParent(thirdLevelName, secondParent);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            if (instance != null)
            {
                instance.transform.SetParent(thirdParent);
                instance.name = Path.GetFileNameWithoutExtension(normalizedPath);

                // If the centering option is checked, center the newly created object.
                if (centerInstances)
                {
                    CenterObject(instance);
                }

                processedCount++;
            }
            else
            {
                Debug.LogError($"Failed to instantiate FBX asset at path: {normalizedPath}");
            }
        }

        foreach (var parentObject in topLevelParents)
        {
            parentObject.SetActive(false);
        }

        Debug.Log($"Hierarchy build complete. Successfully processed and instantiated {processedCount} models. Top-level containers have been hidden.");
    }
    
    /// <summary>
    /// Repositions a GameObject so that its visual center (based on its renderers)
    /// is aligned with its transform's pivot point.
    /// </summary>
    /// <param name="targetObject">The GameObject to center.</param>
    private void CenterObject(GameObject targetObject)
    {
        var renderers = targetObject.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"Cannot center '{targetObject.name}' because it has no Renderer components.", targetObject);
            return;
        }

        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        // Calculate the offset from the object's pivot to the center of its visual bounds.
        Vector3 centerOffset = combinedBounds.center - targetObject.transform.position;

        // Move the object by the inverse of the offset. This aligns the visual center with the pivot.
        targetObject.transform.position -= centerOffset;
    }

    /// <summary>
    /// A helper method to find a child GameObject by name, or create it if it doesn't exist.
    /// </summary>
    private Transform FindOrCreateParent(string name, Transform parentTransform)
    {
        if (parentTransform == null)
        {
            GameObject foundObject = GameObject.Find(name);
            if (foundObject == null)
            {
                foundObject = new GameObject(name);
            }
            return foundObject.transform;
        }

        Transform childTransform = parentTransform.Find(name);
        if (childTransform == null)
        {
            GameObject newChild = new GameObject(name);
            newChild.transform.SetParent(parentTransform);
            newChild.transform.localPosition = Vector3.zero;
            newChild.transform.localRotation = Quaternion.identity;
            newChild.transform.localScale = Vector3.one;
            return newChild.transform;
        }

        return childTransform;
    }
}

