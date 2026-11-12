using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum PerceptionActivity
{
    Idle,
    InvestigatingNoise,
    InvestigatingBody,
    WaitingToReport
}

/// <summary>
/// Witness perception: sight (range + FOV + line of sight) and hearing (radius).
/// Seen crimes are reported straight away (with the suspect's last known position),
/// heard crimes are investigated first, and bodies are discovered by periodic scanning.
/// </summary>
[DisallowMultipleComponent]
public class NPCPerception : MonoBehaviour
{
    private const float RotationStopAngle = 1f;
    private const float MinFlatDirectionSqrMagnitude = 0.0001f;

    private static readonly List<NPCPerception> all = new List<NPCPerception>();

    /// <summary>All active perceiving NPCs (used by the debug HUD).</summary>
    public static IReadOnlyList<NPCPerception> All => all;

    [Header("Target")]
    [Tooltip("The suspect this NPC can recognise (normally the player). Leave empty to auto-find the 'Player' tag.")]
    [SerializeField] private Transform target;
    [Tooltip("If Target is empty, find the GameObject tagged 'Player' on start.")]
    [SerializeField] private bool autoFindPlayerTarget = true;

    [Header("Sight")]
    [Tooltip("Where sight and hearing are measured from. Defaults to this transform.")]
    [SerializeField] private Transform eyePoint;
    [SerializeField, Min(0f)] private float viewDistance = 12f;
    [SerializeField, Range(1f, 360f)] private float viewAngle = 90f;
    [Tooltip("Layers that block line of sight.")]
    [SerializeField] private LayerMask obstructionMask = ~0;
    [SerializeField] private bool useLineOfSight = true;

    [Header("Hearing")]
    [Tooltip("Base hearing radius. A crime is heard if within max(this, the crime's sound radius).")]
    [SerializeField, Min(0f)] private float hearingDistance = 10f;

    [Header("Reporting")]
    [Tooltip("Minimum seconds between two reports (only used without NPCPoliceCallCountdown).")]
    [SerializeField, Min(0f)] private float reportCooldown = 1.5f;
    [Tooltip("If the witness heard the crime and then saw the suspect while investigating, police get a direct sighting too.")]
    [SerializeField] private bool heardThenSeenCountsAsSighting = true;

    [Header("Investigation")]
    [Tooltip("Degrees per second when turning toward a noise or suspect.")]
    [SerializeField, Min(1f)] private float lookTurnSpeed = 240f;
    [Tooltip("Pause after turning before the final sight check.")]
    [SerializeField, Min(0f)] private float lookCheckDelay = 0.35f;
    [Tooltip("Walk to heard crimes. If off, the NPC only turns toward the sound.")]
    [SerializeField] private bool moveToHeardCrime = true;
    [SerializeField, Min(0f)] private float hearingInvestigationDuration = 3f;
    [SerializeField, Min(0f)] private float hearingMoveStopDistance = 1.5f;
    [Tooltip("Radius around the noise where random search points are picked.")]
    [SerializeField, Min(0f)] private float hearingInvestigateRadius = 3f;
    [SerializeField, Min(0.05f)] private float hearingInvestigatePointInterval = 0.8f;

    [Header("Dead Bodies")]
    [Tooltip("Height added above the body's collider center for the sight check.")]
    [SerializeField] private float deadBodySightHeightOffset = 0.8f;
    [Tooltip("Within this distance the body is confirmed even without a clean sight line.")]
    [SerializeField, Min(0f)] private float deadBodyConfirmDistance = 2.0f;
    [Tooltip("Periodically look for bodies in view (discovers crimes nobody witnessed).")]
    [SerializeField] private bool enableDeadBodyAutoScan = true;
    [SerializeField, Min(0.1f)] private float deadBodyScanInterval = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private GizmoDrawMode gizmoMode = GizmoDrawMode.Selected;
    [SerializeField] private bool drawNavPath = true;

    [Header("Runtime (read only)")]
    [ReadOnly, SerializeField] private PerceptionActivity activity;
    [ReadOnly, SerializeField] private DetectionReason lastDetection;
    [ReadOnly, SerializeField] private bool canSeeTargetNow;
    [ReadOnly, SerializeField] private int bodiesReported;

    private float nextAllowedReportTime;
    private float nextDeadBodyScanTime;
    private Coroutine hearingRoutine;
    private NavMeshAgent navAgent;
    private NPCPoliceCallCountdown callCountdown;
    private bool holdPositionUntilReportSent;
    private bool hasInvestigationPoint;
    private Vector3 investigationPoint;
    private Vector3 lastNoisePosition;
    private float lastNoiseTime = -100f;

    // Body -> death count when we reported it. A revived and re-killed actor has a new death count, so it can be found again.
    private readonly Dictionary<ActorHealth, int> reportedBodies = new Dictionary<ActorHealth, int>();

    public Transform Target => target;
    public NPCPoliceCallCountdown CallCountdown => callCountdown;
    public float ViewDistance => viewDistance;
    public float HearingDistance => hearingDistance;
    public Vector3 EyePosition => eyePoint ? eyePoint.position : transform.position;

    public PerceptionActivity Activity
    {
        get
        {
            if (holdPositionUntilReportSent || (callCountdown && callCountdown.IsCalling))
            {
                return PerceptionActivity.WaitingToReport;
            }

            return activity == PerceptionActivity.WaitingToReport ? PerceptionActivity.Idle : activity;
        }
    }

    /// <summary>True while investigating or waiting to report; other movement (wandering) should pause.</summary>
    public bool IsBusy => hearingRoutine != null || holdPositionUntilReportSent;

    private bool AgentReady => navAgent && navAgent.enabled && navAgent.isOnNavMesh;

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
        callCountdown = GetComponent<NPCPoliceCallCountdown>();
        TryAutoFindTarget();
    }

    private void OnEnable()
    {
        all.Add(this);
        CrimeManager.OnCrimeCommitted += HandleCrimeCommitted;

        // Stagger scans so NPCs don't all run their body scan on the same frame.
        nextDeadBodyScanTime = Time.time + Random.Range(0f, deadBodyScanInterval);
    }

    private void OnDisable()
    {
        all.Remove(this);
        CrimeManager.OnCrimeCommitted -= HandleCrimeCommitted;

        StopHearingRoutine();
        SetAgentStopped(false);
        holdPositionUntilReportSent = false;
        activity = PerceptionActivity.Idle;
    }

    private void TryAutoFindTarget()
    {
        if (target || !autoFindPlayerTarget)
        {
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player)
        {
            target = player.transform;
        }
    }

    /// <summary>
    /// Main entry when a crime is committed: filter irrelevant events, report on sight, otherwise investigate by hearing.
    /// </summary>
    private void HandleCrimeCommitted(CrimeEvent committedCrime)
    {
        if (holdPositionUntilReportSent || CrimeManager.Instance == null)
        {
            return;
        }

        // Victims report through their own hit reaction; criminals don't report themselves.
        if (PerceptionUtility.IsSameOrChild(transform, committedCrime.victim) ||
            PerceptionUtility.IsSameOrChild(transform, committedCrime.suspect))
        {
            return;
        }

        // Cheap distance reject before any raycasts.
        float audibleDistance = Mathf.Max(hearingDistance, committedCrime.soundRadius);
        float relevantDistance = Mathf.Max(audibleDistance, viewDistance + 3f);
        if ((EyePosition - committedCrime.location).sqrMagnitude > relevantDistance * relevantDistance)
        {
            return;
        }

        if (committedCrime.IsBodyCrime)
        {
            HandleBodyCrime(committedCrime);
            return;
        }

        if (!target || !IsTrackedSuspect(committedCrime.suspect))
        {
            return;
        }

        bool usesCallDelay = callCountdown;
        if (!usesCallDelay && Time.time < nextAllowedReportTime)
        {
            return;
        }

        if (CanSeeTarget(target))
        {
            // Clean visual confirmation: call immediately.
            ReportWitnessedCrime(committedCrime, DetectionReason.Sight);
            return;
        }

        if (!CanHearCrime(committedCrime))
        {
            return;
        }

        StartHearingRoutine(LookTowardCrimeThenCheck(committedCrime), PerceptionActivity.InvestigatingNoise, committedCrime.location);
    }

    private void HandleBodyCrime(CrimeEvent committedCrime)
    {
        // Witnessing the killer is the strongest evidence: police get a direct sighting.
        if (committedCrime.type == CrimeType.Murder && target && IsTrackedSuspect(committedCrime.suspect) && CanSeeTarget(target))
        {
            ReportWitnessedCrime(committedCrime, DetectionReason.Sight);
            return;
        }

        if (CanSeePoint(GetDeadBodySightPoint(committedCrime), committedCrime.victim))
        {
            ReportWitnessedCrime(committedCrime, DetectionReason.DeadBodySight);
            return;
        }

        if (CanHearCrime(committedCrime))
        {
            StartHearingRoutine(InvestigateDeadBodyThenCheck(committedCrime), PerceptionActivity.InvestigatingBody, committedCrime.location);
        }
    }

    private void Update()
    {
        activity = Activity;
        canSeeTargetNow = target && CanSeeTarget(target);

        if (holdPositionUntilReportSent || !enableDeadBodyAutoScan || Time.time < nextDeadBodyScanTime)
        {
            return;
        }

        nextDeadBodyScanTime = Time.time + Mathf.Max(0.1f, deadBodyScanInterval);
        ScanForVisibleDeadBodies();
    }

    /// <summary>
    /// Used by the call countdown right before the call goes out: if the witness can still see the suspect,
    /// the report gets a fresh last known position.
    /// </summary>
    public bool TryRefreshSuspectSighting(ref CrimeEvent crime)
    {
        if (!isActiveAndEnabled || !target || !IsTrackedSuspect(crime.suspect) || !CanSeeTarget(target))
        {
            return false;
        }

        crime.SetSuspectSighting(crime.suspect.position);
        crime.directPolicePursuit = true;
        return true;
    }

    private bool IsTrackedSuspect(Transform suspect)
    {
        return suspect && target && (suspect == target || target.IsChildOf(suspect));
    }

    private bool CanHearCrime(CrimeEvent committedCrime)
    {
        float audibleDistance = Mathf.Max(hearingDistance, committedCrime.soundRadius);
        return (EyePosition - committedCrime.location).sqrMagnitude <= audibleDistance * audibleDistance;
    }

    private void StartHearingRoutine(IEnumerator routine, PerceptionActivity newActivity, Vector3 noisePosition)
    {
        StopHearingRoutine();
        activity = newActivity;
        lastNoisePosition = noisePosition;
        lastNoiseTime = Time.time;
        hearingRoutine = StartCoroutine(RunHearingRoutine(routine));
    }

    private IEnumerator RunHearingRoutine(IEnumerator routine)
    {
        yield return routine;
        hearingRoutine = null;
        hasInvestigationPoint = false;
        if (activity != PerceptionActivity.WaitingToReport)
        {
            activity = PerceptionActivity.Idle;
        }
    }

    private void StopHearingRoutine()
    {
        if (hearingRoutine != null)
        {
            StopCoroutine(hearingRoutine);
            hearingRoutine = null;
        }

        hasInvestigationPoint = false;
    }

    private IEnumerator LookTowardCrimeThenCheck(CrimeEvent committedCrime)
    {
        if (moveToHeardCrime && AgentReady)
        {
            yield return MoveToHeardCrimeThenCheck(committedCrime);
        }
        else
        {
            yield return RotateAndCheckFromCurrentPosition(committedCrime);
        }
    }

    /// <summary>
    /// Moves toward the heard location and repeatedly checks if suspect becomes visible during investigation.
    /// </summary>
    private IEnumerator MoveToHeardCrimeThenCheck(CrimeEvent committedCrime)
    {
        SetDestination(committedCrime.location);

        float endTime = Time.time + hearingInvestigationDuration;
        float nextInvestigatePointTime = Time.time;
        while (Time.time < endTime)
        {
            if (target && CanSeeTarget(target))
            {
                StopNavMovement();
                yield return RotateTowardsPointBeforeReport(target.position);
                ReportWitnessedCrime(committedCrime, DetectionReason.HeardThenSaw);
                yield break;
            }

            // Once we reach the area, keep sampling nearby spots to mimic searching.
            if (HasReachedHearingStopDistance() && Time.time >= nextInvestigatePointTime)
            {
                SetRandomInvestigationPoint(committedCrime.location);
                nextInvestigatePointTime = Time.time + hearingInvestigatePointInterval;
            }

            yield return null;
        }

        yield return RotateAndCheckFromCurrentPosition(committedCrime);
    }

    private IEnumerator InvestigateDeadBodyThenCheck(CrimeEvent committedCrime)
    {
        if (moveToHeardCrime && AgentReady)
        {
            SetDestination(committedCrime.location);

            float endTime = Time.time + hearingInvestigationDuration;
            float nextInvestigatePointTime = Time.time;

            while (Time.time < endTime)
            {
                // Spotting the killer while walking over beats finding the body.
                if (committedCrime.type == CrimeType.Murder && target && IsTrackedSuspect(committedCrime.suspect) && CanSeeTarget(target))
                {
                    StopNavMovement();
                    yield return RotateTowardsPointBeforeReport(target.position);
                    ReportWitnessedCrime(committedCrime, DetectionReason.HeardThenSaw);
                    yield break;
                }

                if (CanConfirmBody(committedCrime))
                {
                    StopNavMovement();
                    yield return RotateTowardsPointBeforeReport(GetDeadBodySightPoint(committedCrime));
                    ReportWitnessedCrime(committedCrime, DetectionReason.DeadBodyInvestigate);
                    yield break;
                }

                if (HasReachedHearingStopDistance() && Time.time >= nextInvestigatePointTime)
                {
                    SetRandomInvestigationPoint(committedCrime.location);
                    nextInvestigatePointTime = Time.time + hearingInvestigatePointInterval;
                }

                yield return null;
            }
        }
        else
        {
            yield return RotateTowardsPointBeforeReport(GetDeadBodySightPoint(committedCrime));
        }

        if (CanConfirmBody(committedCrime))
        {
            ReportWitnessedCrime(committedCrime, DetectionReason.DeadBodyInvestigate);
        }
        else if (debugLogs)
        {
            Debug.Log($"[NPCPerception] '{name}' investigated a body scene but did not confirm it visually.", this);
        }
    }

    private bool CanConfirmBody(CrimeEvent committedCrime)
    {
        if (CanSeePoint(GetDeadBodySightPoint(committedCrime), committedCrime.victim))
        {
            return true;
        }

        Vector3 bodyPos = committedCrime.victim ? committedCrime.victim.position : committedCrime.location;
        return (transform.position - bodyPos).sqrMagnitude <= deadBodyConfirmDistance * deadBodyConfirmDistance;
    }

    private Vector3 GetDeadBodySightPoint(CrimeEvent committedCrime)
    {
        Vector3 basePos = committedCrime.victim ? PerceptionUtility.GetAimPoint(committedCrime.victim) : committedCrime.location;
        return basePos + Vector3.up * deadBodySightHeightOffset;
    }

    /// <summary>
    /// Rotates toward the crime source, waits briefly, then performs a final sight check before giving up.
    /// </summary>
    private IEnumerator RotateAndCheckFromCurrentPosition(CrimeEvent committedCrime)
    {
        SetAgentStopped(true);

        try
        {
            // Flatten Y so we only rotate on the ground plane.
            yield return RotateTowardsFlatDirection(GetFlatDirectionFromTransformTo(committedCrime.location));

            if (lookCheckDelay > 0f)
            {
                yield return new WaitForSeconds(lookCheckDelay);
            }

            if (!callCountdown && Time.time < nextAllowedReportTime)
            {
                // Another report just happened, so skip duplicate spam.
                yield break;
            }

            if (target && CanSeeTarget(target))
            {
                StopNavMovement();
                yield return RotateTowardsPointBeforeReport(target.position);
                ReportWitnessedCrime(committedCrime, DetectionReason.HeardThenLooked);
            }
            else if (debugLogs)
            {
                Debug.Log($"[NPCPerception] '{name}' heard {committedCrime.type}, investigated, but did not see the suspect.", this);
            }
        }
        finally
        {
            SetAgentStopped(false);
        }
    }

    private void SetRandomInvestigationPoint(Vector3 center)
    {
        if (!AgentReady)
        {
            return;
        }

        Vector2 ring = Random.insideUnitCircle * hearingInvestigateRadius;
        Vector3 candidate = center + new Vector3(ring.x, 0f, ring.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, Mathf.Max(0.5f, hearingInvestigateRadius), NavMesh.AllAreas))
        {
            SetDestination(hit.position);
        }
    }

    private IEnumerator RotateTowardsPointBeforeReport(Vector3 reportPoint)
    {
        Vector3 flatDirection = GetFlatDirectionFromTransformTo(reportPoint);
        if (flatDirection.sqrMagnitude <= MinFlatDirectionSqrMagnitude)
        {
            yield break;
        }

        SetAgentStopped(true);
        try
        {
            yield return RotateTowardsFlatDirection(flatDirection);
        }
        finally
        {
            SetAgentStopped(false);
        }
    }

    /// <summary>
    /// Sends the crime report either immediately or through delayed call logic, depending on attached components.
    /// </summary>
    private void ReportWitnessedCrime(CrimeEvent committedCrime, DetectionReason reason)
    {
        CrimeEvent crime = committedCrime;
        crime.reason = reason;
        lastDetection = reason;

        bool sawSuspect = reason == DetectionReason.Sight ||
                          (heardThenSeenCountsAsSighting &&
                           (reason == DetectionReason.HeardThenSaw || reason == DetectionReason.HeardThenLooked));

        // Only a real sighting lets police head straight for the suspect.
        crime.directPolicePursuit = sawSuspect && crime.suspect;
        if (crime.directPolicePursuit)
        {
            crime.SetSuspectSighting(crime.suspect.position);
        }

        MarkBodyReported(crime.victim);

        bool holdForBodyReport = crime.IsBodyCrime;
        if (holdForBodyReport)
        {
            BeginHoldAtSceneForReport();
        }

        if (callCountdown && callCountdown.isActiveAndEnabled)
        {
            callCountdown.SchedulePoliceCall(crime, reason.ToString());

            if (debugLogs)
            {
                Debug.Log($"[NPCPerception] '{name}' will call police for {crime.type} ({reason}) after delay.", this);
            }
        }
        else
        {
            CrimeManager.Instance.ReportCrime(crime, transform);
            nextAllowedReportTime = Time.time + reportCooldown;
            if (holdForBodyReport)
            {
                EndHoldAtSceneAfterReport();
            }

            if (debugLogs)
            {
                Debug.Log($"[NPCPerception] '{name}' reported {crime.type} ({reason}).", this);
            }
        }
    }

    public void NotifyReportSentToPolice()
    {
        nextAllowedReportTime = Time.time + reportCooldown;

        if (holdPositionUntilReportSent)
        {
            EndHoldAtSceneAfterReport();
        }
    }

    private void MarkBodyReported(Transform victim)
    {
        if (!victim)
        {
            return;
        }

        ActorHealth health = victim.GetComponentInParent<ActorHealth>();
        if (health && health.IsDead)
        {
            reportedBodies[health] = health.DeathCount;
            bodiesReported = reportedBodies.Count;
        }
    }

    private bool IsBodyAlreadyReported(ActorHealth health)
    {
        return reportedBodies.TryGetValue(health, out int deathCount) && deathCount == health.DeathCount;
    }

    private void BeginHoldAtSceneForReport()
    {
        StopHearingRoutine();
        holdPositionUntilReportSent = true;
        activity = PerceptionActivity.WaitingToReport;
        StopNavMovement();
    }

    private void EndHoldAtSceneAfterReport()
    {
        holdPositionUntilReportSent = false;
        activity = PerceptionActivity.Idle;
        SetAgentStopped(false);
    }

    /// <summary>True only when the target is in range, inside the FOV and not blocked.</summary>
    private bool CanSeeTarget(Transform targetTransform)
    {
        return PerceptionUtility.CanSee(
            EyePosition, transform.forward, PerceptionUtility.GetAimPoint(targetTransform),
            viewDistance, viewAngle, useLineOfSight, obstructionMask, transform, targetTransform);
    }

    private bool CanSeePoint(Vector3 worldPoint, Transform allowedHit = null)
    {
        return PerceptionUtility.CanSee(
            EyePosition, transform.forward, worldPoint,
            viewDistance, viewAngle, useLineOfSight, obstructionMask, transform, allowedHit);
    }

    /// <summary>
    /// Looks for bodies in view. Iterates the dead-actor registry instead of a physics overlap (no allocations,
    /// and the list is tiny compared to every collider in range).
    /// </summary>
    private void ScanForVisibleDeadBodies()
    {
        IReadOnlyList<ActorHealth> bodies = ActorHealth.Dead;
        if (bodies.Count == 0)
        {
            return;
        }

        Vector3 eye = EyePosition;
        float sqrRange = viewDistance * viewDistance;

        for (int i = 0; i < bodies.Count; i++)
        {
            ActorHealth health = bodies[i];
            if (!health || health.transform == transform)
            {
                continue;
            }

            if ((health.transform.position - eye).sqrMagnitude > sqrRange || IsBodyAlreadyReported(health))
            {
                continue;
            }

            Transform bodyTransform = health.transform;
            Vector3 bodySightPoint = PerceptionUtility.GetAimPoint(bodyTransform) + Vector3.up * deadBodySightHeightOffset;
            if (!CanSeePoint(bodySightPoint, bodyTransform))
            {
                continue;
            }

            if (debugLogs)
            {
                Debug.Log($"[NPCPerception] '{name}' discovered body '{bodyTransform.name}' while scanning.", this);
            }

            var deadBodyCrime = new CrimeEvent(bodyTransform.position, CrimeType.DeadBody, null, hearingDistance, victim: bodyTransform);
            ReportWitnessedCrime(deadBodyCrime, DetectionReason.DeadBodyAutoScan);

            // We now hold position to call it in; one body per scan.
            return;
        }
    }

    private bool HasReachedHearingStopDistance()
    {
        return AgentReady && !navAgent.pathPending &&
               (!navAgent.hasPath || navAgent.remainingDistance <= hearingMoveStopDistance);
    }

    private void SetDestination(Vector3 point)
    {
        if (!AgentReady)
        {
            return;
        }

        navAgent.isStopped = false;
        navAgent.SetDestination(point);
        investigationPoint = point;
        hasInvestigationPoint = true;
    }

    private void SetAgentStopped(bool stopped)
    {
        if (AgentReady)
        {
            navAgent.isStopped = stopped;
        }
    }

    private void StopNavMovement()
    {
        if (!AgentReady)
        {
            return;
        }

        navAgent.isStopped = true;
        navAgent.ResetPath();
    }

    private Vector3 GetFlatDirectionFromTransformTo(Vector3 worldPoint)
    {
        Vector3 flatDirection = worldPoint - transform.position;
        flatDirection.y = 0f;
        return flatDirection;
    }

    private IEnumerator RotateTowardsFlatDirection(Vector3 flatDirection)
    {
        if (flatDirection.sqrMagnitude <= MinFlatDirectionSqrMagnitude)
        {
            yield break;
        }

        Quaternion targetRotation = Quaternion.LookRotation(flatDirection.normalized);
        while (Quaternion.Angle(transform.rotation, targetRotation) > RotationStopAngle)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, lookTurnSpeed * Time.deltaTime);
            yield return null;
        }
    }

    private void OnDrawGizmos()
    {
        if (gizmoMode == GizmoDrawMode.Always) DrawPerceptionGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (gizmoMode == GizmoDrawMode.Selected) DrawPerceptionGizmos();
    }

    private void DrawPerceptionGizmos()
    {
        Vector3 eye = EyePosition;
        Vector3 ground = new Vector3(eye.x, transform.position.y + 0.05f, eye.z);
        bool busy = Application.isPlaying && Activity != PerceptionActivity.Idle;

        // Hearing radius (ground circle) and view cone.
        CrimeDebugDraw.Circle(ground, hearingDistance, CrimeDebugDraw.HearingColor);
        CrimeDebugDraw.ViewCone(ground, transform.forward, viewAngle, viewDistance,
            busy ? CrimeDebugDraw.AlertColor : CrimeDebugDraw.SightColor, busy ? 0.15f : 0.07f);
        CrimeDebugDraw.Circle(ground, deadBodyConfirmDistance, new Color(1f, 1f, 1f, 0.25f));

        // Line to the tracked suspect: green = visible, red = blocked/out of view.
        Transform t = target;
        if (t)
        {
            bool visible = CanSeeTarget(t);
            if (visible || (t.position - eye).sqrMagnitude <= viewDistance * viewDistance * 1.5f)
            {
                CrimeDebugDraw.Line(eye, PerceptionUtility.GetAimPoint(t), visible ? CrimeDebugDraw.OkColor : CrimeDebugDraw.AlertColor, visible ? 2f : 1f);
            }
        }

        if (!Application.isPlaying)
        {
            return;
        }

        if (Time.time - lastNoiseTime < 6f)
        {
            CrimeDebugDraw.Marker(lastNoisePosition + Vector3.up * 0.3f, CrimeDebugDraw.HearingColor, 0.35f);
            CrimeDebugDraw.DashedLine(eye, lastNoisePosition, CrimeDebugDraw.HearingColor);
        }

        if (hasInvestigationPoint)
        {
            CrimeDebugDraw.Marker(investigationPoint + Vector3.up * 0.2f, CrimeDebugDraw.SightColor, 0.2f);
        }

        if (drawNavPath)
        {
            CrimeDebugDraw.AgentPath(navAgent, CrimeDebugDraw.SightColor);
        }

        string label = Activity == PerceptionActivity.Idle ? name : $"{name}\n{Activity}";
        if (callCountdown && callCountdown.IsCalling)
        {
            label += $"\nCALLING {callCountdown.TimeRemaining:0.0}s";
        }

        CrimeDebugDraw.Label(transform.position + Vector3.up * 2.9f, label, busy ? CrimeDebugDraw.AlertColor : Color.white);
    }
}
