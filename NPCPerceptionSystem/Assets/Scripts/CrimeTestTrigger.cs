using UnityEngine;

/// <summary>Debug keys to commit test crimes at this object's position.</summary>
[DisallowMultipleComponent]
public class CrimeTestTrigger : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Commits the crime type below with this object as suspect.")]
    [SerializeField] private KeyCode reportCrimeKey = KeyCode.C;
    [Tooltip("Commits a DeadBody crime here (no killer known).")]
    [SerializeField] private KeyCode deadBodyTestKey = KeyCode.G;

    [Header("Crime Data")]
    [SerializeField] private CrimeType crimeType = CrimeType.Assault;
    [SerializeField, Min(0f)] private float soundRadius = 10f;
    [SerializeField] private Vector3 locationOffset = Vector3.zero;
    [SerializeField, Min(0f)] private float deadBodySceneRadius = 12f;

    [Header("References")]
    [Tooltip("Suspect for test crimes. Defaults to this transform.")]
    [SerializeField] private Transform suspectOverride;

    private bool warnedMissingManager;

    private void Update()
    {
        bool crimeKey = Input.GetKeyDown(reportCrimeKey);
        bool bodyKey = Input.GetKeyDown(deadBodyTestKey);
        if (!crimeKey && !bodyKey)
        {
            return;
        }

        if (CrimeManager.Instance == null)
        {
            if (!warnedMissingManager)
            {
                Debug.LogWarning("[CrimeTestTrigger] No CrimeManager instance found in scene.", this);
                warnedMissingManager = true;
            }

            return;
        }

        Transform suspect = suspectOverride ? suspectOverride : transform;
        Vector3 crimeLocation = transform.position + locationOffset;

        if (crimeKey)
        {
            var crime = new CrimeEvent(crimeLocation, crimeType, suspect, soundRadius);
            CrimeManager.Instance.CommitCrime(crime);
            Debug.Log($"[CrimeTestTrigger] Test '{crimeType}' committed with key '{reportCrimeKey}' (radius {soundRadius:0.##}).", this);
        }

        if (bodyKey)
        {
            var crime = new CrimeEvent(crimeLocation, CrimeType.DeadBody, null, deadBodySceneRadius, victim: transform);
            CrimeManager.Instance.CommitCrime(crime);
            Debug.Log($"[CrimeTestTrigger] Test 'DeadBody' committed with key '{deadBodyTestKey}' (radius {deadBodySceneRadius:0.##}).", this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 p = transform.position + locationOffset + Vector3.up * 0.05f;
        CrimeDebugDraw.Circle(p, soundRadius, CrimeDebugDraw.HearingColor);
        CrimeDebugDraw.Circle(p, deadBodySceneRadius, CrimeDebugDraw.MutedColor);
        CrimeDebugDraw.Label(p + Vector3.up * 0.5f, $"[{reportCrimeKey}] {crimeType}  [{deadBodyTestKey}] body", CrimeDebugDraw.HearingColor);
    }
}
