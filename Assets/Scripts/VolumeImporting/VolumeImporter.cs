using System.IO;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Static helpers to import on-disk datasets into runtime <see cref="VolumeDataset"/>s.
    /// </summary>
    public static class VolumeImporter
    {
        /// <summary>
        /// Loads the first valid image sequence dataset from a folder.
        /// </summary>
        /// <param name="folder">Folder containing an image sequence.</param>
        /// <returns>The loaded dataset, or null if none found.</returns>
        public static VolumeDataset LoadImageSequence(string folder)
        {
            IImageSequenceImporter importer = new ImageSequenceImporter();
            var files = Directory.GetFiles(folder);
            var series = importer.LoadSeries(files, new ImageSequenceImportSettings());
            foreach (var s in series)
            {
                VolumeDataset ds = importer.ImportSeries(s, new ImageSequenceImportSettings());
                Debug.Log($"[VolumeImporter] Image sequence dataset loaded: {ds?.dimX}x{ds?.dimY}x{ds?.dimZ}");
                return ds;
            }
            Debug.LogError("[VolumeImporter] No image sequence found in folder: " + folder);
            return null;
        }
    }
}
