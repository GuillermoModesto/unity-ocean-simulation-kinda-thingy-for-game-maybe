using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

[ExecuteAlways]
[DefaultExecutionOrder(-1000)]
public class ContactMaskManager : MonoBehaviour
{
    public static ContactMaskManager Instance;

    [Header("Mask")]
    public int resolution = 512;

    [Header("Fade")]
    [Range(0f, 1f)] public float persistence = 0.92f;

    [Header("Mapping (make both sides match)")]
    public bool useOceanTransformAsOrigin = true;  // usually ON
    public Vector2 manualOriginXZ = Vector2.zero;  // used only if above is OFF
    public bool invertX = false;
    public bool invertZ = false;
    public bool swapXZ = false;

    public RenderTexture MaskRT { get; private set; }

    Ocean _ocean;
    Material _fadeMat;
    RenderTexture _temp;

    void OnEnable()
    {
        Instance = this;
        _ocean = FindObjectOfType<Ocean>();
        if (_fadeMat == null) _fadeMat = new Material(Shader.Find("Hidden/ClearContactMask"));
        CreateRT();
        ClearMaskNow();
        PushGlobals();
    }

    void OnDisable()
    {
        if (_temp != null) { RenderTexture.ReleaseTemporary(_temp); _temp = null; }
        if (MaskRT != null) { MaskRT.Release(); MaskRT = null; }
    }

    void CreateRT()
    {
        if (MaskRT != null) MaskRT.Release();

        var gf = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, FormatUsage.Render)
                 ? GraphicsFormat.R8_UNorm : GraphicsFormat.R8G8B8A8_UNorm;

        var desc = new RenderTextureDescriptor(resolution, resolution)
        {
            graphicsFormat = gf,
            depthBufferBits = 0,
            sRGB = false,
            msaaSamples = 1
        };
        MaskRT = new RenderTexture(desc)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        MaskRT.Create();
    }

    public void ClearMaskNow()
    {
        var prev = RenderTexture.active;
        RenderTexture.active = MaskRT;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prev;
    }

    void Update()
    {
        // Safe fade (never blit in-place)
        if (MaskRT && persistence < 0.999f)
        {
            if (_temp == null || !_temp.IsCreated() ||
                _temp.width != MaskRT.width || _temp.height != MaskRT.height)
            {
                if (_temp != null) RenderTexture.ReleaseTemporary(_temp);
                _temp = RenderTexture.GetTemporary(MaskRT.descriptor);
            }
            _fadeMat.SetFloat("_Persistence", persistence);
            Graphics.Blit(MaskRT, _temp, _fadeMat);
            Graphics.Blit(_temp, MaskRT);
        }

        PushGlobals();
    }

    void PushGlobals()
    {
        if (_ocean == null) _ocean = FindObjectOfType<Ocean>();

        // Size = inner patch size from Ocean
        float size = _ocean ? _ocean.innerSize : 200f;

        // Origin & basis from Ocean transform (handles rotation/scale)
        Vector3 origin = _ocean ? _ocean.transform.position : Vector3.zero;
        Vector3 basisX = _ocean ? _ocean.transform.right : Vector3.right;   // world-space unit X along ocean
        Vector3 basisZ = _ocean ? _ocean.transform.forward : Vector3.forward; // world-space unit Z along ocean
        basisX.Normalize();
        basisZ.Normalize();

        // Publish to the shader
        Shader.SetGlobalTexture("_ContactMask", MaskRT);
        Shader.SetGlobalFloat("_ContactWorldSize", size);
        Shader.SetGlobalVector("_ContactOrigin", new Vector4(origin.x, origin.y, origin.z, 0));
        Shader.SetGlobalVector("_ContactBasisX", new Vector4(basisX.x, basisX.y, basisX.z, 0));
        Shader.SetGlobalVector("_ContactBasisZ", new Vector4(basisZ.x, basisZ.y, basisZ.z, 0));
    }

    // Map world → UV using the SAME origin & basis we push to the shader
    public Vector2 WorldToUV(Vector3 wp)
    {
        if (_ocean == null) _ocean = FindFirstObjectByType<Ocean>();
        Vector3 origin = _ocean ? _ocean.transform.position : Vector3.zero;
        Vector3 bx = _ocean ? _ocean.transform.right : Vector3.right;   // unit
        Vector3 bz = _ocean ? _ocean.transform.forward : Vector3.forward; // unit
        float size = _ocean ? _ocean.innerSize : 200f;

        Vector3 d = wp - origin;
        // Project world delta onto ocean local X/Z directions
        float ux = Vector3.Dot(d, bx);
        float uz = Vector3.Dot(d, bz);

        return new Vector2(ux / Mathf.Max(size, 1e-3f) + 0.5f,
                           uz / Mathf.Max(size, 1e-3f) + 0.5f);
    }

}

