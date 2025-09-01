using System;
using System.IO;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Supported dataset formats.
    /// </summary>
    public enum DatasetType
    {
        Unknown,
        PARCHG,
        NRRD,
        NIFTI,
        ImageSequence
    }

    /// <summary>
    /// Helpers for determining dataset types from file paths.
    /// </summary>
    public class DatasetImporterUtility
    {
        /// <summary>
        /// Returns the dataset type inferred from a file path's extension.
        /// </summary>
        /// <param name="filePath">Absolute or relative file path.</param>
        /// <returns>Dataset type, or <see cref="DatasetType.Unknown"/> if not recognised.</returns>
        public static DatasetType GetDatasetType(string filePath)
        {
            DatasetType datasetType;

            // Check file extension.
            string extension = Path.GetExtension(filePath);

            if (string.Equals(extension, ".vasp", StringComparison.OrdinalIgnoreCase))
            {
                datasetType = DatasetType.PARCHG;
            }

            else if (string.Equals(extension, ".nrrd", StringComparison.OrdinalIgnoreCase))
            {
                datasetType = DatasetType.NRRD;
            }
            else if (string.Equals(extension, ".nii", StringComparison.OrdinalIgnoreCase))
            {
                datasetType = DatasetType.NIFTI;
            }
            else if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                datasetType = DatasetType.ImageSequence;
            }
            else 
            {
                datasetType = DatasetType.Unknown;
            }
        
            return datasetType;
        }
    }
}
