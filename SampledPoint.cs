using UnityEngine;

/// <summary>
/// A public struct to hold the data for a single sampled point.
/// By being in its own file, it's accessible by both the main script and the job script.
/// </summary>
public struct SampledPoint
{
    public Vector3 position;
    public Vector3 normal;
    public Color32 color;
    public int labelHash;
}