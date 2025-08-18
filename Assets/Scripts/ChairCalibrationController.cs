using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Linq;

public class ChairCalibrationController : MonoBehaviour
{
    [Header("Communication Settings")]
    public bool UseChairConnection = true;
    [Range(1, 60)]
    public float PackagePerSecond = 30;
    private int remotePort = 42424;
    private string remoteIP = "100.1.1.101";
    private int localPort = 42434;
    private UdpClient sender;
    private float sendRate;

    [Header("Experiment Settings")]
    public float AccelDecelDuration = 2.0f;
    public float Steady90Duration = 60.0f;
    private const int NumberOfTrials = 3;

    [Header("Audio Settings")]
    public AudioClip introGuideClip; // Audio guide to play at the start
    public AudioClip beepClip;
    private AudioSource audioSource;

    // --- State Machine for the new trial structure ---
    private enum ExperimentPhase
    {
        Idle,
        PlayingIntro,
        AccelTo90,
        Steady90,
        DecelTo60,
        Steady60,
        DecelTo30,
        Steady30,
        DecelTo0,
        PostRotation,
        Finished
    }

    private ExperimentPhase currentPhase = ExperimentPhase.Idle;
    private float currentVelocity = 0f;
    private float startSpeed = 0f;
    private float phaseTimer = 0f;
    private float responseTimer = 0f;
    private bool isResponseTimerRunning = false;
    private bool isResponseRegistered = false;

    // --- Data Storage for each metric across all trials ---
    private int currentTrial = 0;
    private List<float> habituation90_times = new List<float>();
    private List<float> habituation60_times = new List<float>();
    private List<float> habituation30_times = new List<float>();
    private List<float> postRotationEffect_times = new List<float>();

    void Start()
    {
        InitSender();
        sendRate = 1.0f / PackagePerSecond;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        // Start the experiment with the intro guide
        currentPhase = ExperimentPhase.PlayingIntro;
        StartCoroutine(PlayIntroAndBegin());
    }

    private IEnumerator PlayIntroAndBegin()
    {
        Debug.Log("--- Playing Introduction Audio ---");
        if (introGuideClip != null)
        {
            audioSource.PlayOneShot(introGuideClip);
            yield return new WaitForSeconds(introGuideClip.length);
        }
        else
        {
            Debug.LogWarning("No intro guide clip assigned. Starting immediately.");
            yield return new WaitForSeconds(1.0f); // Brief pause
        }
        
        StartExperiment();
    }

    private void StartExperiment()
    {
        Debug.Log("--- Starting Calibration Experiment ---");
        StartNewTrial();
    }

    void Update()
    {
        // --- Main State Machine Logic ---
        phaseTimer += Time.deltaTime;

        switch (currentPhase)
        {
            case ExperimentPhase.AccelTo90:
                UpdateSpeedChange(startSpeed, 90f, AccelDecelDuration, ExperimentPhase.Steady90);
                break;
            case ExperimentPhase.Steady90:
                UpdateSteadyPhase(Steady90Duration, ExperimentPhase.DecelTo60, habituation90_times, true);
                break;
            case ExperimentPhase.DecelTo60:
                UpdateSpeedChange(startSpeed, 60f, AccelDecelDuration, ExperimentPhase.Steady60);
                break;
            case ExperimentPhase.Steady60:
                UpdateSteadyPhase(Mathf.Infinity, ExperimentPhase.DecelTo30, habituation60_times, false);
                break;
            case ExperimentPhase.DecelTo30:
                UpdateSpeedChange(startSpeed, 30f, AccelDecelDuration, ExperimentPhase.Steady30);
                break;
            case ExperimentPhase.Steady30:
                UpdateSteadyPhase(Mathf.Infinity, ExperimentPhase.DecelTo0, habituation30_times, false);
                break;
            case ExperimentPhase.DecelTo0:
                UpdateSpeedChange(startSpeed, 0f, AccelDecelDuration, ExperimentPhase.PostRotation);
                break;
            case ExperimentPhase.PostRotation:
                UpdateSteadyPhase(Mathf.Infinity, ExperimentPhase.Idle, postRotationEffect_times, false);
                break;
        }

        // Continuously send velocity data to the chair
        if (UseChairConnection && sender != null && currentPhase != ExperimentPhase.Idle && currentPhase != ExperimentPhase.Finished)
        {
            SendVelocityToChair();
        }
    }

    private void StartNewTrial()
    {
        if (currentTrial >= NumberOfTrials)
        {
            FinishExperiment();
            return;
        }
        currentTrial++;
        Debug.Log($"--- Starting Trial {currentTrial}/{NumberOfTrials} ---");
        TransitionToPhase(ExperimentPhase.AccelTo90);
    }

    // --- Generic Phase Update Functions ---

    private void UpdateSpeedChange(float fromSpeed, float toSpeed, float duration, ExperimentPhase nextPhase)
    {
        currentVelocity = RaisedCosineSpeed(fromSpeed, toSpeed, phaseTimer, duration);
        if (phaseTimer >= duration)
        {
            currentVelocity = toSpeed;
            TransitionToPhase(nextPhase);
        }
    }

    private void UpdateSteadyPhase(float duration, ExperimentPhase nextPhase, List<float> dataList, bool isFixedDuration)
    {
        if (isResponseTimerRunning)
        {
            responseTimer += Time.deltaTime;
        }

        // Listen for user input, but only if they haven't responded yet in this phase
        if (!isResponseRegistered && Keyboard.current.digit6Key.wasPressedThisFrame)
        {
            isResponseRegistered = true;
            isResponseTimerRunning = false;
            dataList.Add(responseTimer);
            Debug.Log($"Response recorded at {responseTimer:F2}s for phase {currentPhase}.");
            
            // If it's not a fixed duration, start the 1-second countdown to the next phase
            if (!isFixedDuration)
            {
                StartCoroutine(EndPhaseAfterDelay(1.0f, nextPhase));
            }
        }

        // For fixed duration phases, transition after the time is up
        if (isFixedDuration && phaseTimer >= duration)
        {
            // If the user didn't respond, record a placeholder value (e.g., -1)
            if (!isResponseRegistered)
            {
                 dataList.Add(-1f);
                 Debug.LogWarning($"No response recorded for phase {currentPhase}.");
            }
            TransitionToPhase(nextPhase);
        }
    }

    private IEnumerator EndPhaseAfterDelay(float delay, ExperimentPhase nextPhase)
    {
        yield return new WaitForSeconds(delay);
        // If the next phase is Idle, it means the trial is over
        if (nextPhase == ExperimentPhase.Idle)
        {
            StartNewTrial();
        }
        else
        {
            TransitionToPhase(nextPhase);
        }
    }

    // --- State Transitions and Experiment Flow ---

    private void TransitionToPhase(ExperimentPhase nextPhase)
    {
        Debug.Log($"--- Transitioning to: {nextPhase} ---");
        phaseTimer = 0f;
        startSpeed = currentVelocity; // The start speed for the next phase is the current speed
        currentPhase = nextPhase;

        // Reset response tracking for phases that require it
        if (nextPhase == ExperimentPhase.Steady90 || nextPhase == ExperimentPhase.Steady60 ||
            nextPhase == ExperimentPhase.Steady30 || nextPhase == ExperimentPhase.PostRotation)
        {
            isResponseRegistered = false;
            responseTimer = 0f;
            isResponseTimerRunning = true;
            StartCoroutine(PlayBeepAfterDelay(1.0f));
        }
        
        // Send start command at the very beginning of a trial
        if (nextPhase == ExperimentPhase.AccelTo90)
        {
            if (UseChairConnection && sender != null)
            {
                sender.Send(Encoding.ASCII.GetBytes("start"), "start".Length);
            }
        }
    }

    private void FinishExperiment()
    {
        currentPhase = ExperimentPhase.Finished;
        Debug.Log("--- Calibration Finished! ---");
        
        // Calculate and log averages
        LogAverage("Habituation @ 90deg/s", habituation90_times);
        LogAverage("Habituation @ 60deg/s", habituation60_times);
        LogAverage("Habituation @ 30deg/s", habituation30_times);
        LogAverage("Post-Rotation Effect", postRotationEffect_times);

        Debug.Log("To run another calibration, please restart the scene.");
        StopChair();
    }

    private void LogAverage(string metricName, List<float> times)
    {
        // Filter out non-responses (-1) before calculating average
        var validTimes = times.Where(t => t >= 0).ToList();
        if (validTimes.Any())
        {
            float average = validTimes.Average();
            Debug.Log($"Average for {metricName}: {average:F2} seconds. [Trials: {string.Join(", ", validTimes.Select(t => t.ToString("F2")))}]");
        }
        else
        {
            Debug.Log($"No valid responses recorded for {metricName}.");
        }
    }

    // --- Helper and Communication Functions ---

    private void InitSender()
    {
        if (UseChairConnection)
        {
            try
            {
                sender = new UdpClient(localPort, AddressFamily.InterNetwork);
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(remoteIP), remotePort);
                sender.Connect(endPoint);
                Debug.Log("UDP Sender Initialized for Chair Connection.");
            }
            catch (System.Exception e) { Debug.LogError($"UDP Sender Failed: {e.Message}"); UseChairConnection = false; }
        }
        else { Debug.Log("UDP Sender SKIPPED (Debug Mode)."); }
    }

    private void SendVelocityToChair()
    {
        string message = $"udpvelocity {currentVelocity}";
        byte[] data = Encoding.ASCII.GetBytes(message);
        sender.Send(data, data.Length);
    }
    
    private void StopChair()
    {
        currentVelocity = 0;
        if (UseChairConnection && sender != null)
        {
            string stopMessage = string.Format("udpvelocity {0}", currentVelocity);
            sender.Send(Encoding.ASCII.GetBytes(stopMessage), stopMessage.Length);
            sender.Send(Encoding.ASCII.GetBytes("stop"), "stop".Length);
        }
    }

    private float RaisedCosineSpeed(float start, float end, float t, float duration)
    {
        if (duration <= 0) return end;
        t = Mathf.Clamp(t, 0, duration);
        float cosValue = 0.5f * (1 - Mathf.Cos(Mathf.PI * t / duration));
        return start + (end - start) * cosValue;
    }

    private IEnumerator PlayBeepAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (beepClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(beepClip);
            Debug.Log("BEEP! You can respond now.");
        }
    }

    private void OnApplicationQuit()
    {
        if (sender != null)
        {
            StopChair();
            sender.Close();
        }
    }
}
