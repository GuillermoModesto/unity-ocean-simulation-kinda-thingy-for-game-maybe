using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class ContactFoamManager : MonoBehaviour
{
    public enum SourceMode { AutoFromMeshConvexHull, ChildTransformsWithTag }
    [Header("How to get polygon points (auto)")]
    [SerializeField] SourceMode source = SourceMode.AutoFromMeshConvexHull;
    [SerializeField] string childTag = "PolyPoint";
    [SerializeField] int hullDecimate = 0; // 0=no decimation; >0 keeps every Nth point on hull

    [Header("Ocean shader hookup (auto)")]
    [SerializeField] Renderer targetRenderer;                 // auto-found if left empty
    [SerializeField] string shaderPolyArrayName = "_Poly";    // keep defaults per shader
    [SerializeField] string shaderPolyCountName = "_PolyCount";

    [Header("Polygon ripple params (match shader)")]
    [SerializeField] float rippleSpeed = 1.0f;
    [SerializeField] float rippleWidth = 0.1f;   // 0..1 band thickness
    [SerializeField] float rippleRepeat = 2.0f;  // wavelength (units)
    [SerializeField] float intensity = 1.0f;

    [SerializeField] float rippleDecay = 0.6f;   // per distance unit (higher = faster fade)
    [SerializeField] float rippleMaxDist = 8f;  // 0 = infinite (decay only)

    // Advanced: shrink/expand the polygon relative to its centroid (1=unchanged, <1 shrink, >1 grow)
    [SerializeField] float polygonScale = 1.0f;

    // Cache
    MaterialPropertyBlock _mpb;

    void OnEnable()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        AutoFindOceanRendererIfNeeded();
        Push();
    }

    void OnDisable()
    {
        // optional: clear MPB
        if (targetRenderer)
        {
            targetRenderer.GetPropertyBlock(_mpb);
            _mpb.SetInt(shaderPolyCountName, 0);
            targetRenderer.SetPropertyBlock(_mpb);
        }
    }

    void Update()
    {
        // Keep it “live” in Edit/Play
        Push();
    }

    void AutoFindOceanRendererIfNeeded()
    {
        if (targetRenderer && targetRenderer.sharedMaterial) return;

        // Try: any renderer in scene with a material that uses the ocean shader
        var rends = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var r in rends)
        {
            var mats = r.sharedMaterials;
            foreach (var m in mats)
            {
                if (!m) continue;
                var sh = m.shader;
                if (sh && sh.name == "Ocean/GerstnerURP_Simplified_Green")
                {
                    targetRenderer = r;
                    return;
                }
            }
        }
        // Fallback: try self
        if (!targetRenderer) targetRenderer = GetComponent<Renderer>();
    }

    void Push()
    {
        if (!targetRenderer) AutoFindOceanRendererIfNeeded();
        if (!targetRenderer) return;

        // Build polygon in WORLD XZ
        List<Vector2> polyXZ = BuildPolygonXZ();

        // Optional scale around centroid
        if (polygonScale != 1f && polyXZ.Count >= 3)
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < polyXZ.Count; i++) c += polyXZ[i];
            c /= polyXZ.Count;
            for (int i = 0; i < polyXZ.Count; i++)
                polyXZ[i] = c + (polyXZ[i] - c) * polygonScale;
        }

        // Upload to MPB (Vector4[] required)
        int n = Mathf.Clamp(polyXZ.Count, 0, 256);
        var arr = new Vector4[Mathf.Max(n, 1)];
        for (int i = 0; i < n; i++)
            arr[i] = new Vector4(polyXZ[i].x, polyXZ[i].y, 0f, 0f);
        if (n == 0) arr[0] = Vector4.zero;

        targetRenderer.GetPropertyBlock(_mpb);
        _mpb.SetVectorArray(shaderPolyArrayName, arr);
        _mpb.SetInt(shaderPolyCountName, n);

        // Time + ripple params (names match the shader you pasted)
        _mpb.SetFloat("_PolyTime", Application.isPlaying ? Time.time : 0f);
        _mpb.SetFloat("_PolyRippleSpeed", rippleSpeed);
        _mpb.SetFloat("_PolyRippleWidth", rippleWidth);
        _mpb.SetFloat("_PolyRippleRepeat", rippleRepeat);
        _mpb.SetFloat("_PolyRippleIntensity", intensity);
        _mpb.SetFloat("_PolyRippleDecay", rippleDecay);
        _mpb.SetFloat("_PolyRippleMaxDist", rippleMaxDist);

        targetRenderer.SetPropertyBlock(_mpb);
    }

    // ------------------------------
    // POLYGON BUILDERS (WORLD XZ)
    // ------------------------------

    List<Vector2> BuildPolygonXZ()
    {
        if (source == SourceMode.ChildTransformsWithTag)
            return BuildFromTaggedChildren();

        // Default: convex hull from this object's mesh in world XZ
        return BuildFromMeshConvexHull();
    }

    List<Vector2> BuildFromTaggedChildren()
    {
        var result = new List<Vector2>(16);
        var tfs = GetComponentsInChildren<Transform>(true);
        foreach (var t in tfs)
        {
            if (t == transform) continue;
            if (!string.IsNullOrEmpty(childTag) && !t.CompareTag(childTag)) continue;
            Vector3 w = t.position;
            result.Add(new Vector2(w.x, w.z));
        }
        // Order them around centroid so the shader gets a clean loop
        OrderPointsCCW(result);
        return result;
    }

    List<Vector2> BuildFromMeshConvexHull()
    {
        var result = new List<Vector2>(32);
        var mf = GetComponent<MeshFilter>();
        Mesh mesh = mf ? (Application.isPlaying ? mf.mesh : mf.sharedMesh) : null;
        if (!mesh || mesh.vertexCount == 0) return result;

        var verts = mesh.vertices;
        var trs = transform.localToWorldMatrix;

        // Collect projected XZ points in world
        var pts = new List<Vector2>(verts.Length);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 w = trs.MultiplyPoint3x4(verts[i]);
            pts.Add(new Vector2(w.x, w.z));
        }

        // Convex hull (Graham scan)
        var hull = ConvexHull(pts);
        if (hullDecimate > 1 && hull.Count > hullDecimate)
        {
            var dec = new List<Vector2>();
            for (int i = 0; i < hull.Count; i += hullDecimate)
                dec.Add(hull[i]);
            // ensure closed loop feel by adding last if not same as first
            if (dec.Count >= 2 && dec[0] != dec[^1]) { /* shader closes loop anyway */ }
            hull = dec;
        }
        return hull;
    }

    // Counter-clockwise sort around centroid (for child-tag mode)
    static void OrderPointsCCW(List<Vector2> pts)
    {
        if (pts.Count < 3) return;
        Vector2 c = Vector2.zero;
        foreach (var p in pts) c += p;
        c /= pts.Count;
        pts.Sort((a, b) =>
        {
            float angA = Mathf.Atan2(a.y - c.y, a.x - c.x);
            float angB = Mathf.Atan2(b.y - c.y, b.x - c.x);
            return angA.CompareTo(angB);
        });
    }

    // Simple Convex Hull (Graham / monotone chain)
    static List<Vector2> ConvexHull(List<Vector2> pts)
    {
        var P = new List<Vector2>(pts);
        P.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        if (P.Count <= 1) return P;

        List<Vector2> lower = new();
        foreach (var p in P)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        List<Vector2> upper = new();
        for (int i = P.Count - 1; i >= 0; i--)
        {
            var p = P[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
}
