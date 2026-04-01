using UnityEngine;

[DisallowMultipleComponent]
public class PoliceDispatcher : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PoliceUnit policePrefab;
    [SerializeField] private Transform spawnPoint;

    [Header("Dispatch")]
    [SerializeField] private bool spawnNewUnitPerReport = false;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private PoliceUnit activeUnit;

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
        PoliceUnit unit = GetOrCreateUnit();
        if (!unit)
        {
            Debug.LogWarning("[PoliceDispatcher] No police unit available to dispatch.");
            return;
        }

        unit.GoToCrime(crime);

        if (debugLogs)
        {
            Debug.Log($"[PoliceDispatcher] Dispatched '{unit.name}' for {crime.type} at {crime.location}.");
        }
    }

    private PoliceUnit GetOrCreateUnit()
    {
        if (spawnNewUnitPerReport || activeUnit == null)
        {
            if (!policePrefab)
            {
                return null;
            }

            Vector3 pos = spawnPoint ? spawnPoint.position : transform.position;
            Quaternion rot = spawnPoint ? spawnPoint.rotation : transform.rotation;
            PoliceUnit created = Instantiate(policePrefab, pos, rot);

            if (!spawnNewUnitPerReport)
            {
                activeUnit = created;
            }

            return created;
        }

        return activeUnit;
    }
}
