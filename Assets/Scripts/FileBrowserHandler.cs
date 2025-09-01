using System.Linq;
using SimpleFileBrowser;
using UnityEngine;

/// <summary>
/// Wraps SimpleFileBrowser flows to load/save series and annotations, and to start TIFF imports.
/// Coordinates showing/hiding mesh and volume loaders only; no IO here.
/// </summary>
public class FileBrowserHandler : MonoBehaviour
{
	public GameObject cell;
	public GameObject window;
	public GameObject notesDisplay;
	public GameObject dataMenu;
	public GameObject fileHandler;
	public VolumeRenderingManager volumeManager;
	public TiffTimeSeriesLoader tiffLoader;

        private FileHandler _fileHandler;

        private void HideMeshCell()
        {
            if (cell != null)
            {
                var loader = cell.GetComponent<MeshLoader>();
                if (loader != null)
                {
                    loader.ClearTimeseries();
                }
                cell.SetActive(false);
            }
        }

        private void HideVolumeCell()
        {
            if (tiffLoader != null)
            {
                tiffLoader.ClearData();
                tiffLoader.gameObject.SetActive(false);
            }
            // RAW loader removed
            if (volumeManager != null)
            {
                volumeManager.volumeMaterial?.SetTexture("_VolumeTexture", null);
            }
        }

	private static bool UseSaf(string path)
	{
		#if !UNITY_EDITOR && UNITY_ANDROID
			return FileBrowserHelpers.ShouldUseSAF;
		#else
			return false;
		#endif
	}

    private void Start()
    {
        window.SetActive(false);
        if (tiffLoader != null)
        {
            tiffLoader.gameObject.SetActive(false);
        }
        // RAW/DICOM loaders removed
		
		// Check if cell reference is set
		if (cell == null)
		{
		    Debug.LogError("Cell GameObject reference is not set in FileBrowserHandler. Please assign it in the Inspector.");
		    return;
		}
		
		// Get FileHandler from cell GameObject
		_fileHandler = fileHandler.GetComponent<FileHandler>();
		if (_fileHandler == null)
		{
		    Debug.LogError("FileHandler component not found on cell GameObject. Please add it to the cell GameObject.");
		}
    }
    
    //####################
    //#   Time Series    #
    //####################
        
    public void ShowLoadTimeSeriesFileBrowser()
	{
		// Check if the necessary components exist
		if (cell == null)
		{
		    Debug.LogError("Cannot open file browser: cell GameObject reference is missing.");
		    return;
		}
		
		if (_fileHandler == null)
		{
		    Debug.LogError("Cannot open file browser: FileHandler component is missing from cell GameObject.");
		    return;
		}
		// Show a select folder dialog
		FileBrowser.ShowLoadDialog(LoadTimeseries, Cancel, FileBrowser.PickMode.Folders, false, null, null, "Select folder to load data from", "Load");
		//PositionWindow();
	}

        private void LoadTimeseries(string[] paths)
        {
                // Use the first path returned to load a time series
                if (paths.Length > 0)
                {
                        HideVolumeCell();
                        if (cell != null)
                                cell.SetActive(true);
                        string dirPath = paths[0];
			bool useSaf = UseSaf(dirPath);
			Debug.Log($"Selected directory: {dirPath} (SAF: {useSaf})");
			
			bool dirExists = useSaf ?
				FileBrowserHelpers.DirectoryExists(dirPath) :
				System.IO.Directory.Exists(dirPath);

			if (!dirExists)
			{
				// On some devices, DirectoryExists may fail for SAF paths.
				Debug.LogWarning("Directory check failed for: " + dirPath);
			}

			int plyCount = 0;
			if (useSaf)
			{
				var entries = FileBrowserHelpers.GetEntriesInDirectory(dirPath, true);
				if (entries == null)
				{
					Debug.LogError("Failed to access directory via SAF: " + dirPath);
					return;
				}

				plyCount += entries.Count(entry => entry is { IsDirectory: false, Extension: ".ply" });
			}
			else
			{
				var plyFiles = System.IO.Directory.GetFiles(dirPath, "*.ply");
				plyCount = plyFiles.Length;
			}

			Debug.Log($"Directory contains {plyCount} .ply files");

			if (plyCount == 0)
			{
				Debug.LogError("No .ply files found in the selected directory: " + dirPath);
				return;
			}
            
            // Add null checks before accessing components
            if (notesDisplay != null)
            {
                string notesDir = paths[0] + "/notes";
                DisplayNotes displayNotes = notesDisplay.GetComponent<DisplayNotes>();
                if (displayNotes != null)
                {
                    displayNotes.LoadDataNotes(notesDir);
                }
            }
                
            if (cell != null)
            {
                FileHandler cellFileHandler = fileHandler.GetComponent<FileHandler>();
                if (cellFileHandler != null)
                {
                    cellFileHandler.LoadTimeseries(paths[0]);
                }
                else
                {
                    Debug.LogError("FileHandler component not found on cell object");
                }
            }
            else
            {
                Debug.LogError("cell object is null - please assign it in the inspector");
            }
		}
	}
	
	//####################
	//#   Annotations    #
	//####################

    public void ShowLoadAnnotationFileBrowser()
    {
		// Show a select dialog using .csv filter
		FileBrowser.SetFilters(false, ".csv");
		FileBrowser.SetDefaultFilter(".csv");
        FileBrowser.ShowLoadDialog(LoadAnnotation, Cancel, 
	        FileBrowser.PickMode.FilesAndFolders, false, null, null, "Select folder or .csv to load annotations from", "Load");
		//PositionWindow();
	}

	private void LoadAnnotation(string[] paths)
	{
		// Load annotations using the first filepath returned
		if (paths.Length > 0)
		{
			cell.GetComponent<FileHandler>().LoadTimeseriesAnnotation(paths[0]);
		}
	}

    public void ShowSaveAnnotationFileBrowser()
	{
		// Show a save dialog
		FileBrowser.ShowSaveDialog(SaveAnnotation, Cancel, FileBrowser.PickMode.Folders, false, null, null, "Select folder to save to");
		//PositionWindow();
	}

    private void SaveAnnotation(string[] paths)
	{
		// Save annotations using the first filepath returned
		if (paths.Length > 0)
		{
			cell.GetComponent<FileHandler>().SaveAnnotations(paths[0]);
		}
	}

	// Empty function purely for cancel
    private void Cancel()
    {
            if (dataMenu != null)
            {
                    dataMenu.SetActive(true);
            }
    }

    //####################
	//#  Volume Loaders  #
	//####################
	
    public void ShowLoadTiffFileBrowser()
    {
        FileBrowser.SetFilters(false, ".tif", ".tiff");
        FileBrowser.SetDefaultFilter(".tif");
        FileBrowser.ShowLoadDialog(OnTiffSelected, Cancel, FileBrowser.PickMode.Files, false, null, null, "Select .tif file", "Load");
    }

    private void OnTiffSelected(string[] paths)
    {
        if (paths.Length > 0)
        {
            HideMeshCell();
            if (tiffLoader != null)
                tiffLoader.gameObject.SetActive(true);

            // Route the call through FileHandler
            if (_fileHandler != null)
            {
                _fileHandler.StartTiffImport(paths[0]);
            }
            else
            {
                Debug.LogError("FileHandler reference not set in FileBrowserHandler.");
            }
        }
        else
        {
            Debug.LogWarning("[FileBrowserHandler] No .tif selected.");
        }
    }
    
    // DICOM file browser removed
    
    // RAW file browser removed
}
