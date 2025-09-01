using System.Collections;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(MeshLoader))]
[RequireComponent(typeof(XRMultiplayer.ClientNetworkTransform))]
public class MeshController : NetworkBehaviour
{
    public GameObject colorMapper;
    public float speedInterval = 0.01f;

    [SerializeField]
    private MarkerAnnotation[] markerAnnotations;

    [Header("UI Elements")]
    public Slider timeSlider;
    public TMP_Text frameLabel;

    private MeshLoader _loader;
    private ColorHandler _colorHandler;

    private float _timeBetweenFrames = 0.1f;
    private int _currentFrame;
    private int _nFrames;
    private bool _paused = true;
    private IEnumerator _playCoroutine = null;
    
    private NetworkVariable<int> _networkFrame = new(0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private Quaternion _startRotation;
    private Vector3 _startPosition;
    private Vector3 _startScale;

    public event System.Action<int> OnFrameChanged;

    private void Start()
    {
        _loader = GetComponent<MeshLoader>();
        if (colorMapper != null)
            _colorHandler = colorMapper.GetComponent<ColorHandler>();

        _startRotation = transform.rotation;
        _startPosition = transform.position;
        _startScale = transform.localScale;
        _nFrames = _loader.NFrames;

        if (timeSlider != null)
        {
            timeSlider.wholeNumbers = true;
            timeSlider.minValue = 0;
            timeSlider.maxValue = _nFrames > 0 ? _nFrames - 1 : 0;
            timeSlider.onValueChanged.AddListener(OnSliderChanged);
        }

        OnFrameChanged += UpdateMarkerAnnotationsFrame;
        UpdateFrameUI();
    }
    
    private void Update()
    {
        if (_loader != null && _loader.NFrames != _nFrames)
        {
            DisplayFrame();
        }
    }
    
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
        if (IsOwner && _networkFrame.Value != _currentFrame)
        {
            _networkFrame.Value = _currentFrame;
        }
    }

    private void EnsureSeries()
    {
        int newFrameCount = _loader.NFrames;
        if (newFrameCount != _nFrames)
        {
            _nFrames = newFrameCount;
            if (timeSlider != null)
            {
                timeSlider.maxValue = _nFrames > 0 ? _nFrames - 1 : 0;
            }
        }
    }

    public void DisplayFrame()
    {
        EnsureSeries();
        if (_loader.NFrames == 0 || _loader.meshes == null)
        {
            Debug.LogWarning("No meshes loaded yet. Skipping DisplayFrame.");
            return;
        }
        _loader.CurrentFrame = _currentFrame;
        _loader.DisplayMesh();
        OnFrameChanged?.Invoke(_currentFrame);
        UpdateFrameUI();
    }

    public void OnSliderChanged(float value)
    {
        StopPlayback();
        int frame = Mathf.RoundToInt(value);
        if (frame != _currentFrame)
        {
            _currentFrame = frame;
            UpdateNetworkFrame();
            DisplayFrame();
        }
    }
    
    private void UpdateFrameUI()
    {
        if (timeSlider != null)
        {
            timeSlider.onValueChanged.RemoveListener(OnSliderChanged);
            timeSlider.value = _currentFrame;
            timeSlider.onValueChanged.AddListener(OnSliderChanged);
        }
        if (frameLabel != null)
        {
            frameLabel.text = _nFrames > 0 ? $"{_currentFrame + 1} / {_nFrames}" : "0 / 0";
        }
    }

    public void SwitchColourMode()
    {
        if (_loader.meshes == null)
            return;
        _loader.colorModeIndex++;
        if (_loader.colorModeIndex == _loader.NModes)
            _loader.colorModeIndex = 1;
        DisplayFrame();
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
        if (_loader.meshes == null)
            return;
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
    public void SetAlpha(RaycastHit hit, float rad)
    {
        if (_loader.meshes == null)
            return;
        int noVert = _loader.meshes[_loader.CurrentFrame].vertices.Length / 2;
        rad /= transform.localScale.x;
        Vector3 hitPoint = transform.InverseTransformPoint(hit.point);
        var verts = _loader.meshes[_loader.CurrentFrame].vertices;
        var vertCPatches = _loader.meshColors[_loader.CurrentFrame, (int)MeshLoader.colorMode.labels];
        var vertCScaled = _loader.meshColors[_loader.CurrentFrame, (int)MeshLoader.colorMode.scaled];
        var vertCOverlay = _loader.meshColors[_loader.CurrentFrame, (int)MeshLoader.colorMode.overlay];
        var alpha = System.Convert.ToByte(_loader.GetSliderValue(_loader.alphaSlider, 255f));
        if (alpha != 255)
            _loader.CellMaterial.shader = _loader.TransparentShader;
        for (int vert = 0; vert < noVert; vert++)
        {
            if (Vector3.Distance(verts[vert], hitPoint) < rad)
            {
                vertCPatches[vert].a = alpha;
                vertCPatches[vert + noVert].a = alpha;
                vertCScaled[vert].a = alpha;
                vertCScaled[vert + noVert].a = alpha;
                vertCOverlay[vert].a = alpha;
                vertCOverlay[vert + noVert].a = alpha;
            }
        }
        _loader.DisplayMesh();
    }

    public void SetColor(RaycastHit hit, int colorIndex, float rad)
    {
        if (_loader.meshes == null)
            return;
        if (_loader.colorModeIndex == (int)MeshLoader.colorMode.scaled)
            return;
        int noVert = _loader.meshes[_loader.CurrentFrame].vertices.Length / 2;
        rad /= transform.localScale.x;
        Vector3 hitPoint = transform.InverseTransformPoint(hit.point);
        var verts = _loader.meshes[_loader.CurrentFrame].vertices;
        var vertCPatches = _loader.meshColors[_loader.CurrentFrame, (int)MeshLoader.colorMode.labels];
        var vertCScaled = _loader.meshColors[_loader.CurrentFrame, (int)MeshLoader.colorMode.scaled];
        var vertCOverlay = _loader.meshColors[_loader.CurrentFrame, (int)MeshLoader.colorMode.overlay];
        var vertLabels = _loader.meshLabelIdx[_loader.CurrentFrame];
        var alpha = System.Convert.ToByte(_loader.GetSliderValue(_loader.alphaSlider, 255f));
        if (alpha != 255)
            _loader.CellMaterial.shader = _loader.TransparentShader;
        Color32 color = _colorHandler.GetPaintColor(colorIndex);
        color.a = alpha;
        for (int vert = 0; vert < noVert; vert++)
        {
            if (Vector3.Distance(verts[vert], hitPoint) < rad)
            {
                vertLabels[vert] = colorIndex;
                for (int j = 0; j < 2; j++)
                {
                    vertCPatches[vert + j * noVert] = color;
                    if (colorIndex > 0)
                    {
                        var c2 = vertCScaled[vert];
                        c2.a = alpha;
                        vertCOverlay[vert + j * noVert] = c2;
                    }
                    else
                    {
                        vertCOverlay[vert + j * noVert] = color;
                    }
                }
            }
        }
        _loader.meshColors[_loader.CurrentFrame, (int)MeshLoader.colorMode.labels] = vertCPatches;
        _loader.meshColors[_loader.CurrentFrame, (int)MeshLoader.colorMode.overlay] = vertCOverlay;
        _loader.meshLabelIdx[_loader.CurrentFrame] = vertLabels;
        _loader.DisplayMesh();
    }
    public void ToggleMarker(RaycastHit hit, int colorIndex)
    {
        if (_loader.meshes == null)
            return;
        int noVert = _loader.meshes[_loader.CurrentFrame].vertices.Length / 2;
        int vertex = GetClosestVertexHit(hit);
        vertex = vertex <= noVert ? vertex : vertex - noVert;
        Vector3 pos = _loader.meshes[_loader.CurrentFrame].vertices[vertex];
        if (_loader.markers[_loader.CurrentFrame].Contains(vertex))
        {
            int idx = _loader.markers[_loader.CurrentFrame].IndexOf(vertex);
            _loader.markers[_loader.CurrentFrame].Remove(vertex);
            _loader.markerColors[_loader.CurrentFrame].RemoveAt(idx);
            int i = 0;
            while (i < transform.childCount)
            {
                var marker = transform.GetChild(i++);
                if (marker.localPosition == pos)
                {
                    Destroy(marker.gameObject);
                    break;
                }
            }
        }
        else
        {
            _loader.AddMarker(vertex, colorIndex);
            _loader.markers[_loader.CurrentFrame].Add(vertex);
            _loader.markerColors[_loader.CurrentFrame].Add(colorIndex);
        }
    }

    private int GetClosestVertexHit(RaycastHit hit)
    {
        Vector3 bary = hit.barycentricCoordinate;
        int triIndex = hit.triangleIndex * 3;
        if (bary.x > bary.y)
        {
            if (bary.x > bary.z)
                return _loader.meshes[_loader.CurrentFrame].triangles[triIndex];
            return _loader.meshes[_loader.CurrentFrame].triangles[triIndex + 2];
        }
        if (bary.y > bary.z)
            return _loader.meshes[_loader.CurrentFrame].triangles[triIndex + 1];
        return _loader.meshes[_loader.CurrentFrame].triangles[triIndex + 2];
    }

    private void UpdateMarkerAnnotationsFrame(int frame)
    {
        if (markerAnnotations == null) return;

        foreach (var markerAnnotation in markerAnnotations)
        {
            if (markerAnnotation != null)
            {
                markerAnnotation.ActivateMarker(frame);
            }
        }
    }
}
