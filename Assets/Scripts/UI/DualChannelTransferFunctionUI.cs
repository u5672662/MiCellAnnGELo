using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// Minimal UI layer to control dual-channel transfer function opacity and presets.
    /// Updates are forwarded to the runtime manager; no rendering logic here.
    /// </summary>
    public class DualChannelTransferFunctionUI : MonoBehaviour
    {
        [Header("UI References")]
        public TMP_Text modeStatusText;
        
        [Header("Red Channel Controls")]
        public Slider redOpacitySlider;
        public TMP_Text redOpacityLabel;
        public Button redColorButton;
        
        [Header("Green Channel Controls")]
        public Slider greenOpacitySlider;
        public TMP_Text greenOpacityLabel;
        public Button greenColorButton;
        
        [Header("Advanced Controls")]
        public Button advancedEditButton;
        public Button saveTransferFunctionButton;
        public Button loadTransferFunctionButton;
        
        // Reference to the volume rendering manager
        [SerializeField]
        private VolumeRenderingManager volumeManager;
        private DualChannelTransferFunctionManager tfManager;
        
        // Current opacity values
        private float redOpacity = 1f;
        private float greenOpacity = 1f;
        
        private void Start()
        {
            // Find the volume rendering manager in the scene
            volumeManager = FindFirstObjectByType<VolumeRenderingManager>();
            if (volumeManager == null)
            {
                Debug.LogWarning("[DualChannelTransferFunctionUI] No VolumeRenderingManager found in scene. UI will be disabled.");
                gameObject.SetActive(false);
                return;
            }
            
            InitializeUI();
            SetupEventHandlers();
            UpdateUI();
        }
        
        private void Update()
        {
            if (volumeManager == null) return;
            
            UpdateUI();
            
            // Get the manager if we are in dual channel mode and don't have it yet
            if (volumeManager.ShouldUseDualChannelMode())
            {
                if (tfManager == null)
                {
                    var mgr = volumeManager.GetDualChannelManager();
                    tfManager = mgr as DualChannelTransferFunctionManager;
                }
            }
            else
            {
                tfManager = null; // Clear manager if not in dual channel mode
            }
        }
        
        private void InitializeUI()
        {
            // Initialize sliders
            if (redOpacitySlider != null)
            {
                redOpacitySlider.minValue = 0f;
                redOpacitySlider.maxValue = 2f;
                redOpacitySlider.value = redOpacity;
            }
            
            if (greenOpacitySlider != null)
            {
                greenOpacitySlider.minValue = 0f;
                greenOpacitySlider.maxValue = 2f;
                greenOpacitySlider.value = greenOpacity;
            }
        }
        
        private void SetupEventHandlers()
        {
            // Opacity sliders
            if (redOpacitySlider != null)
                redOpacitySlider.onValueChanged.AddListener(OnRedOpacityChanged);
            
            if (greenOpacitySlider != null)
                greenOpacitySlider.onValueChanged.AddListener(OnGreenOpacityChanged);
            
            // Advanced controls
            if (advancedEditButton != null)
                advancedEditButton.onClick.AddListener(OpenAdvancedEditor);
            
            if (saveTransferFunctionButton != null)
                saveTransferFunctionButton.onClick.AddListener(SaveTransferFunction);
            
            if (loadTransferFunctionButton != null)
                loadTransferFunctionButton.onClick.AddListener(LoadTransferFunction);
        }
        
        private void UpdateUI()
        {
            if (volumeManager == null) return;
            
            // Update mode status
            bool isDualChannelMode = volumeManager.ShouldUseDualChannelMode();
            
            if (modeStatusText != null)
            {
                modeStatusText.text = isDualChannelMode ? "Dual Channel Mode" : "Single Channel Mode";
            }
            
            // Enable/disable dual channel controls
            SetDualChannelControlsActive(isDualChannelMode);
            
            // Update opacity labels
            UpdateOpacityLabels();
        }
        
        private void SetDualChannelControlsActive(bool active)
        {
            if (redOpacitySlider != null)
                redOpacitySlider.interactable = active;
            if (greenOpacitySlider != null)
                greenOpacitySlider.interactable = active;
            if (redColorButton != null)
                redColorButton.interactable = active;
            if (greenColorButton != null)
                greenColorButton.interactable = active;
            if (advancedEditButton != null)
                advancedEditButton.interactable = active;
            if (saveTransferFunctionButton != null)
                saveTransferFunctionButton.interactable = active;
            if (loadTransferFunctionButton != null)
                loadTransferFunctionButton.interactable = active;
        }
        
        private void UpdateOpacityLabels()
        {
            if (redOpacityLabel != null)
                redOpacityLabel.text = $"Red Opacity: {redOpacity:F2}";
            
            if (greenOpacityLabel != null)
                greenOpacityLabel.text = $"Green Opacity: {greenOpacity:F2}";
        }
        
        // Event Handlers
        
        public void OnRedOpacityChanged(float value)
        {
            redOpacity = value;
            UpdateOpacityLabels();
            
            if (tfManager != null)
            {
                // Create a simple linear curve with the specified maximum opacity
                AnimationCurve opacityCurve = AnimationCurve.Linear(0f, 0f, 1f, value);
                tfManager.UpdateRedChannelOpacity(opacityCurve);
            }
        }
        
        public void OnGreenOpacityChanged(float value)
        {
            greenOpacity = value;
            UpdateOpacityLabels();
            
            if (tfManager != null)
            {
                // Create a simple linear curve with the specified maximum opacity
                AnimationCurve opacityCurve = AnimationCurve.Linear(0f, 0f, 1f, value);
                tfManager.UpdateGreenChannelOpacity(opacityCurve);
            }
        }
        
        public void OpenAdvancedEditor()
        {
            // This would open a more sophisticated transfer function editor
            // For now, just log a message
            Debug.Log("[DualChannelTransferFunctionUI] Advanced editor not yet implemented");
            
            // TODO: Implement advanced transfer function editor with:
            // - Control points for opacity curves
            // - Color gradient editing
            // - Real-time preview
            // - Save/load presets
        }
        
        public void SaveTransferFunction()
        {
            if (tfManager != null)
            {
                var data = tfManager.SaveTransferFunctionData();
                // For now, save to PlayerPrefs (in a real implementation, use file dialog)
                PlayerPrefs.SetString("TransferFunctionData", JsonUtility.ToJson(data));
                Debug.Log("[DualChannelTransferFunctionUI] Transfer function saved to PlayerPrefs");
            }
        }
        
        public void LoadTransferFunction()
        {
            if (tfManager != null && PlayerPrefs.HasKey("TransferFunctionData"))
            {
                string json = PlayerPrefs.GetString("TransferFunctionData");
                var data = JsonUtility.FromJson<DualChannelTransferFunctionManager.TransferFunctionData>(json);
                tfManager.LoadTransferFunctionData(data);
                
                // Update UI sliders to match loaded data
                if (data.redOpacity.keys.Length > 0)
                {
                    redOpacity = data.redOpacity.keys[data.redOpacity.keys.Length - 1].value;
                    if (redOpacitySlider != null)
                        redOpacitySlider.value = redOpacity;
                }
                
                if (data.greenOpacity.keys.Length > 0)
                {
                    greenOpacity = data.greenOpacity.keys[data.greenOpacity.keys.Length - 1].value;
                    if (greenOpacitySlider != null)
                        greenOpacitySlider.value = greenOpacity;
                }
                
                UpdateOpacityLabels();
                Debug.Log("[DualChannelTransferFunctionUI] Transfer function loaded from PlayerPrefs");
            }
            else
            {
                Debug.LogWarning("[DualChannelTransferFunctionUI] No saved transfer function data found");
            }
        }
        
        // Public method to set reference to transfer function manager
        public void SetTransferFunctionManager(DualChannelTransferFunctionManager manager)
        {
            tfManager = manager;
        }
        
        private void OnDestroy()
        {
            // Clean up event handlers
            if (redOpacitySlider != null)
                redOpacitySlider.onValueChanged.RemoveListener(OnRedOpacityChanged);
            
            if (greenOpacitySlider != null)
                greenOpacitySlider.onValueChanged.RemoveListener(OnGreenOpacityChanged);
        }
    }
}