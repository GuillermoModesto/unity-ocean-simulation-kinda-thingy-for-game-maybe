using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class SimpleWakeSpawner : MonoBehaviour
{
    public Transform[] polyPoints;       // assign child transforms tagged “PolyPoint”, or leave empty to use sternOnly
    public Transform sternPoint;         // fallback: one point at stern
    public float emitRatePerPoint = 8f;  // particles/sec per point
    public float jitter = 0.5f;          // random offset in meters
    public float startSize = 2.5f;
    public float startLifetime = 4f;

    ParticleSystem ps;
    BoatController bc;
    float accum;

    void Awake()
    {
        ps = GetComponent<ParticleSystem>(); 
        bc = GetComponent<BoatController>();
    }

    void LateUpdate()
    {
        if (!ps || !bc.isMoving) return;

        var points = new List<Transform>();
        if (polyPoints != null && polyPoints.Length >= 3) points.AddRange(polyPoints);
        else if (sternPoint) points.Add(sternPoint);
        else points.Add(transform); // last resort

        float dt = Time.deltaTime;
        float perPoint = emitRatePerPoint * dt;

        var emit = new ParticleSystem.EmitParams();
        emit.startSize = startSize;
        emit.startLifetime = startLifetime;
        emit.startColor = Color.white;

        foreach (var p in points)
        {
            int count = Mathf.FloorToInt(perPoint);
            float frac = perPoint - count;
            if (Random.value < frac) count++;

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = p.position + new Vector3(
                    (Random.value * 2f - 1f) * jitter,
                    0f,
                    (Random.value * 2f - 1f) * jitter
                );

                // snap to sea level (optional: raycast to your water)
                pos.y = 0f;

                emit.position = pos;
                emit.velocity = Vector3.zero;
                ps.Emit(emit, 1);
            }
        }
    }
}
