using UnityEngine;

[ExecuteAlways]
public class OceanDebugProbe : MonoBehaviour
{
    public Color color = Color.cyan;
    public float normalLen = 1f;

    void OnDrawGizmos()
    {
        float h; Vector3 n;
        Ocean.Sample(new Vector2(transform.position.x, transform.position.z), out h, out n, iterations: 1);
        var p = new Vector3(transform.position.x, h, transform.position.z);
        Gizmos.color = color;
        Gizmos.DrawSphere(p, 0.08f);
        Gizmos.DrawLine(p, p + n.normalized * normalLen);
    }
}
