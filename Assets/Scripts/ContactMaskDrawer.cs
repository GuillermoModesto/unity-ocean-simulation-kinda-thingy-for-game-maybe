using UnityEngine;

[RequireComponent(typeof(BuoyantRigidbody))]
public class ContactMaskDrawer : MonoBehaviour
{
    [Tooltip("Radius in mask UV space (0..0.5). 0.02 ≈ small circle.")]
    public float radiusUV = 0.02f;

    Material _mat;
    Mesh _mesh;

    void Awake()
    {
        _mat = new Material(Shader.Find("Hidden/DrawContactPolygon")); // writes white, Blend One One
        _mesh = new Mesh { name = "ContactMaskQuad" };
        _mesh.MarkDynamic();
    }

    void LateUpdate()
    {
        var mgr = ContactMaskManager.Instance;
        if (!mgr || !mgr.MaskRT) return;

        // Center of the boat in mask UVs
        Vector2 uv = mgr.WorldToUV(transform.position);
        float r = Mathf.Clamp(radiusUV, 0.005f, 0.08f);

        Vector2 a = uv + new Vector2(-r, -r);
        Vector2 b = uv + new Vector2(r, -r);
        Vector2 c = uv + new Vector2(r, r);
        Vector2 d = uv + new Vector2(-r, r);

        // keep inside the RT (avoid wrapping/bleed)
        Vector2 clampMin = Vector2.one * 0.0005f;
        Vector2 clampMax = Vector2.one * 0.9995f;
        a = Vector2.Min(clampMax, Vector2.Max(clampMin, a));
        b = Vector2.Min(clampMax, Vector2.Max(clampMin, b));
        c = Vector2.Min(clampMax, Vector2.Max(clampMin, c));
        d = Vector2.Min(clampMax, Vector2.Max(clampMin, d));

        // Build quad (positions unused; shader uses UV as position in LoadOrtho())
        _mesh.Clear(false);
        _mesh.vertices = new Vector3[4] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
        _mesh.uv = new Vector2[4] { a, b, c, d };
        _mesh.triangles = new int[6] { 0, 1, 2, 0, 2, 3 };

        // Draw into the mask RT
        var prev = RenderTexture.active;
        RenderTexture.active = mgr.MaskRT;
        GL.PushMatrix();
        GL.LoadOrtho();
        _mat.SetPass(0);
        Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
        GL.PopMatrix();
        RenderTexture.active = prev;
    }
}
