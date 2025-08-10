using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering; // GraphicsFormat, FormatUsage

/// Paints a wake trail (grayscale) into a small RenderTexture in world-space UVs.
[ExecuteAlways]
public class WakePainter : MonoBehaviour
{
    [Header("Mapping (world → UV)")]
    public float uvScale = 0.02f; // 50 m per UV
    public Vector2 uvOffset = Vector2.zero;

    [Header("RenderTexture")]
    public int textureSize = 512;
    [Tooltip("Multiply per second (0..1). 0.9 = fade 10% per second.")]
    public float decayPerSecond = 0.9f;
    public float blurRadius = 1.2f;

    [Header("Splat")]
    public float radiusMeters = 2.5f;
    public float baseIntensity = 0.6f;
    public float speedIntensity = 0.035f; // per m/s
    public float hardness = 1.5f;

    [Header("Optional")]
    public Transform followTarget;
    public bool showInSceneAsGlobal = true;

    RenderTexture rtA, rtB;
    Material matWrite, matDecay;
    Vector2 prevXZ;
    bool hasPrev;

    static readonly int ID_MainTex = Shader.PropertyToID("_MainTex");
    static readonly int ID_Center = Shader.PropertyToID("_Center");
    static readonly int ID_Radius = Shader.PropertyToID("_Radius");
    static readonly int ID_Inten = Shader.PropertyToID("_Intensity");
    static readonly int ID_Hard = Shader.PropertyToID("_Hardness");
    static readonly int ID_Decay = Shader.PropertyToID("_Decay");
    static readonly int ID_Blur = Shader.PropertyToID("_BlurRadius");

    void OnEnable()
    {
        EnsureRTs();
        EnsureMaterials();
        if (!followTarget) followTarget = transform;
        hasPrev = false;
    }

    void OnDisable()
    {
        // avoid “active RT” warning
        if (RenderTexture.active == rtA || RenderTexture.active == rtB)
            RenderTexture.active = null;

        ReleaseRTs();

        if (matWrite) DestroyImmediate(matWrite);
        if (matDecay) DestroyImmediate(matDecay);
        matWrite = matDecay = null;
    }

    void EnsureRTs()
    {
        if (rtA && (rtA.width != textureSize || rtA.height != textureSize))
            ReleaseRTs();

        if (!rtA)
        {
            var fmt = PickSupportedFormat();
            var desc = new RenderTextureDescriptor(textureSize, textureSize)
            {
                graphicsFormat = fmt,         // linear UNorm
                depthBufferBits = 0,
                msaaSamples = 1,
                mipCount = 1,
                sRGB = false,                 // <— force linear; no sRGB for masks
                useMipMap = false,
                autoGenerateMips = false
            };

            rtA = new RenderTexture(desc) { name = "WakeRT_A", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            rtB = new RenderTexture(desc) { name = "WakeRT_B", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            rtA.Create(); rtB.Create();

            Graphics.Blit(Texture2D.blackTexture, rtA);
            Graphics.Blit(Texture2D.blackTexture, rtB);
        }
    }

    static GraphicsFormat PickSupportedFormat()
    {
        // Prefer 8-bit single channel, then 16-bit, then RGBA8 (all linear UNorm)
        if (SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, FormatUsage.Render) &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, FormatUsage.Sample))
            return GraphicsFormat.R8_UNorm;

        if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_UNorm, FormatUsage.Render) &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R16_UNorm, FormatUsage.Sample))
            return GraphicsFormat.R16_UNorm;

        return GraphicsFormat.R8G8B8A8_UNorm; // universal fallback
    }

    void ReleaseRTs()
    {
        if (rtA) { rtA.Release(); DestroyImmediate(rtA); }
        if (rtB) { rtB.Release(); DestroyImmediate(rtB); }
        rtA = rtB = null;
    }

    void EnsureMaterials()
    {
        if (!matWrite) matWrite = new Material(Shader.Find("Hidden/WakeWrite"));
        if (!matDecay) matDecay = new Material(Shader.Find("Hidden/WakeDecay"));
    }

    void LateUpdate()
    {
        if (!rtA || !rtB) EnsureRTs();
        if (!matWrite || !matDecay) EnsureMaterials();
        if (!followTarget) followTarget = transform;

        // --- Position → UV ---
        Vector3 wp = followTarget.position;
        Vector2 uv = new Vector2(wp.x, wp.z) * uvScale + uvOffset;

        // --- Speed magnitude for intensity ---
        float speed = 0f;
        Vector2 xz = new Vector2(wp.x, wp.z);
        if (hasPrev)
            speed = (xz - prevXZ).magnitude / Mathf.Max(Time.deltaTime, 1e-5f);
        prevXZ = xz; hasPrev = true;

        // 1) Decay + blur: rtA -> rtB
        float mul = Mathf.Clamp01(1f - decayPerSecond * Time.deltaTime);
        matDecay.SetFloat(ID_Decay, mul);
        matDecay.SetFloat(ID_Blur, blurRadius);
        Graphics.Blit(rtA, rtB, matDecay, 0);

        // 2) Add splat: rtB + splat -> rtA
        float radiusUV = Mathf.Max(1e-4f, radiusMeters * uvScale);
        float intensity = Mathf.Clamp01(baseIntensity + speedIntensity * speed);

        matWrite.SetVector(ID_Center, new Vector4(uv.x, uv.y, 0, 0));
        matWrite.SetFloat(ID_Radius, radiusUV);
        matWrite.SetFloat(ID_Inten, intensity);
        matWrite.SetFloat(ID_Hard, Mathf.Max(0.8f, hardness));
        matWrite.SetTexture(ID_MainTex, rtB);
        Graphics.Blit(rtB, rtA, matWrite, 0);

        if (showInSceneAsGlobal)
        {
            Shader.SetGlobalTexture("_WakeMap", rtA);
            Shader.SetGlobalVector("_WakeUV", new Vector4(uvScale, uvOffset.x, uvOffset.y, 0));
        }
    }

    public RenderTexture GetWakeRT() => rtA;
}
