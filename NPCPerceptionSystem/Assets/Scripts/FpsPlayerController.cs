using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class FpsPlayerController : MonoBehaviour
{
    [Header("View")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private float mouseSensitivity = 2.0f;
    [SerializeField] private float maxPitch = 90f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5.0f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -20f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.25f;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundedStickVelocity = -2f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursorOnPlay = true;

    [Header("Debug")]
    [SerializeField] private bool drawGroundCheckGizmo = true;

    private CharacterController controller = null!;
    private float pitch;
    private Vector3 velocity;
    private bool isGrounded;

    private void Reset()
    {
        var cam = GetComponentInChildren<Camera>();
        cameraPivot = cam ? cam.transform : null;
        groundCheck = transform;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (!cameraPivot)
        {
            var cam = GetComponentInChildren<Camera>();
            cameraPivot = cam ? cam.transform : null;
        }

    }

    private void Start()
    {
        if (!cameraPivot)
        {
            Debug.LogError($"{nameof(FpsPlayerController)} on '{name}' needs a Camera child assigned to cameraPivot.");
            enabled = false;
            return;
        }

        if (!groundCheck) groundCheck = transform;

        Cursor.lockState = lockCursorOnPlay ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !lockCursorOnPlay;
    }

    private void Update()
    {
        GroundCheck();
        HandleLook();
        HandleMove();
    }

    private void GroundCheck()
    {
        var checkPos = groundCheck ? groundCheck.position : transform.position;
        var sphereGrounded = Physics.CheckSphere(checkPos, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore);
        isGrounded = sphereGrounded || controller.isGrounded;
    }

    private void HandleLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        var mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        var mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleMove()
    {
        // Walking (WASD)
        var inputX = Input.GetAxisRaw("Horizontal");
        var inputZ = Input.GetAxisRaw("Vertical");
        var input = Vector3.ClampMagnitude(new Vector3(inputX, 0f, inputZ), 1f);

        var move = transform.TransformDirection(input) * moveSpeed;
        controller.Move(move * Time.deltaTime);

        // Jump + gravity with explicit ground check
        if (isGrounded && velocity.y < 0f)
        {
            velocity.y = groundedStickVelocity;
        }

        if (isGrounded && Input.GetButtonDown("Jump"))
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    private void OnDrawGizmos()
    {
        if (!drawGroundCheckGizmo)
        {
            return;
        }

        Vector3 checkPos = groundCheck ? groundCheck.position : transform.position;
        Gizmos.color = new Color(0.25f, 0.95f, 1f, 0.5f);
        Gizmos.DrawWireSphere(checkPos, groundCheckRadius);
    }
}

