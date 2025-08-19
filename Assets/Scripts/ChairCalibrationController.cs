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
    public bool IsDebugMode = true;

    [Range(1, 60)]
    public float PackagePerSecond = 30;
    private int remotePort = 42424;
    private string remoteIP = "100.1.1.101";
    private int localPort = 42434;
    private UdpClient sender;
    private float sendRate;

    [Header("Experiment Settings")]
    private const int NumberOfTrials = 3;
    public float PostResponseDelay = 3.0f; // Time after response before next phase

    // NEW: 4 configurable speeds for each steady-state phase
    [Header("Rotation Speeds (deg/s)")]
    public float SteadySpeed1 = 90.0f;
    public float SteadySpeed2 = 60.0f;
    public float SteadySpeed3 = 30.0f;
    public float SteadySpeed4 = 15.0f; // Added 4th speed

    // NEW: 5 configurable durations for accel/decel phases
    [Header("Accel/Decel Durations (s)")]
    public float AccelToSpeed1Duration = 2.0f;
    public float DecelToSpeed2Duration = 2.0f;
    public float DecelToSpeed3Duration = 2.0f;
    public float DecelToSpeed4Duration = 2.0f; // Added duration for 4th speed transition
    public float DecelToStopDuration = 2.0f;
    public float InterTrialInterval = 5.0f; // Time between trials

    [Header("Audio Settings")]
    public AudioClip introGuideClip; // Audio guide to play at the start
    public AudioClip beepClip;
    private AudioSource audioSource;

    // --- Updated State Machine with 4 speed stages ---
    private enum ExperimentPhase
    {
        Idle,
        PlayingIntro,
        AccelToSpeed1,
        SteadySpeed1,
        DecelToSpeed2,
        SteadySpeed2,
        DecelToSpeed3,
        SteadySpeed3,
        DecelToSpeed4, // New phase
        SteadySpeed4,  // New phase
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
    private bool isResponseRegistered = true;

    // --- Data Storage for each metric across all trials ---
    private int currentTrial = 0;
    private List<float> habituation_speed1_times = new List<float>();
    private List<float> habituation_speed2_times = new List<float>();
    private List<float> habituation_speed3_times = new List<float>();
    private List<float> habituation_speed4_times = new List<float>(); // New list for 4th speed data
    private List<float> postRotationEffect_times = new List<float>();

    void Start()
    {
        InitSender();
        sendRate = 1.0f / PackagePerSecond;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        currentPhase = ExperimentPhase.PlayingIntro;
        StartCoroutine(PlayIntroAndBegin());
    }

    private IEnumerator PlayIntroAndBegin()
    {
        if (!IsDebugMode)
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
                yield return new WaitForSeconds(1.0f);
            }
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
        phaseTimer += Time.deltaTime;

        switch (currentPhase)
        {
            // UPDATED: State machine now includes 4 speed stages
            case ExperimentPhase.AccelToSpeed1:
                UpdateSpeedChange(startSpeed, SteadySpeed1, AccelToSpeed1Duration, ExperimentPhase.SteadySpeed1);
                break;
            case ExperimentPhase.SteadySpeed1:
                UpdateSteadyPhase(ExperimentPhase.DecelToSpeed2, habituation_speed1_times);
                break;
            case ExperimentPhase.DecelToSpeed2:
                UpdateSpeedChange(startSpeed, SteadySpeed2, DecelToSpeed2Duration, ExperimentPhase.SteadySpeed2);
                break;
            case ExperimentPhase.SteadySpeed2:
                UpdateSteadyPhase(ExperimentPhase.DecelToSpeed3, habituation_speed2_times);
                break;
            case ExperimentPhase.DecelToSpeed3:
                UpdateSpeedChange(startSpeed, SteadySpeed3, DecelToSpeed3Duration, ExperimentPhase.SteadySpeed3);
                break;
            case ExperimentPhase.SteadySpeed3:
                UpdateSteadyPhase(ExperimentPhase.DecelToSpeed4, habituation_speed3_times);
                break;
            case ExperimentPhase.DecelToSpeed4: // New case
                UpdateSpeedChange(startSpeed, SteadySpeed4, DecelToSpeed4Duration, ExperimentPhase.SteadySpeed4);
                break;
            case ExperimentPhase.SteadySpeed4: // New case
                UpdateSteadyPhase(ExperimentPhase.DecelTo0, habituation_speed4_times);
                break;
            case ExperimentPhase.DecelTo0:
                UpdateSpeedChange(startSpeed, 0f, DecelToStopDuration, ExperimentPhase.PostRotation);
                break;
            case ExperimentPhase.PostRotation:
                UpdateSteadyPhase(ExperimentPhase.Idle, postRotationEffect_times);
                break;
        }

        if (UseChairConnection && sender != null && currentPhase != ExperimentPhase.Idle && currentPhase != ExperimentPhase.Finished)
        {
            SendVelocityToChair();
        }

        if (Keyboard.current.sKey.wasPressedThisFrame)
        {
            Debug.Log("S pressed - Emergency Stop");
            EmergencyStop();
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
        TransitionToPhase(ExperimentPhase.AccelToSpeed1);
    }

    private void UpdateSpeedChange(float fromSpeed, float toSpeed, float duration, ExperimentPhase nextPhase)
    {
        currentVelocity = RaisedCosineSpeed(fromSpeed, toSpeed, phaseTimer, duration);
        if (phaseTimer >= duration)
        {
            currentVelocity = toSpeed;
            TransitionToPhase(nextPhase);
        }
    }

    // UPDATED: Removed duration parameter. Phase ends based on user response.
    private void UpdateSteadyPhase(ExperimentPhase nextPhase, List<float> dataList)
    {
        if (isResponseTimerRunning)
        {
            responseTimer += Time.deltaTime;
        }

        if (!isResponseRegistered && Keyboard.current.numpad6Key.wasPressedThisFrame)
        {
            isResponseRegistered = true;
            isResponseTimerRunning = false;
            dataList.Add(responseTimer);
            Debug.Log($"Response recorded at {responseTimer:F2}s for phase {currentPhase}.");

            float delay = (nextPhase == ExperimentPhase.Idle) ? InterTrialInterval : PostResponseDelay;
            StartCoroutine(EndPhaseAfterDelay(delay, nextPhase));
        }
    }

    private IEnumerator EndPhaseAfterDelay(float delay, ExperimentPhase nextPhase)
    {
        Debug.Log($"Response registered. Waiting {delay}s before transitioning to {nextPhase}.");
        yield return new WaitForSeconds(delay);
        if (nextPhase == ExperimentPhase.Idle)
        {
            StartNewTrial();
        }
        else
        {
            TransitionToPhase(nextPhase);
        }
    }

    private void TransitionToPhase(ExperimentPhase nextPhase)
    {
        Debug.Log($"--- Transitioning to: {nextPhase} ---");
        phaseTimer = 0f;
        startSpeed = currentVelocity;
        currentPhase = nextPhase;

        // UPDATED: Condition now includes the new SteadySpeed4 phase
        if (nextPhase == ExperimentPhase.SteadySpeed1 || nextPhase == ExperimentPhase.SteadySpeed2 ||
            nextPhase == ExperimentPhase.SteadySpeed3 || nextPhase == ExperimentPhase.SteadySpeed4 ||
            nextPhase == ExperimentPhase.PostRotation)
        {
            responseTimer = 0f;
            isResponseTimerRunning = true;
            StartCoroutine(PlayBeepAfterDelay(7.0f));
        }

        if (nextPhase == ExperimentPhase.AccelToSpeed1)
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

        // UPDATED: Logging now includes the 4th speed
        LogAverage($"Habituation @ {SteadySpeed1}deg/s", habituation_speed1_times);
        LogAverage($"Habituation @ {SteadySpeed2}deg/s", habituation_speed2_times);
        LogAverage($"Habituation @ {SteadySpeed3}deg/s", habituation_speed3_times);
        LogAverage($"Habituation @ {SteadySpeed4}deg/s", habituation_speed4_times);
        LogAverage("Post-Rotation Effect", postRotationEffect_times);

        Debug.Log("To run another calibration, please restart the scene.");
        StopChair();
    }

    private void LogAverage(string metricName, List<float> times)
    {
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
            isResponseRegistered = false;
        }
    }
    private void EmergencyStop()
    {
        Debug.Log("Rotation stopped completely by user.");
        //isExperimentRunning = false;
        currentPhase = ExperimentPhase.Idle;
        //CancelInvoke(nameof(UpdateSpeedChange));
        CancelInvoke(nameof(Update));
        StopChair();
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
