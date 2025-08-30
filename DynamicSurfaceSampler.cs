using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class DynamicSurfaceSampler : MonoBehaviour
{
    [Header("Sampling Settings")]
    public float defaultPointsPerUnitArea = 100f;
    // *** NEW ***: Added a keyword to identify the dynamic object.
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

    [Header("Randomization")]
    public int fixedRandomSeed = 1337;
    
    // *** NEW ***: A list to hold the static scene points, sampled only once.
    private List<SampledPoint> staticSampledPoints;

    void Start()
    {
        // The BatchProcessor now controls when sampling starts.
    }

    // *** MODIFIED ***: The main coroutine is updated for the new efficient logic.
    public IEnumerator StartSampling(string outputSubFolder)
    {
        float interval = 1f / captureFPS;
        int totalFrames = Mathf.CeilToInt(captureDuration * captureFPS);

        Debug.Log($"Starting sampling for {captureDuration}s at {captureFPS} FPS. Saving to '{outputSubFolder}'.");

        // --- Step 1: Perform the one-time scan of the static environment ---
        Debug.Log("Performing initial scan of the static environment...");
        staticSampledPoints = new List<SampledPoint>();
        Bounds roomBounds = CalculateRoomBounds(roomObjectKeyword); // Calculate bounds once
        SampleStaticScene(staticSampledPoints, roomBounds);
        Debug.Log($"Static scene scan complete. Captured {staticSampledPoints.Count} points.");

        // --- Step 2: Loop through frames, sampling only the dynamic object and combining ---
        for (int frame = 0; frame < totalFrames; frame++)
        {
            // Create a list for the dynamic points for this specific frame.
            List<SampledPoint> dynamicPointsThisFrame = new List<SampledPoint>();
            SampleDynamicObjects(dynamicPointsThisFrame, roomBounds);

            // Combine the persistent static points with the new dynamic points for this frame.
            List<SampledPoint> combinedFramePoints = new List<SampledPoint>(staticSampledPoints);
            combinedFramePoints.AddRange(dynamicPointsThisFrame);

            // Save the combined point cloud.
            SaveFrameToPCD(combinedFramePoints, frame, outputSubFolder);

            yield return new WaitForSeconds(interval);
        }

        Debug.Log("Sampling for current animation finished.");
    }

    // *** NEW ***: This method samples everything EXCEPT the dynamic (human) object.
    void SampleStaticScene(List<SampledPoint> pointsList, Bounds roomBounds)
    {
        // Sample static meshes
        foreach (MeshFilter mf in FindObjectsOfType<MeshFilter>())
        {
            if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
            
            // If the object's name contains the human keyword, skip it.
            if (!string.IsNullOrEmpty(humanObjectKeyword) && mf.gameObject.name.ToLower().Contains(humanObjectKeyword.ToLower())) continue;
            
            SampleMeshPointsUniform(mf.sharedMesh, mf.transform, mf.gameObject.name, roomBounds, pointsList);
        }

        // It's unlikely for static objects to be SkinnedMeshRenderers, but we check just in case.
        foreach (SkinnedMeshRenderer smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!smr.gameObject.activeInHierarchy) continue;

            // If the object's name contains the human keyword, skip it.
            if (!string.IsNullOrEmpty(humanObjectKeyword) && smr.gameObject.name.ToLower().Contains(humanObjectKeyword.ToLower())) continue;

            Mesh bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);
            SampleMeshPointsUniform(bakedMesh, smr.transform, smr.gameObject.name, roomBounds, pointsList);
        }
    }

    // *** NEW ***: This method samples ONLY the dynamic (human) object.
// *** NEW ***: This method samples ONLY the dynamic (human) object.
    void SampleDynamicObjects(List<SampledPoint> pointsList, Bounds roomBounds)
    {
        // Dynamic objects are typically SkinnedMeshRenderers
        foreach (SkinnedMeshRenderer smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!smr.gameObject.activeInHierarchy) continue;

            // Only sample the object if its name contains the human keyword.
            if (!string.IsNullOrEmpty(humanObjectKeyword) && smr.gameObject.name.ToLower().Contains(humanObjectKeyword.ToLower()))
            {
                Mesh bakedMesh = new Mesh();
                smr.BakeMesh(bakedMesh); // Bake the mesh at its current animation pose
                SampleMeshPointsUniform(bakedMesh, smr.transform, smr.gameObject.name, roomBounds, pointsList);
            }
        }
        
        // Also check MeshFilters in case the dynamic object is not skinned.
        foreach (MeshFilter mf in FindObjectsOfType<MeshFilter>())
        {
            if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
            
            if (!string.IsNullOrEmpty(humanObjectKeyword) && mf.gameObject.name.ToLower().Contains(humanObjectKeyword.ToLower()))
            {
                // *** THIS IS THE CORRECTED LINE ***
                SampleMeshPointsUniform(mf.sharedMesh, mf.transform, mf.gameObject.name, roomBounds, pointsList);
            }
        }
    }
    // NOTE: The rest of your code (`SampleMeshPointsUniform`, `GetDensityForObject`, `CalculateRoomBounds`, `SaveFrameToPCD`)
    // can remain exactly the same. They are generic enough to work with this new structure. I'm including them here for completeness.

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
                Debug.LogWarning($"Texture on '{objectName}' is not marked as Read/Write. Colors may be incorrect. Please enable it in texture import settings.", texture);
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

        if (!Directory.Exists(finalDirectory))
            Directory.CreateDirectory(finalDirectory);

        string filename = $"frame_{frameIndex:D3}.pcd";
        string path = Path.Combine(finalDirectory, filename);

        using (StreamWriter writer = new StreamWriter(path, false, Encoding.ASCII))
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