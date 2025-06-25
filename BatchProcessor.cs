using UnityEngine;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SimpleJSON;

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
    public SceneRecorder sceneRecorder; // Needed for the batch process part

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
    private int currentAnimationIndex = -1;
    private Vector2 liveOffset; // Use Vector2 for UI (x, z)
    private bool isDirty = false;

    void Start()
    {
        // Basic validation
        if (smplPlayer == null || surfaceSampler == null || sceneRecorder == null)
        {
            Debug.LogError("BatchProcessor Error: SMPLPlayer, SurfaceSampler, or SceneRecorder is not assigned!");
            return;
        }

        // Setup for the tuning stage
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
                foreach (KeyValuePair<string, JSONNode> animOffset in json)
                {
                    tunedOffsets[animOffset.Key] = new Vector3(animOffset.Value["x"], 0, animOffset.Value["z"]);
                }
                Debug.Log($"Successfully loaded {tunedOffsets.Count} initial offsets from {initialOffsetsConfigName}.");
            }
            catch (Exception e) { Debug.LogError($"Error parsing {initialOffsetsConfigName}: {e.Message}"); }
        }
        else { Debug.LogWarning($"Initial offsets file not found at {configPath}. All animations will start with a (0,0) offset."); }
    }

    private void PopulateAnimationList()
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, animationsSubfolderPath);
        if (!Directory.Exists(fullPath)) return;
        animationFiles = Directory.GetFiles(fullPath)
            .Where(file => file.EndsWith(".json") || file.EndsWith(".txt"))
            .ToList();
    }

    private void LoadAnimationForTuning(int index)
    {
        if (animationFiles.Count == 0) return;
        currentAnimationIndex = Mathf.Clamp(index, 0, animationFiles.Count - 1);
        string filePath = animationFiles[currentAnimationIndex];
        string animFileName = Path.GetFileNameWithoutExtension(filePath);
        Vector3 offsetToLoad = tunedOffsets.ContainsKey(animFileName) ? tunedOffsets[animFileName] : Vector3.zero;
        liveOffset = new Vector2(offsetToLoad.x, offsetToLoad.z);
        smplPlayer.LoadAndPlayAnimation(filePath, offsetToLoad);
        isDirty = false;
    }

    private void SaveCurrentOffset()
    {
        if (currentAnimationIndex < 0) return;
        string animFileName = Path.GetFileNameWithoutExtension(animationFiles[currentAnimationIndex]);
        // *** MODIFIED ***: Include the new environment folder name in the path.
        string outputDirectory = Path.Combine(surfaceSampler.baseOutputDirectory, environmentFolderName, animFileName, sceneFolderName);
        string savePath = Path.Combine(outputDirectory, "animation_offsets.json");
        Directory.CreateDirectory(outputDirectory);
        JSONNode rootNode = new JSONObject();
        JSONObject offsetNode = new JSONObject { ["x"] = liveOffset.x, ["z"] = liveOffset.y };
        rootNode[animFileName] = offsetNode;
        File.WriteAllText(savePath, rootNode.ToString(4));
        Debug.Log($"Saved offset for '{animFileName}' to: {savePath}");
        isDirty = false;
    }

    void OnGUI()
    {
        if (currentState != EditorState.Tuning || animationFiles.Count == 0) return;

        GUI.Box(new Rect(10, 10, 300, 220), "Animation Tuner & Batcher");
        string animFileName = Path.GetFileNameWithoutExtension(animationFiles[currentAnimationIndex]);
        GUI.Label(new Rect(20, 40, 280, 20), $"Animation: {animFileName}");

        GUI.Label(new Rect(20, 70, 40, 20), $"X: {liveOffset.x:F2}");
        liveOffset.x = GUI.HorizontalSlider(new Rect(70, 75, 230, 20), liveOffset.x, -10f, 10f);
        GUI.Label(new Rect(20, 100, 40, 20), $"Z: {liveOffset.y:F2}");
        liveOffset.y = GUI.HorizontalSlider(new Rect(70, 105, 230, 20), liveOffset.y, -10f, 10f);

        if (GUI.changed)
        {
            Vector3 newOffset = new Vector3(liveOffset.x, 0, liveOffset.y);
            tunedOffsets[animFileName] = newOffset;
            smplPlayer.UpdateLiveOffset(newOffset);
            isDirty = true;
        }

        if (GUI.Button(new Rect(20, 135, 80, 20), "<< Prev")) LoadAnimationForTuning(currentAnimationIndex - 1);
        if (GUI.Button(new Rect(110, 135, 80, 20), "Next >>")) LoadAnimationForTuning(currentAnimationIndex + 1);
        GUI.color = isDirty ? Color.green : Color.white;
        if (GUI.Button(new Rect(200, 135, 100, 20), "Save Offset")) SaveCurrentOffset();
        GUI.color = Color.white;

        GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
        if (GUI.Button(new Rect(20, 170, 280, 40), "Start Automated Batch Process"))
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
            Debug.Log($"--- Starting batch recording for: {animFileName} ---");
            
            // --- Load Offset and Text Prompt ---
            Vector3 finalOffset = Vector3.zero;
            string description = "No description found.";
            // *** MODIFIED ***: Include the new environment folder name in the path.
            string offsetFilePath = Path.Combine(surfaceSampler.baseOutputDirectory, environmentFolderName, animFileName, sceneFolderName, "animation_offsets.json");
            
            try
            {
                // Load the text prompt from the main animation file
                var animJson = JSON.Parse(File.ReadAllText(filePath));
                if (animJson["description"] != null)
                {
                    description = animJson["description"];
                }

                // Load the tuned offset if it exists
                if (File.Exists(offsetFilePath))
                {
                    var offsetJson = JSON.Parse(File.ReadAllText(offsetFilePath));
                    if (offsetJson[animFileName] != null)
                    {
                        finalOffset.x = offsetJson[animFileName]["x"];
                        finalOffset.z = offsetJson[animFileName]["z"];
                        Debug.Log($"Loaded tuned offset for '{animFileName}': X={finalOffset.x}, Z={finalOffset.z}");
                    }
                }
                else { Debug.LogWarning($"No tuned offset file found for '{animFileName}'. Using default (0,0) offset."); }
            }
            catch (Exception e) { Debug.LogError($"Error loading data for {animFileName}: {e.Message}"); }
            
            // --- Add data to summary report ---
            JSONObject animSummaryReport = new JSONObject();
            animSummaryReport["animationName"] = animFileName;
            animSummaryReport["tunedOffset"] = new JSONObject { ["x"] = finalOffset.x, ["z"] = finalOffset.z };
            processedAnimationsReport.Add(animSummaryReport);

            // --- Create and Save Individual Report ---
            JSONObject individualReport = new JSONObject();
            individualReport["animationName"] = animFileName;
            individualReport["description"] = description;
            individualReport["tunedOffset"] = new JSONObject { ["x"] = finalOffset.x, ["z"] = finalOffset.z };

            // *** MODIFIED ***: Include the new environment folder name in the path passed to the sampler.
            string samplerSubfolderPath = Path.Combine(environmentFolderName, animFileName, sceneFolderName);
            string finalOutputDirectory = Path.Combine(surfaceSampler.baseOutputDirectory, samplerSubfolderPath);
            SaveIndividualReport(individualReport, finalOutputDirectory);

            // --- Run Recorders ---
            string videoFilePath = Path.Combine(finalOutputDirectory, animFileName + ".mp4");
            sceneRecorder.BeginRecording(videoFilePath);
            smplPlayer.LoadAndPlayAnimation(filePath, finalOffset);
            
            yield return null;

            yield return StartCoroutine(surfaceSampler.StartSampling(samplerSubfolderPath));
            
            sceneRecorder.EndRecording();
            Debug.Log($"--- Finished batch recording for: {animFileName} ---");
        }
        
        SaveSummaryReport(summaryReport);

        Debug.Log("====== AUTOMATED BATCH PROCESS COMPLETE! ======");
        if (quitOnFinish)
        {
            Debug.Log("Quitting application.");
            Application.Quit();
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
        // *** MODIFIED ***: Include the new environment folder name in the report.
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

        return report;
    }

    private void SaveSummaryReport(JSONNode report)
    {
        try
        {
            // *** MODIFIED ***: Include the new environment folder name in the path.
            string reportDirectory = Path.Combine("reports", environmentFolderName, sceneFolderName);
            Directory.CreateDirectory(reportDirectory);
            string reportPath = Path.Combine(reportDirectory, "scene_summary_report.json");
            File.WriteAllText(reportPath, report.ToString(4));
            Debug.Log($"Successfully saved summary report to {reportPath}");
        }
        catch (Exception e) { Debug.LogError($"Failed to save summary report: {e.Message}"); }
    }

    // *** NEW ***: Saves the individual report inside the specific animation's folder.
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
