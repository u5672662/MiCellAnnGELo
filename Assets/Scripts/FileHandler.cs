using SimpleFileBrowser;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// Orchestrates importing mesh time series or TIFF volumes, synchronising across networked clients,
/// updating UI feedback, and saving/loading annotations. File IO is handled via SimpleFileBrowser when needed.
/// </summary>
public class FileHandler : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private bool verboseLogging = false;

    private int clientFramesLoaded = 0;
    // References to mesh scripts
    [SerializeField] private MeshController meshController;
    [SerializeField] private MeshLoader meshLoader;
    [SerializeField] private TiffTimeSeriesLoader tiffLoader;

    public GameObject[] loadTimeseriesButtons,
        loadAnnotationsButtons,
        saveAnnotationsButtons,
        updateColoursButtons,
        toggleUIButtons;
    
    // Reference to the GameObject containing the ColorHandler component
    public GameObject colorMapper;
    
    // UI Components
    public Slider progressBar;
    public TMP_Text progressText;
    public TMP_Text currentFrameText;
    
    // Components
    private ColorHandler colorHandler;
    [SerializeField] private SupabaseInteractor supabaseInteractor;
    [SerializeField] private AnnotationManager annotationManager;
    
    // File loading variables
    private HashSet<int> labelSet;
    private List<int> labelList;
    private string fileErrorMessage = "";
    private bool fileError = false;
    private float timeOfLastUpdate;
    private readonly float frameCutoff = 0.01f;
    
    private static int SafeParseInt(string input)
    {
        if (long.TryParse(input, out var val))
        {
            if (val > int.MaxValue) return int.MaxValue;
            if (val < int.MinValue) return int.MinValue;
            return (int)val;
        }
        return 0;
    }

    private static byte SafeParseByte(string input)
    {
        if (int.TryParse(input, out var val))
        {
            if (val < byte.MinValue) val = byte.MinValue;
            else if (val > byte.MaxValue) val = byte.MaxValue;
            return (byte)val;
        }
        return 0;
    }
    
    private bool UseSAF(string path)
    {
#if !UNITY_EDITOR && UNITY_ANDROID
        return FileBrowserHelpers.ShouldUseSAF;
#else
        return false;
#endif
    }
    
    private void Awake()
    {
        if (updateColoursButtons != null)
        {
            foreach (var button in updateColoursButtons)
            {
                if (button != null)
                {
                    var uiButton = button.GetComponent<Button>();
                    if (uiButton != null)
                    {
                        uiButton.onClick.AddListener(() => meshLoader.UpdateColours());
                    }
                }
            }
        }

        // Automatically grab MeshLoader if the reference is missing
        if (meshLoader == null)
        {
            if (verboseLogging) Debug.Log("[FileHandler] MeshLoader reference was not set. Attempting to find it on the same GameObject.");
            meshLoader = FindAnyObjectByType<MeshLoader>();
        }

        if (colorMapper != null)
        {
            colorHandler = colorMapper.GetComponent<ColorHandler>();
            if (colorHandler == null)
            {
                Debug.LogError("ColorHandler component not found on the colorMapper GameObject.", this);
            }
        }
        else
        {
            Debug.LogError("ColorMapper GameObject is not assigned in the Inspector.", this);
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            // When a new client connects, if a timeseries is already loaded,
            // we need to tell them to start loading it.
            if (meshLoader != null && meshLoader.NFrames > 0)
            {
                // Find the original path used to load the data. This assumes it's stored somewhere accessible.
                // For simplicity, this example assumes we might need a networked variable or another mechanism
                // to store the 'dirPath' used to load the current dataset.
                // Let's assume for now we cannot re-trigger the load automatically and it must be manual.
                // A more robust solution would be to sync the loaded state.
                Debug.Log($"Client {clientId} connected. A timeseries is loaded, but automatic sync for new clients is not fully implemented.");

                // A potential implementation:
                // string loadedPath = GetCurrentLoadedPath(); // Placeholder for getting the path
                // if (!string.IsNullOrEmpty(loadedPath))
                // {
                //     ClientRpcParams clientRpcParams = new ClientRpcParams
                //     {
                //         Send = new ClientRpcSendParams
                //         {
                //             TargetClientIds = new ulong[]{ clientId }
                //         }
                //     };
                //     LoadTimeseriesClientRpc(loadedPath, clientRpcParams);
                // }
            }
        }
    }
    
    private int GetFileNo(string filePath)
    {
        // Extract the filename without extension to avoid digits from the directory path
        string decodedPath = System.Uri.UnescapeDataString(filePath);
        string fileName = Path.GetFileNameWithoutExtension(decodedPath);
        if (string.IsNullOrEmpty(fileName))
        {
            // SAF paths may not be compatible with Path helpers, attempt manual extraction
            int lastSlash = decodedPath.LastIndexOf('/') + 1;
            if (lastSlash > 0 && lastSlash < filePath.Length)
            {
                fileName = decodedPath.Substring(lastSlash);
                int dot = fileName.IndexOf('.');
                if (dot >= 0)
                    fileName = fileName.Substring(0, dot);
            }
        }

        // Gather digits from the filename
        string num = "";
        foreach (char c in fileName)
        {
            if (char.IsDigit(c))
                num += c;
        }

        int meshIndex = 0;
        if (num.Length > 0)
            meshIndex = SafeParseInt(num) - 1;
        if (verboseLogging)
        {
            Debug.Log($"GetFileNo: path={filePath}, name={fileName}, digits={num}, index={meshIndex}");
        }
        return meshIndex;
    }

    public void LoadTimeseries(string dirPath)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                LoadTimeseriesInternal(dirPath);
                // Don't send the directory path to clients - they will download files individually
                // LoadTimeseriesClientRpc(dirPath);
            }
            else
            {
                LoadTimeseriesServerRpc(dirPath);
            }
        }
        else
        {
            LoadTimeseriesInternal(dirPath);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void LoadTimeseriesServerRpc(string path)
    {
        LoadTimeseriesInternal(path);
        LoadTimeseriesClientRpc(path);
    }
    
    [ClientRpc]
    private void DownloadFrameClientRpc(string fileName)
    {
        if (IsServer)
            return;
        string dest = Path.Combine(Application.persistentDataPath, fileName);
        supabaseInteractor?.DownloadSigned(fileName, dest);
    }
    
    [ClientRpc]
    private void DownloadFrameClientRpc(string fileName, int meshIndex)
    {
        if (IsServer)
            return;
        
        // Ensure the mesh arrays are initialized for the required size
        if (meshLoader.meshes == null)
        {
            Debug.LogError($"DownloadFrameClientRpc: meshLoader.meshes is null! Cannot download mesh {meshIndex}");
            return;
        }
        
        if (meshLoader.meshes.Length <= meshIndex)
        {
            Debug.LogError($"DownloadFrameClientRpc: meshIndex {meshIndex} is out of bounds for array length {meshLoader.meshes.Length}");
            return;
        }
        
        int fileIndex = GetFileNo(fileName);
        Debug.Log($"GetFileNo: path={fileName}, name={Path.GetFileNameWithoutExtension(fileName)}, digits={fileIndex:D4}, index={meshIndex}");
        
        string dest = Path.Combine(Application.persistentDataPath, fileName);
        Debug.Log($"[FileHandler] Downloading from Supabase with key: {fileName}, dest: {dest}, meshIndex: {meshIndex}");
        
        supabaseInteractor?.DownloadSigned(fileName, dest, () =>
        {
            Debug.Log($"ImportMesh start: {dest} -> index {meshIndex}");
            StartCoroutine(ImportMeshAndDisplay(dest, meshIndex));
        });
    }
    
    private IEnumerator ImportMeshAndDisplay(string filePath, int meshIndex)
    {
        // Reset global error flag before import
        fileError = false;
        fileErrorMessage = "";
        
        // Pre-initialize essential arrays if needed
        if (meshLoader.meshColors == null)
        {
            Debug.LogError($"meshColors array is null before importing mesh {meshIndex}");
            fileError = true;
            fileErrorMessage = "meshColors array not initialized";
            yield break;
        }
        
        // Ensure the meshLabelIdx array is initialized for this mesh
        if (meshLoader.meshLabelIdx == null || meshIndex >= meshLoader.meshLabelIdx.Length)
        {
            Debug.LogError($"meshLabelIdx array not properly sized for mesh {meshIndex}");
            fileError = true;
            fileErrorMessage = "meshLabelIdx array not properly initialized";
            yield break;
        }
        
        yield return StartCoroutine(ImportMesh(filePath, meshIndex));
        
        if (!fileError && meshLoader.meshes != null && meshIndex < meshLoader.meshes.Length && meshLoader.meshes[meshIndex] != null)
        {
            // Set label colors for this mesh
            SetLabelColors(meshIndex);
            
            // Update progress UI
            if (meshLoader.nFrames > 0)
            {
                progressBar.value = (float)(meshIndex + 1) / meshLoader.nFrames;
                progressText.text = $"Downloaded: {meshIndex + 1} / {meshLoader.nFrames}";
                currentFrameText.text = $"Currently displaying frame: {meshIndex + 1} / {meshLoader.nFrames}";
            }
            
            // Display the mesh locally first (safe, no network messages)
            meshLoader.CurrentFrame = meshIndex;
            meshLoader.DisplayMeshLocal(meshIndex);
            
            // Now that colors are ready, safe to broadcast to other clients if we're the server
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
            {
                // Send display command to other clients now that this mesh is fully ready
                meshLoader.DisplayMeshClientRpc(meshIndex);
            }
            
            Debug.Log($"Mesh {meshIndex} imported and displayed with {meshLoader.nVertices[meshIndex]} vertices");

            // Check if all frames are loaded on the client
            if (!IsServer)
            {
                clientFramesLoaded++;
                if (clientFramesLoaded >= meshLoader.NFrames)
                {
                    Debug.Log("Client has loaded all frames. Resetting to frame 0.");
                    meshLoader.CurrentFrame = 0;
                    meshLoader.DisplayMeshLocal(0);
                }
            }
        }
        else
        {
            Debug.LogError($"Failed to import and display mesh at index {meshIndex}. fileError={fileError}, meshes array null={meshLoader.meshes == null}");
            if (meshLoader.meshes != null && meshIndex < meshLoader.meshes.Length)
            {
                Debug.LogError($"Mesh at index {meshIndex} is null={meshLoader.meshes[meshIndex] == null}");
            }
        }
    }

    [ClientRpc]
    private void InitializeTimeseriesClientRpc(int frameCount)
    {
        if (IsServer)
            return;
            
        Debug.Log($"[FileHandler] Client: Initializing timeseries with {frameCount} frames");
        
        // Reset the frame counter
        clientFramesLoaded = 0;
        
        // Initialize label collections on the client
        labelSet = new HashSet<int>();
        labelList = new List<int>();

        // Clear and setup timeseries on client
        meshLoader.ClearTimeseries();
        meshLoader.CreateTimeseries(frameCount);
        
        // Verify initialization
        if (meshLoader.meshColors != null)
        {
            Debug.Log($"meshColors array initialized: [{meshLoader.meshColors.GetLength(0)}, {meshLoader.meshColors.GetLength(1)}]");
        }
        else
        {
            Debug.LogError("meshColors array is null after CreateTimeseries!");
        }
        
        // Reset loading bar
        progressBar.value = 0;
        progressText.text = $"Waiting for meshes: 0 / {frameCount}";
        currentFrameText.text = $"Currently displaying frame: 0 / {frameCount}";
    }

    [ClientRpc]
    private void LoadCompleteClientRpc()
    {
        if (IsServer)
            return;
            
        Debug.Log("[FileHandler] Client: All meshes loaded");
        progressText.text = "Time series loaded";
        
        SetButtonState(true);
    }
    
    [ClientRpc]
    private void LoadTimeseriesClientRpc(string path)
    {
        if (IsServer)
            return;
        LoadTimeseriesInternal(path);
    }

    private void LoadTimeseriesInternal(string dirPath)
    {
        if (annotationManager != null)
        {
            annotationManager.ClearAllAnnotations();
        }

        // Check if meshController is null before starting
        if (meshController == null)
        {
            Debug.LogError("MeshController reference is null in FileHandler.LoadTimeseries");
            return;
        }
        
        // Stop any current coroutines that may interfere
        StopCoroutines();
        fileError = false;
        fileErrorMessage = "";
        
        bool useSaf = UseSAF(dirPath);
        if (verboseLogging)
        {
            Debug.Log($"LoadTimeseries called with path {dirPath} (SAF: {useSaf})");
        }

        bool dirExists = useSaf ?
            FileBrowserHelpers.DirectoryExists(dirPath) :
            Directory.Exists(dirPath);
        if (verboseLogging)
        {
            Debug.Log("Directory exists: " + dirExists);
        }


        if (!dirExists)
        {
            Debug.LogWarning("Directory check failed for: " + dirPath);
        }
        
        // Check for TIFF files first
        if (useSaf)
        {
            var entries = FileBrowserHelpers.GetEntriesInDirectory(dirPath, false);
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (!entry.IsDirectory && (entry.Extension == ".tif" || entry.Extension == ".tiff"))
                    {
                        Debug.Log("Found TIFF file, starting volume import process.");
                        StartCoroutine(ImportTiffVolume(entry.Path));
                        return; // Exit after starting TIFF import
                    }
                }
            }
        }
        else
        {
            string[] tiffFiles = Directory.GetFiles(dirPath, "*.tif");
            if (tiffFiles.Length == 0) tiffFiles = Directory.GetFiles(dirPath, "*.tiff");

            if (tiffFiles.Length > 0)
            {
                Debug.Log("Found TIFF file, starting volume import process.");
                StartCoroutine(ImportTiffVolume(tiffFiles[0]));
                return; // Exit after starting TIFF import
            }
        }
        
        int plyCount = 0;
        SimpleFileBrowser.FileSystemEntry[] plyEntries = null;

        if (useSaf)
        {
            var entries = FileBrowserHelpers.GetEntriesInDirectory(dirPath, true);
            if (entries == null)
            {
                Debug.LogError("Failed to access directory via SAF: " + dirPath);
                return;
            }

            List<SimpleFileBrowser.FileSystemEntry> list = new List<SimpleFileBrowser.FileSystemEntry>();
            foreach (var entry in entries)
            {
                if (!entry.IsDirectory && entry.Extension == ".ply")
                {
                    list.Add(entry);
                }
            }

            plyEntries = list.ToArray();
            plyCount = plyEntries.Length;
        }
        else
        {
            string[] frames = Directory.GetFiles(dirPath, "*.ply");
            plyCount = frames.Length;
        }
        if (verboseLogging)
        {
            Debug.Log($"Found {plyCount} PLY files in directory");
        }
        
        if (plyCount == 0)
        {
            Debug.LogError("No PLY files found in directory: " + dirPath);
            return;
        }
        
        if (meshLoader != null && meshLoader.gameObject != null)
        {
            meshLoader.gameObject.SetActive(true);
        }

        if (tiffLoader != null && tiffLoader.gameObject != null)
        {
            tiffLoader.gameObject.SetActive(false);
        }
        
        StartCoroutine(ImportMeshes(dirPath, plyEntries));
    }

    private IEnumerator ImportMeshes(string dirPath, SimpleFileBrowser.FileSystemEntry[] safEntries = null)
    {
        // Disable UI buttons during load
        SetButtonState(false);
        if (verboseLogging)
        {
            Debug.Log($"ImportMeshes starting. Directory: {dirPath}, SAF entries: {(safEntries != null)}");
        }
        string[] frames = null;
        SimpleFileBrowser.FileSystemEntry[] fileEntries = safEntries;
        int frameCount;
        if (fileEntries != null)
        {
            frameCount = fileEntries.Length;
        }
        else
        {
            frames = Directory.GetFiles(dirPath, "*.ply");
            frameCount = frames.Length;
        }
        if (verboseLogging)
        {
            Debug.Log($"ImportMeshes found {frameCount} frames");
        }
        
        if (frameCount > 0)
        {
            labelSet = new HashSet<int>();
            labelList = new List<int>();

            if (meshLoader == null)
            {
                Debug.LogError("[FileHandler] MeshLoader reference is not set in the Inspector! Please assign it on the GameObject with the FileHandler script.", this.gameObject);
                progressText.text = "Error: MeshLoader not assigned.";
                SetButtonState(true);
                yield break;
            }

            // Clear current timeseries
            meshLoader.ClearTimeseries();
            // Setup timeseries
            meshLoader.CreateTimeseries(frameCount);
            
            // If networked, notify clients to prepare for the timeseries
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
            {
                InitializeTimeseriesClientRpc(frameCount);
            }
            // Reset loading bar
            progressBar.value = 0;
            progressText.text = "Loading: 0 / " + frameCount;
            // Load meshes from files
            currentFrameText.text = "Currently displaying frame: 0 / " + frameCount;
            // Loop through each file to import
            timeOfLastUpdate = Time.realtimeSinceStartup;
            for (int i = 0; i < frameCount; i++)
            {
                string filePath = fileEntries != null ? fileEntries[i].Path : frames[i];
                // Get the mesh's index from the filename
                int meshIndex = GetFileNo(filePath);
                
                if (meshIndex < 0 || meshIndex >= frameCount)
                {
                    Debug.LogWarning($"Mesh index {meshIndex} out of range. Using sequential index {i}");
                    meshIndex = i;
                }
                if (verboseLogging)
                {
                    Debug.Log($"Importing mesh {i+1}/{frameCount}: {filePath}, index {meshIndex}");
                }

                // Import file
                yield return StartCoroutine(ImportMesh(filePath, meshIndex));
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    if (IsServer)
                    {
                        string fileName = Path.GetFileName(filePath);
                        if (string.IsNullOrEmpty(fileName))
                        {
                            // For SAF paths, extract filename from the path
                            int lastSlash = filePath.LastIndexOf('/');
                            if (lastSlash >= 0 && lastSlash < filePath.Length - 1)
                            {
                                fileName = filePath.Substring(lastSlash + 1);
                            }
                            else
                            {
                                fileName = $"mesh_{meshIndex:D4}.ply";
                            }
                        }
                        
                        // URL decode the filename if it contains encoded characters
                        if (fileName.Contains("%"))
                        {
                            fileName = System.Uri.UnescapeDataString(fileName);
                        }
                        
                        // For Android SAF paths that might have additional path components, extract just the filename
                        if (fileName.Contains("/"))
                        {
                            int lastSlash = fileName.LastIndexOf('/');
                            if (lastSlash >= 0 && lastSlash < fileName.Length - 1)
                            {
                                fileName = fileName.Substring(lastSlash + 1);
                            }
                        }
                        
                        Debug.Log($"[FileHandler] Uploading with key: {fileName}");
                        supabaseInteractor?.UploadFile(filePath, fileName, false,
                            () => DownloadFrameClientRpc(fileName, meshIndex));
                    }
                }
                // Check for exception
                if (fileError)
                {
                    meshLoader.ClearTimeseries();
                    // Show error message
                    progressBar.value = 0;
                    progressText.text = fileErrorMessage;
                    // Renable buttons
                    SetButtonState(true);
                    // Stop import coroutine
                    yield break;
                }
                                // Change progress bar
                progressBar.value = (float)(i + 1) / frameCount;
                progressText.text = "Loading: " + (i + 1) + " / " + frameCount;
                // Display just loaded mesh (only if not networked or if we're a client)
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !IsServer)
                {
                    meshLoader.CurrentFrame = meshIndex;
                    meshLoader.DisplayMesh();
                }
                else
                {
                    // Server: just update current frame, don't broadcast display yet
                    // The display will be triggered after upload completion in DownloadFrameClientRpc callback
                    meshLoader.CurrentFrame = meshIndex;
                    meshLoader.DisplayMeshLocal(meshIndex);
                }
                yield return null;
                timeOfLastUpdate = Time.realtimeSinceStartup;
            }
            progressText.text = "Updating labels";
            labelList.AddRange(labelSet);
            labelList.Sort();
            if (labelList.Count > 0)
            {
                for (int i = 0; i < frameCount; i++)
                {
                    int vCount = meshLoader.nVertices[i];
                    for (int j = 0; j < vCount; j++)
                    {
                        meshLoader.meshLabelIdx[i][j] = labelList.IndexOf(meshLoader.meshLabelIdx[i][j]);
                    }
                    SetLabelColors(i);
                    yield return null;
                }
            }
            progressText.text = "Time series loaded";
            
            // If networked and server, notify clients that loading is complete
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
            {
                LoadCompleteClientRpc();
            }
        } 
        else
        {
            progressText.text = "No time series files found in directory";
        }

        // Renable buttons
        SetButtonState(true);
    }

    public void StartTiffImport(string filePath)
    {
        if (meshLoader != null && meshLoader.gameObject != null)
        {
            meshLoader.gameObject.SetActive(false);
        }

        if (tiffLoader != null && tiffLoader.gameObject != null)
        {
            tiffLoader.gameObject.SetActive(true);
        }
        StartCoroutine(ImportTiffVolume(filePath));
    }

    private string SanitizeFileName(string path)
    {
        string fileName = FileBrowserHelpers.GetFilename(path);

        // URL decode, in case it's still encoded
        if (fileName.Contains("%"))
        {
            fileName = System.Uri.UnescapeDataString(fileName);
        }

        // The name might still contain path-like structures from SAF
        int lastSlash = fileName.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            fileName = fileName.Substring(lastSlash + 1);
        }
    
        int lastColon = fileName.LastIndexOf(':');
        if (lastColon >= 0)
        {
            fileName = fileName.Substring(lastColon + 1);
        }

        return fileName;
    }

    private IEnumerator ImportTiffVolume(string filePath)
    {
        if (tiffLoader == null)
        {
            Debug.LogError("[FileHandler] TiffTimeSeriesLoader is not assigned.");
            SetProgressError("Tiff Loader not found.");
            yield break;
        }
        
        if (meshLoader != null && meshLoader.gameObject != null)
        {
            meshLoader.gameObject.SetActive(false);
        }

        if (tiffLoader != null && tiffLoader.gameObject != null)
        {
            tiffLoader.gameObject.SetActive(true);
        }

        // Host loads, uploads, and tells clients to download
        if (IsServer)
        {
            Debug.Log($"[FileHandler] [Host] Starting TIFF import for: {filePath}");
            SetProgressText("Preparing TIFF file for upload...");

            string fileName = SanitizeFileName(filePath);
            string pathToUpload = filePath;

            // On Android, we may need to copy the file to a temp location first
#if !UNITY_EDITOR && UNITY_ANDROID
            if (FileBrowserHelpers.ShouldUseSAFForPath(filePath))
            {
                Debug.Log($"[FileHandler] Using SAF. Caching file before upload.");
                byte[] bytes = FileBrowserHelpers.ReadBytesFromFile(filePath);
                string tempPath = Path.Combine(Application.temporaryCachePath, fileName);
                File.WriteAllBytes(tempPath, bytes);
                pathToUpload = tempPath;
                Debug.Log($"[FileHandler] File cached to {pathToUpload}");
            }
#endif
            
            // If there are no other connected clients, skip upload and load locally for speed
            bool hasOtherClients = NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClientsList != null && NetworkManager.Singleton.ConnectedClientsList.Count > 1;
            if (!hasOtherClients)
            {
                Debug.Log($"[FileHandler] [Host] No other clients connected. Skipping upload of {fileName} and loading locally.");
                SetProgressText("Loading volume locally...");
                yield return StartCoroutine(tiffLoader.LoadTiffCoroutine(pathToUpload));
                yield break;
            }

            Debug.Log($"[FileHandler] [Host] Starting upload of {fileName}...");
            
            // Use the progress callback to update the UI
            Action<float> onUploadProgress = (progress) =>
            {
                progressBar.value = progress;
                progressText.text = $"Uploading: {(int)(progress * 100)}%";
            };

            supabaseInteractor.UploadFile(pathToUpload, fileName, true, () =>
            {
                Debug.Log($"[FileHandler] [Host] Upload complete. Notifying clients to download {fileName}");
                SetProgressText("Upload complete. Notifying clients...");
                DownloadTiffClientRpc(fileName);
                
                // Now that upload is done, load the TIFF locally on the host
                Debug.Log($"[FileHandler] [Host] Loading TIFF locally from {pathToUpload}");
                StartCoroutine(tiffLoader.LoadTiffCoroutine(pathToUpload));
                
            }, onUploadProgress);
            
            // The rest of the logic is now in the upload completion callback
        }
        else
        {
            Debug.LogWarning("[FileHandler] ImportTiffVolume called on a client. This should only be initiated by the host.");
        }
    }

    [ClientRpc]
    private void DownloadTiffClientRpc(string fileName)
    {
        if (IsServer) return;

        Debug.Log($"[FileHandler] [Client] Received request to download TIFF: {fileName}");
        string destPath = Path.Combine(Application.persistentDataPath, fileName);
        
        progressText.text = $"Downloading volume: {fileName}";
        progressBar.value = 0;
        
        Debug.Log($"[FileHandler] [Client] Starting download to: {destPath}");
        supabaseInteractor.DownloadSigned(fileName, destPath, () =>
        {
            if (meshLoader != null && meshLoader.gameObject != null)
            {
                meshLoader.gameObject.SetActive(false);
            }
            
            if (tiffLoader != null && tiffLoader.gameObject != null)
            {
                tiffLoader.gameObject.SetActive(true);
            }
            
            Debug.Log($"[FileHandler] [Client] TIFF downloaded to {destPath}. Now loading volume.");
            progressText.text = "Volume downloaded. Loading...";
            StartCoroutine(tiffLoader.LoadTiffCoroutine(destPath));
            progressText.text = "Volume loaded.";
            progressBar.value = 1;
        });
    }

    private IEnumerator ImportMesh(string filename, int meshIndex)
    {
        if (verboseLogging)
        {
            Debug.Log($"ImportMesh start: {filename} -> index {meshIndex}");
        }
        
        // Local error tracking for this import operation
        bool localFileError = false;
        string localErrorMessage = "";
        
        Color32 paintBackground = colorHandler.GetPaintBackground();
        // Initialise mesh property lists
        var triangles = new int[0];
        var vertices = new Vector3[0];
        int vCount = 0;
        int triCount = 0;
        string entireText = null;
        yield return null;
        timeOfLastUpdate = Time.realtimeSinceStartup;
        
        try
        {
            // Read file into a string
            if (UseSAF(filename))
            {
                entireText = FileBrowserHelpers.ReadTextFromFile(filename);
            }
            else
            {
                using (StreamReader stream = File.OpenText(filename))
                {
                    entireText = stream.ReadToEnd();
                }
            }
        }
        catch (Exception ex)
        {
            // Flag error
            localFileError = true;
            localErrorMessage = "Error occurred while loading ply data for frame " + meshIndex + ": " + ex.Message;
            Debug.LogError($"File reading error: {ex.Message}");
        }

        if (!localFileError)
        {
            yield return null;
        
            // Update last update time
            timeOfLastUpdate = Time.realtimeSinceStartup;
        
            using (StringReader reader = new StringReader(entireText))
            {
                string currentText = reader.ReadLine();
                char[] splitIdentifier = {' '};
                string[] brokenString;

                // Read past header
                while (currentText != null && !currentText.Equals("end_header"))
                {
                    // Yield control if time has passed 0.01s
                    if (Time.realtimeSinceStartup - timeOfLastUpdate > frameCutoff)
                    {
                        yield return null;
                        timeOfLastUpdate = Time.realtimeSinceStartup;
                    }
                    // Check if line contains mesh property info
                    if (currentText.StartsWith("element"))
                    {
                        try
                        {
                            // Break up string into words
                            brokenString = currentText.Split(splitIdentifier);
                            // Check if line describes number of vertices or triangles
                            if (brokenString[1].Equals("vertex"))
                            {
                                // Initialise vertex and color arrays
                                vCount = Convert.ToInt32(brokenString[2]);
                                vertices = new Vector3[vCount * 2];
                                meshLoader.meshLabelIdx[meshIndex] = new int[vCount];
                                // Ensure meshColors array is properly initialized
                                if (meshLoader.meshColors == null)
                                {
                                    Debug.LogError($"meshColors array is null for mesh {meshIndex}");
                                    localFileError = true;
                                    localErrorMessage = $"meshColors array is null for mesh {meshIndex}";
                                    yield break;
                                }
                                
                                if (meshIndex >= meshLoader.meshColors.GetLength(0))
                                {
                                    Debug.LogError($"meshIndex {meshIndex} is out of bounds for meshColors array length {meshLoader.meshColors.GetLength(0)}");
                                    localFileError = true;
                                    localErrorMessage = $"meshIndex {meshIndex} out of bounds";
                                    yield break;
                                }
                                
                                for (int i = 0; i < meshLoader.NModes; i++)
                                {
                                    meshLoader.meshColors[meshIndex, i] = new Color32[2 * vCount];
                                    Debug.Log($"Initialized meshColors[{meshIndex}, {i}] with {2 * vCount} elements");
                                }
                            }
                            else
                            {
                                // Initialise triangle array
                                triCount = SafeParseInt(brokenString[2]);
                                triangles = new int[triCount * 3 * 2];
                            }
                        }
                        catch (Exception ex)
                        {
                            localFileError = true;
                            localErrorMessage = "Error occurred while parsing ply header for frame " + meshIndex + ": " + ex.Message;
                            Debug.LogError($"Header parsing error: {ex.Message}. Line content: '{currentText}'");
                        }
                    }
                    // Read next line
                    currentText = reader.ReadLine();
                } 
                if (verboseLogging)
                {
                    Debug.Log($"Parsed header: vertices={vCount}, triangles={triCount}");
                }
                // Check number of vertices and triagles found
                if (vCount == 0 || triCount == 0)
                {
                    // Flag error
                    localFileError = true;
                    localErrorMessage = "Error: ply data has no vertices or triangles in frame " + meshIndex;
                    Debug.LogError($"Invalid mesh data: vertices={vCount}, triangles={triCount}");
                    // Stop import
                    yield break;
                }
                // Read end header
                currentText = reader.ReadLine();

                // Loop through vertex lines
                for (int i = 0; i < vCount; i++)
                {
                    // Yield control if time has passed 0.01s
                    if (Time.realtimeSinceStartup - timeOfLastUpdate > frameCutoff)
                    {
                        yield return null;
                        timeOfLastUpdate = Time.realtimeSinceStartup;
                    }

                    try
                    {
                        // Split line into word strings
                        brokenString = currentText.Split(splitIdentifier);
                        // Get vertex vector, apply scale factors, and convert axes to Unity system
                        var vertVect = new Vector3(Convert.ToSingle(brokenString[0]) / 100,
                                                   Convert.ToSingle(brokenString[2]) / 100,
                                                   Convert.ToSingle(brokenString[1]) / 100);
                        // Get base color of vertex
                        // TODO: add option to use blue channel as label indicator for legacy functionality, or as a color channel
                        var color = new Color32(SafeParseByte(brokenString[3]), SafeParseByte(brokenString[4]), 0, 255);
                        // set the label index of the vertex
                        // TODO: partially revert to allow backwards compatability, then use the csv output/input to define labels
                        int labelIndex = SafeParseInt(brokenString[5]);
                        meshLoader.meshLabelIdx[meshIndex][i] = labelIndex;
                        // collect label numbers to map to sorted indices later
                        labelSet.Add(labelIndex);

                        for (int j = 0; j < 2; j++)
                        {
                            int k = i + j * vCount;
                            // Set positions for both internal and external vertices
                            vertices[k] = vertVect;
                            // Set colours of both internal and external vertices
                            // Safety check: ensure color arrays are initialized
                            if (meshLoader.meshColors[meshIndex, (int)MeshLoader.colorMode.original] == null)
                            {
                                throw new System.InvalidOperationException($"meshColors array not initialized for mesh {meshIndex}, mode original");
                            }
                            
                            meshLoader.meshColors[meshIndex, (int)MeshLoader.colorMode.original][k] = color;
                            meshLoader.meshColors[meshIndex, (int)MeshLoader.colorMode.scaled][k] = color;
                            // Initially set all label colors to background
                            meshLoader.meshColors[meshIndex, (int)MeshLoader.colorMode.labels][k] = paintBackground;
                            meshLoader.meshColors[meshIndex, (int)MeshLoader.colorMode.overlay][k] = paintBackground;
                        }
                    }
                    catch (Exception ex)
                    {
                        localFileError = true;
                        localErrorMessage = "Error occurred while parsing vertices for frame " + meshIndex + ": " + ex.Message;
                        Debug.LogError($"Vertex parsing error at line {i}: {ex.Message}. Line content: '{currentText}'");
                    }
                    // Read next line
                    currentText = reader.ReadLine();
                }
                
                // Loop through triangles
                // TODO: use edge lengths to inform marker sizes
                for (int i = 0; i < triCount; i++)
                {
                    // Yield control if time has passed 0.01s
                    if (Time.realtimeSinceStartup - timeOfLastUpdate > frameCutoff)
                    {
                        yield return null;
                        timeOfLastUpdate = Time.realtimeSinceStartup;
                    }
                    // Get vertices for triangle
                    brokenString = currentText.Split(splitIdentifier);
                    try
                    {
                        var vert1 = SafeParseInt(brokenString[1]);
                        var vert2 = SafeParseInt(brokenString[2]);
                        var vert3 = SafeParseInt(brokenString[3]);
                        int[] v = { vert2, vert1, vert3 };
                        for (int j = 0; j < 2; j++)
                        {
                            int k = i + j * triCount;
                            for (int l = 0; l < 3; l++)
                            {
                                // Set triangle vertices
                                triangles[3 * k + l] = v[l] + j * vCount;
                            }
                            v[0] = vert1;
                            v[1] = vert2;
                        }
                    }
                    catch (Exception ex)
                    {
                        localFileError = true;
                        localErrorMessage = "Error occurred while parsing triangles for frame " + meshIndex + ": " + ex.Message;
                        Debug.LogError($"Triangle parsing error at line {i}: {ex.Message}. Line content: '{currentText}'");
                    }
                    currentText = reader.ReadLine();
                }
            }
            // Create new mesh and set properties
            Mesh mesh = new Mesh();
            // Increase index format to 32bit to allow for needed number of vertices
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            yield return null;
            timeOfLastUpdate = Time.realtimeSinceStartup;
            mesh.RecalculateNormals();
            var norms = mesh.normals;
            // Inverse normals for internal vertices
            for (int i = 0; i < vCount; i++)
            {
                // Yield control if time has passed 0.01s
                if (Time.realtimeSinceStartup - timeOfLastUpdate > frameCutoff)
                {
                    yield return null;
                    timeOfLastUpdate = Time.realtimeSinceStartup;
                }
                norms[i + vCount] = -norms[i];
            }
            // Set normals
            mesh.normals = norms;
            // Set mesh in meshes list
            meshLoader.meshes[meshIndex] = mesh;
            meshLoader.nVertices[meshIndex] = vCount;
            if (verboseLogging)
            {
                Debug.Log($"Mesh {meshIndex} imported with {vCount} vertices");
            }
            
            // Set global error flags based on local error state
            fileError = localFileError;
            fileErrorMessage = localErrorMessage;
        
            if (!localFileError)
            {
                Debug.Log($"ImportMesh completed successfully for mesh {meshIndex}");
            }
            else
            {
                Debug.LogError($"ImportMesh failed for mesh {meshIndex}: {localErrorMessage}");
            }
        
            // Additional validation
            if (meshLoader.meshes[meshIndex] == null)
            {
                Debug.LogError($"ERROR: Mesh {meshIndex} is null after assignment!");
                fileError = true;
                fileErrorMessage = $"Mesh {meshIndex} is null after assignment";
            }
            else
            {
                Debug.Log($"SUCCESS: Mesh {meshIndex} assigned correctly with {mesh.vertexCount} vertices");
            }
        }
        else
        {
            // If file read failed, set the global error flags here
            fileError = true;
            fileErrorMessage = localErrorMessage;
        }
    }

    public void LoadTimeseriesAnnotation(string dirPath)
    {
        if (meshLoader.meshes != null)
        {
            // Stop any coroutines that may interfere
            meshController.StopPlayback();
            // Check if path is a single .csv, otherwise expects a folder
            if (dirPath.EndsWith(".csv"))
            {
                try
                {
                    ImportMarkers(dirPath);
                }
                catch (Exception)
                {
                    // Wipe markers
                    for (int i = 0; i < meshLoader.markers.Length; i++)
                    {
                        meshLoader.markers[i] = new List<int>();
                        meshLoader.markerColors[i] = new List<int>();
                    }
                    progressBar.value = 0;
                    progressText.text = "Failed to load markers";
                }
            }
            else
                StartCoroutine(ImportColours(dirPath));
        }
    }

    private void ImportMarkers(string filename)
    {
        // Loop through each frame and create an empty marker set
        for (int i = 0; i < meshLoader.markers.Length; i++)
        {
            meshLoader.markers[i] = new List<int>();
            meshLoader.markerColors[i] = new List<int>();
        }
        HashSet<int> markerSet = new HashSet<int>();
        List<int> markerList = new List<int>();
        // Read the file into a string
        string entireText;
        if (UseSAF(filename))
        {
            entireText = FileBrowserHelpers.ReadTextFromFile(filename);
        }
        else
        {
            using (StreamReader stream = File.OpenText(filename))
            {
                entireText = stream.ReadToEnd();
            }
        }

        int iColor = 0;
        int iFrame = 1;
        int iVert = 2;
        using (StringReader reader = new StringReader(entireText))
        {
            string currentLine;
            // Loop through every line of the csv
            while ((currentLine = reader.ReadLine()) != null)
            {
                // Split csv up
                string[] splitString = currentLine.Split(',');
                int colorIndex = 0;
                if (splitString.Length > 5)
                {
                    colorIndex = SafeParseInt(splitString[iColor]);
                }
                else
                {
                    iVert = 1;
                    iFrame = 0;
                }
                markerSet.Add(colorIndex);
                // Get vertex of marker
                var vert = SafeParseInt(splitString[iVert]);
                // Get frame of marker
                var frame = SafeParseInt(splitString[iFrame]) - 1;
                // Check frame exists then create a marker
                if (frame < meshLoader.markers.Length)
                {
                    meshLoader.markers[frame].Add(vert);
                    meshLoader.markerColors[frame].Add(colorIndex);
                }
            }
        }
        markerList.AddRange(markerSet);
        markerList.Sort();
        for(int i = 0; i < meshLoader.NFrames; i++)
        {
            for(int j = 0; j < meshLoader.markers[i].Count; j++)
            {
                // increase value to match colors
                meshLoader.markers[i][j] = markerList.IndexOf(meshLoader.markers[i][j]) + 1;
            }
        }
        progressBar.value = 1;
        progressText.text = "Markers loaded";
        meshLoader.ChangeMarkers();
    }

    private IEnumerator ImportColours(string dirPath)
    {
        // Disable UI buttons
        SetButtonState(false);
        timeOfLastUpdate = Time.realtimeSinceStartup;
        bool useSaf = UseSAF(dirPath);
        string[] csvs = null;
        SimpleFileBrowser.FileSystemEntry[] csvEntries = null;
        if (useSaf)
        {
            var entries = FileBrowserHelpers.GetEntriesInDirectory(dirPath, true);
            if (entries != null)
            {
                List<SimpleFileBrowser.FileSystemEntry> list = new List<SimpleFileBrowser.FileSystemEntry>();
                foreach (var e in entries)
                {
                    if (!e.IsDirectory && e.Extension == ".csv")
                        list.Add(e);
                }
                csvEntries = list.ToArray();
            }
        }
        else
        {
            csvs = Directory.GetFiles(dirPath, "*.csv");
        }
        int csvCount = useSaf ? (csvEntries != null ? csvEntries.Length : 0) : csvs.Length;
        // Set loading bar
        progressBar.value = 0;
        progressText.text = "Loading: 0 / " + csvCount;
        for (int i = 0; i < csvCount; i++)
        {
            // Yield if current update has taken over 0.01s
            if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
            {
                yield return null;
                timeOfLastUpdate = Time.realtimeSinceStartup;
            }
            // Import a frame's colouring
            try
            {
                string filePath = useSaf ? csvEntries[i].Path : csvs[i];
                ImportCellColour(filePath);
            }
            catch (Exception)
            {
                // Show error message
                progressBar.value = 0;
                string errPath = useSaf ? csvEntries[i].Path : csvs[i];
                progressText.text = "Error importing colour to frame number " + (GetFileNo(errPath) + 1);
                // Renable buttons
                SetButtonState(true);
                // Stop import coroutine
                yield break;
            }
            // Update loading bar
            progressBar.value = (float)(i + 1) / csvCount;
            progressText.text = "Loading: " + (i + 1) + " / " + csvCount;
        }
        progressText.text = "Colourmap loaded";
        meshLoader.colorModeIndex = (int)MeshLoader.colorMode.scaled;
        // Renable buttons
        SetButtonState(true);
        meshLoader.DisplayMesh();
    }

    private void ImportCellColour(string filename)
    {
        // Get index of the frame the file corresponds to
        int meshIndex = GetFileNo(filename);

        if (meshIndex < meshLoader.NFrames)
        {
            // Get number of external vertices
            int noVert = meshLoader.meshes[meshIndex].vertices.Length / 2;

            // Read file to string
            string entireText;
            if (UseSAF(filename))
            {
                entireText = FileBrowserHelpers.ReadTextFromFile(filename);
            }
            else
            {
                using (StreamReader stream = File.OpenText(filename))
                {
                    entireText = stream.ReadToEnd();
                }
            }

            using (StringReader reader = new StringReader(entireText))
            {
                string currentText;
                int i = -1;
                // Read every line (each line is a vertex) until end of file or vertex limit reached
                while ((currentText = reader.ReadLine()) != null && ++i < noVert)
                {
                    meshLoader.meshLabelIdx[meshIndex][i] = SafeParseInt(currentText[0].ToString());
                }
            }
            SetLabelColors(meshIndex);
        }
    }

    private void SetLabelColors(int meshIndex)
    {
        int noVert = meshLoader.nVertices[meshIndex];
        for(int i = 0; i < noVert; i++)
        {
            int colorIndex = meshLoader.meshLabelIdx[meshIndex][i];
            Color32 labelColor = colorHandler.GetPaintColor(colorIndex);
            Color32 color = meshLoader.meshColors[meshIndex, (int)MeshLoader.colorMode.scaled][i];
            color.a = 255;
            for (int j = 0; j < 2; j++)
            {
                int k = i + noVert * j;
                // set label color
                meshLoader.meshColors[meshIndex, (int)MeshLoader.colorMode.labels][k] = labelColor;
                // If patch flag is nonzero, set overlay to color, otherwise set to background
                if (colorIndex > 0)
                {
                    meshLoader.meshColors[meshIndex, (int)MeshLoader.colorMode.overlay][k] = color;
                }
                else
                {
                    meshLoader.meshColors[meshIndex, (int)MeshLoader.colorMode.overlay][k] = colorHandler.GetPaintColor(0);
                }
            }
        }
    }

    public void SaveAnnotations(string dirPath)
    {
        if (meshLoader.meshes != null)
        {
            bool error = false;
            meshController.StopPlayback();
            try
            {
                ExportMarkers(dirPath);
            }
            catch (Exception)
            {
                error = true;
                progressBar.value = 0;
                progressText.text = "Failed to export markers to file";
            }
            if (!error)
                StartCoroutine(ExportColours(dirPath));
        }
    }

    private void ExportMarkers(string filename)
    {
        // Create filepath for marker annotations
        filename += Path.DirectorySeparatorChar + "Marker_Annotations.csv";
        // Create the file, or overwrite if the file exists.
        using (FileStream fs = File.Create(filename))
        using (var sr = new StreamWriter(fs))
        {
            // Loop through each marker
            for (int i = 0; i < meshLoader.markers.Length; i++)
            {
                // Add 1 to i to create frame num for output
                int frame = i + 1;
                // Loop through vertices
                for (int j = 0; j < meshLoader.markers[i].Count; j++)
                {
                    // Write marker to csv
                    var pos = meshLoader.meshes[i].vertices[meshLoader.markers[i][j]] * 100;
                    // Revert to input coordinate system
                    sr.WriteLine(string.Format("{0},{1},{2},{3},{4},{5}", meshLoader.markerColors[i][j], frame, meshLoader.markers[i][j], pos.x, pos.z, pos.y));
                }
            }
        }   
    }

    private IEnumerator ExportColours(string dirPath)
    {
        // Set buttons to be inactive
        SetButtonState(false);
        timeOfLastUpdate = Time.realtimeSinceStartup;
        // Get the ply files in the directory
        string[] frames = Directory.GetFiles(dirPath, "*.ply");
        // Reset progress bar
        progressBar.value = 0;
        progressText.text = "Saving: 0 / " + meshLoader.NFrames;
        // Check if number of plys = number of frames
        // TODO: only export to csv
        if (frames.Length == meshLoader.NFrames)
        {
            // Assume plys are source files for loaded timeseries
            string tempPath = dirPath + Path.DirectorySeparatorChar + "temp.ply";
            // Export every frame
            for (int frameNo = 0; frameNo < frames.Length; frameNo++)
            {
                // If update has taken longer than 0.01s yield
                if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                {
                    yield return null;
                    timeOfLastUpdate = Time.realtimeSinceStartup;
                }
                // Get frame number
                int meshNo = GetFileNo(frames[frameNo]);
                // Read existing ply into string
                string entireText;
                try
                {
                    StreamReader stream = File.OpenText(frames[frameNo]);
                    entireText = stream.ReadToEnd();
                    stream.Close();
                }
                catch (Exception)
                {
                    progressBar.value = 0;
                    progressText.text = "Failed to export colours of frame " + (frameNo + 1);
                    yield break;
                }
                yield return null;
                using (StringReader reader = new StringReader(entireText))
                // Write to temp file
                using (FileStream fs = File.Create(tempPath))
                using (StreamWriter sr = new StreamWriter(fs))
                {
                    // Read/rewrite to header
                    string currentLine = reader.ReadLine();
                    while (!currentLine.Equals("end_header"))
                    {
                        if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                        {
                            yield return null;
                            timeOfLastUpdate = Time.realtimeSinceStartup;
                        }
                        try { 
                            // Write header contents
                            sr.WriteLine(currentLine);
                            currentLine = reader.ReadLine();
                        }
                        catch (Exception)
                        {
                            progressBar.value = 0;
                            progressText.text = "Failed to export colours of frame " + (frameNo + 1);
                            yield break;
                        }
                    }
                    try { 
                        // Write/read end header
                        sr.WriteLine(currentLine);
                        currentLine = reader.ReadLine();
                    }
                    catch (Exception)
                    {
                        progressBar.value = 0;
                        progressText.text = "Failed to export colours of frame " + (frameNo + 1);
                        yield break;
                    }

                    // Loop through every vertex
                    for (int i = 0; i < meshLoader.meshColors[meshNo, (int)MeshLoader.colorMode.labels].Length / 2; i++)
                    {
                        // Yield to maintain framerate
                        if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                        {
                            yield return null;
                            timeOfLastUpdate = Time.realtimeSinceStartup;
                        }
                        // change last entry in colors
                        string[] splitLine = currentLine.Split(" ");
                        splitLine[splitLine.Length - 1] = Convert.ToString(meshLoader.meshLabelIdx[meshNo][i]);
                        currentLine = string.Join(" ", splitLine);
                        try { 
                            // Write line
                            sr.WriteLine(currentLine);
                            currentLine = reader.ReadLine();
                        }
                        catch (Exception)
                        {
                            progressBar.value = 0;
                            progressText.text = "Failed to export colours of frame " + (frameNo + 1);
                            yield break;
                        }
                    }
                    // Write the rest of the file to temp
                    do
                    {
                        if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                        {
                            yield return null;
                            timeOfLastUpdate = Time.realtimeSinceStartup;
                        }
                        try { 
                            sr.WriteLine(currentLine);
                        }
                        catch (Exception)
                        {
                            progressBar.value = 0;
                            progressText.text = "Failed to export colours of frame " + (frameNo + 1);
                            yield break;
                        }
                    } while ((currentLine = reader.ReadLine()) != null);
                }

                try {
                    // Replace original file with the temp file
                    File.Delete(frames[frameNo]);
                    File.Move(tempPath, frames[frameNo]);
                }
                catch (Exception)
                {
                    progressBar.value = 0;
                    progressText.text = "Failed to export colours of frame " + (frameNo + 1);
                    yield break;
                }

                // Update progress bar
                progressBar.value = (float)(frameNo + 1) / frames.Length;
                progressText.text = "Saving: " + (frameNo + 1) + " / " + frames.Length;
            }
        } 
        else
        {
            // Write colour labels to a csv folder
            // Create folder for colour labels
            dirPath += Path.DirectorySeparatorChar + "labels";
            try { 
                Directory.CreateDirectory(dirPath);
            }
            catch (Exception)
            {
                progressBar.value = 0;
                progressText.text = "Failed to export colours";
                yield break;
            }
            // Loop through every mesh
            for (int f = 0; f < meshLoader.NFrames; f++)
            {
                // Yield to maintain framerate
                if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                {
                    yield return null;
                    timeOfLastUpdate = Time.realtimeSinceStartup;
                }
                // Create file for the mesh's colour labels
                // TODO: allow adding to file
                // TODO: add header
                string filename = dirPath + Path.DirectorySeparatorChar + "t" + (f + 1) + ".csv";
                using (FileStream fs = File.Create(filename))
                using (StreamWriter sr = new StreamWriter(fs))
                {
                    // Loop every vertex
                    for (int i = 0; i < meshLoader.nVertices[f]; i++)
                    {
                        // Yield to maintain framerate
                        if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                        {
                            yield return null;
                            timeOfLastUpdate = Time.realtimeSinceStartup;
                        }
                        // TODO: add more data to table
                        string[] lineOut = { meshLoader.meshLabelIdx[f][i].ToString() };
                        try {
                            // Write label index to the csv
                            sr.WriteLine(string.Join(",",lineOut));
                        }
                        catch (Exception)
                        {
                            progressBar.value = 0;
                            progressText.text = "Failed to export colours of frame " + (f + 1);
                            yield break;
                        }
                    }
                }
                // Update progress bar
                progressBar.value = (float)(f + 1) / meshLoader.NFrames;
                progressText.text = "Saving: " + (f + 1) + " / " + meshLoader.NFrames;
            }
        }
        progressText.text = "Annotations saved";
        SetButtonState(true);
    }

    private void UpdateProgressFraction(int i, int n, string textIn)
    {
        progressBar.value = (float)(i + 1) / n;
        progressText.text = textIn + ": " + (i + 1) + " / " + n;
    }
    
    private void SetProgressText(string textIn)
    {
        progressBar.value = 1;
        progressText.text = textIn;
    }
    
    private void SetProgressError(string textIn)
    {
        progressBar.value = 0;
        progressText.text = textIn;
    }

    public void StopCoroutines()
    {
        StopAllCoroutines();
        if (meshController != null)
        {
            meshController.StopPlayback();
        }
    }
    

    private void SetButtonsInteractable(GameObject[] buttons, bool active)
    {
        if (buttons == null) return;

        foreach (GameObject btn in buttons)
        {
            if (btn == null) continue;

            var uiButton = btn.GetComponent<Button>();
            if (uiButton != null)
            {
                uiButton.interactable = active;
                continue;
            }

            // Fallback to toggling the GameObject if no Button component exists
            btn.SetActive(active);
        }
    }

    public void SetButtonState(bool active)
    {
        SetButtonsInteractable(loadTimeseriesButtons, active);
        SetButtonsInteractable(loadAnnotationsButtons, active);
        SetButtonsInteractable(saveAnnotationsButtons, active);
        SetButtonsInteractable(updateColoursButtons, active);
        SetButtonsInteractable(toggleUIButtons, active);
    }
    
}