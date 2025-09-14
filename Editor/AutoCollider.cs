using UnityEngine;
using UnityEditor;

/// <summary>
/// An Editor Window to automatically add and remove colliders from child objects.
/// This is useful for quickly setting up scenes for physics-based interactions and collision detection.
/// </summary>
public class AutoCollider : EditorWindow
{
    private GameObject parentObject; // The root object whose children will be processed.

    /// <summary>
    /// Creates a menu item in the Unity Editor under "Tools" to open this window.
    /// </summary>
    [MenuItem("Tools/Auto Collider")]
    public static void ShowWindow()
    {
        // Get existing open window or if none, make a new one.
        GetWindow<AutoCollider>("Auto Collider");
    }

    /// <summary>
    /// Renders the GUI for the editor window.
    /// </summary>
    void OnGUI()
    {
        GUILayout.Label("Scene Collider Setup", EditorStyles.boldLabel);
        
        EditorGUILayout.HelpBox("Drag the parent GameObject of your scene (e.g., 'Bedroom') into the field below. The script will process all of its direct children.", MessageType.Info);

        // Field for the user to drag and drop the parent GameObject.
        parentObject = (GameObject)EditorGUILayout.ObjectField("Parent Scene Object", parentObject, typeof(GameObject), true);

        if (parentObject == null)
        {
            EditorGUILayout.HelpBox("Please assign a Parent Scene Object.", MessageType.Warning);
            return;
        }

        // Button to add colliders.
        if (GUILayout.Button("Add MeshColliders to Children"))
        {
            AddCollidersToChildren();
        }

        // Button to remove colliders.
        if (GUILayout.Button("Remove ALL Colliders from Children"))
        {
            if (EditorUtility.DisplayDialog("Confirm Removal", 
                "Are you sure you want to remove all collider components from the children of '" + parentObject.name + "'? This action cannot be undone.", 
                "Yes, Remove Them", 
                "Cancel"))
            {
                RemoveCollidersFromChildren();
            }
        }
    }

    /// <summary>
    /// Iterates through all direct children of the parentObject and adds a MeshCollider
    /// if the child has a MeshFilter but no existing Collider.
    /// </summary>
    private void AddCollidersToChildren()
    {
        if (parentObject == null) return;

        int collidersAdded = 0;
        // Get all Transform components in the children of the parent.
        foreach (Transform child in parentObject.transform)
        {
            // Check if the child object has a mesh...
            if (child.GetComponent<MeshFilter>() != null)
            {
                // ...and check if it does NOT already have any type of collider.
                if (child.GetComponent<Collider>() == null)
                {
                    // If both are true, add a MeshCollider.
                    child.gameObject.AddComponent<MeshCollider>();
                    collidersAdded++;
                }
            }
        }

        // Show a confirmation dialog to the user.
        EditorUtility.DisplayDialog("Process Complete", 
            $"Added {collidersAdded} MeshCollider(s) to the children of '{parentObject.name}'.", 
            "OK");
    }

    /// <summary>
    /// Iterates through all direct children of the parentObject and removes any
    /// component that inherits from Collider.
    /// </summary>
    private void RemoveCollidersFromChildren()
    {
        if (parentObject == null) return;

        int collidersRemoved = 0;
        foreach (Transform child in parentObject.transform)
        {
            // Get all colliders on the child object.
            Collider[] colliders = child.GetComponents<Collider>();
            if (colliders.Length > 0)
            {
                collidersRemoved += colliders.Length;
                // Loop through and destroy each one.
                foreach (var col in colliders)
                {
                    DestroyImmediate(col);
                }
            }
        }

        // Show a confirmation dialog to the user.
        EditorUtility.DisplayDialog("Process Complete", 
            $"Removed {collidersRemoved} Collider(s) from the children of '{parentObject.name}'.", 
            "OK");
    }
}
