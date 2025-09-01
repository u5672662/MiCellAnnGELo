using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Utility functions for converting image pixels to scalar density values.
    /// </summary>
    internal static class DensityHelper
    {
        /// <summary>
        /// Convert an array of colors to integer density values in the range 0-255.
        /// </summary>
        /// <param name="pixels">Array of image pixels.</param>
        /// <returns>Density values extracted from the colors.</returns>
        public static int[] ConvertColorsToDensities(Color[] pixels)
        {
            int[] densities = new int[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                float intensity = pixels[i].grayscale;
                densities[i] = Mathf.RoundToInt(intensity * 255f);
            }
            return densities;
        }
    }
}