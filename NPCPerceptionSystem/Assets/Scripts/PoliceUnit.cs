using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class PoliceUnit : MonoBehaviour
{
    private enum UnitState
    {
        Idle,
        GoingToCrime,
        ChasingSuspect
    }

    [Header("Crime scene")]
    [SerializeField] private float crimeArrivalTolerance = 1.75f;

    [Header("Chase")]
    [SerializeField] private float chasePathRefreshInterval = 0.2f;
    [SerializeField] private float arrestDistance = 1.25f;

    [Header("Arrest UI")]
    [SerializeField] private BustedScreenController bustedScreen;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private NavMeshAgent agent;
    private UnitState state = UnitState.Idle;
    private Vector3 crimeLocation;
    private Transform suspect;
    private float nextChasePathTime;
    private bool arrestTriggered;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (!bustedScreen)
        {
            bustedScreen = FindObjectOfType<BustedScreenController>();
        }
    }

    /// <summary>
    /// Initializes police response state and decides between direct pursuit or first moving to crime scene.
    /// </summary>
    public void GoToCrime(CrimeEvent reportedCrime)
    {
        crimeLocation = reportedCrime.location;
        suspect = reportedCrime.suspect;
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
        bool navSaysArrived = agent.remainingDistance <= agent.stoppingDistance;

        if (!closeEnough && !navSaysArrived)
        {
            return;
        }

        if (debugLogs)
        {
            Debug.Log($"[PoliceUnit] '{name}' arrived at crime area. dist={dist:F2}");
        }

        if (suspect)
        {
            // We made it to the scene and still know who to chase.
            state = UnitState.ChasingSuspect;
            RefreshChaseDestination(true);

            if (debugLogs)
            {
                Debug.Log($"[PoliceUnit] '{name}' chasing suspect '{suspect.name}'.");
            }
        }
        else
        {
            state = UnitState.Idle;

            if (debugLogs)
            {
                Debug.Log($"[PoliceUnit] '{name}' no suspect to chase; holding position.");
            }
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
            state = UnitState.Idle;

            if (debugLogs)
            {
                Debug.Log($"[PoliceUnit] '{name}' lost suspect reference; stopping chase.");
            }
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
}
