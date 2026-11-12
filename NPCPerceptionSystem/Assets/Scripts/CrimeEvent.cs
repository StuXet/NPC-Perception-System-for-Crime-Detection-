using UnityEngine;

public enum CrimeType
{
    Assault,
    Murder,
    DeadBody
}

/// <summary>
/// How a witness learned about a crime. Drives whether police get a direct sighting (last known position) or must investigate.
/// </summary>
public enum DetectionReason
{
    None,
    Sight,
    HeardThenSaw,
    HeardThenLooked,
    Victim,
    DeadBodySight,
    DeadBodyInvestigate,
    DeadBodyAutoScan
}

[System.Serializable]
public struct CrimeEvent
{
    public Vector3 location;
    public CrimeType type;

    [Tooltip("Who committed the crime (attacker / killer). Null when unknown, e.g. a body found without a known killer.")]
    public Transform suspect;

    [Tooltip("Who the crime was committed against. For Murder / DeadBody this is the body.")]
    public Transform victim;

    public Transform reporter;
    public float timestamp;
    public float soundRadius;

    [Tooltip("True when a witness actually saw the suspect, so police can head straight for the last known position.")]
    public bool directPolicePursuit;

    public bool hasSuspectLastKnownPosition;
    public Vector3 suspectLastKnownPosition;

    [Tooltip("Assigned by CrimeManager. 0 = not yet tracked.")]
    public int incidentId;

    public DetectionReason reason;

    public CrimeEvent(
        Vector3 location,
        CrimeType type,
        Transform suspect,
        float soundRadius = 10f,
        bool directPolicePursuit = false,
        Transform victim = null)
    {
        this.location = location;
        this.type = type;
        this.suspect = suspect;
        this.victim = victim;
        this.reporter = null;
        this.timestamp = Time.time;
        this.soundRadius = soundRadius;
        this.directPolicePursuit = directPolicePursuit;
        this.hasSuspectLastKnownPosition = false;
        this.suspectLastKnownPosition = Vector3.zero;
        this.incidentId = 0;
        this.reason = DetectionReason.None;
    }

    /// <summary>True for crimes that leave a body behind (needs paramedic + scene investigation).</summary>
    public bool IsBodyCrime => type == CrimeType.Murder || type == CrimeType.DeadBody;

    /// <summary>Higher value = more urgent. Used by dispatchers to queue / reassign units.</summary>
    public int Priority => GetPriority(type, directPolicePursuit);

    public static int GetPriority(CrimeType type, bool directPursuit)
    {
        int basePriority;
        switch (type)
        {
            case CrimeType.Murder: basePriority = 30; break;
            case CrimeType.Assault: basePriority = 20; break;
            default: basePriority = 10; break;
        }

        return basePriority + (directPursuit ? 5 : 0);
    }

    public void SetSuspectSighting(Vector3 position)
    {
        hasSuspectLastKnownPosition = true;
        suspectLastKnownPosition = position;
    }
}
