using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SimpleJSON;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A unified, two-stage tool for animation processing.
/// Stage 1: An interactive UI for tuning and saving animation offsets.
/// Stage 2: An automated batch process to record videos, point clouds, and a final report.
/// </summary>
public class BatchProcessor : MonoBehaviour
{
    [Header("Reporting Settings")]
    [Tooltip("The name of the root GameObject in the scene that contains the environment to be reported (e.g. 'Bedroom').")]
    public string sceneRootObjectName = "Bedroom";

    [Header("Component References")]
    public SMPLAnimationPlayer smplPlayer;
    public DynamicSurfaceSampler surfaceSampler;
    public SceneRecorder sceneRecorder;
    [Tooltip("A list of cameras to use for recording. The active camera can be switched in the UI.")]
    public List<Camera> cameras = new List<Camera>();

    [Header("Batch Settings")]
    [Tooltip("Path to the folder of JSON animations, relative to the StreamingAssets folder.")]
    public string animationsSubfolderPath = "Animations";
    [Tooltip("The name of the JSON file in StreamingAssets for reading initial/default offsets.")]
    public string initialOffsetsConfigName = "animation_offsets.json";
    [Tooltip("The name of the JSON file for reading/writing the list of excluded animations.")]
    public string excludedAnimationsConfigName = "excluded_animations.json";
    [Tooltip("Name of the environment folder inside the base output directory (e.g., 'Bathroom').")]
    public string environmentFolderName = "bathroom";
    [Tooltip("The specific number for the environment instance (e.g., '001' for 'Bathroom_001').")]
    public string environmentFolderNumber = "001";
    [Tooltip("The name of the scene folder where final offsets and output will be saved.")]
    public string sceneFolderName = "scene1";
    [Tooltip("Should the application quit after the batch process is complete?")]
    public bool quitOnFinish = true;

    [Header("Snapping Settings")]
    [Tooltip("The layer mask representing the floor, used for the 'Snap to Floor' feature.")]
    public LayerMask floorLayer;

    [Header("Dynamic Placement Settings")]
    [Tooltip("Reference to the CharacterController on the SMPL model for collision checks.")]
    public CharacterController characterController;
    [Tooltip("The layers that the character should consider as obstacles.")]
    public LayerMask placementCollisionLayerMask;
    [Tooltip("The size of the area (in meters) to search for a valid placement, centered on the character's current position.")]
    public Vector2 placementSearchAreaSize = new Vector2(5f, 5f);
    [Tooltip("The distance between each test point in the search grid (in meters). Smaller values are more accurate but slower.")]
    public float placementSearchGridDensity = 0.25f;
    [Tooltip("The angular step (in degrees) to test rotations during placement search. Set to 0 or less to only test the current rotation from the slider.")]
    public float placementRotationSearchStep = 10f;


    // --- Private State ---
    private enum EditorState { Tuning, BatchProcessing }
    private EditorState currentState = EditorState.Tuning;

    private List<string> animationFiles = new List<string>();
    private Dictionary<string, Vector3> tunedOffsets = new Dictionary<string, Vector3>();
    private Dictionary<string, float> tunedRotations = new Dictionary<string, float>();
    private Dictionary<string, bool> includedAnimations = new Dictionary<string, bool>();
    private int currentAnimationIndex = -1;
    private Vector3 liveOffset;
    private float liveRotationY;
    private bool isDirty = false;
    private int activeCameraIndex = 0;
    private string currentAnimationDescription = "No description available.";
    private bool isSearchingForPlacement = false;
    private bool isAutoPlacing = false; // Flag for the new automated placement process
    private string autoPlacementStatus = ""; // Status message for the UI

    // --- Helper properties to generate the new folder structure consistently ---
    /// <summary>Formats the environment name with its number, e.g., "Bathroom_001".</summary>
    private string FormattedEnvironmentName => $"{environmentFolderName}_{environmentFolderNumber}";
    /// <summary>Gets the base output path for the current scene, e.g., "Scans/Bathroom/Bathroom_001/scene1".</summary>
    private string BaseOutputPathForScene => Path.Combine(surfaceSampler.baseOutputDirectory, environmentFolderName, FormattedEnvironmentName, sceneFolderName);
    /// <summary>Gets the directory path for summary reports, e.g., "Scans/Bathroom/Bathroom_001/scene1/reports".</summary>
    private string ReportDirectoryPath => Path.Combine(BaseOutputPathForScene, "reports");


    void Start()
    {
        if (smplPlayer == null || surfaceSampler == null || sceneRecorder == null)
        {
            Debug.LogError("BatchProcessor Error: SMPLPlayer, SurfaceSampler, or SceneRecorder is not assigned!");
            return;
        }
        
        if (cameras.Count == 0)
        {
            Debug.Log("No cameras assigned in the Inspector. Attempting to find Camera.main.");
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                cameras.Add(mainCamera);
            }
            else
            {
                Debug.LogError("No cameras assigned and could not find a camera tagged 'MainCamera'!");
            }
        }

        SetActiveCamera(activeCameraIndex);

        PopulateAnimationList();
        LoadInitialOffsets();
        LoadExcludedAnimations();
        LoadAnimationForTuning(0);
    }

    // ===================================================================
    // =========== STAGE 1: INTERACTIVE TUNING ===========================
    // ===================================================================

    private void SwitchToNextCamera()
    {
        if (cameras.Count <= 1) return;
        activeCameraIndex = (activeCameraIndex + 1) % cameras.Count;
        SetActiveCamera(activeCameraIndex);
        Debug.Log($"Switched to camera: {cameras[activeCameraIndex].name}");
    }

    private void SetActiveCamera(int index)
    {
        if (cameras.Count == 0 || index < 0 || index >= cameras.Count) return;
        for (int i = 0; i < cameras.Count; i++)
        {
            if (cameras[i] != null)
            {
                cameras[i].gameObject.SetActive(i == index);
            }
        }
        activeCameraIndex = index;
    }

    private void SnapToFloor()
    {
        if (smplPlayer == null) return;
        Transform armatureTransform = smplPlayer.transform.Find("Armature"); 
        if (armatureTransform == null)
        {
            Debug.LogError("Could not find 'Armature' child object on the SMPL model. Cannot snap to floor.");
            return;
        }
        RaycastHit hit;
        if (Physics.Raycast(armatureTransform.position, Vector3.down, out hit, 10f, floorLayer))
        {
            float distanceToFloor = armatureTransform.position.y - hit.point.y;
            liveOffset.y -= distanceToFloor;
            string animFileName = Path.GetFileNameWithoutExtension(animationFiles[currentAnimationIndex]);
            tunedOffsets[animFileName] = liveOffset;
            smplPlayer.UpdateLiveOffset(liveOffset);
            isDirty = true;
            Debug.Log($"Snapped Armature to floor. Adjusted Y-offset by {-distanceToFloor:F3}. New Y-offset: {liveOffset.y:F3}");
        }
        else
        {
            Debug.LogWarning("Snap to Floor failed: Raycast did not hit any object on the specified floor layer.");
        }
    }

    private void LoadInitialOffsets()
    {
        string masterConfigPath = Path.Combine(Application.streamingAssetsPath, initialOffsetsConfigName);
        if (File.Exists(masterConfigPath))
        {
            try
            {
                string jsonText = File.ReadAllText(masterConfigPath);
                var json = JSON.Parse(jsonText);
                foreach (KeyValuePair<string, JSONNode> animConfig in json)
                {
                    float y = animConfig.Value["y"] != null ? (float)animConfig.Value["y"] : 0f;
                    tunedOffsets[animConfig.Key] = new Vector3(animConfig.Value["x"], y, animConfig.Value["z"]);
                    tunedRotations[animConfig.Key] = animConfig.Value["y_rotation"];
                }
                Debug.Log($"Successfully loaded {tunedOffsets.Count} initial offsets from master config '{initialOffsetsConfigName}'.");
            }
            catch (Exception e) { Debug.LogError($"Error parsing {initialOffsetsConfigName}: {e.Message}"); }
        }
        else { Debug.LogWarning($"Master offsets file not found at {masterConfigPath}. This is optional."); }

        Debug.Log("Checking for individually tuned offsets in the output directory...");
        int overrideCount = 0;
        foreach (string filePath in animationFiles)
        {
            string animFileName = Path.GetFileNameWithoutExtension(filePath);
            string offsetFilePath = Path.Combine(BaseOutputPathForScene, animFileName, "animation_offsets.json");

            if (File.Exists(offsetFilePath))
            {
                try
                {
                    string jsonText = File.ReadAllText(offsetFilePath);
                    var offsetJson = JSON.Parse(jsonText);
                    if (offsetJson[animFileName] != null)
                    {
                        var animConfig = offsetJson[animFileName];
                        float y = animConfig["y"] != null ? (float)animConfig["y"] : 0f;
                        tunedOffsets[animFileName] = new Vector3(animConfig["x"], y, animConfig["z"]);
                        tunedRotations[animFileName] = animConfig["y_rotation"];
                        overrideCount++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Could not parse individual offset file for '{animFileName}': {e.Message}");
                }
            }
        }
        if (overrideCount > 0)
        {
            Debug.Log($"Loaded and applied {overrideCount} individually tuned offsets, overriding defaults.");
        }
    }
    
    private void LoadExcludedAnimations()
    {
        string configPath = Path.Combine(ReportDirectoryPath, excludedAnimationsConfigName);
        if (File.Exists(configPath))
        {
            try
            {
                string jsonText = File.ReadAllText(configPath);
                var json = JSON.Parse(jsonText);
                JSONArray excludedArray = json["excluded_animations"].AsArray;

                if (excludedArray != null)
                {
                    int loadCount = 0;
                    foreach (JSONNode animNameNode in excludedArray)
                    {
                        string animFileName = animNameNode.Value;
                        if (includedAnimations.ContainsKey(animFileName))
                        {
                            includedAnimations[animFileName] = false;
                            loadCount++;
                        }
                    }
                    Debug.Log($"Successfully loaded and applied {loadCount} exclusions from {configPath}.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error parsing {excludedAnimationsConfigName}: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"Excluded animations file not found at {configPath}. All animations will be included by default.");
        }
    }

    private void PopulateAnimationList()
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, animationsSubfolderPath);
        if (!Directory.Exists(fullPath)) return;
        animationFiles = Directory.GetFiles(fullPath)
            .Where(file => file.EndsWith(".json") || file.EndsWith(".txt"))
            .ToList();
        includedAnimations.Clear();
        foreach(string filePath in animationFiles)
        {
            string animFileName = Path.GetFileNameWithoutExtension(filePath);
            includedAnimations[animFileName] = true;
        }
    }

    private void LoadAnimationForTuning(int index)
    {
        if (animationFiles.Count == 0) return;
        currentAnimationIndex = Mathf.Clamp(index, 0, animationFiles.Count - 1);
        string filePath = animationFiles[currentAnimationIndex];
        string animFileName = Path.GetFileNameWithoutExtension(filePath);
        
        try
        {
            string jsonText = File.ReadAllText(filePath);
            var animJson = JSON.Parse(jsonText);
            currentAnimationDescription = animJson["description"] ?? "No description available.";
        }
        catch (Exception e)
        {
            Debug.LogError($"Error reading animation file {animFileName}: {e.Message}");
            currentAnimationDescription = "Error loading description.";
        }

        Vector3 offsetToLoad = tunedOffsets.ContainsKey(animFileName) ? tunedOffsets[animFileName] : Vector3.zero;
        float rotationToLoad = tunedRotations.ContainsKey(animFileName) ? tunedRotations[animFileName] : 0f;
        
        liveOffset = offsetToLoad;
        liveRotationY = rotationToLoad;
        smplPlayer.LoadAndPlayAnimation(filePath, offsetToLoad, rotationToLoad);
    }

    private void SaveCurrentOffset()
    {
        if (currentAnimationIndex < 0) return;

        string animFileName = Path.GetFileNameWithoutExtension(animationFiles[currentAnimationIndex]);
        Vector3 offset = liveOffset;
        float rotation = liveRotationY;
        tunedOffsets[animFileName] = offset;
        tunedRotations[animFileName] = rotation;

        string outputDirectory = Path.Combine(BaseOutputPathForScene, animFileName);
        string savePath = Path.Combine(outputDirectory, "animation_offsets.json");
        Directory.CreateDirectory(outputDirectory);

        JSONNode rootNode = new JSONObject();
        JSONObject offsetNode = new JSONObject { ["x"] = offset.x, ["y"] = offset.y, ["z"] = offset.z, ["y_rotation"] = rotation };
        rootNode[animFileName] = offsetNode;

        File.WriteAllText(savePath, rootNode.ToString(4));
        Debug.Log($"Saved current offset for '{animFileName}' to: {savePath}");
    }

    private void SaveAllModifiedOffsets()
    {
        if (tunedOffsets.Count == 0)
        {
            Debug.Log("No offset changes to save.");
            return;
        }

        Debug.Log($"Attempting to save offsets for {tunedOffsets.Count} modified animations...");
        int savedCount = 0;
        foreach (var animFileName in tunedOffsets.Keys)
        {
            if (includedAnimations.ContainsKey(animFileName) && includedAnimations[animFileName])
            {
                if (tunedRotations.ContainsKey(animFileName))
                {
                    Vector3 offset = tunedOffsets[animFileName];
                    float rotation = tunedRotations[animFileName];

                    string outputDirectory = Path.Combine(BaseOutputPathForScene, animFileName);
                    string savePath = Path.Combine(outputDirectory, "animation_offsets.json");
                    Directory.CreateDirectory(outputDirectory);

                    JSONNode rootNode = new JSONObject();
                    JSONObject offsetNode = new JSONObject { ["x"] = offset.x, ["y"] = offset.y, ["z"] = offset.z, ["y_rotation"] = rotation };
                    rootNode[animFileName] = offsetNode;

                    File.WriteAllText(savePath, rootNode.ToString(4));
                    savedCount++;
                }
            }
            else
            {
                Debug.Log($"Skipping saving offset for excluded animation: {animFileName}");
            }
        }
        Debug.Log($"Successfully saved offsets for {savedCount} included animations.");
    }

    private void SaveExcludedAnimationsConfig()
    {
        string outputDirectory = ReportDirectoryPath;
        string savePath = Path.Combine(outputDirectory, excludedAnimationsConfigName);
        try
        {
            Directory.CreateDirectory(outputDirectory);
            JSONObject rootNode = new JSONObject();
            JSONArray excludedArray = new JSONArray();

            foreach (var pair in includedAnimations)
            {
                if (!pair.Value)
                {
                    excludedArray.Add(pair.Key);
                }
            }

            rootNode["excluded_animations"] = excludedArray;
            File.WriteAllText(savePath, rootNode.ToString(4));
            Debug.Log($"Saved excluded animations list to: {savePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save excluded animations config: {e.Message}");
        }
    }

    void OnGUI()
    {
        if (currentState != EditorState.Tuning || animationFiles.Count == 0) return;
        
        GUIStyle descriptionStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            padding = new RectOffset(5, 5, 5, 5)
        };

        GUI.Box(new Rect(10, 10, 330, 550), "Animation Tuner & Batcher"); // Increased box height
        string animFileName = Path.GetFileNameWithoutExtension(animationFiles[currentAnimationIndex]);
        
        float yPos = 40;

        // --- Controls that should be disabled during automated processes ---
        bool controlsEnabled = !isSearchingForPlacement && !isAutoPlacing;
        GUI.enabled = controlsEnabled;

        bool isIncluded = includedAnimations[animFileName];
        includedAnimations[animFileName] = GUI.Toggle(new Rect(20, yPos, 300, 20), isIncluded, $" Process Animation: {animFileName}");
        yPos += 30;

        GUI.Label(new Rect(20, yPos, 300, 60), currentAnimationDescription, descriptionStyle);
        yPos += 70;

        GUI.Label(new Rect(20, yPos, 40, 20), $"X: {liveOffset.x:F2}");
        liveOffset.x = GUI.HorizontalSlider(new Rect(70, yPos + 2.5f, 260, 20), liveOffset.x, -10f, 10f);
        yPos += 30;
        
        GUI.Label(new Rect(20, yPos, 40, 20), $"Y: {liveOffset.y:F2}");
        liveOffset.y = GUI.HorizontalSlider(new Rect(70, yPos + 2.5f, 260, 20), liveOffset.y, -5f, 5f);
        yPos += 30;
        
        GUI.Label(new Rect(20, yPos, 40, 20), $"Z: {liveOffset.z:F2}");
        liveOffset.z = GUI.HorizontalSlider(new Rect(70, yPos + 2.5f, 260, 20), liveOffset.z, -10f, 10f);
        yPos += 30;
        
        GUI.Label(new Rect(20, yPos, 80, 20), $"Rot Y: {liveRotationY:F1}");
        liveRotationY = GUI.HorizontalSlider(new Rect(90, yPos + 2.5f, 240, 20), liveRotationY, -180f, 180f);
        yPos += 35;

        if (GUI.changed)
        {
            tunedOffsets[animFileName] = liveOffset;
            tunedRotations[animFileName] = liveRotationY;
            smplPlayer.UpdateLiveOffset(liveOffset);
            smplPlayer.UpdateLiveRotation(liveRotationY);
            isDirty = true;
        }

        if (GUI.Button(new Rect(20, yPos, 70, 20), "<< Prev")) LoadAnimationForTuning(currentAnimationIndex - 1);
        if (GUI.Button(new Rect(95, yPos, 70, 20), "Next >>")) LoadAnimationForTuning(currentAnimationIndex + 1);
        
        if (GUI.Button(new Rect(170, yPos, 95, 20), "Save Current"))
        {
            SaveCurrentOffset();
        }

        GUI.color = isDirty ? Color.green : Color.white;
        if (GUI.Button(new Rect(270, yPos, 60, 20), "Save All"))
        {
            SaveAllModifiedOffsets();
            SaveExcludedAnimationsConfig();
            isDirty = false;
        }
        GUI.color = Color.white;
        yPos += 30;
        
        if (GUI.Button(new Rect(20, yPos, 145, 20), "Snap to Floor"))
        {
            SnapToFloor();
        }

        GUI.enabled = true; // --- Re-enable GUI for process-specific controls ---

        if (isSearchingForPlacement)
        {
            GUI.backgroundColor = new Color(1f, 0.8f, 0.8f); // Reddish color for stop button
            if (GUI.Button(new Rect(175, yPos, 145, 20), "Stop Search"))
            {
                isSearchingForPlacement = false; // The coroutine will see this and stop
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            GUI.enabled = !isAutoPlacing; // Can't start a manual search while auto-placing
            if (GUI.Button(new Rect(175, yPos, 145, 20), "Find Valid Placement"))
            {
                StartCoroutine(FindAndApplyValidPlacementWithNavMesh(success => {
                    if (success) Debug.Log("Manual placement search was successful.");
                    else Debug.LogWarning("Manual placement search failed or was cancelled.");
                }));
            }
            GUI.enabled = true; // Reset
        }
        yPos += 30;
        
        // --- NEW: Automated Placement Section ---
        if (isAutoPlacing)
        {
            GUI.Label(new Rect(20, yPos, 215, 25), autoPlacementStatus);
            GUI.backgroundColor = new Color(1f, 0.8f, 0.8f); // Red
            if (GUI.Button(new Rect(240, yPos, 80, 25), "Stop"))
            {
                isAutoPlacing = false; // Signal the coroutine to stop
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            GUI.enabled = !isSearchingForPlacement; // Can't start auto-place if a manual search is running
            GUI.backgroundColor = new Color(0.8f, 0.9f, 1f); // Light blue
            if (GUI.Button(new Rect(20, yPos, 300, 25), "Run Auto-Placements"))
            {
                StartCoroutine(RunAutomatedPlacements());
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true; // Reset
        }
        yPos += 35;


        string cameraName = (cameras.Count > 0 && cameras[activeCameraIndex] != null) ? cameras[activeCameraIndex].name : "None";
        GUI.Label(new Rect(20, yPos, 200, 20), $"Active Camera: {cameraName}");
        if (GUI.Button(new Rect(240, yPos, 80, 20), "Next Cam"))
        {
            SwitchToNextCamera();
        }
        yPos += 30;

        GUI.enabled = controlsEnabled; // Re-apply main disable for final button

        int includedCount = includedAnimations.Values.Count(b => b);
        GUI.Label(new Rect(20, yPos, 300, 20), $"{includedCount} / {animationFiles.Count} animations selected for batch.");
        yPos += 30;

        GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
        if (GUI.Button(new Rect(20, yPos, 300, 40), $"Start Batch Process ({includedCount} items)"))
        {
            currentState = EditorState.BatchProcessing;
            StartCoroutine(RunAutomatedBatch());
        }
        GUI.backgroundColor = Color.white;
        
        GUI.enabled = true; // --- IMPORTANT: Final reset to ensure GUI is enabled for next frame ---
    }


    // ===================================================================
    // =========== DYNAMIC PLACEMENT =====================================
    // ===================================================================

    /// <summary>
    /// Pre-calculates the 2D bounding box ("footprint") of an animation's root motion.
    /// </summary>
    private Rect PrecomputeAnimationFootprint(string jsonFilePath)
    {
        try
        {
            string jsonText = File.ReadAllText(jsonFilePath);
            var json = JSON.Parse(jsonText);
            var transNode = json["trans"];
            if (transNode.Count == 0) return Rect.zero;

            Vector3 firstFrameMayaPos = new Vector3(transNode[0][0], transNode[0][1], transNode[0][2]);
            Vector3 animationStartOffset = smplPlayer.ConvertMayaToUnity(firstFrameMayaPos);

            float minX = 0, maxX = 0, minZ = 0, maxZ = 0;

            for (int frame = 0; frame < transNode.Count; frame++)
            {
                var mayaPos = new Vector3(transNode[frame][0], transNode[frame][1], transNode[frame][2]);
                Vector3 currentFrameUnityPos = smplPlayer.ConvertMayaToUnity(mayaPos);
                Vector3 displacement = currentFrameUnityPos - animationStartOffset;

                if (displacement.x < minX) minX = displacement.x;
                if (displacement.x > maxX) maxX = displacement.x;
                if (displacement.z < minZ) minZ = displacement.z;
                if (displacement.z > maxZ) maxZ = displacement.z;
            }
            return new Rect(minX, minZ, maxX - minX, maxZ - minZ);
        }
        catch (Exception e)
        {
            Debug.LogError($"Could not precompute animation footprint for {jsonFilePath}: {e.Message}");
            return Rect.zero;
        }
    }
    
    private IEnumerator FindAndApplyValidPlacementWithNavMesh(Action<bool> onComplete, int maxAttempts = 20)
    {
        if (currentAnimationIndex < 0)
        {
            Debug.LogError("No animation loaded. Cannot find placement.");
            onComplete?.Invoke(false);
            yield break;
        }

        isSearchingForPlacement = true;
        Debug.Log("Starting efficient NavMesh placement search...");
        
        string filePath = animationFiles[currentAnimationIndex];
        string animFileName = Path.GetFileNameWithoutExtension(filePath);
        Vector3 startOffset = smplPlayer.GetModelBaseInitialPosition();

        Bounds roomBounds = surfaceSampler.GetRoomBounds();
        if (roomBounds.size == Vector3.zero)
        {
            Debug.LogError("Could not determine room bounds. Aborting placement search.");
            isSearchingForPlacement = false;
            onComplete?.Invoke(false);
            yield break;
        }

        Rect animFootprint = PrecomputeAnimationFootprint(filePath);
        Debug.Log($"Animation footprint for {animFileName}: {animFootprint}");
        if (animFootprint.size == Vector2.zero)
        {
            Debug.LogError("Animation footprint could not be calculated. Aborting search.");
            isSearchingForPlacement = false;
            onComplete?.Invoke(false);
            yield break;
        }

        for (int i = 0; i < maxAttempts; i++)
        {
            if (!isSearchingForPlacement) 
            {
                Debug.Log("Placement search was cancelled by the user.");
                onComplete?.Invoke(false);
                yield break;
            }

            float randomX = UnityEngine.Random.Range(roomBounds.min.x, roomBounds.max.x);
            float randomZ = UnityEngine.Random.Range(roomBounds.min.z, roomBounds.max.z);
            Vector3 randomPoint = new Vector3(randomX, roomBounds.center.y, randomZ);

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 100.0f, NavMesh.AllAreas))
            {
                Vector3 potentialStartPos = hit.position;
                for (float rotY = 0; rotY < 360; rotY += placementRotationSearchStep)
                {
                    if (!isSearchingForPlacement) 
                    {
                        Debug.Log("Placement search was cancelled by the user.");
                        onComplete?.Invoke(false);
                        yield break; 
                    }

                    Quaternion rotation = Quaternion.Euler(0, rotY, 0);

                    Vector3 corner1 = potentialStartPos + rotation * new Vector3(animFootprint.xMin, 0, animFootprint.yMin);
                    Vector3 corner2 = potentialStartPos + rotation * new Vector3(animFootprint.xMax, 0, animFootprint.yMin);
                    Vector3 corner3 = potentialStartPos + rotation * new Vector3(animFootprint.xMin, 0, animFootprint.yMax);
                    Vector3 corner4 = potentialStartPos + rotation * new Vector3(animFootprint.xMax, 0, animFootprint.yMax);

                    NavMeshHit cornerHit;
                    if (NavMesh.SamplePosition(corner1, out cornerHit, 0.1f, NavMesh.AllAreas) &&
                        NavMesh.SamplePosition(corner2, out cornerHit, 0.1f, NavMesh.AllAreas) &&
                        NavMesh.SamplePosition(corner3, out cornerHit, 0.1f, NavMesh.AllAreas) &&
                        NavMesh.SamplePosition(corner4, out cornerHit, 0.1f, NavMesh.AllAreas))
                    {
                        if (CheckAnimationPathOnNavMesh(filePath, potentialStartPos, rotY))
                        {
                            potentialStartPos.y = potentialStartPos.y - 0.1f ;
                            Debug.Log($"Found valid NavMesh placement at {potentialStartPos} on attempt {i + 1}.");

                            liveOffset = potentialStartPos - startOffset;
                            liveRotationY = rotY;
                            tunedOffsets[animFileName] = liveOffset;
                            tunedRotations[animFileName] = liveRotationY;

                            smplPlayer.UpdateLiveOffset(liveOffset);
                            smplPlayer.UpdateLiveRotation(liveRotationY);
                            isDirty = true;
                            isSearchingForPlacement = false;
                            onComplete?.Invoke(true); // Success!
                            yield break; 
                        }
                    }
                }
            }
            yield return null; 
        }

        Debug.LogWarning($"Could not find a valid placement for '{animFileName}' after {maxAttempts} attempts.");
        isSearchingForPlacement = false;
        onComplete?.Invoke(false); // Failure
    }

    /// <summary>
    /// Coroutine to automatically find and save placements for all animations.
    /// It iterates through each animation, attempts to find a valid placement,
    /// saves it on success, and marks it for exclusion on failure.
    /// </summary>
    private IEnumerator RunAutomatedPlacements()
    {
        isAutoPlacing = true;
        autoPlacementStatus = "Starting...";
        Debug.Log("====== STARTING AUTOMATED PLACEMENT PROCESS ======");
        
        var animationsToProcess = new List<string>(animationFiles);
        var excludedByThisRun = new List<string>();
        var placedByThisRun = new List<string>();

        for (int i = 0; i < animationsToProcess.Count; i++)
        {
            if (!isAutoPlacing)
            {
                Debug.LogWarning("Automated placement process was stopped by the user.");
                break; // Exit the loop if user cancelled
            }

            string animFileName = Path.GetFileNameWithoutExtension(animationsToProcess[i]);
            autoPlacementStatus = $"({i + 1}/{animationsToProcess.Count}): {animFileName}";
            Debug.Log($"--- Attempting to place animation {i + 1}/{animationsToProcess.Count}: {animFileName} ---");

            LoadAnimationForTuning(i);
            yield return new WaitForSeconds(0.1f);

            bool placementFound = false;
            // Use a callback to get the success/failure result from the search coroutine
            yield return StartCoroutine(FindAndApplyValidPlacementWithNavMesh(success => {
                placementFound = success;
            }, maxAttempts: 50)); // More attempts for the automated process

            if (placementFound)
            {
                Debug.Log($"Successfully found placement for '{animFileName}'. Saving offsets.");
                SaveCurrentOffset();
                placedByThisRun.Add(animFileName);
            }
            else
            {
                Debug.LogWarning($"Failed to find placement for '{animFileName}'. Marking as excluded.");
                includedAnimations[animFileName] = false;
                excludedByThisRun.Add(animFileName);
            }

            yield return null; // Yield a frame to let UI update and check for stop signal
        }
        
        Debug.Log("Saving the updated list of excluded animations...");
        SaveExcludedAnimationsConfig();
        isDirty = true;
        
        Debug.Log("====== AUTOMATED PLACEMENT PROCESS COMPLETE! ======");
        Debug.Log($"Successfully placed: {placedByThisRun.Count} animations.");
        Debug.Log($"Failed to place (and excluded): {excludedByThisRun.Count} animations.");

        // Cleanup
        autoPlacementStatus = "";
        isAutoPlacing = false;

        // Reload the current animation to reflect any UI changes (e.g., the include/exclude toggle)
        if (currentAnimationIndex >= 0)
        {
            LoadAnimationForTuning(currentAnimationIndex);
        }
    }
    
    private bool CheckAnimationPathOnNavMesh(string jsonFilePath, Vector3 startWorldPos, float rotationY)
    {
        try
        {
            string jsonText = File.ReadAllText(jsonFilePath);
            var json = JSON.Parse(jsonText);
            var transNode = json["trans"];
            if (transNode.Count == 0) return true; // Empty animation is valid.

            Vector3 firstFrameMayaPos = new Vector3(transNode[0][0], transNode[0][1], transNode[0][2]);
            Vector3 animationStartOffset = smplPlayer.ConvertMayaToUnity(firstFrameMayaPos);
            Quaternion characterRotation = Quaternion.Euler(0, rotationY, 0);

            for (int frame = 0; frame < transNode.Count; frame++)
            {
                var mayaPos = new Vector3(transNode[frame][0], transNode[frame][1], transNode[frame][2]);
                Vector3 currentFrameUnityPos = smplPlayer.ConvertMayaToUnity(mayaPos);
                Vector3 displacement = currentFrameUnityPos - animationStartOffset;
                Vector3 predictedWorldPos = startWorldPos + (characterRotation * displacement);

                Vector3 groundCheckPos = new Vector3(predictedWorldPos.x, startWorldPos.y, predictedWorldPos.z);

                NavMeshHit navHit;
                if (!NavMesh.SamplePosition(groundCheckPos, out navHit, 0.1f, NavMesh.AllAreas))
                {
                    return false; 
                }
            }
        }
        catch(Exception e)
        {
            Debug.LogError($"Error checking animation path on NavMesh: {e.Message}");
            return false; // Assume failure on error.
        }

        return true; // Success! All frames were on the NavMesh.
    }

    // ===================================================================
    // =========== STAGE 2: AUTOMATED BATCH RUNNER =======================
    // ===================================================================
    private IEnumerator RunAutomatedBatch()
    {
        Debug.Log("====== SWITCHING TO AUTOMATED BATCH MODE ======");
        
        if (smplPlayer != null)
        {
            smplPlayer.loop = false;
        }

        if (surfaceSampler != null)
        {
            if (surfaceSampler.saveStaticPointCloudSeparately)
            {
                Debug.Log("Saving static point cloud separately...");
                string scenePath = Path.Combine(environmentFolderName, FormattedEnvironmentName, sceneFolderName);
                surfaceSampler.CaptureAndSaveStaticPointCloud(scenePath);
                surfaceSampler.ClearCachedStaticPoints();
            }
            else
            {
                Debug.Log("Pre-sampling static scene once to be included in all animation frames...");
                surfaceSampler.SampleAndCacheStaticScene();
            }
        }

        JSONObject summaryReport = CreateReportHeader();
        JSONArray processedAnimationsReport = new JSONArray();
        summaryReport["processedAnimations"] = processedAnimationsReport;

        foreach (string filePath in animationFiles)
        {
            string animFileName = Path.GetFileNameWithoutExtension(filePath);
            if (!includedAnimations.ContainsKey(animFileName) || !includedAnimations[animFileName])
            {
                Debug.Log($"--- Skipping '{animFileName}' as it is excluded from the batch process. ---");
                continue;
            }

            Debug.Log($"--- Starting batch process for: {animFileName} ---");

            Vector3 finalOffset = Vector3.zero;
            float finalRotation = 0f;
            string description = "No description found.";
            string offsetFilePath = Path.Combine(BaseOutputPathForScene, animFileName, "animation_offsets.json");
            JSONNode animJson = null;
            
            float calculatedDuration = -1f;
            float fps = 20f; // Default FPS

            try
            {
                animJson = JSON.Parse(File.ReadAllText(filePath));
                if (animJson["description"] != null) description = animJson["description"];

                if (animJson["num_frames"] != null && animJson["fps"] != null)
                {
                    int numFrames = animJson["num_frames"];
                    fps = animJson["fps"];
                    if (fps > 0)
                    {
                        calculatedDuration = numFrames / fps;
                        Debug.Log($"'{animFileName}' duration calculated: {numFrames} frames / {fps} FPS = {calculatedDuration:F2} seconds.");
                    }
                }
                else
                {
                    Debug.LogWarning($"'{animFileName}' is missing 'num_frames' or 'fps' key. Duration cannot be calculated accurately.");
                }
                
                if (File.Exists(offsetFilePath))
                {
                    var offsetJson = JSON.Parse(File.ReadAllText(offsetFilePath));
                    if (offsetJson[animFileName] != null)
                    {
                        finalOffset.x = offsetJson[animFileName]["x"];
                        finalOffset.y = offsetJson[animFileName]["y"] != null ? (float)offsetJson[animFileName]["y"] : 0f;
                        finalOffset.z = offsetJson[animFileName]["z"];
                        finalRotation = offsetJson[animFileName]["y_rotation"];
                    }
                }
                else { Debug.LogWarning($"No tuned offset file found for '{animFileName}'. Using default offset/rotation."); }
            }
            catch (Exception e) { Debug.LogError($"Error loading data for {animFileName}: {e.Message}"); }

            JSONObject animSummaryReport = new JSONObject();
            animSummaryReport["animationName"] = animFileName;
            animSummaryReport["tunedOffset"] = new JSONObject { ["x"] = finalOffset.x, ["y"] = finalOffset.y, ["z"] = finalOffset.z };
            animSummaryReport["tunedRotationY"] = finalRotation;
            processedAnimationsReport.Add(animSummaryReport);

            JSONObject individualReport = new JSONObject();
            individualReport["animationName"] = animFileName;
            individualReport["description"] = description;
            individualReport["tunedOffset"] = new JSONObject { ["x"] = finalOffset.x, ["y"] = finalOffset.y, ["z"] = finalOffset.z };
            individualReport["tunedRotationY"] = finalRotation;
            if (animJson != null && animJson["prompt_segments"] != null)
            {
                JSONArray newSegments = new JSONArray();
                foreach (var segmentNode in animJson["prompt_segments"].AsArray)
                {
                    var segment = segmentNode.Value;
                    JSONObject segObj = new JSONObject();
                    segObj["prompt"] = segment["prompt"];
                    segObj["label"] = segment["label"];
                    segObj["start_frame"] = segment["start_frame"];
                    segObj["end_frame"] = segment["end_frame"];
                    segObj["num_frames"] = segment["num_frames"];
                    newSegments.Add(segObj);
                }
                individualReport["prompt_segments"] = newSegments;
            }

            string samplerSubfolderPath = Path.Combine(environmentFolderName, FormattedEnvironmentName, sceneFolderName, animFileName);
            string finalOutputDirectory = Path.Combine(BaseOutputPathForScene, animFileName);
            
            SaveIndividualReport(individualReport, finalOutputDirectory);

            Debug.Log($"Starting Pass 1: Point Cloud Sampling for '{animFileName}'.");
            smplPlayer.IsInBatchMode = true; 
            smplPlayer.LoadAndPlayAnimation(filePath, finalOffset, finalRotation);
            yield return StartCoroutine(surfaceSampler.StartSampling(samplerSubfolderPath, calculatedDuration, smplPlayer, fps));
            Debug.Log("Pass 1 (Point Cloud Sampling) complete.");

            if (calculatedDuration > 0)
            {
                Debug.Log($"Starting Pass 2: Video Recording for '{animFileName}'.");
                smplPlayer.IsInBatchMode = false; 
                smplPlayer.LoadAndPlayAnimation(filePath, finalOffset, finalRotation);
                yield return new WaitForSeconds(0.1f);

                string videoFilePath = Path.Combine(finalOutputDirectory, animFileName + ".mp4");
                sceneRecorder.BeginRecording(videoFilePath, fps);
                yield return new WaitForSeconds(calculatedDuration);
                sceneRecorder.EndRecording();
                Debug.Log("Pass 2 (Video Recording) complete.");
            }
            else
            {
                Debug.LogWarning($"Skipping video recording for '{animFileName}' because its duration could not be calculated.");
            }
            
            Debug.Log($"--- Finished batch process for: {animFileName} ---");
            Debug.Log($"--- Saved outputs to {finalOutputDirectory} ---");
        }
        
        JSONArray excludedAnimationsReport = new JSONArray();
        foreach (string animFileName in includedAnimations.Keys)
        {
            if (!includedAnimations[animFileName]) excludedAnimationsReport.Add(animFileName);
        }
        summaryReport["excludedAnimations"] = excludedAnimationsReport;
        
        SaveExcludedAnimationsConfig(); 
        SaveSummaryReport(summaryReport);
        
        if (smplPlayer != null)
        {
            smplPlayer.loop = true;
            smplPlayer.IsInBatchMode = false;
            smplPlayer.Stop();
        }

        Debug.Log("====== AUTOMATED BATCH PROCESS COMPLETE! ======");
        if (quitOnFinish)
        {
            Debug.Log("Quitting application.");
    #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
    #else
            Application.Quit();
    #endif
        }
    }

    // ===================================================================
    // =========== REPORTING =============================================
    // ===================================================================

    private JSONObject CreateReportHeader()
    {
        JSONObject report = new JSONObject();
        report["reportGenerated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        report["environmentName"] = environmentFolderName;
        report["environmentInstance"] = FormattedEnvironmentName; 
        report["sceneName"] = sceneFolderName;

        JSONObject batchSettings = new JSONObject();
        batchSettings["animationsSubfolderPath"] = animationsSubfolderPath;
        batchSettings["initialOffsetsConfigName"] = initialOffsetsConfigName;
        batchSettings["quitOnFinish"] = quitOnFinish;
        report["batchProcessorSettings"] = batchSettings;
        
        JSONObject smplSettings = new JSONObject();
        Transform smplTransform = smplPlayer.transform;
        smplSettings["initialPosition"] = new JSONObject { ["x"] = smplTransform.position.x, ["y"] = smplTransform.position.y, ["z"] = smplTransform.position.z };
        smplSettings["initialRotation"] = new JSONObject { ["x"] = smplTransform.rotation.x, ["y"] = smplTransform.rotation.y, ["z"] = smplTransform.rotation.z, ["w"] = smplTransform.rotation.w };
        smplSettings["initialScale"] = new JSONObject { ["x"] = smplTransform.localScale.x, ["y"] = smplTransform.localScale.y, ["z"] = smplTransform.localScale.z };
        smplSettings["loopAnimation"] = smplPlayer.loop;
        smplSettings["yOffset"] = smplPlayer.yOffset;
        smplSettings["playbackSpeed"] = smplPlayer.playbackSpeed;
        report["smplPlayerSettings"] = smplSettings;
        
        JSONObject samplerSettings = new JSONObject();
        samplerSettings["defaultPointsPerUnitArea"] = surfaceSampler.defaultPointsPerUnitArea;
        samplerSettings["roomObjectKeyword"] = surfaceSampler.roomObjectKeyword;
        samplerSettings["captureDuration"] = surfaceSampler.captureDuration;
        samplerSettings["captureFPS"] = surfaceSampler.captureFPS;
        samplerSettings["baseOutputDirectory"] = surfaceSampler.baseOutputDirectory;
        samplerSettings["fixedRandomSeed"] = surfaceSampler.fixedRandomSeed;
        samplerSettings["saveStaticPointCloudSeparately"] = surfaceSampler.saveStaticPointCloudSeparately;
        report["surfaceSamplerSettings"] = samplerSettings;
        
        JSONObject recorderSettings = new JSONObject();
        report["sceneRecorderSettings"] = recorderSettings;

        if (cameras.Count > 0 && cameras[activeCameraIndex] != null)
        {
            Camera activeCamera = cameras[activeCameraIndex];
            JSONObject cameraSettings = new JSONObject();
            Transform camTransform = activeCamera.transform;
            cameraSettings["name"] = activeCamera.name;
            cameraSettings["position"] = new JSONObject { ["x"] = camTransform.position.x, ["y"] = camTransform.position.y, ["z"] = camTransform.position.z };
            cameraSettings["rotation"] = new JSONObject { ["x"] = camTransform.eulerAngles.x, ["y"] = camTransform.eulerAngles.y, ["z"] = camTransform.eulerAngles.z };
            cameraSettings["fieldOfView"] = activeCamera.fieldOfView;
            cameraSettings["nearClipPlane"] = activeCamera.nearClipPlane;
            cameraSettings["farClipPlane"] = activeCamera.farClipPlane;
            cameraSettings["projectionType"] = activeCamera.orthographic ? "Orthographic" : "Perspective";
            report["cameraSettings"] = cameraSettings;
        }

    #if UNITY_EDITOR
        JSONObject packagesReport = new JSONObject();
        string manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                string manifestText = File.ReadAllText(manifestPath);
                var manifestJson = JSON.Parse(manifestText);
                var dependencies = manifestJson["dependencies"].AsObject;
                if (dependencies != null)
                {
                    foreach(KeyValuePair<string, JSONNode> dependency in dependencies)
                    {
                        packagesReport[dependency.Key] = dependency.Value;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to read or parse package manifest: {e.Message}");
                packagesReport["error"] = "Failed to read package manifest.";
            }
        }
        else
        {
            packagesReport["error"] = "Packages/manifest.json not found.";
        }
        report["installedPackages"] = packagesReport;

        JSONObject sceneObjectsReport = new JSONObject();
        GameObject sceneRootObject = GameObject.Find(sceneRootObjectName);

        if (sceneRootObject == null)
        {
            Debug.LogError($"Scene reporting failed: Could not find the root object named '{sceneRootObjectName}'.");
            sceneObjectsReport["error"] = $"Scene root object '{sceneRootObjectName}' not found.";
        }
        else if (!sceneRootObject.activeInHierarchy)
        {
            Debug.LogWarning($"Scene reporting skipped: The root object '{sceneRootObjectName}' is inactive.");
            sceneObjectsReport["status"] = $"Object '{sceneRootObjectName}' is inactive.";
        }
        else
        {
            JSONObject rootInfo = new JSONObject();
            rootInfo["instanceName"] = sceneRootObject.name;
            rootInfo["position"] = new JSONObject { ["x"] = sceneRootObject.transform.position.x, ["y"] = sceneRootObject.transform.position.y, ["z"] = sceneRootObject.transform.position.z };
            rootInfo["rotation"] = new JSONObject { ["x"] = sceneRootObject.transform.eulerAngles.x, ["y"] = sceneRootObject.transform.eulerAngles.y, ["z"] = sceneRootObject.transform.eulerAngles.z };
            rootInfo["scale"] = new JSONObject { ["x"] = sceneRootObject.transform.localScale.x, ["y"] = sceneRootObject.transform.localScale.y, ["z"] = sceneRootObject.transform.localScale.z };
            sceneObjectsReport["rootObject"] = rootInfo;

            JSONArray childrenArray = new JSONArray();
            foreach (Transform child in sceneRootObject.transform)
            {
                if (!child.gameObject.activeInHierarchy) continue;

                JSONObject childInfo = new JSONObject();
                childInfo["instanceName"] = child.name;
                childInfo["position"] = new JSONObject { ["x"] = child.position.x, ["y"] = child.position.y, ["z"] = child.position.z };
                childInfo["rotation"] = new JSONObject { ["x"] = child.eulerAngles.x, ["y"] = child.eulerAngles.y, ["z"] = child.eulerAngles.z };
                childInfo["scale"] = new JSONObject { ["x"] = child.localScale.x, ["y"] = child.localScale.y, ["z"] = child.localScale.z };

                if (PrefabUtility.IsPartOfAnyPrefab(child.gameObject))
                {
                    childInfo["assetPath"] = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                    JSONArray overrides = new JSONArray();
                    foreach (var added in PrefabUtility.GetAddedComponents(child.gameObject))
                    {
                        overrides.Add(new JSONObject { ["type"] = "Added Component", ["component"] = added.GetType().FullName });
                    }
                    PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(child.gameObject);
                    if (modifications != null)
                    {
                        foreach (var mod in modifications)
                        {
                            if (mod.target != null)
                            {
                                overrides.Add(new JSONObject { 
                                    ["type"] = "Modified Property", 
                                    ["component"] = mod.target.GetType().FullName,
                                    ["property"] = mod.propertyPath,
                                    ["value"] = mod.value
                                });
                            }
                        }
                    }
                    if(overrides.Count > 0)
                    {
                        childInfo["overrides"] = overrides;
                    }
                }
                else
                {
                    JSONObject details = new JSONObject();
                    details["status"] = "Not a Prefab";
                    MeshFilter mf = child.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("Built-in"))
                    {
                        details["reason"] = "Primitive Shape";
                        details["shape"] = mf.sharedMesh.name.Replace("Built-in\\/", "");
                    }
                    else if (child.GetComponents<Component>().Length == 1 && child.childCount > 0)
                    {
                        details["reason"] = "Organizational Object";
                    }
                    else
                    {
                        details["reason"] = "Generic GameObject";
                    }
                    childInfo["assetPath"] = details;
                }
                childrenArray.Add(childInfo);
            }
            sceneObjectsReport["childObjects"] = childrenArray;
        }
        report["sceneObjects"] = sceneObjectsReport;
    #endif
        return report;
    }

    private void SaveSummaryReport(JSONNode report)
    {
        try
        {
            string reportDirectory = ReportDirectoryPath;
            Directory.CreateDirectory(reportDirectory);
            string reportPath = Path.Combine(reportDirectory, "scene_summary_report.json");
            File.WriteAllText(reportPath, report.ToString(4));
            Debug.Log($"Successfully saved summary report to {reportPath}");
        }
        catch (Exception e) { Debug.LogError($"Failed to save summary report: {e.Message}"); }
    }

    private void SaveIndividualReport(JSONNode report, string outputDirectory)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string reportPath = Path.Combine(outputDirectory, "report.json");
            File.WriteAllText(reportPath, report.ToString(4));
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save individual report for {report["animationName"]}: {e.Message}");
        }
    }
}