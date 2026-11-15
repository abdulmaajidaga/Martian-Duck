using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// First-person CharacterController tuned for Mars: WASD to move, mouse to look, Shift to sprint,
/// Space to jump, Esc to unlock the cursor and left click to lock it again.
/// Uses the Input System package when it is enabled, otherwise the legacy Input Manager.
/// The camera is expected to be a child of this object (placed at eye height).
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class MarsPlayerController : MonoBehaviour
{
    public const float MarsGravity = 3.71f;

    [Header("Movement")]
    [Tooltip("Walking speed (m/s). Faster than a real suited astronaut (~1.5 m/s) so the 45 km map is playable.")]
    [SerializeField, Min(0f)] float walkSpeed = 4f;
    [Tooltip("Speed while holding Shift (m/s).")]
    [SerializeField, Min(0f)] float sprintSpeed = 12f;
    [Tooltip("How quickly speed changes on the ground (m/s²).")]
    [SerializeField, Min(0f)] float groundAcceleration = 30f;
    [Tooltip("How quickly speed changes in the air (m/s²). Some control is kept so short hops over bumps don't feel like lost input.")]
    [SerializeField, Min(0f)] float airAcceleration = 8f;

    [Header("Explore mode")]
    [Tooltip("Explore (creative) mode: much faster movement for getting around the 45 km map. Toggled from the HUD (X).")]
    [SerializeField] bool exploreMode;
    [Tooltip("Walking speed in explore mode (m/s).")]
    [SerializeField, Min(0f)] float exploreWalkSpeed = 30f;
    [Tooltip("Speed while holding Shift in explore mode (m/s).")]
    [SerializeField, Min(0f)] float exploreSprintSpeed = 150f;
    [Tooltip("How quickly speed changes in explore mode, on the ground and in the air (m/s²).")]
    [SerializeField, Min(0f)] float exploreAcceleration = 250f;

    /// <summary>Explore (creative) mode: very fast walking/sprinting with full air control.</summary>
    public bool ExploreMode { get => exploreMode; set => exploreMode = value; }
    /// <summary>Current horizontal speed (m/s).</summary>
    public float HorizontalSpeed => horizontalVelocity.magnitude;

    [Header("Ground following")]
    [Tooltip("After walking off a bump or down a slope, stick back to the ground if it is within this distance (m) instead of floating off in the low gravity.")]
    [SerializeField, Min(0f)] float groundSnapDistance = 0.8f;
    [Tooltip("Seconds after leaving the ground during which the player still counts as grounded (full control, can jump).")]
    [SerializeField, Min(0f)] float coyoteTime = 0.15f;

    [Header("Jumping & Gravity")]
    [Tooltip("Downward acceleration in m/s². Mars: 3.71, Earth: 9.81.")]
    [SerializeField, Min(0f)] float gravity = MarsGravity;
    [Tooltip("Height of a jump in meters. 1.2 m on Mars takes the same push-off as about 0.45 m on Earth.")]
    [SerializeField, Min(0f)] float jumpHeight = 1.2f;

    [Header("Slopes")]
    [Tooltip("Steepest slope, in degrees, the player can walk up. Applied to the CharacterController.")]
    [SerializeField, Range(0f, 90f)] float slopeLimit = 40f;
    [Tooltip("Slide down surfaces steeper than the slope limit instead of standing on them.")]
    [SerializeField] bool slideOnSteepSlopes = true;
    [SerializeField, Min(0f)] float slideSpeed = 3f;

    [Header("Look")]
    [Tooltip("Camera to pitch up and down. Defaults to the first Camera among this object's children.")]
    [SerializeField] Transform cameraTransform;
    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    [SerializeField, Min(0f)] float lookSensitivity = 0.1f;
    [SerializeField, Range(0f, 90f)] float maxPitch = 85f;
    [SerializeField] bool invertY;

    // The legacy "Mouse X/Y" axes report pixels x 0.1 with default Input Manager settings.
    const float LegacyMouseAxisToPixels = 10f;

    CharacterController controller;
    Vector3 horizontalVelocity;
    float verticalVelocity;
    float pitch;
    float lastGroundedTime = -1f;
    bool jumpedThisAir;

    bool onSteepSlope;
    Vector3 groundNormal = Vector3.up;
    bool steepHitThisMove;
    Vector3 groundNormalThisMove;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        controller.slopeLimit = slopeLimit;
        controller.minMoveDistance = 0f; // Keeps isGrounded reliable at low speeds.

        if (cameraTransform == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null)
                cameraTransform = childCamera.transform;
        }

        if (cameraTransform != null)
            pitch = Mathf.DeltaAngle(0f, cameraTransform.localEulerAngles.x);
    }

    void OnValidate()
    {
        if (TryGetComponent(out CharacterController cc))
            cc.slopeLimit = slopeLimit;
    }

    void OnEnable() => SetCursorLocked(true);
    void OnDisable() => SetCursorLocked(false);

    void Update()
    {
        UpdateCursor();

        if (Cursor.lockState == CursorLockMode.Locked)
            Look();

        Move();
    }

    void UpdateCursor()
    {
        if (UnlockPressed())
            SetCursorLocked(false);
        else if (Cursor.lockState != CursorLockMode.Locked && RelockPressed())
            SetCursorLocked(true);
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void Look()
    {
        Vector2 delta = ReadLookDelta() * lookSensitivity;

        transform.Rotate(0f, delta.x, 0f, Space.World);

        pitch = Mathf.Clamp(pitch + (invertY ? delta.y : -delta.y), -maxPitch, maxPitch);
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void Move()
    {
        float dt = Time.deltaTime;
        bool touching = controller.isGrounded;
        if (touching) { lastGroundedTime = Time.time; jumpedThisAir = false; }
        // "Grounded" for control purposes includes a short grace period after leaving the ground (coyote time).
        bool grounded = touching || (!jumpedThisAir && Time.time - lastGroundedTime <= coyoteTime);
        bool standingOnSteepSlope = touching && onSteepSlope;

        // Horizontal movement, with full control on the ground and limited control in the air.
        Vector2 input = Vector2.ClampMagnitude(ReadMoveInput(), 1f);
        bool sprint = SprintHeld();
        float targetSpeed = exploreMode ? (sprint ? exploreSprintSpeed : exploreWalkSpeed) : (sprint ? sprintSpeed : walkSpeed);
        Vector3 targetVelocity = (transform.right * input.x + transform.forward * input.y) * targetSpeed;
        float acceleration = exploreMode ? exploreAcceleration : grounded ? groundAcceleration : airAcceleration;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, acceleration * dt);

        // Vertical movement.
        if (touching && verticalVelocity < 0f)
        {
            // Push down enough to follow the ground when walking downhill instead of hopping off it.
            float slopeDrop = horizontalVelocity.magnitude * Mathf.Tan(slopeLimit * Mathf.Deg2Rad);
            verticalVelocity = -(1f + slopeDrop);
        }

        if (grounded && !standingOnSteepSlope && JumpPressed())
        {
            verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            jumpedThisAir = true;
        }

        verticalVelocity -= gravity * dt;

        Vector3 slide = Vector3.zero;
        if (standingOnSteepSlope && slideOnSteepSlopes)
            slide = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized * slideSpeed;

        steepHitThisMove = false;
        groundNormalThisMove = Vector3.up;

        CollisionFlags flags = controller.Move((horizontalVelocity + slide + Vector3.up * verticalVelocity) * dt);

        // Walked off a bump or over a crest: pull back down onto the ground if it is close, so the low gravity
        // doesn't turn every bump into a floaty hop with reduced control.
        if (touching && !controller.isGrounded && !jumpedThisAir && verticalVelocity <= 0f && groundSnapDistance > 0f)
            SnapToGround();

        onSteepSlope = steepHitThisMove;
        groundNormal = groundNormalThisMove;

        // Stop rising when hitting a ceiling.
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;
    }

    void SnapToGround()
    {
        float radius = controller.radius * 0.9f;
        Vector3 bottom = transform.TransformPoint(controller.center) + Vector3.down * (controller.height * 0.5f - controller.radius);
        if (Physics.SphereCast(bottom + Vector3.up * 0.05f, radius, Vector3.down, out RaycastHit hit,
                               groundSnapDistance + controller.skinWidth + 0.05f, ~0, QueryTriggerInteraction.Ignore)
            && Vector3.Angle(Vector3.up, hit.normal) <= slopeLimit + 5f)
        {
            float drop = Mathf.Max(0f, hit.distance - 0.05f - controller.skinWidth * 0.5f);
            if (drop > 0.001f) controller.Move(Vector3.down * drop);
            verticalVelocity = -1f;
        }
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Only consider surfaces underneath the player, not walls hit while moving sideways.
        if (hit.moveDirection.y > -0.3f || hit.normal.y <= 0f)
            return;

        groundNormalThisMove = hit.normal;
        if (Vector3.Angle(Vector3.up, hit.normal) > slopeLimit + 0.5f)
            steepHitThisMove = true;
    }

    // ---------------------------------------------------------------- Input

#if ENABLE_INPUT_SYSTEM
    static Vector2 ReadMoveInput()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
            return Vector2.zero;

        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        return new Vector2(x, y);
    }

    static Vector2 ReadLookDelta()
    {
        Mouse mouse = Mouse.current;
        return mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
    }

    static bool SprintHeld()
    {
        Keyboard kb = Keyboard.current;
        return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
    }

    static bool JumpPressed()
    {
        Keyboard kb = Keyboard.current;
        return kb != null && kb.spaceKey.wasPressedThisFrame;
    }

    static bool UnlockPressed()
    {
        Keyboard kb = Keyboard.current;
        return kb != null && kb.escapeKey.wasPressedThisFrame;
    }

    static bool RelockPressed()
    {
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }
#else
    static Vector2 ReadMoveInput()
    {
        float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        return new Vector2(x, y);
    }

    static Vector2 ReadLookDelta() =>
        new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * LegacyMouseAxisToPixels;

    static bool SprintHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    static bool JumpPressed() => Input.GetKeyDown(KeyCode.Space);
    static bool UnlockPressed() => Input.GetKeyDown(KeyCode.Escape);
    static bool RelockPressed() => Input.GetMouseButtonDown(0);
#endif
}
