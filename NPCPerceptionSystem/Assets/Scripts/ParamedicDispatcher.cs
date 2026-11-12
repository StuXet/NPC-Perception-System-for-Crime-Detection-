using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sends paramedics to bodies that police found. Bodies wait in a queue when every paramedic is busy,
/// instead of the current patient being abandoned.
/// </summary>
[DisallowMultipleComponent]
public class ParamedicDispatcher : MonoBehaviour
{
    public static ParamedicDispatcher Instance { get; private set; }

    [Header("References")]
    [SerializeField] private ParamedicUnit paramedicPrefab;
    [Tooltip("Where new paramedics spawn. Defaults to this transform.")]
    [SerializeField] private Transform spawnPoint;

    [Header("Dispatch")]
    [Tooltip("Maximum number of paramedic units alive at once.")]
    [SerializeField, Min(1)] private int maxUnits = 1;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private GizmoDrawMode gizmoMode = GizmoDrawMode.Selected;

    [Header("Runtime (read only)")]
    [ReadOnly, SerializeField] private List<ParamedicUnit> units = new List<ParamedicUnit>();
    [ReadOnly, SerializeField] private List<Transform> queuedBodies = new List<Transform>();

    public IReadOnlyList<ParamedicUnit> Units => units;
    public int QueuedCount => queuedBodies.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[ParamedicDispatcher] Duplicate on '{name}'. Removing this component.", this);
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void DispatchToDeadBody(Transform deadBodyTarget)
    {
        if (!IsValidBody(deadBodyTarget))
        {
            return;
        }

        units.RemoveAll(u => !u);

        if (IsBodyHandled(deadBodyTarget))
        {
            return;
        }

        ParamedicUnit unit = FindNearestIdleUnit(deadBodyTarget.position);
        if (!unit && units.Count < maxUnits)
        {
            unit = SpawnUnit();
        }

        if (!unit)
        {
            queuedBodies.Add(deadBodyTarget);
            Log($"All paramedics busy. Queued '{deadBodyTarget.name}' ({queuedBodies.Count} waiting).");
            return;
        }

        Assign(unit, deadBodyTarget);
    }

    private void Assign(ParamedicUnit unit, Transform body)
    {
        unit.GoToDeadBody(body);
        Log($"Dispatched '{unit.name}' to revive '{body.name}'.");
    }

    private void HandleUnitIdle(ParamedicUnit unit)
    {
        while (queuedBodies.Count > 0)
        {
            Transform body = queuedBodies[0];
            queuedBodies.RemoveAt(0);
            if (IsValidBody(body))
            {
                Assign(unit, body);
                return;
            }
        }
    }

    private static bool IsValidBody(Transform body)
    {
        if (!body)
        {
            return false;
        }

        ActorHealth health = body.GetComponent<ActorHealth>();
        return health && health.IsDead;
    }

    private bool IsBodyHandled(Transform body)
    {
        if (queuedBodies.Contains(body))
        {
            return true;
        }

        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] && units[i].CurrentTarget == body)
            {
                return true;
            }
        }

        return false;
    }

    private ParamedicUnit FindNearestIdleUnit(Vector3 location)
    {
        ParamedicUnit best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < units.Count; i++)
        {
            ParamedicUnit unit = units[i];
            if (!unit || !unit.isActiveAndEnabled || unit.IsBusy)
            {
                continue;
            }

            float sqr = (unit.transform.position - location).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = unit;
            }
        }

        return best;
    }

    private ParamedicUnit SpawnUnit()
    {
        if (!paramedicPrefab)
        {
            Debug.LogWarning("[ParamedicDispatcher] No paramedic prefab assigned.", this);
            return null;
        }

        Vector3 pos = spawnPoint ? spawnPoint.position : transform.position;
        Quaternion rot = spawnPoint ? spawnPoint.rotation : transform.rotation;
        ParamedicUnit created = Instantiate(paramedicPrefab, pos, rot);
        created.name = $"{paramedicPrefab.name}_{units.Count + 1}";
        created.BecameIdle += HandleUnitIdle;
        units.Add(created);
        return created;
    }

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log($"[ParamedicDispatcher] {message}", this);
        }
    }

    private void OnDrawGizmos()
    {
        if (gizmoMode == GizmoDrawMode.Always) DrawDispatcherGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (gizmoMode == GizmoDrawMode.Selected) DrawDispatcherGizmos();
    }

    private void DrawDispatcherGizmos()
    {
        Vector3 pos = spawnPoint ? spawnPoint.position : transform.position;
        CrimeDebugDraw.Circle(pos + Vector3.up * 0.05f, 1f, CrimeDebugDraw.MedicColor, 3f);
        string text = Application.isPlaying
            ? $"HOSPITAL\nunits {units.Count}/{maxUnits} | queued {queuedBodies.Count}"
            : $"HOSPITAL\nmax units {maxUnits}";
        CrimeDebugDraw.Label(pos + Vector3.up * 2f, text, CrimeDebugDraw.MedicColor);

        for (int i = 0; i < queuedBodies.Count; i++)
        {
            if (queuedBodies[i])
            {
                CrimeDebugDraw.DashedLine(pos, queuedBodies[i].position, CrimeDebugDraw.MedicColor);
            }
        }
    }
}
