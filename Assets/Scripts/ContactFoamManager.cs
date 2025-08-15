using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;

/// Attach once (e.g., on your Ocean GameObject).
/// It finds all BuoyantRigidbody components, checks their FloatPoints against the water,
/// and sends contact points to the ocean shader each frame.
public class ContactFoamManager : MonoBehaviour
{
    [Header("Contact detection")]
    [Tooltip("Meters above the sampled water surface still considered 'touching'.")]
    public float contactThreshold = 0.03f;
    [Tooltip("Radius (meters) for gradient falloff per contact point.")]
    public float contactRadius = 0.6f;
    [Tooltip("Max contact points sent to shader.")]
    [Range(8, 128)] public int maxContacts = 48;

    [Header("Look")]
    [Range(0, 3)] public float contactFoamAmount = 1.0f;      // overall foam intensity
    public Vector4 rippleParams = new Vector4(20f, 4f, 2f, 0.12f); // (frequency, speed, decay, strength)

    static readonly List<Vector4> contacts = new List<Vector4>(128);
    BuoyantRigidbody[] buoyants;
    float nextRefreshTime;

    void OnEnable() => RefreshBuoyants();
    void RefreshBuoyants()
    {
        buoyants = FindObjectsByType<BuoyantRigidbody>(new FindObjectsSortMode());
        nextRefreshTime = Time.time + 1f; // refresh the list once per second
    }

    void LateUpdate()
    {
        if (Time.time >= nextRefreshTime) RefreshBuoyants();

        contacts.Clear();

        if (buoyants != null)
        {
            foreach (var b in buoyants)
            {
                if (!b || b.FloatPoints == null) continue;

                foreach (var t in b.FloatPoints)
                {
                    if (!t) continue;

                    Vector3 p = t.position;                        // <- samplePoint
                    float h; Vector3 n;
                    Ocean.Sample(new Vector2(p.x, p.z), out h, out n); // <- waterHeight is 'h'

                    // "Touching" if probe is at/under the surface (with a small allowance)
                    if (p.y <= h + contactThreshold)
                    {
                        // x = worldX, y = worldZ, z = radius, w = strength
                        contacts.Add(new Vector4(p.x, p.z, contactRadius, 1f));
                        if (contacts.Count >= maxContacts) break;
                    }
                }
                if (contacts.Count >= maxContacts) break;
            }
        }

        // Upload to shader as globals
        int count = Mathf.Min(contacts.Count, maxContacts);
        // pack into a fixed array (no allocs)
        Vector4[] data = SharedArray(maxContacts);
        for (int i = 0; i < count; i++) data[i] = contacts[i];
        for (int i = count; i < maxContacts; i++) data[i] = Vector4.zero;

        Shader.SetGlobalInt("_ContactCount", count);
        Shader.SetGlobalVectorArray("_ContactPoints", data);
        Shader.SetGlobalFloat("_ContactFoamAmount", contactFoamAmount);
        Shader.SetGlobalVector("_ContactRipple", rippleParams);
    }

    static Vector4[] pool;
    static Vector4[] SharedArray(int cap)
    {
        if (pool == null || pool.Length != cap) pool = new Vector4[cap];
        return pool;
    }
}
