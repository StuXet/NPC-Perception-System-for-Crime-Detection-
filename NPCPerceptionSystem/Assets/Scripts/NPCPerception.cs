using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class NPCPerception : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Sight")]
    [SerializeField] private Transform eyePoint;
    [SerializeField] private float viewDistance = 12f;
    [SerializeField, Range(1f, 180f)] private float viewAngle = 90f;
    [SerializeField] private LayerMask obstructionMask = ~0;
    [SerializeField] private bool useLineOfSight = true;

    [Header("Reporting")]
    [SerializeField] private float reportCooldown = 1.5f;
    [SerializeField] private float hearingDistance = 10f;
    [SerializeField] private float lookTurnSpeed = 240f;
    [SerializeField] private float lookCheckDelay = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private bool drawGizmos = true;

    private float nextAllowedReportTime;
    private Coroutine hearingRoutine;

    private void Reset()
    {
        eyePoint = transform;
    }

    private void Awake()
    {
        if (!eyePoint)
        {
            eyePoint = transform;
        }
    }

    private void OnEnable()
    {
        CrimeManager.OnCrimeCommitted += HandleCrimeCommitted;
    }

    private void OnDisable()
    {
        CrimeManager.OnCrimeCommitted -= HandleCrimeCommitted;

        if (hearingRoutine != null)
        {
            StopCoroutine(hearingRoutine);
            hearingRoutine = null;
        }
    }

    private void HandleCrimeCommitted(CrimeEvent committedCrime)
    {
        if (!target || CrimeManager.Instance == null)
        {
            return;
        }

        bool sameSuspect = committedCrime.suspect == target;
        bool suspectIsParent = committedCrime.suspect != null && target.IsChildOf(committedCrime.suspect);
        if (!sameSuspect && !suspectIsParent)
        {
            return;
        }

        if (Time.time < nextAllowedReportTime)
        {
            return;
        }

        if (CanSeeTarget(target))
        {
            ReportWitnessedCrime(committedCrime, "Sight");
            return;
        }

        if (!CanHearCrime(committedCrime))
        {
            return;
        }

        if (hearingRoutine != null)
        {
            StopCoroutine(hearingRoutine);
        }
        hearingRoutine = StartCoroutine(LookTowardCrimeThenCheck(committedCrime));
    }

    private bool CanHearCrime(CrimeEvent committedCrime)
    {
        Vector3 earPosition = eyePoint ? eyePoint.position : transform.position;
        float distanceToCrime = Vector3.Distance(earPosition, committedCrime.location);
        float audibleDistance = Mathf.Max(hearingDistance, committedCrime.soundRadius);
        return distanceToCrime <= audibleDistance;
    }

    private IEnumerator LookTowardCrimeThenCheck(CrimeEvent committedCrime)
    {
        Vector3 flatDirection = committedCrime.location - transform.position;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(flatDirection.normalized);
            float maxStepPerFrame = lookTurnSpeed * Time.deltaTime;

            while (Quaternion.Angle(transform.rotation, targetRotation) > 1f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxStepPerFrame);
                yield return null;
                maxStepPerFrame = lookTurnSpeed * Time.deltaTime;
            }
        }

        if (lookCheckDelay > 0f)
        {
            yield return new WaitForSeconds(lookCheckDelay);
        }

        if (Time.time < nextAllowedReportTime)
        {
            hearingRoutine = null;
            yield break;
        }

        if (target && CanSeeTarget(target))
        {
            ReportWitnessedCrime(committedCrime, "Hearing->Look");
        }
        else if (debugLogs)
        {
            Debug.Log($"[NPCPerception] '{name}' heard {committedCrime.type}, looked, but did not see suspect.");
        }

        hearingRoutine = null;
    }

    private void ReportWitnessedCrime(CrimeEvent committedCrime, string reason)
    {
        CrimeManager.Instance.ReportCrime(committedCrime, transform);
        nextAllowedReportTime = Time.time + reportCooldown;

        if (debugLogs)
        {
            Debug.Log($"[NPCPerception] '{name}' reported {committedCrime.type} ({reason}).");
        }
    }

    private bool CanSeeTarget(Transform targetTransform)
    {
        Vector3 eye = eyePoint ? eyePoint.position : transform.position;
        Vector3 toTarget = targetTransform.position - eye;
        float sqrDistance = toTarget.sqrMagnitude;
        float maxSqrDistance = viewDistance * viewDistance;

        if (sqrDistance > maxSqrDistance)
        {
            return false;
        }

        Vector3 directionToTarget = toTarget.normalized;
        float halfFov = viewAngle * 0.5f;
        float angle = Vector3.Angle(transform.forward, directionToTarget);
        if (angle > halfFov)
        {
            return false;
        }

        if (!useLineOfSight)
        {
            return true;
        }

        float distance = Mathf.Sqrt(sqrDistance);
        if (Physics.Raycast(eye, directionToTarget, out RaycastHit hit, distance, obstructionMask, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == targetTransform || hit.transform.IsChildOf(targetTransform);
        }

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Vector3 eye = eyePoint ? eyePoint.position : transform.position;
        float halfFov = viewAngle * 0.5f;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(eye, hearingDistance);

        Vector3 leftBoundary = Quaternion.Euler(0f, -halfFov, 0f) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0f, halfFov, 0f) * transform.forward;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(eye, eye + leftBoundary * viewDistance);
        Gizmos.DrawLine(eye, eye + rightBoundary * viewDistance);

        if (target)
        {
            Gizmos.color = CanSeeTarget(target) ? Color.green : Color.red;
            Gizmos.DrawLine(eye, target.position);
        }
    }
}
