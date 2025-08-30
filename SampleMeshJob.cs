using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// A Burst-compiled C# Job that samples points on a single mesh.
/// This runs in parallel with other jobs.
/// </summary>
[BurstCompile]
public struct SampleMeshJob : IJob
{
    // --- Input Data (read-only) ---
    [ReadOnly] public NativeArray<Vector3> vertices;
    [ReadOnly] public NativeArray<int> triangles;
    [ReadOnly] public NativeArray<Vector3> normals;
    [ReadOnly] public NativeArray<Vector2> uvs;
    [ReadOnly] public NativeArray<Color32> textureData;

    public int textureWidth;
    public int textureHeight;
    public bool hasNormals;
    public bool hasUVsAndTexture;
    public Matrix4x4 localToWorldMatrix;
    public float totalSurfaceArea;
    [ReadOnly] public NativeArray<float> cumulativeAreas;
    public int totalPointsNeeded;
    public int labelHash;
    public uint randomSeed;

    // --- Output Data (writable from any thread) ---
    public NativeList<SampledPoint>.ParallelWriter outputPoints;

    public void Execute()
    {
        if (totalPointsNeeded == 0) return;

        // Use Unity.Mathematics.Random, which is safe for jobs
        var random = new Unity.Mathematics.Random(randomSeed);

        for (int i = 0; i < totalPointsNeeded; i++)
        {
            // Select a triangle weighted by its area
            float randomValue = random.NextFloat() * totalSurfaceArea;
            int selectedTriangle = 0;
            for (int j = 0; j < cumulativeAreas.Length; j++)
            {
                if (randomValue <= cumulativeAreas[j])
                {
                    selectedTriangle = j;
                    break;
                }
            }

            int triIndex = selectedTriangle * 3;
            int vertIndex0 = triangles[triIndex];
            int vertIndex1 = triangles[triIndex + 1];
            int vertIndex2 = triangles[triIndex + 2];

            Vector3 v0_local = vertices[vertIndex0];
            Vector3 v1_local = vertices[vertIndex1];
            Vector3 v2_local = vertices[vertIndex2];

            // Generate barycentric coordinates for a random point within the triangle
            float u = random.NextFloat();
            float v = random.NextFloat();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            float w = 1f - u - v;

            // Interpolate position and transform to world space
            Vector3 point_local = v0_local * w + v1_local * u + v2_local * v;
            Vector3 point_world = localToWorldMatrix.MultiplyPoint3x4(point_local);

            // Interpolate color from texture data
            Color32 pointColor = new Color32(255, 255, 255, 255);
            if (hasUVsAndTexture)
            {
                Vector2 uv0 = uvs[vertIndex0];
                Vector2 uv1 = uvs[vertIndex1];
                Vector2 uv2 = uvs[vertIndex2];
                Vector2 interpolatedUV = uv0 * w + uv1 * u + uv2 * v;

                int x = (int)(interpolatedUV.x * textureWidth);
                int y = (int)(interpolatedUV.y * textureHeight);
                int texIndex = y * textureWidth + x;

                if (texIndex >= 0 && texIndex < textureData.Length)
                {
                    pointColor = textureData[texIndex];
                }
            }

            // Interpolate normal and transform to world space
            Vector3 pointNormal_world = localToWorldMatrix.MultiplyVector(Vector3.up).normalized;
            if (hasNormals)
            {
                Vector3 n0_local = normals[vertIndex0];
                Vector3 n1_local = normals[vertIndex1];
                Vector3 n2_local = normals[vertIndex2];
                Vector3 normal_local = (n0_local * w + n1_local * u + n2_local * v).normalized;
                pointNormal_world = localToWorldMatrix.MultiplyVector(normal_local).normalized;
            }

            // Add the final point to the output list
            outputPoints.AddNoResize(new SampledPoint
            {
                position = point_world,
                normal = pointNormal_world,
                color = pointColor,
                labelHash = labelHash
            });
        }
    }
}