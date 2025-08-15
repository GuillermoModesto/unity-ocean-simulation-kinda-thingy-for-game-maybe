using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(BuoyantRigidbody))]
public class ContactPolygonGizmos : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("If a probe is at or below (waterHeight + threshold) it's considered touching.")]
    public float contactThreshold = 0.03f;
    public bool projectVerticesToSurface = true;

    [Header("Gizmos")]
    public Color contactPointColor = new Color(1f, 0.5f, 0f, 1f);
    public Color polygonEdgeColor = Color.white;
    public Color polygonFillColor = new Color(1f, 1f, 1f, 0.08f);
    public float pointSize = 0.06f;
    public bool drawAlways = false; // if false, draws only when the object is selected

    // ——— public helper if you want to reuse this later ———
    public bool GetCurrentContactPolygon(List<Vector3> outPolygonWorld)
    {
        outPolygonWorld.Clear();
        var br = GetComponent<BuoyantRigidbody>();
        if (!br || br.FloatPoints == null || br.FloatPoints.Length == 0) return false;

        // 1) collect touching points
        tmpPoints.Clear();
        foreach (var t in br.FloatPoints)
        {
            if (!t) continue;
            var p = t.position;
            float h; Vector3 n;
            Ocean.Sample(new Vector2(p.x, p.z), out h, out n);
            if (p.y <= h + contactThreshold)
                tmpPoints.Add(projectVerticesToSurface ? new Vector3(p.x, h, p.z) : p);
        }
        if (tmpPoints.Count < 3) return false;

        // 2) order by angle around centroid (good enough for hulls / convex-ish loops)
        Vector3 c = Vector3.zero;
        for (int i = 0; i < tmpPoints.Count; i++) c += tmpPoints[i];
        c /= tmpPoints.Count;

        tmpPoints.Sort((a, b) =>
        {
            float A = Mathf.Atan2(a.z - c.z, a.x - c.x);
            float B = Mathf.Atan2(b.z - c.z, b.x - c.x);
            return A.CompareTo(B);
        });

        outPolygonWorld.AddRange(tmpPoints);
        return true;
    }

    // ——— Gizmos drawing ———
    void OnDrawGizmos()
    {
        if (!drawAlways) return;
        DrawGizmosInternal();
    }

    void OnDrawGizmosSelected()
    {
        if (drawAlways) return;
        DrawGizmosInternal();
    }

    // temp lists to avoid allocs
    static readonly List<Vector3> tmpPoints = new List<Vector3>(64);
    static readonly List<Vector3> polygon = new List<Vector3>(64);

    void DrawGizmosInternal()
    {
        if (!GetCurrentContactPolygon(polygon))
            return;

        // contact points (small spheres)
        Gizmos.color = contactPointColor;
        foreach (var v in polygon)
            Gizmos.DrawSphere(v, pointSize);

        // polygon edges
        Gizmos.color = polygonEdgeColor;
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            Gizmos.DrawLine(a, b);
        }

        // optional translucent fill (triangle fan)
        Gizmos.color = polygonFillColor;
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            DrawTriangleGizmo(polygon[0], polygon[i], polygon[i + 1]);
        }
    }

    // Simple filled triangle using Gizmos (drawn as 3 quads along edges for visibility)
    void DrawTriangleGizmo(Vector3 a, Vector3 b, Vector3 c)
    {
        // approximate by drawing three little quads; good enough for preview
        DrawFatLine(a, b);
        DrawFatLine(b, c);
        DrawFatLine(c, a);
    }

    void DrawFatLine(Vector3 a, Vector3 b)
    {
        var cam = Camera.current;
        if (!cam) { Gizmos.DrawLine(a, b); return; }
        var dir = (b - a);
        var n = cam.transform.rotation * Vector3.forward;
        var side = Vector3.Cross(n, dir).normalized * 0.02f; // thickness
        Gizmos.DrawLine(a - side, b - side);
        Gizmos.DrawLine(a + side, b + side);
    }
}
