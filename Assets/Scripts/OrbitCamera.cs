using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;        // The boat
    public float distance = 12f;
    public float minDistance = 5f;
    public float maxDistance = 40f;
    public float zoomSpeed = 2f;

    public float height = 3f;
    public float sensitivity = 100f;
    public float minPitch = -20f;
    public float maxPitch = 70f;

    private InputSystem_Actions _actions;
    private Vector2 _lookInput;
    private float _yaw = 0f;
    private float _pitch = 20f;

    void Awake()
    {
        _actions = new InputSystem_Actions();

        // Mouse look
        _actions.Player.Look.performed += ctx => _lookInput = ctx.ReadValue<Vector2>();
        _actions.Player.Look.canceled += ctx => _lookInput = Vector2.zero;

        // Scroll wheel zoom (uses the Zoom action if it's in the generated class)
        // If your generated class doesn't yet have Zoom, the fallback below will handle it.
        try
        {
            _actions.Player.Zoom.performed += ctx =>
            {
                Vector2 scroll = ctx.ReadValue<Vector2>(); // Y is the wheel delta
                ApplyZoom(scroll.y);
            };
        }
        catch { /* Zoom action not generated yet; fallback will be used */ }
    }

    void OnEnable() => _actions.Enable();
    void OnDisable() => _actions.Disable();

    void LateUpdate()
    {
        if (!target) return;

        // Fallback zoom if Zoom action isn't available (or not bound)
        if (Mouse.current != null)
        {
            // Some mice report per-frame deltas; do NOT multiply by deltaTime
            float fallbackScrollY = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(fallbackScrollY) > 0.0001f)
                ApplyZoom(fallbackScrollY);
        }

        // Orbit angles from mouse
        _yaw += _lookInput.x * sensitivity * Time.deltaTime;
        _pitch -= _lookInput.y * sensitivity * Time.deltaTime;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        // Calculate rotation
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);

        // Position camera behind target at orbit distance
        Vector3 camPos = target.position + rot * new Vector3(0, height, -distance);
        transform.position = camPos;

        // Always look at target
        transform.LookAt(target.position + Vector3.up * height * 0.5f);
    }

    private void ApplyZoom(float scrollY)
    {
        // Positive scrollY usually means scroll up; invert here if you prefer the opposite
        distance -= scrollY * (zoomSpeed * 0.1f); // 0.1f scales typical scroll values to a comfortable speed
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }
}
