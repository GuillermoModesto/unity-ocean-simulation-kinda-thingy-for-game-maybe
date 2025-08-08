using UnityEngine;

/// <summary>
/// Utility class providing physics-related helper methods for water simulation and movement.
/// Includes functions for calculating geometric properties and applying forces to rigidbodies.
/// </summary>
public class PhysicsHelper : MonoBehaviour
{
    /// <summary>
    /// Calculates the geometric center (average position) of a set of points.
    /// </summary>
    /// <param name="points">Array of Vector3 points.</param>
    /// <returns>Center position as Vector3.</returns>
    public static Vector3 GetCenter(Vector3[] points)
    {
        var center = Vector3.zero;
        for (int i = 0; i < points.Length; i++)
            center += points[i] / points.Length;
        return center;
    }

    /// <summary>
    /// Calculates the normal vector of a plane best fitting the given points.
    /// Useful for orienting objects to the water surface.
    /// </summary>
    /// <param name="points">Array of Vector3 points.</param>
    /// <returns>Normal vector as Vector3.</returns>
    public static Vector3 GetNormal(Vector3[] points)
    {
        //https://www.ilikebigbits.com/2015_03_04_plane_from_points.html
        if (points.Length < 3)
            return Vector3.up;

        var center = GetCenter(points);

        float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;

        for (int i = 0; i < points.Length; i++)
        {
            var r = points[i] - center;
            xx += r.x * r.x;
            xy += r.x * r.y;
            xz += r.x * r.z;
            yy += r.y * r.y;
            yz += r.y * r.z;
            zz += r.z * r.z;
        }

        var det_x = yy * zz - yz * yz;
        var det_y = xx * zz - xz * xz;
        var det_z = xx * yy - xy * xy;

        if (det_x > det_y && det_x > det_z)
            return new Vector3(det_x, xz * yz - xy * zz, xy * yz - xz * yy).normalized;
        if (det_y > det_z)
            return new Vector3(xz * yz - xy * zz, det_y, xy * xz - yz * xx).normalized;
        else
            return new Vector3(xy * yz - xz * yy, xy * xz - yz * xx, det_z).normalized;

    }

    /// <summary>
    /// Applies a force to a Rigidbody to reach a target velocity.
    /// Useful for smooth acceleration and movement control.
    /// </summary>
    /// <param name="rigidbody">Rigidbody to apply force to.</param>
    /// <param name="velocity">Target velocity vector.</param>
    /// <param name="force">Force multiplier (default 1).</param>
    /// <param name="mode">ForceMode for the force application (default Force).</param>
    public static void ApplyForceToReachVelocity(Rigidbody rigidbody, Vector3 velocity, float force = 1, ForceMode mode = ForceMode.Force)
    {
        if (force == 0 || velocity.magnitude == 0)
            return;

        velocity = velocity + velocity.normalized * 0.2f * rigidbody.linearDamping;

        //force = 1 => need 1 s to reach velocity (if mass is 1) => force can be max 1 / Time.fixedDeltaTime
        force = Mathf.Clamp(force, -rigidbody.mass / Time.fixedDeltaTime, rigidbody.mass / Time.fixedDeltaTime);

        //dot product is a projection from rhs to lhs with a length of result / lhs.magnitude https://www.youtube.com/watch?v=h0NJK4mEIJU
        if (rigidbody.linearVelocity.magnitude == 0)
        {
            rigidbody.AddForce(velocity * force, mode);
        }
        else
        {
            var velocityProjectedToTarget = (velocity.normalized * Vector3.Dot(velocity, rigidbody.linearVelocity) / velocity.magnitude);
            rigidbody.AddForce((velocity - velocityProjectedToTarget) * force, mode);
        }
    }
}
