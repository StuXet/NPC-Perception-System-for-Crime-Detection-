using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Walks to a body and revives it after a treatment time.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class ParamedicUnit : MonoBehaviour
{
    private static readonly List<ParamedicUnit> all = new List<ParamedicUnit>();
    public static IReadOnlyList<ParamedicUnit> All => all;

    /// <summary>Fired when the unit finishes (or drops) a patient.</summary>
    public event Action<ParamedicUnit> BecameIdle;

    [Header("Revive")]
    [Tooltip("Distance to the body at which treatment starts.")]
    [SerializeField, Min(0.1f)] private float reviveDistance = 1.5f;
    [Tooltip("Seconds of treatment before the patient is revived.")]
    [SerializeField, Min(0f)] private float reviveDuration = 1.0f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private GizmoDrawMode gizmoMode = GizmoDrawMode.Always;
    [SerializeField] private bool drawNavPath = true;

    [Header("Runtime (read only)")]
    [ReadOnly, SerializeField] private Transform deadBodyTarget;
    [ReadOnly, SerializeField, Range(0f, 1f)] private float reviveProgress;

    private NavMeshAgent agent;
    private ActorHealth targetHealth;
    private float reviveStartTime = -1f;

    public bool IsBusy => deadBodyTarget;
    public Transform CurrentTarget => deadBodyTarget;
    public float ReviveProgress => reviveProgress;

    public string StatusText
    {
        get
        {
            if (!deadBodyTarget) return "Idle";
            return reviveStartTime >= 0f
                ? $"Treating {deadBodyTarget.name} {reviveProgress * 100f:0}%"
                : $"En route to {deadBodyTarget.name}";
        }
    }

    private bool AgentReady => agent && agent.enabled && agent.isOnNavMesh;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    public void GoToDeadBody(Transform bodyTarget)
    {
        deadBodyTarget = bodyTarget;
        targetHealth = bodyTarget ? bodyTarget.GetComponent<ActorHealth>() : null;
        reviveStartTime = -1f;
        reviveProgress = 0f;

        if (!deadBodyTarget || !AgentReady)
        {
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(deadBodyTarget.position);

        if (debugLogs)
        {
            Debug.Log($"[ParamedicUnit] '{name}' heading to '{deadBodyTarget.name}'.", this);
        }
    }

    private void Update()
    {
        if (!deadBodyTarget)
        {
            return;
        }

        if (!targetHealth || !targetHealth.IsDead)
        {
            // Patient gone or already revived by someone else.
            FinishJob();
            return;
        }

        if (!AgentReady)
        {
            return;
        }

        float dist = Vector3.Distance(transform.position, deadBodyTarget.position);
        if (dist > reviveDistance)
        {
            reviveStartTime = -1f;
            reviveProgress = 0f;
            if (!agent.pathPending)
            {
                agent.isStopped = false;
                agent.SetDestination(deadBodyTarget.position);
            }

            return;
        }

        agent.isStopped = true;
        transform.LookAt(new Vector3(deadBodyTarget.position.x, transform.position.y, deadBodyTarget.position.z));

        if (reviveStartTime < 0f)
        {
            reviveStartTime = Time.time;
        }

        reviveProgress = reviveDuration > 0f ? Mathf.Clamp01((Time.time - reviveStartTime) / reviveDuration) : 1f;
        if (reviveProgress < 1f)
        {
            return;
        }

        targetHealth.Revive();

        if (debugLogs)
        {
            Debug.Log($"[ParamedicUnit] '{name}' revived '{deadBodyTarget.name}'.", this);
        }

        FinishJob();
    }

    private void FinishJob()
    {
        deadBodyTarget = null;
        targetHealth = null;
        reviveStartTime = -1f;
        reviveProgress = 0f;

        if (AgentReady)
        {
            agent.isStopped = false;
            agent.ResetPath();
        }

        BecameIdle?.Invoke(this);
    }

    private void OnDrawGizmos()
    {
        if (gizmoMode == GizmoDrawMode.Always) DrawParamedicGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (gizmoMode == GizmoDrawMode.Selected) DrawParamedicGizmos();
    }

    private void DrawParamedicGizmos()
    {
        Vector3 ground = transform.position + Vector3.up * 0.05f;
        CrimeDebugDraw.Circle(ground, reviveDistance, CrimeDebugDraw.MedicColor, 2f);

        if (Application.isPlaying && deadBodyTarget)
        {
            CrimeDebugDraw.DashedLine(transform.position + Vector3.up, deadBodyTarget.position + Vector3.up * 0.3f, CrimeDebugDraw.MedicColor);
            if (drawNavPath)
            {
                CrimeDebugDraw.AgentPath(agent, CrimeDebugDraw.MedicColor);
            }
        }

        string label = Application.isPlaying ? $"PARAMEDIC\n{StatusText}" : "PARAMEDIC";
        CrimeDebugDraw.Label(transform.position + Vector3.up * 2.6f, label, CrimeDebugDraw.MedicColor);
    }
}
