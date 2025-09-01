using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Creates and applies dual-channel transfer function textures to a volume material.
/// Encapsulates red/green opacity curves and colour gradients. No rendering code here.
/// </summary>
public class DualChannelTransferFunctionManager : MonoBehaviour
{
    [Header("Transfer Function Settings")]
    [Tooltip("Resolution of the transfer function texture")]
    public int transferFunctionResolution = 256;
    
    [Header("Red Channel Transfer Function")]
    public AnimationCurve redChannelOpacityCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public Gradient redChannelColorGradient = new Gradient();
    
    [Header("Green Channel Transfer Function")]
    public AnimationCurve greenChannelOpacityCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public Gradient greenChannelColorGradient = new Gradient();
    
    [Header("Material References")]
    public Material volumeMaterial;
    
    // Events
    [System.Serializable]
    public class TransferFunctionChangedEvent : UnityEvent { }
    public TransferFunctionChangedEvent OnTransferFunctionChanged;
    
    // Private members
    private Texture2D redChannelTFTexture;
    private Texture2D greenChannelTFTexture;
    
    // Shader property IDs for performance
    private static readonly int RedChannelTFProperty = Shader.PropertyToID("_RedChannelTF");
    private static readonly int GreenChannelTFProperty = Shader.PropertyToID("_GreenChannelTF");
    private static readonly int DualChannelTFKeyword = Shader.PropertyToID("DUAL_CHANNEL_TF_ON");
    
    private void Start()
    {
        InitializeDefaultGradients();
        CreateTransferFunctionTextures();
        ApplyTransferFunctionsToMaterial();
    }
    
    private void OnValidate()
    {
        // Update transfer functions when values change in inspector
        if (Application.isPlaying)
        {
            CreateTransferFunctionTextures();
            ApplyTransferFunctionsToMaterial();
        }
    }
    
    private void InitializeDefaultGradients()
    {
        // Set default red channel gradient (red to bright red)
        GradientColorKey[] redColorKeys = new GradientColorKey[2];
        redColorKeys[0] = new GradientColorKey(Color.red, 0f);
        redColorKeys[1] = new GradientColorKey(Color.red, 1f);
        
        GradientAlphaKey[] redAlphaKeys = new GradientAlphaKey[2];
        redAlphaKeys[0] = new GradientAlphaKey(0f, 0f);
        redAlphaKeys[1] = new GradientAlphaKey(1f, 1f);
        
        redChannelColorGradient.SetKeys(redColorKeys, redAlphaKeys);
        
        // Set default green channel gradient (green to bright green)
        GradientColorKey[] greenColorKeys = new GradientColorKey[2];
        greenColorKeys[0] = new GradientColorKey(Color.green, 0f);
        greenColorKeys[1] = new GradientColorKey(Color.green, 1f);
        
        GradientAlphaKey[] greenAlphaKeys = new GradientAlphaKey[2];
        greenAlphaKeys[0] = new GradientAlphaKey(0f, 0f);
        greenAlphaKeys[1] = new GradientAlphaKey(1f, 1f);
        
        greenChannelColorGradient.SetKeys(greenColorKeys, greenAlphaKeys);
    }
    
    public void CreateTransferFunctionTextures()
    {
        CreateRedChannelTexture();
        CreateGreenChannelTexture();
    }
    
    /// <summary>
    /// Manual initialization for when the component is created dynamically
    /// </summary>
    public void Initialize()
    {
        InitializeDefaultGradients();
        CreateTransferFunctionTextures();
    }
    
    private void CreateRedChannelTexture()
    {
        if (redChannelTFTexture != null)
            DestroyImmediate(redChannelTFTexture);
            
        redChannelTFTexture = new Texture2D(transferFunctionResolution, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        
        Color[] colors = new Color[transferFunctionResolution];
        for (int i = 0; i < transferFunctionResolution; i++)
        {
            float t = (float)i / (transferFunctionResolution - 1);
            Color gradientColor = redChannelColorGradient.Evaluate(t);
            float opacity = redChannelOpacityCurve.Evaluate(t);
            
            colors[i] = new Color(gradientColor.r, gradientColor.g, gradientColor.b, opacity);
        }
        
        redChannelTFTexture.SetPixels(colors);
        redChannelTFTexture.Apply();
    }
    
    private void CreateGreenChannelTexture()
    {
        if (greenChannelTFTexture != null)
            DestroyImmediate(greenChannelTFTexture);
            
        greenChannelTFTexture = new Texture2D(transferFunctionResolution, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        
        Color[] colors = new Color[transferFunctionResolution];
        for (int i = 0; i < transferFunctionResolution; i++)
        {
            float t = (float)i / (transferFunctionResolution - 1);
            Color gradientColor = greenChannelColorGradient.Evaluate(t);
            float opacity = greenChannelOpacityCurve.Evaluate(t);
            
            colors[i] = new Color(gradientColor.r, gradientColor.g, gradientColor.b, opacity);
        }
        
        greenChannelTFTexture.SetPixels(colors);
        greenChannelTFTexture.Apply();
    }
    
    public void ApplyTransferFunctionsToMaterial()
    {
        if (volumeMaterial == null) 
        {
            Debug.LogError("[DualChannelTransferFunctionManager] Cannot apply transfer functions - volumeMaterial is null");
            return;
        }
        
        // Set transfer function textures
        if (redChannelTFTexture != null)
        {
            volumeMaterial.SetTexture(RedChannelTFProperty, redChannelTFTexture);
            Debug.Log($"[DualChannelTransferFunctionManager] Applied red channel TF: {redChannelTFTexture.name} ({redChannelTFTexture.width}x{redChannelTFTexture.height})");
        }
        else
        {
            Debug.LogWarning("[DualChannelTransferFunctionManager] Red channel TF texture is null");
        }
            
        if (greenChannelTFTexture != null)
        {
            volumeMaterial.SetTexture(GreenChannelTFProperty, greenChannelTFTexture);
            Debug.Log($"[DualChannelTransferFunctionManager] Applied green channel TF: {greenChannelTFTexture.name} ({greenChannelTFTexture.width}x{greenChannelTFTexture.height})");
        }
        else
        {
            Debug.LogWarning("[DualChannelTransferFunctionManager] Green channel TF texture is null");
        }
        
        // Enable dual channel transfer function keyword
        volumeMaterial.EnableKeyword("DUAL_CHANNEL_TF_ON");
        Debug.Log("[DualChannelTransferFunctionManager] Enabled DUAL_CHANNEL_TF_ON keyword");
        
        OnTransferFunctionChanged?.Invoke();
    }
    
    public void DisableDualChannelMode()
    {
        if (volumeMaterial != null)
        {
            volumeMaterial.DisableKeyword("DUAL_CHANNEL_TF_ON");
        }
    }
    
    // Public methods for runtime modification
    public void UpdateRedChannelOpacity(AnimationCurve newCurve)
    {
        redChannelOpacityCurve = newCurve;
        CreateRedChannelTexture();
        ApplyTransferFunctionsToMaterial();
    }
    
    public void UpdateGreenChannelOpacity(AnimationCurve newCurve)
    {
        greenChannelOpacityCurve = newCurve;
        CreateGreenChannelTexture();
        ApplyTransferFunctionsToMaterial();
    }
    
    public void UpdateRedChannelColor(Gradient newGradient)
    {
        redChannelColorGradient = newGradient;
        CreateRedChannelTexture();
        ApplyTransferFunctionsToMaterial();
    }
    
    public void UpdateGreenChannelColor(Gradient newGradient)
    {
        greenChannelColorGradient = newGradient;
        CreateGreenChannelTexture();
        ApplyTransferFunctionsToMaterial();
    }
    
    // Save/Load functionality
    [System.Serializable]
    public class TransferFunctionData
    {
        public AnimationCurve redOpacity;
        public AnimationCurve greenOpacity;
        public string redColorGradientJson;
        public string greenColorGradientJson;
    }
    
    public TransferFunctionData SaveTransferFunctionData()
    {
        return new TransferFunctionData
        {
            redOpacity = new AnimationCurve(redChannelOpacityCurve.keys),
            greenOpacity = new AnimationCurve(greenChannelOpacityCurve.keys),
            redColorGradientJson = JsonUtility.ToJson(redChannelColorGradient),
            greenColorGradientJson = JsonUtility.ToJson(greenChannelColorGradient)
        };
    }
    
    public void LoadTransferFunctionData(TransferFunctionData data)
    {
        redChannelOpacityCurve = data.redOpacity;
        greenChannelOpacityCurve = data.greenOpacity;
        
        // Note: Unity's JsonUtility doesn't directly support Gradient serialization
        // This would need a custom serialization method for full implementation
        
        CreateTransferFunctionTextures();
        ApplyTransferFunctionsToMaterial();
    }
    
    private void OnDestroy()
    {
        if (redChannelTFTexture != null)
            DestroyImmediate(redChannelTFTexture);
        if (greenChannelTFTexture != null)
            DestroyImmediate(greenChannelTFTexture);
    }
}