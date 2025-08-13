using UnityEngine;

/// <summary>
/// Simplest possible buoyancy: positions an object on the Ocean surface
/// at its XZ and (optionally) tilts it to the local wave normal.
/// </summary>
public class BuoyantFollowOcean : MonoBehaviour
{
    [Tooltip("Extra height above the water surface (e.g., to place the waterline).")]
    public float waterlineOffset = 0f;

    [Header("Orientation")]
    [Tooltip("If true, smoothly align this object's up to the ocean normal.")]
    public bool alignToWaveNormal = true;
    [Tooltip("How fast we slerp toward the wave normal (per second).")]
    [Min(0f)] public float normalAlignSpeed = 6f;

    [Header("Smoothing")]
    [Tooltip("If > 0, smoothly follow the surface instead of snapping (seconds to reach ~63%).")]
    [Min(0f)] public float heightSmoothTime = 0.12f;
    private float _heightVel; // for SmoothDamp

    void LateUpdate()
    {
        // Requires an Ocean in the scene (Ocean.Sample uses a singleton internally)
        Vector2 xz = new Vector2(transform.position.x, transform.position.z);

        float waterHeight;
        Vector3 waterNormal;
        Ocean.Sample(xz, out waterHeight, out waterNormal, iterations: 4); // world-space height & normal

        // Set/Smooth Y to water height (+ optional offset)
        var p = transform.position;
        float targetY = waterHeight + waterlineOffset;

        if (heightSmoothTime > 0f && Application.isPlaying)
            p.y = Mathf.SmoothDamp(p.y, targetY, ref _heightVel, heightSmoothTime);
        else
            p.y = targetY;

        transform.position = p;

        // Optionally tilt to wave normal (keep current yaw)
        if (alignToWaveNormal)
        {
            // Keep heading: project forward onto the tangent plane so yaw remains intuitive
            Vector3 up = waterNormal.sqrMagnitude > 0.0001f ? waterNormal.normalized : Vector3.up;

            // Rebuild rotation with same yaw but new up
            Vector3 fwd = transform.forward;
            // Ensure fwd isn’t parallel to up
            if (Vector3.Dot(fwd, up) > 0.999f) fwd = Vector3.right;

            // Gram–Schmidt to get an orthonormal basis with 'up'
            Vector3 right = Vector3.Normalize(Vector3.Cross(up, fwd));
            Vector3 fwdOnPlane = Vector3.Normalize(Vector3.Cross(right, up));
            Quaternion targetRot = Quaternion.LookRotation(fwdOnPlane, up);

            if (normalAlignSpeed > 0f && Application.isPlaying)
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-normalAlignSpeed * Time.deltaTime));
            else
                transform.rotation = targetRot;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Draw a little marker where we sample
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.15f);
    }
#endif
}
