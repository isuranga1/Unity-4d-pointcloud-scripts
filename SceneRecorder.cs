using UnityEngine;
using System.IO;
// Add the required namespaces for the Unity Recorder API.
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;

/// <summary>
/// Controls the Unity Recorder to save video files directly.
/// NOTE: This script relies on the Unity Recorder package and will only work inside the Unity Editor.
/// </summary>
public class SceneRecorder : MonoBehaviour
{
    private RecorderController recorderController;
    
    /// <summary>
    /// Starts recording a video to the specified output file path.
    /// </summary>
    /// <param name="outputFilePath">The full path and filename for the output video (e.g., "C:/Path/To/Output/recording.mp4").</param>
    public void BeginRecording(string outputFilePath)
    {
        // Check if a controller already exists and is recording.
        if (recorderController != null && recorderController.IsRecording())
        {
            Debug.LogWarning("Recorder is already running. Stop it before starting a new recording.");
            return;
        }

        // Ensure the output directory exists.
        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // --- Configure the Recorder ---
        // *** FIX ***: Create a new settings and controller instance for each recording.
        // This is a more robust method that avoids state issues and the need for a 'Clear()' method.
        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        recorderController = new RecorderController(controllerSettings);
        
        var settings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        settings.name = "My Video Recorder";
        settings.Enabled = true;

        // Set the output format to MP4.
        settings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        settings.EncodingQuality = MovieRecorderSettings.VideoEncodingQuality.High;

        // Define what to record (the Game View).
        settings.ImageInputSettings = new GameViewInputSettings
        {
            OutputWidth = 1920, // Set your desired resolution
            OutputHeight = 1080
        };
        
        // Define where to save the file. The Recorder adds its own extension.
        settings.OutputFile = Path.ChangeExtension(outputFilePath, null); 

        // Add the configured settings to the controller.
        controllerSettings.AddRecorderSettings(settings);
        
        recorderController.PrepareRecording();
        recorderController.StartRecording();
        
        Debug.Log($"[SceneRecorder] Started. Saving video to: {outputFilePath}");
    }

    /// <summary>
    /// Stops the recording process.
    /// </summary>
    public void EndRecording()
    {
        if (recorderController == null || !recorderController.IsRecording()) return;

        recorderController.StopRecording();
        Debug.Log($"[SceneRecorder] Stopped.");
    }

    void OnDisable()
    {
        // Clean up the controller when the component is disabled or the application quits.
        if (recorderController != null)
        {
            // A final check to ensure we stop recording if the object is disabled mid-session.
            if(recorderController.IsRecording())
            {
                recorderController.StopRecording();
            }
        }
    }
}
