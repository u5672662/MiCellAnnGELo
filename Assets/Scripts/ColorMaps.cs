using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class ColorMaps
{
    // List of label colors
    // TODO: import list of glasbey colors at startup
    public static Color32[] LabelColorList = new Color32[]
    {
        new Color32(249, 249, 249, 150), // off white
        new Color32(180,   0,   0, 150), // red
        new Color32(  0,   0, 180, 150)  // blue
    };
    // List of marker colors
    //TODO: import list of glasbey colors at startup
    public static Color32[] MarkerColorList = new Color32[]
    {
        new Color32(180,   0, 180, 150)  // pink
    };
    public static Color32 OpacityColor = new Color32(100, 100, 100, 150);

    public static int nLabels = 3;
    public static int nMarkers = 1;


    // mapping of color channels
    public static int[] red_map = new int[] { 1, 0, 0 };
    public static int[] green_map = new int[] { 0, 1, 0 };
    public static int[] blue_map = new int[] { 0, 0, 1 };

    //[RuntimeInitializeOnLoadMethod]
    //public static void Init()
    //{
    //    // load colors here.
    //    nLabels = LabelColorList.Length;
    //    nMarkers = MarkerColorList.Length;
    //}

    // Returns the label color of the specified index.
    public static Color32 GetLabelColor(int index)
    {
        // Ensure index is within range
        index %= nLabels;
        // Return the requested color
        return LabelColorList[index];
    }

    // Returns the marker color of the specified index.
    public static Color32 GetMarkerColor(int index)
    {
        // Ensure index is within range
        index %= nMarkers;
        // Return the requested color
        return MarkerColorList[index];
    }

    // Returns the mapped input color
    // TODO: implement this, possible using shaders
}
