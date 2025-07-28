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
        // Send marker: R --- Feeling clockwise rotation
        if (Input.GetKeyDown(KeyCode.R))
        {
            SendMarker(7);
        }

        // Send marker: L --- Feeling counter clockwise rotation
        if (Input.GetKeyDown(KeyCode.L))
        {
            SendMarker(8);
        }

        // Send marker: N --- Feeling stable
        if (Input.GetKeyDown(KeyCode.N))
        {
            SendMarker(9);
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