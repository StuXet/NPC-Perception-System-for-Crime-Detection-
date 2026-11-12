using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Simple ambient wandering around a home point. Pauses while the NPC's perception is investigating or reporting,
/// so the two systems never fight over the NavMeshAgent destination.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class NPCWander : MonoBehaviour
{
    private enum WanderState
    {
        Idle,
        Moving,
        Paused
    }

    [Header("Wander")]
    [Tooltip("Random destinations are picked within this radius of the home position (start position).")]
    [SerializeField, Min(0.5f)] private float wanderRadius = 8f;
    [SerializeField, Min(0f)] private float minIdleTime = 0.5f;
    [SerializeField, Min(0f)] private float maxIdleTime = 2f;
    [Tooltip("How many random points to try before giving up for this cycle.")]
    [SerializeField, Min(1)] private int sampleAttempts = 12;

    [Header("Debug")]
    [SerializeField] private bool debugLogs;
    [SerializeField] private GizmoDrawMode gizmoMode = GizmoDrawMode.Selected;

    [Header("Runtime (read only)")]
    [ReadOnly, SerializeField] private WanderState state = WanderState.Idle;

    private NavMeshAgent agent;
    private NPCPerception perception;
    private Vector3 homePosition;
    private bool homeSet;
    private float idleUntil;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        perception = GetComponent<NPCPerception>();
    }

    private void Start()
    {
        if (!homeSet)
        {
            homePosition = transform.position;
            homeSet = true;
        }

        idleUntil = 0f;
    }

    /// <summary>
    /// Wait while idle, move to sampled points, idle again on arrival. Yields to perception while it is busy.
    /// </summary>
    private void Update()
    {
        if (!agent || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        if (perception && perception.isActiveAndEnabled && perception.IsBusy)
        {
            state = WanderState.Paused;
            return;
        }

        if (state == WanderState.Paused)
        {
            // Perception just finished: take a short breather before wandering again.
            state = WanderState.Idle;
            idleUntil = Time.time + Random.Range(minIdleTime, maxIdleTime);
            return;
        }

        if (agent.isStopped || agent.pathPending)
        {
            return;
        }

        if (state == WanderState.Moving)
        {
            bool arrived = !agent.hasPath || agent.remainingDistance <= agent.stoppingDistance;
            if (!arrived)
            {
                return;
            }

            // Small idle pause helps wandering feel less robotic.
            state = WanderState.Idle;
            idleUntil = Time.time + Random.Range(minIdleTime, maxIdleTime);
            return;
        }

        if (Time.time < idleUntil)
        {
            return;
        }

        if (TryPickNewDestination())
        {
            state = WanderState.Moving;
        }
        else
        {
            idleUntil = Time.time + 1f;
        }
    }

    /// <summary>
    /// Samples random points around home and picks the first valid NavMesh destination.
    /// </summary>
    private bool TryPickNewDestination()
    {
        for (int i = 0; i < sampleAttempts; i++)
        {
            Vector2 offset2 = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = homePosition + new Vector3(offset2.x, 0f, offset2.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                bool ok = agent.SetDestination(hit.position);
                if (debugLogs)
                {
                    Debug.Log($"[NPCWander] '{name}' wandering to {hit.position} (set={ok}).", this);
                }

                return ok;
            }
        }

        if (debugLogs)
        {
            Debug.LogWarning($"[NPCWander] '{name}' could not find NavMesh point near {homePosition}.", this);
        }

        return false;
    }

    public void SetHomePosition(Vector3 worldPosition)
    {
        homePosition = worldPosition;
        homeSet = true;
    }

    private void OnDrawGizmos()
    {
        if (gizmoMode == GizmoDrawMode.Always) DrawWanderGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (gizmoMode == GizmoDrawMode.Selected) DrawWanderGizmos();
    }

    private void DrawWanderGizmos()
    {
        Vector3 home = Application.isPlaying && homeSet ? homePosition : transform.position;
        CrimeDebugDraw.Circle(home + Vector3.up * 0.05f, wanderRadius, new Color(0.6f, 0.9f, 0.6f, 0.5f));
        CrimeDebugDraw.Marker(home + Vector3.up * 0.2f, new Color(0.6f, 0.9f, 0.6f, 0.9f), 0.2f);
    }
}
