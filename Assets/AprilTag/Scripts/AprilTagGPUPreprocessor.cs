using System;
using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// GPU-accelerated image preprocessing for AprilTag detection
    /// Inspired by PhotonVision's preprocessing pipeline but optimized for Quest GPU
    /// </summary>
    public class AprilTagGPUPreprocessor : IDisposable
    {
        [Serializable]
        public class PreprocessingSettings
        {
            [Header("Adaptive Threshold")]
            [Tooltip("Enable adaptive thresholding for better detection in varying lighting")]
            public bool EnableAdaptiveThreshold = false; // Start disabled - binary output can be too aggressive

            [Range(3, 21)]
            [Tooltip("Block size for adaptive threshold (must be odd)")]
            public int AdaptiveBlockSize = 11;

            [Range(-10f, 10f)]
            [Tooltip("Constant subtracted from weighted mean")]
            public float AdaptiveConstant = 2f;

            [Header("Histogram Equalization")]
            [Tooltip("Enable histogram equalization for contrast enhancement")]
            public bool EnableHistogramEqualization = true; // Working feature - enabled

            [Range(0f, 1f)]
            [Tooltip("Strength of histogram equalization (0 = none, 1 = full)")]
            public float HistogramStrength = 0.4f; // Increased for better effect

            [Header("Noise Reduction")]
            [Tooltip(
                "Enable Gaussian blur for noise reduction - Improves corner detection by reducing false corners from noise"
            )]
            public bool EnableNoiseReduction = true; // Replaces need for corner quality filtering

            [Range(1, 5)]
            [Tooltip("Gaussian blur kernel radius")]
            public int BlurRadius = 2; // Optimized for AprilTag corner preservation

            [Range(0.5f, 3f)]
            [Tooltip("Gaussian sigma value")]
            public float BlurSigma = 0.5f; // Balanced for noise reduction without losing corners

            [Header("Edge Enhancement")]
            [Tooltip(
                "Enable edge enhancement for sharper tag borders - Improves corner sharpness (use carefully, can introduce artifacts)"
            )]
            public bool EnableEdgeEnhancement = false; // Enable only if corners are not sharp enough

            [Range(0f, 2f)]
            [Tooltip("Edge enhancement strength")]
            public float EdgeStrength = 0.3f; // Conservative default to avoid artifacts

            [Header("Performance")]
            [Tooltip("Use half precision (16-bit) for better performance")]
            public bool UseHalfPrecision = true;

            [Tooltip("Process at reduced resolution for performance")]
            public bool EnableDownsampling = false;

            [Range(0.25f, 1f)]
            [Tooltip("Downsampling factor (1 = full resolution)")]
            public float DownsampleFactor = 0.5f;
        }

        // Compute shaders
        private ComputeShader m_preprocessorShader;
        private ComputeShader m_histogramShader;

        // Shader kernels
        private int m_grayscaleKernel;
        private int m_adaptiveThresholdKernel;
        private int m_gaussianBlurKernel;
        private int m_edgeEnhanceKernel;
        private int m_histogramKernel;
        private int m_histogramApplyKernel;
        private int m_grayscaleToRGBAKernel;

        // Render textures for pipeline stages
        private RenderTexture m_sourceTexture;
        private RenderTexture m_grayscaleTexture;
        private RenderTexture m_processedTexture;
        private RenderTexture m_tempTexture;
        private RenderTexture m_finalRGBATexture;

        // Histogram buffers
        private ComputeBuffer m_histogramBuffer;
        private ComputeBuffer m_cdfBuffer;

        // Gaussian kernel buffer
        private ComputeBuffer m_gaussianKernel;

        // Current settings
        private PreprocessingSettings m_settings;
        private int m_width;
        private int m_height;

        // Safety limits
        private int m_maxImageWidth;
        private int m_maxImageHeight;

        // Shader paths
        private string m_preprocessorShaderPath;
        private string m_histogramShaderPath;

        // Performance tracking
        private float _lastProcessingTime;
        private bool _isInitialized;

        public PreprocessingSettings Settings => m_settings;
        public float LastProcessingTimeMs => _lastProcessingTime;
        public bool IsInitialized => _isInitialized;

        public AprilTagGPUPreprocessor(
            int width,
            int height,
            PreprocessingSettings settings = null,
            int maxWidth = 1920,
            int maxHeight = 1080,
            string preprocessorShaderPath = "AprilTagPreprocessor",
            string histogramShaderPath = "AprilTagHistogram"
        )
        {
            m_width = width;
            m_height = height;
            m_settings = settings ?? new PreprocessingSettings();
            m_maxImageWidth = maxWidth;
            m_maxImageHeight = maxHeight;
            m_preprocessorShaderPath = preprocessorShaderPath;
            m_histogramShaderPath = histogramShaderPath;

            Initialize();
        }

        private void Initialize()
        {
            try
            {
                // Load compute shaders
                m_preprocessorShader = Resources.Load<ComputeShader>(m_preprocessorShaderPath);
                m_histogramShader = Resources.Load<ComputeShader>(m_histogramShaderPath);

                if (m_preprocessorShader == null)
                {
                    Debug.LogError(
                        $"[GPU] Failed to load preprocessing compute shader! Make sure Assets/AprilTag/Resources/{m_preprocessorShaderPath}.compute exists."
                    );
                    _isInitialized = false;
                    return;
                }

                if (m_histogramShader == null)
                {
                    Debug.LogError(
                        $"[GPU] Failed to load histogram compute shader! Make sure Assets/AprilTag/Resources/{m_histogramShaderPath}.compute exists."
                    );
                    _isInitialized = false;
                    return;
                }

                // Verify compute shader support
                if (!SystemInfo.supportsComputeShaders)
                {
                    Debug.LogError("[GPU] Compute shaders are not supported on this device!");
                    _isInitialized = false;
                    return;
                }

                // Get kernel indices
                m_grayscaleKernel = m_preprocessorShader.FindKernel("CSGrayscale");
                m_adaptiveThresholdKernel = m_preprocessorShader.FindKernel("CSAdaptiveThreshold");
                m_gaussianBlurKernel = m_preprocessorShader.FindKernel("CSGaussianBlur");
                m_edgeEnhanceKernel = m_preprocessorShader.FindKernel("CSEdgeEnhance");
                m_grayscaleToRGBAKernel = m_preprocessorShader.FindKernel("CSGrayscaleToRGBA");

                m_histogramKernel = m_histogramShader.FindKernel("CSCalculateHistogram");
                m_histogramApplyKernel = m_histogramShader.FindKernel("CSApplyHistogram");

                // Create render textures
                CreateRenderTextures();

                // Create compute buffers
                m_histogramBuffer = new ComputeBuffer(256, sizeof(uint));
                m_cdfBuffer = new ComputeBuffer(256, sizeof(float));

                // Initialize Gaussian kernel
                UpdateGaussianKernel();

                _isInitialized = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GPU] Initialization failed: {e.Message}");
                _isInitialized = false;
            }
        }

        private void CreateRenderTextures()
        {
            var format = m_settings.UseHalfPrecision
                ? RenderTextureFormat.RHalf
                : RenderTextureFormat.RFloat;

            // Calculate actual dimensions based on downsampling
            int processWidth = m_width;
            int processHeight = m_height;

            if (m_settings.EnableDownsampling)
            {
                processWidth = Mathf.RoundToInt(m_width * m_settings.DownsampleFactor);
                processHeight = Mathf.RoundToInt(m_height * m_settings.DownsampleFactor);
            }

            // Source texture (full resolution RGBA)
            m_sourceTexture = new RenderTexture(m_width, m_height, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            m_sourceTexture.Create();

            // Grayscale texture
            m_grayscaleTexture = new RenderTexture(processWidth, processHeight, 0, format)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            m_grayscaleTexture.Create();

            // Processed texture (final output)
            m_processedTexture = new RenderTexture(processWidth, processHeight, 0, format)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            m_processedTexture.Create();

            // Temp texture for multi-pass operations
            m_tempTexture = new RenderTexture(processWidth, processHeight, 0, format)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            m_tempTexture.Create();

            // Final RGBA texture for AprilTag detector
            m_finalRGBATexture = new RenderTexture(
                processWidth,
                processHeight,
                0,
                RenderTextureFormat.ARGB32
            )
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            m_finalRGBATexture.Create();
        }

        private void UpdateGaussianKernel()
        {
            int kernelSize = m_settings.BlurRadius * 2 + 1;
            float[] kernel = new float[kernelSize * kernelSize];
            float sigma = m_settings.BlurSigma;
            float sum = 0;

            // Generate Gaussian kernel
            for (int y = 0; y < kernelSize; y++)
            {
                for (int x = 0; x < kernelSize; x++)
                {
                    int dx = x - m_settings.BlurRadius;
                    int dy = y - m_settings.BlurRadius;
                    float value = Mathf.Exp(-(dx * dx + dy * dy) / (2f * sigma * sigma));
                    kernel[y * kernelSize + x] = value;
                    sum += value;
                }
            }

            // Normalize
            for (int i = 0; i < kernel.Length; i++)
            {
                kernel[i] /= sum;
            }

            // Update or create buffer
            m_gaussianKernel?.Release();
            m_gaussianKernel = new ComputeBuffer(kernel.Length, sizeof(float));
            m_gaussianKernel.SetData(kernel);
        }

        /// <summary>
        /// Process WebCamTexture through GPU preprocessing pipeline
        /// </summary>
        public RenderTexture ProcessTexture(WebCamTexture source)
        {
            if (!_isInitialized || source == null || !source.isPlaying)
            {
                Debug.LogWarning("[GPU] Cannot process - not initialized or source not ready");
                return null;
            }

            // Safety check for large images that might cause crashes
            if (source.width > m_maxImageWidth || source.height > m_maxImageHeight)
            {
                Debug.LogWarning(
                    $"[GPU] Image too large for GPU processing: {source.width}x{source.height} exceeds limits ({m_maxImageWidth}x{m_maxImageHeight}). Skipping preprocessing."
                );
                return null;
            }

            var startTime = Time.realtimeSinceStartup;

            try
            {
                // Copy source to GPU
                Graphics.Blit(source, m_sourceTexture);

                // Run preprocessing pipeline
                ProcessPipeline();

                _lastProcessingTime = (Time.realtimeSinceStartup - startTime) * 1000f;

                return m_finalRGBATexture;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GPU] GPU processing failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Process existing RenderTexture through GPU preprocessing pipeline
        /// </summary>
        public RenderTexture ProcessTexture(RenderTexture source)
        {
            if (!_isInitialized || source == null)
            {
                Debug.LogWarning("[GPU] Cannot process - not initialized or source null");
                return null;
            }

            var startTime = Time.realtimeSinceStartup;

            // Copy source if needed
            if (source != m_sourceTexture)
            {
                Graphics.Blit(source, m_sourceTexture);
            }

            // Run preprocessing pipeline
            ProcessPipeline();

            _lastProcessingTime = (Time.realtimeSinceStartup - startTime) * 1000f;

            return m_finalRGBATexture;
        }

        private void ProcessPipeline()
        {
            // Step 1: Convert to grayscale (and optionally downsample)
            ConvertToGrayscale();

            // Step 2: Noise reduction (if enabled)
            if (m_settings.EnableNoiseReduction)
            {
                ApplyGaussianBlur();
            }

            // Step 3: Histogram equalization (if enabled)
            if (m_settings.EnableHistogramEqualization)
            {
                ApplyHistogramEqualization();
            }

            // Step 4: Edge enhancement (if enabled)
            if (m_settings.EnableEdgeEnhancement)
            {
                ApplyEdgeEnhancement();
            }

            // Step 5: Adaptive threshold (if enabled)
            if (m_settings.EnableAdaptiveThreshold)
            {
                ApplyAdaptiveThreshold();
            }

            // Step 6: Convert final grayscale to RGBA for AprilTag detector
            ConvertToRGBA();
        }

        private void ConvertToGrayscale()
        {
            m_preprocessorShader.SetTexture(m_grayscaleKernel, "_SourceTex", m_sourceTexture);
            m_preprocessorShader.SetTexture(m_grayscaleKernel, "_ResultTex", m_grayscaleTexture);

            int threadGroupsX = Mathf.CeilToInt(m_grayscaleTexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(m_grayscaleTexture.height / 8f);

            m_preprocessorShader.Dispatch(m_grayscaleKernel, threadGroupsX, threadGroupsY, 1);
        }

        private void ApplyGaussianBlur()
        {
            m_preprocessorShader.SetTexture(m_gaussianBlurKernel, "_SourceTex", m_grayscaleTexture);
            m_preprocessorShader.SetTexture(m_gaussianBlurKernel, "_ResultTex", m_tempTexture);
            m_preprocessorShader.SetBuffer(
                m_gaussianBlurKernel,
                "_GaussianKernel",
                m_gaussianKernel
            );
            m_preprocessorShader.SetInt("_KernelRadius", m_settings.BlurRadius);

            int threadGroupsX = Mathf.CeilToInt(m_grayscaleTexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(m_grayscaleTexture.height / 8f);

            m_preprocessorShader.Dispatch(m_gaussianBlurKernel, threadGroupsX, threadGroupsY, 1);

            // Swap textures
            SwapTextures(ref m_grayscaleTexture, ref m_tempTexture);
        }

        private void ApplyHistogramEqualization()
        {
            // Clear histogram buffer
            m_histogramBuffer.SetData(new uint[256]);

            // Calculate histogram
            m_histogramShader.SetTexture(m_histogramKernel, "_SourceTex", m_grayscaleTexture);
            m_histogramShader.SetBuffer(m_histogramKernel, "_Histogram", m_histogramBuffer);

            int threadGroupsX = Mathf.CeilToInt(m_grayscaleTexture.width / 32f);
            int threadGroupsY = Mathf.CeilToInt(m_grayscaleTexture.height / 32f);

            m_histogramShader.Dispatch(m_histogramKernel, threadGroupsX, threadGroupsY, 1);

            // Apply histogram equalization
            m_histogramShader.SetTexture(m_histogramApplyKernel, "_SourceTex", m_grayscaleTexture);
            m_histogramShader.SetTexture(m_histogramApplyKernel, "_ResultTex", m_tempTexture);
            m_histogramShader.SetBuffer(m_histogramApplyKernel, "_Histogram", m_histogramBuffer);
            m_histogramShader.SetFloat("_Strength", m_settings.HistogramStrength);
            m_histogramShader.SetInt(
                "_ImagePixelCount",
                m_grayscaleTexture.width * m_grayscaleTexture.height
            );

            threadGroupsX = Mathf.CeilToInt(m_grayscaleTexture.width / 8f);
            threadGroupsY = Mathf.CeilToInt(m_grayscaleTexture.height / 8f);

            m_histogramShader.Dispatch(m_histogramApplyKernel, threadGroupsX, threadGroupsY, 1);

            // Swap textures
            SwapTextures(ref m_grayscaleTexture, ref m_tempTexture);
        }

        private void ApplyEdgeEnhancement()
        {
            m_preprocessorShader.SetTexture(m_edgeEnhanceKernel, "_SourceTex", m_grayscaleTexture);
            m_preprocessorShader.SetTexture(m_edgeEnhanceKernel, "_ResultTex", m_tempTexture);
            m_preprocessorShader.SetFloat("_EdgeStrength", m_settings.EdgeStrength);

            int threadGroupsX = Mathf.CeilToInt(m_grayscaleTexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(m_grayscaleTexture.height / 8f);

            m_preprocessorShader.Dispatch(m_edgeEnhanceKernel, threadGroupsX, threadGroupsY, 1);

            // Swap textures
            SwapTextures(ref m_grayscaleTexture, ref m_tempTexture);
        }

        private void ApplyAdaptiveThreshold()
        {
            m_preprocessorShader.SetTexture(
                m_adaptiveThresholdKernel,
                "_SourceTex",
                m_grayscaleTexture
            );
            m_preprocessorShader.SetTexture(
                m_adaptiveThresholdKernel,
                "_ResultTex",
                m_processedTexture
            );
            m_preprocessorShader.SetInt("_BlockSize", m_settings.AdaptiveBlockSize);
            m_preprocessorShader.SetFloat("_Constant", m_settings.AdaptiveConstant / 255f);

            int threadGroupsX = Mathf.CeilToInt(m_processedTexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(m_processedTexture.height / 8f);

            m_preprocessorShader.Dispatch(
                m_adaptiveThresholdKernel,
                threadGroupsX,
                threadGroupsY,
                1
            );
        }

        private void SwapTextures(ref RenderTexture a, ref RenderTexture b)
        {
            var temp = a;
            a = b;
            b = temp;
        }

        private void ConvertToRGBA()
        {
            // Get the final grayscale texture (either _processedTexture from adaptive threshold or _grayscaleTexture)
            var sourceTexture = m_settings.EnableAdaptiveThreshold
                ? m_processedTexture
                : m_grayscaleTexture;

            m_preprocessorShader.SetTexture(m_grayscaleToRGBAKernel, "_ResultTex", sourceTexture);
            m_preprocessorShader.SetTexture(
                m_grayscaleToRGBAKernel,
                "_ResultTexRGBA",
                m_finalRGBATexture
            );

            int threadGroupsX = Mathf.CeilToInt(m_finalRGBATexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(m_finalRGBATexture.height / 8f);

            m_preprocessorShader.Dispatch(m_grayscaleToRGBAKernel, threadGroupsX, threadGroupsY, 1);
        }

        /// <summary>
        /// Get processed pixels as Color32 array for AprilTag detection
        /// </summary>
        public Color32[] GetProcessedPixels()
        {
            if (!_isInitialized || m_finalRGBATexture == null)
            {
                Debug.LogWarning(
                    "[GPU] Cannot get processed pixels - preprocessor not initialized or final RGBA texture is null"
                );
                return null;
            }

            try
            {
                // Store current active render texture
                var previousActive = RenderTexture.active;

                // Read directly from RGBA texture
                var tempTex = new Texture2D(
                    m_finalRGBATexture.width,
                    m_finalRGBATexture.height,
                    TextureFormat.RGBA32,
                    false
                );
                RenderTexture.active = m_finalRGBATexture;
                tempTex.ReadPixels(
                    new Rect(0, 0, m_finalRGBATexture.width, m_finalRGBATexture.height),
                    0,
                    0
                );
                tempTex.Apply();

                // Restore previous active render texture
                RenderTexture.active = previousActive;

                // Get pixels directly - no conversion needed
                var pixels = tempTex.GetPixels32();
                UnityEngine.Object.Destroy(tempTex);

                // Validate pixel array size
                if (pixels == null || pixels.Length == 0)
                {
                    Debug.LogError("[GPU] Got null or empty pixel array from processed texture");
                    return null;
                }

                // Ensure we have the expected number of pixels
                int expectedPixels = m_finalRGBATexture.width * m_finalRGBATexture.height;
                if (pixels.Length != expectedPixels)
                {
                    Debug.LogError(
                        $"[GPU] Pixel count mismatch: expected {expectedPixels}, got {pixels.Length}"
                    );
                    return null;
                }

                return pixels;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GPU] Failed to read processed pixels: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Update preprocessing settings
        /// </summary>
        public void UpdateSettings(PreprocessingSettings newSettings)
        {
            m_settings = newSettings;

            // Update Gaussian kernel if blur settings changed
            if (m_settings.EnableNoiseReduction)
            {
                UpdateGaussianKernel();
            }

            // Recreate render textures if resolution settings changed
            if (m_settings.EnableDownsampling)
            {
                DisposeRenderTextures();
                CreateRenderTextures();
            }
        }

        private void DisposeRenderTextures()
        {
            m_sourceTexture?.Release();
            m_grayscaleTexture?.Release();
            m_processedTexture?.Release();
            m_tempTexture?.Release();
            m_finalRGBATexture?.Release();
        }

        public void Dispose()
        {
            DisposeRenderTextures();

            m_histogramBuffer?.Dispose();
            m_cdfBuffer?.Dispose();
            m_gaussianKernel?.Dispose();

            _isInitialized = false;
        }
    }
}
