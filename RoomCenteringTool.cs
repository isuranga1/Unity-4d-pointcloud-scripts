using UnityEngine;
using UnityEditor;

/// <summary>
/// An editor tool to calculate the geometric center of a selected GameObject's children 
/// and move the object so that this center is at the world origin (0,0,0).
/// </summary>
public class RoomCenteringTool : EditorWindow
{
    /// <summary>
    /// Creates a menu item in the Unity Editor to open this tool's window.
    /// </summary>
    [MenuItem("Tools/Room Centering Tool")]
    public static void ShowWindow()
    {
        // Get an existing open window or, if none, make a new one.
        GetWindow<RoomCenteringTool>("Room Center");
    }

    /// <summary>
    /// Renders the GUI for the editor window.
    /// </summary>
    void OnGUI()
    {
        // --- Window Title ---
        GUILayout.Label("Center Room at World Origin", EditorStyles.boldLabel);
        GUILayout.Space(10);
        EditorGUILayout.HelpBox("Select the root GameObject of your room in the Hierarchy, then click the button below. The tool will calculate the center of all visual elements and move the root so this center is at (0,0,0).", MessageType.Info);
        GUILayout.Space(15);

        // --- Selection Info ---
        GameObject selectedObject = Selection.activeGameObject;

        if (selectedObject == null)
        {
            EditorGUILayout.LabelField("Selected Object:", "None");
            // Disable the button if nothing is selected to prevent errors.
            GUI.enabled = false;
        }
        else
        {
            EditorGUILayout.LabelField("Selected Object:", selectedObject.name);
        }

        // --- Action Button ---
        if (GUILayout.Button("Calculate and Center Selected Room", GUILayout.Height(30)))
        {
            if (selectedObject != null)
            {
                CenterSelectedRoom(selectedObject);
            }
        }
        
        // Re-enable the GUI for the next frame.
        GUI.enabled = true;
    }

    /// <summary>
    /// Performs the core logic of calculating the bounds and repositioning the object.
    /// </summary>
    /// <param name="roomRoot">The parent GameObject of the room to be centered.</param>
    private void CenterSelectedRoom(GameObject roomRoot)
    {
        // Find all Renderer components in the object and its children. We use renderers 
        // because they provide the 'bounds', which define the visual space an object occupies.
        Renderer[] renderers = roomRoot.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            Debug.LogError($"Room Centering Tool: No 'Renderer' components found in '{roomRoot.name}' or its children. Cannot calculate bounds.");
            EditorUtility.DisplayDialog("Error", $"No renderers found in '{roomRoot.name}' or its children. The object must have visual components (like meshes) to be centered.", "OK");
            return;
        }

        // Start with the bounds of the first renderer.
        Bounds combinedBounds = renderers[0].bounds;

        // Use a loop to expand this single bounding box to encapsulate all other renderers.
        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        // The center of this combined bounding box is the true geometric center of the room.
        Vector3 roomCenter = combinedBounds.center;

        // The offset required to move the room's center to the world origin (0,0,0).
        Vector3 offset = -roomCenter;

        Debug.Log($"[Room Centering Tool] Calculated center for '{roomRoot.name}': {roomCenter.ToString("F4")}");
        Debug.Log($"[Room Centering Tool] Applying position offset: {offset.ToString("F4")}");

        // IMPORTANT: Register the object for an undo operation. This allows you to press Ctrl+Z if you don't like the result.
        Undo.RecordObject(roomRoot.transform, "Center Room at Origin");

        // Apply the calculated offset to the root object's position.
        roomRoot.transform.position += offset;

        Debug.Log($"[Room Centering Tool] Repositioning complete. New position for '{roomRoot.name}': {roomRoot.transform.position.ToString("F4")}");
        EditorUtility.DisplayDialog("Success!", $"The room '{roomRoot.name}' has been successfully centered at the world origin.", "OK");
    }
}