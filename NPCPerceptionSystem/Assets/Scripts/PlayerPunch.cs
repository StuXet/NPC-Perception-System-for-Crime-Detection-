using UnityEngine;

[DisallowMultipleComponent]
public class PlayerPunch : MonoBehaviour
{
    [Header("Raycast")]
    [Tooltip("Camera the punch ray is cast from (screen center). Auto-found in children if empty.")]
    [SerializeField] private Camera punchCamera;
    [SerializeField, Min(0.1f)] private float punchRange = 2.5f;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private bool drawPunchRayGizmo = true;

    [Header("Timing")]
    [Tooltip("Seconds between punches that connect.")]
    [SerializeField, Min(0f)] private float punchCooldown = 0.35f;

    [Header("Damage")]
    [Tooltip("Damage per punch. 100 HP NPCs die on the 4th hit at 25.")]
    [SerializeField, Min(0f)] private float punchDamage = 25f;

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

        bool anyPunchEffect = false;

        ActorHealth health = hit.collider.GetComponentInParent<ActorHealth>();
        if (health)
        {
            health.TakeDamage(punchDamage, gameObject);
            anyPunchEffect = true;
        }

        PunchHitReceiver receiver = hit.collider.GetComponentInParent<PunchHitReceiver>();
        if (receiver && !(health && health.IsDead))
        {
            receiver.ReceivePunch(transform.position, transform);
            anyPunchEffect = true;
        }

        if (!anyPunchEffect)
        {
            return;
        }

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
