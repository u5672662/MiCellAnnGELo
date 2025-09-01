using System;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Computes gradients using central differences. Supports optional input smoothing.
    /// </summary>
    public class CentralDifferenceGradientComputator : GradientComputator
    {
        public CentralDifferenceGradientComputator(VolumeDataset dataset, bool smootheDataValues) : base(dataset, smootheDataValues)
        {
        }

        public override Vector3 ComputeGradient(int x, int y, int z, float minValue, float maxRange)
        {
            float x1 = (data[Math.Min(x + 1, dimX - 1) + y * dimX + z * (dimX * dimY)] - minValue) / maxRange;
            float x2 = (data[Math.Max(x - 1, 0) + y * dimX + z * (dimX * dimY)] - minValue) / maxRange;
            float y1 = (data[x + Math.Min(y + 1, dimY - 1) * dimX + z * (dimX * dimY)] - minValue) / maxRange;
            float y2 = (data[x + Math.Max(y - 1, 0) * dimX + z * (dimX * dimY)] - minValue) / maxRange;
            float z1 = (data[x + y * dimX + Math.Min(z + 1, dimZ - 1) * (dimX * dimY)] - minValue) / maxRange;
            float z2 = (data[x + y * dimX + Math.Max(z - 1, 0) * (dimX * dimY)] - minValue) / maxRange;

            return new Vector3((x1 - x2) * 0.5f, (y1 - y2) * 0.5f, (z1 - z2) * 0.5f);
        }
    }
}
