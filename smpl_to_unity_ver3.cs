using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;
using System.IO;

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
    private bool isPlaying = false;
    private Transform[] bones;
    
    private Vector3 modelBaseInitialPosition;
    private Vector3 currentAdditionalOffset;
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
    }

    void Update()
    {
        if (isPlaying && animData != null)
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

    public void LoadAndPlayAnimation(string jsonFilePath, Vector3 additionalOffset)
    {
        this.currentAdditionalOffset = additionalOffset;
        string jsonText = File.ReadAllText(jsonFilePath);
        LoadAnimation(jsonText);
        Play();
    }

    // *** NEW PUBLIC METHOD ***: Allows the UI to update the offset in real-time.
    public void UpdateLiveOffset(Vector3 newOffset)
    {
        this.currentAdditionalOffset = newOffset;
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

            if (animData.frameCount > 0)
            {
                animationStartOffset = animData.translations[0];
            }
            else
            {
                animationStartOffset = Vector3.zero;
            }
            
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
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load animation: {e.Message}");
        }
    }

    void ApplyFrame()
    {
        if (animData == null) return;

        float frameFloat = currentTime * animData.fps;
        int frame = Mathf.FloorToInt(frameFloat);
        frame = Mathf.Clamp(frame, 0, animData.frameCount - 1);
        
        Vector3 currentAnimTranslation = animData.translations[frame];
        Vector3 animationDisplacement = currentAnimTranslation - animationStartOffset;

        Vector3 finalStartPosition = modelBaseInitialPosition + currentAdditionalOffset;

        float newX = finalStartPosition.x + animationDisplacement.x;
        float newY = currentAnimTranslation.y;
        float newZ = finalStartPosition.z + animationDisplacement.z;

        transform.localPosition = new Vector3(newX, newY, newZ);

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
                bones[boneIndex].localRotation = bones[boneIndex].localRotation * animData.poses[frame, jointIndex];
            }
        }
    }
    
    Vector3 ConvertMayaToUnity(Vector3 mayaPos)
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
        isPlaying = true;
    }

    public void Stop()
    {
        isPlaying = false;
        currentTime = 0f;
        if (animData != null) ApplyFrame();
    }
}
