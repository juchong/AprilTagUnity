using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace AprilTag
{
    /// <summary>
    /// GPU-accelerated image preprocessing for AprilTag detection
    /// Inspired by PhotonVision's preprocessing pipeline but optimized for Quest GPU
    /// </summary>
    public class AprilTagGPUPreprocessor : IDisposable
    {
        [System.Serializable]
        public class PreprocessingSettings
        {
            [Header("Adaptive Threshold")]
            [Tooltip("Enable adaptive thresholding for better detection in varying lighting")]
            public bool enableAdaptiveThreshold = false; // Start disabled - binary output can be too aggressive
            [Range(3, 21)]
            [Tooltip("Block size for adaptive threshold (must be odd)")]
            public int adaptiveBlockSize = 11;
            [Range(-10f, 10f)]
            [Tooltip("Constant subtracted from weighted mean")]
            public float adaptiveConstant = 2f;
            
            [Header("Histogram Equalization")]
            [Tooltip("Enable histogram equalization for contrast enhancement")]
            public bool enableHistogramEqualization = true; // Working feature - enabled
            [Range(0f, 1f)]
            [Tooltip("Strength of histogram equalization (0 = none, 1 = full)")]
            public float histogramStrength = 0.7f; // Increased for better effect
            
            [Header("Noise Reduction")]
            [Tooltip("Enable Gaussian blur for noise reduction")]
            public bool enableNoiseReduction = true; // Working feature - enabled
            [Range(1, 5)]
            [Tooltip("Gaussian blur kernel radius")]
            public int blurRadius = 2; // Increased for better effect
            [Range(0.5f, 3f)]
            [Tooltip("Gaussian sigma value")]
            public float blurSigma = 1.0f; // Increased for better effect
            
            [Header("Edge Enhancement")]
            [Tooltip("Enable edge enhancement for sharper tag borders")]
            public bool enableEdgeEnhancement = false; // Problematic feature - disabled
            [Range(0f, 2f)]
            [Tooltip("Edge enhancement strength")]
            public float edgeStrength = 0.3f; // Reduced default
            
            [Header("Performance")]
            [Tooltip("Use half precision (16-bit) for better performance")]
            public bool useHalfPrecision = true;
            [Tooltip("Process at reduced resolution for performance")]
            public bool enableDownsampling = false;
            [Range(0.25f, 1f)]
            [Tooltip("Downsampling factor (1 = full resolution)")]
            public float downsampleFactor = 0.5f;
        }
        
        // Compute shaders
        private ComputeShader _preprocessorShader;
        private ComputeShader _histogramShader;
        
        // Shader kernels
        private int _grayscaleKernel;
        private int _adaptiveThresholdKernel;
        private int _gaussianBlurKernel;
        private int _edgeEnhanceKernel;
        private int _histogramKernel;
        private int _histogramApplyKernel;
        private int _grayscaleToRGBAKernel;
        
        // Render textures for pipeline stages
        private RenderTexture _sourceTexture;
        private RenderTexture _grayscaleTexture;
        private RenderTexture _processedTexture;
        private RenderTexture _tempTexture;
        private RenderTexture _finalRGBATexture;
        
        // Histogram buffers
        private ComputeBuffer _histogramBuffer;
        private ComputeBuffer _cdfBuffer;
        
        // Gaussian kernel buffer
        private ComputeBuffer _gaussianKernel;
        
        // Current settings
        private PreprocessingSettings _settings;
        private int _width;
        private int _height;
        
        // Performance tracking
        private float _lastProcessingTime;
        private bool _isInitialized;
        
        public PreprocessingSettings Settings => _settings;
        public float LastProcessingTimeMs => _lastProcessingTime;
        public bool IsInitialized => _isInitialized;
        
        public AprilTagGPUPreprocessor(int width, int height, PreprocessingSettings settings = null)
        {
            _width = width;
            _height = height;
            _settings = settings ?? new PreprocessingSettings();
            
            Initialize();
        }
        
        private void Initialize()
        {
            try
            {
                // Load compute shaders
                _preprocessorShader = Resources.Load<ComputeShader>("AprilTagPreprocessor");
                _histogramShader = Resources.Load<ComputeShader>("AprilTagHistogram");
                
                if (_preprocessorShader == null)
                {
                    Debug.LogError("[AprilTagGPUPreprocessor] Failed to load AprilTagPreprocessor compute shader! Make sure Assets/AprilTag/Resources/AprilTagPreprocessor.compute exists.");
                    _isInitialized = false;
                    return;
                }
                
                if (_histogramShader == null)
                {
                    Debug.LogError("[AprilTagGPUPreprocessor] Failed to load AprilTagHistogram compute shader! Make sure Assets/AprilTag/Resources/AprilTagHistogram.compute exists.");
                    _isInitialized = false;
                    return;
                }
                
                // Verify compute shader support
                if (!SystemInfo.supportsComputeShaders)
                {
                    Debug.LogError("[AprilTagGPUPreprocessor] Compute shaders are not supported on this device!");
                    _isInitialized = false;
                    return;
                }
                
                // Get kernel indices
                _grayscaleKernel = _preprocessorShader.FindKernel("CSGrayscale");
                _adaptiveThresholdKernel = _preprocessorShader.FindKernel("CSAdaptiveThreshold");
                _gaussianBlurKernel = _preprocessorShader.FindKernel("CSGaussianBlur");
                _edgeEnhanceKernel = _preprocessorShader.FindKernel("CSEdgeEnhance");
                _grayscaleToRGBAKernel = _preprocessorShader.FindKernel("CSGrayscaleToRGBA");
                
                _histogramKernel = _histogramShader.FindKernel("CSCalculateHistogram");
                _histogramApplyKernel = _histogramShader.FindKernel("CSApplyHistogram");
                
                // Create render textures
                CreateRenderTextures();
                
                // Create compute buffers
                _histogramBuffer = new ComputeBuffer(256, sizeof(uint));
                _cdfBuffer = new ComputeBuffer(256, sizeof(float));
                
                // Initialize Gaussian kernel
                UpdateGaussianKernel();
                
                _isInitialized = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AprilTagGPUPreprocessor] Initialization failed: {e.Message}");
                _isInitialized = false;
            }
        }
        
        private void CreateRenderTextures()
        {
            var format = _settings.useHalfPrecision ? RenderTextureFormat.RHalf : RenderTextureFormat.RFloat;
            
            // Calculate actual dimensions based on downsampling
            int processWidth = _width;
            int processHeight = _height;
            
            if (_settings.enableDownsampling)
            {
                processWidth = Mathf.RoundToInt(_width * _settings.downsampleFactor);
                processHeight = Mathf.RoundToInt(_height * _settings.downsampleFactor);
            }
            
            // Source texture (full resolution RGBA)
            _sourceTexture = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _sourceTexture.Create();
            
            // Grayscale texture
            _grayscaleTexture = new RenderTexture(processWidth, processHeight, 0, format)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _grayscaleTexture.Create();
            
            // Processed texture (final output)
            _processedTexture = new RenderTexture(processWidth, processHeight, 0, format)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _processedTexture.Create();
            
            // Temp texture for multi-pass operations
            _tempTexture = new RenderTexture(processWidth, processHeight, 0, format)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _tempTexture.Create();
            
            // Final RGBA texture for AprilTag detector
            _finalRGBATexture = new RenderTexture(processWidth, processHeight, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _finalRGBATexture.Create();
        }
        
        private void UpdateGaussianKernel()
        {
            int kernelSize = _settings.blurRadius * 2 + 1;
            float[] kernel = new float[kernelSize * kernelSize];
            float sigma = _settings.blurSigma;
            float sum = 0;
            
            // Generate Gaussian kernel
            for (int y = 0; y < kernelSize; y++)
            {
                for (int x = 0; x < kernelSize; x++)
                {
                    int dx = x - _settings.blurRadius;
                    int dy = y - _settings.blurRadius;
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
            _gaussianKernel?.Release();
            _gaussianKernel = new ComputeBuffer(kernel.Length, sizeof(float));
            _gaussianKernel.SetData(kernel);
        }
        
        /// <summary>
        /// Process WebCamTexture through GPU preprocessing pipeline
        /// </summary>
        public RenderTexture ProcessTexture(WebCamTexture source)
        {
            if (!_isInitialized || source == null || !source.isPlaying)
            {
                Debug.LogWarning("[AprilTagGPUPreprocessor] Cannot process - not initialized or source not ready");
                return null;
            }
            
            // Safety check for large images that might cause crashes
            if (source.width > 1920 || source.height > 1080)
            {
                Debug.LogWarning($"[AprilTagGPUPreprocessor] Image too large for GPU processing: {source.width}x{source.height}. Skipping preprocessing.");
                return null;
            }
            
            var startTime = Time.realtimeSinceStartup;
            
            try
            {
                // Copy source to GPU
                Graphics.Blit(source, _sourceTexture);
                
                // Run preprocessing pipeline
                ProcessPipeline();
                
                _lastProcessingTime = (Time.realtimeSinceStartup - startTime) * 1000f;
                
                return _finalRGBATexture;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AprilTagGPUPreprocessor] GPU processing failed: {e.Message}");
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
                Debug.LogWarning("[AprilTagGPUPreprocessor] Cannot process - not initialized or source null");
                return null;
            }
            
            var startTime = Time.realtimeSinceStartup;
            
            // Copy source if needed
            if (source != _sourceTexture)
            {
                Graphics.Blit(source, _sourceTexture);
            }
            
            // Run preprocessing pipeline
            ProcessPipeline();
            
            _lastProcessingTime = (Time.realtimeSinceStartup - startTime) * 1000f;
            
            return _finalRGBATexture;
        }
        
        private void ProcessPipeline()
        {
            // Step 1: Convert to grayscale (and optionally downsample)
            ConvertToGrayscale();
            
            // Step 2: Noise reduction (if enabled)
            if (_settings.enableNoiseReduction)
            {
                ApplyGaussianBlur();
            }
            
            // Step 3: Histogram equalization (if enabled)
            if (_settings.enableHistogramEqualization)
            {
                ApplyHistogramEqualization();
            }
            
            // Step 4: Edge enhancement (if enabled)
            if (_settings.enableEdgeEnhancement)
            {
                ApplyEdgeEnhancement();
            }
            
            // Step 5: Adaptive threshold (if enabled)
            if (_settings.enableAdaptiveThreshold)
            {
                ApplyAdaptiveThreshold();
            }
            
            // Step 6: Convert final grayscale to RGBA for AprilTag detector
            ConvertToRGBA();
        }
        
        private void ConvertToGrayscale()
        {
            _preprocessorShader.SetTexture(_grayscaleKernel, "_SourceTex", _sourceTexture);
            _preprocessorShader.SetTexture(_grayscaleKernel, "_ResultTex", _grayscaleTexture);
            
            int threadGroupsX = Mathf.CeilToInt(_grayscaleTexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(_grayscaleTexture.height / 8f);
            
            _preprocessorShader.Dispatch(_grayscaleKernel, threadGroupsX, threadGroupsY, 1);
        }
        
        private void ApplyGaussianBlur()
        {
            _preprocessorShader.SetTexture(_gaussianBlurKernel, "_SourceTex", _grayscaleTexture);
            _preprocessorShader.SetTexture(_gaussianBlurKernel, "_ResultTex", _tempTexture);
            _preprocessorShader.SetBuffer(_gaussianBlurKernel, "_GaussianKernel", _gaussianKernel);
            _preprocessorShader.SetInt("_KernelRadius", _settings.blurRadius);
            
            int threadGroupsX = Mathf.CeilToInt(_grayscaleTexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(_grayscaleTexture.height / 8f);
            
            _preprocessorShader.Dispatch(_gaussianBlurKernel, threadGroupsX, threadGroupsY, 1);
            
            // Swap textures
            SwapTextures(ref _grayscaleTexture, ref _tempTexture);
        }
        
        private void ApplyHistogramEqualization()
        {
            // Clear histogram buffer
            _histogramBuffer.SetData(new uint[256]);
            
            // Calculate histogram
            _histogramShader.SetTexture(_histogramKernel, "_SourceTex", _grayscaleTexture);
            _histogramShader.SetBuffer(_histogramKernel, "_Histogram", _histogramBuffer);
            
            int threadGroupsX = Mathf.CeilToInt(_grayscaleTexture.width / 32f);
            int threadGroupsY = Mathf.CeilToInt(_grayscaleTexture.height / 32f);
            
            _histogramShader.Dispatch(_histogramKernel, threadGroupsX, threadGroupsY, 1);
            
            // Apply histogram equalization
            _histogramShader.SetTexture(_histogramApplyKernel, "_SourceTex", _grayscaleTexture);
            _histogramShader.SetTexture(_histogramApplyKernel, "_ResultTex", _tempTexture);
            _histogramShader.SetBuffer(_histogramApplyKernel, "_Histogram", _histogramBuffer);
            _histogramShader.SetFloat("_Strength", _settings.histogramStrength);
            _histogramShader.SetInt("_ImagePixelCount", _grayscaleTexture.width * _grayscaleTexture.height);
            
            threadGroupsX = Mathf.CeilToInt(_grayscaleTexture.width / 8f);
            threadGroupsY = Mathf.CeilToInt(_grayscaleTexture.height / 8f);
            
            _histogramShader.Dispatch(_histogramApplyKernel, threadGroupsX, threadGroupsY, 1);
            
            // Swap textures
            SwapTextures(ref _grayscaleTexture, ref _tempTexture);
        }
        
        private void ApplyEdgeEnhancement()
        {
            _preprocessorShader.SetTexture(_edgeEnhanceKernel, "_SourceTex", _grayscaleTexture);
            _preprocessorShader.SetTexture(_edgeEnhanceKernel, "_ResultTex", _tempTexture);
            _preprocessorShader.SetFloat("_EdgeStrength", _settings.edgeStrength);
            
            int threadGroupsX = Mathf.CeilToInt(_grayscaleTexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(_grayscaleTexture.height / 8f);
            
            _preprocessorShader.Dispatch(_edgeEnhanceKernel, threadGroupsX, threadGroupsY, 1);
            
            // Swap textures
            SwapTextures(ref _grayscaleTexture, ref _tempTexture);
        }
        
        private void ApplyAdaptiveThreshold()
        {
            _preprocessorShader.SetTexture(_adaptiveThresholdKernel, "_SourceTex", _grayscaleTexture);
            _preprocessorShader.SetTexture(_adaptiveThresholdKernel, "_ResultTex", _processedTexture);
            _preprocessorShader.SetInt("_BlockSize", _settings.adaptiveBlockSize);
            _preprocessorShader.SetFloat("_Constant", _settings.adaptiveConstant / 255f);
            
            int threadGroupsX = Mathf.CeilToInt(_processedTexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(_processedTexture.height / 8f);
            
            _preprocessorShader.Dispatch(_adaptiveThresholdKernel, threadGroupsX, threadGroupsY, 1);
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
            var sourceTexture = _settings.enableAdaptiveThreshold ? _processedTexture : _grayscaleTexture;
            
            _preprocessorShader.SetTexture(_grayscaleToRGBAKernel, "_ResultTex", sourceTexture);
            _preprocessorShader.SetTexture(_grayscaleToRGBAKernel, "_ResultTexRGBA", _finalRGBATexture);
            
            int threadGroupsX = Mathf.CeilToInt(_finalRGBATexture.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(_finalRGBATexture.height / 8f);
            
            _preprocessorShader.Dispatch(_grayscaleToRGBAKernel, threadGroupsX, threadGroupsY, 1);
        }
        
        /// <summary>
        /// Get processed pixels as Color32 array for AprilTag detection
        /// </summary>
        public Color32[] GetProcessedPixels()
        {
            if (!_isInitialized || _finalRGBATexture == null)
            {
                Debug.LogWarning("[AprilTagGPUPreprocessor] Cannot get processed pixels - preprocessor not initialized or final RGBA texture is null");
                return null;
            }
            
            try
            {
                // Store current active render texture
                var previousActive = RenderTexture.active;
                
                // Read directly from RGBA texture
                var tempTex = new Texture2D(_finalRGBATexture.width, _finalRGBATexture.height, TextureFormat.RGBA32, false);
                RenderTexture.active = _finalRGBATexture;
                tempTex.ReadPixels(new Rect(0, 0, _finalRGBATexture.width, _finalRGBATexture.height), 0, 0);
                tempTex.Apply();
                
                // Restore previous active render texture
                RenderTexture.active = previousActive;
                
                // Get pixels directly - no conversion needed
                var pixels = tempTex.GetPixels32();
                UnityEngine.Object.Destroy(tempTex);
                
                // Validate pixel array size
                if (pixels == null || pixels.Length == 0)
                {
                    Debug.LogError("[AprilTagGPUPreprocessor] Got null or empty pixel array from processed texture");
                    return null;
                }
                
                // Ensure we have the expected number of pixels
                int expectedPixels = _finalRGBATexture.width * _finalRGBATexture.height;
                if (pixels.Length != expectedPixels)
                {
                    Debug.LogError($"[AprilTagGPUPreprocessor] Pixel count mismatch: expected {expectedPixels}, got {pixels.Length}");
                    return null;
                }
                
                return pixels;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AprilTagGPUPreprocessor] Failed to read processed pixels: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Update preprocessing settings
        /// </summary>
        public void UpdateSettings(PreprocessingSettings newSettings)
        {
            _settings = newSettings;
            
            // Update Gaussian kernel if blur settings changed
            if (_settings.enableNoiseReduction)
            {
                UpdateGaussianKernel();
            }
            
            // Recreate render textures if resolution settings changed
            if (_settings.enableDownsampling)
            {
                DisposeRenderTextures();
                CreateRenderTextures();
            }
        }
        
        private void DisposeRenderTextures()
        {
            _sourceTexture?.Release();
            _grayscaleTexture?.Release();
            _processedTexture?.Release();
            _tempTexture?.Release();
            _finalRGBATexture?.Release();
        }
        
        public void Dispose()
        {
            DisposeRenderTextures();
            
            _histogramBuffer?.Dispose();
            _cdfBuffer?.Dispose();
            _gaussianKernel?.Dispose();
            
            _isInitialized = false;
        }
    }
}
