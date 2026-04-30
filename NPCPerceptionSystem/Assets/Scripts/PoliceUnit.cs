using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class PoliceUnit : MonoBehaviour
{
    private const float InvestigationEyeHeight = 1.2f;

    private enum UnitState
    {
        Idle,
        GoingToCrime,
        InvestigatingScene,
        ChasingSuspect
    }

    [Header("Crime scene")]
    [SerializeField] private float crimeArrivalTolerance = 1.75f;

    [Header("Chase")]
    [SerializeField] private float chasePathRefreshInterval = 0.2f;
    [SerializeField] private float arrestDistance = 1.25f;

    [Header("Investigation")]
    [SerializeField] private float investigationDurationSeconds = 8f;
    [SerializeField] private float investigatePointInterval = 1.0f;
    [SerializeField] private float investigationViewDistance = 14f;
    [SerializeField, Range(1f, 180f)] private float investigationViewAngle = 100f;
    [SerializeField] private LayerMask investigationObstructionMask = ~0;

    [Header("References")]
    [SerializeField] private Transform playerTarget;

    [Header("Arrest UI")]
    [SerializeField] private BustedScreenController bustedScreen;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private bool drawGizmos = true;

    private NavMeshAgent agent;
    private UnitState state = UnitState.Idle;
    private Vector3 crimeLocation;
    private Transform suspect;
    private float nextChasePathTime;
    private float investigationEndTime;
    private float nextInvestigatePointTime;
    private float investigationRadius;
    private CrimeType currentCrimeType;
    private Transform reportingWitness;
    private bool arrestTriggered;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (!bustedScreen)
        {
            bustedScreen = FindObjectOfType<BustedScreenController>();
        }

        if (!playerTarget)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player)
            {
                playerTarget = player.transform;
            }
        }
    }

    /// <summary>
    /// Initializes police response state and decides between direct pursuit or first moving to crime scene.
    /// </summary>
    public void GoToCrime(CrimeEvent reportedCrime)
    {
        crimeLocation = reportedCrime.location;
        suspect = reportedCrime.suspect;
        currentCrimeType = reportedCrime.type;
        reportingWitness = reportedCrime.reporter;
        investigationRadius = Mathf.Max(2f, reportedCrime.soundRadius);
        nextChasePathTime = 0f;
        arrestTriggered = false;
        agent.isStopped = false;

        if (reportedCrime.directPolicePursuit && suspect)
        {
            // Witness confirmed visual contact, so skip scene arrival and start pursuit immediately.
            state = UnitState.ChasingSuspect;
            RefreshChaseDestination(true);

            if (debugLogs)
            {
                Debug.Log($"[PoliceUnit] '{name}' direct pursuit of suspect '{suspect.name}' (witness saw them).");
            }

            return;
        }

        state = UnitState.GoingToCrime;

        bool set = agent.SetDestination(crimeLocation);

        if (debugLogs)
        {
            Debug.Log(
                $"[PoliceUnit] '{name}' dispatched to crime at {crimeLocation}. Chase target: " +
                $"{(suspect ? suspect.name : "none")}. Destination set: {set}"
            );
        }
    }

    private void Update()
    {
        if (agent.pathPending)
        {
            return;
        }

        switch (state)
        {
            case UnitState.GoingToCrime:
                UpdateGoingToCrime();
                break;
            case UnitState.InvestigatingScene:
                UpdateInvestigatingScene();
                break;
            case UnitState.ChasingSuspect:
                UpdateChasingSuspect();
                break;
        }
    }

    /// <summary>
    /// Handles arrival detection at crime scene and transitions to chase or idle based on suspect availability.
    /// </summary>
    private void UpdateGoingToCrime()
    {
        if (!agent.hasPath)
        {
            return;
        }

        float dist = Vector3.Distance(transform.position, crimeLocation);
        bool closeEnough = dist <= crimeArrivalTolerance;
        bool navSaysArrived = HasArrived(agent.stoppingDistance);

        if (!closeEnough && !navSaysArrived)
        {
            return;
        }

        if (debugLogs)
        {
            Debug.Log($"[PoliceUnit] '{name}' arrived at crime area. dist={dist:F2}");
        }

        if (currentCrimeType == CrimeType.DeadBody)
        {
            StartInvestigatingScene();
            return;
        }

        if (suspect)
        {
            // We made it to the scene and still know who to chase.
            TransitionToChase(suspect, $"[PoliceUnit] '{name}' chasing suspect '{suspect.name}'.");
        }
        else
        {
            TransitionToIdle($"[PoliceUnit] '{name}' no suspect to chase; holding position.");
        }
    }

    private void StartInvestigatingScene()
    {
        state = UnitState.InvestigatingScene;
        float duration = Mathf.Max(0.5f, investigationDurationSeconds);
        investigationEndTime = Time.time + duration;
        nextInvestigatePointTime = 0f;
        PickInvestigationPoint();

        if (currentCrimeType == CrimeType.DeadBody && ParamedicDispatcher.Instance)
        {
            ParamedicDispatcher.Instance.DispatchToDeadBody(suspect);
        }

        if (debugLogs)
        {
            Debug.Log($"[PoliceUnit] '{name}' investigating dead-body scene for {duration:0.##}s in radius {investigationRadius:0.##}.");
        }
    }

    private void UpdateInvestigatingScene()
    {
        if (CanSeePlayerInInvestigationRadius(out Transform seenPlayer))
        {
            TransitionToChase(seenPlayer, $"[PoliceUnit] '{name}' saw player in crime scene radius and assumes killer.");
            return;
        }

        if (TryFindNpcSuspectInInvestigationRadius(out Transform npcSuspect))
        {
            TransitionToChase(
                npcSuspect,
                $"[PoliceUnit] '{name}' did not find player and assumes NPC '{npcSuspect.name}' is the killer."
            );
            return;
        }

        if (Time.time >= investigationEndTime)
        {
            TransitionToIdle($"[PoliceUnit] '{name}' finished investigation without identifying killer.");
            return;
        }

        bool arrived = HasArrived(agent.stoppingDistance);
        if (arrived && Time.time >= nextInvestigatePointTime)
        {
            PickInvestigationPoint();
        }
    }

    /// <summary>
    /// Maintains pursuit loop: arrest on close range, otherwise refresh chase destination on interval.
    /// </summary>
    private void UpdateChasingSuspect()
    {
        if (!suspect)
        {
            // Suspect reference can disappear if the object is disabled/destroyed.
            TransitionToIdle($"[PoliceUnit] '{name}' lost suspect reference; stopping chase.");
            return;
        }

        float suspectDistance = Vector3.Distance(transform.position, suspect.position);
        if (suspectDistance <= arrestDistance)
        {
            TriggerArrest();
            return;
        }

        if (Time.time >= nextChasePathTime)
        {
            // Periodic repath keeps up with a moving target without excessive NavMesh updates.
            RefreshChaseDestination(false);
        }
    }

    private void TriggerArrest()
    {
        if (arrestTriggered)
        {
            return;
        }

        arrestTriggered = true;
        state = UnitState.Idle;
        agent.isStopped = true;

        if (debugLogs)
        {
            Debug.Log($"[PoliceUnit] '{name}' arrested suspect '{(suspect ? suspect.name : "Unknown")}'.");
        }

        bool arrestedPlayer = playerTarget && suspect && (suspect == playerTarget || suspect.IsChildOf(playerTarget));
        if (!arrestedPlayer && suspect)
        {
            // Non-player suspect is removed from scene immediately on arrest.
            Destroy(suspect.gameObject);
            suspect = null;
            return;
        }

        if (bustedScreen)
        {
            bustedScreen.ShowBusted();
        }
        else if (debugLogs)
        {
            Debug.LogWarning("[PoliceUnit] Arrest triggered but no BustedScreenController was found in scene.");
        }
    }

    /// <summary>
    /// Recomputes chase target position on NavMesh and schedules next path refresh tick.
    /// </summary>
    private void RefreshChaseDestination(bool immediate)
    {
        if (!suspect)
        {
            return;
        }

        Vector3 targetPos = suspect.position;
        if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            // Keep destination valid even if suspect briefly stands off the baked mesh.
            targetPos = hit.position;
        }

        agent.SetDestination(targetPos);
        nextChasePathTime = Time.time + (immediate ? 0f : chasePathRefreshInterval);
    }

    private void PickInvestigationPoint()
    {
        Vector2 offset = Random.insideUnitCircle * investigationRadius;
        Vector3 candidate = crimeLocation + new Vector3(offset.x, 0f, offset.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, investigationRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
        else
        {
            agent.SetDestination(crimeLocation);
        }

        nextInvestigatePointTime = Time.time + investigatePointInterval;
    }

    private bool CanSeePlayerInInvestigationRadius(out Transform seenPlayer)
    {
        seenPlayer = null;

        Transform player = playerTarget ? playerTarget : suspect;
        if (!player)
        {
            return false;
        }

        // Player must be inside crime-scene radius to be assumed killer during this phase.
        if (!IsWithinInvestigationRadius(player.position))
        {
            return false;
        }

        if (!CanSeeTransformInInvestigation(player))
        {
            return false;
        }

        seenPlayer = player;
        return true;
    }

    private bool TryFindNpcSuspectInInvestigationRadius(out Transform npcSuspect)
    {
        npcSuspect = null;

        Collider[] nearby = Physics.OverlapSphere(crimeLocation, investigationRadius, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < nearby.Length; i++)
        {
            ActorHealth health = nearby[i].GetComponentInParent<ActorHealth>();
            if (!health || health.IsDead)
            {
                continue;
            }

            Transform candidate = health.transform;
            if (!candidate || candidate == transform)
            {
                continue;
            }

            if (IsPlayerTransform(candidate))
            {
                continue;
            }

            if (reportingWitness && (candidate == reportingWitness || candidate.IsChildOf(reportingWitness)))
            {
                continue;
            }

            if (!IsWithinInvestigationRadius(candidate.position))
            {
                continue;
            }

            if (!CanSeeTransformInInvestigation(candidate))
            {
                continue;
            }

            npcSuspect = candidate;
            return true;
        }

        return false;
    }

    private bool CanSeeTransformInInvestigation(Transform target)
    {
        if (!target)
        {
            return false;
        }

        Vector3 eye = GetInvestigationEyePosition();
        Vector3 toTarget = target.position - eye;
        float sqrDist = toTarget.sqrMagnitude;
        if (sqrDist > investigationViewDistance * investigationViewDistance)
        {
            return false;
        }

        float angle = Vector3.Angle(transform.forward, toTarget.normalized);
        if (angle > investigationViewAngle * 0.5f)
        {
            return false;
        }

        float dist = Mathf.Sqrt(sqrDist);
        if (Physics.Raycast(eye, toTarget.normalized, out RaycastHit hit, dist, investigationObstructionMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform != target && !hit.transform.IsChildOf(target))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasArrived(float stopDistance)
    {
        return !agent.pathPending && (!agent.hasPath || agent.remainingDistance <= stopDistance);
    }

    private Vector3 GetInvestigationEyePosition()
    {
        return transform.position + Vector3.up * InvestigationEyeHeight;
    }

    private bool IsWithinInvestigationRadius(Vector3 worldPosition)
    {
        return Vector3.Distance(worldPosition, crimeLocation) <= investigationRadius;
    }

    private bool IsPlayerTransform(Transform candidate)
    {
        return playerTarget && (candidate == playerTarget || candidate.IsChildOf(playerTarget));
    }

    private void TransitionToChase(Transform chaseTarget, string debugMessage)
    {
        suspect = chaseTarget;
        state = UnitState.ChasingSuspect;
        RefreshChaseDestination(true);

        if (debugLogs)
        {
            Debug.Log(debugMessage);
        }
    }

    private void TransitionToIdle(string debugMessage)
    {
        state = UnitState.Idle;
        agent.ResetPath();

        if (debugLogs)
        {
            Debug.Log(debugMessage);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Vector3 eye = GetInvestigationEyePosition();
        float halfFov = investigationViewAngle * 0.5f;

        // Police investigation vision range.
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(eye, investigationViewDistance);

        Vector3 leftBoundary = Quaternion.Euler(0f, -halfFov, 0f) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0f, halfFov, 0f) * transform.forward;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(eye, eye + leftBoundary * investigationViewDistance);
        Gizmos.DrawLine(eye, eye + rightBoundary * investigationViewDistance);

        // Arrest radius around police.
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, arrestDistance);

        // Active crime scene investigation radius.
        if (investigationRadius > 0.01f)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.95f);
            Gizmos.DrawWireSphere(crimeLocation, investigationRadius);
        }

        // Debug line to tracked target (player/suspect), color-coded by visibility.
        Transform tracked = playerTarget ? playerTarget : suspect;
        if (tracked)
        {
            Vector3 toTracked = tracked.position - eye;
            bool inDistance = toTracked.sqrMagnitude <= investigationViewDistance * investigationViewDistance;
            bool inAngle = Vector3.Angle(transform.forward, toTracked.normalized) <= halfFov;
            Gizmos.color = (inDistance && inAngle) ? Color.green : Color.gray;
            Gizmos.DrawLine(eye, tracked.position);
        }
    }
}
