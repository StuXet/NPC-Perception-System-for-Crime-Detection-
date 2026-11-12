using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum PoliceState
{
    Idle,
    GoingToCrime,
    InvestigatingScene,
    ChasingSuspect,
    SearchingLastKnown
}

/// <summary>Who police may treat as the killer when they find a body without a known suspect.</summary>
public enum NpcSuspectRule
{
    Never,
    AnyVisibleNpcAtScene
}

/// <summary>
/// Police response state machine.
/// Police only know what witnesses told them: they chase toward the suspect's last known position,
/// track the suspect while they can see them, and search the area when they lose sight.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class PoliceUnit : MonoBehaviour
{
    private const float EyeHeight = 1.2f;

    private static readonly List<PoliceUnit> all = new List<PoliceUnit>();
    public static IReadOnlyList<PoliceUnit> All => all;

    /// <summary>Fired when the unit finishes a job and can be dispatched again.</summary>
    public event Action<PoliceUnit> BecameIdle;

    [Header("Crime Scene")]
    [Tooltip("Distance to the crime location that counts as 'arrived'.")]
    [SerializeField, Min(0.1f)] private float crimeArrivalTolerance = 1.75f;

    [Header("Patrol Perception (idle / driving to a call)")]
    [SerializeField, Min(0f)] private float patrolViewDistance = 12f;
    [SerializeField, Range(1f, 360f)] private float patrolViewAngle = 90f;

    [Header("Alerted Perception (investigating / chasing / searching)")]
    [Tooltip("Police look further and wider once they are on the scene.")]
    [SerializeField, Min(0f)] private float investigationViewDistance = 14f;
    [SerializeField, Range(1f, 360f)] private float investigationViewAngle = 100f;
    [Tooltip("Layers that block police line of sight.")]
    [SerializeField] private LayerMask investigationObstructionMask = ~0;

    [Header("Investigation (body found)")]
    [SerializeField, Min(0.5f)] private float investigationDurationSeconds = 8f;
    [Tooltip("Seconds between picking new search points around the scene.")]
    [SerializeField, Min(0.1f)] private float investigatePointInterval = 1.0f;
    [Tooltip("If no player is found at a body scene, may police arrest an NPC who is there?")]
    [SerializeField] private NpcSuspectRule npcSuspectRule = NpcSuspectRule.AnyVisibleNpcAtScene;

    [Header("Chase")]
    [SerializeField, Min(0.05f)] private float chasePathRefreshInterval = 0.2f;
    [SerializeField, Min(0.1f)] private float arrestDistance = 1.25f;
    [Tooltip("After losing sight, police keep tracking the real position for this long (turning a corner shouldn't lose them instantly).")]
    [SerializeField, Min(0f)] private float chaseMemorySeconds = 1.5f;
    [Tooltip("Old behaviour: police always know where the suspect is. Useful to compare against the perception-based chase.")]
    [SerializeField] private bool omniscientChase = false;

    [Header("Search (suspect lost)")]
    [SerializeField, Min(0f)] private float searchDurationSeconds = 10f;
    [SerializeField, Min(0.5f)] private float searchRadius = 6f;

    [Header("References")]
    [Tooltip("Auto-found by the 'Player' tag if empty.")]
    [SerializeField] private Transform playerTarget;

    [Header("Arrest UI")]
    [SerializeField] private BustedScreenController bustedScreen;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private GizmoDrawMode gizmoMode = GizmoDrawMode.Always;
    [SerializeField] private bool drawNavPath = true;

    [Header("Runtime (read only)")]
    [ReadOnly, SerializeField] private PoliceState state = PoliceState.Idle;
    [ReadOnly, SerializeField] private int incidentId;
    [ReadOnly, SerializeField] private CrimeType currentCrimeType;
    [ReadOnly, SerializeField] private Transform suspect;
    [ReadOnly, SerializeField] private bool canSeeSuspect;

    private NavMeshAgent agent;
    private CrimeEvent currentCrime;
    private Vector3 crimeLocation;
    private Transform victim;
    private Transform reportingWitness;
    private float investigationRadius;
    private float nextChasePathTime;
    private float stateEndTime;
    private float nextSearchPointTime;
    private Vector3 searchCenter;
    private bool hasLastKnownPosition;
    private Vector3 lastKnownPosition;
    private float lastSeenTime = float.NegativeInfinity;
    private float stateEnterTime;

    public PoliceState State => state;
    public bool IsBusy => state != PoliceState.Idle;
    public int CurrentIncidentId => IsBusy ? incidentId : 0;
    public int CurrentPriority => IsBusy ? currentCrime.Priority : -1;
    public CrimeEvent CurrentCrime => currentCrime;
    public Transform Suspect => suspect;

    /// <summary>Short status for overlays.</summary>
    public string StatusText
    {
        get
        {
            switch (state)
            {
                case PoliceState.Idle: return "Idle";
                case PoliceState.GoingToCrime: return $"Responding to {currentCrimeType} #{incidentId}";
                case PoliceState.InvestigatingScene: return $"Investigating #{incidentId} ({Mathf.Max(0f, stateEndTime - Time.time):0}s)";
                case PoliceState.ChasingSuspect:
                    return canSeeSuspect || omniscientChase
                        ? $"CHASING {NameOf(suspect)}"
                        : $"Heading to last known pos ({Time.time - lastSeenTime:0.0}s unseen)";
                case PoliceState.SearchingLastKnown: return $"Searching for {NameOf(suspect)} ({Mathf.Max(0f, stateEndTime - Time.time):0}s)";
                default: return state.ToString();
            }
        }
    }

    private bool AgentReady => agent && agent.enabled && agent.isOnNavMesh;

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

    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    /// <summary>
    /// Starts (or updates) the response to a reported crime.
    /// A direct sighting sends the unit to the suspect's last known position; otherwise it drives to the scene.
    /// </summary>
    public void GoToCrime(CrimeEvent reportedCrime)
    {
        bool sameIncident = IsBusy && reportedCrime.incidentId != 0 && reportedCrime.incidentId == incidentId;

        currentCrime = reportedCrime;
        incidentId = reportedCrime.incidentId;
        crimeLocation = reportedCrime.location;
        currentCrimeType = reportedCrime.type;
        reportingWitness = reportedCrime.reporter;
        victim = reportedCrime.victim;
        investigationRadius = Mathf.Max(2f, reportedCrime.soundRadius);

        if (reportedCrime.suspect)
        {
            suspect = reportedCrime.suspect;
        }
        else if (!sameIncident)
        {
            suspect = null;
        }

        if (reportedCrime.hasSuspectLastKnownPosition)
        {
            hasLastKnownPosition = true;
            lastKnownPosition = reportedCrime.suspectLastKnownPosition;
        }
        else if (!sameIncident)
        {
            hasLastKnownPosition = false;
        }

        if (!sameIncident)
        {
            lastSeenTime = float.NegativeInfinity;
        }

        ResumeAgent();

        if (reportedCrime.directPolicePursuit && suspect)
        {
            if (sameIncident && state == PoliceState.ChasingSuspect)
            {
                return;
            }

            // A witness saw the suspect: head for where they were last seen.
            TransitionToChase(suspect, $"responding to sighting of '{suspect.name}' (last seen {(hasLastKnownPosition ? lastKnownPosition.ToString() : "unknown")}).");
            return;
        }

        if (sameIncident)
        {
            // Another report of what we are already handling. Nothing new to do.
            return;
        }

        SetState(PoliceState.GoingToCrime);
        SetIncidentState(IncidentState.Responding);
        SetDestination(crimeLocation);
        Log($"dispatched to {currentCrimeType} #{incidentId} at {crimeLocation}. Known suspect: {NameOf(suspect)}.");
    }

    private void Update()
    {
        if (!AgentReady)
        {
            return;
        }

        canSeeSuspect = suspect && CanSee(suspect, IsAlerted);

        switch (state)
        {
            case PoliceState.GoingToCrime:
                UpdateGoingToCrime();
                break;
            case PoliceState.InvestigatingScene:
                UpdateInvestigatingScene();
                break;
            case PoliceState.ChasingSuspect:
                UpdateChasingSuspect();
                break;
            case PoliceState.SearchingLastKnown:
                UpdateSearching();
                break;
        }
    }

    private bool IsAlerted => state != PoliceState.Idle && state != PoliceState.GoingToCrime;

    private void UpdateGoingToCrime()
    {
        // Spotting a known suspect on the way beats driving to the scene.
        if (suspect && canSeeSuspect)
        {
            MarkPoliceArrived();
            TransitionToChase(suspect, $"spotted suspect '{suspect.name}' on the way.");
            return;
        }

        if (agent.pathPending)
        {
            return;
        }

        float dist = Vector3.Distance(transform.position, crimeLocation);
        if (dist > crimeArrivalTolerance && !HasArrived())
        {
            return;
        }

        Log($"arrived at crime scene #{incidentId} (dist {dist:F2}).");
        MarkPoliceArrived();

        if (currentCrime.IsBodyCrime)
        {
            StartInvestigatingScene();
            return;
        }

        if (suspect)
        {
            // Suspect not in view: search where they were last reported.
            StartSearch(hasLastKnownPosition ? lastKnownPosition : crimeLocation, "suspect not in sight at the scene");
        }
        else
        {
            TransitionToIdle("no suspect known; leaving the scene.", IncidentOutcome.NoSuspectFound);
        }
    }

    private void StartInvestigatingScene()
    {
        SetState(PoliceState.InvestigatingScene);
        SetIncidentState(IncidentState.Investigating);
        stateEndTime = Time.time + investigationDurationSeconds;
        PickPointAround(crimeLocation, investigationRadius);

        if (victim && ParamedicDispatcher.Instance)
        {
            ParamedicDispatcher.Instance.DispatchToDeadBody(victim);
        }

        Log($"investigating body scene #{incidentId} for {investigationDurationSeconds:0.#}s in radius {investigationRadius:0.#}.");
    }

    private void UpdateInvestigatingScene()
    {
        if (TryFindPlayerAtScene(out Transform seenPlayer))
        {
            TransitionToChase(seenPlayer, "saw the player at the crime scene and assumes they are the killer.");
            return;
        }

        if (npcSuspectRule == NpcSuspectRule.AnyVisibleNpcAtScene && TryFindNpcSuspectAtScene(out Transform npcSuspect))
        {
            TransitionToChase(npcSuspect, $"no player found; suspects NPC '{npcSuspect.name}'.");
            return;
        }

        if (Time.time >= stateEndTime)
        {
            TransitionToIdle("finished investigating without identifying the killer.", IncidentOutcome.NoSuspectFound);
            return;
        }

        if (HasArrived() && Time.time >= nextSearchPointTime)
        {
            PickPointAround(crimeLocation, investigationRadius);
        }
    }

    /// <summary>
    /// Track the suspect while visible (plus a short memory), otherwise go to the last known position and search.
    /// </summary>
    private void UpdateChasingSuspect()
    {
        if (!suspect)
        {
            TransitionToIdle("suspect no longer exists; stopping chase.", IncidentOutcome.Abandoned);
            return;
        }

        bool sees = canSeeSuspect || omniscientChase;
        if (sees)
        {
            if (float.IsNegativeInfinity(lastSeenTime))
            {
                MarkPoliceArrived();
            }

            lastSeenTime = Time.time;
            lastKnownPosition = suspect.position;
            hasLastKnownPosition = true;
        }

        bool tracking = sees || Time.time - lastSeenTime <= chaseMemorySeconds;
        if (tracking)
        {
            if (Vector3.Distance(transform.position, suspect.position) <= arrestDistance)
            {
                TriggerArrest();
                return;
            }

            if (Time.time >= nextChasePathTime)
            {
                // Periodic repath keeps up with a moving target without excessive NavMesh updates.
                SetDestinationSampled(suspect.position);
                nextChasePathTime = Time.time + chasePathRefreshInterval;
            }

            return;
        }

        // Lost them: go to the last place anyone saw them.
        Vector3 goal = hasLastKnownPosition ? lastKnownPosition : crimeLocation;
        if (Time.time >= nextChasePathTime)
        {
            SetDestinationSampled(goal);
            nextChasePathTime = Time.time + chasePathRefreshInterval * 5f;
        }

        if (!agent.pathPending && (HasArrived() || FlatDistance(transform.position, goal) <= crimeArrivalTolerance))
        {
            MarkPoliceArrived();
            StartSearch(goal, "reached last known position, suspect not in sight");
        }
    }

    private void StartSearch(Vector3 center, string reason)
    {
        searchCenter = center;
        stateEndTime = Time.time + searchDurationSeconds;
        SetState(PoliceState.SearchingLastKnown);
        SetIncidentState(IncidentState.Searching);
        PickPointAround(searchCenter, searchRadius);
        Log($"searching around {center} ({reason}).");
    }

    private void UpdateSearching()
    {
        if (suspect && canSeeSuspect)
        {
            TransitionToChase(suspect, $"found '{suspect.name}' again while searching.");
            return;
        }

        if (Time.time >= stateEndTime)
        {
            TransitionToIdle($"gave up searching for '{NameOf(suspect)}'.", suspect ? IncidentOutcome.SuspectEscaped : IncidentOutcome.NoSuspectFound);
            return;
        }

        if (HasArrived() && Time.time >= nextSearchPointTime)
        {
            PickPointAround(searchCenter, searchRadius);
        }
    }

    private void TriggerArrest()
    {
        bool arrestedPlayer = IsPlayerTransform(suspect);
        string suspectName = NameOf(suspect);

        if (!arrestedPlayer && suspect)
        {
            // Non-player suspect is removed from the scene on arrest.
            Destroy(suspect.gameObject);
            suspect = null;
            TransitionToIdle($"arrested NPC '{suspectName}'.", IncidentOutcome.SuspectArrested);
            return;
        }

        TransitionToIdle($"arrested the player '{suspectName}'.", IncidentOutcome.PlayerBusted);

        if (bustedScreen)
        {
            bustedScreen.ShowBusted();
        }
        else
        {
            Debug.LogWarning("[PoliceUnit] Arrest triggered but no BustedScreenController was found in scene.", this);
        }
    }

    private bool TryFindPlayerAtScene(out Transform seenPlayer)
    {
        seenPlayer = null;
        Transform player = playerTarget ? playerTarget : suspect;
        if (!player || !IsWithinInvestigationRadius(player.position) || !CanSee(player, true))
        {
            return false;
        }

        seenPlayer = player;
        return true;
    }

    /// <summary>
    /// Picks a living NPC in view at the scene. Never picks police, paramedics, the victim or anyone who reported the crime.
    /// </summary>
    private bool TryFindNpcSuspectAtScene(out Transform npcSuspect)
    {
        npcSuspect = null;
        CrimeIncident incident = CrimeManager.Instance ? CrimeManager.Instance.GetIncident(incidentId) : null;

        IReadOnlyList<ActorHealth> actors = ActorHealth.All;
        for (int i = 0; i < actors.Count; i++)
        {
            ActorHealth health = actors[i];
            if (!health || health.IsDead)
            {
                continue;
            }

            Transform candidate = health.transform;
            if (candidate == transform || IsPlayerTransform(candidate) ||
                PerceptionUtility.IsSameOrChild(candidate, victim) ||
                PerceptionUtility.IsSameOrChild(candidate, reportingWitness) ||
                (incident != null && incident.witnesses.Contains(candidate)))
            {
                continue;
            }

            if (health.TryGetComponent(out PoliceUnit _) || health.TryGetComponent(out ParamedicUnit _))
            {
                continue;
            }

            if (!IsWithinInvestigationRadius(candidate.position) || !CanSee(candidate, true))
            {
                continue;
            }

            npcSuspect = candidate;
            return true;
        }

        return false;
    }

    private bool CanSee(Transform target, bool alerted)
    {
        if (!target)
        {
            return false;
        }

        return PerceptionUtility.CanSee(
            EyePosition, transform.forward, PerceptionUtility.GetAimPoint(target),
            alerted ? investigationViewDistance : patrolViewDistance,
            alerted ? investigationViewAngle : patrolViewAngle,
            true, investigationObstructionMask, transform, target);
    }

    private Vector3 EyePosition => transform.position + Vector3.up * EyeHeight;

    private void PickPointAround(Vector3 center, float radius)
    {
        Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
        Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);

        SetDestination(NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas) ? hit.position : center);
        nextSearchPointTime = Time.time + investigatePointInterval;
    }

    private void SetDestinationSampled(Vector3 position)
    {
        // Keep destination valid even if the target briefly stands off the baked mesh.
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            position = hit.position;
        }

        SetDestination(position);
    }

    private void SetDestination(Vector3 position)
    {
        if (AgentReady)
        {
            agent.isStopped = false;
            agent.SetDestination(position);
        }
    }

    private void ResumeAgent()
    {
        if (AgentReady)
        {
            agent.isStopped = false;
        }
    }

    private bool HasArrived()
    {
        return !agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.1f);
    }

    private bool IsWithinInvestigationRadius(Vector3 worldPosition)
    {
        return FlatDistance(worldPosition, crimeLocation) <= investigationRadius;
    }

    private bool IsPlayerTransform(Transform candidate)
    {
        return PerceptionUtility.IsSameOrChild(candidate, playerTarget);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void TransitionToChase(Transform chaseTarget, string message)
    {
        suspect = chaseTarget;
        SetState(PoliceState.ChasingSuspect);
        SetIncidentState(IncidentState.Chasing);
        nextChasePathTime = 0f;

        if (!CanSee(suspect, true) && !omniscientChase && hasLastKnownPosition)
        {
            SetDestinationSampled(lastKnownPosition);
        }
        else
        {
            SetDestinationSampled(suspect.position);
        }

        Log(message);
    }

    private void TransitionToIdle(string message, IncidentOutcome outcome)
    {
        if (CrimeManager.Instance && incidentId != 0)
        {
            CrimeManager.Instance.ResolveIncident(incidentId, outcome);
        }

        SetState(PoliceState.Idle);
        if (AgentReady)
        {
            agent.ResetPath();
        }

        Log(message);
        BecameIdle?.Invoke(this);
    }

    private void SetState(PoliceState newState)
    {
        state = newState;
        stateEnterTime = Time.time;
    }

    private void SetIncidentState(IncidentState incidentState)
    {
        if (CrimeManager.Instance && incidentId != 0)
        {
            CrimeManager.Instance.SetIncidentState(incidentId, incidentState);
        }
    }

    private void MarkPoliceArrived()
    {
        if (CrimeManager.Instance && incidentId != 0)
        {
            CrimeManager.Instance.MarkPoliceArrived(incidentId);
        }
    }

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log($"[PoliceUnit] '{name}' {message}", this);
        }
    }

    private static string NameOf(Transform t) => t ? t.name : "Unknown";

    private void OnDrawGizmos()
    {
        if (gizmoMode == GizmoDrawMode.Always) DrawPoliceGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (gizmoMode == GizmoDrawMode.Selected) DrawPoliceGizmos();
    }

    private void DrawPoliceGizmos()
    {
        Vector3 ground = transform.position + Vector3.up * 0.05f;
        bool alerted = Application.isPlaying && IsAlerted;
        float viewDistance = alerted ? investigationViewDistance : patrolViewDistance;
        float viewAngle = alerted ? investigationViewAngle : patrolViewAngle;

        CrimeDebugDraw.ViewCone(ground, transform.forward, viewAngle, viewDistance,
            alerted ? CrimeDebugDraw.AlertColor : CrimeDebugDraw.PoliceColor, alerted ? 0.12f : 0.06f);
        CrimeDebugDraw.Circle(ground, arrestDistance, Color.red, 2f);

        if (!Application.isPlaying)
        {
            CrimeDebugDraw.Label(transform.position + Vector3.up * 2.6f, "POLICE", CrimeDebugDraw.PoliceColor);
            return;
        }

        if (state == PoliceState.InvestigatingScene || state == PoliceState.GoingToCrime)
        {
            CrimeDebugDraw.Circle(crimeLocation + Vector3.up * 0.05f, investigationRadius, new Color(1f, 0.5f, 0f, 0.9f), 2f);
        }

        if (state == PoliceState.SearchingLastKnown)
        {
            CrimeDebugDraw.Circle(searchCenter + Vector3.up * 0.05f, searchRadius, new Color(1f, 0.6f, 0.1f, 0.9f), 2f);
        }

        if (hasLastKnownPosition && state != PoliceState.Idle)
        {
            CrimeDebugDraw.Marker(lastKnownPosition + Vector3.up * 0.5f, CrimeDebugDraw.AlertColor, 0.4f);
            CrimeDebugDraw.Label(lastKnownPosition + Vector3.up * 1.2f, "last known", CrimeDebugDraw.AlertColor);
        }

        if (suspect && state != PoliceState.Idle)
        {
            CrimeDebugDraw.Line(EyePosition, PerceptionUtility.GetAimPoint(suspect),
                canSeeSuspect ? CrimeDebugDraw.OkColor : CrimeDebugDraw.MutedColor, canSeeSuspect ? 2f : 1f);
        }

        if (drawNavPath)
        {
            CrimeDebugDraw.AgentPath(agent, CrimeDebugDraw.PoliceColor);
        }

        Color labelColor = state == PoliceState.ChasingSuspect ? CrimeDebugDraw.AlertColor : CrimeDebugDraw.PoliceColor;
        CrimeDebugDraw.Label(transform.position + Vector3.up * 2.6f, $"POLICE\n{StatusText}\n{Time.time - stateEnterTime:0.0}s in state", labelColor);
    }
}
