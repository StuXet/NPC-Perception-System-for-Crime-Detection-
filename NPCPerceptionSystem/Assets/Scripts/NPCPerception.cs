using System.Collections;
using UnityEngine;
using UnityEngine.AI;

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
    [SerializeField] private bool moveToHeardCrime = true;
    [SerializeField] private float hearingInvestigationDuration = 3f;
    [SerializeField] private float hearingMoveStopDistance = 1.5f;
    [SerializeField] private float hearingInvestigateRadius = 3f;
    [SerializeField] private float hearingInvestigatePointInterval = 0.8f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private bool drawGizmos = true;

    private float nextAllowedReportTime;
    private Coroutine hearingRoutine;
    private NavMeshAgent navAgent;

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

        navAgent = GetComponent<NavMeshAgent>();
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

        if (navAgent)
        {
            navAgent.isStopped = false;
        }
    }

    /// <summary>
    /// Main entry when a crime is committed: filter irrelevant events, report on sight, otherwise investigate by hearing.
    /// </summary>
    private void HandleCrimeCommitted(CrimeEvent committedCrime)
    {
        if (!target || CrimeManager.Instance == null)
        {
            return;
        }

        // Only react when the event is tied to the suspect this NPC is tracking.
        bool sameSuspect = committedCrime.suspect == target;
        bool suspectIsParent = committedCrime.suspect != null && target.IsChildOf(committedCrime.suspect);
        if (!sameSuspect && !suspectIsParent)
        {
            return;
        }

        bool usesCallDelay = TryGetComponent<NPCPoliceCallCountdown>(out _);
        if (!usesCallDelay && Time.time < nextAllowedReportTime)
        {
            return;
        }

        if (CanSeeTarget(target))
        {
            // Clean visual confirmation: call immediately.
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
        if (moveToHeardCrime && navAgent && navAgent.isOnNavMesh)
        {
            yield return MoveToHeardCrimeThenCheck(committedCrime);
        }
        else
        {
            yield return RotateAndCheckFromCurrentPosition(committedCrime);
        }

        hearingRoutine = null;
    }

    /// <summary>
    /// Moves toward the heard location and repeatedly checks if suspect becomes visible during investigation.
    /// </summary>
    private IEnumerator MoveToHeardCrimeThenCheck(CrimeEvent committedCrime)
    {
        navAgent.isStopped = false;
        navAgent.SetDestination(committedCrime.location);

        float endTime = Time.time + hearingInvestigationDuration;
        float nextInvestigatePointTime = Time.time;
        while (Time.time < endTime)
        {
            if (target && CanSeeTarget(target))
            {
                StopNavMovement();
                yield return RotateTowardsTargetBeforeReport(target);
                ReportWitnessedCrime(committedCrime, "Hearing->Move->Sight");
                yield break;
            }

            bool arrived = !navAgent.pathPending &&
                           (!navAgent.hasPath || navAgent.remainingDistance <= hearingMoveStopDistance);
            if (arrived)
            {
                // Once we reach the area, keep sampling nearby spots to mimic searching.
                if (Time.time >= nextInvestigatePointTime)
                {
                    SetRandomInvestigationPoint(committedCrime.location);
                    nextInvestigatePointTime = Time.time + hearingInvestigatePointInterval;
                }
            }

            yield return null;
        }

        yield return RotateAndCheckFromCurrentPosition(committedCrime);
    }

    /// <summary>
    /// Rotates toward the crime source, waits briefly, then performs a final sight check before giving up.
    /// </summary>
    private IEnumerator RotateAndCheckFromCurrentPosition(CrimeEvent committedCrime)
    {
        if (navAgent)
        {
            navAgent.isStopped = true;
        }

        try
        {
            // Flatten Y so we only rotate on the ground plane.
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

            bool usesDelay = TryGetComponent<NPCPoliceCallCountdown>(out _);
            if (!usesDelay && Time.time < nextAllowedReportTime)
            {
                // Another report just happened, so skip duplicate spam.
                yield break;
            }

            if (target && CanSeeTarget(target))
            {
                StopNavMovement();
                yield return RotateTowardsTargetBeforeReport(target);
                ReportWitnessedCrime(committedCrime, "Hearing->Look");
            }
            else if (debugLogs)
            {
                Debug.Log($"[NPCPerception] '{name}' heard {committedCrime.type}, investigated, but did not see suspect.");
            }
        }
        finally
        {
            if (navAgent)
            {
                navAgent.isStopped = false;
            }
        }
    }

    private void SetRandomInvestigationPoint(Vector3 center)
    {
        if (!navAgent || !navAgent.isOnNavMesh)
        {
            return;
        }

        Vector2 ring = Random.insideUnitCircle * hearingInvestigateRadius;
        Vector3 candidate = center + new Vector3(ring.x, 0f, ring.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, hearingInvestigateRadius, NavMesh.AllAreas))
        {
            navAgent.SetDestination(hit.position);
        }
    }

    private IEnumerator RotateTowardsTargetBeforeReport(Transform reportTarget)
    {
        if (!reportTarget)
        {
            yield break;
        }

        Vector3 flatDirection = reportTarget.position - transform.position;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude <= 0.0001f)
        {
            yield break;
        }

        if (navAgent)
        {
            navAgent.isStopped = true;
        }

        try
        {
            Quaternion targetRotation = Quaternion.LookRotation(flatDirection.normalized);
            while (Quaternion.Angle(transform.rotation, targetRotation) > 1f)
            {
                float step = lookTurnSpeed * Time.deltaTime;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, step);
                yield return null;
            }
        }
        finally
        {
            if (navAgent)
            {
                navAgent.isStopped = false;
            }
        }
    }

    private void StopNavMovement()
    {
        if (!navAgent || !navAgent.isOnNavMesh)
        {
            return;
        }

        navAgent.isStopped = true;
        navAgent.ResetPath();
    }

    /// <summary>
    /// Sends the crime report either immediately or through delayed call logic, depending on attached components.
    /// </summary>
    private void ReportWitnessedCrime(CrimeEvent committedCrime, string reason)
    {
        CrimeEvent crime = committedCrime;
        // Immediate line-of-sight reports should make police chase right away.
        crime.directPolicePursuit = reason == "Sight";

        if (TryGetComponent<NPCPoliceCallCountdown>(out NPCPoliceCallCountdown callDelay))
        {
            callDelay.SchedulePoliceCall(crime, reason);

            if (debugLogs)
            {
                Debug.Log($"[NPCPerception] '{name}' will call police for {crime.type} ({reason}) after delay.");
            }
        }
        else
        {
            CrimeManager.Instance.ReportCrime(crime, transform);
            nextAllowedReportTime = Time.time + reportCooldown;

            if (debugLogs)
            {
                Debug.Log($"[NPCPerception] '{name}' reported {crime.type} ({reason}).");
            }
        }
    }

    public void NotifyReportSentToPolice()
    {
        nextAllowedReportTime = Time.time + reportCooldown;
    }

    /// <summary>
    /// Returns true only when target is in range, inside FOV, and not blocked by line-of-sight obstruction.
    /// </summary>
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
