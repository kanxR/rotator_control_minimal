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

    [Header("Calibration Settings")]
    public float AccelDuration = 2.0f;
    public float DecelDuration = 2.0f;
    private const int NumberOfTrials = 3;

    [Header("Audio Settings")]
    public AudioClip beepClip;
    private AudioSource audioSource;

    private enum CalibrationPhase
    {
        Idle,
        SelectingSpeed,
        Accelerating,
        Steady,
        Decelerating,
        Finished
    }

    private CalibrationPhase currentPhase = CalibrationPhase.Idle;
    private float targetSpeed = 0f;
    private float currentVelocity = 0f;
    private float phaseTimer = 0f;
    private float habituationTimer = 0f;
    private bool habituationTimerRunning = false;
    private int currentTrial = 0;
    private List<float> habituationTimes = new List<float>();

    void Start()
    {
        // --- Initialize Communication ---
        InitSender();
        sendRate = 1.0f / PackagePerSecond;

        // --- Initialize Audio ---
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;

        // --- Initial State ---
        currentPhase = CalibrationPhase.SelectingSpeed;
        Debug.Log("--- Chair Calibration Ready ---");
        Debug.Log("Press '1' for 30 deg/s, '2' for 60 deg/s, '3' for 90 deg/s to start.");
    }

    void Update()
    {
        // --- State Machine Logic ---
        switch (currentPhase)
        {
            case CalibrationPhase.SelectingSpeed:
                HandleSpeedSelection();
                break;

            case CalibrationPhase.Accelerating:
                UpdateAcceleration();
                break;

            case CalibrationPhase.Steady:
                UpdateSteady();
                break;

            case CalibrationPhase.Decelerating:
                UpdateDeceleration();
                break;
        }

        // --- Send Data to Chair ---
        // This part runs continuously to update the chair's velocity
        if (currentPhase == CalibrationPhase.Accelerating ||
            currentPhase == CalibrationPhase.Steady ||
            currentPhase == CalibrationPhase.Decelerating)
        {
            SendVelocityToChair();
        }
    }

    private void HandleSpeedSelection()
    {
        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            StartCalibration(30.0f);
        }
        else if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            StartCalibration(60.0f);
        }
        else if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            StartCalibration(90.0f);
        }
    }

    private void StartCalibration(float speed)
    {
        targetSpeed = speed;
        habituationTimes.Clear();
        currentTrial = 0;
        Debug.Log($"Calibration started for {targetSpeed} deg/s.");
        StartNewTrial();
    }

    private void StartNewTrial()
    {
        if (currentTrial >= NumberOfTrials)
        {
            FinishCalibration();
            return;
        }
        currentTrial++;
        Debug.Log($"--- Starting Calibration Trial {currentTrial}/{NumberOfTrials} ---");
        TransitionToPhase(CalibrationPhase.Accelerating);
    }

    private void UpdateAcceleration()
    {
        phaseTimer += Time.deltaTime;
        currentVelocity = RaisedCosineSpeed(0, targetSpeed, phaseTimer, AccelDuration);

        if (phaseTimer >= AccelDuration)
        {
            currentVelocity = targetSpeed;
            TransitionToPhase(CalibrationPhase.Steady);
        }
    }

    private void UpdateSteady()
    {
        // The timer for habituation starts once we are in the steady phase
        if (habituationTimerRunning)
        {
            habituationTimer += Time.deltaTime;
        }

        // Check for user input to stop the timer
        if (Keyboard.current.digit6Key.wasPressedThisFrame)
        {
            RecordHabituationTime();
            TransitionToPhase(CalibrationPhase.Decelerating);
        }
    }

    private void UpdateDeceleration()
    {
        phaseTimer += Time.deltaTime;
        // Decelerate from the speed at which the user responded
        currentVelocity = RaisedCosineSpeed(targetSpeed, 0, phaseTimer, DecelDuration);

        if (phaseTimer >= DecelDuration)
        {
            currentVelocity = 0;
            // *** FIX: Change state to Idle immediately to prevent this block from running again. ***
            // This stops the UpdateDeceleration logic and prevents the coroutine from being called multiple times.
            currentPhase = CalibrationPhase.Idle;

            Debug.Log("Rotation stopped. Preparing for next trial.");
            // Wait a moment before starting the next trial
            StartCoroutine(InterTrialDelay());
        }
    }

    private IEnumerator InterTrialDelay()
    {
        // Send a final stop command to ensure the chair is stopped.
        StopChair();
        yield return new WaitForSeconds(2.0f); // A brief pause between trials
        StartNewTrial();
    }


    private void RecordHabituationTime()
    {
        habituationTimerRunning = false;
        habituationTimes.Add(habituationTimer);
        Debug.Log($"Habituation time for trial {currentTrial}: {habituationTimer:F2} seconds.");
    }

    private void FinishCalibration()
    {
        currentPhase = CalibrationPhase.Finished;
        float averageTime = 0;
        if (habituationTimes.Count > 0)
        {
            averageTime = habituationTimes.Average();
        }
        Debug.Log("--- Calibration Finished ---");
        Debug.Log($"Recorded times: {string.Join(", ", habituationTimes.Select(t => t.ToString("F2")))}");
        Debug.Log($"Average Habituation Time: {averageTime:F2} seconds.");
        Debug.Log("You can now use this value to set 'SteadyDuration' in the main experiment script.");
        Debug.Log("To run another calibration, please restart the scene.");
        StopChair();
    }

    private void TransitionToPhase(CalibrationPhase nextPhase)
    {
        Debug.Log($"Transitioning to: {nextPhase}");
        phaseTimer = 0f;
        currentPhase = nextPhase;

        if (nextPhase == CalibrationPhase.Accelerating)
        {
            if (UseChairConnection && sender != null)
            {
                sender.Send(Encoding.ASCII.GetBytes("start"), "start".Length);
            }
        }
        else if (nextPhase == CalibrationPhase.Steady)
        {
            PlayBeep();
            habituationTimer = 0f;
            habituationTimerRunning = true;
            Debug.Log("Reached target speed. Timer started. Press '6' when you feel stable.");
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

    private void SendVelocityToChair()
    {
        if (UseChairConnection && sender != null)
        {
            string message = $"udpvelocity {currentVelocity}";
            byte[] data = Encoding.ASCII.GetBytes(message);
            sender.Send(data, data.Length);
        }
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

    private void PlayBeep()
    {
        if (beepClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(beepClip);
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
