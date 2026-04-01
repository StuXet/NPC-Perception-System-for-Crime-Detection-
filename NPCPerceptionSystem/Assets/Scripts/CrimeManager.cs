using System;
using UnityEngine;

[DisallowMultipleComponent]
public class CrimeManager : MonoBehaviour
{
    public static CrimeManager Instance { get; private set; }

    public static event Action<CrimeEvent> OnCrimeCommitted;
    public static event Action<CrimeEvent> OnCrimeReported;

    [Header("Debug")]
    [SerializeField] private bool drawDebugMarker = true;
    [SerializeField] private float markerLifetime = 6f;
    [SerializeField] private float markerScale = 0.4f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Duplicate CrimeManager found. Destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void ReportCrime(CrimeEvent crime, Transform reporter = null)
    {
        crime.reporter = reporter;

        Debug.Log(
            $"[CrimeManager] Crime reported: {crime.type} at {crime.location} " +
            $"by reporter '{(reporter ? reporter.name : "None")}' suspect '{(crime.suspect ? crime.suspect.name : "Unknown")}'"
        );

        if (drawDebugMarker)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.transform.position = crime.location + Vector3.up * 0.5f;
            marker.transform.localScale = Vector3.one * markerScale;
            marker.name = "CrimeDebugMarker";

            var col = marker.GetComponent<Collider>();
            if (col) Destroy(col);

            Destroy(marker, markerLifetime);
        }

        OnCrimeReported?.Invoke(crime);
    }

    public void CommitCrime(CrimeEvent crime)
    {
        Debug.Log(
            $"[CrimeManager] Crime committed: {crime.type} at {crime.location} " +
            $"suspect '{(crime.suspect ? crime.suspect.name : "Unknown")}'"
        );

        OnCrimeCommitted?.Invoke(crime);
    }
}