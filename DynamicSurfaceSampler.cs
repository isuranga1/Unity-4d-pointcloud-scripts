using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class DynamicSurfaceSampler : MonoBehaviour
{
    [Header("Sampling Settings")]
    public float defaultPointsPerUnitArea = 100f;
    public string humanObjectKeyword = "human"; 
    public ObjectDensityOverride[] densityOverrides;
    public string roomObjectKeyword = "room";

    [System.Serializable]
    public struct ObjectDensityOverride
    {
        public string objectKeyword;
        public float pointsPerUnitArea;
        public bool exactMatch;
    }

    private struct SampledPoint
    {
        public Vector3 position;
        public Vector3 normal;
        public Color32 color;
        public string label;
    }

    [Header("Capture Settings")]
    public float captureDuration = 5f;
    public float captureFPS = 10f;
    public string baseOutputDirectory = "Scans";

    [Header("Output Settings")]
    [Tooltip("If true, saves a single point cloud of the static scene to 'scene_point_cloud/scene.pcd' before processing animations.")]
    public bool saveStaticPointCloudSeparately = false;

    [Header("Randomization")]
    public int fixedRandomSeed = 1337;
    
    private List<SampledPoint> staticSampledPoints;
    private Bounds roomBounds; // To cache the calculated bounds.

    void Start()
    {
        // The BatchProcessor now controls when sampling starts.
    }
    
    /// <summary>
    /// Calculates and returns the bounding box of the room. Caches the result for efficiency.
    /// </summary>
    /// <returns>The calculated Bounds of the room.</returns>
    public Bounds GetRoomBounds()
    {
        if (roomBounds.size == Vector3.zero)
        {
            roomBounds = CalculateRoomBounds(roomObjectKeyword);
        }
        return roomBounds;
    }

    void OnDrawGizmosSelected()
    {
        // This will draw the room bounds in the Scene view when this object is selected.
        // It's helpful for visualizing the placement search area.
        Gizmos.color = new Color(0, 1, 0, 0.5f); // Green and semi-transparent
        Bounds boundsToDraw = GetRoomBounds();
        if (boundsToDraw.size != Vector3.zero)
        {
            Gizmos.DrawWireCube(boundsToDraw.center, boundsToDraw.size);
        }
    }

    public IEnumerator StartSampling(string outputSubFolder, float animationDuration, SMPLAnimationPlayer playerToCheck, float animationFPS)
    {
        float durationToUse = (animationDuration > 0) ? animationDuration : this.captureDuration;
        int totalFramesToCapture = Mathf.FloorToInt(durationToUse * this.captureFPS);
        if (totalFramesToCapture <= 0) totalFramesToCapture = 1;

        Debug.Log($"Starting synchronized capture. Source FPS: {animationFPS}, Capture FPS: {this.captureFPS}. Capturing {totalFramesToCapture} frames.");
        
        Bounds currentRoomBounds = GetRoomBounds();

        // Only sample the static scene here if we are NOT saving it separately.
        if (!saveStaticPointCloudSeparately)
        {
            Debug.Log("Performing initial scan of the static environment to be included in each frame...");
            staticSampledPoints = new List<SampledPoint>();
            SampleStaticScene(staticSampledPoints, currentRoomBounds);
            Debug.Log($"Static scene scan complete. Captured {staticSampledPoints.Count} points.");
        }
        else
        {
            // If we are saving separately, ensure the static list is empty for this animation run.
            if(staticSampledPoints == null)
            {
                staticSampledPoints = new List<SampledPoint>();
            }
            staticSampledPoints.Clear();
            Debug.Log("Skipping per-animation static scan. Dynamic points will be saved alone.");
        }

        for (int captureFrame = 0; captureFrame < totalFramesToCapture; captureFrame++)
        {
            float timestamp = (float)captureFrame / this.captureFPS;
            int sourceAnimationFrame = Mathf.FloorToInt(timestamp * animationFPS);
            playerToCheck.SetPoseForFrame(sourceAnimationFrame);
            yield return new WaitForEndOfFrame();

            List<SampledPoint> dynamicPointsThisFrame = new List<SampledPoint>();
            SampleDynamicObjects(dynamicPointsThisFrame, currentRoomBounds);
            
            // Always start with a copy of the (potentially empty) static points list
            List<SampledPoint> combinedFramePoints = new List<SampledPoint>(staticSampledPoints);
            combinedFramePoints.AddRange(dynamicPointsThisFrame);
            SaveFrameToPCD(combinedFramePoints, captureFrame, outputSubFolder);
        }
        
        Debug.Log($"Synchronized capture finished. Captured {totalFramesToCapture} frames.");
    }

    /// <summary>
    /// Captures the static environment and saves it to a single PCD file.
    /// This should be called by the BatchProcessor before starting the animation loop.
    /// </summary>
    /// <param name="scenePath">The relative path for the scene, e.g., "Bathroom/Bathroom_001/scene1".</param>
    public void CaptureAndSaveStaticPointCloud(string scenePath)
    {
        if (!saveStaticPointCloudSeparately)
        {
            Debug.LogWarning("'Save Static Point Cloud Separately' is disabled in DynamicSurfaceSampler, but a save was requested. Skipping.");
            return;
        }

        Debug.Log("Performing a separate scan of the static environment...");
        List<SampledPoint> points = new List<SampledPoint>();
        Bounds currentRoomBounds = GetRoomBounds();
        SampleStaticScene(points, currentRoomBounds);

        string outputDirectory = Path.Combine(baseOutputDirectory, scenePath, "scene_point_cloud");
        string filename = "scene.pcd";
        string fullPath = Path.Combine(outputDirectory, filename);

        Debug.Log($"Saving static scene point cloud with {points.Count} points to {fullPath}");
        SavePCD(points, fullPath);
        Debug.Log("Static scene scan and save complete.");
    }

    void SampleStaticScene(List<SampledPoint> pointsList, Bounds bounds)
    {
        foreach (MeshFilter mf in FindObjectsOfType<MeshFilter>())
        {
            if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
            if (!string.IsNullOrEmpty(humanObjectKeyword) && mf.gameObject.name.ToLower().Contains(humanObjectKeyword.ToLower())) continue;
            SampleMeshPointsUniform(mf.sharedMesh, mf.transform, mf.gameObject.name, bounds, pointsList);
        }

        foreach (SkinnedMeshRenderer smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!smr.gameObject.activeInHierarchy) continue;
            if (!string.IsNullOrEmpty(humanObjectKeyword) && smr.gameObject.name.ToLower().Contains(humanObjectKeyword.ToLower())) continue;
            Mesh bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);
            SampleMeshPointsUniform(bakedMesh, smr.transform, smr.gameObject.name, bounds, pointsList);
        }
    }

    void SampleDynamicObjects(List<SampledPoint> pointsList, Bounds bounds)
    {
        foreach (SkinnedMeshRenderer smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!smr.gameObject.activeInHierarchy) continue;

            if (!string.IsNullOrEmpty(humanObjectKeyword) && smr.gameObject.name.ToLower().Contains(humanObjectKeyword.ToLower()))
            {
                Mesh bakedMesh = new Mesh();
                smr.BakeMesh(bakedMesh);
                SampleMeshPointsUniform(bakedMesh, smr.transform, smr.gameObject.name, bounds, pointsList);
            }
        }
        
        foreach (MeshFilter mf in FindObjectsOfType<MeshFilter>())
        {
            if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
            
            if (!string.IsNullOrEmpty(humanObjectKeyword) && mf.gameObject.name.ToLower().Contains(humanObjectKeyword.ToLower()))
            {
                SampleMeshPointsUniform(mf.sharedMesh, mf.transform, mf.gameObject.name, bounds, pointsList);
            }
        }
    }
    void SampleMeshPointsUniform(Mesh mesh, Transform transform, string objectName, Bounds roomBounds, List<SampledPoint> sampledPoints)
    {
        if (mesh.vertexCount == 0) return;

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector2[] uvs = mesh.uv;
        Vector3[] normals = mesh.normals; 

        bool hasNormals = normals != null && normals.Length > 0;

        Renderer renderer = transform.GetComponent<Renderer>();
        Texture2D texture = null;
        if (renderer != null && renderer.sharedMaterial != null)
        {
            texture = renderer.sharedMaterial.mainTexture as Texture2D;
            if (texture != null && !texture.isReadable)
            {
                texture = null;
            }
        }
        
        int objectID = objectName.GetHashCode();
        float objectDensity = GetDensityForObject(objectName);
        List<float> triangleAreas = new List<float>();
        float totalSurfaceArea = 0f;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = transform.TransformPoint(vertices[triangles[i]]);
            Vector3 v1 = transform.TransformPoint(vertices[triangles[i + 1]]);
            Vector3 v2 = transform.TransformPoint(vertices[triangles[i + 2]]);
            float area = Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;
            triangleAreas.Add(area);
            totalSurfaceArea += area;
        }

        int totalPointsNeeded = Mathf.CeilToInt(totalSurfaceArea * objectDensity);
        if (totalPointsNeeded == 0) return;

        List<float> cumulativeAreas = new List<float>();
        float runningSum = 0f;
        foreach (float area in triangleAreas)
        {
            runningSum += area;
            cumulativeAreas.Add(runningSum);
        }

        Random.InitState(fixedRandomSeed + objectID);

        for (int pointIndex = 0; pointIndex < totalPointsNeeded; pointIndex++)
        {
            float randomValue = Random.value * totalSurfaceArea;
            int selectedTriangle = 0;
            for (int i = 0; i < cumulativeAreas.Count; i++)
            {
                if (randomValue <= cumulativeAreas[i])
                {
                    selectedTriangle = i;
                    break;
                }
            }

            int triIndex = selectedTriangle * 3;
            int vertIndex0 = triangles[triIndex];
            int vertIndex1 = triangles[triIndex + 1];
            int vertIndex2 = triangles[triIndex + 2];

            Vector3 v0 = transform.TransformPoint(vertices[vertIndex0]);
            Vector3 v1 = transform.TransformPoint(vertices[vertIndex1]);
            Vector3 v2 = transform.TransformPoint(vertices[vertIndex2]);

            float u = Random.value;
            float v = Random.value;
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            Vector3 sample = v0 + u * (v1 - v0) + v * (v2 - v0);

            if (roomBounds.Contains(sample))
            {
                Color32 pointColor = Color.white;
                if (texture != null && uvs.Length > 0)
                {
                    Vector2 uv0 = uvs[vertIndex0];
                    Vector2 uv1 = uvs[vertIndex1];
                    Vector2 uv2 = uvs[vertIndex2];
                    Vector2 interpolatedUV = uv0 + u * (uv1 - uv0) + v * (uv2 - uv0);
                    pointColor = texture.GetPixelBilinear(interpolatedUV.x, interpolatedUV.y);
                }

                Vector3 pointNormal = Vector3.up;
                if (hasNormals)
                {
                    Vector3 n_local_0 = normals[vertIndex0];
                    Vector3 n_local_1 = normals[vertIndex1];
                    Vector3 n_local_2 = normals[vertIndex2];
                    Vector3 interpolatedNormalLocal = (n_local_0 + u * (n_local_1 - n_local_0) + v * (n_local_2 - n_local_0)).normalized;
                    pointNormal = transform.TransformDirection(interpolatedNormalLocal);
                }

                sampledPoints.Add(new SampledPoint
                {
                    position = sample,
                    normal = pointNormal,
                    color = pointColor,
                    label = objectName
                });
            }
        }
    }

    float GetDensityForObject(string objectName)
    {
        foreach (ObjectDensityOverride densityOverride in densityOverrides)
        {
            bool isMatch = densityOverride.exactMatch
                ? string.Equals(objectName, densityOverride.objectKeyword, System.StringComparison.OrdinalIgnoreCase)
                : objectName.ToLower().Contains(densityOverride.objectKeyword.ToLower());

            if (isMatch)
            {
                return densityOverride.pointsPerUnitArea;
            }
        }
        return defaultPointsPerUnitArea;
    }

    Bounds CalculateRoomBounds(string keyword)
    {
        Bounds bounds = new Bounds();
        bool initialized = false;

        System.Action<Vector3> encapsulate = (worldPoint) =>
        {
            if (!initialized)
            {
                bounds = new Bounds(worldPoint, Vector3.zero);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(worldPoint);
            }
        };
        
        Debug.Log($"--- Checking for room bounds using keyword: '{keyword}' ---");

        foreach (var mf in FindObjectsOfType<MeshFilter>())
        {
            bool hasMesh = mf.sharedMesh != null;
            bool nameMatches = mf.name.ToLower().Contains(keyword.ToLower());
            if (nameMatches && hasMesh)
            {
                foreach (var v in mf.sharedMesh.vertices) encapsulate(mf.transform.TransformPoint(v));
            }
        }
        
        foreach (var smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            bool hasMesh = smr.sharedMesh != null;
            bool nameMatches = smr.name.ToLower().Contains(keyword.ToLower());
            if (nameMatches && hasMesh)
            {
                foreach (var v in smr.sharedMesh.vertices) encapsulate(smr.transform.TransformPoint(v));
            }
        }

        if (!initialized)
        {
            Debug.LogWarning($"Could not find any 'room' objects with meshes to calculate bounds. Using a large default bound.");
            bounds = new Bounds(Vector3.zero, new Vector3(1000, 1000, 1000));
        }
        else
        {
            Debug.Log("Successfully calculated room bounds.");
        }

        return bounds;
    }
    
    void SaveFrameToPCD(List<SampledPoint> points, int frameIndex, string outputSubFolder)
    {
        string finalDirectory = Path.Combine(baseOutputDirectory, outputSubFolder);
        string filename = $"frame_{frameIndex:D3}.pcd";
        string path = Path.Combine(finalDirectory, filename);
        SavePCD(points, path);
    }
    
    /// <summary>
    /// Generic method to save a list of points to a PCD file at a specified full path.
    /// </summary>
    /// <param name="points">The list of points to save.</param>
    /// <param name="fullPath">The complete file path, including filename and extension.</param>
    void SavePCD(List<SampledPoint> points, string fullPath)
    {
        string directory = Path.GetDirectoryName(fullPath);
        if (directory != null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        
        using (StreamWriter writer = new StreamWriter(fullPath, false, Encoding.ASCII))
        {
            writer.WriteLine("# .PCD v0.7 - Point Cloud Data file format");
            writer.WriteLine("VERSION 0.7");
            writer.WriteLine("FIELDS x y z normal_x normal_y normal_z rgb label");
            writer.WriteLine("SIZE 4 4 4 4 4 4 4 4");
            writer.WriteLine("TYPE F F F F F F U U");
            writer.WriteLine("COUNT 1 1 1 1 1 1 1 1");
            writer.WriteLine($"WIDTH {points.Count}");
            writer.WriteLine("HEIGHT 1");
            writer.WriteLine("VIEWPOINT 0 0 0 1 0 0 0");
            writer.WriteLine($"POINTS {points.Count}");
            writer.WriteLine("DATA ascii");

            for (int i = 0; i < points.Count; i++)
            {
                SampledPoint sp = points[i];
                Vector3 p = sp.position;
                Vector3 n = sp.normal;
                Color32 c = sp.color;

                uint rgb = ((uint)c.r << 16) | ((uint)c.g << 8) | ((uint)c.b);
                string safeLabel = sp.label.Replace(' ', '_');
                
                writer.WriteLine($"{p.x} {p.y} {p.z} {n.x} {n.y} {n.z} {rgb} {safeLabel}");
            }
        }
    }
}
