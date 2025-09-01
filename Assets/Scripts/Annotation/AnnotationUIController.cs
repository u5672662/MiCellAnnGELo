using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires UI controls to annotation systems: initialises display/manager components, populates text,
/// and hooks button actions and frame-change events.
/// </summary>
public class AnnotationUIController : MonoBehaviour
{
    [SerializeField]
    private MarkerAnnotation[] markerAnnotations;

    [SerializeField]
    private TextMeshProUGUI annotationText;

    [SerializeField]
    private Button[] saveButtons;

    [SerializeField]
    private Button[] loadButtons;

    [SerializeField]
    private MeshController meshController;

    private void Start()
    {
        if (annotationText == null)
        {
            Debug.LogError("AnnotationText is not assigned in the AnnotationUIController.");
            return;
        }

        if (saveButtons == null || saveButtons.Length == 0)
        {
            Debug.LogError("SaveButtons are not assigned in the AnnotationUIController.");
            return;
        }

        if (loadButtons == null || loadButtons.Length == 0)
        {
            Debug.LogError("LoadButtons are not assigned in the AnnotationUIController.");
            return;
        }

        if (markerAnnotations == null || markerAnnotations.Length == 0)
        {
            Debug.LogError("MarkerAnnotations are not assigned in the AnnotationUIController.");
            return;
        }
        
        if (meshController == null)
        {
            Debug.LogError("MeshController is not assigned in the AnnotationUIController.");
            return;
        }

        // Get or Add AnnotationDisplay
#if UNITY_2023_1_OR_NEWER || UNITY_2022_2_OR_NEWER
        AnnotationDisplay annotationDisplay = FindFirstObjectByType<AnnotationDisplay>();
#else
        AnnotationDisplay annotationDisplay = FindObjectOfType<AnnotationDisplay>();
#endif
        if (annotationDisplay == null)
        {
            annotationDisplay = gameObject.AddComponent<AnnotationDisplay>();
        }

        // Get or Add AnnotationManager
#if UNITY_2023_1_OR_NEWER || UNITY_2022_2_OR_NEWER
        AnnotationManager annotationManager = FindFirstObjectByType<AnnotationManager>();
#else
        AnnotationManager annotationManager = FindObjectOfType<AnnotationManager>();
#endif
        if (annotationManager == null)
        {
            annotationManager = gameObject.AddComponent<AnnotationManager>();
        }
        annotationManager.Initialize(markerAnnotations, annotationDisplay);
        
        // Initialize the AnnotationDisplay
        annotationDisplay.Initialize(markerAnnotations, annotationText);
        annotationDisplay.DisplayAnnotations(markerAnnotations);

        // Add listeners to the buttons
        foreach (var button in saveButtons)
        {
            button.onClick.AddListener(annotationManager.SaveAnnotations);
        }

        foreach (var button in loadButtons)
        {
            button.onClick.AddListener(annotationManager.LoadAnnotations);
        }
        
        // Subscribe MarkerAnnotation instances to the frame change event.
        // This will update the internal frame counter in MarkerAnnotation and
        // handle showing/hiding markers for the correct frame.
        foreach (var markerAnnotation in markerAnnotations)
        {
            meshController.OnFrameChanged += markerAnnotation.ActivateMarker;
        }
    }
}
