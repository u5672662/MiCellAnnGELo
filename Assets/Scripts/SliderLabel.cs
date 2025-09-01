using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class SliderLabel : NetworkBehaviour
{
    // INSPECTOR REFERENCES
    [Tooltip("The UI Slider this script controls. Auto-detected if not set.")]
    [SerializeField] private Slider slider;
    [Tooltip("The TMP_Text that shows the current value. Auto-detected if not set.")]
    [SerializeField] private TMP_Text valueText;

    // CONFIGURATION
    [Tooltip("The minimum value that maps to 0% (only used if showRawValue is false).")]
    [SerializeField] private float minValue = 0f;
    [Tooltip("The maximum value that maps to 100% (only used if showRawValue is false).")]
    [SerializeField] private float maxValue = 1f;
    [Tooltip("Number of decimal places shown in the label.")]
    [SerializeField] private int decimalPlaces = 1;
    [Tooltip("If true, display the raw slider value. If false, show a percentage.")]
    [SerializeField] private bool showRawValue = false;

    // NETWORK STATE
    private readonly NetworkVariable<float> networkValue = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // INTERNAL STATE
    private bool isReady = false;
    private bool isUpdatingFromNetwork = false;

    private void Awake()
    {
        // --- Component Auto-Detection ---
        if (slider == null)
        {
            slider = GetComponentInChildren<Slider>();
        }
        if (valueText == null)
        {
            var texts = GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length == 1)
            {
                valueText = texts[0];
            }
            else if (texts.Length > 1)
            {
                // Heuristic: Prefer a text component named "Value" or similar
                foreach (var t in texts)
                {
                    if (t.name.ToLower().Contains("value"))
                    {
                        valueText = t;
                        break;
                    }
                }
                // Fallback: Use the last one found
                if (valueText == null) valueText = texts[texts.Length - 1];
            }
        }

        if (slider == null)
        {
            Debug.LogError($"SliderLabel on {gameObject.name} could not find a Slider component.", this);
            enabled = false;
            return;
        }

        // Auto-configure display mode based on slider's range if not explicitly set.
        // Sliders with a large range (like 0-255) usually want raw values.
        if (slider.maxValue > 1.05f)
        {
            showRawValue = true;
        }
    }

    private void Start()
    {
        slider.onValueChanged.AddListener(OnSliderValueChanged);
        UpdateLabel(slider.value);
        isReady = true;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            networkValue.Value = slider.value;
        }
        networkValue.OnValueChanged += OnNetworkValueChanged;
        OnNetworkValueChanged(0, networkValue.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        networkValue.OnValueChanged -= OnNetworkValueChanged;
    }

    private void OnSliderValueChanged(float value)
    {
        if (!isReady || isUpdatingFromNetwork) return;

        UpdateLabel(value);

        if (IsServer)
        {
            networkValue.Value = value;
        }
        else // Client sends an update request to the server
        {
            UpdateSliderServerRpc(value);
        }
    }

    private void OnNetworkValueChanged(float previousValue, float newValue)
    {
        isUpdatingFromNetwork = true;
        slider.value = newValue;
        UpdateLabel(newValue);
        isUpdatingFromNetwork = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdateSliderServerRpc(float value)
    {
        networkValue.Value = value;
    }
    
    /// <summary>
    /// Forces the label to refresh based on the current slider value.
    /// Useful when the slider value is changed via SetValueWithoutNotify.
    /// </summary>
    public void RefreshLabel()
    {
        if (slider == null) return;
        UpdateLabel(slider.value);
    }
    
    private void UpdateLabel(float value)
    {
        if (valueText == null) return;

        if (showRawValue)
        {
            string fmt = decimalPlaces > 0 ? $"F{decimalPlaces}" : "F0";
            valueText.text = value.ToString(fmt);
        }
        else
        {
            float displayMin = Mathf.Approximately(minValue, maxValue) ? slider.minValue : minValue;
            float displayMax = Mathf.Approximately(minValue, maxValue) ? slider.maxValue : maxValue;
            
            float normalized = Mathf.InverseLerp(displayMin, displayMax, value);
            float pct = Mathf.Clamp01(normalized) * 100f;
            string pctFmt = decimalPlaces > 0 ? pct.ToString($"F{decimalPlaces}") : ((int)pct).ToString();
            valueText.text = $"{pctFmt}%";
        }
    }
} 