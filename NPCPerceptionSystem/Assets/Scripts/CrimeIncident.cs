using System.Collections.Generic;
using UnityEngine;

public enum IncidentState
{
    Undiscovered,
    Reported,
    Responding,
    Investigating,
    Chasing,
    Searching,
    Resolved
}

public enum IncidentOutcome
{
    None,
    SuspectArrested,
    PlayerBusted,
    SuspectEscaped,
    NoSuspectFound,
    Abandoned
}

/// <summary>
/// One tracked crime from the moment it happens until police resolve it.
/// Records timings so discovery delay and response time can be measured.
/// </summary>
[System.Serializable]
public class CrimeIncident
{
    public int id;
    public CrimeType type;
    public IncidentState state;
    public IncidentOutcome outcome;

    public Vector3 location;
    public Transform suspect;
    public Transform victim;

    [Header("Timeline (Time.time, -1 = not yet)")]
    public float committedTime;
    public float lastActivityTime;
    public float reportedTime = -1f;
    public float policeArrivedTime = -1f;
    public float resolvedTime = -1f;
    public float victimRevivedTime = -1f;

    [Header("Witnesses")]
    public DetectionReason discoveryReason;
    public Transform firstReporter;
    public int reportCount;
    public bool directPursuitRequested;
    public bool hasSuspectLastKnownPosition;
    public Vector3 suspectLastKnownPosition;
    public List<Transform> witnesses = new List<Transform>();

    [Tooltip("False when the incident was only created by a report (no CommitCrime call), so the committed time is unknown.")]
    public bool committedTimeKnown = true;

    public bool IsOpen => state != IncidentState.Resolved;
    public bool IsDiscovered => reportedTime >= 0f;

    /// <summary>Seconds between the crime and the first report. -1 while undiscovered.</summary>
    public float DiscoveryDelay => IsDiscovered && committedTimeKnown ? reportedTime - committedTime : -1f;

    /// <summary>Seconds between the first report and police arriving. -1 until police arrive.</summary>
    public float ResponseDelay => IsDiscovered && policeArrivedTime >= 0f ? policeArrivedTime - reportedTime : -1f;

    public float Age => Time.time - committedTime;

    public string Label => $"#{id} {type}";
}
