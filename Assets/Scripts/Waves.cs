using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System;
using UnityEngine.Serialization; // for FormerlySerializedAs

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
    [SerializeField] private float[] lodRingWidths = new float[] { 40f, 60f, 90f, 140f, 220f };
    [SerializeField] private int[] lodRingRes = new int[] { 64, 48, 32, 24, 16 };
    [SerializeField, Range(1, 6)] private int ringThicknessCells = 1;
    [SerializeField] private float targetCellSize = 8f;

    [Header("Runtime Rebuild")]
    public bool rebuildAtRuntime = true;
    public float rebuildDistanceThreshold = 10f;
    public bool rebuildOnLODParamChange = true;

    private Mesh mesh;
    private MeshFilter meshFilter;

    private NativeArray<float3> vertexData;
    private NativeArray<float2> uvData;
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
            // These defaults mirror your previous ones but split the old 'speed' into direction + moveSpeed.
            octaves = new Octave[]
            {
                new Octave {
                    direction = new Vector2(0.2f, 0.8f), moveSpeed = new Vector2(0.6f, 2.2f).magnitude,
                    scale = new Vector2(0.016f, 0.05f),
                    height = 9f, perlinBlend = 0.7f, baseScaleMultiplier = 1f, active = true,
                    windResponse = 1.6f, currentResponse = 0.2f, scaleFrequencyBoost = 0.012f
                },
                new Octave {
                    direction = new Vector2(0.3f, 0.9f), moveSpeed = new Vector2(6f, 14f).magnitude,
                    scale = new Vector2(0.09f, 0.2f),
                    height = 5f, perlinBlend = 0.8f, baseScaleMultiplier = 1f, active = true,
                    windResponse = 1.3f, currentResponse = 0.2f, scaleFrequencyBoost = 0.0104f
                },
                new Octave {
                    direction = new Vector2(0.3f, 0.8f), moveSpeed = new Vector2(8f, 20f).magnitude,
                    scale = new Vector2(0.2f, 0.3f),
                    height = 2f, perlinBlend = 0.8f, baseScaleMultiplier = 1f, active = true,
                    windResponse = 1f, currentResponse = 0.2f, scaleFrequencyBoost = 0f
                },
                new Octave {
                    direction = new Vector2(0.1f, 0.9f), moveSpeed = new Vector2(28f, 12f).magnitude,
                    scale = new Vector2(0.4f, 0.7f),
                    height = 0.8f, perlinBlend = 0.7f, baseScaleMultiplier = 1f, active = true,
                    windResponse = 0.2f, currentResponse = 0.2f, scaleFrequencyBoost = 0f
                },
                new Octave {
                    direction = new Vector2(0.2f, 1f), moveSpeed = new Vector2(3.93f, 2.91f).magnitude,
                    scale = new Vector2(0.2f, 0.5f),
                    height = 1.1f, perlinBlend = 1f, baseScaleMultiplier = 1f, active = true,
                    windResponse = 0.8f, currentResponse = 0.3f, scaleFrequencyBoost = 0f
                },
                new Octave {
                    direction = new Vector2(0.1f, 1f), moveSpeed = new Vector2(34.8f, 30.2f).magnitude,
                    scale = new Vector2(1f, 1.8f),
                    height = 0.35f, perlinBlend = 1f, baseScaleMultiplier = 1f, active = true,
                    windResponse = 1.2f, currentResponse = 0.25f, scaleFrequencyBoost = 0f
                }
            };
        }
    }

    public float GetHeightAt(Vector3 worldPos)
    {
        if (vertices == null || vertices.Length == 0) return transform.position.y;
        float minDistSq = float.MaxValue;
        float h = transform.position.y;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = vertices[i];
            float dx = p.x - worldPos.x;
            float dz = p.z - worldPos.z;
            float d2 = dx * dx + dz * dz;
            if (d2 < minDistSq) { minDistSq = d2; h = p.y; }
        }
        return h + transform.position.y;
    }

    // Approximate vertical amplitude from all active octaves, matching WavesJob logic.
    public float EstimateSurfaceAmplitude()
    {
        if (octaves == null || octaves.Length == 0) return 1f;

        float total = 0f;
        for (int i = 0; i < octaves.Length; i++)
        {
            var oc = octaves[i];
            if (!oc.active) continue;

            float ampFromEnv = 1f;
            if (i == 0)
                ampFromEnv += currentStrength * oc.currentResponse * 1.2f;
            else
                ampFromEnv += (windStrength * oc.windResponse + currentStrength * oc.currentResponse) * 0.3f;

            total += Mathf.Abs(oc.height * ampFromEnv);
        }
        return Mathf.Max(0.1f, total);
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
            // Ensure a valid, normalized direction
            Vector2 dir = o.direction.sqrMagnitude > 0f ? o.direction.normalized :
                          (windDirection.sqrMagnitude > 0f ? windDirection.normalized : Vector2.right);

            octaveData[i] = new OctaveData
            {
                direction = new Unity.Mathematics.float2(dir.x, dir.y),
                moveSpeed = Mathf.Max(0f, o.moveSpeed),

                scale = o.scale,
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
        [FormerlySerializedAs("speed")]
        [Tooltip("Normalized travel direction of this octave (fallback to wind if zero).")]
        public Vector2 direction;   // NEW: replaces 'speed' (migrated via attribute + OnValidate)
        [Tooltip("Scalar speed along 'direction' (units/sec).")]
        public float moveSpeed;     // NEW

        public Vector2 scale;
        public float height;
        [Range(0f, 1f)] public float perlinBlend;
        public float baseScaleMultiplier;
        public bool active;
        [Range(0f, 2f)] public float windResponse;
        [Range(0f, 2f)] public float currentResponse;

        [UnityEngine.Tooltip("How strongly Scale affects frequency for this octave (1 = raw, 4 = punchy).")]
        public float scaleFrequencyBoost;
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

        // Normalize env dirs once (job expects normalized)
        var windDirNorm = windDirection.sqrMagnitude > 0f ? windDirection.normalized : Vector2.zero;
        var curDirNorm = currentDirection.sqrMagnitude > 0f ? currentDirection.normalized : Vector2.zero;

        var job = new WavesJob
        {
            time = Time.time,
            vertices = vertexData,
            windDirection = new float2(windDirNorm.x, windDirNorm.y),
            windStrength = windStrength,
            currentDirection = new float2(curDirNorm.x, curDirNorm.y),
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
        vertices = updated;
    }

    private bool ShouldRebuild()
    {
        Vector3 centerSnapped = GetCubeCenter();
        float dist = Vector3.Distance(centerSnapped, lastCubeCenter);
        bool movedEnough = dist >= Mathf.Max(0.5f * GetInnerCellSize(), rebuildDistanceThreshold);

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

        // 2) Build expanding rings outward
        float prevMinX = minHX, prevMaxX = maxHX;
        float prevMinZ = minHZ, prevMaxZ = maxHZ;

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

            // Top & bottom strips (extend along X)
            AddStripX(newMinX, newMaxX, prevMaxZ, newMaxZ, R, rowsAcross,
                      ref verts, ref uvsL, ref tris, areaSize);
            AddStripX(newMinX, newMaxX, newMinZ, prevMinZ, R, rowsAcross,
                      ref verts, ref uvsL, ref tris, areaSize);

            // Left & right strips (extend along Z)
            AddStripZ(newMinX, prevMinX, prevMinZ, prevMaxZ, R, rowsAcross,
                      ref verts, ref uvsL, ref tris, areaSize);
            AddStripZ(prevMaxX, newMaxX, prevMinZ, prevMaxZ, R, rowsAcross,
                      ref verts, ref uvsL, ref tris, areaSize);

            prevMinX = newMinX; prevMaxX = newMaxX;
            prevMinZ = newMinZ; prevMaxZ = newMaxZ;
        }

        vertices = verts.ToArray();
        triangles = tris.ToArray();
        uvs = uvsL.ToArray();
    }

    void AddStripX(
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

    void AddStripZ(
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

    /// <summary>Approximates the surface normal at worldPos by 4-sample gradient.</summary>
public Vector3 GetNormalAt(Vector3 worldPos, float sampleRadius = 0.5f)
{
    float dx = Mathf.Max(0.05f, sampleRadius);
    float dz = Mathf.Max(0.05f, sampleRadius);

    float hL = GetHeightAt(worldPos + new Vector3(-dx, 0f, 0f));
    float hR = GetHeightAt(worldPos + new Vector3(dx, 0f, 0f));
    float hD = GetHeightAt(worldPos + new Vector3(0f, 0f, -dz));
    float hU = GetHeightAt(worldPos + new Vector3(0f, 0f,  dz));

    float slopeX = (hR - hL) / (2f * dx);
    float slopeZ = (hU - hD) / (2f * dz);

    Vector3 n = new Vector3(-slopeX, 1f, -slopeZ);
    return n.normalized;
}
    float GetInnerCellSize()
    {
        int H = Mathf.Max(1, lodHighRes);
        float halfCube = Mathf.Max(1f, lodCubeSize * 0.5f);
        return (halfCube * 2f) / H; // world meters per inner-grid cell
    }

    private Vector3 GetCubeCenter()
    {
        Vector3 camPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

        float cell = GetInnerCellSize();
        float x = Mathf.Round(camPos.x / cell) * cell;
        float z = Mathf.Round(camPos.z / cell) * cell;

        // keep sea level (y) from the water object
        return new Vector3(x, transform.position.y, z);
    }

    // Existing mesh builders retained…

    // ---------- Migration & validation ----------
    void OnValidate()
    {
        if (octaves == null) return;

        for (int i = 0; i < octaves.Length; i++)
        {
            var o = octaves[i];

            // If this asset was saved with the old 'speed' field, FormerlySerializedAs
            // will place that vector into 'direction'. Convert its length to moveSpeed
            // and normalize to keep behavior identical.
            if (o.moveSpeed <= 1e-5f && o.direction.sqrMagnitude > 1e-6f)
            {
                o.moveSpeed = o.direction.magnitude;
                if (o.moveSpeed > 1e-5f) o.direction /= o.moveSpeed;
            }

            // If no direction after migration, fall back to global wind or +X
            if (o.direction.sqrMagnitude <= 1e-6f)
            {
                var w = windDirection.sqrMagnitude > 0f ? windDirection.normalized : new Vector2(1f, 0f);
                o.direction = w;
            }

            octaves[i] = o;
        }
    }

    // ---------- Helpers ----------
    private int HashArray(int[] arr)
    {
        unchecked { int h = 17; for (int i = 0; i < arr.Length; i++) h = h * 31 + arr[i]; return h; }
    }
    private int HashArray(float[] arr)
    {
        unchecked { int h = 17; for (int i = 0; i < arr.Length; i++) h = h * 31 + Mathf.RoundToInt(arr[i] * 1000f); return h; }
    }
}
