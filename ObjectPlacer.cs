using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Needed for Shuffle
using SimpleJSON; // Make sure SimpleJSON.cs is in your project!

public class ObjectPlacer : MonoBehaviour
{
    // Custom class to hold a prefab, its desired count, and placement settings
    [System.Serializable] // Makes this class visible and editable in the Inspector
    public class ObjectPlacementEntry
    {
        public GameObject prefab;
        public int count; // How many of this specific prefab to place

        [Header("Object-Specific Placement")]
        public float surfaceLocalY = 2f; // Y-level for this specific object type
        public Vector3 placementExtents = new Vector3(4f, 0f, 4f); // Per-object placement area extents
        [Tooltip("Radius around JSON 'trans' coordinates where this object cannot be placed.")]
        public float exclusionRadius = 2.0f;

        [Header("Object-Specific Scale")]
        [Range(0.1f, 5.0f)] // Adding a slider for better inspector control
        public float minScale = 0.8f; // Minimum uniform scale for this object type
        [Range(0.1f, 5.0f)] // Adding a slider for better inspector control
        public float maxScale = 1.2f; // Maximum uniform scale for this object type

        // Optional: Validate min/max scale in the editor
        public void OnValidate()
        {
            if (minScale > maxScale)
            {
                Debug.LogWarning($"Min Scale for {prefab?.name ?? " (null prefab)"} is greater than Max Scale. Adjusting Min Scale to Max Scale.", prefab);
                minScale = maxScale;
            }
            if (minScale < 0.1f) minScale = 0.1f; // Prevent extremely small scales
            if (maxScale < 0.1f) maxScale = 0.1f;
        }
    }

    // Use a List of our custom entries
    public List<ObjectPlacementEntry> objectsToPlace; // Assign your Prefabs and their settings here!

    [Header("Room Placement Settings")]
    public GameObject roomGameObject;

    [Header("Randomness Settings")]
    [Tooltip("Set to a non-zero value to use a specific seed for reproducible layouts. If 0, a random seed will be used.")]
    public int randomSeed = 0;

    [Header("Overlap Prevention Settings")]
    public float minDistanceBetweenObjects = 1.0f; // Minimum clear distance between centers of objects (horizontal)
    public int maxPlacementAttempts = 50;           // How many times to try finding a spot for one object

    [Header("JSON Exclusion Settings")]
    [Tooltip("Drag your JSON file (as a TextAsset) here. 'trans' coordinates are expected as [z, x, y].")]
    public TextAsset jsonExclusionFile;
    
    private List<GameObject> placedObjects = new List<GameObject>(); // To keep track of placed instances

    // A flattened list of all entries to be placed, taking into account their counts
    private List<ObjectPlacementEntry> _placementQueue = new List<ObjectPlacementEntry>();

    // Stores x,z coordinates from JSON for exclusion (Y component is 0 for horizontal distance checks)
    private List<Vector3> _exclusionPointsXZ = new List<Vector3>();

    void Start()
    {
        // --- NEW: Initialize the random number generator ---
        // This ensures a different layout every time unless a specific seed is provided.
        if (randomSeed != 0)
        {
            Random.InitState(randomSeed);
            Debug.Log($"Using specified random seed: {randomSeed}");
        }
        else
        {
            // Use a time-based seed for true randomness each run
            int seed = (int)System.DateTime.Now.Ticks;
            Random.InitState(seed);
            Debug.Log($"Using a new random seed: {seed}");
        }

        if (roomGameObject == null)
        {
            Debug.LogError("Room GameObject is not assigned to ObjectPlacer!", this);
            enabled = false;
            return;
        }

        if (objectsToPlace == null || objectsToPlace.Count == 0)
        {
            Debug.LogError("No Object Placement Entries assigned to 'Objects To Place' list!", this);
            enabled = false;
            return;
        }

        ProcessJsonExclusionPoints();
        PreparePlacementQueue();
        PlaceObjectsInRoomFlat();
    }

    /// <summary>
    /// Processes the JSON input string loaded from a TextAsset to extract exclusion points.
    /// </summary>
    void ProcessJsonExclusionPoints()
    {
        _exclusionPointsXZ.Clear();
        if (jsonExclusionFile == null)
        {
            Debug.LogWarning("JSON Exclusion File is not assigned. No exclusion points will be used.", this);
            return;
        }

        string jsonString = jsonExclusionFile.text;
        if (string.IsNullOrWhiteSpace(jsonString))
        {
            Debug.LogWarning($"JSON content from '{jsonExclusionFile.name}' is empty. No exclusion points will be used.", this);
            return;
        }

        try
        {
            var parsedJson = JSON.Parse(jsonString);
            if (parsedJson["trans"] != null && parsedJson["trans"].IsArray)
            {
                foreach (JSONNode node in parsedJson["trans"].AsArray)
                {
                    if (node.IsArray && node.Count >= 3)
                    {
                        float z_json = node[0].AsFloat;
                        float x_json = node[1].AsFloat;
                        _exclusionPointsXZ.Add(new Vector3(x_json, 0, z_json));
                    }
                    else
                    {
                        Debug.LogWarning("Found an invalid 'trans' entry in JSON. Skipping.", this);
                    }
                }
                Debug.Log($"Successfully extracted {_exclusionPointsXZ.Count} exclusion points from '{jsonExclusionFile.name}'.");
            }
            else
            {
                Debug.LogWarning($"JSON from '{jsonExclusionFile.name}' does not contain a valid 'trans' array.", this);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to parse JSON from '{jsonExclusionFile.name}': {e.Message}", this);
            _exclusionPointsXZ.Clear();
        }
    }

    /// <summary>
    /// Populates and shuffles the queue of objects to be placed based on their counts.
    /// </summary>
    void PreparePlacementQueue()
    {
        _placementQueue.Clear();
        foreach (ObjectPlacementEntry entry in objectsToPlace)
        {
            entry.OnValidate();
            if (entry.prefab == null || entry.count <= 0)
            {
                if (entry.prefab == null) Debug.LogWarning("An entry in 'Objects To Place' has a null prefab and will be skipped.", this);
                else Debug.LogWarning($"Prefab '{entry.prefab.name}' has a count of {entry.count} and will not be placed.", this);
                continue;
            }

            for (int i = 0; i < entry.count; i++)
            {
                _placementQueue.Add(entry);
            }
        }
        ShuffleList(_placementQueue);

        if (_placementQueue.Count == 0)
        {
            Debug.LogError("Placement queue is empty. No objects will be placed.", this);
            enabled = false;
        }
    }

    /// <summary>
    /// Places objects from the queue into the room, checking for overlaps and exclusions.
    /// </summary>
    void PlaceObjectsInRoomFlat()
    {
        // Clean up previously placed objects
        foreach (GameObject obj in placedObjects)
        {
            if (Application.isEditor)
                DestroyImmediate(obj);
            else
                Destroy(obj);
        }
        placedObjects.Clear();

        foreach (ObjectPlacementEntry currentEntryToPlace in _placementQueue)
        {
            GameObject prefabToPlace = currentEntryToPlace.prefab;
            Collider prefabCollider = prefabToPlace.GetComponent<Collider>();

            if (prefabCollider == null)
            {
                Debug.LogWarning($"Prefab '{prefabToPlace.name}' is missing a Collider and will be skipped.", prefabToPlace);
                continue;
            }

            bool placedSuccessfully = false;
            for (int attempts = 0; attempts < maxPlacementAttempts; attempts++)
            {
                // Generate random position and properties using the PER-OBJECT settings
                float randomLocalX = Random.Range(-currentEntryToPlace.placementExtents.x, currentEntryToPlace.placementExtents.x);
                float randomLocalZ = Random.Range(-currentEntryToPlace.placementExtents.z, currentEntryToPlace.placementExtents.z);
                float randomScale = Random.Range(currentEntryToPlace.minScale, currentEntryToPlace.maxScale);
                Vector3 proposedScale = Vector3.one * randomScale;
                Quaternion proposedRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

                // --- Calculate Y position using the PER-OBJECT setting ---
                GameObject tempObject = Instantiate(prefabToPlace);
                tempObject.transform.localScale = proposedScale;
                Collider tempCollider = tempObject.GetComponent<Collider>();
                float objectHalfHeight = tempCollider.bounds.extents.y;
                Destroy(tempObject); 

                float actualLocalYForPivot = currentEntryToPlace.surfaceLocalY + objectHalfHeight;
                Vector3 randomLocalPosition = new Vector3(randomLocalX, actualLocalYForPivot, randomLocalZ);
                Vector3 proposedWorldPosition = roomGameObject.transform.TransformPoint(randomLocalPosition);

                // --- Overlap Checks ---
                if (!IsOverlapping(proposedWorldPosition, prefabCollider, randomScale, currentEntryToPlace))
                {
                    // If no overlaps, instantiate the object
                    GameObject placedObject = Instantiate(prefabToPlace, proposedWorldPosition, proposedRotation);
                    placedObject.transform.localScale = proposedScale;
                    placedObject.transform.parent = roomGameObject.transform;
                    placedObjects.Add(placedObject);
                    placedSuccessfully = true;
                    break; 
                }
            }

            if (!placedSuccessfully)
            {
                Debug.LogWarning($"Could not place '{prefabToPlace.name}' after {maxPlacementAttempts} attempts.", this);
            }
        }
    }

    /// <summary>
    /// Checks if a proposed object position overlaps with existing objects or exclusion zones.
    /// </summary>
    /// <returns>True if there is an overlap, false otherwise.</returns>
    private bool IsOverlapping(Vector3 proposedWorldPosition, Collider prefabCollider, float proposedScale, ObjectPlacementEntry currentEntry)
    {
        // Calculate the radius of the new object
        float newObjectRadius = Mathf.Max(prefabCollider.bounds.extents.x, prefabCollider.bounds.extents.z) * proposedScale;
        Vector3 newObjectHorizontalPos = new Vector3(proposedWorldPosition.x, 0, proposedWorldPosition.z);

        // 1. Check against already placed objects
        foreach (GameObject existingObject in placedObjects)
        {
            Collider existingCollider = existingObject.GetComponent<Collider>();
            if (existingCollider == null) continue;

            float existingObjectRadius = Mathf.Max(existingCollider.bounds.extents.x, existingCollider.bounds.extents.z) * existingObject.transform.localScale.x;
            Vector3 existingObjectHorizontalPos = new Vector3(existingObject.transform.position.x, 0, existingObject.transform.position.z);
            
            float requiredSeparation = newObjectRadius + existingObjectRadius + minDistanceBetweenObjects;

            if (Vector3.Distance(newObjectHorizontalPos, existingObjectHorizontalPos) < requiredSeparation)
            {
                return true; // Overlap found
            }
        }

        // 2. Check against JSON exclusion zones using the PER-OBJECT exclusion radius
        foreach (Vector3 exclusionPointXZ in _exclusionPointsXZ)
        {
            if (Vector3.Distance(newObjectHorizontalPos, exclusionPointXZ) < (newObjectRadius + currentEntry.exclusionRadius))
            {
                return true; // Overlap found
            }
        }

        return false; // No overlaps found
    }

    /// <summary>
    /// Shuffles a list using the Fisher-Yates algorithm.
    /// </summary>
    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}
