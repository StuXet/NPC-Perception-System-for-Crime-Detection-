using UnityEngine;

[DisallowMultipleComponent]
public class ParamedicDispatcher : MonoBehaviour
{
    public static ParamedicDispatcher Instance { get; private set; }

    [Header("References")]
    [SerializeField] private ParamedicUnit paramedicPrefab;
    [SerializeField] private Transform spawnPoint;

    [Header("Dispatch")]
    [SerializeField] private bool spawnNewUnitPerDispatch = false;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private ParamedicUnit activeUnit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void DispatchToDeadBody(Transform deadBodyTarget)
    {
        if (!deadBodyTarget)
        {
            return;
        }

        ActorHealth health = deadBodyTarget.GetComponent<ActorHealth>();
        if (!health || !health.IsDead)
        {
            return;
        }

        ParamedicUnit unit = GetOrCreateUnit();
        if (!unit)
        {
            Debug.LogWarning("[ParamedicDispatcher] No paramedic unit available.");
            return;
        }

        unit.GoToDeadBody(deadBodyTarget);

        if (debugLogs)
        {
            Debug.Log($"[ParamedicDispatcher] Dispatched '{unit.name}' to revive '{deadBodyTarget.name}'.");
        }
    }

    private ParamedicUnit GetOrCreateUnit()
    {
        if (spawnNewUnitPerDispatch || activeUnit == null)
        {
            if (!paramedicPrefab)
            {
                return null;
            }

            Vector3 pos = spawnPoint ? spawnPoint.position : transform.position;
            Quaternion rot = spawnPoint ? spawnPoint.rotation : transform.rotation;
            ParamedicUnit created = Instantiate(paramedicPrefab, pos, rot);

            if (!spawnNewUnitPerDispatch)
            {
                activeUnit = created;
            }

            return created;
        }

        return activeUnit;
    }
}
