using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System;

/// <summary>
/// Generates and animates a dynamic water mesh using multiple wave octaves.
/// Mesh vertices are updated in parallel using a job system, with environmental influences like wind and current.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Waves : MonoBehaviour
{
    // --- Environmental Influences ---
    /// <summary>Direction of wind affecting the waves.</summary>
    public Vector2 windDirection = new Vector2(1, 0);
    /// <summary>Strength of wind (0 to 1).</summary>
    public float windStrength = 1f;
    /// <summary>Direction of water current.</summary>
    public Vector2 currentDirection = new Vector2(1, 0);
    /// <summary>Strength of current (0 to 1).</summary>
    public float currentStrength = 1f;

    // --- Mesh Settings ---
    /// <summary>Number of grid cells per side (mesh resolution).</summary>
    public int dimensions = 100;
    /// <summary>Scale factor for mesh size.</summary>
    public float meshScale = 1f;

    // --- Wave Octaves ---
    /// <summary>Array of octave settings controlling wave layers.</summary>
    public Octave[] octaves;

    /// <summary>Mesh representing the water surface.</summary>
    private Mesh mesh;
    /// <summary>MeshFilter component reference.</summary>
    private MeshFilter meshFilter;
    /// <summary>Native array of mesh vertex positions for job processing.</summary>
    private NativeArray<float3> vertexData;
    /// <summary>Native array of octave data for job processing.</summary>
    private NativeArray<OctaveData> octaveData;

    /// <summary>
    /// Initializes the mesh and native arrays, and sets up octave data.
    /// </summary>
    private void Start()
    {
        mesh = new Mesh
        {
            name = gameObject.name + "_Mesh",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        mesh.vertices = GenerateVertices();
        mesh.triangles = GenerateTriangles();
        mesh.uv = GenerateUVs();
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        meshFilter = GetComponent<MeshFilter>();
        meshFilter.mesh = mesh;

        // Allocate native vertex array for job usage
        vertexData = new NativeArray<float3>(mesh.vertices.Length, Allocator.Persistent);

        // Copy initial vertex positions
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
            vertexData[i] = verts[i];

        // Convert Octave settings to native struct array
        octaveData = new NativeArray<OctaveData>(octaves.Length, Allocator.Persistent);
        for (int i = 0; i < octaves.Length; i++)
        {
            var o = octaves[i];
            octaveData[i] = new OctaveData
            {
                scale = o.scale,
                speed = o.speed,
                height = o.height,
                perlinBlend = o.perlinBlend,
                baseScaleMultiplier = o.baseScaleMultiplier,
                active = o.active,
                windResponse = o.windResponse,
                currentResponse = o.currentResponse
            };
        }
    }

    /// <summary>
    /// Releases native arrays when the object is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (vertexData.IsCreated) vertexData.Dispose();
        if (octaveData.IsCreated) octaveData.Dispose();
    }

    /// <summary>
    /// Updates the mesh vertices each frame using the WavesJob.
    /// Octave data and environmental influences are refreshed from inspector values.
    /// </summary>
    void Update()
    {
        // Update octave native data with latest inspector values
        for (int i = 0; i < octaves.Length; i++)
        {
            octaveData[i] = new OctaveData
            {
                scale = octaves[i].scale,
                speed = octaves[i].speed,
                height = octaves[i].height,
                perlinBlend = octaves[i].perlinBlend,
                baseScaleMultiplier = octaves[i].baseScaleMultiplier,
                active = octaves[i].active,
                windResponse = octaves[i].windResponse,
                currentResponse = octaves[i].currentResponse
            };
        }

        // Normalize wind and current directions
        windDirection = windDirection.sqrMagnitude > 0f ? windDirection.normalized : Vector2.zero;
        currentDirection = currentDirection.sqrMagnitude > 0f ? currentDirection.normalized : Vector2.zero;

        // Set up and schedule the wave simulation job
        var job = new WavesJob
        {
            dimensions = dimensions,
            time = Time.time,
            vertices = vertexData,
            windDirection = windDirection.normalized,
            windStrength = Mathf.Clamp01(windStrength),
            currentDirection = currentDirection.normalized,
            currentStrength = Mathf.Clamp01(currentStrength),
            octaves = octaveData
        };

        JobHandle handle = job.Schedule(vertexData.Length, 64);
        handle.Complete();

        // Copy updated vertex positions back to the mesh
        Vector3[] updatedVerts = new Vector3[vertexData.Length];
        for (int i = 0; i < vertexData.Length; i++)
            updatedVerts[i] = vertexData[i];

        mesh.vertices = updatedVerts;
        mesh.RecalculateNormals();
    }

    /// <summary>
    /// Generates the initial grid of mesh vertices.
    /// </summary>
    /// <returns>Array of vertex positions.</returns>
    private Vector3[] GenerateVertices()
    {
        Vector3[] vertices = new Vector3[(dimensions + 1) * (dimensions + 1)];
        for (int i = 0; i <= dimensions; i++)
        {
            for (int j = 0; j <= dimensions; j++)
            {
                vertices[Index(i, j)] = new Vector3(i * meshScale, 0, j * meshScale);
            }
        }
        return vertices;
    }

    /// <summary>
    /// Generates triangle indices for the mesh grid.
    /// </summary>
    /// <returns>Array of triangle indices.</returns>
    private int[] GenerateTriangles()
    {
        int[] triangles = new int[dimensions * dimensions * 6];
        int t = 0;

        for (int i = 0; i < dimensions; i++)
        {
            for (int j = 0; j < dimensions; j++)
            {
                int a = Index(i, j);
                int b = Index(i + 1, j);
                int c = Index(i, j + 1);
                int d = Index(i + 1, j + 1);

                triangles[t++] = a;
                triangles[t++] = d;
                triangles[t++] = b;

                triangles[t++] = a;
                triangles[t++] = c;
                triangles[t++] = d;
            }
        }
        return triangles;
    }

    /// <summary>
    /// Generates UV coordinates for the mesh grid.
    /// </summary>
    /// <returns>Array of UV coordinates.</returns>
    private Vector2[] GenerateUVs()
    {
        Vector2[] uvs = new Vector2[(dimensions + 1) * (dimensions + 1)];
        for (int i = 0; i <= dimensions; i++)
        {
            for (int j = 0; j <= dimensions; j++)
            {
                uvs[Index(i, j)] = new Vector2((float)i / dimensions, (float)j / dimensions);
            }
        }
        return uvs;
    }

    /// <summary>
    /// Gets the interpolated height of the water surface at a given world position.
    /// </summary>
    /// <param name="position">World position to sample.</param>
    /// <returns>Height of the water surface at the position.</returns>
    public float GetHeightAt(Vector3 position)
    {
        // Convert to local space (ignore Y)
        Vector3 localPos = transform.InverseTransformPoint(position);

        // Find grid cell
        float x = Mathf.Clamp(localPos.x, 0, dimensions);
        float z = Mathf.Clamp(localPos.z, 0, dimensions);

        int x0 = Mathf.FloorToInt(x);
        int z0 = Mathf.FloorToInt(z);
        int x1 = Mathf.Min(x0 + 1, dimensions);
        int z1 = Mathf.Min(z0 + 1, dimensions);

        // Interpolation weights
        float u = x - x0;
        float v = z - z0;

        // Get heights from surrounding vertices
        float h00 = mesh.vertices[Index(x0, z0)].y;
        float h10 = mesh.vertices[Index(x1, z0)].y;
        float h01 = mesh.vertices[Index(x0, z1)].y;
        float h11 = mesh.vertices[Index(x1, z1)].y;

        // Bilinear interpolation
        float height = (1 - u) * (1 - v) * h00
                     + u * (1 - v) * h10
                     + (1 - u) * v * h01
                     + u * v * h11;

        // Convert back to world Y
        return transform.TransformPoint(new Vector3(0, height, 0)).y;
    }

    /// <summary>
    /// Converts 2D grid coordinates to 1D array index.
    /// </summary>
    private int Index(int i, int j) => i * (dimensions + 1) + j;

    /// <summary>
    /// Struct for configuring individual wave octaves.
    /// </summary>
    [Serializable]
    public struct Octave
    {
        /// <summary>Speed of the wave movement for this octave.</summary>
        public Vector2 speed;
        /// <summary>Scale of the wave pattern for this octave.</summary>
        public Vector2 scale;
        /// <summary>Amplitude (height) of the wave for this octave.</summary>
        public float height;
        /// <summary>Blend factor between sine/cosine and perlin noise (0=sine, 1=perlin).</summary>
        [Range(0f, 1f)] public float perlinBlend;
        /// <summary>Multiplier for base scale (used for first octaves).</summary>
        public float baseScaleMultiplier;
        /// <summary>If true, this octave is active in the simulation.</summary>
        public bool active;

        /// <summary>How much this octave responds to wind (0 = none, 1 = full).</summary>
        [Tooltip("How much this octave reacts to wind (0 = none, 1 = full)")]
        [Range(0f, 2f)] public float windResponse;

        /// <summary>How much this octave responds to current (0 = none, 1 = full).</summary>
        [Tooltip("How much this octave reacts to current (0 = none, 1 = full)")]
        [Range(0f, 2f)] public float currentResponse;
    }
}
