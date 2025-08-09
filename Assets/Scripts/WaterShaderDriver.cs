using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class WaterShaderDriver : MonoBehaviour
{
    static readonly int ID_WindDir = Shader.PropertyToID("_WindDir");
    static readonly int ID_CurrentDir = Shader.PropertyToID("_CurrentDir");
    static readonly int ID_WaveTime = Shader.PropertyToID("_WaveTime");

    public Waves waves; // assign in Inspector; if left null, it tries to find one
    Renderer rend;
    MaterialPropertyBlock mpb;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
        if (!waves) waves = FindFirstObjectByType<Waves>();
    }

    void LateUpdate()
    {
        if (!waves) return;

        rend.GetPropertyBlock(mpb);

        Vector2 wDir = waves.windDirection.sqrMagnitude > 0 ? waves.windDirection.normalized : Vector2.zero;
        Vector2 cDir = waves.currentDirection.sqrMagnitude > 0 ? waves.currentDirection.normalized : Vector2.zero;

        mpb.SetVector(ID_WindDir, new Vector4(wDir.x, wDir.y, 0f, waves.windStrength));
        mpb.SetVector(ID_CurrentDir, new Vector4(cDir.x, cDir.y, 0f, waves.currentStrength));
        mpb.SetFloat(ID_WaveTime, Time.time);

        rend.SetPropertyBlock(mpb);
    }
}
