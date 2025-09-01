using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Helper utilities used by the importers.
    /// </summary>
    internal static class ImporterUtilsInternal
    {
        /// <summary>
        /// Convert dataset orientation from DICOM LPS to Unity's coordinate system.
        /// Current implementation swaps Y and Z axis and applies a -90 degree rotation
        /// around the X axis so that axial slices face forward.
        /// </summary>
        public static void ConvertLPSToUnityCoordinateSpace(VolumeDataset dataset)
        {
            if (dataset == null)
                return;

            Vector3 scale = dataset.scale;
            dataset.scale = new Vector3(scale.x, scale.z, scale.y);
            dataset.rotation = Quaternion.Euler(-90f, 0f, 0f);
        }
    }
}