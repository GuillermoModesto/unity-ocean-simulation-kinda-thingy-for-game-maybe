using UnityEngine;

/// <summary>
/// Floating physics that can detach from crests, fly, and re-enter water.
/// Stabilized for full submersion to avoid jitter/spin.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class WaterFloat : MonoBehaviour
{
    [Header("Drag")]
    [Tooltip("Linear drag in air (0.05–0.3).")]
    public float airDrag = 0.2f;
    [Tooltip("Linear drag in water (2–6).")]
    public float waterDrag = 3.5f;
    [Tooltip("Angular drag in water (1–4).")]
    public float waterAngularDrag = 2.2f;

    [Header("Buoyancy")]
    [Tooltip("Overall buoyancy multiplier. 1.0 ≈ neutral (depends on mass/scale).")]
    public float buoyancy = 1.6f;
    [Tooltip("Depth (m) where a point reaches full buoyancy; tames suction near surface.")]
    public float maxBuoyancyDepth = 0.5f;

    [Header("Torque Stabilization")]
    [Tooltip("When fully submerged, blend buoyancy force application towards COM to reduce torque.")]
    [Range(0f, 1f)] public float deepSubmergeTorqueBlend = 0.2f; // 0=full lever arm, 1=at COM
    [Tooltip("Consider 'fully submerged' if every point is deeper than this (m).")]
    public float fullSubmergedDepth = 0.15f;
    [Tooltip("Limit angular velocity while in water (rad/s). 0 = no limit.")]
    public float maxAngularVelInWater = 2.5f;

    [Header("Orientation")]
    [Tooltip("Rotate to match water normal only when partly submerged; use upright PD when fully submerged.")]
    public bool affectDirection = true;
    [Tooltip("PD torque strength aligning up vector (in water).")]
    public float uprightTorque = 8f;
    [Tooltip("PD torque damping (0–1); higher = more damping.")]
    [Range(0f, 1f)] public float uprightDamping = 0.35f;
    [Tooltip("How far to sample around center for normal (m).")]
    public float normalSampleRadius = 0.75f;

    [Header("Attachment")]
    [Tooltip("If true, snap to surface height (not realistic for detaching).")]
    public bool attachToSurface = false;

    [Header("Float Points")]
    [Tooltip("Sample points used to compute buoyancy and orientation.")]
    public Transform[] floatPoints;

    // refs/state
    Rigidbody rb;
    Waves waves;

    Vector3[] waterLinePoints;
    Vector3 centerOffset;                 // local-space centroid of float points
    Vector3 targetUp = Vector3.up;
    Vector3 smoothVectorRotation;         // SmoothDamp helper
    float waterLine;

    // convenience
    public Vector3 center => transform.TransformPoint(centerOffset);

    void Awake()
    {
        waves = FindFirstObjectByType<Waves>();
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false; // we apply gravity as acceleration every step

        if (floatPoints == null || floatPoints.Length == 0)
            floatPoints = new[] { this.transform };

        waterLinePoints = new Vector3[floatPoints.Length];

        // compute center offset
        centerOffset = Vector3.zero;
        for (int i = 0; i < floatPoints.Length; i++)
            centerOffset += transform.InverseTransformPoint(floatPoints[i].position);
        centerOffset /= Mathf.Max(1, floatPoints.Length);
    }

    void FixedUpdate()
    {
        if (!waves) return;

        // Always apply gravity so bodies fall when unsupported
        rb.AddForce(Physics.gravity, ForceMode.Acceleration);

        // --- Sample water ---
        float newWaterLine = 0f;
        bool anySubmerged = false;
        bool fullySubmerged = true;
        int N = floatPoints.Length;

        float invN = 1f / Mathf.Max(1, N);
        float depthCap = Mathf.Max(0.05f, maxBuoyancyDepth);

        float submergedCount = 0f;
        float minDepthAll = float.PositiveInfinity;

        for (int i = 0; i < N; i++)
        {
            Vector3 fp = floatPoints[i].position;
            float h = waves.GetHeightAt(fp);
            waterLinePoints[i] = new Vector3(fp.x, h, fp.z);

            float depth = h - fp.y; // >0 if below surface
            if (depth > 0f) { anySubmerged = true; submergedCount += 1f; }
            minDepthAll = Mathf.Min(minDepthAll, depth);

            newWaterLine += h;
        }
        newWaterLine *= invN;
        float submergence01 = submergedCount * invN;      // 0..1 fraction of points submerged
        fullySubmerged = (submergedCount >= N) && (minDepthAll > fullSubmergedDepth);

        float waterLineDelta = newWaterLine - waterLine;
        waterLine = newWaterLine;

        // Drag scales with submergence (no hard flip)
        rb.linearDamping = Mathf.Lerp(airDrag, waterDrag, submergence01);
        rb.angularDamping = Mathf.Lerp(0.05f, waterAngularDrag, submergence01);

        // In-water angular velocity clamp
        if (submergence01 > 0f && maxAngularVelInWater > 0f)
        {
            float w = rb.angularVelocity.magnitude;
            if (w > maxAngularVelInWater)
                rb.angularVelocity = rb.angularVelocity * (maxAngularVelInWater / w);
        }

        // --- Buoyancy forces ---
        // Apply per-point upward acceleration proportional to submergence.
        // When fully submerged, blend application point towards COM to reduce torque (stabilizes).
        Vector3 g = Physics.gravity;
        Vector3 com = rb.worldCenterOfMass;
        float blend = fullySubmerged ? Mathf.Clamp01(deepSubmergeTorqueBlend) : 0f;

        for (int i = 0; i < N; i++)
        {
            Vector3 fp = floatPoints[i].position;
            float depth = waterLinePoints[i].y - fp.y; // positive if under
            if (depth <= 0f) continue;

            float sub = Mathf.Clamp01(depth / depthCap); // 0..1
            Vector3 upAccel = -g * (buoyancy * sub * invN);

            // Reduce lever arm when deep to prevent spin/jitter
            Vector3 applyPos = (blend > 0f) ? Vector3.Lerp(fp, com, blend) : fp;
            rb.AddForceAtPosition(upAccel, applyPos, ForceMode.Acceleration);
        }

        // Optional “stick” to surface (disable for realistic detach)
        if (attachToSurface)
        {
            Vector3 p = rb.position;
            p.y = waterLine - centerOffset.y;
            rb.MovePosition(p);
        }

        // --- Orientation control ---
        if (affectDirection)
        {
            if (submergence01 > 0f && !fullySubmerged)
            {
                // Partly submerged: align to water surface normal near center
                Vector3 samplePos = new Vector3(center.x, waterLine, center.z);
                targetUp = waves.GetNormalAt(samplePos, normalSampleRadius);
                Vector3 smoothedUp = Vector3.SmoothDamp(transform.up, targetUp, ref smoothVectorRotation, 0.12f);
                ApplyUprightPD(smoothedUp, uprightTorque, uprightDamping);
            }
            else if (fullySubmerged)
            {
                // Fully submerged: align gently to world-up to kill wild spins
                ApplyUprightPD(Vector3.up, uprightTorque * 0.6f, uprightDamping);
            }
            // In air (submergence01 == 0): no alignment torque; let it fly naturally
        }
    }

    /// <summary>PD controller that applies torque to align transform.up to targetUp.</summary>
    void ApplyUprightPD(Vector3 targetUp, float kp, float damping01)
    {
        Vector3 currentUp = transform.up;
        // rotation needed to go from currentUp to targetUp
        Vector3 axis = Vector3.Cross(currentUp, targetUp);
        float sinAngle = axis.magnitude;
        if (sinAngle < 1e-5f) return;

        axis /= sinAngle; // normalize
        float angle = Mathf.Asin(Mathf.Clamp(sinAngle, -1f, 1f)); // [-pi/2..pi/2] good for small angles

        // PD torque: proportional to angle, damped by current angular velocity
        Vector3 torque = axis * (kp * angle) - rb.angularVelocity * (kp * Mathf.Clamp01(damping01));
        rb.AddTorque(torque, ForceMode.Acceleration);
    }

    void OnDrawGizmos()
    {
        if (floatPoints == null) return;
        for (int i = 0; i < floatPoints.Length; i++)
        {
            if (floatPoints[i] == null) continue;
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(floatPoints[i].position, 0.05f);

            if (Application.isPlaying && waterLinePoints != null && i < waterLinePoints.Length)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawCube(waterLinePoints[i], Vector3.one * 0.12f);
            }
        }

        if (Application.isPlaying)
        {
            Gizmos.color = Color.red;
            Vector3 c = transform.TransformPoint(centerOffset);
            Gizmos.DrawRay(new Vector3(c.x, waterLine, c.z), targetUp * 0.8f);
        }
    }
}
