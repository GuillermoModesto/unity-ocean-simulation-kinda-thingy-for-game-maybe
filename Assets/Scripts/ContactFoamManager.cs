using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class ContactFoamManager : MonoBehaviour
{
    public enum SourceMode { AutoFromMeshConvexHull, ChildTransformsWithTag }

    [Header("Polygon source")]
    [SerializeField] SourceMode source = SourceMode.AutoFromMeshConvexHull;
    [SerializeField] string childTag = "PolyPoint";
    [SerializeField] int hullDecimate = 0;      // keep every Nth hull vertex (0 = none)
    [SerializeField] float polygonScale = 1.0f; // 1=unchanged, <1 shrink, >1 grow

    [Header("Overall ripple amount")]
    [SerializeField] float contactFoamAmount = 1.0f;

    [Header("Ocean hookup")]
    [SerializeField] Renderer targetRenderer;      // assign the Ocean renderer, or auto-find
    [SerializeField] string shaderPolyArrayName = "_Poly";
    [SerializeField] string shaderPolyCountName = "_PolyCount";

    [Header("Polygon ripple params (match shader)")]
    [SerializeField] float rippleSpeed = 1.0f;
    [SerializeField] float rippleWidth = 0.1f;
    [SerializeField] float rippleRepeat = 2.0f;
    [SerializeField] float rippleIntensity = 1.0f;
    [SerializeField] float rippleDecay = 0.6f;     // 1/unit
    [SerializeField] float rippleMaxDist = 8f;     // 0 = off

    const int MAX_POLY = 256;                      // must match shader
    static readonly Vector4[] POLY_BUFFER = new Vector4[MAX_POLY];

    MaterialPropertyBlock _mpb;
    readonly List<Vector2> _polyXZ = new();

    void OnEnable()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        AutoFindOceanRendererIfNeeded();

        // Prime full-size array once so Unity doesn't lock a tiny buffer
        if (targetRenderer)
        {
            targetRenderer.GetPropertyBlock(_mpb);
            _mpb.Clear();
            _mpb.SetVectorArray(shaderPolyArrayName, POLY_BUFFER); // full length, zeros
            _mpb.SetInt(shaderPolyCountName, 0);
            targetRenderer.SetPropertyBlock(_mpb);
        }

        Push();
    }

    void OnDisable()
    {
        if (targetRenderer)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(_mpb);
            _mpb.Clear(); // clears _Poly etc.
            targetRenderer.SetPropertyBlock(_mpb);
        }
    }

    void Update()
    {
        AutoFindOceanRendererIfNeeded();

        // Build polygon (world XZ)
        BuildPolygonXZ(_polyXZ);
        EnsureFallbackPolygon(_polyXZ); // guarantee >= 3 points

        if (polygonScale != 1f && _polyXZ.Count >= 3)
        {
            Vector2 c = Vector2.zero; foreach (var p in _polyXZ) c += p; c /= _polyXZ.Count;
            for (int i = 0; i < _polyXZ.Count; i++) _polyXZ[i] = c + (_polyXZ[i] - c) * polygonScale;
        }

        Push();
    }

    void AutoFindOceanRendererIfNeeded()
    {
        if (targetRenderer && targetRenderer.sharedMaterial) return;

        var rends = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var r in rends)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (!m) continue;
                if (m.shader && m.shader.name == "Ocean/GerstnerURP_Simplified_Green")
                { targetRenderer = r; return; }
            }
        }
        if (!targetRenderer) targetRenderer = GetComponent<Renderer>();
    }

    void Push()
    {
        if (!targetRenderer) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        int n = Mathf.Clamp(_polyXZ.Count, 0, MAX_POLY);

        // Fill fixed-size buffer (first n = points, rest zeros)
        for (int i = 0; i < n; i++) POLY_BUFFER[i] = new Vector4(_polyXZ[i].x, _polyXZ[i].y, 0f, 0f);
        for (int i = n; i < MAX_POLY; i++) POLY_BUFFER[i] = Vector4.zero;

        targetRenderer.GetPropertyBlock(_mpb);
        _mpb.Clear();

        _mpb.SetVectorArray(shaderPolyArrayName, POLY_BUFFER); // ALWAYS same length
        _mpb.SetInt(shaderPolyCountName, n);

        _mpb.SetFloat("_PolyTime", Application.isPlaying ? Time.time : 0f);
        _mpb.SetFloat("_PolyRippleSpeed", rippleSpeed);
        _mpb.SetFloat("_PolyRippleWidth", rippleWidth);
        _mpb.SetFloat("_PolyRippleRepeat", rippleRepeat);
        _mpb.SetFloat("_PolyRippleIntensity", rippleIntensity);
        _mpb.SetFloat("_PolyRippleDecay", rippleDecay);
        _mpb.SetFloat("_PolyRippleMaxDist", rippleMaxDist);
        _mpb.SetFloat("_ContactFoamAmount", contactFoamAmount);

        targetRenderer.SetPropertyBlock(_mpb);
    }

    // ---------- Polygon builders ----------

    void BuildPolygonXZ(List<Vector2> outList)
    {
        outList.Clear();
        if (source == SourceMode.ChildTransformsWithTag) BuildFromTaggedChildren(outList);
        else BuildFromMeshConvexHull(outList);
    }

    void BuildFromTaggedChildren(List<Vector2> result)
    {
        var tfs = GetComponentsInChildren<Transform>(true);
        foreach (var t in tfs)
        {
            if (t == transform) continue;
            if (!string.IsNullOrEmpty(childTag) && !t.CompareTag(childTag)) continue;
            Vector3 w = t.position;
            result.Add(new Vector2(w.x, w.z));
        }
        OrderPointsCCW(result);
    }

    void BuildFromMeshConvexHull(List<Vector2> result)
    {
        var mf = GetComponent<MeshFilter>();
        Mesh mesh = mf ? (Application.isPlaying ? mf.mesh : mf.sharedMesh) : null;

        // Fallback to renderer bounds if no mesh
        if (!mesh || mesh.vertexCount == 0)
        {
            var rend = GetComponentInChildren<Renderer>();
            if (rend)
            {
                Bounds b = rend.bounds;
                result.Add(new Vector2(b.min.x, b.min.z));
                result.Add(new Vector2(b.max.x, b.min.z));
                result.Add(new Vector2(b.max.x, b.max.z));
                result.Add(new Vector2(b.min.x, b.max.z));
            }
            return;
        }

        var verts = mesh.vertices;
        var trs = transform.localToWorldMatrix;
        var pts = new List<Vector2>(verts.Length);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 w = trs.MultiplyPoint3x4(verts[i]);
            pts.Add(new Vector2(w.x, w.z));
        }
        var hull = ConvexHull(pts);
        if (hullDecimate > 1 && hull.Count > hullDecimate)
        {
            var dec = new List<Vector2>();
            for (int i = 0; i < hull.Count; i += hullDecimate) dec.Add(hull[i]);
            hull = dec;
        }
        result.AddRange(hull);
    }

    void EnsureFallbackPolygon(List<Vector2> poly)
    {
        if (poly.Count >= 3) return;
        poly.Clear();
        Vector2 c = new Vector2(transform.position.x, transform.position.z);
        float r = 1.0f;
        const int SEG = 12;
        for (int i = 0; i < SEG; i++)
        {
            float ang = i * (Mathf.PI * 2f / SEG);
            poly.Add(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
        }
    }

    static void OrderPointsCCW(List<Vector2> pts)
    {
        if (pts.Count < 3) return;
        Vector2 c = Vector2.zero; foreach (var p in pts) c += p; c /= pts.Count;
        pts.Sort((a, b) =>
        {
            float angA = Mathf.Atan2(a.y - c.y, a.x - c.x);
            float angB = Mathf.Atan2(b.y - c.y, b.x - c.x);
            return angA.CompareTo(angB);
        });
    }

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

    static float Cross(Vector2 a, Vector2 b, Vector2 c)
        => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
}
