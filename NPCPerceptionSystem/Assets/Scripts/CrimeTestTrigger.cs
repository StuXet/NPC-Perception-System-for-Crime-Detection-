using UnityEngine;

[DisallowMultipleComponent]
public class CrimeTestTrigger : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode reportCrimeKey = KeyCode.C;
    [SerializeField] private KeyCode deadBodyTestKey = KeyCode.G;

    [Header("Crime Data")]
    [SerializeField] private CrimeType crimeType = CrimeType.Assault;
    [SerializeField] private float soundRadius = 10f;
    [SerializeField] private Vector3 locationOffset = Vector3.zero;
    [SerializeField] private float deadBodySceneRadius = 12f;

    [Header("References")]
    [SerializeField] private Transform suspectOverride;

    private void Update()
    {
        if (CrimeManager.Instance == null)
        {
            Debug.LogWarning("[CrimeTestTrigger] No CrimeManager instance found in scene.");
            return;
        }

        if (Input.GetKeyDown(reportCrimeKey))
        {
            CommitCrime(crimeType, soundRadius, reportCrimeKey);
        }

        if (Input.GetKeyDown(deadBodyTestKey))
        {
            CommitCrime(CrimeType.DeadBody, deadBodySceneRadius, deadBodyTestKey);
        }
    }

    private void CommitCrime(CrimeType type, float radius, KeyCode key)
    {
        Transform suspect = suspectOverride ? suspectOverride : transform;
        Vector3 crimeLocation = transform.position + locationOffset;
        CrimeEvent crime = new CrimeEvent(crimeLocation, type, suspect, radius);

        CrimeManager.Instance.CommitCrime(crime);
        Debug.Log($"[CrimeTestTrigger] Test crime '{type}' committed with key '{key}' (radius: {radius:0.##}).");
    }
}
