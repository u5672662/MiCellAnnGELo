using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loads, displays, and network‑syncs mesh time series data and UI controls.
/// Handles marker placement and colour updates without altering mesh content.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshLoader : NetworkBehaviour
{
    private MeshController meshController;
    
    public Slider progressBar;
    public Slider[] minR, maxR, minG, maxG, alphaSlider;
    
    public TMP_Text progressText, currentFrameText;

    // Stores prefab to duplicate for markers
    public GameObject markerPrefab;

    public GameObject colorMapper;

    // Stores an array of meshes (one for each frame of the loaded timeseries)
    public Mesh[] meshes;

    // Stores an array of Color32s indexed by [meshNumber, colourMode][vertex] for each mesh
    public Color32[,][] meshColors;
    public int[][] meshLabelIdx;

    public static Color32 backgroundVertexColor = new (100, 100, 100, 150);

    // Stores a list of vertices that have a marker
    public List<int>[] markers;
    public List<int>[] markerColors;

    public int[] nVertices;

    // Stores the colour mode (0 = original, 1 = labels, 2 = scaled, 3 = overlay)
    public enum colorMode
    {
        original,
        labels,
        scaled,
        overlay
    };

    [SerializeField] private int nModes = 4;
    public int colorModeIndex = 2;


    // Current frame number being displayed (index to meshes array)
    [SerializeField] private int currentFrame;
    public int nFrames;
    private Shader trans, opaque;
    private Material cellMat;
    private Quaternion startRotation;
    private Vector3 startPosition;
    private Vector3 startScale;

    public Material CellMaterial => cellMat;
    public Shader TransparentShader => trans;
    public Shader OpaqueShader => opaque;
    public ColorHandler Handler => colorHandler;
    public int CurrentFrame 
    { 
        get => currentFrame; 
        set => currentFrame = value; 
    }
    public int NFrames => nFrames;

    public int NModes => nModes;
    
    private ColorHandler colorHandler;
    private FileHandler fileHandler;

    // ------------------------------------------------------------------------
    // Networking for wrist & wall UI sliders
    // ------------------------------------------------------------------------
    // Each NetworkVariable represents a slider that needs to stay in sync
    // across all clients as well as between the wrist UI (player-attached)
    // and the wall UI (world-space).
    private readonly NetworkVariable<float> _netMinR = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _netMaxR = new(255f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _netMinG = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _netMaxG = new(255f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _netAlpha = new(255f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Set to true when updating sliders due to a network change to avoid feedback loops.
    private bool _isUpdatingFromNetwork = false;


    // TODO: allow different colour range inputs and rescale 
    private float[] rRange = { 0, 255.0f };

    private float[] gRange = { 0, 255.0f };

    // TODO: set initial size so that all inputs appear a similar size initially
    private Vector3 vSize;

    // Reference to the GameObject containing components
    public GameObject fileHandlerObject;
    
    /// <summary>
    /// Reads the first slider's value in an array to support mirrored UI controls.
    /// </summary>
    public float GetSliderValue(Slider[] sliders, float defaultValue = 0f)
    {
        // Reads the value from the first slider in an array.
        // This is used by the colour update logic to get the current settings.
        // Assumes all sliders in the array are synchronised by a component like SliderControl or SliderLabel.
        if (sliders != null && sliders.Length > 0 && sliders[0] != null)
            return sliders[0].value;
        return defaultValue;
    }

    private void Awake()
    {
        // Get the MeshController and MeshLoader components
        meshController = GetComponent<MeshController>();
        if (meshController == null)
        {
            Debug.LogError("MeshController not found on the same GameObject as FileHandler");
            // Try to find it in the scene
            meshController = FindAnyObjectByType<MeshController>();
            if (meshController != null)
            {
                Debug.Log("Found MeshController on a different GameObject");
            }
            else
            {
                Debug.LogError("MeshController not found in the scene!");
            }
        }
    }

    private void Start()
    {
        try {
            // Find shaders and material to use for the cell
            trans = Shader.Find("Custom/TransVertexColour");
            if (trans == null)
                trans = Resources.Load<Shader>("Shaders/TransVertexColour");
            opaque = Shader.Find("Custom/OpaqueVertexColour");
            
            if (trans == null || opaque == null)
            {
                Debug.LogError("Failed to find one or more required shaders. Trans shader: " + (trans != null) + ", Opaque shader: " + (opaque != null));
            }
            else
            {
                //Debug.Log("Shaders loaded. Trans: " + trans.name + " (supported: " + trans.isSupported + "), Opaque: " + opaque.name + " (supported: " + opaque.isSupported + ")");
            }
            
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                cellMat = meshRenderer.material;
                //Debug.Log("MeshRenderer found. Material: " + cellMat.name + ", Shader: " + cellMat.shader.name);
            }
            else
            {
                Debug.LogError("MeshRenderer component not found on GameObject");
            }
            Debug.Log("Running on platform: " + Application.platform);
            
            startRotation = transform.rotation;
            startPosition = transform.position;
            startScale = transform.localScale;
            
            // Get ColorHandler from colorMapper
            if (colorMapper != null)
            {
                colorHandler = colorMapper.GetComponent<ColorHandler>();
                if (colorHandler == null)
                {
                    Debug.LogError("ColorHandler component not found on colorMapper GameObject");
                }
            }
            else
            {
                Debug.LogError("colorMapper is not assigned in the Inspector");
            }
            
            // Try to get FileHandler from fileHandlerObject first
            if (fileHandlerObject != null)
            {
                fileHandler = FindAnyObjectByType<FileHandler>();
                if (fileHandler == null)
                {
                    Debug.LogError("FileHandler component not found on fileHandlerObject GameObject");
                }
            }
            else
            {
                // Try to get it from the same GameObject
                fileHandler = GetComponent<FileHandler>();
                if (fileHandler == null)
                {
                    // Try to find it in the scene
                    fileHandler = FindAnyObjectByType<FileHandler>();
                    if (fileHandler != null)
                    {
                        fileHandlerObject = fileHandler.gameObject;
                    }
                    else
                    {
                        Debug.LogError("FileHandler component not found anywhere in the scene");
                    }
                }
            }
            
            // Do not try to auto‑load in editor if we're missing components
            #if UNITY_EDITOR
            if (fileHandler != null && System.IO.Directory.Exists("X:/Uni Work MSc/Dissertation/Data/11-02-25/HM3477_XX0001"))
            {
                //fileHandler.LoadTimeseries("X:/Uni Work MSc/Dissertation/Data/11-02-25/HM3477_XX0001");
            }
            #endif
            Debug.Log("MeshController Start finished. Using shader " + (cellMat != null ? cellMat.shader.name : "none"));
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error in MeshController.Start(): " + e.Message + "\n" + e.StackTrace);
        }
        }

    #region Networked-Slider Synchronisation

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Subscribe to changes coming from the server
        _netMinR.OnValueChanged += OnNetMinRChanged;
        _netMaxR.OnValueChanged += OnNetMaxRChanged;
        _netMinG.OnValueChanged += OnNetMinGChanged;
        _netMaxG.OnValueChanged += OnNetMaxGChanged;
        _netAlpha.OnValueChanged += OnNetAlphaChanged;

        if (IsServer)
        {
            // Server is authoritative – push initial values to the network
            // Initialise default ranges explicitly to avoid overlapping handles when prefabs start at 0.
            _netMinR.Value = 0f;
            _netMaxR.Value = 255f;
            _netMinG.Value = 0f;
            _netMaxG.Value = 255f;
            _netAlpha.Value = GetSliderValue(alphaSlider, 255f);

            // Push those defaults to the local UI so the server sees the correct values too.
            ApplyValueToSliders(minR, _netMinR.Value);
            ApplyValueToSliders(maxR, _netMaxR.Value);
            ApplyValueToSliders(minG, _netMinG.Value);
            ApplyValueToSliders(maxG, _netMaxG.Value);
        }
        else
        {
            // Clients pull the current values from the network
            _isUpdatingFromNetwork = true;
            ApplyValueToSliders(minR, _netMinR.Value);
            ApplyValueToSliders(maxR, _netMaxR.Value);
            ApplyValueToSliders(minG, _netMinG.Value);
            ApplyValueToSliders(maxG, _netMaxG.Value);
            ApplyValueToSliders(alphaSlider, _netAlpha.Value);
            _isUpdatingFromNetwork = false;
        }

        // Ensure our local UI is hooked up
        SetupSliderListeners();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _netMinR.OnValueChanged -= OnNetMinRChanged;
        _netMaxR.OnValueChanged -= OnNetMaxRChanged;
        _netMinG.OnValueChanged -= OnNetMinGChanged;
        _netMaxG.OnValueChanged -= OnNetMaxGChanged;
        _netAlpha.OnValueChanged -= OnNetAlphaChanged;
    }

    #endregion

    #region Local UI Helpers

    private void SetupSliderListeners()
    {
        AddListenerArray(minR, OnLocalMinRChanged);
        AddListenerArray(maxR, OnLocalMaxRChanged);
        AddListenerArray(minG, OnLocalMinGChanged);
        AddListenerArray(maxG, OnLocalMaxGChanged);
        AddListenerArray(alphaSlider, OnLocalAlphaChanged);
    }

    private static void AddListenerArray(Slider[] sliders, UnityEngine.Events.UnityAction<float> callback)
    {
        if (sliders == null) return;
        foreach (var s in sliders)
        {
            if (s == null) continue;
            s.onValueChanged.AddListener(callback);
        }
    }

    private static void ApplyValueToSliders(Slider[] sliders, float value)
    {
        if (sliders == null) return;
        foreach (var s in sliders)
        {
            if (s == null) continue;
            s.SetValueWithoutNotify(value);
        }
    }

    #endregion

    #region Network → Local callbacks

    private void OnNetMinRChanged(float prev, float next)   => OnNetValueChanged(minR, next);
    private void OnNetMaxRChanged(float prev, float next)   => OnNetValueChanged(maxR, next);
    private void OnNetMinGChanged(float prev, float next)   => OnNetValueChanged(minG, next);
    private void OnNetMaxGChanged(float prev, float next)   => OnNetValueChanged(maxG, next);
    private void OnNetAlphaChanged(float prev, float next)  => OnNetValueChanged(alphaSlider, next);

    private void OnNetValueChanged(Slider[] sliders, float value)
    {
        if (_isUpdatingFromNetwork) return;

        _isUpdatingFromNetwork = true;
        ApplyValueToSliders(sliders, value);
        // Do not recalculate colours automatically; wait for the dedicated "Update Colours" button
        _isUpdatingFromNetwork = false;
    }

    #endregion

    #region Local → Network callbacks

    private void OnLocalMinRChanged(float value)  => PushLocalChange(_netMinR, UpdateMinRServerRpc, value);
    private void OnLocalMaxRChanged(float value)  => PushLocalChange(_netMaxR, UpdateMaxRServerRpc, value);
    private void OnLocalMinGChanged(float value)  => PushLocalChange(_netMinG, UpdateMinGServerRpc, value);
    private void OnLocalMaxGChanged(float value)  => PushLocalChange(_netMaxG, UpdateMaxGServerRpc, value);
    private void OnLocalAlphaChanged(float value) => PushLocalChange(_netAlpha, UpdateAlphaServerRpc, value);

    private delegate void FloatServerRpc(float v);

    private void PushLocalChange(NetworkVariable<float> netVar, FloatServerRpc rpc, float value)
    {
        if (_isUpdatingFromNetwork) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                netVar.Value = value;
            }
            else
            {
                rpc(value);
            }
        }
        else
        {
            // Offline single-player mode – colours will refresh when the user presses the "Update Colours" button
        }
    }

    [ServerRpc(RequireOwnership = false)] private void UpdateMinRServerRpc(float v) { _netMinR.Value = v; }
    [ServerRpc(RequireOwnership = false)] private void UpdateMaxRServerRpc(float v) { _netMaxR.Value = v; }
    [ServerRpc(RequireOwnership = false)] private void UpdateMinGServerRpc(float v) { _netMinG.Value = v; }
    [ServerRpc(RequireOwnership = false)] private void UpdateMaxGServerRpc(float v) { _netMaxG.Value = v; }
    [ServerRpc(RequireOwnership = false)] private void UpdateAlphaServerRpc(float v) { _netAlpha.Value = v; }

    #endregion

    //-------------------------------------------------------------------------------------------
    //                  PROCESSING LOADED MESHES                                                |
    //-------------------------------------------------------------------------------------------

    /// <summary>
    /// Displays the current frame locally, or requests a server broadcast if networked.
    /// </summary>
    public void DisplayMesh()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                DisplayMeshInternal(currentFrame);
                DisplayMeshClientRpc(currentFrame);
            }
            else
            {
                DisplayMeshServerRpc(currentFrame);
            }
        }
        else
        {
            DisplayMeshInternal(currentFrame);
        }
    }
    
    public void DisplayMeshLocal(int frame)
    {
        DisplayMeshInternal(frame);
    }

    [ServerRpc(RequireOwnership = false)]
    private void DisplayMeshServerRpc(int frame)
    {
        DisplayMeshInternal(frame);
        DisplayMeshClientRpc(frame);
    }

    [ClientRpc]
    public void DisplayMeshClientRpc(int frame)
    {
        if (IsServer)
            return;
        
        // Check if the mesh exists before trying to display it
        if (meshes != null && frame >= 0 && frame < meshes.Length && meshes[frame] != null)
        {
            DisplayMeshInternal(frame);
        }
        else
        {
            Debug.LogWarning($"DisplayMeshClientRpc: Mesh at frame {frame} not ready yet");
        }
    }

    private void DisplayMeshInternal(int frame)
    {
        Debug.Log("DisplayMesh called. Frame: " + frame + ", Shader: " + (cellMat != null ? cellMat.shader.name : "null"));

        if (meshes != null)
        {
            if (frame < 0 || frame >= meshes.Length)
            {
                Debug.LogError("Invalid current frame index: " + frame + " (array length: " + meshes.Length + ")");
                return;
            }
            
            // Check if colour arrays are ready
            if (meshColors == null || 
                frame >= meshColors.GetLength(0) || 
                meshColors[frame, 0] == null)
            {
                Debug.LogWarning($"DisplayMeshInternal: colours for frame {frame} not ready yet");
                return;
            }

            if (meshes[frame] != null)
            {
                try
                {
                    meshes[frame].colors32 = meshColors[frame, colorModeIndex];
                    GetComponent<MeshFilter>().mesh = meshes[frame];
                    var meshCol = GetComponent<MeshCollider>();
                    meshCol.sharedMesh = meshes[frame];
                    meshCol.convex = false; // Concave collider for accurate placement
                    meshCol.isTrigger = false; // Keep as non-trigger for VR grabbing
                    
                    // Try to use PlayerPassthroughHandler via reflection if present
                    var pphType = System.Type.GetType("PlayerPassthroughHandler");
                    if (pphType != null)
                    {
                        var pph = GetComponent(pphType) ?? gameObject.AddComponent(pphType);
                        pphType.GetMethod("RefreshIgnores")?.Invoke(pph, null);
                    }

                    // Ensure the player can move through the mesh by disabling collisions with any CharacterController
#if UNITY_2023_1_OR_NEWER || UNITY_2022_2_OR_NEWER
                    foreach (var cc in Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
#else
                    foreach (var cc in Object.FindObjectsOfType<CharacterController>())
#endif
                    {
                        Physics.IgnoreCollision(meshCol, cc);
                    }
                    currentFrameText.text = "Currently displaying frame: " + (frame + 1) + " / " + nFrames;
                    currentFrame = frame;
                    ChangeMarkers();
                }
                catch (System.Exception e)
                {
                    Debug.LogError("Error displaying mesh: " + e.Message);
                }
            }
            else
            {
                Debug.LogError("Failed to display mesh: Mesh at index " + frame + " is null");
            }
        }
        else
        {
            Debug.LogError("Failed to display mesh: No meshes loaded (meshes array is null)");
        }
    }

    private void UnloadMarkers()
    {
        foreach (Transform child in transform)
            GameObject.Destroy(child.gameObject);
    }

    public void ChangeMarkers()
    {
        UnloadMarkers();
        if (markers[currentFrame] != null && markers[currentFrame].Count > 0)
        {
            for (int i = 0; i < markers[currentFrame].Count; i++)
            {
                AddMarker(markers[currentFrame][i], markerColors[currentFrame][i]);
            }
        }
    }

    public void AddMarker(int vertex, int color)
    {
        GameObject markerObject = Instantiate(markerPrefab, transform);
        markerObject.transform.localPosition = meshes[currentFrame].vertices[vertex];
        Renderer markerRenderer = markerObject.GetComponent<Renderer>();
        markerRenderer.material.color = colorHandler.GetMarkerColor(color);
    }

    public void UpdateColours()
    {
        fileHandler.StopCoroutines();
        StartCoroutine(UpdateColorsCoroutine());
    }

    public IEnumerator UpdateColorsCoroutine()
    {
        if (meshes != null)
        {
            fileHandler.SetButtonState(false);
            var timeOfLastUpdate = Time.realtimeSinceStartup;
            var rScale = 255.0f / GetSliderValue(maxR, 255f);
            var gScale = 255.0f / GetSliderValue(maxG, 255f);

            var rMin = GetSliderValue(minR) * rScale;
            var gMin = GetSliderValue(minG) * gScale;

            progressBar.value = 0;
            progressText.text = "Updating: 0 / " + nFrames;

            for (int frameNo = 0; frameNo < nFrames; frameNo++)
            {
                if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                {
                    yield return null;
                    timeOfLastUpdate = Time.realtimeSinceStartup;
                }

                progressBar.value = (float)(frameNo + 1) / nFrames;
                progressText.text = "Updating: " + (frameNo + 1) + " / " + nFrames;
                UpdateColor(frameNo, rScale, gScale, rMin, gMin);
            }

            progressText.text = "Colours updated";
            fileHandler.SetButtonState(true);
            DisplayMesh();
        }
    }

    // TODO: adjust this to only update current frame using textures
    public void UpdateColor(int frameNo, float rScale, float gScale, float rMin, float gMin)
    {
        var vertCount = nVertices[frameNo];
        var aVal = GetSliderValue(alphaSlider, 255f);
        var aValB = System.Convert.ToByte(aVal);
        if (Mathf.Approximately(aVal, 255))
            cellMat.shader = opaque;
        else
            cellMat.shader = trans;
        Debug.Log("UpdateColor: alpha=" + aVal + ", using shader " + cellMat.shader.name);
        for (int i = 0; i < vertCount; i++)
        {
            var r = meshColors[frameNo, (int)colorMode.original][i].r * rScale;
            var g = meshColors[frameNo, (int)colorMode.original][i].g * gScale;
            var color = meshColors[frameNo, (int)colorMode.scaled][i];
            if (r < rMin && g < gMin)
                color = backgroundVertexColor;
            else
            {
                r = (r > 255) ? 255 : r;
                g = (g > 255) ? 255 : g;
                if (r >= rMin)
                    color.r = System.Convert.ToByte(r);
                else
                    color.r = 0;

                if (g >= gMin)
                    color.g = System.Convert.ToByte(g);
                else
                    color.g = 0;
                color.b = 0;
            }

            color.a = aValB;
            meshColors[frameNo, (int)colorMode.scaled][i] = color;
            meshColors[frameNo, (int)colorMode.scaled][i + vertCount] = color;
            meshColors[frameNo, (int)colorMode.labels][i].a = aValB;
            meshColors[frameNo, (int)colorMode.labels][i + vertCount].a = aValB;

            if (meshLabelIdx[frameNo][i] == 0)
            {
                color = colorMapper.GetComponent<ColorHandler>().GetPaintBackground();
                color.a = aValB;
            }

            meshColors[frameNo, (int)colorMode.overlay][i] = color;
            meshColors[frameNo, (int)colorMode.overlay][i + vertCount] = color;
        }
    }

    //                  SERIES CREATION/DESTRUCTION                                             |
    //-------------------------------------------------------------------------------------------

    public void ClearTimeseries()
    {
        // Reset index
        currentFrame = 0;
        // Remove current model from scene
        GetComponent<MeshFilter>().mesh = null;
        GetComponent<MeshCollider>().sharedMesh = null;
        // Reset shader
        cellMat.shader = opaque;
        //Debug.Log("ClearTimeseries: shader reset to " + cellMat.shader.name);
        // Reset position and remove any markers
        meshController.ResetCellPosition();
        UnloadMarkers();
        // Set arrays to null
        meshes = null;
        meshColors = null;
        markers = null;
        // Set status text
        currentFrameText.text = "No time series currently loaded";
        progressText.text = "";
    }

    public void CreateTimeseries(int n)
    {
        nFrames = n;
        // Initialise all arrays
        meshes = new Mesh[n];
        meshColors = new Color32[n, nModes][];
        meshLabelIdx = new int[n][];
        markers = new List<int>[n];
        markerColors = new List<int>[n];
        nVertices = new int[n];
        for (int i = 0; i < n; i++)
        {
            // Create a new marker list
            markers[i] = new List<int>();
            markerColors[i] = new List<int>();
        }
    }
}