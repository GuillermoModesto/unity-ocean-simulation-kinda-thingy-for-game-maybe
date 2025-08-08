using UnityEngine;
using static UnityEngine.GraphicsBuffer;

/// <summary>
/// Simulates floating physics for objects on a dynamic water surface.
/// Calculates waterline, applies drag, and adjusts orientation based on water normals.
/// </summary>
public class WaterFloat : MonoBehaviour
{
    /// <summary>Drag applied when object is in air.</summary>
    public float airDrag = 1;
    /// <summary>Drag applied when object is in water.</summary>
    public float waterDrag = 10;
    /// <summary>If true, object orientation is affected by water surface normal.</summary>
    public bool affectDirection = true;
    /// <summary>If true, object snaps to water surface height.</summary>
    public bool attachToSurface = false;
    /// <summary>Points used to sample water height and calculate buoyancy.</summary>
    public Transform[] floatPoints;

    protected Rigidbody rb;
    protected Waves waves;

    /// <summary>Current average waterline under float points.</summary>
    protected float waterLine;
    /// <summary>World positions of float points, with y set to water height.</summary>
    protected Vector3[] waterLinePoints;

    /// <summary>Offset from object center to center of float points.</summary>
    protected Vector3 centerOffset;
    /// <summary>Helper for smoothing orientation changes.</summary>
    protected Vector3 smoothVectorRotation;
    /// <summary>Target up vector based on water surface normal.</summary>
    protected Vector3 targetUp;

    /// <summary>World position of the object's center, considering offset.</summary>
    public Vector3 center => transform.TransformPoint(centerOffset);

    /// <summary>
    /// Initializes references and calculates center offset from float points.
    /// </summary>
    void Awake()
    {
        waves = FindFirstObjectByType<Waves>();
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;

        waterLinePoints = new Vector3[floatPoints.Length];
        centerOffset = Vector3.zero;
        for (int i = 0; i < floatPoints.Length; i++)
        {
            centerOffset += transform.InverseTransformPoint(floatPoints[i].position);
        }
        centerOffset /= floatPoints.Length;
    }

    /// <summary>
    /// Updates waterline, applies drag and forces, and adjusts orientation.
    /// </summary>
    void Update()
    {
        var newWaterLine = 0f;
        var pointUnderWater = false;

        // Sample water height at each float point
        for (int i = 0; i < floatPoints.Length; i++)
        {
            waterLinePoints[i] = floatPoints[i].position;
            waterLinePoints[i].y = waves.GetHeightAt(floatPoints[i].position);
            newWaterLine += waterLinePoints[i].y / floatPoints.Length;
            if (waterLinePoints[i].y < floatPoints[i].position.y)
            {
                pointUnderWater = true;
            }
        }

        var waterLineDelta = newWaterLine - waterLine;
        waterLine = newWaterLine;

        var gravity = Physics.gravity;
        rb.linearDamping = airDrag;

        // If object is below waterline, apply water drag and adjust position
        if (waterLine > center.y)
        {
            rb.linearDamping = waterDrag;
            if (attachToSurface)
            {
                // Snap object to water surface
                rb.position = new Vector3(rb.position.x, waterLine - centerOffset.y, rb.position.z);
            }
            else
            {
                // Invert gravity and move object up by waterline delta
                gravity = -Physics.gravity;
                transform.Translate(Vector3.up * waterLineDelta * 0.9f);
            }
        }
        // Apply vertical force proportional to waterline difference
        rb.AddForce(gravity * Mathf.Clamp(Mathf.Abs(waterLine - center.y), 0, 1));

        // Calculate water surface normal for orientation
        targetUp = PhysicsHelper.GetNormal(waterLinePoints);

        // If any float point is underwater, smoothly rotate towards water normal
        if (pointUnderWater)
        {
            targetUp = Vector3.SmoothDamp(transform.up, targetUp, ref smoothVectorRotation, 0.2f);
            rb.rotation = Quaternion.FromToRotation(transform.up, targetUp) * rb.rotation;
        }
    }
    
    /// <summary>
    /// Draws gizmos for float points, waterline, and orientation in the editor.
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        if (floatPoints == null)
            return;

        for (int i = 0; i < floatPoints.Length; i++)
        {
            if (floatPoints[i] == null)
                continue;

            if (waves != null)
            {
                // Draw cube at waterline position
                Gizmos.color = Color.red;
                Gizmos.DrawCube(waterLinePoints[i], Vector3.one * 0.15f);
            }

            // Draw sphere at float point position
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(floatPoints[i].position, 0.05f);
        }

        // Draw center and up vector if playing
        if (Application.isPlaying)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawCube(new Vector3(center.x, waterLine, center.z), Vector3.one * 0.5f);
            Gizmos.DrawRay(new Vector3(center.x, waterLine, center.z), targetUp * 1f);
        }
    }
    
}
