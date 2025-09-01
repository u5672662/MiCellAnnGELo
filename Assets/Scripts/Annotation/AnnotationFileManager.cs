using SimpleFileBrowser;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Coordinates annotation data across the scene: maintains references, clears, saves, and loads annotations.
/// </summary>
public partial class AnnotationManager : MonoBehaviour
{
    private MarkerAnnotation[] _markerAnnotations;
    private AnnotationDisplay _annotationDisplay;

    /// <summary>
    /// Globally accessible instance for convenience in UI bindings.
    /// </summary>
    public static AnnotationManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    /// <summary>
    /// Initialises manager references for later operations.
    /// </summary>
    /// <param name="markerAnnotations">Array of annotation sources to manage.</param>
    /// <param name="annotationDisplay">Display component used for rendering annotation text.</param>
    public void Initialize(MarkerAnnotation[] markerAnnotations, AnnotationDisplay annotationDisplay)
    {
        _markerAnnotations = markerAnnotations;
        _annotationDisplay = annotationDisplay;
    }

    /// <summary>
    /// Clears the on-screen display and removes all markers from all managed sources.
    /// </summary>
    public void ClearAllAnnotations()
    {
        if (_annotationDisplay != null)
        {
            _annotationDisplay.ClearDisplay();
        }

        if (_markerAnnotations != null)
        {
            foreach (var markerAnnotation in _markerAnnotations)
            {
                if (markerAnnotation != null)
                {
                    markerAnnotation.ClearAllMarkers();
                }
            }
        }
    }

    /// <summary>
    /// Opens a file save dialogue and writes all annotations to a CSV file.
    /// </summary>
    public void SaveAnnotations()
    {
        FileBrowser.SetFilters(true, new FileBrowser.Filter("CSV", ".csv"));
        FileBrowser.ShowSaveDialog(OnSaveSuccess, OnSaveCancel, FileBrowser.PickMode.Files, false, null, "annotations.csv", "Save Annotations");
    }

    private void OnSaveSuccess(string[] paths)
    {
        if (paths.Length == 0) return;

        var path = paths[0];

        try
        {
            var csvContent = new StringBuilder();
            csvContent.AppendLine("Source,Frame,X,Y,Z,Scale,Owner,ColorHex");

            if (_markerAnnotations == null || _markerAnnotations.Length == 0)
            {
                Debug.LogError("No MarkerAnnotation references are set in AnnotationManager.");
                return;
            }

            foreach (var markerAnnotation in _markerAnnotations)
            {
                if (markerAnnotation == null)
                {
                    Debug.LogWarning("A MarkerAnnotation in the array is null.");
                    continue;
                }

                IEnumerable<string> markersData = markerAnnotation.SerialiseMarkers();
                foreach (string line in markersData)
                {
                    csvContent.AppendLine($"{markerAnnotation.SourceName},{line}");
                }
            }

            FileBrowserHelpers.WriteTextToFile(path, csvContent.ToString());
            Debug.Log($"Annotations saved to {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save annotations to {path}. Error: {e.Message}\n{e.StackTrace}");
        }
    }

    private void OnSaveCancel()
    {
        Debug.Log("Save dialog was cancelled.");
    }
    
    /// <summary>
    /// Opens a file load dialogue and populates annotations from the selected CSV file.
    /// </summary>
    public void LoadAnnotations()
    {
        FileBrowser.SetFilters(true, new FileBrowser.Filter("CSV", ".csv"));
        FileBrowser.ShowLoadDialog(OnLoadSuccess, OnLoadCancel, FileBrowser.PickMode.Files, false, null, "annotations.csv", "Load Annotations");
    }

    private void OnLoadSuccess(string[] paths)
    {
        if (paths.Length == 0) return;

        string path = paths[0];
        string entireText = FileBrowserHelpers.ReadTextFromFile(path);

        ClearAllAnnotations();
        foreach (var markerAnnotation in _markerAnnotations)
        {
            markerAnnotation.LoadMarkerInfo(entireText);
        }

        Debug.Log($"Annotations loaded from {path}");
    }

    private void OnLoadCancel()
    {
        Debug.Log("Load dialog was cancelled.");
    }
}


