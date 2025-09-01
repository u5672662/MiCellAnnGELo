using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds a pair of min/max sliders to a networked range with display text.
/// Server is authoritative; clients request updates via RPC.
/// </summary>
public class SliderControl : NetworkBehaviour
{
    // INSPECTOR REFERENCES
    [SerializeField]
    [Tooltip("Slider for the minimum value. Auto-detected if not set.")]
    private Slider minSlider;

    [SerializeField]
    [Tooltip("Slider for the maximum value. Auto-detected if not set.")]
    private Slider maxSlider;
    
    [SerializeField]
    [Tooltip("Text to display the range. Auto-detected if not set.")]
    private TMP_Text rangeText;

    // CONFIGURATION
    [Tooltip("Display values as a percentage (e.g., 50%) instead of raw numbers.")]
    public bool showAsPercentage = false;
    [Tooltip("Number of decimal places for the display.")]
    [SerializeField] private int decimalPlaces = 0;

    // NETWORK STATE
    private readonly NetworkVariable<float> networkMinValue = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> networkMaxValue = new(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // INTERNAL STATE
    private bool isReady = false;
    private bool isUpdatingFromNetwork = false;

    private void Awake()
    {
        // --- Component Auto-Detection ---
        if (minSlider == null || maxSlider == null)
        {
            var sliders = GetComponentsInChildren<Slider>();
            if (sliders.Length >= 2)
            {
                // A common pattern is to have sliders named "Min" and "Max"
                foreach (var s in sliders)
                {
                    if (s.name.ToLower().Contains("min")) minSlider = s;
                    if (s.name.ToLower().Contains("max")) maxSlider = s;
                }
                // Fallback if naming is not clear
                if (minSlider == null || maxSlider == null)
                {
                    minSlider = sliders[0];
                    maxSlider = sliders[1];
                }
            }
        }
        if (rangeText == null)
        {
            rangeText = GetComponentInChildren<TMP_Text>();
        }

        if (minSlider == null || maxSlider == null)
        {
            Debug.LogError($"SliderControl on {gameObject.name} could not find two Slider components.", this);
            enabled = false;
            return;
        }
    }

    private void Start()
    {
        // Add listeners after Awake has found components
        minSlider.onValueChanged.AddListener(OnMinValueChanged);
        maxSlider.onValueChanged.AddListener(OnMaxValueChanged);

        // Set initial text
        UpdateText();
        
        // Mark as ready to prevent OnValueChanged calls during setup
        isReady = true;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Server is the authority and sets the initial values from its own sliders.
        if (IsServer)
        {
            networkMinValue.Value = minSlider.value;
            networkMaxValue.Value = maxSlider.value;
        }

        // Both server and client subscribe to changes.
        networkMinValue.OnValueChanged += OnNetworkMinValueChanged;
        networkMaxValue.OnValueChanged += OnNetworkMaxValueChanged;

        // Immediately update visuals with the latest network state.
        OnNetworkMinValueChanged(0, networkMinValue.Value);
        OnNetworkMaxValueChanged(0, networkMaxValue.Value);
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        networkMinValue.OnValueChanged -= OnNetworkMinValueChanged;
        networkMaxValue.OnValueChanged -= OnNetworkMaxValueChanged;
    }

    // --- Local UI Input Handlers ---

    private void OnMinValueChanged(float value)
    {
        if (!isReady || isUpdatingFromNetwork) return;

        MinThrottle(); // Ensures min <= max

        if (IsServer)
        {
            networkMinValue.Value = minSlider.value;
        }
        else // A client can request a change
        {
            UpdateMinServerRpc(minSlider.value);
        }
    }

    private void OnMaxValueChanged(float value)
    {
        if (!isReady || isUpdatingFromNetwork) return;

        MaxThrottle(); // Ensures max >= min

        if (IsServer)
        {
            networkMaxValue.Value = maxSlider.value;
        }
        else
        {
            UpdateMaxServerRpc(maxSlider.value);
        }
    }

    // --- Network Update Handlers ---

    private void OnNetworkMinValueChanged(float previousValue, float newValue)
    {
        if (minSlider == null) return;
        isUpdatingFromNetwork = true;
        minSlider.value = newValue;
        MinThrottle(); // Re-validate constraints after network update
        isUpdatingFromNetwork = false;
    }

    private void OnNetworkMaxValueChanged(float previousValue, float newValue)
    {
        if (maxSlider == null) return;
        isUpdatingFromNetwork = true;
        maxSlider.value = newValue;
        MaxThrottle(); // Re-validate constraints after network update
        isUpdatingFromNetwork = false;
    }

    // --- RPCs to Server ---

    [ServerRpc(RequireOwnership = false)]
    private void UpdateMinServerRpc(float value)
    {
        // Optional: Add server-side validation here
        networkMinValue.Value = value;
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdateMaxServerRpc(float value)
    {
        // Optional: Add server-side validation here
        networkMaxValue.Value = value;
    }

    // --- UI Logic ---

    /// <summary>
    /// Forces the range text to refresh based on current slider values.
    /// Useful when slider values are changed via SetValueWithoutNotify.
    /// </summary>
    public void RefreshText()
    {
        UpdateText();
    }

    public void MinThrottle()
    {
        if (minSlider.value > maxSlider.value)
        {
            minSlider.value = maxSlider.value;
        }
        UpdateText();
    }

    public void MaxThrottle()
    {
        if (maxSlider.value < minSlider.value)
        {
            maxSlider.value = minSlider.value;
        }
        UpdateText();
    }

    private void UpdateText()
    {
        if (rangeText == null) return;

        if (showAsPercentage)
        {
            float pctMin = Mathf.InverseLerp(minSlider.minValue, minSlider.maxValue, minSlider.value) * 100f;
            float pctMax = Mathf.InverseLerp(maxSlider.minValue, maxSlider.maxValue, maxSlider.value) * 100f;
            string fmt = decimalPlaces > 0 ? $"F{decimalPlaces}" : "F0";
            rangeText.text = $"{pctMin.ToString(fmt)}% - {pctMax.ToString(fmt)}%";
        }
        else
        {
            string fmt = "F0"; // Assuming raw values are integers for display
            rangeText.text = $"{minSlider.value.ToString(fmt)} - {maxSlider.value.ToString(fmt)}";
        }
    }
}