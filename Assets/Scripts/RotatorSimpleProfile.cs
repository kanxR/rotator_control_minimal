using UnityEngine;
using System.Collections;
using System.Net.Sockets;
using System.Net;
using System.Text;
using UnityEngine.InputSystem;

public class RotatorSimpleProfile : MonoBehaviour
{

    [Header("Communication settings")]
    [Range(1, 60)]
    public float PackagePerSecond = 30;
    private int remotePort = 42424;
    private string remoteIP = "100.1.1.101";
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
        }
    }


    public void StartRotation()
    {
        IsSending = true;
        lastSendTime = Time.time;
        InvokeRepeating("Send", 0, sendRate);
        sender.Send(Encoding.ASCII.GetBytes("start"), "start".Length);
    }

    public void StopRotation()
    {
        if (!isStopping)
        {
            Debug.Log("Starting deceleration...");
            IsSending = false;
            isStopping = true; // Enter deceleration phase
            lastSendTime = Time.time; // Reset timing for consistent deltaTime
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