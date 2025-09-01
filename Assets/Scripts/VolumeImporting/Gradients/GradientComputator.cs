#if UVR_USE_SIMPLEITK
using itk.simple;
#endif
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Base class for computing voxel gradients for a dataset.
    /// Implementations provide different discrete operators and optional smoothing.
    /// </summary>
    public abstract class GradientComputator
    {
        protected int[] data;
        protected int dimX, dimY, dimZ;

        protected GradientComputator(VolumeDataset dataset, bool smootheDataValues)
        {
            this.data = dataset.data;
            this.dimX = dataset.dimX;
            this.dimY = dataset.dimY;
            this.dimZ = dataset.dimZ;
            
            if (smootheDataValues)
            {
#if UVR_USE_SIMPLEITK
                Image image = new Image((uint)dimX, (uint)dimY, (uint)dimZ, PixelIDValueEnum.sitkFloat32);

                for (uint z = 0; z < dimZ; z++)
                {
                    for (uint y = 0; y < dimY; y++)
                    {
                        for (uint x = 0; x < dimX; x++)
                        {
                            float value = data[x + y * dimX + z * (dimX * dimY)];
                            image.SetPixelAsFloat(new VectorUInt32() { x, y, z }, value);
                        }
                    }
                }

                BilateralImageFilter filter = new BilateralImageFilter();
                image = filter.Execute(image);

                this.data = new int[data.Length];
                IntPtr imgBuffer = image.GetBufferAsFloat();
                
                // We need to convert from float pointer to int array.
                float[] floatData = new float[data.Length];
                Marshal.Copy(imgBuffer, floatData, 0, data.Length);
                for(int i = 0; i < data.Length; i++)
                {
                    this.data[i] = (int)floatData[i];
                }
#else
                Debug.LogWarning("SimpleITK is required to generate smooth gradients.");
#endif
            }
        }
        /// <summary>
        /// Computes a normalised gradient vector at the voxel index.
        /// </summary>
        public abstract Vector3 ComputeGradient(int x, int y, int z, float minValue, float maxRange);
    }

    /// <summary>
    /// Factory for concrete gradient computators.
    /// </summary>
    public static class GradientComputatorFactory
    {
        public static GradientComputator CreateGradientComputator(VolumeDataset dataset, GradientType gradientType)
        {
            return gradientType switch
            {
                GradientType.CentralDifference => new CentralDifferenceGradientComputator(dataset, false),
                GradientType.SmoothedCentralDifference => new CentralDifferenceGradientComputator(dataset, true),
                GradientType.Sobel => new SobelGradientComputator(dataset, false),
                GradientType.SmoothedSobel => new SobelGradientComputator(dataset, true),
                _ => throw new NotImplementedException(),
            };
        }
    }
}
