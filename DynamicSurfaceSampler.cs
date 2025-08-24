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

    // *** NEW ***: A struct to hold all data for a single sampled point.
    private struct SampledPoint
    {
        public Vector3 position;
        public Color32 color;
        public string label;
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
            // *** MODIFIED ***: Use the new SampledPoint struct list.
            List<SampledPoint> sampledPoints = new List<SampledPoint>();

            SampleScenePoints(sampledPoints);
            SaveFrameToPCD(sampledPoints, frame, outputSubFolder);

            yield return new WaitForSeconds(interval);
        }

        Debug.Log("Sampling for current animation finished.");
    }

    // *** MODIFIED ***: Simplified signature to pass one list.
    void SampleScenePoints(List<SampledPoint> sampledPoints)
    {
        Bounds roomBounds = CalculateRoomBounds(roomObjectKeyword);

        foreach (MeshFilter mf in FindObjectsOfType<MeshFilter>())
        {
            if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
            SampleMeshPointsUniform(mf.sharedMesh, mf.transform, mf.gameObject.name, roomBounds, sampledPoints);
        }

        foreach (SkinnedMeshRenderer smr in FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!smr.gameObject.activeInHierarchy) continue;
            Mesh bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);
            SampleMeshPointsUniform(bakedMesh, smr.transform, smr.gameObject.name, roomBounds, sampledPoints);
        }
    }

    // *** MODIFIED ***: This is the core of the change. It now samples color.
    void SampleMeshPointsUniform(Mesh mesh, Transform transform, string objectName, Bounds roomBounds, List<SampledPoint> sampledPoints)
    {
        if (mesh.vertexCount == 0) return;

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector2[] uvs = mesh.uv; // *** NEW ***: Get UV coordinates.

        // *** NEW ***: Get the renderer and texture for color sampling.
        Renderer renderer = transform.GetComponent<Renderer>();
        Texture2D texture = null;
        if (renderer != null && renderer.sharedMaterial != null)
        {
            texture = renderer.sharedMaterial.mainTexture as Texture2D;
            if (texture != null && !texture.isReadable)
            {
                Debug.LogWarning($"Texture on '{objectName}' is not marked as Read/Write. Colors may be incorrect. Please enable it in texture import settings.", texture);
                texture = null; // Can't sample from a non-readable texture.
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

            // *** MODIFIED ***: Generate barycentric coordinates here to reuse them for UV interpolation.
            float u = Random.value;
            float v = Random.value;
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            Vector3 sample = v0 + u * (v1 - v0) + v * (v2 - v0);

            if (roomBounds.Contains(sample))
            {
                // *** NEW ***: Logic to calculate the color at the sampled point.
                Color32 pointColor = Color.white; // Default color
                if (texture != null && uvs.Length > 0)
                {
                    Vector2 uv0 = uvs[vertIndex0];
                    Vector2 uv1 = uvs[vertIndex1];
                    Vector2 uv2 = uvs[vertIndex2];
                    
                    // Interpolate UV coordinates using the same barycentric coordinates
                    Vector2 interpolatedUV = uv0 + u * (uv1 - uv0) + v * (uv2 - uv0);
                    
                    // Get the color from the texture
                    pointColor = texture.GetPixelBilinear(interpolatedUV.x, interpolatedUV.y);
                }

                // *** MODIFIED ***: Add the complete SampledPoint data to the list.
                sampledPoints.Add(new SampledPoint
                {
                    position = sample,
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

    // This method is no longer needed as its logic was moved into SampleMeshPointsUniform
    // Vector3 SamplePointOnTriangle(Vector3 v0, Vector3 v1, Vector3 v2) { ... }

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
    
    // *** MODIFIED ***: Saves the new format with RGB and label data.
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
            // *** MODIFIED ***: Added rgb and changed label type
            writer.WriteLine("FIELDS x y z rgb label"); 
            writer.WriteLine("SIZE 4 4 4 4 4"); // Size of float, float, float, uint32, uint32 (for label ID)
            writer.WriteLine("TYPE F F F U U"); // F=float, U=unsigned int
            writer.WriteLine("COUNT 1 1 1 1 1");
            writer.WriteLine($"WIDTH {points.Count}");
            writer.WriteLine("HEIGHT 1");
            writer.WriteLine("VIEWPOINT 0 0 0 1 0 0 0");
            writer.WriteLine($"POINTS {points.Count}");
            writer.WriteLine("DATA ascii");

            for (int i = 0; i < points.Count; i++)
            {
                SampledPoint sp = points[i];
                Vector3 p = sp.position;
                Color32 c = sp.color;

                // *** NEW ***: Pack R, G, B into a single 32-bit integer.
                uint rgb = ((uint)c.r << 16) | ((uint)c.g << 8) | ((uint)c.b);
                
                // *** NEW ***: Use the hashcode of the label string as a numeric ID.
                uint labelId = (uint)sp.label.GetHashCode();

                writer.WriteLine($"{p.x} {p.y} {p.z} {rgb} {labelId}");
            }
        }
    }
}