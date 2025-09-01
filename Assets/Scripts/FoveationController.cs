using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Enables and configures foveated rendering at runtime.
/// This script should be attached to a GameObject in the main scene.
/// </summary>
public class FoveationController : MonoBehaviour
{
    void Start()
    {
        List<XRDisplaySubsystem> xrDisplays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(xrDisplays);

        if (xrDisplays.Count > 0)
        {
            if(xrDisplays[0] != null)
            {
                // Set foveation to full strength
                xrDisplays[0].foveatedRenderingLevel = 1.0f;
                // Enable gaze-based foveation
                xrDisplays[0].foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
                Debug.Log("Foveated rendering enabled at full strength with gaze tracking.");
            }
            else
            {
                Debug.LogWarning("XRDisplaySubsystem is null. Foveated rendering cannot be enabled.");
            }

        }
        else
        {
            Debug.LogWarning("No XRDisplaySubsystem found. Foveated rendering will not be enabled.");
        }
    }
}
