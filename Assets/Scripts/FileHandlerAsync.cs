using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System;
using System.Text;
using UnityEngine.UI;
using System.Threading.Tasks;

public class FileHandlerAsync : MonoBehaviour
{
    /*
    public Slider progressBar;
    public Text progressText;
    public GameObject loadTimeseries, loadAnnotations,
                  saveAnnotations, updateColours;
    private bool fileError;
    private MeshLoader meshController;
    private Mesh[] meshes;
    private Color32[,][] meshColours;
    private float timeOfLastUpdate = 0f;
    // Stores the colour mode (0 = original, 1 = patches, 2 = scaled, 3 = overlay)
    private enum colorMode { original, patches, scaled, overlay };

    private void Start()
    {

    }

    private int GetFileNo(string filePath)
    {
        // Search the filename for a number
        string num = "";
        int meshIndex = 0;
        // Gather each number of the filename into a string
        for (int i = 0; i < filePath.Length; i++)
        {
            if (filePath[i] == Path.DirectorySeparatorChar)
                num = "";
            if (char.IsDigit(filePath[i]))
                num += filePath[i];
        }
        // Convert the string to an int and minus 1 to make an index
        if (num.Length > 0)
            meshIndex = int.Parse(num) - 1;
        return meshIndex;
    }

    public void LoadTimeseries(GameObject cell, string dirPath)
    {
        Debug.Log("Loading time series");
        MeshLoader meshController = cell.GetComponent<MeshLoader>();
        fileError = false;
        LoadMeshesAsync(meshController, dirPath);
        //Task.Run(() => LoadMeshesAsync(meshController, dirPath));
    }

    public void LoadTimeseriesAnnotation(string dirPath, int nFrames)
    {
        HashSet<int>[] markers = new HashSet<int>[nFrames];
        if (meshes != null)
        {
            // Stop any coroutines that may interfere
            meshController.StopPlayback();
            // Check if path is a single .csv, otherwise expects a folder
            if (dirPath.EndsWith(".csv"))
            {
                try
                {
                    markers = ImportMarkers(dirPath, nFrames);
                }
                catch (Exception)
                {
                    SetProgressError("Failed to load markers.");
                }
            }
            else
                StartCoroutine(ImportColours(dirPath));
        }
        meshController.markers = markers;
    }

    public void SaveAnnotations(string dirPath)
    {
        if (meshes != null)
        {
            bool error = false;
            meshController.StopPlayback();
            try
            {
                ExportMarkers(dirPath, meshController.markers);
            }
            catch (Exception)
            {
                error = true;
                SetProgressError("Failed to export markers to file.");
            }
            if (!error)
                StartCoroutine(ExportColours(dirPath));
        }
    }

    private async void LoadMeshesAsync(MeshLoader meshController, string dirPath)
    {
        // Disable UI buttons during load
        SetButtonState(false);
        // Clear current timeseries
        meshController.ClearTimeseries();
        // Get the filepaths to any .PLYs in dirPath
        string[] frames = Directory.GetFiles(dirPath, "*.ply");
        int nFrames = frames.Length;
        Debug.Log("Number of frames to load:" + nFrames);
        // Reset loading bar
        UpdateProgressFraction(0, nFrames, "Loading");
        if (nFrames > 0)
        {
            // Otherwise load meshes from files
            // Initialise all arrays
            meshes = new Mesh[nFrames];
            meshColours = new Color32[nFrames, 4][];
            HashSet<int>[] markers = new HashSet<int>[nFrames];
            // TODO: replace this line
            //currentFrameText.text = "Currently displaying frame: 0 / " + nFrames;
            // Loop through each file to import
            for (int i = 0; i < nFrames; i++)
            {
                // Get the mesh's index from the filename
                int meshIndex = GetFileNo(frames[i]);
                Debug.Log("Loading frame " + meshIndex);
                // Create a new marker list
                markers[i] = new HashSet<int>();
                // Import file
                fileError = await LoadMeshAsync(frames[i], meshIndex);
                // Check for exception
                if (fileError)
                {
                    // Show error message
                    SetProgressError("Error loading meshes.");
                    // Renable buttons
                    SetButtonState(true);
                    // Stop import coroutine
                    return;
                }
                // Change progress bar
                UpdateProgressFraction(i, nFrames, "Loading");
                // Display just loaded mesh
            }
            SetProgressText("Time series loaded");
            meshController.meshColors = meshColours;
            meshController.meshes = meshes;
            meshController.DisplayMesh();
        }
        else
        {
            SetProgressError("No time series files found in directory");
        }
        // Renable buttons
        SetButtonState(true);
    }

    private async Task<bool> LoadMeshAsync(string filename, int meshIndex)
    {
        // load the mesh from a ply file (2-channel only)
        Mesh meshIn = await ReadPLYAsync(filename);
        int vCount = meshIn.vertexCount;
        var triCount = meshIn.triangles.Length;
        // Initialise mesh property lists
        var triangles = new int[triCount * 2];
        var vertices = new Vector3[vCount * 2];

         // Check number of vertices and triagles found
        if (vCount == 0 || triCount == 0)
        {
            // Flag error
            fileError = true;
            // Stop import
            return fileError;
        }
        // Initialise meshColours
        for (int i = 0; i < 4; i++)
        {
            meshColours[meshIndex, i] = new Color32[vCount * 2];
        }
        Debug.Log("Converting to game meshes.");
        // Loop through vertex lines
        for (int i = 0; i < vCount; i++)
        {
            // Split line into word strings
            // Set positions for both internal and external vertices
            // Get base colour of vertex
            var colour = meshIn.colors32[i];
            // Set colours
            for (int j = 0; j < 2; j++)
            {
                vertices[i + j * vCount] = meshIn.vertices[i];
                meshColours[meshIndex, (int)colorMode.original][i + j * vCount] = colour;
                meshColours[meshIndex, (int)colorMode.scaled][i + j * vCount] = colour;
                // Check if blue channel (patch flag) is 0
                if (colour[2] == 0)
                {
                    // Set blue
                    meshColours[meshIndex, (int)colorMode.patches][i + j * vCount] = Color.blue;
                    meshColours[meshIndex, (int)colorMode.overlay][i + j * vCount] = Color.blue;
                }
                else
                {
                    // Set red
                    meshColours[meshIndex, (int)colorMode.patches][i + j * vCount] = Color.red;
                    meshColours[meshIndex, (int)colorMode.overlay][i + j * vCount] = colour;
                }
            }
        }

        // Loop through triangles
        for (int i = 0; i < triCount; i = i + 3)
        {
            // Set triangle vertices
            triangles[i] = meshIn.triangles[i + 0];
            triangles[i + 1] = meshIn.triangles[i + 1];
            triangles[i + 2] = meshIn.triangles[i + 2];
            // Internal triangles have reversed winding order
            triangles[i + triCount] = meshIn.triangles[i + 1];
            triangles[i + 1 + triCount] = meshIn.triangles[i + 0];
            triangles[i + 2 + triCount] = meshIn.triangles[i + 2];
        }
        
        // Create new mesh and set properties
        Mesh mesh = new Mesh();
        // Increase index format to 32bit to allow for needed number of vertices
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        var norms = mesh.normals;
        // Inverse normals for internal vertices
        for (int i = 0; i < vCount; i++)
        {
            norms[i + vCount] = -norms[i];
        }
        // Set normals
        mesh.normals = norms;
        // Set mesh in meshes list
        meshes[meshIndex] = mesh;
        return false;
    }

    private async Task<Mesh> ReadPLYAsync(string filename)
    {
        Debug.Log("Reading ply file");
        // Initialise mesh property lists
        var triangles = new int[0];
        var vertices = new Vector3[0];
        var colors = new Color32[0];
        int vCount = 0;
        var triCount = 0;
        string entireText;
        // output
        Mesh mesh = new Mesh();

        try
        {
            // Read file into a string
            Debug.Log("Opening file");
            using (StreamReader sr = new StreamReader(filename))
            {
                entireText = await sr.ReadToEndAsync();
            }
                //StreamReader stream = File.OpenText(filename);
            //entireText = await stream.ReadToEndAsync();
            Debug.Log("Text read.");
            //stream.Close();
        }
        catch (Exception)
        {
            // Flag error
            fileError = true;
            // Stop import
            return mesh;
        }
        // Update last update time
        timeOfLastUpdate = Time.realtimeSinceStartup;

        Debug.Log("Reading components.");
        using (StringReader reader = new StringReader(entireText))
        {
            string currentText = reader.ReadLine();
            char[] splitIdentifier = { ' ' };
            string[] brokenString;
            Debug.Log("Reading header.");
            // Read past header
            while (currentText != null && !currentText.Equals("end_header"))
            {
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
                            // Initialise vertex and colour arrays
                            vCount = System.Convert.ToInt32(brokenString[2]);
                            vertices = new Vector3[vCount];
                            colors = new Color32[vCount];
                        }
                        else
                        {
                            // Initialise triangle array
                            triCount = System.Convert.ToInt32(brokenString[2]) * 3;
                            triangles = new int[triCount];
                        }
                    }
                    catch (Exception)
                    {
                        // Flag error
                        fileError = true;
                        // Stop import
                        return mesh;
                    }
                }
                // Read next line
                currentText = reader.ReadLine();
            }
            // Check number of vertices and triagles found
            if (vCount == 0 || triCount == 0)
            {
                // Flag error
                fileError = true;
                // Stop import
                return mesh;
            }
            // Read end header
            currentText = reader.ReadLine();
            Debug.Log("Reading vertices and colours");
            // Loop through vertex lines
            for (int i = 0; i < vCount; i++)
            {
                try
                {
                    // Split line into word strings
                    brokenString = currentText.Split(splitIdentifier);
                    // Get vertex vector and convert axes to Unity system
                    vertices[i] = new Vector3(System.Convert.ToSingle(brokenString[0]),
                                               System.Convert.ToSingle(brokenString[2]),
                                               System.Convert.ToSingle(brokenString[1]));
                    // Get base colour of vertex
                    colors[i] = new Color32(System.Convert.ToByte(brokenString[3]),
                        System.Convert.ToByte(brokenString[4]),
                        System.Convert.ToByte(brokenString[5]),
                        255);
                }
                catch (Exception)
                {
                    // Flag error
                    fileError = true;
                    // Stop import
                    return mesh;
                }
                // Read next line
                currentText = reader.ReadLine();
            }
            Debug.Log("Reading faces.");
            // Loop through triangles
            for (int i = 0; i < triCount; i = i + 3)
            {
                // Get vertices for triangle
                brokenString = currentText.Split(splitIdentifier);
                try
                {
                    triangles[i + 0] = System.Convert.ToInt32(brokenString[2]);
                    triangles[i + 1] = System.Convert.ToInt32(brokenString[1]);
                    triangles[i + 2] = System.Convert.ToInt32(brokenString[3]);
                }
                catch (Exception)
                {
                    // Flag error
                    fileError = true;
                    // Stop import
                    return mesh;
                }
                currentText = reader.ReadLine();
            }
        }
        // Increase index format to 32bit to allow for needed number of vertices
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.Clear();
        // Return values in Mesh object
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.colors32 = colors;
        return mesh;
    }

    private HashSet<int>[] ImportMarkers(string filename, int nFrames)
    {
        HashSet<int>[] markers = new HashSet<int>[nFrames];
        // Loop through each frame and create an empty marker set
        for (int i = 0; i < nFrames; i++)
            markers[i] = new HashSet<int>();

        // Read the file into a string
        StreamReader stream = File.OpenText(filename);
        string entireText = stream.ReadToEnd();
        stream.Close();

        using (StringReader reader = new StringReader(entireText))
        {
            string currentLine;
            // Loop through every line of the csv
            while ((currentLine = reader.ReadLine()) != null)
            {
                // Split csv up
                string[] splitString = currentLine.Split(',');
                // Get vertex of marker
                var vert = System.Convert.ToInt32(splitString[1]);
                // Get frame of marker
                var frame = System.Convert.ToInt32(splitString[0]) - 1;
                // Check frame exists then create a marker
                if (frame < nFrames)
                    markers[frame].Add(vert);
            }
        }
        UpdateProgressFraction(1, 1, "Markers loaded");
        return markers;
    }

    private IEnumerator ImportColours(string dirPath)
    {
        // Disable UI buttons
        SetButtonState(false);
        timeOfLastUpdate = Time.realtimeSinceStartup;
        string[] csvs = Directory.GetFiles(dirPath, "*.csv");
        int nFrames = csvs.Length;
        UpdateProgressFraction(0, nFrames, "Loading: ");
        // Set loading bar
        for (int i = 0; i < csvs.Length; i++)
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
                ImportCellColour(csvs[i]);
            }
            catch (Exception)
            {
                // Show error message
                UpdateProgressFraction(i, 0, "Error importing colour to frame number ");
                // Renable buttons
                SetButtonState(true);
                // Stop import coroutine
                yield break;
            }
            // Update loading bar
            UpdateProgressFraction(i, nFrames, "Loading: ");
        }
        SetProgressText("Colourmap loaded");
        meshController.colorModeIndex = (int)colorMode.scaled;
        // Renable buttons
        SetButtonState(true);
        meshController.DisplayMesh();
    }

    private void ImportCellColour(string filename)
    {
        // Get index of the frame the file corresponds to
        int meshIndex = GetFileNo(filename);

        if (meshIndex < meshes.Length)
        {
            // Get number of external vertices
            int noVert = meshes[meshIndex].vertices.Length / 2;

            // Read file to string
            StreamReader stream = File.OpenText(filename);
            string entireText = stream.ReadToEnd();
            stream.Close();

            using (StringReader reader = new StringReader(entireText))
            {
                string currentText;
                int i = -1;
                // Read every line (each line is a vertex) until end of file or vertex limit reached
                while ((currentText = reader.ReadLine()) != null && ++i < noVert)
                {
                    // If patch flag is 1, set vertex to red, otherwise set to blue
                    if (currentText[0] != '0')
                    {
                        var colour = meshColours[meshIndex, (int)colorMode.scaled][i];
                        colour.a = 255;
                        meshColours[meshIndex, (int)colorMode.patches][i] = Color.red;
                        meshColours[meshIndex, (int)colorMode.patches][i + noVert] = Color.red;
                        meshColours[meshIndex, (int)colorMode.overlay][i] = colour;
                        meshColours[meshIndex, (int)colorMode.overlay][i + noVert] = colour;
                    }
                    else
                    {
                        meshColours[meshIndex, (int)colorMode.patches][i] = Color.blue;
                        meshColours[meshIndex, (int)colorMode.patches][i + noVert] = Color.blue;
                        meshColours[meshIndex, (int)colorMode.overlay][i] = Color.blue;
                        meshColours[meshIndex, (int)colorMode.overlay][i + noVert] = Color.blue;
                    }
                    // Increment to next vertex
                }
            }
        }
    }

    private void ExportMarkers(string filename, HashSet<int>[] markers)
    {
        // Create filepath for marker annotations
        filename += Path.DirectorySeparatorChar + "Marker_Annotations.csv";
        // Create the file, or overwrite if the file exists.
        using (FileStream fs = File.Create(filename))
        using (var sr = new StreamWriter(fs))
        {
            // TODO: add headers
            // Loop through each marker
            for (int i = 0; i < markers.Length; i++)
            {
                // Add 1 to i to create frame num for output
                int frame = i + 1;
                // Loop through vertices
                foreach (var vertex in markers[i])
                {
                    // Write marker to csv
                    // Revert to input coordinate system
                    var pos = meshes[i].vertices[vertex] * 100;
                    // TODO: add vertex label
                    sr.WriteLine(string.Format("{0},{1},{2},{3},{4}", frame, vertex, pos.x, pos.z, pos.y));
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
        int nFrames = meshes.Length;
        // Reset progress bar
        UpdateProgressFraction(-1, nFrames, "Saving: ");
        // Check if number of plys = number of frames
        if (frames.Length == nFrames)
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
                    SetProgressError("Failed to export colours of frame " + frameNo + 1);
                    yield break;
                }
                yield return null;
                using (StringReader reader = new StringReader(entireText))
                // Write to temp file
                using (FileStream fs = File.Create(tempPath))
                using (StreamWriter sr = new StreamWriter(fs))
                {
                    // Read/rewrite to past header
                    string currentLine = reader.ReadLine();
                    while (!currentLine.Equals("end_header"))
                    {
                        if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                        {
                            yield return null;
                            timeOfLastUpdate = Time.realtimeSinceStartup;
                        }
                        try
                        {
                            // Write header contents
                            sr.WriteLine(currentLine);
                            currentLine = reader.ReadLine();
                        }
                        catch (Exception)
                        {
                            SetProgressError("Failed to export colours of frame " + frameNo + 1);
                            yield break;
                        }
                    }
                    try
                    {
                        // Write/read end header
                        sr.WriteLine(currentLine);
                        currentLine = reader.ReadLine();
                    }
                    catch (Exception)
                    {
                        SetProgressError("Failed to export colours of frame " + frameNo + 1);
                        yield break;
                    }

                    // Loop through every vertex
                    for (int i = 0; i < meshColours[meshNo, (int)colorMode.patches].Length / 2; i++)
                    {
                        // Yield to maintain framerate
                        if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                        {
                            yield return null;
                            timeOfLastUpdate = Time.realtimeSinceStartup;
                        }
                        // Turn the current lined to a char array
                        char[] charArray = currentLine.ToCharArray();
                        // Replace last character based off 
                        // TODO: adapt for labelmap
                        charArray[charArray.Length - 1] = meshColours[meshNo, (int)colorMode.patches][i].b == 0 ? '1' : '0';
                        // Convert back to string
                        currentLine = new string(charArray);
                        try
                        {
                            // Write line
                            sr.WriteLine(currentLine);
                            currentLine = reader.ReadLine();
                        }
                        catch (Exception)
                        {
                            SetProgressError("Failed to export colours of frame " + frameNo + 1);
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
                        try
                        {
                            sr.WriteLine(currentLine);
                        }
                        catch (Exception)
                        {
                            SetProgressError("Failed to export colours of frame " + frameNo + 1);
                            yield break;
                        }
                    } while ((currentLine = reader.ReadLine()) != null);
                }

                try
                {
                    // Replace original file with the temp file
                    File.Delete(frames[frameNo]);
                    File.Move(tempPath, frames[frameNo]);
                }
                catch (Exception)
                {
                    SetProgressError("Failed to export colours of frame " + frameNo + 1);
                    yield break;
                }

                // Update progress bar
                UpdateProgressFraction(frameNo, nFrames, "Saving: ");
            }
        }
        else
        {
            // Write colour patches to a csv folder
            // Create folder for colour patches
            dirPath += Path.DirectorySeparatorChar + "Colour_Patches";
            try
            {
                Directory.CreateDirectory(dirPath);
            }
            catch (Exception)
            {
                progressBar.value = 0;
                progressText.text = "Failed to export colours";
                yield break;
            }
            // Loop through every mesh
            for (int meshNo = 0; meshNo < meshes.Length; meshNo++)
            {
                // Yield to maintain framerate
                if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                {
                    yield return null;
                    timeOfLastUpdate = Time.realtimeSinceStartup;
                }
                // Create file for the mesh's colour patches
                string filename = dirPath + Path.DirectorySeparatorChar + "Frame" + (meshNo + 1) + ".csv";
                using (FileStream fs = File.Create(filename))
                using (StreamWriter sr = new StreamWriter(fs))
                {
                    // Loop every vertex
                    for (int i = 0; i < meshColours[meshNo, (int)colorMode.patches].Length / 2; i++)
                    {
                        // Yield to maintain framerate
                        if (Time.realtimeSinceStartup - timeOfLastUpdate > 0.01)
                        {
                            yield return null;
                            timeOfLastUpdate = Time.realtimeSinceStartup;
                        }
                        try
                        {
                            // Write colour to the csv
                            // TODO: adapt for labelmap
                            sr.WriteLine(meshColours[meshNo, (int)colorMode.patches][i].b == 0 ? 1 : 0);
                        }
                        catch (Exception)
                        {
                            UpdateProgressFraction(meshNo, 0, "Failed to export colors ");
                            yield break;
                        }
                    }
                }
                // Update progress bar
                UpdateProgressFraction(meshNo, meshes.Length, "Saving: ");
            }
        }
        SetProgressText("Annotations saved");
        SetButtonState(true);
    }

    private void UpdateProgressFraction(int i, int n, string textIn)
    {
        progressBar.value = (float)(i + 1) / n;
        progressText.text = textIn + (i + 1) + " / " + n;
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

    private void SetButtonState(bool active)
    {
        loadTimeseries.SetActive(active);
        loadAnnotations.SetActive(active);
        saveAnnotations.SetActive(active);
        updateColours.SetActive(active);
    }
    */
}
