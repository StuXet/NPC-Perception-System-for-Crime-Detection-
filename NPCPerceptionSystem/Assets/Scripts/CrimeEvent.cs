using UnityEngine;

public enum CrimeType
{
    Assault,
    Murder,
    DeadBody
}

[System.Serializable]
public struct CrimeEvent
{
    public Vector3 location;
    public CrimeType type;
    public Transform suspect;
    public Transform reporter;
    public float timestamp;
    public float soundRadius;

    public CrimeEvent(Vector3 location, CrimeType type, Transform suspect, float soundRadius = 10f)
    {
        this.location = location;
        this.type = type;
        this.suspect = suspect;
        this.reporter = null;
        this.timestamp = Time.time;
        this.soundRadius = soundRadius;
    }
}