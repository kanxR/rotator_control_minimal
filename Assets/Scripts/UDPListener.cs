using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System;
using UnityEngine;
using System.Collections;

public class UDPListener : MonoBehaviour
{
    private int port = 42425;
    public int i_cur_angle = -1;
    private bool isListening = true;

    void Start()
    {
        listenThread = new Thread(new ThreadStart(SimplestReceiver));
        listenThread.Start();
    }

    private Thread listenThread;
    private UdpClient listenClient;
    private void SimplestReceiver()
    {
        Debug.Log(",,,,,,,,,,,, Overall listener thread started.");

        IPEndPoint listenEndPoint = new IPEndPoint(IPAddress.Any, port);
        listenClient = new UdpClient(listenEndPoint);
        Debug.Log(",,,,,,,,,,,, listen client started.");

        while (isListening)
        {
            //Debug.Log(",,,,, listen client listening");

            try
            {
                Byte[] data = listenClient.Receive(ref listenEndPoint);
                string message = Encoding.ASCII.GetString(data);
                //Debug.Log("Listener heard: " + message);

                if (message.StartsWith("rotvel"))
                {
                    if (int.TryParse(message.Split(" ")[1], out i_cur_angle))
                    {

                    }
                }
            }
            catch (SocketException ex)
            {
                if (ex.ErrorCode != 10060)
                    Debug.Log("a more serious error " + ex.ErrorCode);
                else
                    Debug.Log("expected timeout error");
            }
            catch (Exception ex)
            {
                Debug.Log("Error in UDPListener " + ex.Message);
            }

            Thread.Sleep(5); // tune for your situation, can usually be omitted
        }
    }

    void OnDestroy() { CleanUp(); }
    void OnDisable() { CleanUp(); }
    // be certain to catch ALL possibilities of exit in your environment,
    // or else the thread will typically live on beyond the app quitting.

    void CleanUp()
    {
        isListening = false;
        if (listenThread != null && listenThread.IsAlive)
        {
            listenThread.Join(5000);
            listenThread = null;
            Debug.Log("Listener thread cleaned up");
        }

        if (listenClient != null)
        {
            listenClient.Close();
            listenClient = null;
            Debug.Log("Listener client cleaned up");
        }

        //Debug.Log("Cleanup for listener...");

        //// note, consider carefully that it may not be running
        //listenClient.Close();
        //Debug.Log(",,,,, listen client correctly stopped");

        //listenThread.Abort();
        //listenThread.Join(5000);
        //listenThread = null;
        //Debug.Log(",,,,, listener thread correctly stopped");
    }
}