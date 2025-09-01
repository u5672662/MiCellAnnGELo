using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loads per‑frame raw volumes or image sequences and applies them to a <see cref="VolumeRenderingManager"/>.
/// Provides simple UI time controls. File parsing kept minimal.
/// </summary>
[RequireComponent(typeof(VolumeRenderingManager))]
public class VolumeTimeSeriesLoader : MonoBehaviour
{
    [Tooltip("Folder containing per-frame raw volumes")] public string folderPath = "";
    public bool loadOnStart = false;
    public Slider timeSlider;
    public TMPro.TMP_Text frameLabel;

    private VolumeRenderingManager volumeManager;
    private List<string> framePaths = new List<string>();
    private int currentFrame = -1;

    public int FrameCount => framePaths.Count;
    public int CurrentFrame => currentFrame;

    [Tooltip("Multiplier applied to the final object scale")] public float scaleMultiplier = 2f;

    private MeshRenderer _meshRenderer;
    private void Awake()
    {
        volumeManager = GetComponent<VolumeRenderingManager>();
        _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer != null)
        {
            _meshRenderer.enabled = false;
        }
    }

    private void Start()
    {
        if (loadOnStart && !string.IsNullOrEmpty(folderPath))
            LoadSeries(folderPath);
    }

    public void LoadSeries(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Debug.LogError($"[VolumeTimeSeriesLoader] Folder not found: {folder}");
            return;
        }

        framePaths = Directory.GetFiles(folder, "*.raw").OrderBy(p => p).ToList();
        if (framePaths.Count == 0)
        {
            Debug.LogError($"[VolumeTimeSeriesLoader] No raw files found in {folder}");
            return;
        }

        if (timeSlider != null)
        {
            timeSlider.wholeNumbers = true;
            timeSlider.minValue = 0;
            timeSlider.maxValue = framePaths.Count - 1;
            timeSlider.onValueChanged.AddListener(OnSliderChanged);
        }

        LoadFrame(0);
    }

    private void OnSliderChanged(float value)
    {
        int idx = Mathf.RoundToInt(value);
        if (idx != currentFrame)
            LoadFrame(idx);
    }

    public void LoadFrame(int index)
    {
        if (index < 0 || index >= framePaths.Count)
            return;

        StopAllCoroutines();
        StartCoroutine(LoadFrameCoroutine(index));
    }

    private IEnumerator LoadFrameCoroutine(int index)
    {
        string rawPath = framePaths[index];
        string metaPath = rawPath + ".meta";
        if (!File.Exists(metaPath))
            metaPath = Path.ChangeExtension(rawPath, ".meta");

        if (!File.Exists(metaPath))
        {
            Debug.LogError($"[VolumeTimeSeriesLoader] Meta file missing for {rawPath}");
            yield break;
        }

        int w = 0, h = 0, d = 0;
        foreach (string line in File.ReadAllLines(metaPath))
        {
            string[] parts = line.Split('=');
            if (parts.Length != 2)
                continue;
            string key = parts[0].Trim().ToLower();
            string val = parts[1].Trim();
            if (key == "width") int.TryParse(val, out w);
            else if (key == "height") int.TryParse(val, out h);
            else if (key == "depth") int.TryParse(val, out d);
        }

        //LoadRawVolume(rawPath, w, h, d);
        currentFrame = index;
        frameLabel?.SetText($"{index + 1} / {framePaths.Count}");
        yield return null;
    }


    public void LoadImageSequenceVolume(string folderPath)
    {
        var dataset = UnityVolumeRendering.VolumeImporter.LoadImageSequence(folderPath);
        ApplyDataset(dataset);
    }


    public void LoadVolume(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            Debug.LogError($"LoadVolume: folder not found -> {folderPath}");
            return;
        }

        // Look for image sequence files
        string[] imageExts = new[] { "*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff" };
        foreach (string ext in imageExts)
        {
            if (Directory.GetFiles(folderPath, ext).Length > 0)
            {
                LoadImageSequenceVolume(folderPath);
                return;
            }
        }

        Debug.LogError($"LoadVolume: No supported volume data found in {folderPath}");
    }

    private bool TryReadMeta(string metaPath, out int width, out int height, out int depth)
    {
        width = height = depth = 0;
        if (!File.Exists(metaPath))
            return false;

        try
        {
            string[] lines = File.ReadAllLines(metaPath);
            foreach (string line in lines)
            {
                string[] parts = line.Split('=');
                if (parts.Length != 2)
                    continue;

                string key = parts[0].Trim().ToLower();
                string value = parts[1].Trim();

                switch (key)
                {
                    case "width":
                        int.TryParse(value, out width);
                        break;
                    case "height":
                        int.TryParse(value, out height);
                        break;
                    case "depth":
                        int.TryParse(value, out depth);
                        break;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error reading meta file {metaPath}: {e.Message}");
            return false;
        }

        return width > 0 && height > 0 && depth > 0;
    }

    private void ApplyDataset(UnityVolumeRendering.VolumeDataset dataset)
    {
        if (dataset == null || volumeManager.volumeMaterial == null)
            return;

        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        Texture3D tex = dataset.GetDataTexture();
        volumeManager.volumeMaterial.SetTexture("_VolumeTexture", tex);
        if (_meshRenderer != null)
        {
            _meshRenderer.material = volumeManager.volumeMaterial;
            _meshRenderer.enabled = true;
        }
        volumeManager.SetVisible(true);

        // Scale object to dataset aspect ratio
        transform.localScale = dataset.scale / Mathf.Max(dataset.scale.x, Mathf.Max(dataset.scale.y, dataset.scale.z)) * scaleMultiplier;
        transform.rotation = dataset.rotation;
        Debug.Log($"[VolumeRenderingManager] Dataset applied. Scale set to {transform.localScale}, rotation {transform.rotation.eulerAngles}");
    }

    public void ClearData()
    {
        StopAllCoroutines();
        framePaths.Clear();
        currentFrame = -1;
        if (volumeManager != null && volumeManager.volumeMaterial != null)
        {
            volumeManager.volumeMaterial.SetTexture("_VolumeTexture", null);
        }
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer != null)
        {
            _meshRenderer.material = null;
            _meshRenderer.enabled = false;
        }
        volumeManager?.SetVisible(false);
    }
}

