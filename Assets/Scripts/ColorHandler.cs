using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ColorHandler : MonoBehaviour
{
    public GameObject buttonPrefab;
    public Transform markerPanel;
    public Transform labelPanel;
    public Color32 OpacityColor = new Color32(100, 100, 100, 200);

    // List of paint colors
    private Color32[] LabelColorList = new Color32[]
    {
        new Color32(230,230,230,200),
        new Color32(180,0,0,200),
        new Color32(0,0,180,200),
        new Color32(0,46,0,200),
        new Color32(0,108,190,200),
        new Color32(162,198,80,200),
        new Color32(118,0,86,200),
        new Color32(182,130,82,200),
        new Color32(84,74,86,200),
        new Color32(194,132,200,200),
        new Color32(0,200,128,200),
        new Color32(102,140,92,200),
        new Color32(8,2,74,200),
        new Color32(0,178,200,200),
        new Color32(134,74,198,200),
        new Color32(0,150,0,200),
        new Color32(116,114,0,200),
        new Color32(200,84,84,200),
        new Color32(32,0,0,200),
        new Color32(84,0,0,200),
    };
    // List of marker colors
    private Color32[] MarkerColorList = new Color32[]
    {
        new Color32(0,0,40,200),
        new Color32(180,0,180,200),
        new Color32(0,200,0,200),
        new Color32(200,144,0,200),
        new Color32(0,166,142,200),
        new Color32(198,116,134,200),
        new Color32(80,52,0,200),
        new Color32(2,46,152,200),
        new Color32(56,112,0,200),
        new Color32(122,160,200,200),
        new Color32(198,200,142,200),
        new Color32(200,0,66,200),
        new Color32(2,74,70,200),
        new Color32(180,82,34,200),
        new Color32(104,72,126,200),
        new Color32(194,200,0,200),
        new Color32(198,48,134,200),
        new Color32(130,120,102,200),
        new Color32(76,0,38,200),
        new Color32(100,200,90,200),
    };

    // Current number of paint colors
    private int nPaintColors = 10;
    // Current number of marker colors
    private int nMarkerColors = 10;

    // No initialization performed at present
    //void Start()
    //{

    //}

    // Returns the label color of the specified index.
    public Color32 GetPaintColor(int index)
    {
        // Ensure index is within range
        index %= nPaintColors;
        // Return the requested color
        return LabelColorList[index];
    }

    // Paint background color is first label color
    public Color32 GetPaintBackground()
    {
        return LabelColorList[0];
    }

    // Change index to be within paint index range, but at extremes (0 or n)
    // we move to one in from the other extreme (0->n-1, n->1)
    public int GetPaintCIndex(int index)
    {
        Debug.Log("Getting true index for label index " + index + " from list of " + nPaintColors + ".");
        if (index % nPaintColors == 0)
        {
            // exclude zero label.
            // Set to n - 1 when coming from even multiple of n (including 0)
            // Set to 1 when coming from odd multiple of n (including n)
            if ((index / nPaintColors) % 2 == 0)
                index = nPaintColors - 1;
            else
                index = 1;
        }
        else
        {
            // ensure index is in range
            index %= nPaintColors;
        }
        return index;
    }

    // Returns the marker color of the specified index.
    public Color32 GetMarkerColor(int index)
    {
        Debug.Log("Getting marker color for color index " + index + ".");
        // ensure index is in range
        index %= nMarkerColors;        
        // Return the requested color
        return MarkerColorList[index];
    }

    // Markers use same index method as paints, although there is no background marker
    public int GetMarkerCIndex(int index)
    {
        Debug.Log("Getting true index for input index " + index + " from list of " + nMarkerColors + ".");
        if (index % nMarkerColors == 0)
        {
            // exclude zero label.
            // Set to n - 1 when coming from even multiple of n (including 0)
            // Set to 1 when coming from odd multiple of n (including n)
            if ((index / nMarkerColors) % 2 == 0)
                index = nMarkerColors - 1;
            else
                index = 1;
        }
        else
        {
            // ensure index is in range
            index %= nMarkerColors;
        }
        return index;
    }

    // TODO: allow user to increase number of colors.
    // Increase the number of marker colors
    public void AddMarkerColor()
    {
        nMarkerColors += 1;
    }

    // Increase the number of label colors
    public void AddLabelColor()
    {
        nPaintColors += 1;
    }
}
