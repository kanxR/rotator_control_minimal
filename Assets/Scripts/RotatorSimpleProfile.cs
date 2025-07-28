using LSL;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    public float AccelerationDuration = 5.0f;
    public float StableRotationDuration = 5.0f;
    public float DecelerationDuration = 5.0f;
    public float InterTrialInterval = 10.0f;
    public float HighSpeed = 120.0f;
    public float LowSpeed = 90.0f;

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
        Deceleration,
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
            LSL.channel_format_t.cf_string,
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

        float topSpeed = 0;
        float direction = 1.0f;

        switch (condition)
        {
            case TrialCondition.ClockwiseHigh:
                topSpeed = HighSpeed;
                direction = 1.0f;
                break;
            case TrialCondition.ClockwiseLow:
                topSpeed = LowSpeed;
                direction = 1.0f;
                break;
            case TrialCondition.CounterClockwiseHigh:
                topSpeed = HighSpeed;
                direction = -1.0f;
                break;
            case TrialCondition.CounterClockwiseLow:
                topSpeed = LowSpeed;
                direction = -1.0f;
                break;
        }

        targetSpeed = topSpeed * direction;

        // Send "start" command to the chair at the beginning of the first phase
        if (UseChairConnection && sender != null)
        {
            sender.Send(Encoding.ASCII.GetBytes("start"), "start".Length);
        }

        // Transition to the first phase: Acceleration
        TransitionToPhase(ExperimentPhase.Acceleration);
    }

    private void UpdateTrialState()
    {
        if (!isExperimentRunning) return;

        phaseTimer += sendRate; // Increment timer by the update interval

        switch (currentPhase)
        {
            case ExperimentPhase.Acceleration:
                currentVelocity = Mathf.Lerp(startSpeed, targetSpeed, phaseTimer / AccelerationDuration);
                if (phaseTimer >= AccelerationDuration)
                {
                    currentVelocity = targetSpeed;
                    TransitionToPhase(ExperimentPhase.StableRotation);
                }
                break;

            case ExperimentPhase.StableRotation:
                currentVelocity = targetSpeed; // Keep speed constant
                if (phaseTimer >= StableRotationDuration)
                {
                    TransitionToPhase(ExperimentPhase.Deceleration);
                }
                break;

            case ExperimentPhase.Deceleration:
                currentVelocity = Mathf.Lerp(startSpeed, 0, phaseTimer / DecelerationDuration);
                if (phaseTimer >= DecelerationDuration)
                {
                    currentVelocity = 0;
                    TrialCondition condition = trialList[currentTrialIndex];
                    SendMarker($"stop_trial_{condition}");
                    TransitionToPhase(ExperimentPhase.InterTrialInterval);
                }
                break;

            case ExperimentPhase.InterTrialInterval:
                currentVelocity = 0; // Ensure chair is stopped
                if (phaseTimer >= InterTrialInterval)
                {
                    currentTrialIndex++;
                    StartNewTrial();
                }
                break;
        }

        // Send velocity data via UDP
        if (UseChairConnection && sender != null)
        {
            string message = string.Format("udpvelocity {0}", currentVelocity);
            sender.Send(Encoding.ASCII.GetBytes(message), message.Length);
            Debug.Log("current speed: " + currentVelocity);
        }
    }

    private void TransitionToPhase(ExperimentPhase nextPhase)
    {
        currentPhase = nextPhase;
        phaseTimer = 0f;
        startSpeed = currentVelocity; // The start speed for the next phase is the current speed

        TrialCondition condition = trialList[currentTrialIndex];
        string marker = "";

        switch (nextPhase)
        {
            case ExperimentPhase.Acceleration:
                marker = $"start_acceleration_{condition}";
                targetSpeed = (condition.ToString().Contains("High") ? HighSpeed : LowSpeed) * (condition.ToString().Contains("Counter") ? -1 : 1);
                break;
            case ExperimentPhase.StableRotation:
                marker = $"start_stable_{condition}";
                break;
            case ExperimentPhase.Deceleration:
                marker = $"start_deceleration_{condition}";
                targetSpeed = 0; // Target for deceleration is always 0
                break;
            case ExperimentPhase.InterTrialInterval:
                Debug.Log($"--- Inter-trial rest for {InterTrialInterval} seconds ---");
                // No marker needed here, stop marker was sent at end of deceleration
                break;
        }

        if (!string.IsNullOrEmpty(marker))
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

    private void SendMarker(string marker)
    {
        if (chairRotationOutlet != null)
        {
            string[] sample = { marker };
            chairRotationOutlet.push_sample(sample);
            Debug.Log("Sent LSL Marker: " + marker);
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