using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;
using System.IO;
using System;

public class SMPLAnimationPlayer : MonoBehaviour
{
    [Header("SMPL Character")]
    public SkinnedMeshRenderer skinnedMeshRenderer;

    [Header("Animation")]
    public bool loop = true;
    [Tooltip("Value to subtract from the Y-coordinate of the animation data.")]
    public float yOffset = 0.95f;

    [Header("Playback")]
    [Range(0.1f, 3f)]
    public float playbackSpeed = 1f;

    // Animation data
    private AnimationData animData;
    private float currentTime = 0f;

    public bool IsPlaying { get; private set; } = false;
    public bool IsInBatchMode { get; set; } = false;
    private Transform[] bones;

    private Vector3 modelBaseInitialPosition;
    private Vector3 modelBaseInitialRotation;
    private Vector3 currentPositionalOffset;
    private float currentAdditionalRotationY;
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
        public Quaternion[,] poses;
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
        modelBaseInitialPosition = transform.localPosition;
        modelBaseInitialRotation = transform.localEulerAngles;
    }

    void Update()
    {
        if (!IsInBatchMode && IsPlaying && animData != null)
        {
            currentTime += Time.deltaTime * playbackSpeed;
            float duration = animData.frameCount / (float)animData.fps;
            if (currentTime >= duration)
            {
                if (loop) currentTime = 0f;
                else Stop();
            }
            ApplyFrame();
        }
    }

    public void LoadAndPlayAnimation(string jsonFilePath, Vector3 positionalOffset, float additionalRotationY)
    {
        this.currentPositionalOffset = positionalOffset;
        UpdateLiveRotation(additionalRotationY);
        string jsonText = File.ReadAllText(jsonFilePath);
        LoadAnimation(jsonText);
        Play();
    }

    public void UpdateLiveOffset(Vector3 newPositionalOffset)
    {
        this.currentPositionalOffset = newPositionalOffset;
    }

    public void UpdateLiveRotation(float newRotationY)
    {
        this.currentAdditionalRotationY = newRotationY;
        // transform.localEulerAngles = modelBaseInitialRotation + new Vector3(0, this.currentAdditionalRotationY, 0);
    }

    void LoadAnimation(string jsonText)
    {
        try
        {
            var json = JSON.Parse(jsonText);
            animData = new AnimationData();
            animData.fps = json["fps"];
            var transNode = json["trans"];
            var posesNode = json["poses"];
            animData.frameCount = transNode.Count;

            animData.translations = new Vector3[animData.frameCount];
            for (int frame = 0; frame < animData.frameCount; frame++)
            {
                float jsonX = transNode[frame][0];
                float jsonZ = transNode[frame][1];
                float jsonY = transNode[frame][2];
                jsonY -= yOffset;
                var mayaPos = new Vector3(jsonX, jsonZ, jsonY);
                animData.translations[frame] = ConvertMayaToUnity(mayaPos);
            }

            animationStartOffset = animData.frameCount > 0 ? animData.translations[0] : Vector3.zero;

            animData.poses = new Quaternion[animData.frameCount, 24];
            for (int frame = 0; frame < animData.frameCount; frame++)
            {
                for (int joint = 0; joint < 24; joint++)
                {
                    var mayaQuat = new Quaternion(
                        posesNode[frame][joint][1],
                        posesNode[frame][joint][2],
                        posesNode[frame][joint][3],
                        posesNode[frame][joint][0]
                    );
                    animData.poses[frame, joint] = ConvertQuatMayaToUnity(mayaQuat);
                }
            }
            Debug.Log($"Loaded animation: {animData.frameCount} frames at {animData.fps} fps");
        }
        catch (Exception e) { Debug.LogError($"Failed to load animation: {e.Message}"); }
    }

    // *** NEW: Public method to get the initial position ***
    public Vector3 GetModelBaseInitialPosition()
    {
        return modelBaseInitialPosition;
    }

    // *** MODIFIED: Changed protection level to public ***
    public Vector3 ConvertMayaToUnity(Vector3 mayaPos)
    {
        return new Vector3(-mayaPos.x, mayaPos.z, -mayaPos.y);
    }

    Quaternion ConvertQuatMayaToUnity(Quaternion mayaQuat)
    {
        return new Quaternion(-mayaQuat.x, mayaQuat.y, mayaQuat.z, -mayaQuat.w);
    }

    public void Play()
    {
        if (animData == null) return;
        currentTime = 0f;
        IsPlaying = true;
    }

    public void Stop()
    {
        IsPlaying = false;
        currentTime = 0f;
        if (animData != null) ApplyFrame();
    }
    
    public void SetPoseForFrame(int frame)
    {
        if (animData == null) return;

        int clampedFrame = Mathf.Clamp(frame, 0, animData.frameCount - 1);
        
        // --- START: NEW AND IMPROVED LOGIC ---

        // 1. Set the character's overall rotation first for this frame.
        transform.localEulerAngles = modelBaseInitialRotation + new Vector3(0, currentAdditionalRotationY, 0);

        // 2. Calculate the animation's root displacement in its own local space.
        Vector3 currentAnimTranslation = animData.translations[clampedFrame];
        Vector3 animationDisplacement = currentAnimTranslation - animationStartOffset;

        // 3. Calculate the final world position.
        // We start at the base position, add the user-defined offset,
        // and then add the animation's displacement, converting it from local to world space using the rotation we just set.
        Vector3 finalStartPosition = modelBaseInitialPosition + currentPositionalOffset;
        Vector3 worldSpaceDisplacement = transform.rotation * animationDisplacement;
        transform.localPosition = finalStartPosition + worldSpaceDisplacement;

        // --- END: NEW AND IMPROVED LOGIC ---


        // The bone animation logic remains the same.
        for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            string boneName = bones[boneIndex].name;
            if (BoneMapping.TryGetValue(boneName, out int jointIndex))
            {
                bones[boneIndex].localEulerAngles = Vector3.zero;
                if (boneName == "Pelvis")
                {
                    bones[boneIndex].Rotate(-90, 0, 0, Space.Self);
                }
                bones[boneIndex].localRotation *= animData.poses[clampedFrame, jointIndex];
            }
        }
    }

    void ApplyFrame()
    {
        if (animData == null) return;
        int frame = Mathf.FloorToInt(currentTime * animData.fps);
        SetPoseForFrame(frame);
    }
}
