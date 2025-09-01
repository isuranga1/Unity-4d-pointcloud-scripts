using UnityEngine;
using System.IO;
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
    /// <param name="outputFilePath">The full path for the output video (e.g., "C:/Path/To/Output/recording.mp4").</param>
    /// <param name="frameRate">The target frame rate for the video recording.</param>
    public void BeginRecording(string outputFilePath, float frameRate) // MODIFIED: Added frameRate parameter
    {
        if (recorderController != null && recorderController.IsRecording())
        {
            Debug.LogWarning("Recorder is already running. Stop it before starting a new recording.");
            return;
        }

        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        recorderController = new RecorderController(controllerSettings);
        
        var settings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        settings.name = "My Video Recorder";
        settings.Enabled = true;

        settings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        settings.EncodingQuality = MovieRecorderSettings.VideoEncodingQuality.High;

        // *** NEW: Set the recorder's frame rate to match the animation's FPS ***
        settings.FrameRatePlayback = FrameRatePlayback.Constant;
        settings.FrameRate = frameRate;
        // **********************************************************************

        settings.ImageInputSettings = new GameViewInputSettings
        {
            OutputWidth = 1920,
            OutputHeight = 1080
        };
        
        settings.OutputFile = Path.ChangeExtension(outputFilePath, null); 

        controllerSettings.AddRecorderSettings(settings);
        
        recorderController.PrepareRecording();
        recorderController.StartRecording();
        
        Debug.Log($"[SceneRecorder] Started. Saving video to: {outputFilePath} at {frameRate} FPS.");
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
        if (recorderController != null)
        {
            if(recorderController.IsRecording())
            {
                recorderController.StopRecording();
            }
        }
    }
}