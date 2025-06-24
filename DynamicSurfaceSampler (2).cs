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
        [Tooltip("Object name keyword to match (case insensitive)")]
        public string objectKeyword;
        [Tooltip("Points per unit area for this object type")]
        public float pointsPerUnitArea;
        [Tooltip("Use exact name match instead of keyword search")]
        public bool exactMatch;
    }

    [Header("Capture Settings")]
    public float captureDuration = 5f;
    public float captureFPS = 10f;
    public string outputDirectory = "Scans";

    [Header("Randomization")]
    public int fixedRandomSeed = 1337;



    void Start()
    {
        StartCoroutine(DynamicSamplingCoroutine());
    }

    IEnumerator DynamicSamplingCoroutine()
    {
        float interval = 1f / captureFPS;
        int totalFrames = Mathf.CeilToInt(captureDuration * captureFPS);

        for (int frame = 0; frame < totalFrames; frame++)
        {
            List<Vector3> sampledPoints = new List<Vector3>();
            List<string> labels = new List<string>();

            SampleScenePoints(sampledPoints, labels);
            SaveFrameToPCD(sampledPoints, labels, frame);

            yield return new WaitForSeconds(interval);
        }
    }

    void SampleScenePoints(List<Vector3> sampledPoints, List<string> labels)
    {
        Bounds roomBounds = CalculateRoomBounds(roomObjectKeyword);

        // Sample from MeshFilter objects (static meshes)
        foreach (MeshFilter mf in FindObjectsOfType<MeshFilter>())
        {
            if (!mf.gameObject.activeInHierarchy) continue;
            
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;

            SampleMeshPointsUniform(mesh, mf.transform, mf.gameObject.name, roomBounds, sampledPoints, labels);
        }

        // Sample from SkinnedMeshRenderer objects (animated characters)
        foreach (SkinnedMeshRenderer smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!smr.gameObject.activeInHierarchy) continue;
            
            // Use current animated pose - requires Read/Write enabled on mesh
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

        // Get density for this specific object
        float objectDensity = GetDensityForObject(objectName);

        // Step 1: Calculate total surface area and triangle areas
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

        // Step 2: Calculate total points needed for this object based on surface area and object-specific density
        int totalPointsNeeded = Mathf.CeilToInt(totalSurfaceArea * objectDensity);
        
        if (totalPointsNeeded == 0) return;

        // Step 3: Create cumulative area array for weighted sampling
        List<float> cumulativeAreas = new List<float>();
        float runningSum = 0f;
        foreach (float area in triangleAreas)
        {
            runningSum += area;
            cumulativeAreas.Add(runningSum);
        }

        // Step 4: Sample points using weighted triangle selection
        Random.InitState(fixedRandomSeed + objectID);

        for (int pointIndex = 0; pointIndex < totalPointsNeeded; pointIndex++)
        {
            // Select triangle based on area weighting
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

            // Sample point on selected triangle
            int triIndex = selectedTriangle * 3;
            Vector3 v0 = transform.TransformPoint(vertices[triangles[triIndex]]);
            Vector3 v1 = transform.TransformPoint(vertices[triangles[triIndex + 1]]);
            Vector3 v2 = transform.TransformPoint(vertices[triangles[triIndex + 2]]);

            Vector3 sample = SamplePointOnTriangle(v0, v1, v2);
            
            // Only add if within room bounds
            if (roomBounds.Contains(sample))
            {
                sampledPoints.Add(sample);
                labels.Add(objectName);
            }
        }
    }

    float GetDensityForObject(string objectName)
    {
        // Debug: Print object name to console
        Debug.Log($"Checking density for object: '{objectName}'");
        
        // Check density overrides
        foreach (ObjectDensityOverride densityOverride in densityOverrides)
        {
            bool isMatch = false;
            
            if (densityOverride.exactMatch)
            {
                // Exact name match (case insensitive)
                isMatch = string.Equals(objectName, densityOverride.objectKeyword, System.StringComparison.OrdinalIgnoreCase);
                Debug.Log($"Exact match check: '{objectName}' vs '{densityOverride.objectKeyword}' = {isMatch}");
            }
            else
            {
                // Keyword search (case insensitive)
                isMatch = objectName.ToLower().Contains(densityOverride.objectKeyword.ToLower());
                Debug.Log($"Keyword match check: '{objectName}' contains '{densityOverride.objectKeyword}' = {isMatch}");
            }
            
            if (isMatch)
            {
                Debug.Log($"Using override density: {densityOverride.pointsPerUnitArea} for '{objectName}'");
                return densityOverride.pointsPerUnitArea;
            }
        }
        
        // Return default density if no override found
        Debug.Log($"Using default density: {defaultPointsPerUnitArea} for '{objectName}'");
        return defaultPointsPerUnitArea;
    }

    Vector3 SamplePointOnTriangle(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        float u = Random.value;
        float v = Random.value;
        if (u + v > 1f) { u = 1f - u; v = 1f - v; }
        return v0 + u * (v1 - v0) + v * (v2 - v0);
    }

    Bounds CalculateRoomBounds(string keyword)
    {
        Bounds bounds = new Bounds();
        bool initialized = false;

        // Check both MeshFilter and SkinnedMeshRenderer for room bounds
        foreach (MeshFilter mf in FindObjectsOfType<MeshFilter>())
        {
            if (!mf.name.ToLower().Contains(keyword.ToLower()))
                continue;

            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;

            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 worldPoint = mf.transform.TransformPoint(v);
                if (!initialized)
                {
                    bounds = new Bounds(worldPoint, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(worldPoint);
                }
            }
        }

        foreach (SkinnedMeshRenderer smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!smr.name.ToLower().Contains(keyword.ToLower()))
                continue;

            Mesh mesh = smr.sharedMesh;
            if (mesh == null) continue;

            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 worldPoint = smr.transform.TransformPoint(v);
                if (!initialized)
                {
                    bounds = new Bounds(worldPoint, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(worldPoint);
                }
            }
        }

        if (!initialized)
        {
            bounds = new Bounds(Vector3.zero, new Vector3(1000, 1000, 1000));
        }

        return bounds;
    }

    void SaveFrameToPCD(List<Vector3> points, List<string> labels, int frameIndex)
    {
        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        string filename = $"frame_{frameIndex:D3}.pcd";
        string path = Path.Combine(outputDirectory, filename);

        using (StreamWriter writer = new StreamWriter(path, false, Encoding.ASCII))
        {
            // PCD Header
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

            // Point data
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                writer.WriteLine($"{p.x} {p.y} {p.z} {labels[i]}");
            }
        }
    }
}