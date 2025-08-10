using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Waves : MonoBehaviour
{
    [Header("Environment")]
    public Vector2 windDirection = new Vector2(1, 0);
    public float windStrength = 1f;
    public Vector2 currentDirection = new Vector2(1, 0);
    public float currentStrength = 1f;

    [Header("Original Grid Emulation")]
    [Tooltip("Used only for octave math to emulate the original grid behavior.")]
    public int dimensions = 100;

    [Header("Base Mesh Size (fallback bounds)")]
    public float meshScale = 1f;

    [Header("Octaves")]
    public Octave[] octaves;

    // ---- Multi-LOD: inner cube + rings ----
    [Header("Inner High-Res Cube")]
    public float lodCubeSize = 200f;
    public int lodHighRes = 100;

    [Header("Multi-LOD Rings (outside the inner cube)")]
    [SerializeField] private float[] lodRingWidths = new float[] { 40f, 60f, 90f, 140f, 220f }; // outermost is bigger
    [SerializeField] private int[] lodRingRes = new int[] { 64, 48, 32, 24, 16 }; // decreasing outwards
    [SerializeField, Range(1, 6)] private int ringThicknessCells = 1;
    [SerializeField] private float targetCellSize = 8f;

    [Header("Runtime Rebuild")]
    public bool rebuildAtRuntime = true;
    public float rebuildDistanceThreshold = 10f;
    public bool rebuildOnLODParamChange = true;

    private Mesh mesh;
    private MeshFilter meshFilter;

    private NativeArray<float3> vertexData;
    private NativeArray<float2> uvData;          // pass UVs to job
    private NativeArray<OctaveData> octaveData;

    private Vector3[] vertices;
    private int[] triangles;
    private Vector2[] uvs;

    // tracking for runtime rebuilds
    private Vector3 lastCubeCenter;
    private int lastHighRes, lastRingWidthHash, lastRingResHash;
    private float lastCubeSize;
    private int lastVertexCount = -1;
    private int lastOctaveCount = -1;

    private void Awake()
    {
        EnsureDefaultOctaves();
    }

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        RegenerateMesh();
        mesh.MarkDynamic();

        EnsureVertexArray();
        EnsureUVArray();
        EnsureOctaveArray();

        CopyMeshVerticesToNative();
        CopyMeshUVsToNative();
        CopyOctavesToNative();
    }

    void OnDisable() => DisposeArrays();
    void OnDestroy() => DisposeArrays();

    private void EnsureDefaultOctaves()
    {
        if (octaves == null || octaves.Length == 0)
        {
            octaves = new Octave[]
            {
            // 1. Swell - large, slow waves
            new Octave {
                speed = new Vector2(0.25f, 0.18f),
                scale = new Vector2(0.05f, 0.05f),   // ~20m wavelength
                height = 2.5f,                       // was 1.2f
                perlinBlend = 0.1f,
                baseScaleMultiplier = 1f,
                active = true,
                windResponse = 0.3f,
                currentResponse = 0.6f,
                scaleFrequencyBoost = 0f           // was 2f
            },
            // 2. Main waves
            new Octave {
                speed = new Vector2(6.08f, 2.38f),
                scale = new Vector2(0.2f, 0.4f),   // ~7m wavelength
                height = 2.1f,                       // was 0.8f
                perlinBlend = 0.82f,
                baseScaleMultiplier = 1f,
                active = true,
                windResponse = 1.6f,
                currentResponse = 0.4f,
                scaleFrequencyBoost = 0f           // was 3f
            },
            // 3. Extra realism ripples
            new Octave {
                speed = new Vector2(3.93f, 2.91f),
                scale = new Vector2(0.4f, 0.4f),     // ~2.5m wavelength
                height = 2f,                       // was 0.35f
                perlinBlend = 1f,
                baseScaleMultiplier = 1f,
                active = true,
                windResponse = 1.4f,
                currentResponse = 0.3f,
                scaleFrequencyBoost = 0f             // was 4.5f
            },
            // 4. Choppy detail
            new Octave {
                speed = new Vector2(28f, 12f),
                scale = new Vector2(0.9f, 0.9f),     // ~1.1m wavelength
                height = 1.4f,                       // was 0.15f
                perlinBlend = 0.7f,
                baseScaleMultiplier = 1f,
                active = true,
                windResponse = 1.5f,
                currentResponse = 0.2f,
                scaleFrequencyBoost = 0f             // was 6f
            },
            // 5. Periodic longer waves
            new Octave {
                speed = new Vector2(20f, 10f),
                scale = new Vector2(0.4f, 0.65f),     // ~1.1m wavelength
                height = 2f,                       // was 0.15f
                perlinBlend = 0.8f,
                baseScaleMultiplier = 1f,
                active = true,
                windResponse = 1.5f,
                currentResponse = 0.2f,
                scaleFrequencyBoost = 0f             // was 6f
            }
            };
        }
    }



    void Update()
    {
        // Rebuild mesh if camera moved or LOD params changed
        if (rebuildAtRuntime && ShouldRebuild())
        {
            RegenerateMesh();
            EnsureVertexArray();
            EnsureUVArray();
            CopyMeshVerticesToNative();
            CopyMeshUVsToNative();
        }

        // Octave size may change live
        if (octaves.Length != lastOctaveCount)
            EnsureOctaveArray();
        CopyOctavesToNative();

        // Normalize safely
        var windDirNorm = windDirection.sqrMagnitude > 0f ? windDirection.normalized : Vector2.zero;
        var curDirNorm = currentDirection.sqrMagnitude > 0f ? currentDirection.normalized : Vector2.zero;

        // Schedule job
        var job = new WavesJob
        {
            time = Time.time,
            vertices = vertexData,
            windDirection = windDirNorm,
            windStrength = windStrength,
            currentDirection = curDirNorm,
            currentStrength = currentStrength,
            octaves = octaveData
        };

        var handle = job.Schedule(vertexData.Length, 128);
        handle.Complete();

        // Apply back to mesh
        var updated = new Vector3[vertexData.Length];
        for (int i = 0; i < vertexData.Length; i++) updated[i] = vertexData[i];

        mesh.SetVertices(updated);
        mesh.RecalculateNormals();
        // Keep a CPU copy in sync for height sampling
        vertices = updated;
        // mesh.RecalculateBounds(); // enable if waves can push verts far horizontally
    }

    // ---------- Mesh build & LOD ----------

    private bool ShouldRebuild()
    {
        Vector3 center = GetCubeCenter();
        float dist = Vector3.Distance(center, lastCubeCenter);
        bool movedEnough = dist >= rebuildDistanceThreshold;

        bool lodChanged = rebuildOnLODParamChange &&
                          (lodHighRes != lastHighRes ||
                           !Mathf.Approximately(lodCubeSize, lastCubeSize) ||
                           HashArray(lodRingWidths) != lastRingWidthHash ||
                           HashArray(lodRingRes) != lastRingResHash);

        return movedEnough || lodChanged;
    }

    private void RegenerateMesh()
    {
        GenerateMultiZoneLODMesh();

        if (mesh == null)
        {
            mesh = new Mesh
            {
                name = gameObject.name + "_Mesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            meshFilter.mesh = mesh;
        }
        else mesh.Clear();

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        // record state
        lastCubeCenter = GetCubeCenter();
        lastHighRes = lodHighRes;
        lastCubeSize = lodCubeSize;
        lastRingWidthHash = HashArray(lodRingWidths);
        lastRingResHash = HashArray(lodRingRes);
        lastVertexCount = vertices.Length;
    }

    private Vector3 GetCubeCenter()
    {
        Vector3 camPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        return new Vector3(camPos.x, transform.position.y, camPos.z);
    }

    // High-res inner square + N outer rectangular rings (4 strips each)
    private void GenerateMultiZoneLODMesh()
    {
        Vector3 cubeCenter = GetCubeCenter();
        float halfCube = Mathf.Max(1f, lodCubeSize * 0.5f);

        float minHX = cubeCenter.x - halfCube;
        float maxHX = cubeCenter.x + halfCube;
        float minHZ = cubeCenter.z - halfCube;
        float maxHZ = cubeCenter.z + halfCube;

        // UV area baseline
        float farExtent = 0f;
        for (int i = 0; i < lodRingWidths.Length; i++) farExtent += Mathf.Max(0f, lodRingWidths[i]);
        float areaSize = Mathf.Max(dimensions * meshScale, (halfCube + farExtent) * 2f);

        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        var uvsL = new System.Collections.Generic.List<Vector2>();

        // 1) Inner high-res grid
        int H = Mathf.Max(1, lodHighRes);
        int[,] innerIdx = new int[H + 1, H + 1];
        for (int j = 0; j <= H; j++)
        {
            float z = Mathf.Lerp(minHZ, maxHZ, j / (float)H);
            for (int i = 0; i <= H; i++)
            {
                float x = Mathf.Lerp(minHX, maxHX, i / (float)H);
                innerIdx[i, j] = verts.Count;
                verts.Add(new Vector3(x, 0f, z));
                uvsL.Add(new Vector2(x / areaSize, z / areaSize));
            }
        }
        for (int j = 0; j < H; j++)
        {
            for (int i = 0; i < H; i++)
            {
                int a = innerIdx[i, j];
                int b = innerIdx[i + 1, j];
                int c = innerIdx[i, j + 1];
                int d = innerIdx[i + 1, j + 1];
                tris.Add(a); tris.Add(d); tris.Add(b);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
        }

        // 2) Outer rings
        float prevMinX = minHX, prevMaxX = maxHX, prevMinZ = minHZ, prevMaxZ = maxHZ;
        int ringCount = Mathf.Min(lodRingWidths.Length, lodRingRes.Length);

        for (int r = 0; r < ringCount; r++)
        {
            float w = Mathf.Max(0.01f, lodRingWidths[r]);
            int R = Mathf.Max(1, lodRingRes[r]);

            float newMinX = prevMinX - w;
            float newMaxX = prevMaxX + w;
            float newMinZ = prevMinZ - w;
            float newMaxZ = prevMaxZ + w;

            int rowsAcross = (ringThicknessCells > 1)
                ? ringThicknessCells
                : Mathf.Max(1, Mathf.RoundToInt(w / Mathf.Max(1f, targetCellSize)));

            AddStripX(newMinX, newMaxX, prevMaxZ, newMaxZ, R, rowsAcross, ref verts, ref uvsL, ref tris, areaSize);
            AddStripX(newMinX, newMaxX, newMinZ, prevMinZ, R, rowsAcross, ref verts, ref uvsL, ref tris, areaSize);
            AddStripZ(newMinX, prevMinX, prevMinZ, prevMaxZ, R, rowsAcross, ref verts, ref uvsL, ref tris, areaSize);
            AddStripZ(prevMaxX, newMaxX, prevMinZ, prevMaxZ, R, rowsAcross, ref verts, ref uvsL, ref tris, areaSize);

            prevMinX = newMinX; prevMaxX = newMaxX;
            prevMinZ = newMinZ; prevMaxZ = newMaxZ;
        }

        vertices = verts.ToArray();
        triangles = tris.ToArray();
        uvs = uvsL.ToArray();
    }

    private static void AddStripX(
        float x0, float x1, float z0, float z1,
        int colsAlong, int rowsAcross,
        ref System.Collections.Generic.List<Vector3> verts,
        ref System.Collections.Generic.List<Vector2> uvs,
        ref System.Collections.Generic.List<int> tris,
        float areaSize)
    {
        int cols = Mathf.Max(1, colsAlong);
        int rows = Mathf.Max(1, rowsAcross);

        int[,] idx = new int[cols + 1, rows + 1];

        for (int j = 0; j <= rows; j++)
        {
            float z = Mathf.Lerp(z0, z1, j / (float)rows);
            for (int i = 0; i <= cols; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)cols);
                idx[i, j] = verts.Count;
                verts.Add(new Vector3(x, 0f, z));
                uvs.Add(new Vector2(x / areaSize, z / areaSize));
            }
        }
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                int a = idx[i, j];
                int b = idx[i + 1, j];
                int c = idx[i, 1 + j];
                int d = idx[i + 1, 1 + j];
                tris.Add(a); tris.Add(d); tris.Add(b);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
    }

    private static void AddStripZ(
        float x0, float x1, float z0, float z1,
        int colsAlong, int rowsAcross,
        ref System.Collections.Generic.List<Vector3> verts,
        ref System.Collections.Generic.List<Vector2> uvs,
        ref System.Collections.Generic.List<int> tris,
        float areaSize)
    {
        int cols = Mathf.Max(1, rowsAcross); // thickness along X
        int rows = Mathf.Max(1, colsAlong);  // along Z

        int[,] idx = new int[cols + 1, rows + 1];

        for (int j = 0; j <= rows; j++)
        {
            float z = Mathf.Lerp(z0, z1, j / (float)rows);
            for (int i = 0; i <= cols; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)cols);
                idx[i, j] = verts.Count;
                verts.Add(new Vector3(x, 0f, z));
                uvs.Add(new Vector2(x / areaSize, z / areaSize));
            }
        }
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                int a = idx[i, j];
                int b = idx[i + 1, j];
                int c = idx[i, 1 + j];
                int d = idx[i + 1, 1 + j];
                tris.Add(a); tris.Add(d); tris.Add(b);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }
    }

    // ---------- Height & normal sampling ----------

    /// <summary>Returns water height at a given world position (nearest-vertex, local-space safe).</summary>
    public float GetHeightAt(Vector3 worldPos)
    {
        if (vertices == null || vertices.Length == 0)
            return transform.position.y;

        // Convert query point to mesh local space
        Vector3 local = transform.InverseTransformPoint(worldPos);

        // Find nearest vertex in local XZ
        int closest = 0;
        float bestD2 = float.PositiveInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            float dx = v.x - local.x;
            float dz = v.z - local.z;
            float d2 = dx * dx + dz * dz;
            if (d2 < bestD2) { bestD2 = d2; closest = i; }
        }

        // Convert that local Y back to world Y
        float yLocal = vertices[closest].y;
        return transform.TransformPoint(new Vector3(0f, yLocal, 0f)).y;
    }

    /// <summary>Approximates the surface normal at worldPos by 4-sample gradient.</summary>
    public Vector3 GetNormalAt(Vector3 worldPos, float sampleRadius = 0.5f)
    {
        float dx = Mathf.Max(0.05f, sampleRadius);
        float dz = Mathf.Max(0.05f, sampleRadius);

        float hL = GetHeightAt(worldPos + new Vector3(-dx, 0f, 0f));
        float hR = GetHeightAt(worldPos + new Vector3(dx, 0f, 0f));
        float hD = GetHeightAt(worldPos + new Vector3(0f, 0f, -dz));
        float hU = GetHeightAt(worldPos + new Vector3(0f, 0f, dz));

        float slopeX = (hR - hL) / (2f * dx);
        float slopeZ = (hU - hD) / (2f * dz);

        Vector3 n = new Vector3(-slopeX, 1f, -slopeZ);
        return n.normalized;
    }

    // ---------- Native arrays & copying ----------

    private void EnsureVertexArray()
    {
        if (vertexData.IsCreated) vertexData.Dispose();
        vertexData = new NativeArray<float3>(vertices.Length, Allocator.Persistent);
        lastVertexCount = vertices.Length;
    }

    private void EnsureUVArray()
    {
        if (uvData.IsCreated) uvData.Dispose();
        uvData = new NativeArray<float2>(uvs.Length, Allocator.Persistent);
    }

    private void EnsureOctaveArray()
    {
        if (octaveData.IsCreated) octaveData.Dispose();
        octaveData = new NativeArray<OctaveData>(octaves.Length, Allocator.Persistent);
        lastOctaveCount = octaves.Length;
    }

    private void CopyMeshVerticesToNative()
    {
        for (int i = 0; i < vertices.Length; i++)
            vertexData[i] = vertices[i];
    }

    private void CopyMeshUVsToNative()
    {
        for (int i = 0; i < uvs.Length; i++)
            uvData[i] = new float2(uvs[i].x, uvs[i].y);
    }

    private void CopyOctavesToNative()
    {
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
                currentResponse = o.currentResponse,
                scaleFrequencyBoost = o.scaleFrequencyBoost
            };
        }
    }

    private void DisposeArrays()
    {
        if (vertexData.IsCreated) vertexData.Dispose();
        if (uvData.IsCreated) uvData.Dispose();
        if (octaveData.IsCreated) octaveData.Dispose();
    }

    // ---------- Gizmos ----------

    private void OnDrawGizmos()
    {
        if (Camera.main == null) return;
        Vector3 center = GetCubeCenter();
        Vector3 size = new Vector3(lodCubeSize, lodCubeSize, lodCubeSize);
        Gizmos.color = Color.cyan; Gizmos.DrawWireCube(center, size);
        Gizmos.color = Color.blue; Gizmos.DrawSphere(center, 2f);
    }

    // ---------- Types & utils ----------

    [Serializable]
    public struct Octave
    {
        public Vector2 speed;
        public Vector2 scale;
        public float height;
        [Range(0f, 1f)] public float perlinBlend;
        public float baseScaleMultiplier;
        public bool active;
        [Range(0f, 2f)] public float windResponse;
        [Range(0f, 2f)] public float currentResponse;

        // NEW
        [UnityEngine.Tooltip("How strongly Scale affects frequency for this octave (1 = raw, 4 = punchy).")]
        public float scaleFrequencyBoost;
    }

    private static int HashArray(int[] arr)
    {
        unchecked
        {
            int h = 17;
            for (int i = 0; i < arr.Length; i++) h = h * 31 + arr[i];
            return h;
        }
    }
    private static int HashArray(float[] arr)
    {
        unchecked
        {
            int h = 17;
            for (int i = 0; i < arr.Length; i++) h = h * 31 + arr[i].GetHashCode();
            return h;
        }
    }
}
