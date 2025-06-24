using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;

public class SMPLAnimationPlayer : MonoBehaviour
{
    [Header("SMPL Character")]
    public SkinnedMeshRenderer skinnedMeshRenderer;

    [Header("Animation")]
    public TextAsset jsonAnimationFile;
    public bool playOnStart = true;
    public bool loop = true;

    [Header("Playback")]
    [Range(0.1f, 3f)]
    public float playbackSpeed = 1f;

    // Animation data
    private AnimationData animData;
    private float currentTime = 0f;
    private bool isPlaying = false;
    private Transform[] bones;

    // *** NEW ***: Variables to store the initial positions
    private Vector3 modelInitialPosition;
    private Vector3 animationStartOffset;

    // SMPL bone mapping (24 joints)
    private static readonly Dictionary<string, int> BoneMapping = new Dictionary<string, int>
    {
        {"Pelvis", 0}, {"L_Hip", 1}, {"R_Hip", 2}, {"Spine1", 3},
        {"L_Knee", 4}, {"R_Knee", 5}, {"Spine2", 6}, {"L_Ankle", 7},
        {"R_Ankle", 8}, {"Spine3", 9}, {"L_Foot", 10}, {"R_Foot", 11},
        {"Neck", 12}, {"L_Collar", 13}, {"R_Collar", 14}, {"Head", 15},
        {"L_Shoulder", 16}, {"R_Shoulder", 17}, {"L_Elbow", 18}, {"R_Elbow", 19},
        {"L_Wrist", 20}, {"R_Wrist", 21}, {"L_Hand", 22}, {"R_Hand", 23}
    };

    private class AnimationData
    {
        public int fps;
        public int frameCount;
        public Vector3[] translations;
        public Quaternion[,] poses; // [frame, joint]
    }

    void Start()
    {
        if (skinnedMeshRenderer == null)
            skinnedMeshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();

        if (skinnedMeshRenderer == null)
        {
            Debug.LogError("SkinnedMeshRenderer not found!");
            return;
        }

        bones = skinnedMeshRenderer.bones;
        
        // *** NEW ***: Store the initial position of the model in the scene.
        modelInitialPosition = transform.localPosition;

        if (jsonAnimationFile != null)
        {
            LoadAnimation();
            if (playOnStart) Play();
        }
    }

    void Update()
    {
        if (isPlaying && animData != null)
        {
            currentTime += Time.deltaTime * playbackSpeed;

            float duration = animData.frameCount / (float)animData.fps;
            if (currentTime >= duration)
            {
                if (loop)
                    currentTime = 0f;
                else
                    Stop();
            }

            ApplyFrame();
        }

        // Controls
        if (Input.GetKeyDown(KeyCode.Space)) TogglePlay();
        if (Input.GetKeyDown(KeyCode.R)) Restart();
    }

    void LoadAnimation()
    {
        try
        {
            var json = JSON.Parse(jsonAnimationFile.text);
            animData = new AnimationData();

            animData.fps = json["fps"];
            var transNode = json["trans"];
            var posesNode = json["poses"];
            animData.frameCount = transNode.Count;

            // Load translations
            animData.translations = new Vector3[animData.frameCount];
            for (int frame = 0; frame < animData.frameCount; frame++)
            {
                // Your JSON comment says (x,z,y) but typical SMPL data is (x,y,z).
                // The original code `ConvertMayaToUnity` suggests the source is Maya (x, y, z).
                // I will assume the original code's coordinate conversion is what you need.
                var mayaPos = new Vector3(transNode[frame][0], transNode[frame][1], transNode[frame][2]);
                animData.translations[frame] = ConvertMayaToUnity(mayaPos);
            }

            // *** NEW ***: Store the animation's own starting translation from frame 0.
            if (animData.frameCount > 0)
            {
                animationStartOffset = animData.translations[0];
            }
            else
            {
                animationStartOffset = Vector3.zero;
            }

            // Load poses
            animData.poses = new Quaternion[animData.frameCount, 24];
            for (int frame = 0; frame < animData.frameCount; frame++)
            {
                for (int joint = 0; joint < 24; joint++)
                {
                    var mayaQuat = new Quaternion(
                        posesNode[frame][joint][1], // x
                        posesNode[frame][joint][2], // y  
                        posesNode[frame][joint][3], // z
                        posesNode[frame][joint][0]  // w
                    );
                    animData.poses[frame, joint] = ConvertQuatMayaToUnity(mayaQuat);
                }
            }

            Debug.Log($"Loaded animation: {animData.frameCount} frames at {animData.fps} fps");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load animation: {e.Message}");
        }
    }

    void ApplyFrame()
    {
        if (animData == null) return;

        // Get current frame
        float frameFloat = currentTime * animData.fps;
        int frame = Mathf.FloorToInt(frameFloat);
        frame = Mathf.Clamp(frame, 0, animData.frameCount - 1);

        // *** MODIFIED ***: Apply root translation relative to the start position
        // 1. Get the current translation from the animation data.
        Vector3 currentAnimTranslation = animData.translations[frame];

        // 2. Calculate the displacement from the animation's own starting frame.
        Vector3 animationDisplacement = currentAnimTranslation - animationStartOffset;

        // 3. Apply the initial model position (X, Z) and add the animation's displacement.
        //    We use the animation's absolute Y value directly to preserve jumps/crouches.
        float newX = modelInitialPosition.x + animationDisplacement.x;
        float newY = currentAnimTranslation.y; // Use absolute Y from animation data
        float newZ = modelInitialPosition.z + animationDisplacement.z;

        transform.localPosition = new Vector3(newX, newY, newZ);

        // Apply bone rotations (This part remains unchanged)
        for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            string boneName = bones[boneIndex].name;

            if (BoneMapping.TryGetValue(boneName, out int jointIndex))
            {
                // Reset bone rotation
                bones[boneIndex].localEulerAngles = Vector3.zero;

                // Apply pelvis correction for Maya to Unity conversion
                if (boneName == "Pelvis")
                {
                    bones[boneIndex].Rotate(-90, 0, 0, Space.Self);
                }

                // Apply pose
                bones[boneIndex].localRotation = bones[boneIndex].localRotation * animData.poses[frame, jointIndex];
            }
        }
    }

    // Convert Maya coordinates to Unity
    Vector3 ConvertMayaToUnity(Vector3 mayaPos)
    {
        // Maya (X, Y, Z) -> Unity (-X, Y, Z) for left to right-handed conversion
        // The original script had a different conversion. This is more standard.
        // Let's stick to your original to not break anything: new Vector3(-mayaPos.x, mayaPos.z, -mayaPos.y);
        return new Vector3(-mayaPos.x, mayaPos.z, -mayaPos.y);
    }

    Quaternion ConvertQuatMayaToUnity(Quaternion mayaQuat)
    {
        return new Quaternion(-mayaQuat.x, mayaQuat.y, mayaQuat.z, -mayaQuat.w);
    }

    public void Play()
    {
        if (animData == null) return;
        isPlaying = true;
    }

    public void Stop()
    {
        isPlaying = false;
        currentTime = 0f;
        if (animData != null) ApplyFrame();
    }

    public void TogglePlay()
    {
        if (isPlaying) isPlaying = false;
        else Play();
    }

    public void Restart()
    {
        currentTime = 0f;
        if (animData != null) ApplyFrame();
        Play();
    }
}