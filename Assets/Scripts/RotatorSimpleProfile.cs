using LSL;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class RotatorSimpleProfile : MonoBehaviour
{

    //private StreamOutlet outlet;

    //// LSL Stream info definition
    //private const string StreamName = "UnityChairRotationStream";
    //private const string StreamType = "ChairRotation";
    //private const int ChannelCount = 1;
    //private const double NominalSamplingRate = 0.0; // non-periodic event=>0
    //private const LSL.channel_format_t ChannelFormat = LSL.channel_format_t.cf_string;
    //private const string SourceID = "UnityChairRotation_12345";

    // LSL Chair Rotation Stream
    private StreamOutlet chairRotationOutlet;
    private const string ChairRotationStreamName = "UnityChairRotationStream";
    private const string ChairRotationStreamType = "ChairRotationMarkers";
    private const int ChairRotationChannelCount = 1;
    private const double ChairRotationNominalSamplingRate = 0.0; // non-periodic
    private const LSL.channel_format_t ChairRotationChannelFormat = LSL.channel_format_t.cf_string;
    private const string ChairRotationSourceID = "UnityChairRotation_12345";



    [Header("Communication settings")]
    [Range(1, 60)]
    public float PackagePerSecond = 30;
    private int remotePort = 42424;
    private string remoteIP = "127.179.177.25";
    private int localPort = 42434;
    private UdpClient sender;
    private float sendRate;
    [HideInInspector]public bool IsSending = false;


    [Header("Rotation settings")]
    public float RotationVelocity = 30.0f; // Set your desired rotation speed here
    public float RotationDuration = 60f; // Set your rotation duration in seconds
    public float RotationAcceleration = 40f;
    private float fVelocity = 0f;
    private float lastSendTime = 0f;
    private bool isStopping = false;
    private Coroutine rotationCoroutine;


    private void Start()
    {
        InitSender();

        // Initialize LSL Chair Rotation Stream
        StreamInfo chairRotationInfo = new StreamInfo(
            ChairRotationStreamName,
            ChairRotationStreamType,
            ChairRotationChannelCount,
            ChairRotationNominalSamplingRate,
            ChairRotationChannelFormat,
            ChairRotationSourceID
        );
        chairRotationOutlet = new StreamOutlet(chairRotationInfo);
    }

    private void SendChairRotationMarker(string marker)
    {
        if (chairRotationOutlet != null)
        {
            string[] sample = { marker };
            chairRotationOutlet.push_sample(sample);
            Debug.Log("Sent Chair Rotation Marker: " + marker);
        }
    }

    private void Update()
    {
        // Here I set it to start with the Space Key and stop with the S key.
        if (Keyboard.current.spaceKey.wasPressedThisFrame && !IsSending)
        {
            Debug.Log("Space pressed - Start Rotation");
            StartRotation();
        }

        if (Keyboard.current.sKey.wasPressedThisFrame && IsSending)
        {
            Debug.Log("S pressed - Emergency Stop");
            StopRotation();
        }
    }

    private void InitSender()
    {
        sendRate = (1000 / PackagePerSecond) / 1000;
        sender = new UdpClient(localPort, AddressFamily.InterNetwork);
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(remoteIP), remotePort);
        sender.Connect(endPoint);
    }


    private void Send()
    {
        float currentTime = Time.time;
        float deltaTime = currentTime - lastSendTime;
        lastSendTime = currentTime;

        // Here you can customize the rotation logic
        if (IsSending)
        {
            // Accelerate
            fVelocity += RotationAcceleration * deltaTime;
            fVelocity = Mathf.Min(fVelocity, RotationVelocity);
        }
        else if (isStopping)
        {
            // Decelerate
            fVelocity -= RotationAcceleration * deltaTime;
            fVelocity = Mathf.Max(fVelocity, 0f);
        }

        // Always send current velocity during active or stopping phase
        string customMessage = string.Format("udpvelocity {0}", fVelocity);
        sender.Send(Encoding.ASCII.GetBytes(customMessage), customMessage.Length);

        // Fully stopped — send stop signal and cancel repeating
        if (isStopping && fVelocity <= 0f)
        {
            isStopping = false;
            CancelInvoke("Send");
            sender.Send(Encoding.ASCII.GetBytes("stop"), "stop".Length);
            Debug.Log("Rotation stopped completely.");

            // Send LSL marker for rotation end
            SendChairRotationMarker("rotation_ended");
        }
    }


    public void StartRotation()
    {
        IsSending = true;
        lastSendTime = Time.time;
        InvokeRepeating("Send", 0, sendRate);
        sender.Send(Encoding.ASCII.GetBytes("start"), "start".Length);

        // Send LSL marker for rotation start
        SendChairRotationMarker("rotation_started");
    }

    public void StopRotation()
    {
        if (!isStopping)
        {
            Debug.Log("Starting deceleration...");
            IsSending = false;
            isStopping = true; // Enter deceleration phase
            lastSendTime = Time.time; // Reset timing for consistent deltaTime

            SendChairRotationMarker("rotation_decelerating");
        }

        if (rotationCoroutine != null)
        {
            StopCoroutine(rotationCoroutine);
        }
    }

    private void StopSystem()
    {
        IsSending = false;
        sender.Send(Encoding.ASCII.GetBytes("stop"), "stop".Length);
        if (rotationCoroutine != null)
        {
            StopCoroutine(rotationCoroutine);
        }
    }

    private void OnApplicationQuit()
    {
        if (this.enabled)
        {
            StopSystem();
            sender.Close();
        }
    }
}