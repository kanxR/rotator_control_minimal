using LSL;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // Required for randomization (OrderBy)
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class RotatorSimpleProfile : MonoBehaviour
{
    // LSL Marker Stream
    private StreamOutlet chairRotationOutlet;
    private const string ChairRotationStreamName = "UnityChairRotationStream";
    private const string ChairRotationStreamType = "ChairRotationMarkers";

    [Header("Communication Settings")]
    public bool UseChairConnection = true; // Add a toggle: if False, it doesn't connect to the chair 
    [Range(1, 60)]
    public float PackagePerSecond = 30;
    private int remotePort = 42424;//42425? as in UDPListener.cs
    private string remoteIP = "127.179.177.25";
    private int localPort = 42434;
    private UdpClient sender;
    private float sendRate;
    private bool isExperimentRunning = false;
    private float currentVelocity = 0f;

    [Header("Experiment Settings")]
    public int RepetitionsPerCondition = 5; // This will result in 5 * 4 = 20 trials
    public float AccelerationDuration = 5.0f;
    public float StableRotationDuration = 5.0f;
    public float DecelerationDuration = 5.0f;
    public float InterTrialInterval = 10.0f;// Interval between trials in seconds
    public float HighSpeed = 120.0f; // degrees/sec
    public float LowSpeed = 90.0f;  // degrees/sec

    // Enum to define the trial conditions clearly
    private enum TrialCondition
    {
        ClockwiseHigh,
        ClockwiseLow,
        CounterClockwiseHigh,
        CounterClockwiseLow
    }

    private List<TrialCondition> trialList;

    private void Start()
    {
        InitSender();
        InitLSL();
        CreateRandomizedTrialList();
    }

    private void Update()
    {
        // Press Space to start the whole experiment
        if (Keyboard.current.spaceKey.wasPressedThisFrame && !isExperimentRunning)
        {
            Debug.Log("Space pressed - Start Rotation");
            isExperimentRunning = true;
            StartCoroutine(RunExperiment());
        }

        // Press S for an emergency stop
        if (Keyboard.current.sKey.wasPressedThisFrame && isExperimentRunning)
        {
            Debug.Log("S pressed - Emergency Stop");
            StopAllCoroutines();
            StartCoroutine(ForceStopRotation());
        }
    }

      
    /// Initializes the UDP sender for communicating with the chair.
      
    private void InitSender()
    {
        // Initialize the UDP sender only if UseChairConnection is true
        if (UseChairConnection)
        {
            sendRate = 1.0f / PackagePerSecond;
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


    /// Initializes the LSL outlet for sending markers.
    /// used in "Start()" and "RunSingleTrial()" methods.
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

    /// Creates and shuffles the list of all trial conditions.
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

        // Randomize the list using LINQ OrderBy and a random Guid
        System.Random rng = new System.Random();
        trialList = trialList.OrderBy(a => rng.Next()).ToList();

        Debug.Log($"Created a randomized trial list with {trialList.Count} trials.");
    }

    /// Main coroutine that iterates through the randomized trial list.
    private IEnumerator RunExperiment()
    {
        yield return new WaitForSeconds(2.0f); // A brief pause before starting

        for (int i = 0; i < trialList.Count; i++)
        {
            Debug.Log($"--- Starting Trial {i + 1}/{trialList.Count}: {trialList[i]} ---");
            yield return StartCoroutine(RunSingleTrial(trialList[i]));

            if (i < trialList.Count - 1)
            {
                Debug.Log($"--- Inter-trial rest for {InterTrialInterval} seconds ---");
                yield return new WaitForSeconds(InterTrialInterval);
            }
        }

        Debug.Log("--- Experiment Finished! ---");
        isExperimentRunning = false;
        // Send commad to stop the chair if UseChairConnection ==true (the chair is connected)
        if (UseChairConnection && sender != null)
        {
            sender.Send(Encoding.ASCII.GetBytes("stop"), "stop".Length);
        }
    }

      
    /// Coroutine that executes a single trial with its 4 phases.
      
    /// <param name="condition">The condition for the current trial.</param>
    private IEnumerator RunSingleTrial(TrialCondition condition)
    {
        float topSpeed = 0;
        float direction = 1.0f; // 1 for clockwise, -1 for counter-clockwise

        // Determine speed and direction from the condition
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

        // Send the UDP command only if UseChairConnection is true
        if (UseChairConnection && sender != null)
        {
            sender.Send(Encoding.ASCII.GetBytes("start"), "start".Length);
        }

        // --- 1. Acceleration Phase ---
        SendMarker($"start_acceleration_{condition}");
        yield return StartCoroutine(ChangeSpeed(0, topSpeed * direction, AccelerationDuration));

        // --- 2. Stable Rotation Phase ---
        SendMarker($"start_stable_{condition}");
        yield return StartCoroutine(ChangeSpeed(topSpeed * direction, topSpeed * direction, StableRotationDuration));

        // --- 3. Deceleration Phase ---
        SendMarker($"start_deceleration_{condition}");
        yield return StartCoroutine(ChangeSpeed(topSpeed * direction, 0, DecelerationDuration));

        // --- 4. Stop Phase ---
        SendMarker($"stop_trial_{condition}");
        currentVelocity = 0;
        string stopMessage = string.Format("udpvelocity {0}", currentVelocity);
        // Send UDP markers only if UseChairConnection is true
        if (UseChairConnection && sender != null)
        {
            sender.Send(Encoding.ASCII.GetBytes(stopMessage), stopMessage.Length);
        }
    }

      
    /// A generic coroutine to smoothly change speed over a given duration.
      
    private IEnumerator ChangeSpeed(float startSpeed, float endSpeed, float duration)
    {
        float elapsedTime = 0;
        while (elapsedTime < duration)
        {
            currentVelocity = Mathf.Lerp(startSpeed, endSpeed, elapsedTime / duration);
            // Send UDP command only if UseChairConnection is true
            if (UseChairConnection && sender != null)
            {
                string message = string.Format("udpvelocity {0}", currentVelocity);
                sender.Send(Encoding.ASCII.GetBytes(message), message.Length);
            }

            elapsedTime += Time.deltaTime;
            yield return null; // Wait for the next frame
        }
        // Ensure the final speed is set precisely
        currentVelocity = endSpeed;
        // Send UDP command one last time to ensure the final speed is sent
        if (UseChairConnection && sender != null)
        {
            string finalMessage = string.Format("udpvelocity {0}", currentVelocity);
            sender.Send(Encoding.ASCII.GetBytes(finalMessage), finalMessage.Length);
        }
    }

      
    /// Coroutine for a forced, rapid stop.
      
    private IEnumerator ForceStopRotation()
    {
        isExperimentRunning = false;
        yield return StartCoroutine(ChangeSpeed(currentVelocity, 0, DecelerationDuration)); // Use normal deceleration time
        // Send UDP command to stop the chair only if UseChairConnection is true
        if (UseChairConnection && sender != null)
        {
            sender.Send(Encoding.ASCII.GetBytes("stop"), "stop".Length);
        }
        Debug.Log("Rotation stopped completely.");
    }

      
    /// Sends a string marker to the LSL outlet.
      
    private void SendMarker(string marker)
    {
        if (chairRotationOutlet != null)
        {
            string[] sample = { marker };
            chairRotationOutlet.push_sample(sample);
            Debug.Log("Sent LSL Marker: " + marker);
        }
    }

      
    /// Ensures UDP client is closed when the application quits.
      
    private void OnApplicationQuit()
    {
        if (sender != null)
        {
            // Ensure the chair is stopped before quitting
            StartCoroutine(ForceStopRotation());
            sender.Close();
        }
    }
}
