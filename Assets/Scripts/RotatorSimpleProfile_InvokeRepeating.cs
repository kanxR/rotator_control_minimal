using LSL;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class RotatorSimpleProfile_WithAudio : MonoBehaviour
{
    // LSL Marker Stream
    private StreamOutlet chairRotationOutlet;
    private const string ChairRotationStreamName = "UnityChairRotationStream";
    private const string ChairRotationStreamType = "ChairRotationMarkers";

    [Header("Communication Settings")]
    public bool UseChairConnection = true;
    [Range(1, 60)]
    public float PackagePerSecond = 30;
    private int remotePort = 42424;
    private string remoteIP = "100.1.1.101";
    private int localPort = 42434;
    private UdpClient sender;
    private float sendRate;

    public bool isDebugMode = true;

    [Header("Experiment Settings")]
    public int RepetitionsPerCondition = 5;
    public float InterTrialInterval = 10.0f;

    [Header("Steady Speeds (deg/s)")]
    public float SteadySpeed1 = 120.0f;
    public float SteadySpeed2 = 90.0f;
    public float SteadySpeed3 = 60.0f;
    public float SteadySpeed4 = 30.0f;

    [Header("Steady Durations (s)")]
    public float SteadyDuration1 = 10.0f;
    public float SteadyDuration2 = 10.0f;
    public float SteadyDuration3 = 10.0f;
    public float SteadyDuration4 = 10.0f;

    [Header("Acceleration/Deceleration Durations (s)")]
    public float AccelDecelDuration0 = 2.0f; // Before Steady 1
    public float AccelDecelDuration1 = 2.0f; // Before Steady 2
    public float AccelDecelDuration2 = 2.0f; // Before Steady 3
    public float AccelDecelDuration3 = 2.0f; // Before Steady 4
    public float AccelDecelDuration4 = 2.0f; // To stop

    [Header("Audio Settings")]
    public AudioClip introGuideClip; // Audio guide to play at the start
    public AudioClip beepClip;
    private AudioSource audioSource;

    // Enum for trial conditions
    private enum TrialCondition
    {
        Clockwise,
        CounterClockwise
    }

    // Enum for the state machine logic
    private enum ExperimentPhase
    {
        Idle,
        AccelDecel0,
        Steady1,
        AccelDecel1,
        Steady2,
        AccelDecel2,
        Steady3,
        AccelDecel3,
        Steady4,
        AccelDecel4,
        InterTrialInterval
    }

    private List<TrialCondition> trialList;
    private bool isExperimentRunning = false;

    // State machine variables
    private ExperimentPhase currentPhase = ExperimentPhase.Idle;
    private int currentTrialIndex = 0;
    private float phaseTimer = 0f;
    private float currentVelocity = 0f;
    private float startSpeed = 0f;

    // Arrays for speeds and durations
    private float[] steadySpeeds;
    private float[] steadyDurations;
    private float[] accelDecelDurations;

    private void Start()
    {
        InitSender();
        InitLSL();
        CreateRandomizedTrialList();
        sendRate = 1.0f / PackagePerSecond;

        // Initialize arrays for easy access
        steadySpeeds = new float[] { SteadySpeed1, SteadySpeed2, SteadySpeed3, SteadySpeed4 };
        steadyDurations = new float[] { SteadyDuration1, SteadyDuration2, SteadyDuration3, SteadyDuration4 };
        accelDecelDurations = new float[] { AccelDecelDuration0, AccelDecelDuration1, AccelDecelDuration2, AccelDecelDuration3, AccelDecelDuration4 };

        // Setup AudioSource
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
    }

    private void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame && !isExperimentRunning)
        {
            // The coroutine will now handle the experiment start sequence
            StartCoroutine(StartExperimentCoroutine());
        }

        if (Keyboard.current.sKey.wasPressedThisFrame && isExperimentRunning)
        {
            Debug.Log("S pressed - Emergency Stop");
            EmergencyStop();
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
            catch (System.Exception e)
            {
                Debug.LogError($"UDP Sender Initialization Failed: {e.Message}");
                UseChairConnection = false;
            }
        }
        else
        {
            Debug.Log("UDP Sender SKIPPED (Debug Mode).");
        }
    }

    private void InitLSL()
    {
        StreamInfo chairRotationInfo = new StreamInfo(
            ChairRotationStreamName,
            ChairRotationStreamType,
            1, 0.0,
            LSL.channel_format_t.cf_int32,
            "UnityChairRotation_Automated"
        );
        chairRotationOutlet = new StreamOutlet(chairRotationInfo);
    }

    private void CreateRandomizedTrialList()
    {
        trialList = new List<TrialCondition>();
        var conditions = System.Enum.GetValues(typeof(TrialCondition));
        foreach (TrialCondition condition in conditions)
        {
            for (int i = 0; i < RepetitionsPerCondition; i++)
            {
                trialList.Add(condition);
            }
        }
        System.Random rng = new System.Random();
        trialList = trialList.OrderBy(a => rng.Next()).ToList();
        Debug.Log($"Created a randomized trial list with {trialList.Count} trials.");
    }

    private IEnumerator StartExperimentCoroutine()
    {
        

        isExperimentRunning = true; // Lock the spacebar immediately
        if (!isDebugMode)
        {
            Debug.Log("--- Playing Introduction Audio ---");

            if (introGuideClip != null && audioSource != null)
            {
                audioSource.PlayOneShot(introGuideClip);
                yield return new WaitForSeconds(introGuideClip.length);
            }
            else
            {
                Debug.LogWarning("No intro guide clip assigned. Starting immediately.");
            }
        }

        // --- Now, proceed with the original experiment setup ---
        if (trialList == null || trialList.Count == 0)
        {
            Debug.LogError("Trial list is empty. Cannot start experiment.");
            isExperimentRunning = false; // Release the lock if setup fails
            yield break; // Exit the coroutine
        }
        
        Debug.Log("--- Experiment Starting Now ---");
        currentTrialIndex = 0;
        StartNewTrial();
        InvokeRepeating(nameof(UpdateTrialState), 0f, sendRate);
    }

    private void StartNewTrial()
    {
        if (currentTrialIndex >= trialList.Count)
        {
            FinishExperiment();
            return;
        }

        TrialCondition condition = trialList[currentTrialIndex];
        Debug.Log($"--- Starting Trial {currentTrialIndex + 1}/{trialList.Count}: {condition} ---");

        // Set direction
        float direction = (condition == TrialCondition.Clockwise) ? 1.0f : -1.0f;

        // Re-initialize a fresh copy of speeds with the correct direction for the new trial
        float[] directedSteadySpeeds = new float[] {
            Mathf.Abs(SteadySpeed1) * direction,
            Mathf.Abs(SteadySpeed2) * direction,
            Mathf.Abs(SteadySpeed3) * direction,
            Mathf.Abs(SteadySpeed4) * direction
        };
        steadySpeeds = directedSteadySpeeds;


        // Send "start" command to the chair at the beginning of the first phase
        if (UseChairConnection && sender != null)
        {
            sender.Send(Encoding.ASCII.GetBytes("start"), "start".Length);
        }

        // Transition to the first phase: AccelDecel0
        TransitionToPhase(ExperimentPhase.AccelDecel0);
    }

    private void UpdateTrialState()
    {
        if (!isExperimentRunning) return;

        phaseTimer += sendRate; // Increment timer by the update interval
        
        switch (currentPhase)
        {
            case ExperimentPhase.AccelDecel0:
                currentVelocity = RaisedCosineSpeed(startSpeed, steadySpeeds[0], phaseTimer, accelDecelDurations[0]);
                if (phaseTimer >= accelDecelDurations[0])
                {
                    currentVelocity = steadySpeeds[0];
                    TransitionToPhase(ExperimentPhase.Steady1);
                }
                break;

            case ExperimentPhase.Steady1:
                currentVelocity = steadySpeeds[0];
                if (phaseTimer >= steadyDurations[0])
                {
                    TransitionToPhase(ExperimentPhase.AccelDecel1);
                }
                break;

            case ExperimentPhase.AccelDecel1:
                currentVelocity = RaisedCosineSpeed(startSpeed, steadySpeeds[1], phaseTimer, accelDecelDurations[1]);
                if (phaseTimer >= accelDecelDurations[1])
                {
                    currentVelocity = steadySpeeds[1];
                    TransitionToPhase(ExperimentPhase.Steady2);
                }
                break;

            case ExperimentPhase.Steady2:
                currentVelocity = steadySpeeds[1];
                if (phaseTimer >= steadyDurations[1])
                {
                    TransitionToPhase(ExperimentPhase.AccelDecel2);
                }
                break;

            case ExperimentPhase.AccelDecel2:
                currentVelocity = RaisedCosineSpeed(startSpeed, steadySpeeds[2], phaseTimer, accelDecelDurations[2]);
                if (phaseTimer >= accelDecelDurations[2])
                {
                    currentVelocity = steadySpeeds[2];
                    TransitionToPhase(ExperimentPhase.Steady3);
                }
                break;

            case ExperimentPhase.Steady3:
                currentVelocity = steadySpeeds[2];
                if (phaseTimer >= steadyDurations[2])
                {
                    TransitionToPhase(ExperimentPhase.AccelDecel3);
                }
                break;

            case ExperimentPhase.AccelDecel3:
                currentVelocity = RaisedCosineSpeed(startSpeed, steadySpeeds[3], phaseTimer, accelDecelDurations[3]);
                if (phaseTimer >= accelDecelDurations[3])
                {
                    currentVelocity = steadySpeeds[3];
                    TransitionToPhase(ExperimentPhase.Steady4);
                }
                break;

            case ExperimentPhase.Steady4:
                currentVelocity = steadySpeeds[3];
                if (phaseTimer >= steadyDurations[3])
                {
                    TransitionToPhase(ExperimentPhase.AccelDecel4);
                }
                break;

            case ExperimentPhase.AccelDecel4:
                currentVelocity = RaisedCosineSpeed(startSpeed, 0, phaseTimer, accelDecelDurations[4]);
                if (phaseTimer >= accelDecelDurations[4])
                {
                    currentVelocity = 0; // Ensure chair is stopped
                    SendMarker(6);
                    TransitionToPhase(ExperimentPhase.InterTrialInterval);
                }
                break;

            case ExperimentPhase.InterTrialInterval:
                currentVelocity = 0;
                if (phaseTimer >= InterTrialInterval)
                {
                    currentTrialIndex++;
                    StartNewTrial();
                }
                break;
        }

        if (UseChairConnection && sender != null)
        {
            string message = string.Format("udpvelocity {0}", currentVelocity);
            sender.Send(Encoding.ASCII.GetBytes(message), message.Length);
            //Debug.Log("current velocity;" + currentVelocity);
        }
    }

    private float RaisedCosineSpeed(float start, float end, float t, float duration)
    {
        if (duration <= 0) return end;
        t = Mathf.Clamp(t, 0, duration);
        float cosValue = 0.5f * (1 - Mathf.Cos(Mathf.PI * t / duration));
        return start + (end - start) * cosValue;
    }

    private void PlayBeep()
    {
        if (beepClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(beepClip);
            Debug.Log("Beep sound played.");
        }
    }

    private void TransitionToPhase(ExperimentPhase nextPhase)
    {
        currentPhase = nextPhase;
        phaseTimer = 0f;
        startSpeed = currentVelocity;
        int marker = 0;

        Debug.Log($"--- Transitioning to phase: {nextPhase} ---");

        switch (nextPhase)
        {
            case ExperimentPhase.AccelDecel0: marker = 1; break;
            case ExperimentPhase.Steady1:
                marker = 2;
                Invoke(nameof(PlayBeep), 1.0f); 
                break;
            case ExperimentPhase.AccelDecel1: marker = 3; break;
            case ExperimentPhase.Steady2:
                marker = 4;
                Invoke(nameof(PlayBeep), 1.0f);
                break;
            case ExperimentPhase.AccelDecel2: marker = 5; break;
            case ExperimentPhase.Steady3:
                marker = 6;
                Invoke(nameof(PlayBeep), 1.0f);
                break;
            case ExperimentPhase.AccelDecel3: marker = 7; break;
            case ExperimentPhase.Steady4:
                marker = 8;
                Invoke(nameof(PlayBeep), 1.0f);
                break;
            case ExperimentPhase.AccelDecel4: marker = 9; break;
            case ExperimentPhase.InterTrialInterval:
                Debug.Log($"--- Inter-trial rest for {InterTrialInterval} seconds ---");
                break;
        }

        if (marker != 0)
        {
            SendMarker(marker);
        }
    }

    private void FinishExperiment()
    {
        Debug.Log("--- Experiment Finished! ---");
        isExperimentRunning = false;
        currentPhase = ExperimentPhase.Idle;
        CancelInvoke(nameof(UpdateTrialState));
        StopChair();
    }

    private void EmergencyStop()
    {
        Debug.Log("Rotation stopped completely by user.");
        isExperimentRunning = false;
        currentPhase = ExperimentPhase.Idle;
        CancelInvoke(nameof(UpdateTrialState));
        StopChair();
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

    private void SendMarker(int markerValue)
    {
        if (chairRotationOutlet != null)
        {
            int[] sample = { markerValue };
            chairRotationOutlet.push_sample(sample);
            Debug.Log("Sent LSL Marker: " + sample[0]);
        }
        else
        {
            Debug.LogWarning("Sent pseudo LSL Marker:" + markerValue);
        }
    }

    private void OnApplicationQuit()
    {
        if (sender != null)
        {
            if (isExperimentRunning)
            {
                EmergencyStop();
            }
            sender.Close();
        }
    }
}
