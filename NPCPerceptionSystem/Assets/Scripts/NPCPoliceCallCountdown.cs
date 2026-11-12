using System.Collections;
using UnityEngine;

/// <summary>
/// Simulates the time it takes a witness to call the police.
/// New information restarts the countdown; the most serious crime seen so far is the one that gets reported.
/// </summary>
[DisallowMultipleComponent]
public class NPCPoliceCallCountdown : MonoBehaviour
{
    [Tooltip("Seconds between witnessing something and the call reaching the police.")]
    [SerializeField, Min(0f)] private float callDelaySeconds = 5f;

    [Tooltip("Restart the countdown whenever new information arrives (e.g. punched again).")]
    [SerializeField] private bool restartOnNewInfo = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    [Header("Runtime (read only)")]
    [ReadOnly, SerializeField] private bool isCalling;
    [ReadOnly, SerializeField] private float timeRemaining;
    [ReadOnly, SerializeField] private CrimeType pendingType;
    [ReadOnly, SerializeField] private string pendingReason = string.Empty;

    private Coroutine countdownRoutine;
    private CrimeEvent pendingCrime;
    private float callTime;
    private ActorHealth cachedHealth;
    private NPCPerception perception;

    public bool IsCalling => countdownRoutine != null;
    public float TimeRemaining => IsCalling ? Mathf.Max(0f, callTime - Time.time) : 0f;
    public float CallDelaySeconds => callDelaySeconds;
    public CrimeType PendingType => pendingCrime.type;

    private void Awake()
    {
        cachedHealth = GetComponentInParent<ActorHealth>();
        perception = GetComponent<NPCPerception>();
    }

    private void OnDisable()
    {
        CancelPendingCall();
    }

    private void Update()
    {
        isCalling = IsCalling;
        timeRemaining = TimeRemaining;
    }

    /// <summary>
    /// Stores the crime payload and (re)starts the delayed report timer.
    /// A less serious crime never replaces a more serious pending one.
    /// </summary>
    public void SchedulePoliceCall(CrimeEvent crime, string reason = "")
    {
        if (!isActiveAndEnabled || (cachedHealth && cachedHealth.IsDead))
        {
            return;
        }

        bool replacePending = !IsCalling || crime.Priority >= pendingCrime.Priority;
        if (replacePending)
        {
            pendingCrime = crime;
            pendingReason = reason;
            pendingType = crime.type;
        }
        else if (crime.hasSuspectLastKnownPosition && crime.suspect == pendingCrime.suspect)
        {
            // Keep the serious crime but take the fresher suspect position.
            pendingCrime.SetSuspectSighting(crime.suspectLastKnownPosition);
        }

        if (IsCalling && !restartOnNewInfo)
        {
            return;
        }

        if (countdownRoutine != null)
        {
            StopCoroutine(countdownRoutine);
        }

        callTime = Time.time + callDelaySeconds;
        countdownRoutine = StartCoroutine(CountdownRoutine());

        if (debugLogs)
        {
            Debug.Log($"[NPCPoliceCallCountdown] '{name}' calling police in {callDelaySeconds:0.#}s about {pendingCrime.type}. {reason}", this);
        }
    }

    public void CancelPendingCall()
    {
        if (countdownRoutine != null)
        {
            StopCoroutine(countdownRoutine);
            countdownRoutine = null;
        }
    }

    /// <summary>
    /// Waits the configured delay, reports the pending crime, then notifies perception so the NPC can move on.
    /// </summary>
    private IEnumerator CountdownRoutine()
    {
        while (Time.time < callTime)
        {
            yield return null;
        }

        countdownRoutine = null;

        if (cachedHealth && cachedHealth.IsDead)
        {
            yield break;
        }

        // If the witness still sees the suspect, the operator gets a fresh position.
        if (perception)
        {
            perception.TryRefreshSuspectSighting(ref pendingCrime);
        }

        if (CrimeManager.Instance != null)
        {
            CrimeManager.Instance.ReportCrime(pendingCrime, transform);

            if (debugLogs)
            {
                Debug.Log($"[NPCPoliceCallCountdown] '{name}' called police: {pendingCrime.type}. {pendingReason}", this);
            }
        }

        if (perception)
        {
            perception.NotifyReportSentToPolice();
        }
    }
}
