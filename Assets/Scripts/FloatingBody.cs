using UnityEngine;

/// <summary>
/// Rigidbody buoyancy using Transform-defined float points you place in the scene.
/// Add empty child objects where you want the waterline contact points and assign them here.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BuoyantRigidbody : MonoBehaviour
{
    [Header("Floaters (assign scene Transforms)")]
    [Tooltip("Place empties under this object (or anywhere) and assign them here.")]
    public Transform[] floatPoints;

    [Header("Buoyancy")]
    [Tooltip("Meters of submergence for full buoyant force at a point.")]
    public float maxSubmergence = 0.5f;
    [Tooltip("Extra height added to sampled water (shifts waterline).")]
    public float waterlineOffset = 0f;
    [Tooltip("1 = total full-submergence lift equals weight. Adjust up/down to taste.")]
    public float buoyancyScale = 1f;

    [Header("Water Drag")]
    [Tooltip("Linear drag applied to point velocity while submerged.")]
    public float waterDrag = 2.0f;
    [Tooltip("Extra angular damping while any point is submerged.")]
    public float angularWaterDrag = 0.2f;

    [Header("Sampling")]
    [Tooltip("Iterations for inverse-mapping sample (3�5 is plenty).")]
    [Range(0, 8)] public int sampleIterations = 4;

    Rigidbody _rb;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (Application.isPlaying)
        {
            _rb.linearDamping = 0.1f;
            _rb.angularDamping = 0.05f;
        }
    }

    void FixedUpdate()
    {
        if (floatPoints == null || floatPoints.Length == 0) return;

        float perPointLift = (_rb.mass * Physics.gravity.magnitude / Mathf.Max(1, floatPoints.Length)) * buoyancyScale;
        bool anySubmerged = false;

        for (int i = 0; i < floatPoints.Length; i++)
        {
            var fp = floatPoints[i];
            if (!fp) continue;

            Vector3 worldPoint = fp.position;

            float h; Vector3 n;
            Ocean.Sample(new Vector2(worldPoint.x, worldPoint.z), out h, out n, t: -1f, iterations: sampleIterations);

            float waterY = h + waterlineOffset;
            float depth = waterY - worldPoint.y; // positive = under water

            if (depth > 0f)
            {
                anySubmerged = true;

                float submergence = Mathf.Clamp01(depth / Mathf.Max(0.0001f, maxSubmergence));
                Vector3 lift = n.normalized * (perPointLift * submergence);
                _rb.AddForceAtPosition(lift, worldPoint, ForceMode.Force);

                Vector3 vPoint = _rb.GetPointVelocity(worldPoint);
                Vector3 drag = -vPoint * waterDrag * submergence;
                _rb.AddForceAtPosition(drag, worldPoint, ForceMode.Force);
            }
        }

        if (anySubmerged)
        {
            _rb.AddTorque(-_rb.angularVelocity * angularWaterDrag, ForceMode.Force);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (floatPoints == null) return;
        Gizmos.color = Color.cyan;
        foreach (var t in floatPoints)
        {
            if (!t) continue;
            Gizmos.DrawWireSphere(t.position, 0.06f);
            Gizmos.DrawLine(t.position, t.position + Vector3.up * 0.25f);
        }
    }
#endif
}
