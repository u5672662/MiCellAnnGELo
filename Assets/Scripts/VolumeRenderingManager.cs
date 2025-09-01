using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityVolumeRendering;
using System.Collections.Generic;

/// <summary>
/// Manages material properties, transfer functions, and network synchronisation for the volume renderer.
/// Behaviour is purely configurational; rendering is driven entirely by the assigned shader/material.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class VolumeRenderingManager : NetworkBehaviour
{
    private static readonly int StepCount = Shader.PropertyToID("_StepCount");
    private static readonly int GradientTexture = Shader.PropertyToID("_GradientTexture");
    private static readonly int Threshold = Shader.PropertyToID("_Threshold");
    private static readonly int Intensity = Shader.PropertyToID("_Intensity");
    private static readonly int VolumeTexture = Shader.PropertyToID("_VolumeTexture");
    
    [Header("References")]
    public Material volumeMaterial;
    
    [Header("Transfer Function Mode")]
    private object dualChannelTFManager;
    private TiffTimeSeriesLoader tiffLoader;
    
    [Header("UI Controls")]
    public Slider intensitySlider;
    public Slider thresholdSlider;
    public Slider sliceMinSlider;
    public Slider sliceMaxSlider;
    public TMP_Dropdown stepCountSlider;

    [Header("Optional Secondary UI Controls (e.g., wrist UI)")]
    public Slider intensitySliderSecondary;
    public Slider thresholdSliderSecondary;
    public Slider sliceMinSliderSecondary;
    public Slider sliceMaxSliderSecondary;
    
    [Header("Rendering Settings")]
    [Tooltip("Number of ray marching steps")]
    public int stepCount = 128;

    [Tooltip("Volume intensity multiplier")] public float defaultIntensity;
    [Tooltip("Volume visibility threshold")] public float defaultThreshold;
    [Tooltip("Minimum slice along Z")] public float defaultSliceMin;
    [Tooltip("Maximum slice along Z")] public float defaultSliceMax;
    
    private Texture2D _gradientTexture;
    private MeshRenderer _meshRenderer;
    private BoxCollider _collider;

    private readonly NetworkVariable<bool> _visible = new(false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Network variables to sync slider values across all clients
    private readonly NetworkVariable<float> _networkIntensity = new(1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    private readonly NetworkVariable<float> _networkThreshold = new(0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    private readonly NetworkVariable<float> _networkSliceMin = new(0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    private readonly NetworkVariable<float> _networkSliceMax = new(1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private bool _isUpdatingFromNetwork = false;

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer != null)
        {
            _meshRenderer.enabled = false;
        }
        if (!TryGetComponent(out _collider))
        {
            _collider = gameObject.AddComponent<BoxCollider>();
            _collider.size = Vector3.one;
            _collider.center = Vector3.zero;
        }
        // Keep collider as non-trigger for XR grabbing
        _collider.isTrigger = false;

        // Try to use PlayerPassthroughHandler via reflection if present
        var pphType = System.Type.GetType("PlayerPassthroughHandler");
        if (pphType != null)
        {
            var pph = GetComponent(pphType) ?? gameObject.AddComponent(pphType);
            pphType.GetMethod("RefreshIgnores")?.Invoke(pph, null);
        }
        
        // Get references to other components
        // Try to find DualChannelTransferFunctionManager without compile-time dependency
        var tfTypeLocal = System.Type.GetType("DualChannelTransferFunctionManager");
        if (tfTypeLocal != null)
        {
            dualChannelTFManager = GetComponent(tfTypeLocal);
        }
        tiffLoader = FindFirstObjectByType<TiffTimeSeriesLoader>();
    }

    private void OnVisibleChanged(bool previous, bool next)
    {
        ApplyVisibility(next);
    }

    private void OnNetworkIntensityChanged(float previous, float next)
    {
        if (!IsServer && !_isUpdatingFromNetwork)
        {
            _isUpdatingFromNetwork = true;
            if (intensitySlider != null) intensitySlider.SetValueWithoutNotify(next);
            if (intensitySliderSecondary != null) intensitySliderSecondary.SetValueWithoutNotify(next);
            UpdateIntensity(next);
            RefreshAllSliderLabelsAndRanges();
            _isUpdatingFromNetwork = false;
        }
    }

    private void OnNetworkThresholdChanged(float previous, float next)
    {
        if (!IsServer && !_isUpdatingFromNetwork)
        {
            _isUpdatingFromNetwork = true;
            if (thresholdSlider != null) thresholdSlider.SetValueWithoutNotify(next);
            if (thresholdSliderSecondary != null) thresholdSliderSecondary.SetValueWithoutNotify(next);
            UpdateThreshold(next);
            RefreshAllSliderLabelsAndRanges();
            _isUpdatingFromNetwork = false;
        }
    }

    private void OnNetworkSliceMinChanged(float previous, float next)
    {
        if (!IsServer && !_isUpdatingFromNetwork)
        {
            _isUpdatingFromNetwork = true;
            if (sliceMinSlider != null) sliceMinSlider.SetValueWithoutNotify(next);
            if (sliceMinSliderSecondary != null) sliceMinSliderSecondary.SetValueWithoutNotify(next);
            UpdateSliceMin(next);
            RefreshAllSliderLabelsAndRanges();
            _isUpdatingFromNetwork = false;
        }
    }

    private void OnNetworkSliceMaxChanged(float previous, float next)
    {
        if (!IsServer && !_isUpdatingFromNetwork)
        {
            _isUpdatingFromNetwork = true;
            if (sliceMaxSlider != null) sliceMaxSlider.SetValueWithoutNotify(next);
            if (sliceMaxSliderSecondary != null) sliceMaxSliderSecondary.SetValueWithoutNotify(next);
            UpdateSliceMax(next);
            RefreshAllSliderLabelsAndRanges();
            _isUpdatingFromNetwork = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _visible.OnValueChanged += OnVisibleChanged;
        _networkIntensity.OnValueChanged += OnNetworkIntensityChanged;
        _networkThreshold.OnValueChanged += OnNetworkThresholdChanged;
        _networkSliceMin.OnValueChanged += OnNetworkSliceMinChanged;
        _networkSliceMax.OnValueChanged += OnNetworkSliceMaxChanged;
        
        ApplyVisibility(_visible.Value);
        
        // If we're not the server, sync with the network values
        if (!IsServer)
        {
            _isUpdatingFromNetwork = true;
            if (intensitySlider != null) intensitySlider.value = _networkIntensity.Value;
            if (thresholdSlider != null) thresholdSlider.value = _networkThreshold.Value;
            if (sliceMinSlider != null) sliceMinSlider.value = _networkSliceMin.Value;
            if (sliceMaxSlider != null) sliceMaxSlider.value = _networkSliceMax.Value;
            if (intensitySliderSecondary != null) intensitySliderSecondary.SetValueWithoutNotify(_networkIntensity.Value);
            if (thresholdSliderSecondary != null) thresholdSliderSecondary.SetValueWithoutNotify(_networkThreshold.Value);
            if (sliceMinSliderSecondary != null) sliceMinSliderSecondary.SetValueWithoutNotify(_networkSliceMin.Value);
            if (sliceMaxSliderSecondary != null) sliceMaxSliderSecondary.SetValueWithoutNotify(_networkSliceMax.Value);
            _isUpdatingFromNetwork = false;
            
            // Apply the values to the material
            UpdateIntensity(_networkIntensity.Value);
            UpdateThreshold(_networkThreshold.Value);
            UpdateSliceMin(_networkSliceMin.Value);
            UpdateSliceMax(_networkSliceMax.Value);
        }
        else
        {
            // If we're the server, set the initial network values
            if (intensitySlider != null) _networkIntensity.Value = intensitySlider.value;
            if (thresholdSlider != null) _networkThreshold.Value = thresholdSlider.value;
            if (sliceMinSlider != null) _networkSliceMin.Value = sliceMinSlider.value;
            if (sliceMaxSlider != null) _networkSliceMax.Value = sliceMaxSlider.value;
        }
    }

    public override void OnNetworkDespawn()
    {
        _visible.OnValueChanged -= OnVisibleChanged;
        _networkIntensity.OnValueChanged -= OnNetworkIntensityChanged;
        _networkThreshold.OnValueChanged -= OnNetworkThresholdChanged;
        _networkSliceMin.OnValueChanged -= OnNetworkSliceMinChanged;
        _networkSliceMax.OnValueChanged -= OnNetworkSliceMaxChanged;
        base.OnNetworkDespawn();
    }

    public void SetVisible(bool visible)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            if (IsServer)
            {
                _visible.Value = visible;
            }
            else
            {
                SetVisibleServerRpc(visible);
            }
        }
        else
        {
            ApplyVisibility(visible);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetVisibleServerRpc(bool visible)
    {
        if (_visible.Value != visible)
            _visible.Value = visible;
    }

    private void ApplyVisibility(bool visible)
    {
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer != null)
            _meshRenderer.enabled = visible;
    }

    private void OnEnable()
    {
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer != null && volumeMaterial != null)
            _meshRenderer.material = volumeMaterial;
    }
    
    void Start()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        // Ensure a mesh filter with a cube mesh so that the volume can be rendered
        var mf = GetComponent<MeshFilter>();
        if (mf == null)
            mf = gameObject.AddComponent<MeshFilter>();
        if (mf.sharedMesh == null)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mf.sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cube);
        }

        if (_meshRenderer != null && volumeMaterial != null)
        {
            _meshRenderer.material = volumeMaterial;
            
            // Ensure Direct Volume Rendering mode is enabled
            SetVolumeRenderingMode();
            
                    // Create and assign essential textures
        CreateNoiseTexture();
        CreateDefaultSecondaryTextures();
        }
        
        // Initialize UI controls
        if (intensitySlider != null)
            intensitySlider.onValueChanged.AddListener(UpdateIntensity);
        if (thresholdSlider != null)
            thresholdSlider.onValueChanged.AddListener(UpdateThreshold);
        if (sliceMinSlider != null)
            sliceMinSlider.onValueChanged.AddListener(UpdateSliceMin);
        if (sliceMaxSlider != null)
            sliceMaxSlider.onValueChanged.AddListener(UpdateSliceMax);
        if (stepCountSlider != null)
        {
            stepCountSlider.onValueChanged.AddListener(UpdateStepCount);
            SetStepCountSliderValue(stepCount);
        }
        if (intensitySliderSecondary != null)
            intensitySliderSecondary.onValueChanged.AddListener(UpdateIntensity);
        if (thresholdSliderSecondary != null)
            thresholdSliderSecondary.onValueChanged.AddListener(UpdateThreshold);
        if (sliceMinSliderSecondary != null)
            sliceMinSliderSecondary.onValueChanged.AddListener(UpdateSliceMin);
        if (sliceMaxSliderSecondary != null)
            sliceMaxSliderSecondary.onValueChanged.AddListener(UpdateSliceMax);

        // Apply default values to sliders if they exist
        if (intensitySlider != null)
            intensitySlider.value = defaultIntensity;
        if (thresholdSlider != null)
            thresholdSlider.value = defaultThreshold;
        if (sliceMinSlider != null)
            sliceMinSlider.value = defaultSliceMin;
        if (sliceMaxSlider != null)
            sliceMaxSlider.value = defaultSliceMax;
        if (intensitySliderSecondary != null)
            intensitySliderSecondary.SetValueWithoutNotify(intensitySlider != null ? intensitySlider.value : defaultIntensity);
        if (thresholdSliderSecondary != null)
            thresholdSliderSecondary.SetValueWithoutNotify(thresholdSlider != null ? thresholdSlider.value : defaultThreshold);
        if (sliceMinSliderSecondary != null)
            sliceMinSliderSecondary.SetValueWithoutNotify(sliceMinSlider != null ? sliceMinSlider.value : defaultSliceMin);
        if (sliceMaxSliderSecondary != null)
            sliceMaxSliderSecondary.SetValueWithoutNotify(sliceMaxSlider != null ? sliceMaxSlider.value : defaultSliceMax);

        if (volumeMaterial != null)
        {
            volumeMaterial.SetInt(StepCount, stepCount);
            Debug.Log($"[VolumeRenderingManager] Set initial step count to {stepCount}");
        }

        // Set initial material properties from sliders
        UpdateIntensity(intensitySlider != null ? intensitySlider.value : defaultIntensity);
        UpdateThreshold(thresholdSlider != null ? thresholdSlider.value : defaultThreshold);
        UpdateSliceMin(sliceMinSlider != null ? sliceMinSlider.value : defaultSliceMin);
        UpdateSliceMax(sliceMaxSlider != null ? sliceMaxSlider.value : defaultSliceMax);
        
        // Create default gradient texture if needed
        if (ShouldUseDualChannelMode() && dualChannelTFManager != null)
        {
            var tfType = dualChannelTFManager.GetType();
            tfType.GetField("volumeMaterial")?.SetValue(dualChannelTFManager, volumeMaterial);
        }
        else
        {
            CreateDefaultGradientTexture();
        }
    }
    
    private void CreateDefaultGradientTexture()
    {
        // Create a simple grayscale to color gradient texture
        _gradientTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp
        };

        var colors = new Color[256];
        for (var i = 0; i < 256; i++)
        {
            var t = i / 255f;
            
            // Create a simple color ramp (customize as needed)
            var color = Color.Lerp(Color.black, Color.white, t);
            
            // Add some color variation
            if (t > 0.5f)
                color = Color.Lerp(Color.white, new Color(1f, 0.8f, 0.2f), (t - 0.5f) * 2f);
            
            // CRITICAL FIX: Set proper alpha values for volume visibility
            // Create a smooth opacity curve that shows both interior and boundaries
            float alpha;
            if (t < 0.1f)
            {
                alpha = 0.0f; // Completely transparent for very low densities (background)
            }
            else if (t < 0.3f)
            {
                // Gradual fade-in for low density areas (interior)
                alpha = Mathf.Lerp(0.0f, 0.3f, (t - 0.1f) / 0.2f);
            }
            else
            {
                // Higher opacity for medium to high densities (walls and structures)
                alpha = Mathf.Lerp(0.3f, 1.0f, (t - 0.3f) / 0.7f);
            }
            
            color.a = alpha;
            colors[i] = color;
        }
        
        _gradientTexture.SetPixels(colors);
        _gradientTexture.Apply();
        
        // Apply to material if available
        if (volumeMaterial != null)
        {
            volumeMaterial.SetTexture(GradientTexture, _gradientTexture);
            Debug.Log($"[VolumeRenderingManager] Applied single-channel transfer function texture: {_gradientTexture.name} ({_gradientTexture.width}x{_gradientTexture.height})");
        }
        else
        {
            Debug.LogError("[VolumeRenderingManager] Cannot apply transfer function - volumeMaterial is null");
        }
    }
    
    // UI Callbacks
    private void UpdateIntensity(float value)
    {
        if (volumeMaterial != null)
            volumeMaterial.SetFloat(Intensity, value);
        // Mirror across both slider sets without triggering callbacks
        if (!_isUpdatingFromNetwork)
        {
            if (intensitySlider != null) intensitySlider.SetValueWithoutNotify(value);
            if (intensitySliderSecondary != null) intensitySliderSecondary.SetValueWithoutNotify(value);
        }
        RefreshAllSliderLabelsAndRanges();
            
        // Sync to network if we're connected and not updating from network
        if (!_isUpdatingFromNetwork && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            if (IsServer)
            {
                _networkIntensity.Value = value;
            }
            else
            {
                UpdateIntensityServerRpc(value);
            }
        }
    }
    
    private void UpdateThreshold(float value)
    {
        if (volumeMaterial != null)
            volumeMaterial.SetFloat(Threshold, value);
        // Mirror across both slider sets without triggering callbacks
        if (!_isUpdatingFromNetwork)
        {
            if (thresholdSlider != null) thresholdSlider.SetValueWithoutNotify(value);
            if (thresholdSliderSecondary != null) thresholdSliderSecondary.SetValueWithoutNotify(value);
        }
        RefreshAllSliderLabelsAndRanges();
            
        // Sync to network if we're connected and not updating from network
        if (!_isUpdatingFromNetwork && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            if (IsServer)
            {
                _networkThreshold.Value = value;
            }
            else
            {
                UpdateThresholdServerRpc(value);
            }
        }
    }
    
    private void UpdateSliceMin(float value)
    {
        if (volumeMaterial != null)
        {
            volumeMaterial.SetFloat("_SliceMin", value);
            // Ensure min doesn't exceed max
            if (sliceMaxSlider != null && value > sliceMaxSlider.value)
                sliceMaxSlider.SetValueWithoutNotify(value);
            if (sliceMaxSliderSecondary != null && value > sliceMaxSliderSecondary.value)
                sliceMaxSliderSecondary.SetValueWithoutNotify(value);
        }
        // Mirror across both slider sets without triggering callbacks
        if (!_isUpdatingFromNetwork)
        {
            if (sliceMinSlider != null) sliceMinSlider.SetValueWithoutNotify(value);
            if (sliceMinSliderSecondary != null) sliceMinSliderSecondary.SetValueWithoutNotify(value);
        }
        RefreshAllSliderLabelsAndRanges();
            
        // Sync to network if we're connected and not updating from network
        if (!_isUpdatingFromNetwork && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            if (IsServer)
            {
                _networkSliceMin.Value = value;
            }
            else
            {
                UpdateSliceMinServerRpc(value);
            }
        }
    }
    
    private void UpdateSliceMax(float value)
    {
        if (volumeMaterial != null)
        {
            volumeMaterial.SetFloat("_SliceMax", value);
            // Ensure max doesn't go below min
            if (sliceMinSlider != null && value < sliceMinSlider.value)
                sliceMinSlider.SetValueWithoutNotify(value);
            if (sliceMinSliderSecondary != null && value < sliceMinSliderSecondary.value)
                sliceMinSliderSecondary.SetValueWithoutNotify(value);
        }
        // Mirror across both slider sets without triggering callbacks
        if (!_isUpdatingFromNetwork)
        {
            if (sliceMaxSlider != null) sliceMaxSlider.SetValueWithoutNotify(value);
            if (sliceMaxSliderSecondary != null) sliceMaxSliderSecondary.SetValueWithoutNotify(value);
        }
        RefreshAllSliderLabelsAndRanges();
            
        // Sync to network if we're connected and not updating from network
        if (!_isUpdatingFromNetwork && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            if (IsServer)
            {
                _networkSliceMax.Value = value;
            }
            else
            {
                UpdateSliceMaxServerRpc(value);
            }
        }
    }
    
    public void UpdateStepCount(int value)
    {
        if (stepCountSlider != null && value < stepCountSlider.options.Count)
        {
            if (!int.TryParse(stepCountSlider.options[value].text, out stepCount))
            {
                stepCount = value;
            }
        }
        else
        {
            stepCount = value;
        }
        if (volumeMaterial != null)
        {
            volumeMaterial.SetInt(StepCount, stepCount);
        }
    }
    
    /// <summary>
    /// Sets the step count slider to the correct index for the given step count value
    /// </summary>
    private void SetStepCountSliderValue(int targetStepCount)
    {
        if (stepCountSlider == null) return;
        
        // Find the dropdown option that matches our target step count
        for (int i = 0; i < stepCountSlider.options.Count; i++)
        {
            if (int.TryParse(stepCountSlider.options[i].text, out int optionValue))
            {
                if (optionValue == targetStepCount)
                {
                    stepCountSlider.value = i;
                    Debug.Log($"[VolumeRenderingManager] Set step count slider to index {i} (value: {targetStepCount})");
                    return;
                }
            }
        }
        
        // If no exact match found, default to first option and log warning
        if (stepCountSlider.options.Count > 0)
        {
            stepCountSlider.value = 0;
            if (int.TryParse(stepCountSlider.options[0].text, out int firstValue))
            {
                stepCount = firstValue;
                Debug.LogWarning($"[VolumeRenderingManager] Target step count {targetStepCount} not found in dropdown. Defaulting to {firstValue}");
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdateIntensityServerRpc(float value)
    {
        _networkIntensity.Value = value;
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdateThresholdServerRpc(float value)
    {
        _networkThreshold.Value = value;
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdateSliceMinServerRpc(float value)
    {
        _networkSliceMin.Value = value;
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdateSliceMaxServerRpc(float value)
    {
        _networkSliceMax.Value = value;
    }
    
    // Dual Channel Transfer Function Methods
    public bool ShouldUseDualChannelMode()
    {
        // Use dual channel mode when combine channels is disabled (unchecked)
        return tiffLoader != null && !tiffLoader.combineChannels;
    }
    
    public void EnableDualChannelTransferFunctions()
    {
        var tfType = System.Type.GetType("DualChannelTransferFunctionManager");
        if (tfType == null) return;
        if (dualChannelTFManager == null)
        {
            dualChannelTFManager = gameObject.AddComponent(tfType);
            tfType.GetField("volumeMaterial")?.SetValue(dualChannelTFManager, volumeMaterial);
            tfType.GetMethod("Initialize")?.Invoke(dualChannelTFManager, null);
        }
        tfType.GetMethod("ApplyTransferFunctionsToMaterial")?.Invoke(dualChannelTFManager, null);
    }
    
    public void DisableDualChannelTransferFunctions()
    {
        if (dualChannelTFManager != null)
        {
            var tfType = dualChannelTFManager.GetType();
            tfType.GetMethod("DisableDualChannelMode")?.Invoke(dualChannelTFManager, null);
        }
        CreateDefaultGradientTexture();
    }
    
    public object GetDualChannelManager() { return dualChannelTFManager; }
    
    public void OnCombineChannelsChanged()
    {
        // Called when the combine channels toggle changes
        if (ShouldUseDualChannelMode())
        {
            EnableDualChannelTransferFunctions();
        }
        else
        {
            DisableDualChannelTransferFunctions();
        }
    }
    
    public void ImportVolumeDataset(VolumeDataset dataset)
    {
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();
            
        if (dataset == null)
        {
            Debug.LogError("Cannot import null dataset");
            return;
        }

        // Use VolumeDataset's built-in texture creation which properly handles 
        // different bit depths by calculating actual min/max values
        Texture3D dataTexture = dataset.GetDataTexture();

        // Ensure gradient texture is generated so we know max gradient
        Texture3D gradientTexture3D = dataset.GetGradientTexture();
        float gradMax = dataset.gradientMax;
        if (gradMax <= 0.0f) gradMax = 1.75f;

        // Assign 3D gradient texture (for lighting/normals)
        volumeMaterial.SetTexture("_GradientTex", gradientTexture3D);
        volumeMaterial.SetFloat("_GradMax", gradMax);
        
        // Assign 2D transfer function texture
        bool useDualChannel = ShouldUseDualChannelMode();
        Debug.Log($"[VolumeRenderingManager] Transfer function mode - Dual channel: {useDualChannel}, TiffLoader: {tiffLoader != null}, CombineChannels: {(tiffLoader != null ? tiffLoader.combineChannels : "N/A")}");
        
        if (useDualChannel && dualChannelTFManager != null)
        {
            Debug.Log("[VolumeRenderingManager] Using dual channel transfer functions");
            var tfType = dualChannelTFManager.GetType();
            tfType.GetField("volumeMaterial")?.SetValue(dualChannelTFManager, volumeMaterial);
            tfType.GetMethod("ApplyTransferFunctionsToMaterial")?.Invoke(dualChannelTFManager, null);
        }
        else if (useDualChannel && dualChannelTFManager == null)
        {
            Debug.Log("[VolumeRenderingManager] Dual channel mode requested but manager is null, creating it");
            EnableDualChannelTransferFunctions();
        }
        else
        {
            Debug.Log("[VolumeRenderingManager] Using single channel transfer function");
            CreateDefaultGradientTexture();
        }
        
        if (dataTexture == null)
        {
            Debug.LogError("Failed to create data texture from dataset");
            return;
        }

        volumeMaterial.SetTexture(VolumeTexture, dataTexture);
        
        // Ensure Direct Volume Rendering mode is enabled
        SetVolumeRenderingMode();
        
        // Re-assign the material to the renderer
        if (_meshRenderer != null)
            _meshRenderer.material = volumeMaterial;
            
        SetVisible(true);
    }
    
    /// <summary>
    /// Ensures the volume material is set to Direct Volume Rendering mode
    /// </summary>
    private void SetVolumeRenderingMode()
    {
        if (volumeMaterial == null) return;
        
        // Enable Direct Volume Rendering mode
        volumeMaterial.EnableKeyword("MODE_DVR");
        
        // Disable other rendering modes to ensure DVR is active
        volumeMaterial.DisableKeyword("MODE_MIP");
        volumeMaterial.DisableKeyword("MODE_SURF");
        
        Debug.Log("[VolumeRenderingManager] Set to Direct Volume Rendering mode");
    }
    
    /// <summary>
    /// Creates and assigns the noise texture used for ray jittering to reduce banding artifacts
    /// </summary>
    private void CreateNoiseTexture()
    {
        if (volumeMaterial == null) return;
        
        // Create a small random noise texture
        const int noiseSize = 64;
        Texture2D noiseTexture = new Texture2D(noiseSize, noiseSize, TextureFormat.R8, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point
        };
        
        // Fill with random values
        Color[] noisePixels = new Color[noiseSize * noiseSize];
        for (int i = 0; i < noisePixels.Length; i++)
        {
            float randomValue = UnityEngine.Random.Range(0f, 1f);
            noisePixels[i] = new Color(randomValue, randomValue, randomValue, 1f);
        }
        
        noiseTexture.SetPixels(noisePixels);
        noiseTexture.Apply();
        
        // Assign to material
        volumeMaterial.SetTexture("_NoiseTex", noiseTexture);
        
        Debug.Log("[VolumeRenderingManager] Created and assigned noise texture");
    }
    
    /// <summary>
    /// Creates default textures for optional secondary volume features
    /// </summary>
    private void CreateDefaultSecondaryTextures()
    {
        if (volumeMaterial == null) return;
        
        // Create a 1x1x1 black secondary data texture (placeholder)
        Texture3D emptySecondaryData = new Texture3D(1, 1, 1, TextureFormat.R8, false);
        emptySecondaryData.SetPixels(new Color[] { Color.black });
        emptySecondaryData.Apply();
        volumeMaterial.SetTexture("_SecondaryDataTex", emptySecondaryData);
        
        // Create a 1x1 black secondary transfer function texture (placeholder)
        Texture2D emptySecondaryTF = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        emptySecondaryTF.SetPixel(0, 0, Color.clear); // Transparent
        emptySecondaryTF.Apply();
        volumeMaterial.SetTexture("_SecondaryTFTex", emptySecondaryTF);
        
        Debug.Log("[VolumeRenderingManager] Created default secondary textures");
    }
    


    void Update()
    {
        // Monitor the combine channels state and react to changes
        if (tiffLoader != null)
        {
            bool shouldUseDual = ShouldUseDualChannelMode();
            bool currentlyUsingDual = dualChannelTFManager != null &&
                                      volumeMaterial != null &&
                                      volumeMaterial.IsKeywordEnabled("DUAL_CHANNEL_TF_ON");
            
            if (shouldUseDual && !currentlyUsingDual)
            {
                EnableDualChannelTransferFunctions();
            }
            else if (!shouldUseDual && currentlyUsingDual)
            {
                DisableDualChannelTransferFunctions();
            }
        }
    }

    private void RefreshAllSliderLabelsAndRanges()
    {
        try
        {
            var labels = FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var comp in labels)
            {
                if (comp != null && comp.GetType().Name == "SliderLabel")
                {
                    comp.SendMessage("RefreshLabel", SendMessageOptions.DontRequireReceiver);
                }
            }
        }
        catch (System.Exception) { }

        try
        {
            var rangeCtrls = FindObjectsByType<SliderControl>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var ctrl in rangeCtrls)
            {
                if (ctrl != null) ctrl.RefreshText();
            }
        }
        catch (System.Exception) { }
    }
}