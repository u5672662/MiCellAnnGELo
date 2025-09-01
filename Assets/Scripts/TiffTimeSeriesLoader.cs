using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using SimpleFileBrowser;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityVolumeRendering;

/// <summary>
/// Loads multi‑frame TIFF volumes (optionally dual‑channel) and feeds them to <see cref="VolumeRenderingManager"/>.
/// Uses reflection for LibTiff.NET access to avoid direct compile‑time dependency here.
/// </summary>
[RequireComponent(typeof(VolumeRenderingManager))]
public class TiffTimeSeriesLoader : NetworkBehaviour
{
    private static readonly int VolumeTexture = Shader.PropertyToID("_VolumeTexture");
    
    int slicesPerFrame;
    int channelCount;
    [Header("Import Settings")]
    [Tooltip("Combine all channels into one volume")]
    public bool combineChannels = false;
    [Tooltip("If true, TIFF pages are ordered slice0_channel0, slice0_channel1, slice1_channel0...")]
    public bool slicesInterleaved = true;
    [Tooltip("Index of the channel to load for the red channel")]
    public int redChannelIndex = 0;
    [Tooltip("Index of the channel to load for the green channel")]
    public int greenChannelIndex = 1;
    
    [Header("Voxel Size (microns)")]
    [Tooltip("Size of a voxel along X in microns")] public float voxelSizeX = 1f;
    [Tooltip("Size of a voxel along Y in microns")] public float voxelSizeY = 1f;
    [Tooltip("Size of a voxel along Z in microns")] public float voxelSizeZ = 1f;

    [Header("UI Elements")]
    public Slider timeSlider;
    public TMP_Text frameLabel;
    [SerializeField] private TMP_Text currentFrameText;
    [SerializeField] private TMP_Text progressText;

    [Header("Debugging")]
    [Tooltip("Enable verbose debug logging")]
    public bool debugLogging;
    public bool loadOnStart;
    [Tooltip("Path to multi-frame TIFF file")]
    public string tiffPath = "";

    [Tooltip("Multiplier applied to the final object scale")] public float scaleMultiplier = 2f;

    private VolumeRenderingManager _volumeManager;
    private MeshRenderer _meshRenderer;
    private VolumeDataset _volumeDataset;
    private object _tiff;
    private Type _tiffType;
    private Type _tiffTagType;
    private Type _fieldValueType;
    private MethodInfo _setDirectoryMethod;
    private int _totalSlices;
    private int _frameCount;
    private int _headerImages;
    private int _width;
    private int _height;
    private int _currentFrame = -1;

    private string _tempPath;

    private float _voxelX = 1f;
    private float _voxelY = 1f;
    private float _voxelZ = 1f;

    public int FrameCount => _frameCount;
    public int CurrentFrame => _currentFrame;
    public bool IsLoading { get; private set; }
    public VolumeDataset LoadedDataset => _volumeDataset;

    public string GetTempTiffPath()
    {
        return _tempPath;
    }

    private void Awake()
    {
        _volumeManager = GetComponent<VolumeRenderingManager>();
        _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer != null)
        {
            _meshRenderer.enabled = false;
        }

        // Initialise voxel spacing from inspector values
        _voxelX = voxelSizeX;
        _voxelY = voxelSizeY;
        _voxelZ = voxelSizeZ;
    }

    private void Start()
    {
        if (loadOnStart && File.Exists(tiffPath))
            StartCoroutine(LoadTiffCoroutine(tiffPath));
    }

    public void LoadTiff(string path)
    {
        StartCoroutine(LoadTiffCoroutine(path));
    }

    public IEnumerator LoadTiffCoroutine(string path)
    {
        if (IsLoading)
        {
            Debug.LogWarning("[TiffTimeSeriesLoader] Already loading a TIFF file.");
            yield break;
        }
        IsLoading = true;
        
        ClearData();
        
        // This is a long-running operation, so we do it in a coroutine.
        // The actual implementation is now inside this coroutine.
        yield return StartCoroutine(LoadTiffInternal(path));

        IsLoading = false;
    }
    
    private IEnumerator LoadTiffInternal(string path)
    {
        // Most of the original LoadTiffInternal logic goes here,
        // but with `yield return null` at key points to avoid freezing.
        bool errorOccurred = false;
        
        try
        {
#if !UNITY_EDITOR && UNITY_ANDROID
        bool useSaf = FileBrowserHelpers.ShouldUseSAFForPath(path);
#else
            bool useSaf = false;
#endif
            if (useSaf)
            {
                try
                {
                    Debug.Log($"[TiffTimeSeriesLoader] Using SAF. Reading bytes for path: {path}");
                    byte[] bytes = FileBrowserHelpers.ReadBytesFromFile(path);
                    _tempPath = Path.Combine(Application.temporaryCachePath, FileBrowserHelpers.GetFilename(path));
                    Debug.Log($"[TiffTimeSeriesLoader] Writing SAF data to temporary path: {_tempPath}");
                    File.WriteAllBytes(_tempPath, bytes);
                    path = _tempPath;
                    Debug.Log($"[TiffTimeSeriesLoader] SAF file successfully cached. New path: {path}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TiffTimeSeriesLoader] Failed to read or cache SAF file: {e.Message}");
                    errorOccurred = true;
                }
            }
            else
            {
                Debug.Log($"[TiffTimeSeriesLoader] Using standard IO for path: {path}");
            }

            if (!errorOccurred && !FileBrowserHelpers.FileExists(path))
            {
                Debug.LogError($"[TiffTimeSeriesLoader] File not found: {path}");
                currentFrameText.text = $"File not found: {path}";
                errorOccurred = true;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[TiffTimeSeriesLoader] An unexpected error occurred during file access: {e.Message}\n{e.StackTrace}");
            currentFrameText.text = "An error occurred during file access.";
            errorOccurred = true;
        }

        if (errorOccurred)
        {
            yield break;
        }
        
        yield return null;

        // Reset voxel spacing at the start of loading
        _voxelX = voxelSizeX;
        _voxelY = voxelSizeY;
        _voxelZ = voxelSizeZ;

        if (debugLogging)
        {
            Debug.Log($"[TiffTimeSeriesLoader] Loading TIFF from {path}");
        }

        currentFrameText.text = $"Loading TIFF from {path}";

        _headerImages = 0;
        _frameCount = 0;

        // Load types from the LibTiff.NET assembly via reflection
        Assembly lib = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name == "BitMiracle.LibTiff.NET")
            {
                lib = asm;
                break;
            }
        }
        
        yield return null;

        if (lib == null)
        {
            Debug.LogError("[TiffTimeSeriesLoader] LibTiff.NET assembly not loaded");
            currentFrameText.text = "LibTiff.NET assembly not loaded";
            yield break;
        }

        _tiffType = lib.GetType("BitMiracle.LibTiff.Classic.Tiff");
        _tiffTagType = lib.GetType("BitMiracle.LibTiff.Classic.TiffTag");
        _fieldValueType = lib.GetType("BitMiracle.LibTiff.Classic.FieldValue");

        if (_tiffType == null || _tiffTagType == null)
        {
            Debug.LogError("[TiffTimeSeriesLoader] Failed to locate LibTiff types");
            currentFrameText.text = "Failed to locate LibTiff types";
            yield break;
        }

        var openMethod = _tiffType.GetMethod("Open", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(string), typeof(string) }, null);
        if (openMethod != null) _tiff = openMethod.Invoke(null, new object[] { path, "r" });
        
        yield return null;
        
        if (_tiff == null)
        {
            Debug.LogError("[TiffTimeSeriesLoader] Unable to open tiff");
            yield break;
        }
        else if (debugLogging)
        {
            Debug.Log("[TiffTimeSeriesLoader] TIFF opened successfully");
        }

        var getField = _tiffType.GetMethod("GetField", new[] { _tiffTagType });
        var tagWidth = Enum.ToObject(_tiffTagType, 256); // IMAGE WIDTH
        var tagHeight = Enum.ToObject(_tiffTagType, 257); // IMAGE HEIGHT

        var tagXRes = System.Enum.ToObject(_tiffTagType, 282); // X RESOLUTION
        var tagYRes = System.Enum.ToObject(_tiffTagType, 283); // Y RESOLUTION
        var xResField = getField.Invoke(_tiff, new object[] { tagXRes }) as System.Array;
        var yResField = getField.Invoke(_tiff, new object[] { tagYRes }) as System.Array;
        
        yield return null;

        {
            var widthField = getField.Invoke(_tiff, new[] { tagWidth }) as Array;
            var heightField = getField.Invoke(_tiff, new[] { tagHeight }) as Array;

            var tagDesc = System.Enum.ToObject(_tiffTagType, 270); // IMAGE DESCRIPTION
            var descField = getField.Invoke(_tiff, new object[] { tagDesc }) as System.Array;

            var tagSpp = Enum.ToObject(_tiffTagType, 277); // SAMPLES PER PIXEL
            if (getField.Invoke(_tiff, new[] { tagSpp }) is Array { Length: > 0 } sppField)
            {
                var spp = (int)_fieldValueType.GetMethod("ToInt")?.Invoke(sppField.GetValue(0), null)!;
                channelCount = Mathf.Max(channelCount, spp);
            }

            if (descField != null && descField.Length > 0)
            {
                string desc = _fieldValueType.GetMethod("ToString")!.Invoke(descField.GetValue(0), null) as string;
                ParseMetadata(desc);
            }

            _width = widthField != null
                ? (int)_fieldValueType.GetMethod("ToInt")!.Invoke(widthField.GetValue(0), null)
                : 0;
            _height = heightField != null
                ? (int)_fieldValueType.GetMethod("ToInt")!.Invoke(heightField.GetValue(0), null)
                : 0;

            if (debugLogging)
            {
                Debug.Log($"[TiffTimeSeriesLoader] Dimensions: {_width}x{_height}");
            }
        }
        
        yield return null;

        if (xResField != null && xResField.Length > 0)
        {
            float xRes = (float)_fieldValueType.GetMethod("ToFloat")!.Invoke(xResField.GetValue(0), null);
            if (xRes != 0f)
            {
                voxelSizeX = 1f / xRes;
                _voxelX = voxelSizeX;
            }

            if (debugLogging)
            {
                Debug.Log($"[TiffTimeSeriesLoader] X resolution: {_voxelX}");
            }
        }
        
        yield return null;

        if (yResField != null && yResField.Length > 0)
        {
            float yRes = (float)_fieldValueType.GetMethod("ToFloat")?.Invoke(yResField.GetValue(0), null)!;
            if (yRes != 0f)
            {
                voxelSizeY = 1f / yRes;
                _voxelY = voxelSizeY;
            }

            if (debugLogging)
            {
                Debug.Log($"[TiffTimeSeriesLoader] Y resolution: {_voxelY}");
            }
        }
        
        yield return null;

        var readDirectory = _tiffType.GetMethod("ReadDirectory");
        _setDirectoryMethod = _tiffType.GetMethod("SetDirectory", new[] { typeof(short) });
        if (_headerImages > 0)
        {
            _totalSlices = _headerImages;
        }
        else
        {
            _totalSlices = 0;
            do
            {
                _totalSlices++;
            } while ((bool)readDirectory.Invoke(_tiff, null));
        }
        
        yield return null;

        if (debugLogging)
        {
            Debug.Log($"[TiffTimeSeriesLoader] Total slices: {_totalSlices}");
        }

        if (_setDirectoryMethod != null) _setDirectoryMethod.Invoke(_tiff, new object[] { (short)0 });

        if (slicesPerFrame <= 0)
            slicesPerFrame = 1;

        if (channelCount <= 0)
            channelCount = 1;

        _frameCount = _totalSlices / (slicesPerFrame * channelCount);

        if (debugLogging)
        {
            Debug.Log(
                $"[TiffTimeSeriesLoader] Frame count: {_frameCount}, Channels: {channelCount}, Slices per frame: {slicesPerFrame}");
            Debug.Log(
                $"[TiffTimeSeriesLoader] Slice order: {(slicesInterleaved ? "slice-interleaved" : "channel-interleaved")}");
        }
        
        yield return null;

        if (combineChannels && channelCount == 1)
        {
            int guessChannels = 2;
            if (_totalSlices % (slicesPerFrame * guessChannels) == 0)
            {
                channelCount = guessChannels;
                _frameCount = _totalSlices / (slicesPerFrame * channelCount);
            }
        }

        if (timeSlider != null)
        {
            timeSlider.wholeNumbers = true;
            timeSlider.minValue = 0;
            timeSlider.maxValue = _frameCount - 1;
            timeSlider.onValueChanged.AddListener(OnSliderChanged);
        }
        
        yield return null;

        if (debugLogging)
        {
            Debug.Log("[TiffTimeSeriesLoader] TIFF loaded. Initializing first frame");
        }

        currentFrameText.text = "TIFF loaded. Initializing first frame.";
        
        if (_frameCount > 0)
        {
            LoadFrame(0);
        }
        else
        {
            Debug.LogWarning("[TiffTimeSeriesLoader] No frames to load.");
        }
    }

    private void OnSliderChanged(float value)
    {
        int idx = Mathf.RoundToInt(value);
        if (idx != _currentFrame)
            LoadFrame(idx);
    }

    public void LoadFrame(int index)
    {
        if (index < 0 || index >= _frameCount)
            return;

        if (debugLogging)
        {
            Debug.Log($"[TiffTimeSeriesLoader] Queue frame {index} for loading");
        }
        progressText.text = $"Frame {index}";

        StopAllCoroutines();
        StartCoroutine(LoadFrameCoroutine(index));
    }

    private new void OnDestroy()
    {
        if (_tiff == null || _tiffType == null) return;
        var dispose = _tiffType.GetMethod("Dispose");
        dispose?.Invoke(_tiff, null);
#if !UNITY_EDITOR && UNITY_ANDROID
        if (!string.IsNullOrEmpty(_tempPath) && File.Exists(_tempPath))
        {
            File.Delete(_tempPath);
        }
#endif
    }

    private void ParseMetadata(string desc)
    {
        if (string.IsNullOrEmpty(desc))
            return;

        string[] lines = desc.Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim().ToLower();
            if (line.StartsWith("channels=") && int.TryParse(line.Substring(9), out int ch))
                channelCount = ch;
            else if (line.StartsWith("slices=") && int.TryParse(line.Substring(7), out int sl))
                slicesPerFrame = sl;
            else if (line.StartsWith("frames=") && int.TryParse(line.Substring(7), out int fr))
                _frameCount = fr;
            else if (line.StartsWith("images=") && int.TryParse(line.Substring(7), out int imgs))
                _headerImages = imgs;
            else if (line.StartsWith("spacing=") && float.TryParse(line.Substring(8), NumberStyles.Float, CultureInfo.InvariantCulture, out float sp))
            {
                _voxelZ = sp;
                voxelSizeZ = sp;
            }
            else if (line.StartsWith("xspacing=") && float.TryParse(line.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out float xsp))
            {
                _voxelX = xsp;
                voxelSizeX = xsp;
            }
            else if (line.StartsWith("yspacing=") && float.TryParse(line.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out float ysp))
            {
                _voxelY = ysp;
                voxelSizeY = ysp;
            }
        }

        if (debugLogging)
        {
            Debug.Log($"[TiffTimeSeriesLoader] Metadata parsed -> channels: {channelCount}, slices: {slicesPerFrame}, frames: {_frameCount}, header images: {_headerImages}");
        }
        currentFrameText.text = $"Metadata parsed -> channels: {channelCount}, slices: {slicesPerFrame}, frames: {_frameCount}, header images: {_headerImages}";
    }

    private IEnumerator LoadFrameCoroutine(int index)
    {
        var texDepth = slicesPerFrame;
        var redVoxels = new int[_width * _height * texDepth];
        var greenVoxels = new int[_width * _height * texDepth];

        if (debugLogging)
        {
            Debug.Log($"[TiffTimeSeriesLoader] Loading frame {index}. Depth: {texDepth}, Width: {_width}, Height: {_height}, Interleaved: {slicesInterleaved}");
        }

        var getField = _tiffType.GetMethod("GetField", new[] { _tiffTagType });
        var tagBps = Enum.ToObject(_tiffTagType, 258); // BITS PER SAMPLE
        if (getField != null)
        {
            var bpsField = getField.Invoke(_tiff, new [] { tagBps }) as Array;
            var bitsPerSample = 8;
            if (bpsField is { Length: > 0 })
                bitsPerSample = (int)_fieldValueType.GetMethod("ToInt")!.Invoke(bpsField.GetValue(0), null);
            var bytesPerPixel = Mathf.Max(1, bitsPerSample / 8);
            var scanlineSize = (int)_tiffType.GetMethod("ScanlineSize")!.Invoke(_tiff, null);
            var scanline = new byte[scanlineSize];

            for (var z = 0; z < texDepth; z++)
            {
                for (var c = 0; c < channelCount; c++)
                {
                    int page = slicesInterleaved
                        ? index * slicesPerFrame * channelCount + z * channelCount + c
                        : index * slicesPerFrame * channelCount + c * slicesPerFrame + z;
                    if (debugLogging && z == 0 && c == 0)
                    {
                        Debug.Log($"[TiffTimeSeriesLoader] Using page order -> first page index {page}");
                    }
                    if (page >= _totalSlices)
                        continue;

                    _setDirectoryMethod.Invoke(_tiff, new object[] { (short)page });

                    for (int y = 0; y < _height; y++)
                    {
                        _tiffType.GetMethod("ReadScanline", new[] { typeof(byte[]), typeof(int) })!
                            .Invoke(_tiff, new object[] { scanline, y });
                        for (int x = 0; x < _width; x++)
                        {
                            int srcIndex = x * bytesPerPixel;
                            ushort value = bytesPerPixel >= 2
                                ? (ushort)(scanline[srcIndex] | (scanline[srcIndex + 1] << 8))
                                : scanline[srcIndex];

                            int dst = z * _width * _height + y * _width + x;
                            if (combineChannels)
                            {
                                redVoxels[dst] += value;
                            }
                            else
                            {
                                if (c == redChannelIndex)
                                    redVoxels[dst] = value;
                                else if (c == greenChannelIndex)
                                    greenVoxels[dst] = value;
                            }
                        }

                        if (y % 16 == 0)
                            yield return null;
                    }
                }
            }
        }

        if (combineChannels)
        {
            for (int i = 0; i < redVoxels.Length; i++)
            {
                redVoxels[i] /= channelCount;
            }
        }

        _volumeDataset = new VolumeDataset
        {
            datasetName = Path.GetFileName(tiffPath),
            data = redVoxels,
            data2 = greenVoxels,
            dimX = _width,
            dimY = _height,
            dimZ = texDepth,
            isMultiChannel = !combineChannels,
            scale = new Vector3(voxelSizeX, voxelSizeY, voxelSizeZ)
        };
        
        _volumeManager.ImportVolumeDataset(_volumeDataset);

        float sx = _width * _voxelX;
        float sy = _height * _voxelY;
        float sz = texDepth * _voxelZ;
        if (debugLogging)
        {
            Debug.Log($"[TiffTimeSeriesLoader] Voxel sizes -> X:{_voxelX}, Y:{_voxelY}, Z:{_voxelZ}");
        }

        float maxDim = Mathf.Max(sx, Mathf.Max(sy, sz));
        transform.localScale = new Vector3(
            sx / maxDim,
            sy / maxDim,
            sz / maxDim
        ) * scaleMultiplier;
        if (debugLogging)
        {
            Debug.Log($"[TiffTimeSeriesLoader] Set object scale to {transform.localScale}");
        }
        _currentFrame = index;
        frameLabel?.SetText($"{index + 1} / {_frameCount}");
        progressText.text = $"{index + 1} / {_frameCount}";

        // Trigger validation after loading is complete
        var validator = FindFirstObjectByType<DualChannelValidator>();
        if(validator != null)
        {
            validator.ValidateSystem();
        }
    }

    public void ClearData()
    {
        StopAllCoroutines();
        _currentFrame = 0;
        _frameCount = 0;
        if (_volumeManager != null && _volumeManager.volumeMaterial != null)
        {
            _volumeManager.volumeMaterial.SetTexture(VolumeTexture, null);
        }
        if (_meshRenderer != null)
        {
            _meshRenderer.material = null;
            _meshRenderer.enabled = false;
        }
        _volumeManager?.SetVisible(false);
    }
}
