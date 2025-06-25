using UnityEngine;
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
    [Header("Component References")]
    public SMPLAnimationPlayer smplPlayer;
    public DynamicSurfaceSampler surfaceSampler;
    public SceneRecorder sceneRecorder;
    [Tooltip("The main camera of the scene. If not assigned, it will try to find Camera.main.")]
    public Camera mainCamera;

    [Header("Batch Settings")]
    [Tooltip("Path to the folder of JSON animations, relative to the StreamingAssets folder.")]
    public string animationsSubfolderPath = "Animations";
    [Tooltip("The name of the JSON file in StreamingAssets for reading initial/default offsets.")]
    public string initialOffsetsConfigName = "animation_offsets.json";
    [Tooltip("Name of the environment folder inside the base output directory (e.g., 'bedroom').")]
    public string environmentFolderName = "bedroom";
    [Tooltip("The name of the scene folder where final offsets and output will be saved.")]
    public string sceneFolderName = "scene1";
    [Tooltip("Should the application quit after the batch process is complete?")]
    public bool quitOnFinish = true;

    // --- Private State ---
    private enum EditorState { Tuning, BatchProcessing }
    private EditorState currentState = EditorState.Tuning;

    private List<string> animationFiles = new List<string>();
    private Dictionary<string, Vector3> tunedOffsets = new Dictionary<string, Vector3>();
    private Dictionary<string, float> tunedRotations = new Dictionary<string, float>();
    private Dictionary<string, bool> includedAnimations = new Dictionary<string, bool>();
    private int currentAnimationIndex = -1;
    // *** MODIFIED ***: liveOffset is now a Vector3 to handle X, Y, and Z.
    private Vector3 liveOffset;
    private float liveRotationY;
    private bool isDirty = false;

    void Start()
    {
        if (smplPlayer == null || surfaceSampler == null || sceneRecorder == null)
        {
            Debug.LogError("BatchProcessor Error: SMPLPlayer, SurfaceSampler, or SceneRecorder is not assigned!");
            return;
        }
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("Main Camera is not assigned and could not be found with Camera.main tag!");
            }
        }
        LoadInitialOffsets();
        PopulateAnimationList();
        LoadAnimationForTuning(0);
    }

    // ===================================================================
    // =========== STAGE 1: INTERACTIVE TUNING ===========================
    // ===================================================================

    private void LoadInitialOffsets()
    {
        string configPath = Path.Combine(Application.streamingAssetsPath, initialOffsetsConfigName);
        if (File.Exists(configPath))
        {
            try
            {
                string jsonText = File.ReadAllText(configPath);
                var json = JSON.Parse(jsonText);
                foreach (KeyValuePair<string, JSONNode> animConfig in json)
                {
                    // *** MODIFIED ***: Load Y offset from config, default to 0 if it doesn't exist.
                    float y = animConfig.Value["y"] != null ? (float)animConfig.Value["y"] : 0f;
                    tunedOffsets[animConfig.Key] = new Vector3(animConfig.Value["x"], y, animConfig.Value["z"]);
                    tunedRotations[animConfig.Key] = animConfig.Value["y_rotation"];
                }
                Debug.Log($"Successfully loaded {tunedOffsets.Count} initial offsets from {initialOffsetsConfigName}.");
            }
            catch (Exception e) { Debug.LogError($"Error parsing {initialOffsetsConfigName}: {e.Message}"); }
        }
        else { Debug.LogWarning($"Initial offsets file not found at {configPath}. All animations will start with a (0,0,0) offset/rotation."); }
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
        
        Vector3 offsetToLoad = tunedOffsets.ContainsKey(animFileName) ? tunedOffsets[animFileName] : Vector3.zero;
        float rotationToLoad = tunedRotations.ContainsKey(animFileName) ? tunedRotations[animFileName] : 0f;
        
        // *** MODIFIED ***: Directly assign the loaded Vector3 offset.
        liveOffset = offsetToLoad;
        liveRotationY = rotationToLoad;
        smplPlayer.LoadAndPlayAnimation(filePath, offsetToLoad, rotationToLoad);
        isDirty = false;
    }

    private void SaveCurrentOffset()
    {
        if (currentAnimationIndex < 0) return;
        string animFileName = Path.GetFileNameWithoutExtension(animationFiles[currentAnimationIndex]);
        string outputDirectory = Path.Combine(surfaceSampler.baseOutputDirectory, environmentFolderName, animFileName, sceneFolderName);
        string savePath = Path.Combine(outputDirectory, "animation_offsets.json");
        Directory.CreateDirectory(outputDirectory);
        JSONNode rootNode = new JSONObject();
        
        // *** MODIFIED ***: Save X, Y, Z position and Y rotation.
        JSONObject offsetNode = new JSONObject { ["x"] = liveOffset.x, ["y"] = liveOffset.y, ["z"] = liveOffset.z, ["y_rotation"] = liveRotationY };
        rootNode[animFileName] = offsetNode;
        File.WriteAllText(savePath, rootNode.ToString(4));
        Debug.Log($"Saved offset and rotation for '{animFileName}' to: {savePath}");
        isDirty = false;
    }

    void OnGUI()
    {
        if (currentState != EditorState.Tuning || animationFiles.Count == 0) return;
        
        GUI.Box(new Rect(10, 10, 300, 310), "Animation Tuner & Batcher");
        string animFileName = Path.GetFileNameWithoutExtension(animationFiles[currentAnimationIndex]);
        
        bool isIncluded = includedAnimations[animFileName];
        isIncluded = GUI.Toggle(new Rect(20, 40, 280, 20), isIncluded, $" Process Animation: {animFileName}");
        includedAnimations[animFileName] = isIncluded;

        GUI.Label(new Rect(20, 70, 40, 20), $"X: {liveOffset.x:F2}");
        liveOffset.x = GUI.HorizontalSlider(new Rect(70, 75, 230, 20), liveOffset.x, -10f, 10f);
        
        // *** NEW ***: UI slider for the Y-axis offset.
        GUI.Label(new Rect(20, 100, 40, 20), $"Y: {liveOffset.y:F2}");
        liveOffset.y = GUI.HorizontalSlider(new Rect(70, 105, 230, 20), liveOffset.y, -5f, 5f);
        
        GUI.Label(new Rect(20, 130, 40, 20), $"Z: {liveOffset.z:F2}");
        liveOffset.z = GUI.HorizontalSlider(new Rect(70, 135, 230, 20), liveOffset.z, -10f, 10f);
        
        GUI.Label(new Rect(20, 160, 80, 20), $"Rot Y: {liveRotationY:F1}");
        liveRotationY = GUI.HorizontalSlider(new Rect(90, 165, 210, 20), liveRotationY, -180f, 180f);

        if (GUI.changed)
        {
            tunedOffsets[animFileName] = liveOffset; // Store full Vector3
            tunedRotations[animFileName] = liveRotationY;
            smplPlayer.UpdateLiveOffset(liveOffset); // Pass full Vector3
            smplPlayer.UpdateLiveRotation(liveRotationY);
            isDirty = true;
        }

        if (GUI.Button(new Rect(20, 195, 80, 20), "<< Prev")) LoadAnimationForTuning(currentAnimationIndex - 1);
        if (GUI.Button(new Rect(110, 195, 80, 20), "Next >>")) LoadAnimationForTuning(currentAnimationIndex + 1);
        GUI.color = isDirty ? Color.green : Color.white;
        if (GUI.Button(new Rect(200, 195, 100, 20), "Save All")) SaveCurrentOffset();
        GUI.color = Color.white;
        
        int includedCount = includedAnimations.Values.Count(b => b);
        GUI.Label(new Rect(20, 225, 280, 20), $"{includedCount} / {animationFiles.Count} animations selected for batch.");

        GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
        if (GUI.Button(new Rect(20, 255, 280, 40), $"Start Batch Process ({includedCount} items)"))
        {
            currentState = EditorState.BatchProcessing;
            StartCoroutine(RunAutomatedBatch());
        }
        GUI.backgroundColor = Color.white;
    }

    // ===================================================================
    // =========== STAGE 2: AUTOMATED BATCH RUNNER =======================
    // ===================================================================

    private IEnumerator RunAutomatedBatch()
    {
        Debug.Log("====== SWITCHING TO AUTOMATED BATCH MODE ======");
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
            
            Debug.Log($"--- Starting batch recording for: {animFileName} ---");
            
            Vector3 finalOffset = Vector3.zero;
            float finalRotation = 0f;
            string description = "No description found.";
            string offsetFilePath = Path.Combine(surfaceSampler.baseOutputDirectory, environmentFolderName, animFileName, sceneFolderName, "animation_offsets.json");
            
            try
            {
                var animJson = JSON.Parse(File.ReadAllText(filePath));
                if (animJson["description"] != null) description = animJson["description"];

                if (File.Exists(offsetFilePath))
                {
                    var offsetJson = JSON.Parse(File.ReadAllText(offsetFilePath));
                    if (offsetJson[animFileName] != null)
                    {
                        finalOffset.x = offsetJson[animFileName]["x"];
                        // *** MODIFIED ***: Load Y offset.
                        finalOffset.y = offsetJson[animFileName]["y"] != null ? (float)offsetJson[animFileName]["y"] : 0f;
                        finalOffset.z = offsetJson[animFileName]["z"];
                        finalRotation = offsetJson[animFileName]["y_rotation"];
                        Debug.Log($"Loaded tuned offset for '{animFileName}': X={finalOffset.x}, Y={finalOffset.y}, Z={finalOffset.z}, RotY={finalRotation}");
                    }
                }
                else { Debug.LogWarning($"No tuned offset file found for '{animFileName}'. Using default offset/rotation."); }
            }
            catch (Exception e) { Debug.LogError($"Error loading data for {animFileName}: {e.Message}"); }
            
            JSONObject animSummaryReport = new JSONObject();
            animSummaryReport["animationName"] = animFileName;
            // *** MODIFIED ***: Report full Vector3 offset.
            animSummaryReport["tunedOffset"] = new JSONObject { ["x"] = finalOffset.x, ["y"] = finalOffset.y, ["z"] = finalOffset.z };
            animSummaryReport["tunedRotationY"] = finalRotation;
            processedAnimationsReport.Add(animSummaryReport);

            JSONObject individualReport = new JSONObject();
            individualReport["animationName"] = animFileName;
            individualReport["description"] = description;
            individualReport["tunedOffset"] = new JSONObject { ["x"] = finalOffset.x, ["y"] = finalOffset.y, ["z"] = finalOffset.z };
            individualReport["tunedRotationY"] = finalRotation;

            string samplerSubfolderPath = Path.Combine(environmentFolderName, animFileName, sceneFolderName);
            string finalOutputDirectory = Path.Combine(surfaceSampler.baseOutputDirectory, samplerSubfolderPath);
            SaveIndividualReport(individualReport, finalOutputDirectory);

            string videoFilePath = Path.Combine(finalOutputDirectory, animFileName + ".mp4");
            sceneRecorder.BeginRecording(videoFilePath);
            smplPlayer.LoadAndPlayAnimation(filePath, finalOffset, finalRotation);
            
            yield return null;
            yield return StartCoroutine(surfaceSampler.StartSampling(samplerSubfolderPath));
            sceneRecorder.EndRecording();
            Debug.Log($"--- Finished batch recording for: {animFileName} ---");
        }
        
        JSONArray excludedAnimationsReport = new JSONArray();
        foreach (string animFileName in includedAnimations.Keys)
        {
            if (!includedAnimations[animFileName]) excludedAnimationsReport.Add(animFileName);
        }
        summaryReport["excludedAnimations"] = excludedAnimationsReport;
        SaveSummaryReport(summaryReport);

        Debug.Log("====== AUTOMATED BATCH PROCESS COMPLETE! ======");
        if (quitOnFinish)
        {
            Debug.Log("Quitting application.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
    
    // ===================================================================
    // =========== REPORTING =============================================
    // ===================================================================
    
    private JSONObject CreateReportHeader()
    {
        JSONObject report = new JSONObject();
        report["reportGenerated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        report["environmentName"] = environmentFolderName;
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
        report["surfaceSamplerSettings"] = samplerSettings;
        
        JSONObject recorderSettings = new JSONObject();
        report["sceneRecorderSettings"] = recorderSettings;

        if (mainCamera != null)
        {
            JSONObject cameraSettings = new JSONObject();
            Transform camTransform = mainCamera.transform;
            cameraSettings["name"] = mainCamera.name;
            cameraSettings["position"] = new JSONObject { ["x"] = camTransform.position.x, ["y"] = camTransform.position.y, ["z"] = camTransform.position.z };
            cameraSettings["rotation"] = new JSONObject { ["x"] = camTransform.eulerAngles.x, ["y"] = camTransform.eulerAngles.y, ["z"] = camTransform.eulerAngles.z };
            cameraSettings["fieldOfView"] = mainCamera.fieldOfView;
            cameraSettings["nearClipPlane"] = mainCamera.nearClipPlane;
            cameraSettings["farClipPlane"] = mainCamera.farClipPlane;
            cameraSettings["projectionType"] = mainCamera.orthographic ? "Orthographic" : "Perspective";
            report["cameraSettings"] = cameraSettings;
        }

#if UNITY_EDITOR
        JSONArray sceneObjects = new JSONArray();
        HashSet<GameObject> reportedRoots = new HashSet<GameObject>();
        foreach (GameObject go in FindObjectsOfType<GameObject>())
        {
            Transform root = go.transform.root;
            if (reportedRoots.Contains(root.gameObject)) continue;
            reportedRoots.Add(root.gameObject);
            if (root == smplPlayer.transform.root) continue;
            if (root.GetComponentInChildren<Renderer>() == null) continue;
            
            JSONObject objectInfo = new JSONObject();
            objectInfo["instanceName"] = root.name;
            string prefabPath = "Not a prefab";
            if (PrefabUtility.IsPartOfAnyPrefab(root.gameObject))
            {
                prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject);
            }
            objectInfo["assetPath"] = string.IsNullOrEmpty(prefabPath) ? "Not Found" : prefabPath;
            Transform t = root.transform;
            objectInfo["position"] = new JSONObject { ["x"] = t.position.x, ["y"] = t.position.y, ["z"] = t.position.z };
            objectInfo["rotation"] = new JSONObject { ["x"] = t.eulerAngles.x, ["y"] = t.eulerAngles.y, ["z"] = t.eulerAngles.z };
            objectInfo["scale"] = new JSONObject { ["x"] = t.localScale.x, ["y"] = t.localScale.y, ["z"] = t.localScale.z };
            sceneObjects.Add(objectInfo);
        }
        report["sceneObjects"] = sceneObjects;
#endif
        return report;
    }

    private void SaveSummaryReport(JSONNode report)
    {
        try
        {
            string reportDirectory = Path.Combine("reports", environmentFolderName, sceneFolderName);
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
