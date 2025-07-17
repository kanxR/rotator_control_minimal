using UnityEngine;
using LSL;

public class LSLMarkerStream : MonoBehaviour
{
    
    private StreamOutlet outlet;

    // LSL Stream info definition
    private const string StreamName = "UnityMarkerStream";
    private const string StreamType = "Markers";
    private const int ChannelCount = 1;
    private const double NominalSamplingRate = 0.0; // non-periodic event=>0
    private const LSL.channel_format_t ChannelFormat = LSL.channel_format_t.cf_string;
    private const string SourceID = "UnityMarker_12345";

    void Start()
    {
        // Create LSL StreamInfo
        StreamInfo streamInfo = new StreamInfo(StreamName, StreamType, ChannelCount, NominalSamplingRate, ChannelFormat, SourceID);

        //Create Outlet
        outlet = new StreamOutlet(streamInfo);

        Debug.Log("Starting LSL marker stream.");
    }

    void Update()
    {
        // Send marker: Y --- Feeling rotation
        if (Input.GetKeyDown(KeyCode.Y))
        {
            SendMarker("Y_key_pressed");
        }

        // Send marker: N --- Feeling stable
        if (Input.GetKeyDown(KeyCode.N))
        {
            SendMarker("N_key_pressed");
        }
    }

   
    void SendMarker(string markerText)
    {
        // Prepare the markers as string array
        string[] marker = { markerText };

        // Push marker to LSL
        outlet.push_sample(marker);

        // To check in Unity console
        Debug.Log("Sent LSL Marker: " + markerText);
    }
}