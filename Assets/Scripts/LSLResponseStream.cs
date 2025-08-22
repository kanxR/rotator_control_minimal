using UnityEngine;
using LSL;

public class LSLResponseStream : MonoBehaviour
{
    
    private StreamOutlet outlet;

    // LSL Stream info definition
    private const string StreamName = "UnityResponseStream";
    private const string StreamType = "ResponseMarkers";
    private const int ChannelCount = 1;
    private const double NominalSamplingRate = 0.0; // non-periodic event=>0
    private const LSL.channel_format_t ChannelFormat = LSL.channel_format_t.cf_int32;// Using integers for markers
    private const string SourceID = "UnityMarker_12345";

    void Start()
    {
        // Create LSL StreamInfo
        StreamInfo streamInfo = new StreamInfo(StreamName, StreamType, ChannelCount, NominalSamplingRate, ChannelFormat, SourceID);

        //Create Outlet
        outlet = new StreamOutlet(streamInfo);

        Debug.Log("Starting LSL Response stream.");
    }

    void Update()
    {
        //// Send marker: 9 --- Feeling clockwise rotation
        //if (Input.GetKeyDown(KeyCode.Keypad9))
        //{
        //    SendMarker(9);
        //}

        //// Send marker: 3 --- Feeling counter clockwise rotation
        //if (Input.GetKeyDown(KeyCode.Keypad3))
        //{
        //    SendMarker(10);
        //}

        // Send marker: 6 --- Feeling stable
        if (Input.GetKeyDown(KeyCode.Keypad6))
        {
            SendMarker(11);
        }
    }

   
    void SendMarker(int responseValue)
    {
        // Prepare the markers as string array
        int[] marker = { responseValue };

        // Push marker to LSL
        outlet.push_sample(marker);

        // To check in Unity console
        Debug.Log("Sent LSL Response: " + responseValue);
    }
}