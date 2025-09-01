using System.Linq;
using UnityEngine;
using UnityVolumeRendering;

/// <summary>
/// Result of a validation check with a boolean outcome and message.
/// </summary>
[System.Serializable]
public class ValidationResult
{
    public bool isValid;
    public string message;
    
    public ValidationResult(bool valid, string msg)
    {
        isValid = valid;
        message = msg;
    }
}

/// <summary>
/// Editor/runtime helper to validate that dual-channel transfer functions are configured correctly.
/// Non-invasive; logs findings and does not mutate state.
/// </summary>
public class DualChannelValidator : MonoBehaviour
{
    [Header("Validation Settings")]
    [Tooltip("Run validation automatically on start")]
    public bool validateOnStart = false;
    
    [Tooltip("Log detailed validation results")]
    public bool verboseLogging = false;
    
    // Validation results
    [Space]
    [Header("Validation Results (Read Only)")]
    [SerializeField] private bool shaderValid;
    [SerializeField] private bool managerValid;
    [SerializeField] private bool dataValid;
    [SerializeField] private bool materialValid;
    
    private VolumeRenderingManager volumeManager;
    private DualChannelTransferFunctionManager tfManager;
    
    private void Start()
    {
        if (validateOnStart)
        {
            ValidateSystem();
        }
    }
    
    [ContextMenu("Validate Dual Channel System")]
    public void ValidateSystem()
    {
        Debug.Log("=== Dual Channel Transfer Function Validation ===");
        
        bool allValid = true;
        
        // Validate Volume Manager
        var managerResult = ValidateVolumeManager();
        allValid &= managerResult.isValid;
        managerValid = managerResult.isValid;
        LogResult("Volume Manager", managerResult);
        
        // Validate Transfer Function Manager
        var tfManagerResult = ValidateTransferFunctionManager();
        allValid &= tfManagerResult.isValid;
        managerValid = tfManagerResult.isValid;
        LogResult("Transfer Function Manager", tfManagerResult);
        
        // Validate Material and Shader
        var materialResult = ValidateMaterialAndShader();
        allValid &= materialResult.isValid;
        materialValid = materialResult.isValid;
        shaderValid = materialResult.isValid;
        LogResult("Material and Shader", materialResult);
        
        // Validate Volume Data
        var dataResult = ValidateVolumeData();
        allValid &= dataResult.isValid;
        dataValid = dataResult.isValid;
        LogResult("Volume Data", dataResult);
        
        // Overall result
        string overallStatus = allValid ? "PASSED" : "FAILED";
        Debug.Log($"=== Overall Validation: {overallStatus} ===");
        
        if (!allValid)
        {
            Debug.LogWarning("Some validation checks failed. See above messages for details.");
        }
    }
    
    private ValidationResult ValidateVolumeManager()
    {
        volumeManager = FindFirstObjectByType<VolumeRenderingManager>();
        
        if (volumeManager == null)
        {
            return new ValidationResult(false, "No VolumeRenderingManager found in scene");
        }
        
        if (volumeManager.volumeMaterial == null)
        {
            return new ValidationResult(false, "VolumeRenderingManager.volumeMaterial is null");
        }
        
        return new ValidationResult(true, "VolumeRenderingManager found and configured");
    }
    
    private ValidationResult ValidateTransferFunctionManager()
    {
        if (volumeManager == null)
        {
            return new ValidationResult(false, "Cannot validate - VolumeRenderingManager not found");
        }
        
        tfManager = volumeManager.GetComponent<DualChannelTransferFunctionManager>();
        
        if (tfManager == null && !volumeManager.ShouldUseDualChannelMode())
        {
            return new ValidationResult(true, "Dual channel mode disabled - no transfer function manager needed");
        }
        
        if (tfManager == null && volumeManager.ShouldUseDualChannelMode())
        {
            return new ValidationResult(false, "Dual channel mode enabled but no DualChannelTransferFunctionManager component found");
        }
        
        if (tfManager != null && tfManager.volumeMaterial == null)
        {
            return new ValidationResult(false, "DualChannelTransferFunctionManager.volumeMaterial is null");
        }
        
        return new ValidationResult(true, "DualChannelTransferFunctionManager found and configured");
    }
    
    private ValidationResult ValidateMaterialAndShader()
    {
        if (volumeManager == null || volumeManager.volumeMaterial == null)
        {
            return new ValidationResult(false, "Cannot validate - volume material not found");
        }
        
        Material material = volumeManager.volumeMaterial;
        Shader shader = material.shader;
        
        if (shader == null)
        {
            return new ValidationResult(false, "Volume material has no shader assigned");
        }
        
        // Check if shader supports dual channel keywords
        string shaderName = shader.name;
        if (!shaderName.Contains("VolumeRendering") && !shaderName.Contains("Custom"))
        {
            return new ValidationResult(false, $"Shader '{shaderName}' may not support dual channel transfer functions");
        }
        
        // Check for required shader properties
        bool hasRedChannelTF = material.HasProperty("_RedChannelTF");
        bool hasGreenChannelTF = material.HasProperty("_GreenChannelTF");
        
        if (!hasRedChannelTF || !hasGreenChannelTF)
        {
            return new ValidationResult(false, "Shader missing required dual channel transfer function properties (_RedChannelTF, _GreenChannelTF)");
        }
        
        // Check if dual channel keyword is supported
        if (volumeManager.ShouldUseDualChannelMode())
        {
            bool keywordEnabled = material.IsKeywordEnabled("DUAL_CHANNEL_TF_ON");
            if (!keywordEnabled)
            {
                return new ValidationResult(false, "Dual channel mode enabled but DUAL_CHANNEL_TF_ON keyword not active on material");
            }
        }
        
        return new ValidationResult(true, "Material and shader properly configured for dual channel transfer functions");
    }
    
    private ValidationResult ValidateVolumeData()
    {
        if (volumeManager == null)
        {
             return new ValidationResult(false, "Cannot validate - VolumeRenderingManager not found");
        }

        // Find the loader
        var tiffLoader = FindFirstObjectByType<TiffTimeSeriesLoader>();
        if (tiffLoader == null)
        {
            return new ValidationResult(true, "No TiffTimeSeriesLoader found, skipping data validation.");
        }

        // Check the dataset inside the loader
        VolumeDataset dataset = tiffLoader.LoadedDataset;
        bool foundMultiChannelData = (dataset != null && dataset.isMultiChannel && dataset.data2 != null && dataset.data2.Length > 0);
        
        if (volumeManager.ShouldUseDualChannelMode() && !foundMultiChannelData)
        {
            return new ValidationResult(false, "Dual channel mode enabled but loader has not provided multi-channel volume data.");
        }

        if (foundMultiChannelData)
        {
            return new ValidationResult(true, "Multi-channel volume data found and properly configured.");
        }

        return new ValidationResult(true, "No multi-channel data required for current configuration.");
    }
    
    private void LogResult(string category, ValidationResult result)
    {
        if (verboseLogging)
        {
            string status = result.isValid ? "PASS" : "FAIL";
            Debug.Log($"[{status}] {category}: {result.message}");
        }
    }

}