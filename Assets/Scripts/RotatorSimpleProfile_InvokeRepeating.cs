using LSL;
using System.Collections;
using System.Collections.Generic;
//using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
//using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class RotatorSimpleProfile_InvokeRepeating : MonoBehaviour
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

    [Header("Experiment Settings")]
    public int RepetitionsPerCondition = 5;
    public float AccelerationDuration = 2.0f;
    public float SteadyRotationDuration = 10.0f;
    //public float MidSpeedRotationDuration = 10.0f;
    public float DecelerationDuration = 2.0f;
    public float InterTrialInterval = 10.0f;
    public float HighSpeed = 120.0f;
    public float LowSpeed = 90.0f;

    public enum RotationMode
{
    Mode1, // Current mode
    Mode2,
    Mode3,
    Mode4
}

[Header("Rotation Mode")]
public RotationMode rotationMode = RotationMode.Mode1;

    // Enum for trial conditions
    private enum TrialCondition
    {
        ClockwiseHigh,
        ClockwiseLow,
        CounterClockwiseHigh,
        CounterClockwiseLow
    }

    // Enum for the state machine logic
    private enum ExperimentPhase
    {
        Idle,
        Acceleration,
        StableRotation,
        DecelerationToMid, 
        StableRotationMid,
        DecelerationToStop,
        InterTrialInterval
    }

    private List<TrialCondition> trialList;
    private bool isExperimentRunning = false;

    // State machine variables
    private ExperimentPhase currentPhase = ExperimentPhase.Idle;
    private int currentTrialIndex = 0;
    private float phaseTimer = 0f;
    private float currentVelocity = 0f;
    private float targetSpeed = 0f;
    private float startSpeed = 0f;

    private List<float> speedSequence;
    private int sequenceIndex;
    private bool isRepeatingMode;

    // Add this field to your class:
    private float trialDirection = 1f;

    private void Start()
    {
        InitSender();
        InitLSL();
        CreateRandomizedTrialList();
        sendRate = 1.0f / PackagePerSecond;
    }

    private void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame && !isExperimentRunning)
        {
            Debug.Log("Space pressed - Starting Experiment");
            StartExperiment();
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
            sender = new UdpClient(localPort, AddressFamily.InterNetwork);
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(remoteIP), remotePort);
            sender.Connect(endPoint);
            Debug.Log("UDP Sender Initialized for Chair Connection.");
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
            LSL.channel_format_t.cf_int32,// Use cf_string for string markers
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

    private void StartExperiment()
    {
        if (trialList == null || trialList.Count == 0)
        {
            Debug.LogError("Trial list is empty. Cannot start experiment.");
            return;
        }
        isExperimentRunning = true;
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

        // Randomize direction for this trial: +1 (clockwise) or -1 (counterclockwise)
        trialDirection = (Random.value < 0.5f) ? 1f : -1f;

        float topSpeed = 0;
        switch (condition)
        {
            case TrialCondition.ClockwiseHigh:
            case TrialCondition.CounterClockwiseHigh:
                topSpeed = HighSpeed;
                break;
            case TrialCondition.ClockwiseLow:
            case TrialCondition.CounterClockwiseLow:
                topSpeed = LowSpeed;
                break;
        }

        // Get the base speed sequence for the selected mode
        var baseSequence = GetSpeedSequence(topSpeed);

        // Apply the randomized direction to all speeds in the sequence
        speedSequence = baseSequence.Select(s => s * trialDirection).ToList();

        sequenceIndex = 0;
        isRepeatingMode = (rotationMode == RotationMode.Mode4);

        currentVelocity = speedSequence[0];
        targetSpeed = speedSequence[0];
        startSpeed = currentVelocity;

        if (UseChairConnection && sender != null)
        {
            sender.Send(Encoding.ASCII.GetBytes("start"), "start".Length);
        }

        phaseTimer = 0f;
        currentPhase = ExperimentPhase.Acceleration;
    }

    private void UpdateTrialState()
    {
        if (!isExperimentRunning) return;

        phaseTimer += sendRate;

        switch (currentPhase)
        {
            case ExperimentPhase.Acceleration:
                currentVelocity = RaisedCosineSpeed(startSpeed, targetSpeed, phaseTimer, AccelerationDuration);
                if (phaseTimer >= AccelerationDuration)
                {
                    currentVelocity = targetSpeed;
                    phaseTimer = 0f;
                    currentPhase = ExperimentPhase.StableRotation;
                }
                break;

            case ExperimentPhase.StableRotation:
                currentVelocity = targetSpeed;
                if (phaseTimer >= SteadyRotationDuration)
                {
                    // Prepare for next speed in sequence
                    if (sequenceIndex < speedSequence.Count - 1)
                    {
                        sequenceIndex++;
                        startSpeed = currentVelocity;
                        targetSpeed = speedSequence[sequenceIndex];
                        phaseTimer = 0f;
                        currentPhase = ExperimentPhase.DecelerationToMid;
                    }
                    else if (isRepeatingMode)
                    {
                        // Loop for Mode4
                        sequenceIndex = 0;
                        startSpeed = currentVelocity;
                        targetSpeed = speedSequence[sequenceIndex];
                        phaseTimer = 0f;
                        currentPhase = ExperimentPhase.DecelerationToMid;
                    }
                    else
                    {
                        // End of sequence, decelerate to stop
                        startSpeed = currentVelocity;
                        targetSpeed = 0f;
                        phaseTimer = 0f;
                        currentPhase = ExperimentPhase.DecelerationToStop;
                    }
                }
                break;

            case ExperimentPhase.DecelerationToMid:
                currentVelocity = RaisedCosineSpeed(startSpeed, targetSpeed, phaseTimer, DecelerationDuration);
                if (phaseTimer >= DecelerationDuration)
                {
                    currentVelocity = targetSpeed;
                    phaseTimer = 0f;
                    currentPhase = ExperimentPhase.StableRotation;
                }
                break;

            case ExperimentPhase.DecelerationToStop:
                currentVelocity = RaisedCosineSpeed(startSpeed, 0, phaseTimer, DecelerationDuration);
                if (phaseTimer >= DecelerationDuration)
                {
                    currentVelocity = 0;
                    SendMarker(6);
                    phaseTimer = 0f;
                    currentPhase = ExperimentPhase.InterTrialInterval;
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

    Debug.Log("current speed: " + currentVelocity);

    if (UseChairConnection && sender != null)
    {
        string message = string.Format("udpvelocity {0}", currentVelocity);
        sender.Send(Encoding.ASCII.GetBytes(message), message.Length);
    }
}

    // Raised cosine speed profile helper
    private float RaisedCosineSpeed(float start, float end, float t, float duration)
    {
        t = Mathf.Clamp(t, 0, duration);
        float cosValue = 0.5f * (1 - Mathf.Cos(Mathf.PI * t / duration));
        return start + (end - start) * cosValue;
    }

    private void TransitionToPhase(ExperimentPhase nextPhase)
    {
        currentPhase = nextPhase;
        phaseTimer = 0f;
        startSpeed = currentVelocity; // The start speed for the next phase is the current speed

        TrialCondition condition = trialList[currentTrialIndex];
        int marker = 0;//0 means no marker sent

        switch (nextPhase)
        {
            case ExperimentPhase.Acceleration:
                marker = 1;
                targetSpeed = (condition.ToString().Contains("High") ? HighSpeed : LowSpeed) * (condition.ToString().Contains("Counter") ? -1 : 1);
                break;
            case ExperimentPhase.StableRotation:
                marker = 2;
                break;
            case ExperimentPhase.DecelerationToMid:
                marker = 3;
                targetSpeed = startSpeed * 0.5f; //Target is half of the top speed (current "startSpeed")
                break;
            case ExperimentPhase.StableRotationMid: 
                marker = 4;
                break;
            case ExperimentPhase.DecelerationToStop:
                marker = 5;
                targetSpeed = 0; // Target is a full stop
                break;
            case ExperimentPhase.InterTrialInterval:
                Debug.Log($"--- Inter-trial rest for {InterTrialInterval} seconds ---");
                // No marker needed here, stop marker was sent at end of deceleration
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

    //private int GetMarkerCode(string marker)
    //{
    //    switch (marker)
    //    {
    //        case string s when s.Contains("start_acceleration"): return 1;
    //        case string s when s.Contains("start_stable"): return 2;
    //        case string s when s.Contains("start_deceleration_mid"): return 3;
    //        case string s when s.Contains("start_stable_mid"): return 4;
    //        case string s when s.Contains("start_deceleration_to_stop"): return 5;
    //        case string s when s.Contains("stop"): return 6;
    //        default: return 0;
    //    }
    //}

    private void SendMarker(int markerValue)
    {
        if (chairRotationOutlet != null)
        {
            int[] sample = { markerValue };
            chairRotationOutlet.push_sample(sample);
            Debug.Log("Sent LSL Marker: " + sample[0]);
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

    // Update GetSpeedSequence to accept topSpeed as a parameter:
    private List<float> GetSpeedSequence(float topSpeed)
    {
        switch (rotationMode)
        {
            case RotationMode.Mode1:
                // Top speed -> half speed -> stop
                return new List<float> { topSpeed, topSpeed * 0.5f, 0f };
            case RotationMode.Mode2:
                // 90 -> 60 -> 30 -> 0
                return new List<float> { 90f, 60f, 30f, 0f };
            case RotationMode.Mode3:
                // 100 -> 75 -> 50 -> 25 -> 0
                return new List<float> { 100f, 75f, 50f, 25f, 0f };
            case RotationMode.Mode4:
                // 90 -> 60 -> 30 -> 90 (repeat, never stop)
                return new List<float> { 90f, 60f, 30f };
            default:
                return new List<float> { topSpeed, topSpeed * 0.5f, 0f };
        }
    }
}