using UnityEngine;

/// <summary>
/// Controls a boat's movement and steering using player input.
/// Applies forces for propulsion and steering, and manages visual effects.
/// </summary>
public class WaterBoat : MonoBehaviour
{
    /// <summary>Transform representing the boat's motor position.</summary>
    public Transform motor;
    /// <summary>Steering force applied when turning.</summary>
    public float steerPower = 500f;
    /// <summary>Propulsion force applied for forward movement.</summary>
    public float power = 5f;
    /// <summary>Maximum speed the boat can reach.</summary>
    public float maxSpeed = 10f;
    /// <summary>Drag applied to the boat's Rigidbody.</summary>
    public float drag = 0.1f;

    protected Rigidbody rb;
    /// <summary>Initial local rotation of the motor, used for steering animation.</summary>
    protected Quaternion startRotation;
    /// <summary>Particle system for visual effects (e.g., water spray).</summary>
    protected new ParticleSystem particleSystem;

    /// <summary>Stores current movement input from the player.</summary>
    private Vector2 movementInput;
    /// <summary>Handles player input actions for boat control.</summary>
    private PlayerInputActions inputActions;

    /// <summary>
    /// Initializes input actions and sets up event handlers for movement input.
    /// </summary>
    void Awake()
    {
        inputActions = new PlayerInputActions();
        inputActions.BoatControl.Move.performed += ctx => movementInput = ctx.ReadValue<Vector2>();
        inputActions.BoatControl.Move.canceled += ctx => movementInput = Vector2.zero;
    }

    /// <summary>Enables input actions when the object is enabled.</summary>
    void OnEnable() => inputActions.Enable();
    /// <summary>Disables input actions when the object is disabled.</summary>
    void OnDisable() => inputActions.Disable();

    /// <summary>
    /// Initializes Rigidbody and stores the motor's starting rotation.
    /// </summary>
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        startRotation = motor.localRotation;
    }

    /// <summary>
    /// Handles boat movement and steering physics each fixed frame.
    /// Applies steering force, forward propulsion, and updates motor rotation and particle effects.
    /// </summary>
    void FixedUpdate()
    {
        float steer = movementInput.x;
        float throttle = movementInput.y;

        // Apply steering force at the motor position
        rb.AddForceAtPosition(steer * transform.right * steerPower / 100f, motor.position);

        // Calculate forward direction, ignoring vertical component
        var forward = Vector3.Scale(new Vector3(1, 0, 1), transform.forward);

        // Apply propulsion force if throttle is pressed
        if (Mathf.Abs(throttle) > 0.1f)
        {
            PhysicsHelper.ApplyForceToReachVelocity(rb, forward * maxSpeed * throttle, power);
        }

        // Update motor rotation for steering animation
        motor.SetPositionAndRotation(
            motor.position,
            transform.rotation * startRotation * Quaternion.Euler(0, 30f * steer, 0)
        );

        // Manage particle system based on throttle input
        if (particleSystem != null)
        {
            if (Mathf.Abs(throttle) > 0.1f)
            {
                if (!particleSystem.isPlaying) particleSystem.Play();
            }
            else
            {
                if (particleSystem.isPlaying) particleSystem.Stop();
            }
        }
    }
}
