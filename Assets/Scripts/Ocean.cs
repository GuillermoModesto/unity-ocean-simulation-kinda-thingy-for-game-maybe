// Ocean.cs
using System;
using Unity.Mathematics;
using UnityEngine;
#if UNITY_MATHEMATICS
using Unity.Mathematics;
#endif

[ExecuteAlways, RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class Ocean : MonoBehaviour
{
    public OceanWaveSettings settings;
    public Material oceanMaterial;
    public float gravity = 9.81f;

    // Cache for CPU sampling
    private struct WaveCPU
    {
        public float A, k, w, S; // amplitude, wavenumber, angular frequency, steepness
        public Vector2 D;        // unit direction
    }
    private WaveCPU[] _cpuWaves;
    private static Ocean _instance;

    void OnEnable()
    {
        _instance = this;
        ApplySettingsToShader();
        BuildCpuWaves();
        EnsureMesh();
        UpdateMaterialProps(0);
    }

    void OnValidate()
    {
        ApplySettingsToShader();
        BuildCpuWaves();
    }

    void Update()
    {
        if (!oceanMaterial || settings == null) return;
        UpdateMaterialProps(Time.time);
    }

    void ApplySettingsToShader()
    {
        if (!oceanMaterial || settings == null) return;

        int count = Mathf.Min(settings.waves?.Length ?? 0, 32); // cap to 32
        Vector4[] dir_amp_steep = new Vector4[count];
        Vector4[] wl_omega_pad = new Vector4[count];

        for (int i = 0; i < count; i++)
        {
            var w = settings.waves[i];
            float theta = w.directionDegrees * Mathf.Deg2Rad;
            Vector2 D = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)).normalized;

            float k = 2f * Mathf.PI / Mathf.Max(0.001f, w.wavelength);
            // deep water dispersion: c = sqrt(g/k),  omega = k * c = sqrt(g*k)
            float omega = w.speed > 0 ? k * w.speed : Mathf.Sqrt(gravity * k);

            dir_amp_steep[i] = new Vector4(D.x, D.y, w.amplitude, w.steepness * settings.choppiness);
            wl_omega_pad[i] = new Vector4(w.wavelength, omega, 0, 0);
        }

        oceanMaterial.SetInt("_WaveCount", count);
        oceanMaterial.SetVectorArray("_DirAmpSteep", dir_amp_steep);
        oceanMaterial.SetVectorArray("_WlOmegaPad", wl_omega_pad);
        oceanMaterial.SetVector("_UVOffset", settings.uvOffset);
        oceanMaterial.SetFloat("_Choppiness", settings.choppiness);
    }

    void BuildCpuWaves()
    {
        if (settings == null || settings.waves == null) { _cpuWaves = Array.Empty<WaveCPU>(); return; }

        int count = Mathf.Min(settings.waves.Length, 32);
        _cpuWaves = new WaveCPU[count];
        for (int i = 0; i < count; i++)
        {
            var w = settings.waves[i];
            float theta = w.directionDegrees * Mathf.Deg2Rad;
            Vector2 D = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)).normalized;

            float k = 2f * Mathf.PI / Mathf.Max(0.001f, w.wavelength);
            float omega = w.speed > 0 ? k * w.speed : Mathf.Sqrt(gravity * k);

            _cpuWaves[i] = new WaveCPU
            {
                A = w.amplitude,
                k = k,
                w = omega,
                S = w.steepness * settings.choppiness,
                D = D
            };
        }
    }

    void UpdateMaterialProps(float t)
    {
        oceanMaterial.SetFloat("_TimeSeconds", t);
        oceanMaterial.SetVector("_OceanOrigin", transform.position);
    }

    void EnsureMesh()
    {
        // Simple, cheap: a camera-centered, re-usable grid in world-space.
        // You can replace with a geo-clipmap/FFT later.
        var mf = GetComponent<MeshFilter>();
        if (mf.sharedMesh != null) return;
        mf.sharedMesh = GenerateGrid(256, 256, 2f);
    }

    Mesh GenerateGrid(int xVerts, int zVerts, float spacing)
    {
        var mesh = new Mesh { name = "OceanGrid_CW" };

        int vCount = xVerts * zVerts;
        var verts = new Vector3[vCount];
        var uv = new Vector2[vCount];
        var idx = new int[(xVerts - 1) * (zVerts - 1) * 6];

        // vertices
        for (int z = 0; z < zVerts; z++)
        {
            for (int x = 0; x < xVerts; x++)
            {
                int i = z * xVerts + x;
                verts[i] = new Vector3((x - xVerts * 0.5f) * spacing, 0, (z - zVerts * 0.5f) * spacing);
                uv[i] = new Vector2((float)x / (xVerts - 1), (float)z / (zVerts - 1));
            }
        }

        // triangles — CW when viewed from +Y
        int t = 0;
        for (int z = 0; z < zVerts - 1; z++)
        {
            for (int x = 0; x < xVerts - 1; x++)
            {
                int i = z * xVerts + x;

                // tri 1: i, i + xVerts, i + 1
                idx[t++] = i;
                idx[t++] = i + xVerts;
                idx[t++] = i + 1;

                // tri 2: i + 1, i + xVerts, i + xVerts + 1
                idx[t++] = i + 1;
                idx[t++] = i + xVerts;
                idx[t++] = i + xVerts + 1;
            }
        }

        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.triangles = idx;

        mesh.RecalculateBounds();
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f); // huge bounds so it never culls

        return mesh;
    }

    // ======= PUBLIC CPU SAMPLING API =======
    /// <summary> Sample ocean displacement at world XZ and time t (defaults to Time.time). </summary>
    public static void Sample(Vector2 worldXZ, out float height, out Vector3 normal, float t = -1f)
    {
        if (_instance == null || _instance._cpuWaves == null) { height = 0f; normal = Vector3.up; return; }
        if (t < 0f) t = Time.time;

        Vector2 xz = worldXZ - (Vector2)_instance.transform.position.xz();
        Vector3 disp = Vector3.zero;
        Vector3 dPdX = new Vector3(1, 0, 0);
        Vector3 dPdZ = new Vector3(0, 0, 1);

        foreach (var w in _instance._cpuWaves)
        {
            float k = w.k;
            float2 Dxz = new float2(w.D.x, w.D.y);
            float dot = Dxz.x * xz.x + Dxz.y * xz.y;
            float phase = k * dot - w.w * t;
            float cosP = Mathf.Cos(phase);
            float sinP = Mathf.Sin(phase);

            float QA = w.S * w.A;

            // Displacement
            disp.x += QA * w.D.x * cosP;       // horizontal (chop)
            disp.y += w.A * sinP;              // vertical
            disp.z += QA * w.D.y * cosP;

            // Partials for normal
            float dx_phase = k * w.D.x;
            float dz_phase = k * w.D.y;

            // dX/dx = 1 - QA * Dx * Dx * sin(phase) * k
            dPdX.x += -QA * w.D.x * dx_phase * sinP;
            dPdX.y += w.A * dx_phase * cosP;
            dPdX.z += -QA * w.D.y * dx_phase * sinP;

            dPdZ.x += -QA * w.D.x * dz_phase * sinP;
            dPdZ.y += w.A * dz_phase * cosP;
            dPdZ.z += -QA * w.D.y * dz_phase * sinP;
        }

        Vector3 n = Vector3.Normalize(Vector3.Cross(dPdZ, dPdX));
        height = disp.y;
        normal = n;
    }
}

static class VecExt
{
    public static Vector2 xz(this Vector3 v) => new Vector2(v.x, v.z);
}
