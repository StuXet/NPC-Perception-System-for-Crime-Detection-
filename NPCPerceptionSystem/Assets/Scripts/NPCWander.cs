using UnityEngine;
using UnityEngine.AI;


[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class NPCWander : MonoBehaviour
{
    private enum WanderState
    {
        Idle,
        Moving
    }

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 8f;
    [SerializeField] private float minIdleTime = 0.5f;
    [SerializeField] private float maxIdleTime = 2f;
    [SerializeField] private int sampleAttempts = 12;

    [Header("Debug")]
    [SerializeField] private bool debugLogs;

    private NavMeshAgent agent;
    private Vector3 homePosition;
    private WanderState state = WanderState.Idle;
    private float idleUntil;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
        homePosition = transform.position;
        idleUntil = 0f;
    }

    /// <summary>
    /// Simple wander state machine: wait while idle, move to sampled points, then idle again on arrival.
    /// </summary>
    private void Update()
    {
        if (!agent || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        if (agent.isStopped)
        {
            return;
        }

        if (agent.pathPending)
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
                // Use the first valid sampled point instead of over-optimizing path quality.
                bool ok = agent.SetDestination(hit.position);
                if (debugLogs)
                {
                    Debug.Log($"[NPCWander] '{name}' wandering to {hit.position} (set={ok}).");
                }
                return ok;
            }
        }

        if (debugLogs)
        {
            Debug.LogWarning($"[NPCWander] '{name}' could not find NavMesh point near {homePosition}.");
        }
        return false;
    }

    public void SetHomePosition(Vector3 worldPosition)
    {
        homePosition = worldPosition;
    }
}
