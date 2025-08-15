using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class PurgeDebugUI : MonoBehaviour
{
    void Update()
    {
        // 1) Kill any canvas we created earlier (even if hidden or not in hierarchy)
        foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            if (!c) continue;
            if (c.name.Contains("MaskDebugCanvas"))
            {
                Object.DestroyImmediate(c.gameObject);
            }
        }

        // 2) Kill any RawImage that’s showing the mask RT
        var mgr = ContactMaskManager.Instance;
        Texture mask = mgr ? (Texture)mgr.MaskRT : null;

        foreach (var ri in Resources.FindObjectsOfTypeAll<RawImage>())
        {
            if (!ri) continue;
            if (ri.texture == mask || ri.name.Contains("MaskDebugImage"))
            {
                Object.DestroyImmediate(ri.gameObject);
            }
        }
    }
}
