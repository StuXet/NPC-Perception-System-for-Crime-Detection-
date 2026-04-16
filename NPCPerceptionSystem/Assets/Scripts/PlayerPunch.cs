using UnityEngine;

[DisallowMultipleComponent]
public class PlayerPunch : MonoBehaviour
{
    [Header("Raycast")]
    [SerializeField] private Camera punchCamera;
    [SerializeField] private float punchRange = 2.5f;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private bool drawPunchRayGizmo = true;

    [Header("Timing")]
    [SerializeField] private float punchCooldown = 0.35f;

    private float nextPunchTime;

    private void Awake()
    {
        if (!punchCamera)
        {
            punchCamera = GetComponentInChildren<Camera>();
        }
    }

    private void Update()
    {
        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        if (Time.time < nextPunchTime)
        {
            return;
        }

        if (!punchCamera)
        {
            return;
        }

        Ray ray = punchCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (drawPunchRayGizmo)
        {
            Debug.DrawRay(ray.origin, ray.direction * punchRange, new Color(1f, 0.2f, 1f), 1.25f, false);
        }

        if (!Physics.Raycast(ray, out RaycastHit hit, punchRange, hitMask, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        PunchHitReceiver receiver = hit.collider.GetComponentInParent<PunchHitReceiver>();
        if (!receiver)
        {
            return;
        }

        receiver.ReceivePunch(transform.position, transform);
        nextPunchTime = Time.time + punchCooldown;
    }

    private void OnDrawGizmos()
    {
        if (!drawPunchRayGizmo)
        {
            return;
        }

        Camera cam = punchCamera ? punchCamera : GetComponentInChildren<Camera>();
        if (!cam)
        {
            return;
        }

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Gizmos.color = new Color(1f, 0.35f, 1f, 0.9f);
        Gizmos.DrawRay(ray.origin, ray.direction * punchRange);
    }
}
