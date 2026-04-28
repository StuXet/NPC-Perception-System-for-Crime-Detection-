using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class ParamedicUnit : MonoBehaviour
{
    [Header("Revive")]
    [SerializeField] private float reviveDistance = 1.5f;
    [SerializeField] private float reviveDuration = 1.0f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private NavMeshAgent agent;
    private Transform deadBodyTarget;
    private float reviveStartTime = -1f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    public void GoToDeadBody(Transform bodyTarget)
    {
        deadBodyTarget = bodyTarget;
        reviveStartTime = -1f;

        if (!deadBodyTarget)
        {
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(deadBodyTarget.position);

        if (debugLogs)
        {
            Debug.Log($"[ParamedicUnit] '{name}' heading to '{deadBodyTarget.name}'.");
        }
    }

    private void Update()
    {
        if (!deadBodyTarget)
        {
            return;
        }

        ActorHealth health = deadBodyTarget.GetComponent<ActorHealth>();
        if (!health || !health.IsDead)
        {
            deadBodyTarget = null;
            agent.ResetPath();
            return;
        }

        agent.SetDestination(deadBodyTarget.position);

        float dist = Vector3.Distance(transform.position, deadBodyTarget.position);
        if (dist > reviveDistance)
        {
            reviveStartTime = -1f;
            return;
        }

        agent.isStopped = true;
        transform.LookAt(new Vector3(deadBodyTarget.position.x, transform.position.y, deadBodyTarget.position.z));

        if (reviveStartTime < 0f)
        {
            reviveStartTime = Time.time;
            return;
        }

        if (Time.time - reviveStartTime < reviveDuration)
        {
            return;
        }

        health.Revive();

        if (debugLogs)
        {
            Debug.Log($"[ParamedicUnit] '{name}' revived '{deadBodyTarget.name}'.");
        }

        deadBodyTarget = null;
        reviveStartTime = -1f;
        agent.isStopped = false;
    }
}
