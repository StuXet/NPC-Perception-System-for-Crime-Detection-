using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central crime hub.
/// CommitCrime  -> a crime happened (NPCs may perceive it).
/// ReportCrime  -> a witness called it in (dispatchers respond).
/// Every crime is tracked as a <see cref="CrimeIncident"/> so repeated reports of the same crime are merged
/// and discovery / response times can be measured.
/// </summary>
[DisallowMultipleComponent]
public class CrimeManager : MonoBehaviour
{
    public static CrimeManager Instance { get; private set; }

    /// <summary>A crime happened in the world. NPC perception listens to this.</summary>
    public static event Action<CrimeEvent> OnCrimeCommitted;

    /// <summary>
    /// A crime was reported to police. Fired once per incident, plus once more if a later witness
    /// upgrades it to a direct sighting of the suspect.
    /// </summary>
    public static event Action<CrimeEvent> OnCrimeReported;

    /// <summary>Any incident was created or changed state.</summary>
    public static event Action<CrimeIncident> OnIncidentChanged;

    [Header("Incident Merging")]
    [Tooltip("Assaults by the same suspect within this many seconds are merged into one incident.")]
    [SerializeField, Min(0f)] private float assaultMergeWindow = 8f;

    [Tooltip("Assaults by the same suspect within this distance are merged into one incident.")]
    [SerializeField, Min(0f)] private float assaultMergeRadius = 6f;

    [Tooltip("How many resolved incidents to keep in the list for stats/debugging.")]
    [SerializeField, Min(1)] private int maxResolvedIncidentsKept = 30;

    [Header("Debug Marker")]
    [Tooltip("Spawn a small cube in the game view where a crime gets reported.")]
    [SerializeField] private bool drawDebugMarker = true;
    [SerializeField, Min(0.1f)] private float markerLifetime = 6f;
    [SerializeField, Min(0.05f)] private float markerScale = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [Tooltip("Automatically add a CrimeDebugHUD (F1 overlay) if none exists in the scene.")]
    [SerializeField] private bool createDebugHud = true;
    [SerializeField] private GizmoDrawMode gizmoMode = GizmoDrawMode.Always;
    [Tooltip("Draw resolved incidents too (faded).")]
    [SerializeField] private bool drawResolvedIncidents = false;

    [Header("Runtime (read only)")]
    [ReadOnly, SerializeField] private int openIncidentCount;
    [ReadOnly, SerializeField] private List<CrimeIncident> incidents = new List<CrimeIncident>();

    private int nextIncidentId = 1;

    public IReadOnlyList<CrimeIncident> Incidents => incidents;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Only remove this component, never the GameObject (it may host other systems such as a dispatcher).
            Debug.LogWarning(
                $"[CrimeManager] Duplicate CrimeManager on '{name}' (already on '{Instance.name}'). " +
                "Removing the duplicate component - please delete it from the scene.", this);
            Destroy(this);
            return;
        }

        Instance = this;

        if (createDebugHud && !FindObjectOfType<CrimeDebugHUD>())
        {
            gameObject.AddComponent<CrimeDebugHUD>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Registers a crime that just happened and notifies perceiving NPCs.
    /// Returns the crime with its incident id filled in.
    /// </summary>
    public CrimeEvent CommitCrime(CrimeEvent crime)
    {
        CrimeIncident incident = FindMatchingOpenIncident(crime);
        if (incident == null)
        {
            incident = CreateIncident(crime, committedTimeKnown: true);
            Log($"Crime committed: {incident.Label} at {crime.location} suspect '{NameOf(crime.suspect)}' victim '{NameOf(crime.victim)}'");
        }
        else
        {
            // Same crime continuing (e.g. repeated punches). Keep the incident, refresh its data.
            incident.location = crime.location;
            incident.lastActivityTime = Time.time;
            if (crime.type == CrimeType.Murder && incident.type != CrimeType.Murder)
            {
                incident.type = CrimeType.Murder;
                incident.suspect = crime.suspect;
            }

            Log($"Crime continued: {incident.Label} at {crime.location}");
        }

        crime.incidentId = incident.id;
        NotifyIncidentChanged(incident);
        OnCrimeCommitted?.Invoke(crime);
        return crime;
    }

    /// <summary>
    /// A witness reports a crime. Repeated reports of the same incident are merged instead of re-dispatching police.
    /// </summary>
    public void ReportCrime(CrimeEvent crime, Transform reporter = null)
    {
        crime.reporter = reporter;

        CrimeIncident incident = GetIncident(crime.incidentId);
        if (incident == null || !incident.IsOpen)
        {
            CrimeIncident match = FindMatchingOpenIncident(crime);
            if (match != null)
            {
                incident = match;
            }
            else if (incident != null && !incident.IsOpen)
            {
                Log($"Late report for already resolved {incident.Label} by '{NameOf(reporter)}' ignored.");
                return;
            }
        }

        if (incident == null)
        {
            // Discovered without a matching CommitCrime (e.g. scripted body). Commit time unknown.
            incident = CreateIncident(crime, committedTimeKnown: false);
        }

        crime.incidentId = incident.id;
        incident.reportCount++;
        if (reporter && !incident.witnesses.Contains(reporter))
        {
            incident.witnesses.Add(reporter);
        }

        if (crime.hasSuspectLastKnownPosition)
        {
            incident.hasSuspectLastKnownPosition = true;
            incident.suspectLastKnownPosition = crime.suspectLastKnownPosition;
        }

        if (crime.suspect && !incident.suspect)
        {
            incident.suspect = crime.suspect;
        }

        bool firstReport = !incident.IsDiscovered;
        bool upgradedToSighting = !firstReport && crime.directPolicePursuit && !incident.directPursuitRequested;

        if (!firstReport && !upgradedToSighting)
        {
            Log($"Duplicate report of {incident.Label} by '{NameOf(reporter)}' merged ({incident.reportCount} reports).");
            NotifyIncidentChanged(incident);
            return;
        }

        if (firstReport)
        {
            incident.reportedTime = Time.time;
            incident.firstReporter = reporter;
            incident.discoveryReason = crime.reason;
            incident.state = IncidentState.Reported;

            string delay = incident.committedTimeKnown ? $"{incident.DiscoveryDelay:0.0}s after the crime" : "commit time unknown";
            Log($"Crime REPORTED: {incident.Label} by '{NameOf(reporter)}' ({crime.reason}), {delay}.");
            SpawnDebugMarker(crime.location);
        }
        else
        {
            Log($"{incident.Label} upgraded to direct sighting by '{NameOf(reporter)}'.");
        }

        if (crime.directPolicePursuit)
        {
            incident.directPursuitRequested = true;
        }

        NotifyIncidentChanged(incident);
        OnCrimeReported?.Invoke(crime);
    }

    public CrimeIncident GetIncident(int id)
    {
        if (id <= 0)
        {
            return null;
        }

        for (int i = 0; i < incidents.Count; i++)
        {
            if (incidents[i].id == id)
            {
                return incidents[i];
            }
        }

        return null;
    }

    public bool IsIncidentOpen(int id)
    {
        CrimeIncident incident = GetIncident(id);
        return incident != null && incident.IsOpen;
    }

    public void SetIncidentState(int id, IncidentState state)
    {
        CrimeIncident incident = GetIncident(id);
        if (incident == null || !incident.IsOpen || incident.state == state)
        {
            return;
        }

        incident.state = state;
        NotifyIncidentChanged(incident);
    }

    /// <summary>Marks the moment police reach the scene / suspect (used for response-time stats).</summary>
    public void MarkPoliceArrived(int id)
    {
        CrimeIncident incident = GetIncident(id);
        if (incident != null && incident.policeArrivedTime < 0f)
        {
            incident.policeArrivedTime = Time.time;
            NotifyIncidentChanged(incident);
        }
    }

    public void ResolveIncident(int id, IncidentOutcome outcome)
    {
        CrimeIncident incident = GetIncident(id);
        if (incident == null || !incident.IsOpen)
        {
            return;
        }

        incident.state = IncidentState.Resolved;
        incident.outcome = outcome;
        incident.resolvedTime = Time.time;
        Log($"{incident.Label} resolved: {outcome}.");
        NotifyIncidentChanged(incident);
        TrimResolvedIncidents();
    }

    public void MarkVictimRevived(Transform victim)
    {
        for (int i = 0; i < incidents.Count; i++)
        {
            CrimeIncident incident = incidents[i];
            if (incident.victim == victim && incident.victimRevivedTime < 0f)
            {
                incident.victimRevivedTime = Time.time;
                NotifyIncidentChanged(incident);
            }
        }
    }

    private CrimeIncident CreateIncident(CrimeEvent crime, bool committedTimeKnown)
    {
        var incident = new CrimeIncident
        {
            id = nextIncidentId++,
            type = crime.type,
            state = IncidentState.Undiscovered,
            location = crime.location,
            suspect = crime.suspect,
            victim = crime.victim,
            committedTime = committedTimeKnown ? crime.timestamp : Time.time,
            lastActivityTime = Time.time,
            committedTimeKnown = committedTimeKnown
        };

        incidents.Add(incident);
        return incident;
    }

    /// <summary>
    /// Finds an open incident describing the same crime:
    /// body crimes match on the victim, assaults match on suspect + time window + distance.
    /// </summary>
    private CrimeIncident FindMatchingOpenIncident(CrimeEvent crime)
    {
        CrimeIncident fromId = GetIncident(crime.incidentId);
        if (fromId != null && fromId.IsOpen)
        {
            return fromId;
        }

        for (int i = incidents.Count - 1; i >= 0; i--)
        {
            CrimeIncident incident = incidents[i];
            if (!incident.IsOpen)
            {
                continue;
            }

            bool incidentHasBody = incident.type == CrimeType.Murder || incident.type == CrimeType.DeadBody;
            if (crime.IsBodyCrime && incidentHasBody && crime.victim && incident.victim == crime.victim)
            {
                return incident;
            }

            if (crime.type == CrimeType.Assault && incident.type == CrimeType.Assault &&
                crime.suspect && incident.suspect == crime.suspect &&
                Time.time - incident.lastActivityTime <= assaultMergeWindow &&
                Vector3.Distance(incident.location, crime.location) <= assaultMergeRadius)
            {
                return incident;
            }
        }

        return null;
    }

    private void TrimResolvedIncidents()
    {
        int resolved = 0;
        for (int i = incidents.Count - 1; i >= 0; i--)
        {
            if (incidents[i].IsOpen)
            {
                continue;
            }

            resolved++;
            if (resolved > maxResolvedIncidentsKept)
            {
                incidents.RemoveAt(i);
            }
        }
    }

    private void NotifyIncidentChanged(CrimeIncident incident)
    {
        openIncidentCount = 0;
        for (int i = 0; i < incidents.Count; i++)
        {
            if (incidents[i].IsOpen)
            {
                openIncidentCount++;
            }
        }

        OnIncidentChanged?.Invoke(incident);
    }

    private void SpawnDebugMarker(Vector3 location)
    {
        if (!drawDebugMarker)
        {
            return;
        }

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.transform.position = location + Vector3.up * 0.5f;
        marker.transform.localScale = Vector3.one * markerScale;
        marker.name = "CrimeDebugMarker";

        var col = marker.GetComponent<Collider>();
        if (col) Destroy(col);

        Destroy(marker, markerLifetime);
    }

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log($"[CrimeManager] {message}", this);
        }
    }

    private static string NameOf(Transform t) => t ? t.name : "None";

    private void OnDrawGizmos()
    {
        if (gizmoMode == GizmoDrawMode.Always) DrawIncidentGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (gizmoMode == GizmoDrawMode.Selected) DrawIncidentGizmos();
    }

    private void DrawIncidentGizmos()
    {
        for (int i = 0; i < incidents.Count; i++)
        {
            CrimeIncident incident = incidents[i];
            if (!incident.IsOpen && !drawResolvedIncidents)
            {
                continue;
            }

            Color color = CrimeDebugDraw.GetIncidentColor(incident.state);
            if (!incident.IsOpen)
            {
                color.a = 0.35f;
            }

            Vector3 p = incident.location;
            CrimeDebugDraw.Circle(p, 1f, color, 3f);
            CrimeDebugDraw.Line(p, p + Vector3.up * 2.5f, color, 2f);

            if (incident.hasSuspectLastKnownPosition)
            {
                CrimeDebugDraw.Marker(incident.suspectLastKnownPosition + Vector3.up * 0.5f, CrimeDebugDraw.AlertColor);
                CrimeDebugDraw.DashedLine(p, incident.suspectLastKnownPosition, CrimeDebugDraw.AlertColor);
            }

            string time = incident.IsDiscovered
                ? $"found +{(incident.committedTimeKnown ? incident.DiscoveryDelay : 0f):0.0}s"
                : $"undiscovered {incident.Age:0.0}s";
            CrimeDebugDraw.Label(p + Vector3.up * 2.8f, $"{incident.Label}\n{incident.state} | {time}", color);
        }
    }
}
