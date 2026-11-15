using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/*
    This script provides jumping and movement in Unity 3D - Gatsby
    Rewritten for Unity 6 and the new Input System package.
*/

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class Player : MonoBehaviour
{
    // Camera Rotation
    public float mouseSensitivity = 2f;
    private const float MouseScale = 0.1f; // Converts raw mouse pixels to roughly the old GetAxis feel
    private float verticalRotation = 0f;
    private float yaw;
    private Transform cameraTransform;

    // Ground Movement
    private Rigidbody rb;
    public float MoveSpeed = 5f;
    public float SprintSpeed = 7f;
    private Vector2 moveInput;

    // Ball spawning
    public GameObject ball;
    public float ballSpawnDistance = 2f;
    private List<GameObject> spawnedBalls = new List<GameObject>();

    // Jumping
    public float jumpForce = 10f;
    public float fallMultiplier = 3.5f;   // Total gravity multiplier while falling
    public float ascendMultiplier = 3f;   // Total gravity multiplier while rising
    public LayerMask groundLayer;
    private bool isGrounded = true;
    public bool IsGrounded => isGrounded;
    private bool isSprinting = false;
    public bool IsSprinting => isSprinting;
    private bool jumpRequested;
    private float groundCheckTimer = 0f;
    private float groundCheckDelay = 0.3f;
    private float raycastDistance;

    // Input
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction sprintAction;
    private InputAction spawnBallAction;
    private InputAction clearBallsAction;

    void Awake()
    {
        // Move: WASD + arrow keys + gamepad left stick
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick");

        // Look: mouse delta
        lookAction = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");

        // Jump: space + gamepad south button
        jumpAction = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");

        // Sprint: left shift + gamepad left stick click
        sprintAction = new InputAction("Sprint", InputActionType.Button);
        sprintAction.AddBinding("<Keyboard>/leftShift");
        sprintAction.AddBinding("<Gamepad>/leftStickPress");

        // Spawn ball: left click + gamepad west button
        spawnBallAction = new InputAction("SpawnBall", InputActionType.Button);
        spawnBallAction.AddBinding("<Mouse>/leftButton");
        spawnBallAction.AddBinding("<Gamepad>/buttonWest");

        // Clear balls: right click + gamepad east button
        clearBallsAction = new InputAction("ClearBalls", InputActionType.Button);
        clearBallsAction.AddBinding("<Mouse>/rightButton");
        clearBallsAction.AddBinding("<Gamepad>/buttonEast");
    }

    void OnEnable()
    {
        moveAction.Enable();
        lookAction.Enable();
        jumpAction.Enable();
        sprintAction.Enable();
        spawnBallAction.Enable();
        clearBallsAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable();
        lookAction.Disable();
        jumpAction.Disable();
        sprintAction.Disable();
        spawnBallAction.Disable();
        clearBallsAction.Disable();
    }

    void OnDestroy()
    {
        moveAction.Dispose();
        lookAction.Dispose();
        jumpAction.Dispose();
        sprintAction.Dispose();
        spawnBallAction.Dispose();
        clearBallsAction.Dispose();
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        yaw = transform.eulerAngles.y; // Start from the current facing so the player doesn't snap to 0
        cameraTransform = Camera.main.transform;

        // Set the raycast to be slightly beneath the player's feet
        float playerHeight = GetComponent<CapsuleCollider>().height * transform.localScale.y;
        raycastDistance = (playerHeight / 2) + 0.2f;

        // Hides the mouse
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        moveInput = moveAction.ReadValue<Vector2>();

        RotateCamera();

        if (jumpAction.WasPressedThisFrame())
        {
            jumpRequested = true;
        }

        if (spawnBallAction.WasPressedThisFrame())
        {
            SpawnBall();
        }

        if (clearBallsAction.WasPressedThisFrame())
        {
            DeleteAllBalls();
        }
    }

    void FixedUpdate()
    {
        // Body rotation is applied in the physics step (smoothed by interpolation)
        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));

        CheckGround();

        if (jumpRequested && isGrounded)
        {
            Jump();
        }
        jumpRequested = false;

        MovePlayer();
        ApplyJumpPhysics();
    }

    void SpawnBall()
    {
        if (ball == null) return;

        // In front of the camera, in the exact direction the player is looking
        Vector3 spawnPosition = cameraTransform.position + cameraTransform.forward * ballSpawnDistance;

        GameObject newBall = Instantiate(ball, spawnPosition, Quaternion.identity);
        newBall.SetActive(true);

        // Reset any inherited motion so the copy doesn't launch
        Rigidbody ballRb = newBall.GetComponent<Rigidbody>();
        if (ballRb != null)
        {
            ballRb.linearVelocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;
        }

        spawnedBalls.Add(newBall);
    }

    void DeleteAllBalls()
    {
        foreach (GameObject b in spawnedBalls)
        {
            if (b != null) Destroy(b); // null check in case a ball was already destroyed elsewhere
        }
        spawnedBalls.Clear();
    }

    void CheckGround()
    {
        // Skip the check briefly after jumping so we don't re-detect the ground we just left
        if (groundCheckTimer > 0f)
        {
            groundCheckTimer -= Time.fixedDeltaTime;
            return;
        }

        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
        isGrounded = Physics.Raycast(rayOrigin, Vector3.down, raycastDistance, groundLayer);
    }

    void MovePlayer()
    {
        // Only change sprint state on the ground, so you can't change speed mid-air.
        // Sprint only works while moving forward.
        if (isGrounded)
        {
            isSprinting = sprintAction.IsPressed() && moveInput.y > 0.1f;
        }

        float currentSpeed = isSprinting ? SprintSpeed : MoveSpeed;

        // ClampMagnitude (instead of normalized) keeps analog stick sensitivity
        Vector3 movement = Vector3.ClampMagnitude(
            transform.right * moveInput.x + transform.forward * moveInput.y, 1f);

        Vector3 velocity = rb.linearVelocity;
        velocity.x = movement.x * currentSpeed;
        velocity.z = movement.z * currentSpeed;
        rb.linearVelocity = velocity;
    }

    void RotateCamera()
    {
        Vector2 look = lookAction.ReadValue<Vector2>() * mouseSensitivity * MouseScale;

        yaw += look.x; // Applied to the body in FixedUpdate

        verticalRotation -= look.y;
        verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);

        cameraTransform.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
    }

    void Jump()
    {
        isGrounded = false;
        groundCheckTimer = groundCheckDelay;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
    }

    void ApplyJumpPhysics()
    {
        float y = rb.linearVelocity.y;
        if (Mathf.Approximately(y, 0f)) return;

        // Unity already applies 1x gravity, so we only add the extra (multiplier - 1)
        float multiplier = y < 0f ? fallMultiplier : ascendMultiplier;
        rb.linearVelocity += Vector3.up * Physics.gravity.y * (multiplier - 1f) * Time.fixedDeltaTime;
    }
}