using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class ActorHealth : MonoBehaviour
{
    // Registries so other systems can iterate actors / bodies without physics overlap queries.
    private static readonly List<ActorHealth> all = new List<ActorHealth>();
    private static readonly List<ActorHealth> dead = new List<ActorHealth>();

    /// <summary>Every active actor with health in the scene.</summary>
    public static IReadOnlyList<ActorHealth> All => all;

    /// <summary>Every currently dead actor (bodies waiting to be found / revived).</summary>
    public static IReadOnlyList<ActorHealth> Dead => dead;

    public static event Action<ActorHealth, GameObject> OnAnyDied;
    public static event Action<ActorHealth> OnAnyRevived;

    [Header("Health")]
    [Tooltip("Maximum hit points.")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [Tooltip("Starting hit points (clamped to Max Health).")]
    [SerializeField, Min(0f)] private float currentHealth = 100f;

    [Header("Death Physics")]
    [Tooltip("Push applied at the upper body on death so the capsule topples away from the attacker.")]
    [SerializeField, Min(0f)] private float deathImpulseForce = 2.5f;
    [SerializeField, Min(0f)] private float deathTorqueForce = 3f;
    [SerializeField, Min(0f)] private float deathLinearDamping = 2.5f;
    [SerializeField, Min(0f)] private float deathAngularDamping = 3.5f;
    [Tooltip("Height above the pivot where the death impulse is applied.")]
    [SerializeField] private float toppleForceHeight = 1.1f;

    [Header("Death Crime Scene")]
    [Tooltip("Emit a crime when this actor dies so NPCs nearby can perceive it.")]
    [SerializeField] private bool emitDeadBodyCrime = true;
    [Tooltip("When the killer is known, emit a Murder (witnesses who see the killer give police a direct sighting). " +
             "Otherwise a DeadBody crime is emitted.")]
    [SerializeField] private bool reportKillerAsMurder = true;
    [Tooltip("How far the death can be heard.")]
    [SerializeField, Min(0f)] private float deadBodyCrimeRadius = 12f;

    [Header("Revive")]
    [SerializeField] private bool reviveToFullHealth = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private GizmoDrawMode gizmoMode = GizmoDrawMode.Selected;

    [Header("Runtime (read only)")]
    [ReadOnly, SerializeField] private int deathCount;
    [ReadOnly, SerializeField] private string lastAttacker;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float Health01 => maxHealth > 0f ? currentHealth / maxHealth : 0f;
    public bool IsDead => currentHealth <= 0f;

    /// <summary>Increments every death, so "already reported this body" checks reset after a revive.</summary>
    public int DeathCount => deathCount;

    public event Action<ActorHealth, GameObject> Died;
    public event Action<ActorHealth> Revived;

    private bool hasDied;
    private bool createdRigidbodyOnDeath;

    private void Awake()
    {
        if (maxHealth < 1f)
        {
            maxHealth = 1f;
        }

        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        hasDied = currentHealth <= 0f;
    }

    private void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
        if (IsDead && !dead.Contains(this)) dead.Add(this);
    }

    private void OnDisable()
    {
        all.Remove(this);
        dead.Remove(this);
    }

    public void TakeDamage(float amount, GameObject source = null)
    {
        if (hasDied || IsDead)
        {
            return;
        }

        float damage = Mathf.Max(0f, amount);
        currentHealth = Mathf.Max(0f, currentHealth - damage);
        if (source)
        {
            lastAttacker = source.name;
        }

        if (debugLogs)
        {
            Debug.Log($"[Health] '{name}' took {damage:0.##} damage from '{(source ? source.name : "Unknown")}'. HP: {currentHealth:0.##}/{maxHealth:0.##}", this);
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

        currentHealth = Mathf.Min(maxHealth, currentHealth + Mathf.Max(0f, amount));
    }

    private void OnDeath(GameObject source)
    {
        if (hasDied)
        {
            return;
        }

        hasDied = true;
        currentHealth = 0f;
        deathCount++;
        if (!dead.Contains(this)) dead.Add(this);

        if (debugLogs)
        {
            Debug.Log($"[Health] '{name}' died. Last hit by '{(source ? source.name : "Unknown")}'.", this);
        }

        foreach (NPCPoliceCallCountdown countdown in GetComponentsInChildren<NPCPoliceCallCountdown>(true))
        {
            countdown.CancelPendingCall();
        }

        // Disable gameplay behaviours so dead actors can no longer act/report/chase.
        DisableBehaviourIfPresent<NPCWander>();
        DisableBehaviourIfPresent<NPCPerception>();
        DisableBehaviourIfPresent<NPCPoliceCallCountdown>();
        DisableBehaviourIfPresent<PunchHitReceiver>();
        DisableBehaviourIfPresent<PoliceUnit>();
        DisableBehaviourIfPresent<ParamedicUnit>();

        NavMeshAgent navAgent = GetComponent<NavMeshAgent>();
        if (navAgent)
        {
            if (navAgent.isOnNavMesh) navAgent.isStopped = true;
            navAgent.enabled = false;
        }

        // Let the body fall naturally after death.
        Rigidbody rb = GetComponent<Rigidbody>();
        createdRigidbodyOnDeath = !rb;
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

        Died?.Invoke(this, source);
        OnAnyDied?.Invoke(this, source);

        // Emit the crime last, after behaviours are disabled, so the victim itself never reacts to it.
        if (emitDeadBodyCrime && CrimeManager.Instance != null)
        {
            bool killerKnown = reportKillerAsMurder && source && source != gameObject;
            CrimeEvent bodyCrime = killerKnown
                ? new CrimeEvent(transform.position, CrimeType.Murder, source.transform, deadBodyCrimeRadius, victim: transform)
                : new CrimeEvent(transform.position, CrimeType.DeadBody, null, deadBodyCrimeRadius, victim: transform);

            CrimeManager.Instance.CommitCrime(bodyCrime);
        }
    }

    public void Revive()
    {
        if (!hasDied && currentHealth > 0f)
        {
            return;
        }

        hasDied = false;
        currentHealth = reviveToFullHealth ? maxHealth : Mathf.Max(1f, maxHealth * 0.5f);
        dead.Remove(this);

        if (debugLogs)
        {
            Debug.Log($"[Health] '{name}' has been revived. HP: {currentHealth:0.##}/{maxHealth:0.##}", this);
        }

        // Restore physics first so the NavMeshAgent can take control again.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb)
        {
            if (createdRigidbodyOnDeath)
            {
                Destroy(rb);
                createdRigidbodyOnDeath = false;
            }
            else
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
            }
        }

        // Stand back up.
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        NavMeshAgent navAgent = GetComponent<NavMeshAgent>();
        if (navAgent)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
            }

            navAgent.enabled = true;
            if (navAgent.isOnNavMesh)
            {
                navAgent.Warp(transform.position);
                navAgent.isStopped = false;
            }
        }

        // Re-enable gameplay behaviours (after the agent is valid again).
        EnableBehaviourIfPresent<NPCWander>();
        EnableBehaviourIfPresent<NPCPerception>();
        EnableBehaviourIfPresent<NPCPoliceCallCountdown>();
        EnableBehaviourIfPresent<PunchHitReceiver>();
        EnableBehaviourIfPresent<PoliceUnit>();
        EnableBehaviourIfPresent<ParamedicUnit>();

        if (CrimeManager.Instance != null)
        {
            CrimeManager.Instance.MarkVictimRevived(transform);
        }

        Revived?.Invoke(this);
        OnAnyRevived?.Invoke(this);
    }

    private void DisableBehaviourIfPresent<T>() where T : Behaviour
    {
        T component = GetComponent<T>();
        if (component)
        {
            component.enabled = false;
        }
    }

    private void EnableBehaviourIfPresent<T>() where T : Behaviour
    {
        T component = GetComponent<T>();
        if (component)
        {
            component.enabled = true;
        }
    }

    private void ApplyDeathImpulseAndTorque(Rigidbody rb, GameObject source)
    {
        Vector3 awayFromHit = source ? transform.position - source.transform.position : -transform.forward;
        awayFromHit.y = 0f;

        if (awayFromHit.sqrMagnitude < 0.0001f)
        {
            awayFromHit = -transform.forward;
        }

        awayFromHit.Normalize();

        // Apply force at upper body point to create a strong toppling moment instead of floor sliding.
        Vector3 impactPoint = transform.position + Vector3.up * toppleForceHeight;
        rb.AddForceAtPosition(awayFromHit * deathImpulseForce, impactPoint, ForceMode.Impulse);

        // Stronger tipping torque makes capsule/cylinder visibly collapse.
        Vector3 tipAxis = Vector3.Cross(Vector3.up, awayFromHit);
        if (tipAxis.sqrMagnitude < 0.0001f)
        {
            tipAxis = transform.right;
        }

        rb.AddTorque(tipAxis.normalized * deathTorqueForce, ForceMode.Impulse);
    }

    private void OnDrawGizmos()
    {
        if (gizmoMode == GizmoDrawMode.Always) DrawHealthGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        if (gizmoMode == GizmoDrawMode.Selected) DrawHealthGizmo();
    }

    private void DrawHealthGizmo()
    {
        float hp01 = Application.isPlaying ? Health01 : Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));
        Color color = IsDead ? CrimeDebugDraw.MutedColor : Color.Lerp(CrimeDebugDraw.AlertColor, CrimeDebugDraw.OkColor, hp01);
        string text = IsDead ? $"DEAD (x{deathCount})" : $"HP {currentHealth:0}/{maxHealth:0}";
        CrimeDebugDraw.Label(transform.position + Vector3.up * 2.4f, text, color);

        // Toppling force point.
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * toppleForceHeight, 0.08f);
    }
}
