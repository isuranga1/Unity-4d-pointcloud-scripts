using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
    
    void Start()
    {
        // The BatchProcessor now controls when sampling starts.
    }

    public IEnumerator StartSampling(string outputSubFolder)
    {
        float interval = 1f / captureFPS;
        int totalFrames = Mathf.CeilToInt(captureDuration * captureFPS);

        Debug.Log($"Starting sampling for {captureDuration}s at {captureFPS} FPS. Saving to '{outputSubFolder}'.");

        for (int frame = 0; frame < totalFrames; frame++)
        {
            List<Vector3> sampledPoints = new List<Vector3>();
            List<string> labels = new List<string>();

            SampleScenePoints(sampledPoints, labels);
            SaveFrameToPCD(sampledPoints, labels, frame, outputSubFolder);

            yield return new WaitForSeconds(interval);
        }

        Debug.Log("Sampling for current animation finished.");
    }
    
    void SampleScenePoints(List<Vector3> sampledPoints, List<string> labels)
    {
        Bounds roomBounds = CalculateRoomBounds(roomObjectKeyword);

        foreach (MeshFilter mf in FindObjectsOfType<MeshFilter>())
        {
            if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
            SampleMeshPointsUniform(mf.sharedMesh, mf.transform, mf.gameObject.name, roomBounds, sampledPoints, labels);
        }

        foreach (SkinnedMeshRenderer smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!smr.gameObject.activeInHierarchy) continue;
            Mesh bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);
            SampleMeshPointsUniform(bakedMesh, smr.transform, smr.gameObject.name, roomBounds, sampledPoints, labels);
        }
    }

    void SampleMeshPointsUniform(Mesh mesh, Transform transform, string objectName, Bounds roomBounds, List<Vector3> sampledPoints, List<string> labels)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
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
            Vector3 v0 = transform.TransformPoint(vertices[triangles[triIndex]]);
            Vector3 v1 = transform.TransformPoint(vertices[triangles[triIndex + 1]]);
            Vector3 v2 = transform.TransformPoint(vertices[triangles[triIndex + 2]]);
            Vector3 sample = SamplePointOnTriangle(v0, v1, v2);

            if (roomBounds.Contains(sample))
            {
                sampledPoints.Add(sample);
                labels.Add(objectName);
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

    Vector3 SamplePointOnTriangle(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        float u = Random.value;
        float v = Random.value;
        if (u + v > 1f) { u = 1f - u; v = 1f - v; }
        return v0 + u * (v1 - v0) + v * (v2 - v0);
    }

    // *** MODIFIED METHOD ***
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

        // *** Loop through MeshFilters with a null check
        foreach (var mf in FindObjectsOfType<MeshFilter>())
        {
            bool hasMesh = mf.sharedMesh != null;
            bool nameMatches = mf.name.ToLower().Contains(keyword.ToLower());
            // *** NEW: Added a detailed log to help diagnose the issue.
            // Debug.Log($"Checking MeshFilter on '{mf.name}': Name matches = {nameMatches}, Has mesh = {hasMesh}");

            if (nameMatches && hasMesh)
            {
                foreach (var v in mf.sharedMesh.vertices) encapsulate(mf.transform.TransformPoint(v));
            }
        }
        
        // *** Loop through SkinnedMeshRenderers with a null check
        foreach (var smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            bool hasMesh = smr.sharedMesh != null;
            bool nameMatches = smr.name.ToLower().Contains(keyword.ToLower());
            // *** NEW: Added a detailed log to help diagnose the issue.
            // Debug.Log($"Checking SkinnedMeshRenderer on '{smr.name}': Name matches = {nameMatches}, Has mesh = {hasMesh}");

            if (nameMatches && hasMesh)
            {
                foreach (var v in smr.sharedMesh.vertices) encapsulate(smr.transform.TransformPoint(v));
            }
        }

        if (!initialized)
        {
            // Fallback if no room object is found at all
            Debug.LogWarning($"Could not find any 'room' objects with meshes to calculate bounds. Using a large default bound.");
            bounds = new Bounds(Vector3.zero, new Vector3(1000, 1000, 1000));
        }
        else
        {
            Debug.Log("Successfully calculated room bounds.");
        }

        return bounds;
    }
    
    void SaveFrameToPCD(List<Vector3> points, List<string> labels, int frameIndex, string outputSubFolder)
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
            writer.WriteLine("FIELDS x y z label");
            writer.WriteLine("SIZE 4 4 4 1");
            writer.WriteLine("TYPE F F F S");
            writer.WriteLine("COUNT 1 1 1 1");
            writer.WriteLine($"WIDTH {points.Count}");
            writer.WriteLine("HEIGHT 1");
            writer.WriteLine("VIEWPOINT 0 0 0 1 0 0 0");
            writer.WriteLine($"POINTS {points.Count}");
            writer.WriteLine("DATA ascii");

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                writer.WriteLine($"{p.x} {p.y} {p.z} {labels[i]}");
            }
        }
    }
}