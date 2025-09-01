using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(VolumeRenderingManager))]
[RequireComponent(typeof(XRMultiplayer.ClientNetworkTransform))]
/// <summary>
/// Handles time-series playback, marker toggling and network sync for volume datasets.
/// Presentation-only; delegates actual loading to frame loaders.
/// </summary>
public class VolumeController : NetworkBehaviour
{
    public Slider progressBar;
    public TMP_Text progressText;
    public TMP_Text currentFrameText;
    public GameObject markerPrefab;
    public GameObject colorMapper;
    
    [Header("Annotations")]
    [SerializeField]
    private MarkerAnnotation[] markerAnnotations;

    public float speedInterval = 0.01f;
    private float _timeBetweenFrames = 0.1f;
    private int _currentFrame;
    private int _nFrames;
    private bool _paused = true;
    private IEnumerator _playCoroutine = null;
    
    private NetworkVariable<int> _networkFrame = new(0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private List<Vector3>[] _markers;
    private List<int>[] _markerColors;

    private Quaternion _startRotation;
    private Vector3 _startPosition;
    private Vector3 _startScale;

    private VolumeTimeSeriesLoader _volumeLoader;
    private TiffTimeSeriesLoader _tiffLoader;
    private ColorHandler _colorHandler;

    private void Start()
    {
        _volumeLoader = GetComponent<VolumeTimeSeriesLoader>();
        _tiffLoader = GetComponent<TiffTimeSeriesLoader>();
        if (colorMapper != null)
            _colorHandler = colorMapper.GetComponent<ColorHandler>();

        _startRotation = transform.rotation;
        _startPosition = transform.position;
        _startScale = transform.localScale;
    }
    
    /// <inheritdoc />
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkFrame.OnValueChanged += OnNetworkFrameChanged;
        if (IsOwner)
        {
            _networkFrame.Value = _currentFrame;
        }
        else
        {
            _currentFrame = _networkFrame.Value;
        }
        DisplayFrame();
    }

    private void OnNetworkFrameChanged(int previous, int next)
    {
        if (!IsOwner)
        {
            _currentFrame = next;
            DisplayFrame();
        }
    }

    private void UpdateNetworkFrame()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                if (_networkFrame.Value != _currentFrame)
                    _networkFrame.Value = _currentFrame;
            }
            else
            {
                UpdateFrameServerRpc(_currentFrame);
            }
        }
        else
        {
            if (_networkFrame.Value != _currentFrame)
                _networkFrame.Value = _currentFrame;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdateFrameServerRpc(int frame)
    {
        if (_networkFrame.Value != frame)
            _networkFrame.Value = frame;
    }
    

    private void EnsureSeries()
    {
        if (_volumeLoader != null)
            _nFrames = _volumeLoader.FrameCount;
        else if (_tiffLoader != null)
            _nFrames = _tiffLoader.FrameCount;

        if (_nFrames <= 0)
            return;
        if (_markers != null && _markers.Length == _nFrames)
            return;

        _markers = new List<Vector3>[_nFrames];
        _markerColors = new List<int>[_nFrames];
        for (int i = 0; i < _nFrames; i++)
        {
            _markers[i] = new List<Vector3>();
            _markerColors[i] = new List<int>();
        }
    }

    /// <summary>
    /// Loads the current frame into the active loader and updates marker visuals.
    /// </summary>
    public void DisplayFrame()
    {
        EnsureSeries();
        UpdateNetworkFrame();
        if (_volumeLoader != null)
            _volumeLoader.LoadFrame(_currentFrame);
        else if (_tiffLoader != null)
            _tiffLoader.LoadFrame(_currentFrame);

        currentFrameText?.SetText($"Currently displaying frame: {_currentFrame + 1} / {_nFrames}");
        ChangeMarkers();

        // Notify MarkerAnnotation components to show only markers for the active frame
        if (markerAnnotations != null)
        {
            for (int i = 0; i < markerAnnotations.Length; i++)
            {
                if (markerAnnotations[i] != null)
                {
                    markerAnnotations[i].ActivateMarker(_currentFrame);
                }
            }
        }
    }

    private void UnloadMarkers()
    {
        // Only destroy markers that this controller created (VolumeMarkerTag);
        // leave MarkerAnnotation markers intact.
        foreach (Transform child in transform)
        {
            if (child != null && child.TryGetComponent(out VolumeMarkerTag _))
            {
                Destroy(child.gameObject);
            }
        }
    }

    private void ChangeMarkers()
    {
        UnloadMarkers();
        if (_markers == null || _currentFrame >= _markers.Length)
            return;
        for (int i = 0; i < _markers[_currentFrame].Count; i++)
        {
            AddMarker(_markers[_currentFrame][i], _markerColors[_currentFrame][i]);
        }
    }

    private void AddMarker(Vector3 pos, int color)
    {
        GameObject markerObject = Instantiate(markerPrefab, transform);
        markerObject.transform.localPosition = pos;
        Renderer mr = markerObject.GetComponent<Renderer>();
        if (_colorHandler != null)
            mr.material.color = _colorHandler.GetMarkerColor(color);

        if (!markerObject.TryGetComponent(out VolumeMarkerTag _))
        {
            markerObject.AddComponent<VolumeMarkerTag>();
        }

        // Ensure a marker identity exists for other systems if needed
        if (!markerObject.TryGetComponent(out global::MarkerMeta _))
        {
            markerObject.AddComponent<global::MarkerMeta>();
        }
    }

    public void ToggleMarker(RaycastHit hit, int colorIndex)
    {
        Vector3 localPos = transform.InverseTransformPoint(hit.point);
        ToggleMarkerLocal(localPos, colorIndex, _currentFrame);

        // Network propagation
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                ToggleMarkerClientRpc(localPos, colorIndex, _currentFrame);
            }
            else
            {
                ToggleMarkerServerRpc(localPos, colorIndex, _currentFrame);
            }
        }
    }

    private void ToggleMarkerLocal(Vector3 localPos, int colorIndex, int frame)
    {
        EnsureSeries();
        int idx = -1;
        if (_markers[frame] != null)
        {
            for (int i = 0; i < _markers[frame].Count; i++)
            {
                if (Vector3.Distance(_markers[frame][i], localPos) < 0.001f)
                {
                    idx = i;
                    break;
                }
            }
        }

        if (idx >= 0)
        {
            _markers[frame].RemoveAt(idx);
            _markerColors[frame].RemoveAt(idx);
            foreach (Transform child in transform)
            {
                if (Vector3.Distance(child.localPosition, localPos) < 0.001f && child.TryGetComponent(out VolumeMarkerTag _))
                {
                    Destroy(child.gameObject);
                    break;
                }
            }
        }
        else
        {
            if (frame == _currentFrame)
            {
                AddMarker(localPos, colorIndex);
            }
            _markers[frame].Add(localPos);
            _markerColors[frame].Add(colorIndex);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleMarkerServerRpc(Vector3 localPos, int colorIndex, int frame, ServerRpcParams rpcParams = default)
    {
        ToggleMarkerLocal(localPos, colorIndex, frame);
        ToggleMarkerClientRpc(localPos, colorIndex, frame);
    }

    [ClientRpc]
    private void ToggleMarkerClientRpc(Vector3 localPos, int colorIndex, int frame, ClientRpcParams rpcParams = default)
    {
        if (IsServer) return;
        ToggleMarkerLocal(localPos, colorIndex, frame);
    }

    public void ResetCellPosition()
    {
        transform.rotation = _startRotation;
        transform.position = _startPosition;
        transform.localScale = _startScale;
    }

    public void ChangeSpeed(float x)
    {
        float ds = x > 0 ? -speedInterval : speedInterval;
        _timeBetweenFrames += ds;
        if (_timeBetweenFrames < ds)
            _timeBetweenFrames = ds;
    }

    public void ToBeginning()
    {
        StopPlayback();
        _currentFrame = 0;
        UpdateNetworkFrame();
        DisplayFrame();
    }

    public void FrameSkip(float x)
    {
        StopPlayback();
        int dt = x > 0 ? 1 : -1;
        _currentFrame += dt;
        if (_nFrames > 0)
            _currentFrame = (_currentFrame + _nFrames) % _nFrames;
        UpdateNetworkFrame();
        DisplayFrame();
    }

    public void PlayPause()
    {
        if (_paused)
        {
            if (_playCoroutine == null)
                _playCoroutine = ChangeFrame();
            StartCoroutine(_playCoroutine);
            _paused = false;
        }
        else
        {
            if (_playCoroutine != null)
                StopCoroutine(_playCoroutine);
            DisplayFrame();
            _paused = true;
        }
    }

    public void StopPlayback()
    {
        if (_playCoroutine != null)
            StopCoroutine(_playCoroutine);
        _paused = true;
        DisplayFrame();
    }

    private IEnumerator ChangeFrame()
    {
        while (true)
        {
            while (_currentFrame < _nFrames)
            {
                DisplayFrame();
                yield return new WaitForSeconds(_timeBetweenFrames);
                _currentFrame++;
                UpdateNetworkFrame();
            }
            _currentFrame = 0;
            UpdateNetworkFrame();
        }
    }

    // Placeholder methods for compatibility with LaserPointer
    public void SetAlpha(RaycastHit hit, float rad) { }
    public void SetColor(RaycastHit hit, int colorIndex, float rad) { }
}
