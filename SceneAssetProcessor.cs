// Place this script in a folder named "Editor" in your Assets folder.
// This will add a menu item "Tools/Enable Read-Write for Scene Objects".
// When clicked, it will find all meshes and textures used in the current scene
// and enable their Read/Write settings.

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class SceneAssetProcessor
{
    // Adds the menu item at the top of the Unity editor.
    [MenuItem("Tools/Enable Read-Write for Scene Objects")]
    private static void EnableReadWriteForSceneObjects()
    {
        // A HashSet is used to keep track of asset paths we've already processed.
        // This is efficient and prevents modifying the same asset multiple times if it's used
        // on several objects in the scene.
        HashSet<string> processedPaths = new HashSet<string>();

        int modelsChanged = 0;
        int texturesChanged = 0;

        // Find all Renderer components in the active scene. This includes MeshRenderers and SkinnedMeshRenderers.
        Renderer[] allRenderers = GameObject.FindObjectsOfType<Renderer>();

        Debug.Log($"Found {allRenderers.Length} renderers in the scene. Processing assets...");

        foreach (Renderer renderer in allRenderers)
        {
            // --- 1. Process Meshes ---
            Mesh mesh = null;
            if (renderer is MeshRenderer)
            {
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    mesh = meshFilter.sharedMesh;
                }
            }
            else if (renderer is SkinnedMeshRenderer)
            {
                mesh = ((SkinnedMeshRenderer)renderer).sharedMesh;
            }

            if (mesh != null)
            {
                if (ProcessModel(mesh, processedPaths))
                {
                    modelsChanged++;
                }
            }

            // --- 2. Process Textures from Materials ---
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;

                // Get all texture property names from the material's shader
                string[] texturePropertyNames = material.GetTexturePropertyNames();
                foreach (string propName in texturePropertyNames)
                {
                    Texture texture = material.GetTexture(propName);
                    if (texture != null)
                    {
                        if (ProcessTexture(texture, processedPaths))
                        {
                            texturesChanged++;
                        }
                    }
                }
            }
        }

        Debug.Log($"Processing complete. Modified {modelsChanged} model(s) and {texturesChanged} texture(s).");
    }

    private static bool ProcessModel(Mesh mesh, HashSet<string> processedPaths)
    {
        // Get the file path of the mesh asset in the project.
        string path = AssetDatabase.GetAssetPath(mesh);

        // If path is empty, it's a built-in Unity mesh (like a cube), so we skip it.
        // Also skip if we've already processed this path.
        if (string.IsNullOrEmpty(path) || processedPaths.Contains(path))
        {
            return false;
        }

        processedPaths.Add(path);

        // Get the importer for the model at that path.
        ModelImporter modelImporter = AssetImporter.GetAtPath(path) as ModelImporter;
        if (modelImporter != null)
        {
            if (!modelImporter.isReadable)
            {
                // If it's not readable, enable the flag and re-import the asset to apply the change.
                modelImporter.isReadable = true;
                modelImporter.SaveAndReimport();
                Debug.Log("Enabled Read/Write on model: " + path);
                return true;
            }
        }
        return false;
    }

    private static bool ProcessTexture(Texture texture, HashSet<string> processedPaths)
    {
        // Get the file path of the texture asset.
        string path = AssetDatabase.GetAssetPath(texture);

        if (string.IsNullOrEmpty(path) || processedPaths.Contains(path))
        {
            return false;
        }

        processedPaths.Add(path);

        // Get the importer for the texture.
        TextureImporter textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
        if (textureImporter != null)
        {
            if (!textureImporter.isReadable)
            {
                textureImporter.isReadable = true;
                textureImporter.SaveAndReimport();
                Debug.Log("Enabled Read/Write on texture: " + path);
                return true;
            }
        }
        return false;
    }
}