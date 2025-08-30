using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System;
using Unity.Collections;
using Unity.Jobs;

/// <summary>
/// A MonoBehaviour that orchestrates the sampling of all meshes in a scene over time.
/// It prepares data on the main thread, dispatches parallel jobs to do the heavy lifting,
/// and then collects the results to save them to PCD files.
/// </summary>
public class DynamicSurfaceSampler : MonoBehaviour
{
    [Header("Sampling Settings")]
    public float defaultPointsPerUnitArea = 100f;
    public ObjectDensityOverride[] densityOverrides;
    public string roomObjectKeyword = "room";

    [System.Serializable]
    public struct ObjectDensityOverride
    {
        public string objectKeyword;
        public float pointsPerUnitArea;
        public bool exactMatch;
    }

    [Header("Capture Settings")]
    public float captureDuration = 5f;
    public float captureFPS = 10f;
    public string baseOutputDirectory = "Scans";

    [Header("Randomization")]
    public int fixedRandomSeed = 1337;

    public IEnumerator StartSampling(string outputSubFolder)
    {
        float interval = 1f / captureFPS;
        int totalFrames = Mathf.CeilToInt(captureDuration * captureFPS);

        Debug.Log($"Starting sampling for {captureDuration}s at {captureFPS} FPS. Saving to '{outputSubFolder}'.");

        for (int frame = 0; frame < totalFrames; frame++)
        {
            List<SampledPoint> allPointsForFrame = new List<SampledPoint>();
            SampleScenePoints(allPointsForFrame);
            SaveFrameToPCD(allPointsForFrame, frame, outputSubFolder);

            yield return new WaitForSeconds(interval);
        }

        Debug.Log("Sampling for current animation finished.");
    }

    void SampleScenePoints(List<SampledPoint> allPointsForFrame)
    {
        // Lists to manage resource cleanup
        var disposables = new List<IDisposable>();
        var objectsToDestroy = new List<UnityEngine.Object>();

        // A single JobHandle is used to chain all job dependencies together.
        JobHandle combinedDependency = new JobHandle();
        var results = new NativeList<SampledPoint>(100000, Allocator.TempJob);

        // Find all renderers in the scene
        var meshFilters = FindObjectsOfType<MeshFilter>();
        var skinnedRenderers = FindObjectsOfType<SkinnedMeshRenderer>();

        // Schedule a job for each static mesh
        foreach (MeshFilter mf in meshFilters)
        {
            if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
            ScheduleJobForMesh(mf.sharedMesh, mf.transform, mf.gameObject.name, results, disposables, ref combinedDependency);
        }

        // Bake and schedule a job for each skinned mesh
        foreach (SkinnedMeshRenderer smr in skinnedRenderers)
        {
            if (!smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
            Mesh bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);
            objectsToDestroy.Add(bakedMesh);
            ScheduleJobForMesh(bakedMesh, smr.transform, smr.gameObject.name, results, disposables, ref combinedDependency);
        }

        // Wait for the final job in the dependency chain to complete.
        combinedDependency.Complete();

        // Process results back on the main thread
        Bounds roomBounds = CalculateRoomBounds(roomObjectKeyword);
        foreach (var point in results)
        {
            if (roomBounds.Contains(point.position))
            {
                allPointsForFrame.Add(point);
            }
        }

        // --- Final Cleanup ---
        results.Dispose();

        // Dispose all NativeArrays
        foreach (var d in disposables)
        {
            d.Dispose();
        }

        // Destroy all temporary Meshes
        foreach (var obj in objectsToDestroy)
        {
            Destroy(obj);
        }
    }

    void ScheduleJobForMesh(Mesh mesh, Transform transform, string objectName, NativeList<SampledPoint> results, List<IDisposable> disposables, ref JobHandle dependency)
{
    if (mesh.vertexCount == 0 || mesh.triangles.Length == 0) return;

    // Pre-calculate areas on the main thread
    Vector3[] vertices = mesh.vertices;
    int[] triangles = mesh.triangles;
    float totalSurfaceArea = 0f;
    var triangleAreas = new NativeArray<float>(triangles.Length / 3, Allocator.TempJob);

    Matrix4x4 localToWorld = transform.localToWorldMatrix;
    for (int i = 0; i < triangles.Length; i += 3)
    {
        Vector3 v0 = localToWorld.MultiplyPoint3x4(vertices[triangles[i]]);
        Vector3 v1 = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
        Vector3 v2 = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);
        float area = Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;
        triangleAreas[i / 3] = area;
        totalSurfaceArea += area;
    }

    var cumulativeAreas = new NativeArray<float>(triangleAreas.Length, Allocator.TempJob);
    float runningSum = 0f;
    for (int i = 0; i < triangleAreas.Length; i++)
    {
        runningSum += triangleAreas[i];
        cumulativeAreas[i] = runningSum;
    }

    float objectDensity = GetDensityForObject(objectName);
    int totalPointsNeeded = Mathf.CeilToInt(totalSurfaceArea * objectDensity);
    if (totalPointsNeeded == 0)
    {
        triangleAreas.Dispose();
        cumulativeAreas.Dispose();
        return;
    }

    // Copy all necessary mesh data to native arrays
    var jobVertices = new NativeArray<Vector3>(mesh.vertices, Allocator.TempJob);
    var jobTriangles = new NativeArray<int>(mesh.triangles, Allocator.TempJob);
    var jobNormals = new NativeArray<Vector3>(mesh.normals, Allocator.TempJob);
    var jobUVs = new NativeArray<Vector2>(mesh.uv, Allocator.TempJob);

    // --- *** MODIFIED SECTION START *** ---
    // This logic is now safer and clearer.

    NativeArray<Color32> jobTextureData;
    bool hasUVsAndTexture = false;
    int texWidth = 0, texHeight = 0;

    Renderer renderer = transform.GetComponent<Renderer>();
    Texture2D texture = renderer?.sharedMaterial?.mainTexture as Texture2D;

    if (texture != null && texture.isReadable)
    {
        // If we have a valid, readable texture, get its data.
        jobTextureData = texture.GetRawTextureData<Color32>();
        texWidth = texture.width;
        texHeight = texture.height;
        hasUVsAndTexture = true;
    }
    else
    {
        // Otherwise, create a small dummy array.
        // This is necessary because the job struct requires the array to exist.
        jobTextureData = new NativeArray<Color32>(1, Allocator.TempJob);
    }
    // --- *** MODIFIED SECTION END *** ---

    // Create the job
    var job = new SampleMeshJob
    {
        vertices = jobVertices,
        triangles = jobTriangles,
        normals = jobNormals,
        uvs = jobUVs,
        textureData = jobTextureData,
        textureWidth = texWidth,
        textureHeight = texHeight,
        hasNormals = mesh.normals != null && mesh.normals.Length > 0,
        hasUVsAndTexture = hasUVsAndTexture,
        localToWorldMatrix = transform.localToWorldMatrix,
        totalSurfaceArea = totalSurfaceArea,
        cumulativeAreas = cumulativeAreas,
        totalPointsNeeded = totalPointsNeeded,
        labelHash = objectName.GetHashCode(),
        randomSeed = (uint)(fixedRandomSeed + objectName.GetHashCode()),
        outputPoints = results.AsParallelWriter()
    };
    
    // Schedule the job with the dependency, and update the handle to chain the next job.
    dependency = job.Schedule(dependency);

    // --- *** MODIFIED *** ---
    // Add ALL created native arrays to the disposables list for later cleanup.
    // There is no more conditional logic for disposing.
    disposables.Add(jobVertices);
    disposables.Add(jobTriangles);
    disposables.Add(jobNormals);
    disposables.Add(jobUVs);
    disposables.Add(jobTextureData); // Always add the texture data array.
    disposables.Add(triangleAreas);
    disposables.Add(cumulativeAreas);
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
            if (mf.name.ToLower().Contains(keyword.ToLower()) && mf.sharedMesh != null)
            {
                foreach (var v in mf.sharedMesh.vertices) encapsulate(mf.transform.TransformPoint(v));
            }
        }

        foreach (var smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (smr.name.ToLower().Contains(keyword.ToLower()) && smr.sharedMesh != null)
            {
                foreach (var v in smr.sharedMesh.vertices) encapsulate(smr.transform.TransformPoint(v));
            }
        }

        if (!initialized)
        {
            Debug.LogWarning($"Could not find any 'room' objects with meshes. Using a large default bound.");
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
                uint labelId = (uint)sp.labelHash;

                writer.WriteLine($"{p.x} {p.y} {p.z} {n.x} {n.y} {n.z} {rgb} {labelId}");
            }
        }
    }
}