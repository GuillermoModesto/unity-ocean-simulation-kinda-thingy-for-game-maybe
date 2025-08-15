using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BuoyantRigidbody : MonoBehaviour
{
    [Header("Float Settings")]
    public float AirDrag = 1f;
    public float WaterDrag = 10f;
    public float gravityMaxForce = 1f;
    public bool AffectDirection = true;
    public bool AttachToSurface = false;
    public Transform[] FloatPoints;

    // Internal state
    private Rigidbody _rb;
    private Vector3[] _waterLinePoints;
    private Vector3 _centerOffset;
    private Vector3 _smoothVectorRotation;
    private Vector3 _targetUp;

    public float WaterLine { get; private set; }
    public Vector3 Center => transform.position + _centerOffset;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false; // we handle gravity ourselves

        _waterLinePoints = new Vector3[FloatPoints.Length];

        // Precompute center offset
        Vector3 avg = Vector3.zero;
        for (int i = 0; i < FloatPoints.Length; i++)
            avg += FloatPoints[i].position;
        avg /= Mathf.Max(1, FloatPoints.Length);

        _centerOffset = avg - transform.position;
    }

    void FixedUpdate()
    {
        if (FloatPoints == null || FloatPoints.Length == 0) return;

        float newWaterLine = 0f;
        bool pointUnderWater = false;

        // Collect wave-sampled water line points
        for (int i = 0; i < FloatPoints.Length; i++)
        {
            Vector3 fp = FloatPoints[i].position;
            float h; Vector3 n;
            Ocean.Sample(new Vector2(fp.x, fp.z), out h, out n);

            _waterLinePoints[i] = new Vector3(fp.x, h, fp.z);
            newWaterLine += h / FloatPoints.Length;

            if (h > fp.y) pointUnderWater = true;
        }

        WaterLine = newWaterLine;

        // Compute average up vector
        _targetUp = GetNormal(_waterLinePoints);

        // Apply buoyancy
        Vector3 gravity = Physics.gravity;
        _rb.linearDamping = AirDrag;

        if (WaterLine > Center.y)
        {
            _rb.linearDamping = WaterDrag;

            if (AttachToSurface)
            {
                // snap to surface
                _rb.position = new Vector3(_rb.position.x, WaterLine - _centerOffset.y, _rb.position.z);
            }
            else
            {
                // push up toward surface
                gravity = AffectDirection ? _targetUp * -Physics.gravity.y : -Physics.gravity;
                transform.Translate(Vector3.up * (WaterLine - Center.y) * 0.9f);
            }
        }

        _rb.AddForce(gravity * Mathf.Clamp(Mathf.Abs(WaterLine - Center.y), 0f, gravityMaxForce), ForceMode.Acceleration);

        // Rotate boat to align with water normal
        if (pointUnderWater)
        {
            _targetUp = Vector3.SmoothDamp(transform.up, _targetUp, ref _smoothVectorRotation, 0.2f);
            _rb.rotation = Quaternion.FromToRotation(transform.up, _targetUp) * _rb.rotation;
        }
    }

    // Utility to get averaged surface normal
    private Vector3 GetNormal(Vector3[] points)
    {
        if (points.Length < 3) return Vector3.up;

        Vector3 normal = Vector3.zero;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 current = points[i];
            Vector3 next = points[(i + 1) % points.Length];
            normal.x += (current.y - next.y) * (current.z + next.z);
            normal.y += (current.z - next.z) * (current.x + next.x);
            normal.z += (current.x - next.x) * (current.y + next.y);
        }
        return normal.normalized;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (FloatPoints == null) return;

        Gizmos.color = Color.green;
        foreach (var fp in FloatPoints)
        {
            if (!fp) continue;
            Gizmos.DrawSphere(fp.position, 0.1f);
        }

        if (Application.isPlaying)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawCube(new Vector3(Center.x, WaterLine, Center.z), Vector3.one * 0.3f);
            Gizmos.DrawRay(new Vector3(Center.x, WaterLine, Center.z), _targetUp);
        }
    }
#endif
}
