using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sends police units to reported crimes.
/// Picks the nearest idle unit, spawns new units up to a limit, lets urgent crimes take over less urgent jobs,
/// and queues the rest until a unit becomes free.
/// </summary>
[DisallowMultipleComponent]
public class PoliceDispatcher : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PoliceUnit policePrefab;
    [Tooltip("Where new units spawn. Defaults to this transform.")]
    [SerializeField] private Transform spawnPoint;

    [Header("Dispatch")]
    [Tooltip("Maximum number of police units alive at once.")]
    [SerializeField, Min(1)] private int maxUnits = 1;
    [Tooltip("A more urgent crime (e.g. murder with a sighting) may pull a unit off a less urgent job. " +
             "The interrupted job goes back into the queue.")]
    [SerializeField] private bool allowPriorityOverride = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private GizmoDrawMode gizmoMode = GizmoDrawMode.Selected;

    [Header("Runtime (read only)")]
    [ReadOnly, SerializeField] private List<PoliceUnit> units = new List<PoliceUnit>();
    [ReadOnly, SerializeField] private List<CrimeEvent> queuedCrimes = new List<CrimeEvent>();

    public IReadOnlyList<PoliceUnit> Units => units;
    public int QueuedCount => queuedCrimes.Count;

    private void OnEnable()
    {
        CrimeManager.OnCrimeReported += HandleCrimeReported;
    }

    private void OnDisable()
    {
        CrimeManager.OnCrimeReported -= HandleCrimeReported;
    }

    private void HandleCrimeReported(CrimeEvent crime)
    {
        units.RemoveAll(u => !u);

        // 1) A unit is already on this incident: give it the new information (e.g. a fresh sighting).
        PoliceUnit assigned = FindUnitOnIncident(crime.incidentId);
        if (assigned)
        {
            assigned.GoToCrime(crime);
            Log($"Updated '{assigned.name}' with new info for #{crime.incidentId}.");
            return;
        }

        // Already waiting in the queue: just refresh the queued data.
        int queuedIndex = queuedCrimes.FindIndex(c => c.incidentId == crime.incidentId && crime.incidentId != 0);
        if (queuedIndex >= 0)
        {
            queuedCrimes[queuedIndex] = crime;
            TryDispatchQueued();
            return;
        }

        // 2) Nearest idle unit, or 3) spawn a new one.
        PoliceUnit unit = FindNearestIdleUnit(crime.location);
        if (!unit && units.Count < maxUnits)
        {
            unit = SpawnUnit();
        }

        // 4) Take over a less urgent job.
        if (!unit && allowPriorityOverride)
        {
            unit = FindLowestPriorityUnitBelow(crime.Priority);
            if (unit)
            {
                Log($"'{unit.name}' reassigned from {unit.CurrentCrime.type} #{unit.CurrentIncidentId} to more urgent {crime.type} #{crime.incidentId}.");
                Enqueue(unit.CurrentCrime);
                if (CrimeManager.Instance)
                {
                    CrimeManager.Instance.SetIncidentState(unit.CurrentIncidentId, IncidentState.Reported);
                }
            }
        }

        if (!unit)
        {
            Enqueue(crime);
            Log($"No unit free. Queued {crime.type} #{crime.incidentId} ({queuedCrimes.Count} waiting).");
            return;
        }

        Assign(unit, crime);
    }

    private void Assign(PoliceUnit unit, CrimeEvent crime)
    {
        unit.GoToCrime(crime);
        Log($"Dispatched '{unit.name}' to {crime.type} #{crime.incidentId} at {crime.location}.");
    }

    private void HandleUnitIdle(PoliceUnit unit)
    {
        TryDispatchQueued();
    }

    private void TryDispatchQueued()
    {
        while (queuedCrimes.Count > 0)
        {
            int best = GetHighestPriorityQueuedIndex();
            CrimeEvent crime = queuedCrimes[best];

            if (CrimeManager.Instance && !CrimeManager.Instance.IsIncidentOpen(crime.incidentId) && crime.incidentId != 0)
            {
                queuedCrimes.RemoveAt(best);
                continue;
            }

            PoliceUnit unit = FindNearestIdleUnit(crime.location);
            if (!unit && units.Count < maxUnits)
            {
                unit = SpawnUnit();
            }

            if (!unit)
            {
                return;
            }

            queuedCrimes.RemoveAt(best);
            Assign(unit, crime);
        }
    }

    private void Enqueue(CrimeEvent crime)
    {
        for (int i = 0; i < queuedCrimes.Count; i++)
        {
            if (queuedCrimes[i].incidentId == crime.incidentId && crime.incidentId != 0)
            {
                queuedCrimes[i] = crime;
                return;
            }
        }

        queuedCrimes.Add(crime);
    }

    private int GetHighestPriorityQueuedIndex()
    {
        int best = 0;
        for (int i = 1; i < queuedCrimes.Count; i++)
        {
            // Higher priority first; on a tie the older report (lower index) wins.
            if (queuedCrimes[i].Priority > queuedCrimes[best].Priority)
            {
                best = i;
            }
        }

        return best;
    }

    private PoliceUnit FindUnitOnIncident(int incidentId)
    {
        if (incidentId == 0)
        {
            return null;
        }

        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] && units[i].CurrentIncidentId == incidentId)
            {
                return units[i];
            }
        }

        return null;
    }

    private PoliceUnit FindNearestIdleUnit(Vector3 location)
    {
        PoliceUnit best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < units.Count; i++)
        {
            PoliceUnit unit = units[i];
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

    private PoliceUnit FindLowestPriorityUnitBelow(int priority)
    {
        PoliceUnit best = null;
        for (int i = 0; i < units.Count; i++)
        {
            PoliceUnit unit = units[i];
            if (!unit || !unit.isActiveAndEnabled || unit.CurrentPriority >= priority)
            {
                continue;
            }

            if (!best || unit.CurrentPriority < best.CurrentPriority)
            {
                best = unit;
            }
        }

        return best;
    }

    private PoliceUnit SpawnUnit()
    {
        if (!policePrefab)
        {
            Debug.LogWarning("[PoliceDispatcher] No police prefab assigned.", this);
            return null;
        }

        Vector3 pos = spawnPoint ? spawnPoint.position : transform.position;
        Quaternion rot = spawnPoint ? spawnPoint.rotation : transform.rotation;
        PoliceUnit created = Instantiate(policePrefab, pos, rot);
        created.name = $"{policePrefab.name}_{units.Count + 1}";
        created.BecameIdle += HandleUnitIdle;
        units.Add(created);
        return created;
    }

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log($"[PoliceDispatcher] {message}", this);
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
        CrimeDebugDraw.Circle(pos + Vector3.up * 0.05f, 1f, CrimeDebugDraw.PoliceColor, 3f);
        string text = Application.isPlaying
            ? $"POLICE HQ\nunits {units.Count}/{maxUnits} | queued {queuedCrimes.Count}"
            : $"POLICE HQ\nmax units {maxUnits}";
        CrimeDebugDraw.Label(pos + Vector3.up * 2f, text, CrimeDebugDraw.PoliceColor);

        for (int i = 0; i < queuedCrimes.Count; i++)
        {
            CrimeDebugDraw.DashedLine(pos, queuedCrimes[i].location, CrimeDebugDraw.HearingColor);
        }
    }
}
