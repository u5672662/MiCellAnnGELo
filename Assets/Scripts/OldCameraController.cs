using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Base movement speed in units per second.")]
    public float baseMoveSpeed = 5.0f;  // Adjust as needed for FPS movement speed.
    [Tooltip("Smoothing time (in seconds) for movement acceleration/deceleration.")]
    public float movementSmoothTime = 0.1f;
    [Tooltip("Multiplier for mouse rotation sensitivity.")]
    public float rotationSensitivity = 1.0f;

    [Header("Rotation Settings")]
    [Tooltip("Minimum pitch angle (looking down).")]
    public float minPitch = -80f;
    [Tooltip("Maximum pitch angle (looking up).")]
    public float maxPitch = 80f;

    [Header("Optional Movement Boundaries")]
    [Tooltip("Minimum allowed Y position (vertical).")]
    public float minY = 0f;
    [Tooltip("Minimum allowed Z position (depth).")]
    public float minZ = -3f;

    // Internal state for position smoothing.
    private Vector3 startPosition;
    private Quaternion startRotation;
    private Vector3 targetPosition;
    private Vector3 currentVelocity; // Used by SmoothDamp.

    // Rotation accumulators.
    private float yaw;
    private float pitch;

    // Input accumulators.
    private Vector2 movementInput = Vector2.zero;
    // For first-person mouse-look, we capture delta rotation each frame.
    private Vector2 storedRotationInput = Vector2.zero;
    private float speedModifier = 1f;

    void Start()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
        targetPosition = transform.position;

        // Initialize yaw and pitch from the current rotation.
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;
    }

    void Update()
    {
        // --- Rotation Handling ---
        // Get the rotation input accumulated via SetRotationInput.
        Vector2 mouseDelta = GetRotationInput();
        // Adjust yaw and pitch based on mouse delta.
        yaw += mouseDelta.x * rotationSensitivity;
        pitch -= mouseDelta.y * rotationSensitivity;
        // Clamp the pitch to avoid flipping.
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        // Apply the new rotation immediately.
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // --- Movement Handling ---
        // For FPS controls, movement should be relative to the horizontal direction only.
        if (movementInput.sqrMagnitude > 0.001f)
        {
            // Build the forward/right vectors from yaw only.
            Quaternion yawRotation = Quaternion.Euler(0, yaw, 0);
            Vector3 forward = yawRotation * Vector3.forward;
            Vector3 right = yawRotation * Vector3.right;
            Vector3 desiredMove = (right * movementInput.x + forward * movementInput.y);
            targetPosition += desiredMove * baseMoveSpeed * speedModifier * Time.deltaTime;
        }

        // Enforce optional boundaries if needed.
        targetPosition.y = Mathf.Max(targetPosition.y, minY);
        targetPosition.z = Mathf.Max(targetPosition.z, minZ);

        // Smoothly interpolate the camera's position toward the target position.
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, movementSmoothTime);

        // Reset movement input each frame.
        movementInput = Vector2.zero;
    }

    /// <summary>
    /// Called by input events to supply rotation deltas (e.g. from mouse movement).
    /// </summary>
    public void SetRotationInput(Vector2 input)
    {
        // Accumulate the rotation delta.
        storedRotationInput += input;
    }

    /// <summary>
    /// Retrieves and clears the stored rotation input.
    /// </summary>
    private Vector2 GetRotationInput()
    {
        Vector2 input = storedRotationInput;
        storedRotationInput = Vector2.zero;
        return input;
    }

    /// <summary>
    /// Update the movement input (e.g. from keyboard WASD or joystick).
    /// Expected: x for lateral movement, y for forward/backward.
    /// </summary>
    public void SetMovementInput(Vector2 input)
    {
        movementInput = input;
    }

    /// <summary>
    /// Adjust the movement speed multiplier (e.g. fast or slow modifiers).
    /// </summary>
    public void SetSpeedModifier(float modifier)
    {
        speedModifier = modifier;
    }

    /// <summary>
    /// Immediately resets the camera to its starting position and orientation.
    /// </summary>
    public void ResetCamera()
    {
        targetPosition = startPosition;
        yaw = startRotation.eulerAngles.y;
        pitch = startRotation.eulerAngles.x;
        transform.position = startPosition;
        transform.rotation = startRotation;
        currentVelocity = Vector3.zero;
    }
}
