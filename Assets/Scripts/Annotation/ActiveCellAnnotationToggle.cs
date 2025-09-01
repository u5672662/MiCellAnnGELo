using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Provides a UI hook that finds the first enabled <see cref="MarkerAnnotation"/> in the scene
/// and toggles its annotation mode. Attach to a generic “Enable Annotation” button so it works
/// regardless of which cell prefab is currently active (mesh- or volume-based).
/// </summary>
[RequireComponent(typeof(Button))]
public class ActiveCellAnnotationToggle : MonoBehaviour
{
    [Tooltip("Optional tag to restrict the search to objects with this tag. Leave blank to search the whole scene.")]
    [SerializeField] private string cellTag = "";

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(OnButtonClicked);
    }

    private void OnDestroy()
    {
        if (_button != null)
        {
            _button.onClick.RemoveListener(OnButtonClicked);
        }
    }

    private void OnButtonClicked()
    {
        MarkerAnnotation target = null;

        if (!string.IsNullOrEmpty(cellTag))
        {
            var taggedObjects = GameObject.FindGameObjectsWithTag(cellTag);
            foreach (var go in taggedObjects)
            {
                // Skip disabled GameObjects; they can't be interacted with
                if (!go.activeInHierarchy) continue;
                if (go.TryGetComponent(out MarkerAnnotation ma) && ma.enabled)
                {
                    target = ma;
                    break;
                }
            }
        }
        else
        {
            // Fallback: search every MarkerAnnotation in the scene
#if UNITY_2023_1_OR_NEWER || UNITY_2022_2_OR_NEWER
            foreach (var ma in FindObjectsByType<MarkerAnnotation>(FindObjectsSortMode.None))
#else
            foreach (var ma in FindObjectsOfType<MarkerAnnotation>())
#endif
            {
                if (ma.gameObject.activeInHierarchy && ma.enabled)
                {
                    target = ma;
                    break;
                }
            }
        }

        if (target != null)
        {
            target.ToggleAnnotationMode();
        }
        else
        {
            Debug.LogWarning("[ActiveCellAnnotationToggle] No active MarkerAnnotation found in the scene.");
        }
    }
}