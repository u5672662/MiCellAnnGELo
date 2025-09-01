using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Builds and displays a formatted list of annotation entries derived from <see cref="MarkerAnnotation"/> events.
/// </summary>
public class AnnotationDisplay : MonoBehaviour
{
    private TextMeshProUGUI _annotationText;

    private readonly List<string> _annotationLines = new();

    /// <summary>
    /// Initialises the display with the given annotations and target text component, and subscribes to events.
    /// </summary>
    /// <param name="markerAnnotations">Array of marker annotation sources to observe.</param>
    /// <param name="annotationText">UI text component that will render the formatted annotation list.</param>
    public void Initialize(MarkerAnnotation[] markerAnnotations, TextMeshProUGUI annotationText)
    {
        if (markerAnnotations == null)
        {
            Debug.LogError("markerAnnotations is null in AnnotationDisplay.Initialize.");
            return;
        }
        if (annotationText == null)
        {
            Debug.LogError("annotationText is null in AnnotationDisplay.Initialize.");
            return;
        }
        _annotationText = annotationText;
        foreach (var markerAnnotation in markerAnnotations)
        {
            markerAnnotation.OnMarkerPlaced += AddAnnotationLine;
            markerAnnotation.OnMarkerRemoved += RemoveAnnotationLine;
        }
    }

    private void OnDisable()
    {
        // Unsubscribing would be ideal, but requires holding references to markerAnnotations.
    }
    
    /// <summary>
    /// Rebuilds the display contents from the provided annotations' current state.
    /// </summary>
    /// <param name="markerAnnotations">Array of marker annotations to enumerate.</param>
    public void DisplayAnnotations(MarkerAnnotation[] markerAnnotations)
    {
        if (markerAnnotations == null)
        {
            return;
        }
        ClearDisplay();
        foreach (var markerAnnotation in markerAnnotations)
        {
            var allMarkers = markerAnnotation.ReturnMarkerInfo();
            for (int frame = 0; frame < allMarkers.Count; frame++)
            {
                foreach (var marker in allMarkers[frame])
                {
                    AddAnnotationLine(markerAnnotation, frame, marker.transform.localPosition);
                }
            }
        }
    }

    private void AddAnnotationLine(MarkerAnnotation source, int frame, Vector3 position)
    {
        string owner = "Unknown";
        Color colour = Color.white;

        // Attempt to find the marker object that was just added so we can extract its metadata.
        List<List<GameObject>> markerInfo = source.ReturnMarkerInfo();
        if (frame >= 0 && frame < markerInfo.Count)
        {
            foreach (GameObject m in markerInfo[frame])
            {
                if (m == null) continue;
                if (Vector3.Distance(m.transform.localPosition, position) < 0.0001f)
                {
                    if (m.TryGetComponent(out MarkerMeta meta))
                    {
                        owner = meta.OwnerName;
                        colour = meta.MarkerColour;
                    }
                    break;
                }
            }
        }

        string hex = ColorUtility.ToHtmlStringRGB(colour);
        string line = $"Frame {frame} | <color=#{hex}>●</color> {owner} @ {position.ToString("F3")}";
        _annotationLines.Add(line);
        UpdateAnnotationText();
    }

    private void RemoveAnnotationLine(MarkerAnnotation source, int frame, Vector3 position)
    {
        // Build the same string format used in AddAnnotationLine in order to remove it
        List<string> toRemove = new List<string>();
        foreach (string line in _annotationLines)
        {
            if (line.Contains($"Frame {frame}") && line.Contains(position.ToString("F3")))
            {
                toRemove.Add(line);
            }
        }
        foreach (var line in toRemove)
        {
            _annotationLines.Remove(line);
        }
        UpdateAnnotationText();
    }

    private void UpdateAnnotationText()
    {
        if (_annotationText == null)
        {
            return;
        }

        var sb = new StringBuilder();
        // Sort lines to keep them in a consistent order
        _annotationLines.Sort(); 
        foreach (var line in _annotationLines)
        {
            sb.AppendLine(line);
        }
        _annotationText.text = sb.ToString();
    }
    
    /// <summary>
    /// Kept for compatibility with the UI controller event hook; no longer filters text by frame.
    /// </summary>
    /// <param name="frame">Current frame index.</param>
    public void UpdateDisplayForFrame(int frame)
    {
        // This method is now kept for compatibility with the UI Controller event hook,
        // but it no longer filters the text. The main list is always shown.
    }

    /// <summary>
    /// Clears all displayed annotation lines and updates the UI text.
    /// </summary>
    public void ClearDisplay()
    {
        _annotationLines.Clear();
        UpdateAnnotationText();
    }
}
