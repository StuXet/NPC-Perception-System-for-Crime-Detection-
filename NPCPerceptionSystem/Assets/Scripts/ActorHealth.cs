using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class ActorHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    [Header("Death Physics")]
    [SerializeField] private float deathImpulseForce = 2.5f;
    [SerializeField] private float deathTorqueForce = 3f;
    [SerializeField] private float deathLinearDamping = 2.5f;
    [SerializeField] private float deathAngularDamping = 3.5f;
    [SerializeField] private float toppleForceHeight = 1.1f;

    [Header("Death Crime Scene")]
    [SerializeField] private bool emitDeadBodyCrime = true;
    [SerializeField] private float deadBodyCrimeRadius = 12f;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0f;
    private bool hasDied;

    private void Awake()
    {
        if (maxHealth < 1f)
        {
            maxHealth = 1f;
        }

        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        hasDied = currentHealth <= 0f;
    }

    public void TakeDamage(float amount, GameObject source = null)
    {
        if (hasDied || IsDead)
        {
            return;
        }

        float damage = Mathf.Max(0f, amount);
        currentHealth = Mathf.Max(0f, currentHealth - damage);

        if (debugLogs)
        {
            string sourceName = source ? source.name : "Unknown";
            Debug.Log($"[Health] '{name}' took {damage:0.##} damage from '{sourceName}'. HP: {currentHealth:0.##}/{maxHealth:0.##}");
        }

        if (currentHealth <= 0f)
        {
            OnDeath(source);
        }
    }

    public void TakeDamage(float amount)
    {
        TakeDamage(amount, null);
    }

    public void Heal(float amount)
    {
        if (IsDead)
        {
            return;
        }

        float heal = Mathf.Max(0f, amount);
        currentHealth = Mathf.Min(maxHealth, currentHealth + heal);
    }

    private void OnDeath(GameObject source)
    {
        if (hasDied)
        {
            return;
        }

        hasDied = true;
        currentHealth = 0f;

        if (debugLogs)
        {
            string sourceName = source ? source.name : "Unknown";
            Debug.Log($"[Health] '{name}' died. Last hit by '{sourceName}'.");
        }

        foreach (NPCPoliceCallCountdown countdown in GetComponentsInChildren<NPCPoliceCallCountdown>(true))
        {
            countdown.CancelPendingCall();
        }

        if (emitDeadBodyCrime && CrimeManager.Instance != null)
        {
            // For DeadBody crimes, suspect carries the body transform so witnesses can track the actual corpse location.
            CrimeEvent bodyCrime = new CrimeEvent(transform.position, CrimeType.DeadBody, transform, deadBodyCrimeRadius);
            CrimeManager.Instance.CommitCrime(bodyCrime);
        }

        // Disable gameplay behaviors so dead NPCs can no longer act/report/chase.
        DisableBehaviourIfPresent<NPCWander>();
        DisableBehaviourIfPresent<NPCPerception>();
        DisableBehaviourIfPresent<NPCPoliceCallCountdown>();
        DisableBehaviourIfPresent<PunchHitReceiver>();
        DisableBehaviourIfPresent<PoliceUnit>();

        NavMeshAgent navAgent = GetComponent<NavMeshAgent>();
        if (navAgent)
        {
            navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        // Let the body fall naturally after death.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (!rb)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.None;
        rb.drag = deathLinearDamping;
        rb.angularDrag = deathAngularDamping;
        rb.maxAngularVelocity = 40f;

        ApplyDeathImpulseAndTorque(rb, source);
    }

    private void DisableBehaviourIfPresent<T>() where T : Behaviour
    {
        T component = GetComponent<T>();
        if (component)
        {
            component.enabled = false;
        }
    }

    private void ApplyDeathImpulseAndTorque(Rigidbody rb, GameObject source)
    {
        Vector3 awayFromHit;
        if (source)
        {
            awayFromHit = transform.position - source.transform.position;
            awayFromHit.y = 0f;
        }
        else
        {
            awayFromHit = -transform.forward;
        }

        if (awayFromHit.sqrMagnitude < 0.0001f)
        {
            awayFromHit = -transform.forward;
        }

        awayFromHit.Normalize();

        // Apply force at upper body point to create a strong toppling moment instead of floor sliding.
        Vector3 impactPoint = transform.position + Vector3.up * toppleForceHeight;
        Vector3 impulse = awayFromHit * deathImpulseForce;
        rb.AddForceAtPosition(impulse, impactPoint, ForceMode.Impulse);

        // Stronger tipping torque makes capsule/cylinder visibly collapse.
        Vector3 tipAxis = Vector3.Cross(Vector3.up, awayFromHit).normalized;
        if (tipAxis.sqrMagnitude < 0.0001f)
        {
            tipAxis = transform.right;
        }

        rb.AddTorque(tipAxis * deathTorqueForce, ForceMode.Impulse);
    }

}
