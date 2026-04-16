using System.Collections;
using UnityEngine;


[DisallowMultipleComponent]
public class NPCPoliceCallCountdown : MonoBehaviour
{
    [SerializeField] private float callDelaySeconds = 5f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private Coroutine countdownRoutine;
    private CrimeEvent pendingCrime;
    private string pendingReason = string.Empty;

    private void OnDisable()
    {
        if (countdownRoutine != null)
        {
            StopCoroutine(countdownRoutine);
            countdownRoutine = null;
        }
    }

    /// <summary>
    /// Stores latest crime payload and (re)starts delayed report timer so repeated events reset the countdown.
    /// </summary>
    public void SchedulePoliceCall(CrimeEvent crime, string reason = "")
    {
        pendingCrime = crime;
        pendingReason = reason;

        if (countdownRoutine != null)
        {
            // Any new witness update restarts the countdown from zero.
            StopCoroutine(countdownRoutine);
        }

        countdownRoutine = StartCoroutine(CountdownRoutine());

        if (debugLogs)
        {
            Debug.Log($"[NPCPoliceCallCountdown] '{name}' police call scheduled in {callDelaySeconds}s (reset). {reason}");
        }
    }

    /// <summary>
    /// Waits configured delay, reports pending crime, then notifies perception cooldown state.
    /// </summary>
    private IEnumerator CountdownRoutine()
    {
        yield return new WaitForSeconds(callDelaySeconds);

        if (CrimeManager.Instance != null)
        {
            CrimeManager.Instance.ReportCrime(pendingCrime, transform);

            if (debugLogs)
            {
                Debug.Log($"[NPCPoliceCallCountdown] '{name}' called police: {pendingCrime.type}. {pendingReason}");
            }
        }

        GetComponent<NPCPerception>()?.NotifyReportSentToPolice();

        countdownRoutine = null;
    }
}
