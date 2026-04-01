using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class PoliceUnit : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private NavMeshAgent agent;
    private bool hasDestination;
    private Vector3 currentDestination;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    public void GoToCrime(CrimeEvent reportedCrime)
    {
        currentDestination = reportedCrime.location;

        bool set = agent.SetDestination(currentDestination);
        hasDestination = set;

        if (debugLogs)
        {
            Debug.Log($"[PoliceUnit] '{name}' dispatched to crime at {currentDestination}. Destination set: {set}");
        }
    }

    private void Update()
    {
        if (!hasDestination || agent.pathPending)
        {
            return;
        }

        if (agent.remainingDistance <= agent.stoppingDistance)
        {
            hasDestination = false;

            if (debugLogs)
            {
                Debug.Log($"[PoliceUnit] '{name}' arrived at crime location {currentDestination}.");
            }
        }
    }
}
