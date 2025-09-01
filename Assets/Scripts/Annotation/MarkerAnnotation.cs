using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using XRMultiplayer;

/// <summary>
/// Stores metadata for a marker GameObject, including the owner name and display colour.
/// </summary>
public class MarkerMeta : MonoBehaviour
{
    /// <summary>
    /// Display name of the user who placed the marker.
    /// </summary>
    public string OwnerName;

    /// <summary>
    /// Display colour used to tint the marker material.
    /// </summary>
    public Color MarkerColour = Color.white;
}



/// <summary>
/// Manages the placement, removal, and network replication of per-frame annotation markers.
/// Provides input-driven placement and sizing, and raises events consumed by UI.
/// </summary>
public class MarkerAnnotation : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject controller;
    [SerializeField] private GameObject visMarker;
    [SerializeField] private GameObject markerPrefab;
    [Tooltip("Transform that markers should be parented under (typically the cell root that moves/scales/rotates). Defaults to this transform.")]
    [SerializeField] private Transform markerParent;

    [Header("Settings")]
    [SerializeField] private string sourceName = "Default";
    [SerializeField] private bool annotationMode = false;
    [SerializeField] private float scaleStep = 0.002f;
    
    [Header("Input Actions")]
    [SerializeField] private InputActionReference placeAction;
    [SerializeField] private InputActionReference toggleModeAction;
    [SerializeField] private InputActionReference increaseSizeAction;
    [SerializeField] private InputActionReference decreaseSizeAction;
    [SerializeField] private InputActionReference toggleAnnotationAction;

    // Constants for tolerances and limits to avoid magic numbers in logic.
    private const float DistanceEpsilon = 0.0001f;
    private const float MinimumMarkerScale = 0.001f;

    private MeshLoader _meshLoader;
    private TiffTimeSeriesLoader _tiffLoader;
    private Collider _collider;
    private bool _placeMode = true;
    private int _displayFrame = 0;
    private float _markerScale = 0.02f;
    private bool _canPlace;
    private Collider _visMarkerCollider;

    private float _lastScaleAdjustTime;
    [SerializeField] private float scaleAdjustInterval = 0.15f; // seconds between continuous scale steps

    // Actions managed for annotation mode
    [SerializeField] private float stickDistance = 0.03f; // metres (legacy for ray mode)

    // Per-frame list of markers. Markers are instantiated as children of this GameObject so their transforms are in the local space of the cell/volume object.
    private List<List<GameObject>> _markerList = new();

    private void OnEnable()
    {
        // Start with annotation actions disabled
        DisableAction(placeAction);
        DisableAction(toggleModeAction);
        DisableAction(increaseSizeAction);
        DisableAction(decreaseSizeAction);

        // Toggle action always enabled so user can switch modes anytime
        EnableAction(toggleAnnotationAction);
    }

    private void OnDisable()
    {
        DisableAction(placeAction);
        DisableAction(toggleModeAction);
        DisableAction(increaseSizeAction);
        DisableAction(decreaseSizeAction);
    }
    
    /// <summary>
    /// Toggles annotation mode on or off, enabling or disabling relevant input actions and preview visibility.
    /// </summary>
    public void ToggleAnnotationMode()
    {
        annotationMode = !annotationMode; // Toggle annotation mode

        if (annotationMode)
        {
            EnableAction(placeAction);
            EnableAction(toggleModeAction);
            EnableAction(increaseSizeAction);
            EnableAction(decreaseSizeAction);
        }
        else
        {
            DisableAction(placeAction);
            DisableAction(toggleModeAction);
            DisableAction(increaseSizeAction);
            DisableAction(decreaseSizeAction);
        }

        if (visMarker != null)
        {
            visMarker.SetActive(annotationMode); // preview visible only in annotation mode
        }
        UpdateMarkerColor();
    }

    /// <summary>
    /// Legacy UI button hook that simply toggles annotation mode.
    /// </summary>
    public void SetSetting()
    {
        ToggleAnnotationMode();
    }

    private void Start()
    {
        _meshLoader = GetComponent<MeshLoader>();
        _tiffLoader = GetComponent<TiffTimeSeriesLoader>();
        _collider = GetComponent<Collider>();
        if (markerParent == null)
        {
            markerParent = transform;
        }
        if (visMarker != null)
        {
            _visMarkerCollider = visMarker.GetComponent<Collider>();
        }
        if (visMarker != null)
        {
            // Ensure preview lives under the marker parent so local space matches
            visMarker.transform.SetParent(markerParent, worldPositionStays: true);
            // Disable any NetworkObject on the preview to avoid NGO transform sync on the preview object
            if (visMarker.TryGetComponent(out NetworkObject visNetObj))
            {
                visNetObj.enabled = false;
            }
            visMarker.SetActive(true);
            var renderer = visMarker.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null &&
                renderer.sharedMaterial.HasProperty("_EmissionColor"))
            {
                renderer.sharedMaterial.EnableKeyword("_EMISSION");
            }
            UpdateMarkerColor();
        }
        int frameCount = 1;
        if (_meshLoader != null && _meshLoader.NFrames > 0)
        {
            frameCount = _meshLoader.NFrames;
        }
        else if (_tiffLoader != null && _tiffLoader.FrameCount > 0)
        {
            frameCount = _tiffLoader.FrameCount;
        }
        InitializeList(frameCount);
    }

    private void Update()
    {
        // Handle toggling the entire annotation mode first so this can be accessed even when annotations are off
        if (toggleAnnotationAction != null && toggleAnnotationAction.action.WasPressedThisFrame())
        {
            ToggleAnnotationMode();
        }

        if (!annotationMode || controller == null)
        {
            if (visMarker != null)
            {
                visMarker.SetActive(false);
            }
            return;
        }

        // Preview marker follows controller tip; placement allowed only when its collider touches the cell collider
		var ctrlTf = controller.transform;
		if (visMarker != null)
		{
			visMarker.SetActive(true);
			visMarker.transform.SetPositionAndRotation(ctrlTf.position, ctrlTf.rotation);
		}

		_canPlace = false;
		if (_visMarkerCollider != null && _collider != null)
		{
			_canPlace = Physics.ComputePenetration(
				_visMarkerCollider, _visMarkerCollider.transform.position, _visMarkerCollider.transform.rotation,
				_collider, _collider.transform.position, _collider.transform.rotation,
				out _, out _);
			// Fallback for non-convex mesh colliders (ComputePenetration may fail)
			if (!_canPlace)
			{
				_canPlace = _visMarkerCollider.bounds.Intersects(_collider.bounds);
			}
		}

        if (toggleModeAction != null && toggleModeAction.action.WasPressedThisFrame())
        {
            _placeMode = !_placeMode;
            UpdateMarkerColor();
        }

        HandleScaleAdjustment();

        if (placeAction != null && placeAction.action.WasPressedThisFrame())
        {
            if (_placeMode)
            {
                if (_canPlace)
                {
                    TryPlaceMarker();
                }
            }
            else
            {
                TryRemoveMarker();
            }
        }
    }

    private void UpdateMarkerColor()
    {
        if (visMarker == null)
        {
            return;
        }
        var renderer = visMarker.GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null &&
            renderer.sharedMaterial.HasProperty("_EmissionColor"))
        {
            Color color;
            if (!annotationMode)
            {
                color = Color.gray; // Normal mode indicator
            }
            else
            {
                color = _placeMode ? Color.blue : Color.white;
            }
            renderer.sharedMaterial.SetColor("_EmissionColor", color);
        }
    }

    private void TryPlaceMarker()
    {
        if (markerPrefab == null)
        {
            Debug.LogError("[MarkerAnnotation] markerPrefab is not assigned.");
            return;
        }
        if (!_canPlace)
        {
            return;
        }
        if (_collider == null || visMarker == null)
        {
            return;
        }
        // Use the preview marker's local position relative to the configured parent
		Vector3 localPos = visMarker.transform.localPosition;

        // Gather player-specific metadata
        Color playerColor = XRINetworkGameManager.LocalPlayerColor.Value;
        Vector3 colorVec = new Vector3(playerColor.r, playerColor.g, playerColor.b);
        string playerName = XRINetworkGameManager.LocalPlayerName.Value;

        // Always place locally for immediate feedback
        PlaceMarkerLocal(localPos, _markerScale, _displayFrame, colorVec, playerName);
        Debug.Log($"[MarkerAnnotation] Place request. Preview world={visMarker.transform.position}, local={localPos}, frame={_displayFrame}, scale={_markerScale}");

        // If we are in a networked session, propagate the placement across the network.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                // We are the server/host – directly notify all connected clients (except ourselves)
                PlaceMarkerClientRpc(localPos, _markerScale, _displayFrame, colorVec, playerName);
            }
            else
            {
                // We are a client – ask the server to replicate the marker
                PlaceMarkerServerRpc(localPos, _markerScale, _displayFrame, colorVec, playerName);
            }
        }
    }

    /// <summary>
    /// Converts a world-space point to the local space of this object, then clamps it to lie within the attached collider's bounds.
    /// If no collider exists, returns the simple inverse-transform point.
    /// </summary>
    private Vector3 GetClampedLocalPointInsideCollider(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        if (_collider == null)
        {
            return local;
        }

        Bounds localBounds = new Bounds();
        // Build bounds in local-space for common collider types
        switch (_collider)
        {
            case BoxCollider box:
                localBounds.center = box.center;
                localBounds.size = box.size;
                break;
            case SphereCollider sphere:
                localBounds.center = sphere.center;
                localBounds.size = Vector3.one * (sphere.radius * 2f);
                break;
            case CapsuleCollider capsule:
                localBounds.center = capsule.center;
                float diameter = capsule.radius * 2f;
                Vector3 size = new Vector3(diameter, diameter, diameter);
                if (capsule.direction == 0) // X
                    size.x = capsule.height;
                else if (capsule.direction == 1) // Y
                    size.y = capsule.height;
                else // Z
                    size.z = capsule.height;
                localBounds.size = size;
                break;
            default:
                // For mesh or unknown colliders, approximate with renderer bounds if available
                if (TryGetComponent(out Renderer rend))
                {
                    localBounds = rend.localBounds;
                }
                else
                {
                    // Fallback to unit cube
                    localBounds.center = Vector3.zero;
                    localBounds.size = Vector3.one;
                }
                break;
        }

        Vector3 half = localBounds.extents;
        Vector3 c = localBounds.center;
        local.x = Mathf.Clamp(local.x, c.x - half.x, c.x + half.x);
        local.y = Mathf.Clamp(local.y, c.y - half.y, c.y + half.y);
        local.z = Mathf.Clamp(local.z, c.z - half.z, c.z + half.z);
        return local;
    }

    private void TryRemoveMarker()
    {
        if (visMarker == null)
        {
            return;
        }

        // Distance-based overlap in world space (robust against trigger settings and layer collisions)
        float visRadius = 0.0f;
        if (visMarker.TryGetComponent(out Collider visMarkerCollider))
        {
            // Approximate radius using bounds
            visRadius = Mathf.Max(0.0001f, visMarkerCollider.bounds.extents.magnitude * 0.5f);
        }
        else
        {
            // Fallback to scale-based radius
            visRadius = Mathf.Max(0.0001f, visMarker.transform.lossyScale.x * 0.5f);
        }

        for (int i = 0; i < _markerList[_displayFrame].Count; i++)
        {
            GameObject marker = _markerList[_displayFrame][i];
            if (marker == null) continue;

            float markerRadius = 0.0f;
            if (marker.TryGetComponent(out Collider markerCollider))
            {
                markerRadius = Mathf.Max(0.0001f, markerCollider.bounds.extents.magnitude * 0.5f);
            }
            else
            {
                markerRadius = Mathf.Max(0.0001f, marker.transform.lossyScale.x * 0.5f);
            }

            float worldDistance = Vector3.Distance(visMarker.transform.position, marker.transform.position);
            bool overlapping = worldDistance <= (visRadius + markerRadius) * 1.1f;

            // Secondary collider-based test (helps when sizes are very small)
            if (!overlapping && visMarkerCollider != null && marker.TryGetComponent(out Collider mkCol))
            {
                overlapping = Physics.ComputePenetration(
                    visMarkerCollider, visMarker.transform.position, visMarker.transform.rotation,
                    mkCol, marker.transform.position, marker.transform.rotation,
                    out _, out _)
                    || visMarkerCollider.bounds.Intersects(mkCol.bounds);
            }

            if (overlapping)
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    RemoveMarkerServerRpc(marker.transform.localPosition, _displayFrame);
                }
                else
                {
                    RemoveMarkerLocal(marker.transform.localPosition, _displayFrame);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Deactivates all markers in the currently displayed frame.
    /// </summary>
    public void DeactivateMarker()
    {
        if (_displayFrame < 0 || _displayFrame >= _markerList.Count)
        {
            return;
        }
        foreach (GameObject marker in _markerList[_displayFrame])
        {
            if (marker != null)
            {
                marker.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Activates all markers for the specified frame and deactivates the previous frame's markers.
    /// </summary>
    /// <param name="frame">Frame index to display.</param>
    public void ActivateMarker(int frame)
    {
        if (frame < 0 || frame >= _markerList.Count)
        {
            return;
        }
        DeactivateMarker();
        _displayFrame = frame;
        foreach (GameObject marker in _markerList[_displayFrame])
        {
            if (marker != null)
            {
                marker.SetActive(true);
            }
        }
    }

    /// <summary>
    /// Initialises the internal per-frame storage with the specified number of frames.
    /// </summary>
    /// <param name="size">Number of frames to allocate.</param>
    public void InitializeList(int size)
    {
        _markerList = new List<List<GameObject>>(size);
        for (int i = 0; i < size; i++)
        {
            _markerList.Add(new List<GameObject>());
        }
    }

    /// <summary>
    /// Returns the per-frame list of markers. Use with care; the lists are mutable.
    /// </summary>
    public List<List<GameObject>> ReturnMarkerInfo()
    {
        return _markerList;
    }

    /// <summary>
    /// Destroys and clears all markers across all frames.
    /// </summary>
    public void ClearAllMarkers()
    {
        foreach (var frameMarkers in _markerList)
        {
            foreach (var marker in frameMarkers)
            {
                if (marker != null)
                {
                    if (marker.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
                    {
                        netObj.Despawn(true);
                    }
                    Destroy(marker);
                }
            }
            frameMarkers.Clear();
        }
    }


    /// <summary>
    /// Loads markers from a CSV text payload produced by <see cref="SerialiseMarkers"/>.
    /// </summary>
    /// <param name="entireText">CSV text including header.</param>
    public void LoadMarkerInfo(string entireText)
    {
        using (StringReader reader = new StringReader(entireText))
        {
            string line;
            // Skip header
            reader.ReadLine();
            while ((line = reader.ReadLine()) != null)
            {
                string[] split = line.Split(',');
                if (split.Length < 6)
                {
                    continue;
                }
                
                string source = split[0];
                if (source != sourceName)
                {
                    continue;
                }

                int frame = int.Parse(split[1]);
                float x = float.Parse(split[2]);
                float y = float.Parse(split[3]);
                float z = float.Parse(split[4]);
                float scale = float.Parse(split[5]);

                string ownerName = "Unknown";
                Color color = Color.white;

                if (split.Length > 6)
                {
                    ownerName = split[6];
                }

                if (split.Length > 7)
                {
                    if (ColorUtility.TryParseHtmlString("#" + split[7], out Color parsedColor))
                    {
                        color = parsedColor;
                    }
                }
                
                if (frame >= 0 && frame < _markerList.Count)
                {
                    Vector3 colorVec = new Vector3(color.r, color.g, color.b);
                    GameObject marker = MakeMarker(new Vector3(x, y, z), scale, colorVec, ownerName);
                    _markerList[frame].Add(marker);
                    if (frame == _displayFrame)
                    {
                        marker.SetActive(true);
                    }
                    OnMarkerPlaced?.Invoke(this, frame, marker.transform.localPosition);
                }
            }
        }
    }

    private GameObject MakeMarker(Vector3 position, float scale, Vector3 colorVec, string ownerName)
    {
        // Instantiate as child of the configured parent so it stays in that local space
        GameObject marker = Instantiate(markerPrefab, markerParent);
        marker.transform.localPosition = position;
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale = Vector3.one * scale;

        // Ensure a NetworkObject exists so we can parent/spawn correctly in NGO
        if (!marker.TryGetComponent(out NetworkObject netObj))
        {
            netObj = marker.AddComponent<NetworkObject>();
        }

        // For network spawning we need the object active.
        bool wasActive = marker.activeSelf;
        marker.SetActive(true);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !netObj.IsSpawned)
        {
            // If this cell has a NetworkObject, establish networked parenting before spawn
            NetworkObject parentNetObj;
            if (markerParent != null && markerParent.TryGetComponent(out parentNetObj))
            {
                // Preserve the set local pose
                netObj.TrySetParent(parentNetObj, worldPositionStays: false);
            }
            else
            {
                // Ensure transform parenting (non-networked parent) preserves local pose
                marker.transform.SetParent(markerParent, worldPositionStays: false);
            }
            // Spawn the marker so that it is replicated to all clients with the correct parent
            netObj.Spawn(true);
        }

        // Apply player colour to the marker's material so it is immediately visible with the correct tint.
        Color markerColor = new Color(colorVec.x, colorVec.y, colorVec.z, 1f);
        if (marker.TryGetComponent(out Renderer rend))
        {
            // Instantiate a unique material instance so that changing the colour doesn't affect other markers.
            Material matInstance = new Material(rend.sharedMaterial);
            matInstance.color = markerColor;
            if (matInstance.HasProperty("_EmissionColor"))
            {
                matInstance.SetColor("_EmissionColor", markerColor);
            }
            rend.material = matInstance;
        }

        // Store metadata so the UI can show who placed the marker.
        global::MarkerMeta meta = marker.GetComponent<global::MarkerMeta>();
        if (meta == null)
        {
            meta = marker.AddComponent<global::MarkerMeta>();
        }
        meta.OwnerName = ownerName;
        meta.MarkerColour = markerColor;

        // The marker starts hidden – it will be activated later if its frame is currently displayed.
        marker.SetActive(false);
        return marker;
    }

    // Backwards-compatibility overload used by legacy import code that does not include colour/owner metadata.
    private GameObject MakeMarker(Vector3 position, float scale)
    {
        Vector3 defaultColour = Vector3.one; // white
        return MakeMarker(position, scale, defaultColour, "Unknown");
    }

    private void HandleScaleAdjustment()
    {
        bool incHeld = increaseSizeAction != null && increaseSizeAction.action.IsPressed();
        bool decHeld = decreaseSizeAction != null && decreaseSizeAction.action.IsPressed();

        bool incTap = increaseSizeAction != null && increaseSizeAction.action.WasPressedThisFrame();
        bool decTap = decreaseSizeAction != null && decreaseSizeAction.action.WasPressedThisFrame();

        float now = Time.time;
        if (incTap || (incHeld && now - _lastScaleAdjustTime >= scaleAdjustInterval))
        {
            AdjustScale(scaleStep);
            _lastScaleAdjustTime = now;
        }
        if (decTap || (decHeld && now - _lastScaleAdjustTime >= scaleAdjustInterval))
        {
            AdjustScale(-scaleStep);
            _lastScaleAdjustTime = now;
        }
    }

    private void AdjustScale(float delta)
    {
        _markerScale = Mathf.Max(0.001f, _markerScale + delta);
        if (visMarker != null)
        {
            visMarker.transform.localScale = Vector3.one * _markerScale;
        }
    }
    private static void EnableAction(InputActionReference actionRef)
    {
        if (actionRef != null)
        {
            actionRef.action.Enable();
        }
    }

    private static void DisableAction(InputActionReference actionRef)
    {
        if (actionRef != null)
        {
            actionRef.action.Disable();
        }
    }

    #region Network Synchronisation

    [ServerRpc(RequireOwnership = false)]
    private void PlaceMarkerServerRpc(Vector3 localPos, float scale, int frame, Vector3 colorVec, string ownerName, ServerRpcParams rpcParams = default)
    {
        // Place on the server (and therefore host if running as host)
        PlaceMarkerLocal(localPos, scale, frame, colorVec, ownerName);

        // Build a list of clients to notify, excluding the one that originated the RPC (to avoid double-placement)
        ulong senderId = rpcParams.Receive.SenderClientId;
        List<ulong> targetIds = new List<ulong>();
        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            if (kvp.Key != senderId)
            {
                targetIds.Add(kvp.Key);
            }
        }
        ClientRpcParams cp = default;
        if (targetIds.Count > 0)
        {
            cp = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = targetIds.ToArray() }
            };
        }
        PlaceMarkerClientRpc(localPos, scale, frame, colorVec, ownerName, cp);
    }

    [ClientRpc]
    private void PlaceMarkerClientRpc(Vector3 localPos, float scale, int frame, Vector3 colorVec, string ownerName, ClientRpcParams rpcParams = default)
    {
        // Guard against double placement; the target client is the originating sender or host
        if (IsServer) return; // Host already placed via server-side execution
        PlaceMarkerLocal(localPos, scale, frame, colorVec, ownerName);
    }

    private void PlaceMarkerLocal(Vector3 localPos, float scale, int frame, Vector3 colorVec, string ownerName)
    {
        if (frame < 0 || frame >= _markerList.Count)
        {
            return;
        }
        GameObject marker = MakeMarker(localPos, scale, colorVec, ownerName);
        Debug.Log($"[MarkerAnnotation] Marker instantiated local pos {localPos}, parent {transform.name}");
        _markerList[frame].Add(marker);
        if (frame == _displayFrame)
        {
            marker.SetActive(true);
        }
        OnMarkerPlaced?.Invoke(this, frame, localPos);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RemoveMarkerServerRpc(Vector3 localPos, int frame, ServerRpcParams rpcParams = default)
    {
        RemoveMarkerLocal(localPos, frame);
        RemoveMarkerClientRpc(localPos, frame);
    }

    [ClientRpc]
    private void RemoveMarkerClientRpc(Vector3 localPos, int frame, ClientRpcParams rpcParams = default)
    {
        if (IsServer) return;
        RemoveMarkerLocal(localPos, frame);
    }

    private void RemoveMarkerLocal(Vector3 localPos, int frame)
    {
        if (frame < 0 || frame >= _markerList.Count)
        {
            return;
        }
        for (int i = 0; i < _markerList[frame].Count; i++)
        {
            GameObject marker = _markerList[frame][i];
            if (marker == null) continue;
            if (Vector3.Distance(marker.transform.localPosition, localPos) < DistanceEpsilon)
            {
                if (marker.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
                Destroy(marker);
                _markerList[frame].RemoveAt(i);
                OnMarkerRemoved?.Invoke(this, frame, localPos);
                break;
            }
        }
    }

    #endregion

    /// <summary>
    /// Name used to tag this annotation source when saving and loading.
    /// </summary>
    public string SourceName => sourceName;

    #region Serialisation

    /// <summary>
    /// Raised when a marker is placed. Provides the source, frame index, and local-space position.
    /// </summary>
    public event System.Action<MarkerAnnotation, int, Vector3> OnMarkerPlaced;

    /// <summary>
    /// Raised when a marker is removed. Provides the source, frame index, and local-space position.
    /// </summary>
    public event System.Action<MarkerAnnotation, int, Vector3> OnMarkerRemoved;


    /// <summary>
    /// Serialises all markers into a CSV-compatible set of lines.
    /// Each line: frame,x,y,z,scale,owner,hexColour (local coordinates, same system used during placement).
    /// </summary>
    public IEnumerable<string> SerialiseMarkers()
    {
        for (int frame = 0; frame < _markerList.Count; frame++)
        {
            foreach (GameObject marker in _markerList[frame])
            {
                if (marker == null) continue;
                
                Vector3 p = marker.transform.localPosition;
                float s = marker.transform.localScale.x;
                string owner = "Unknown";
                string colorHex = "FFFFFF";

                if (marker.TryGetComponent(out MarkerMeta meta))
                {
                    owner = meta.OwnerName;
                    colorHex = ColorUtility.ToHtmlStringRGB(meta.MarkerColour);
                }
                
                yield return $"{frame},{p.x},{p.y},{p.z},{s},{owner},{colorHex}";
            }
        }
    }

    #endregion
}