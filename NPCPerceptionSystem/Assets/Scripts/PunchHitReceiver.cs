using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class PunchHitReceiver : MonoBehaviour
{
    private enum ColorProp
    {
        None,
        BaseColor,
        Color
    }

    [Header("Knockback")]
    [SerializeField] private float knockbackDistance = 1.2f;
    [SerializeField] private float knockbackDuration = 0.12f;

    [Header("Face punch direction (after knockback)")]
    [Tooltip("After being hit, NPC turns toward the player / direction the punch came from.")]
    [SerializeField] private float faceTurnSpeed = 240f;
    [SerializeField] private float faceAngleThreshold = 1.5f;

    [Header("Visual")]
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float flashDurationSeconds = 0.1f;

    [Header("Crime")]
    [SerializeField] private bool reportAssaultToPolice = true;
    [SerializeField] private CrimeType reportedCrimeType = CrimeType.Assault;
    [SerializeField] private float crimeSoundRadius = 12f;
    [SerializeField] private bool drawPunchSoundRadius = true;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private NavMeshAgent navAgent;
    private Coroutine activeRoutine;
    private NPCPoliceCallCountdown policeCallCountdown;

    private void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        policeCallCountdown = GetComponent<NPCPoliceCallCountdown>();
        if (!policeCallCountdown)
        {
            policeCallCountdown = GetComponentInParent<NPCPoliceCallCountdown>();
        }
    }

    public void ReceivePunch(Vector3 attackerWorldPosition, Transform attacker)
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
        }

        activeRoutine = StartCoroutine(PunchFullSequence(attackerWorldPosition, attacker));
    }

    /// <summary>
    /// Full hit reaction: compute knockback, play movement + turn, then commit/report assault.
    /// </summary>
    private IEnumerator PunchFullSequence(Vector3 attackerWorldPosition, Transform attacker)
    {
        // Start with a horizontal knock direction so hits feel consistent on uneven ground.
        Vector3 knockbackDir = transform.position - attackerWorldPosition;
        knockbackDir.y = 0f;
        if (knockbackDir.sqrMagnitude < 0.0001f)
        {
            knockbackDir = -transform.forward;
            knockbackDir.y = 0f;
        }
        knockbackDir.Normalize();

        yield return PunchRoutine(knockbackDir);

        Vector3 puncherPosition = attacker ? attacker.position : attackerWorldPosition;
        yield return SmoothRotateTowardsHorizontal(puncherPosition);

        if (reportAssaultToPolice && attacker && CrimeManager.Instance != null)
        {
            CrimeEvent crime = new CrimeEvent(transform.position, reportedCrimeType, attacker, crimeSoundRadius, directPolicePursuit: true);
            CrimeManager.Instance.CommitCrime(crime);

            if (policeCallCountdown)
            {
                // Getting punched resets this witness timer instead of firing duplicate calls.
                policeCallCountdown.SchedulePoliceCall(crime, "Punched — timer reset");
            }
            else
            {
                CrimeManager.Instance.ReportCrime(crime, transform);
            }
        }

        activeRoutine = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPunchSoundRadius)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.45f, 0f, 0.95f);
        Gizmos.DrawWireSphere(transform.position, crimeSoundRadius);
    }

    private IEnumerator SmoothRotateTowardsHorizontal(Vector3 worldTarget)
    {
        Vector3 flat = worldTarget - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
        {
            yield break;
        }

        Quaternion targetRotation = Quaternion.LookRotation(flat.normalized);

        if (navAgent)
        {
            navAgent.isStopped = true;
        }

        try
        {
            while (Quaternion.Angle(transform.rotation, targetRotation) > faceAngleThreshold)
            {
                float maxStep = faceTurnSpeed * Time.deltaTime;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxStep);
                yield return null;
            }

            transform.rotation = targetRotation;
        }
        finally
        {
            if (navAgent)
            {
                navAgent.isStopped = false;
            }
        }
    }

    /// <summary>
    /// Applies manual knockback while NavMeshAgent is paused, then restores agent state safely.
    /// </summary>
    private IEnumerator PunchRoutine(Vector3 knockDirection)
    {
        Coroutine flashCo = StartCoroutine(FlashRedRoutine());

        if (navAgent)
        {
            // Temporarily disable NavMeshAgent so manual knockback movement doesn't fight pathfinding.
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        Vector3 start = transform.position;
        Vector3 end = start + knockDirection * knockbackDistance;
        float elapsed = 0f;

        while (elapsed < knockbackDuration)
        {
            elapsed += Time.deltaTime;
            float t = knockbackDuration > 0f ? Mathf.Clamp01(elapsed / knockbackDuration) : 1f;
            transform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }

        transform.position = end;

        if (navAgent)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
            {
                // Snap back to a valid NavMesh point before re-enabling the agent.
                transform.position = navHit.position;
            }

            navAgent.enabled = true;
            navAgent.Warp(transform.position);
            navAgent.isStopped = false;
        }

        yield return flashCo;
    }

    /// <summary>
    /// Temporarily overrides renderer color through property blocks and restores original values after flash.
    /// </summary>
    private IEnumerator FlashRedRoutine()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        var blocks = new MaterialPropertyBlock[renderers.Length];
        var prop = new ColorProp[renderers.Length];
        var originals = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            blocks[i] = new MaterialPropertyBlock();
            renderers[i].GetPropertyBlock(blocks[i]);

            Material mat = renderers[i].sharedMaterial;
            if (!mat)
            {
                prop[i] = ColorProp.None;
                continue;
            }

            if (mat.HasProperty(BaseColorId))
            {
                prop[i] = ColorProp.BaseColor;
                originals[i] = mat.GetColor(BaseColorId);
                blocks[i].SetColor(BaseColorId, hitColor);
            }
            else if (mat.HasProperty(ColorId))
            {
                prop[i] = ColorProp.Color;
                originals[i] = mat.GetColor(ColorId);
                blocks[i].SetColor(ColorId, hitColor);
            }
            else
            {
                prop[i] = ColorProp.None;
                continue;
            }

            renderers[i].SetPropertyBlock(blocks[i]);
        }

        yield return new WaitForSeconds(flashDurationSeconds);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i] || prop[i] == ColorProp.None)
            {
                continue;
            }

            if (prop[i] == ColorProp.BaseColor)
            {
                blocks[i].SetColor(BaseColorId, originals[i]);
            }
            else
            {
                blocks[i].SetColor(ColorId, originals[i]);
            }

            renderers[i].SetPropertyBlock(blocks[i]);
        }
    }
}
