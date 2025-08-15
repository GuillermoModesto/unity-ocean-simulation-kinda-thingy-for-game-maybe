/*
 Summary: Simple boat controller that adds steering and thrust forces while in water, with optional motor visual yaw and particle effects.

 Usage:
   - Requires BuoyantRigidbody and Rigidbody on the same GameObject.
   - Bind movement on the 'Player/Move' action (WASD/left stick).
   - Adjust 'Power', 'MaxSpeed', and 'SteerPower' per craft.
*/

﻿using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(BuoyantRigidbody))]
[RequireComponent(typeof(Rigidbody))]
public class BoatController : MonoBehaviour
{
    [Header("Boat Setup")]
    public Transform Motor;

    [Header("Forces")]
    public float SteerPower = 500f;
    public float Power = 5f;
    public float MaxSpeed = 10f;

    [Header("Behavior")]
    [Tooltip("How strongly the velocity is rotated toward the forward direction each frame.")]
    [Range(0f, 1f)] public float Drag = 0.1f;

    [Header("Rigidbody Damping")]
    public float bodyDrag = 0.2f;
    public float bodyAngularDrag = 1f;

    private Rigidbody _rb;
    private BuoyantRigidbody _buoyant;
    private Quaternion _startRotation;
    private ParticleSystem _particleSystem;
    private Camera _camera;

    private InputSystem_Actions _actions;
    private Vector2 _move;
    private bool _hasActions;

    private Vector3 _camVel;

    void Awake()
    {
        _particleSystem = GetComponentInChildren<ParticleSystem>();
        _rb = GetComponent<Rigidbody>();
        _buoyant = GetComponent<BuoyantRigidbody>();
        _startRotation = Motor ? Motor.localRotation : Quaternion.identity;
        _camera = Camera.main;

        _rb.linearDamping = bodyDrag;
        _rb.angularDamping = bodyAngularDrag;

        _actions = new InputSystem_Actions();
        _actions.Player.Move.performed += ctx => _move = ctx.ReadValue<Vector2>();
        _actions.Player.Move.canceled += ctx => _move = Vector2.zero;
        _hasActions = true;
    }

    void OnEnable()
    {
        if (_hasActions) _actions.Enable();
    }

    void OnDisable()
    {
        if (_hasActions) _actions.Disable();
    }

    void FixedUpdate()
    {

        if (_buoyant == null || !_buoyant.IsInWater)
        {
            if (_particleSystem) _particleSystem.Pause();
            return;
        }

        int steer = 0;
        if (_move.x > 0.2f) steer = -1;
        if (_move.x < -0.2f) steer = 1;

        if (Motor)
            _rb.AddForceAtPosition(steer * transform.right * (SteerPower / 100f), Motor.position);
        else
            _rb.AddTorque(steer * Vector3.up * (SteerPower / 10f), ForceMode.Force);

        Vector3 forward = Vector3.Scale(new Vector3(1f, 0f, 1f), transform.forward);

        if (_move.y > 0.2f)
        {
            ApplyForceToReachVelocity(_rb, forward * MaxSpeed, Power);
            if (_particleSystem) _particleSystem.Play();
        }
        else if (_move.y < -0.2f)
        {
            ApplyForceToReachVelocity(_rb, forward * -MaxSpeed, Power);
            if (_particleSystem) _particleSystem.Play();
        }
        else
        {
            if (_particleSystem) _particleSystem.Pause();
        }

        if (Motor)
        {
            float visualSteer = (steer == 0) ? 0f : (steer > 0 ? 30f : -30f);
            Motor.SetPositionAndRotation(
                Motor.position,
                transform.rotation * _startRotation * Quaternion.Euler(0f, visualSteer, 0f)
            );
        }

        bool movingForward = Vector3.Cross(transform.forward, _rb.linearVelocity).y < 0f;
        Vector3 desiredDir = (movingForward ? 1f : 0f) * transform.forward;

        float ang = Vector3.SignedAngle(_rb.linearVelocity, desiredDir, Vector3.up);
        _rb.linearVelocity = Quaternion.AngleAxis(ang * Drag, Vector3.up) * _rb.linearVelocity;
    }

    public static void ApplyForceToReachVelocity(Rigidbody rb, Vector3 velocity, float force = 1, ForceMode mode = ForceMode.Force)
    {
        if (force == 0 || velocity.magnitude == 0)
            return;

        velocity = velocity + velocity.normalized * 0.2f * rb.linearDamping;
        force = Mathf.Clamp(force, -rb.mass / Time.fixedDeltaTime, rb.mass / Time.fixedDeltaTime);

        if (rb.linearVelocity.magnitude == 0)
        {
            rb.AddForce(velocity * force, mode);
        }
        else
        {
            var projected = velocity.normalized * Vector3.Dot(velocity, rb.linearVelocity) / velocity.magnitude;
            rb.AddForce((velocity - projected) * force, mode);
        }
    }
}
