using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityVolumeRendering
{
    /// <summary>
    /// An imported dataset. Contains a 3D pixel array of density values.
    /// </summary>
    [Serializable]
    public class VolumeDataset : ScriptableObject, ISerializationCallbackReceiver
    {
        /// <summary>
        /// File path of the dataset.
        /// This is the path to the file that was imported (if any).
        /// This is saved for user reference, and is not used for loading the data.
        /// </summary>
        public string filePath;
        
        /// <summary>
        /// The data array.
        /// This contains all the density values of the dataset.
        /// The values are stored in an int array, and then converted to float (0.0-1.0) when creating the texture.
        /// The values are stored in Z-major order, so the values for the first Z-slice comes first.
        /// </summary>
        [SerializeField]
        public int[] data;

        [SerializeField]
        public int[] data2 = null;

        [SerializeField]
        public bool isMultiChannel = false;

        /// <summary>
        /// Dimension of the dataset in the X-axis.
        /// </summary>
        [SerializeField]
        public int dimX, dimY, dimZ;

        [SerializeField]
        public Vector3 scale = Vector3.one;

        [SerializeField]
        public Quaternion rotation;
        
        public float volumeScale;

        [SerializeField]
        public string datasetName;

        private float minDataValue = float.MaxValue;
        private float maxDataValue = float.MinValue;

        private float minDataValue2 = float.MaxValue;
        private float maxDataValue2 = float.MinValue;

        private Texture3D dataTexture = null;
        private Texture3D gradientTexture = null;

        private SemaphoreSlim createDataTextureLock = new SemaphoreSlim(1, 1);
        private SemaphoreSlim createGradientTextureLock = new SemaphoreSlim(1, 1);

        [SerializeField, FormerlySerializedAs("scaleX")]
        private float scaleX_deprecated = 1.0f;
        [SerializeField, FormerlySerializedAs("scaleY")]
        private float scaleY_deprecated = 1.0f;
        [SerializeField, FormerlySerializedAs("scaleZ")]
        private float scaleZ_deprecated = 1.0f;

        [System.Obsolete("Use scale instead")]
        public float scaleX { get { return scale.x; } set { scale.x = value; } }
        [System.Obsolete("Use scale instead")]
        public float scaleY { get { return scale.y; } set { scale.y = value; } }
        [System.Obsolete("Use scale instead")]
        public float scaleZ { get { return scale.z; } set { scale.z = value; } }

        [SerializeField]
        public float gradientMax = 1.75f; // Auto-computed maximum gradient magnitude per dataset

        /// <summary>
        /// Gets the 3D data texture, containing the density values of the dataset.
        /// Will create the data texture if it does not exist. This may be slow (consider using <see cref="GetDataTextureAsync"/>).
        /// </summary>
        /// <returns>3D texture of dataset</returns>
        public Texture3D GetDataTexture()
        {
            if (dataTexture == null)
            {
                Debug.Log($"[VolumeDataset] Creating data texture ({dimX}x{dimY}x{dimZ})");
                dataTexture = AsyncHelper.RunSync<Texture3D>(() => CreateTextureInternalAsync(NullProgressHandler.instance));
                return dataTexture;
            }
            else
            {
                Debug.Log("[VolumeDataset] Returning cached data texture");
                return dataTexture;
            }
        }

        public void RecreateDataTexture()
        {
            dataTexture = AsyncHelper.RunSync<Texture3D>(() => CreateTextureInternalAsync(NullProgressHandler.instance));
        }

        /// <summary>
        /// Gets the 3D data texture, containing the density values of the dataset.
        /// Will create the data texture if it does not exist, without blocking the main thread.
        /// </summary>
        /// <param name="progressHandler">Progress handler for tracking the progress of the texture creation (optional).</param>
        /// <returns>Async task returning a 3D texture of the dataset</returns>
        public async Task<Texture3D> GetDataTextureAsync(IProgressHandler progressHandler = null)
        {
            if (dataTexture == null)
            {
                await createDataTextureLock.WaitAsync();
                try
                {
                    if (progressHandler == null)
                        progressHandler = NullProgressHandler.instance;
                    dataTexture = await CreateTextureInternalAsync(progressHandler);
                }
                finally
                {
                    createDataTextureLock.Release();
                }
            }
            return dataTexture;
        }

        /// <summary>
        /// Gets the gradient texture, containing the gradient values (direction of change) of the dataset.
        /// Will create the gradient texture if it does not exist. This may be slow (consider using <see cref="GetGradientTextureAsync" />).
        /// </summary>
        /// <returns>Gradient texture</returns>
        public Texture3D GetGradientTexture()
        {
            if (gradientTexture == null)
            {
                gradientTexture = AsyncHelper.RunSync<Texture3D>(() => CreateGradientTextureInternalAsync(GradientTypeUtils.GetDefaultGradientType(), NullProgressHandler.instance));
                return gradientTexture;
            }
            else
            {
                return gradientTexture;
            }
        }

        public async Task<Texture3D> RegenerateGradientTextureAsync(GradientType gradientType, IProgressHandler progressHandler = null)
        {
            await createGradientTextureLock.WaitAsync();
            try
            {
                if (progressHandler == null)
                    progressHandler = new NullProgressHandler();
                try
                {
                    gradientTexture = await CreateGradientTextureInternalAsync(gradientType, progressHandler != null ? progressHandler : NullProgressHandler.instance);
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
            finally
            {
                createGradientTextureLock.Release();
            }
            return gradientTexture;
        }

        /// <summary>
        /// Gets the gradient texture, containing the gradient values (direction of change) of the dataset.
        /// Will create the gradient texture if it does not exist, without blocking the main thread.
        /// </summary>
        /// <param name="progressHandler">Progress handler for tracking the progress of the texture creation (optional).</param>
        /// <returns>Async task returning a 3D gradient texture of the dataset</returns>
        public async Task<Texture3D> GetGradientTextureAsync(IProgressHandler progressHandler = null)
        {
            if (gradientTexture == null)
            {
                gradientTexture = await RegenerateGradientTextureAsync(GradientTypeUtils.GetDefaultGradientType(), progressHandler);
            }
            return gradientTexture;
        }

        public float GetMinDataValue()
        {
            if (minDataValue == float.MaxValue)
                CalculateValueBounds(new NullProgressHandler());
            return minDataValue;
        }

        public float GetMaxDataValue()
        {
            if (maxDataValue == float.MinValue)
                CalculateValueBounds(new NullProgressHandler());
            return maxDataValue;
        }

        public void RecalculateBounds()
        {
            CalculateValueBounds(new NullProgressHandler());
        }

        /// <summary>
        /// Ensures that the dataset is not too large.
        /// This is automatically called during import,
        ///  so you should not need to call it yourself unless you're making your own importer of modify the dimensions.
        /// </summary>
        public void FixDimensions()
        {
            int MAX_DIM = 2048; // 3D texture max size. See: https://docs.unity3d.com/Manual/class-Texture3D.html

            while (Mathf.Max(dimX, dimY, dimZ) > MAX_DIM)
            {
                Debug.LogWarning("Dimension exceeds limits (maximum: "+MAX_DIM+"). Dataset is downscaled by 2 on each axis!");

                DownScaleData();
            }
        }

        /// <summary>
        /// Downscales the data by averaging 8 voxels per each new voxel,
        /// and replaces downscaled data with the original data
        /// </summary>
        public void DownScaleData()
        {
            int newDimX = dimX / 2;
            int newDimY = dimY / 2;
            int newDimZ = dimZ / 2;
            float ratioX = (float)dimX / newDimX;
            float ratioY = (float)dimY / newDimY;
            float ratioZ = (float)dimZ / newDimZ;

            int[] downscaledData = new int[newDimX * newDimY * newDimZ];
            for (int z = 0; z < newDimZ; z++)
            {
                for (int y = 0; y < newDimY; y++)
                {
                    for (int x = 0; x < newDimX; x++)
                    {
                        downscaledData[x + y * newDimX + z * newDimX * newDimY] = data[(int)(x * ratioX) + (int)(y * ratioY) * dimX + (int)(z * ratioZ) * dimX * dimY];
                    }
                }
            }
            data = downscaledData;

            if (isMultiChannel && data2 != null && data2.Length > 0)
            {
                int[] downscaledData2 = new int[newDimX * newDimY * newDimZ];
                for (int z = 0; z < newDimZ; z++)
                {
                    for (int y = 0; y < newDimY; y++)
                    {
                        for (int x = 0; x < newDimX; x++)
                        {
                            downscaledData2[x + y * newDimX + z * newDimX * newDimY] = data2[(int)(x * ratioX) + (int)(y * ratioY) * dimX + (int)(z * ratioZ) * dimX * dimY];
                        }
                    }
                }
                data2 = downscaledData2;
            }

            dimX = newDimX;
            dimY = newDimY;
            dimZ = newDimZ;
        }

        private void CalculateValueBounds(IProgressHandler progressHandler)
        {
            if (data == null || data.Length == 0)
                return;

            minDataValue = float.MaxValue;
            maxDataValue = float.MinValue;

            progressHandler.StartStage(1.0f, "Calculating value bounds");
            for (int i = 0; i < data.Length; i++)
                    {
                int val = data[i];
                        minDataValue = Mathf.Min(minDataValue, val);
                        maxDataValue = Mathf.Max(maxDataValue, val);
                progressHandler.ReportProgress(i, data.Length);
            }
            progressHandler.EndStage();
            
            if (isMultiChannel && data2 != null)
            {
                minDataValue2 = float.MaxValue;
                maxDataValue2 = float.MinValue;
                progressHandler.StartStage(1.0f, "Calculating value bounds for second channel");
                for (int i = 0; i < data2.Length; i++)
                {
                    int val = data2[i];
                    minDataValue2 = Mathf.Min(minDataValue2, val);
                    maxDataValue2 = Mathf.Max(maxDataValue2, val);
                    progressHandler.ReportProgress(i, data2.Length);
                }
                progressHandler.EndStage();
            }
        }

        private async Task<Texture3D> CreateTextureInternalAsync(IProgressHandler progressHandler)                                        
        {
            if (data == null || data.Length == 0)
            {
                Debug.LogError("Dataset is empty.");
                return null;
            }

            if (minDataValue > maxDataValue)
                RecalculateBounds();

            progressHandler.StartStage(0.8f, "Converting data to texture");

            TextureFormat texformat = isMultiChannel ? 
                (SystemInfo.SupportsTextureFormat(TextureFormat.RGFloat) ? TextureFormat.RGFloat : TextureFormat.RGHalf) :
                (SystemInfo.SupportsTextureFormat(TextureFormat.RHalf) ? TextureFormat.RHalf : TextureFormat.RFloat);

            Texture3D texture = new Texture3D(dimX, dimY, dimZ, texformat, false);
            
            Color[] colors = new Color[data.Length];

            await Task.Run(() =>
            {
                if (isMultiChannel)
                {
                    float range1 = maxDataValue - minDataValue;
                    float range2 = maxDataValue2 - minDataValue2;
                    if (range1 == 0.0f) range1 = 1.0f;
                    if (range2 == 0.0f) range2 = 1.0f;

                    for (int i = 0; i < data.Length; i++)
                    {
                        float val1 = (float)(data[i] - minDataValue) / range1;
                        float val2 = (float)(data2[i] - minDataValue2) / range2;
                        colors[i] = new Color(val1, val2, 0.0f, 0.0f);
                    }
                }
                else
                {
                    float range = maxDataValue - minDataValue;
                    if (range == 0.0f) range = 1.0f;
                    for (int i = 0; i < data.Length; i++)
                    {
                        float val = (float)(data[i] - minDataValue) / range;
                        colors[i] = new Color(val, 0.0f, 0.0f, 0.0f);
                    }
                }
            });

            progressHandler.EndStage();

            progressHandler.StartStage(0.2f, "Uploading texture to GPU");
                texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(colors);
                texture.Apply(false, true);
            progressHandler.EndStage();

            Debug.Log("Texture generation done.");
            return texture;
        }

        private async Task<Texture3D> CreateGradientTextureInternalAsync(GradientType gradientType, IProgressHandler progressHandler)
        {
            if (dataTexture == null)
            {
                Debug.LogError("Data texture is not available, can't create gradient texture.");
                return null;
            }

            Debug.Log("Async gradient texture generation. Hold on.");

            Texture3D.allowThreadedTextureCreation = true;
            TextureFormat texformat = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf) ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat;

            GradientComputator gradientComputator = GradientComputatorFactory.CreateGradientComputator(this, gradientType);

            progressHandler.StartStage(0.8f, "Creating texture");
            NativeArray<Color> colorBuffer = new NativeArray<Color>(dimX * dimY * dimZ, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            float minValue = GetMinDataValue();
            float maxRange = GetMaxDataValue() - minValue;

            await Task.Run(() =>
            {
                Parallel.For(0, dimZ, z =>
                {
                    for (int y = 0; y < dimY; y++)
                    {
                        for (int x = 0; x < dimX; x++)
                        {
                            Vector3 gradient = gradientComputator.ComputeGradient(x, y, z, minValue, maxRange);
                            Vector3 grad = gradient;
                            colorBuffer[x + y * dimX + z * (dimX * dimY)] = new Color(grad.x, grad.y, grad.z, (data[x + y * dimX + z * (dimX * dimY)] - minValue) / maxRange);
                        }
                    }
                });
            });

            // Compute maximum gradient magnitude from the buffer (single-threaded)
            float maxGrad = 0f;
            for (int i = 0; i < colorBuffer.Length; i++)
            {
                Color c = colorBuffer[i];
                float m = Mathf.Sqrt(c.r * c.r + c.g * c.g + c.b * c.b);
                if (m > maxGrad) maxGrad = m;
            }
            gradientMax = Mathf.Max(maxGrad, 1e-4f); // avoid zero

            progressHandler.EndStage();
            progressHandler.StartStage(0.2f, "Applying texture");

            Texture3D texture = new Texture3D(dimX, dimY, dimZ, texformat, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixelData(colorBuffer, 0);
            texture.Apply(false, true);

            progressHandler.EndStage();
            colorBuffer.Dispose();
            Debug.Log("Gradient texture generation done.");

            return texture;
        }

        public float GetAvgerageVoxelValues(int x, int y, int z)
        {
            float total = 0;
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    for (int w = 0; w < 2; w++)
                        total += (float)data[Mathf.Min(x + i, dimX-1) + Mathf.Min(y + j, dimY-1) * dimX + Mathf.Min(z + w, dimZ-1) * (dimX * dimY)];
            return total / 8;
        }

        public float GetData(int x, int y, int z)
        {
            return (float)data[x + y * dimX + z * (dimX * dimY)];
        }

        public void OnBeforeSerialize()
        {
            scaleX_deprecated = scale.x;
            scaleY_deprecated = scale.y;
            scaleZ_deprecated = scale.z;
        }

        public void OnAfterDeserialize()
        {
            scale = new Vector3(scaleX_deprecated, scaleY_deprecated, scaleZ_deprecated);
        }
    }
}
