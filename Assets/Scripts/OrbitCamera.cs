using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;        // The boat
    public float distance = 12f;
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
        _actions.Player.Look.performed += ctx => _lookInput = ctx.ReadValue<Vector2>();
        _actions.Player.Look.canceled += ctx => _lookInput = Vector2.zero;
    }

    void OnEnable() => _actions.Enable();
    void OnDisable() => _actions.Disable();

    void LateUpdate()
    {
        if (!target) return;

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
}
