using UnityEngine;

[DisallowMultipleComponent]
public class CrimeTestTrigger : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode reportCrimeKey = KeyCode.C;

    [Header("Crime Data")]
    [SerializeField] private CrimeType crimeType = CrimeType.Assault;
    [SerializeField] private float soundRadius = 10f;
    [SerializeField] private Vector3 locationOffset = Vector3.zero;

    [Header("References")]
    [SerializeField] private Transform suspectOverride;

    private void Update()
    {
        if (!Input.GetKeyDown(reportCrimeKey))
        {
            return;
        }

        if (CrimeManager.Instance == null)
        {
            Debug.LogWarning("[CrimeTestTrigger] No CrimeManager instance found in scene.");
            return;
        }

        Transform suspect = suspectOverride ? suspectOverride : transform;
        Vector3 crimeLocation = transform.position + locationOffset;
        CrimeEvent crime = new CrimeEvent(crimeLocation, crimeType, suspect, soundRadius);

        CrimeManager.Instance.CommitCrime(crime);
        Debug.Log($"[CrimeTestTrigger] Test crime committed with key '{reportCrimeKey}'.");
    }
}
