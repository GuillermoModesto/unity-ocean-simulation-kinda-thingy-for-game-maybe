using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class BoatControllerRealistic : MonoBehaviour
{
    public float thrustForce = 30f;
    [Range(0f, 1f)] public float reverseMultiplier = 0.5f;
    public float turnSpeed = 40f;          // steering authority
    public float sprintMultiplier = 1.5f;

    private Rigidbody _rb;
    private InputSystem_Actions _actions;
    private Vector2 _move;
    private bool _sprinting;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.freezeRotation = false; // allow buoyancy to tilt

        // Boat physics tuning
        _rb.linearDamping = 0.2f;         // slight water resistance
        _rb.angularDamping = 1f;    // prevents endless spin

        _actions = new InputSystem_Actions();
        _actions.Player.Move.performed += ctx => _move = ctx.ReadValue<Vector2>();
        _actions.Player.Move.canceled += ctx => _move = Vector2.zero;
        _actions.Player.Sprint.performed += ctx => _sprinting = true;
        _actions.Player.Sprint.canceled += ctx => _sprinting = false;
    }

    void OnEnable() => _actions.Enable();
    void OnDisable() => _actions.Disable();

    void FixedUpdate()
    {
        float boost = _sprinting ? sprintMultiplier : 1f;

        // --- Throttle ---
        float throttle = Mathf.Clamp(_move.y, -1f, 1f);
        float thrust = throttle >= 0 ? thrustForce : thrustForce * reverseMultiplier;
        _rb.AddForce(transform.forward * thrust * throttle * boost, ForceMode.Acceleration);

        // --- Turning via torque (scaled by forward speed) ---
        float steer = Mathf.Clamp(_move.x, -1f, 1f);
        float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward); // signed speed
        float turnScale = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 5f);     // fade in with speed
        _rb.AddTorque(Vector3.up * steer * turnSpeed * boost * turnScale, ForceMode.Acceleration);

        // --- Lateral drag (weaker when slow) ---
        Vector3 vel = _rb.linearVelocity;
        Vector3 forwardVel = Vector3.Project(vel, transform.forward);
        Vector3 lateralVel = vel - forwardVel;
        float lateralFactor = Mathf.Clamp01(vel.magnitude / 2f); // no drag if barely moving
        _rb.AddForce(-lateralVel * 2f * lateralFactor, ForceMode.Acceleration);
    }
}
