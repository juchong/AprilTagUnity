// Assets/AprilTag/AprilTagController.cs
// Quest-only AprilTag tracker using Meta Passthrough + locally integrated AprilTag library.
// Uses reflection to read WebCamTexture so there's no compile-time dependency on WebCamTextureManager.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AprilTag;
using Meta.XR;
using PassthroughCameraSamples;
using Unity.XR.CoreUtils;
using UnityEngine;

public class AprilTagController : MonoBehaviour
{
    [Header("Pipelines")]
    [SerializeField]
    private AprilTagWebcamPipeline m_webcamPipeline;

    [SerializeField]
    private AprilTagVisualization m_visualizationHelper;

    [Header("Passthrough Feed")]
    [Tooltip(
        "Assign the WebCamTextureManager component from Meta's Passthrough Camera API samples."
    )]
    [SerializeField]
    private UnityEngine.Object m_webCamManager; // reflection target

    [Tooltip("Optional: override the feed with your own WebCamTexture.")]
    [SerializeField]
    private WebCamTexture m_webCamTextureOverride;

    [Header("Visualization")]
    [SerializeField]
    private GameObject m_tagVizPrefab;

    [SerializeField]
    private bool m_scaleVizToTagSize = true;

    [Tooltip(
        "Optional: Override the camera used for coordinate transformation. If null, will auto-detect."
    )]
    [SerializeField]
    private Camera m_referenceCamera;

    // POSITION OFFSET: Global position offset applied to ALL tags when using fallback (non-corner-based) positioning
    // - Only applies when corner-based positioning fails and system falls back to direct pose approach
    // - Added to tag's camera-space position before converting to world space
    // - Use when all tags appear consistently shifted in the same direction (system-wide calibration)
    // - Does NOT save to PlayerPrefs
    [Tooltip(
        "Global offset for fallback positioning only (when corner-based fails). Use for system-wide position calibration."
    )]
    [SerializeField]
    private Vector3 m_positionOffset = Vector3.zero;

    // CORNER POSITION OFFSET: Specific offset applied ONLY to corner-based positioning (primary/preferred method)
    // - Applied directly to final world position AFTER all corner calculations and raycasting
    // - Saves to PlayerPrefs for persistence across sessions
    // - Can be adjusted at runtime using Quest controllers when m_enableConfigurationTool is enabled
    //   * Right A Button: Move right/left (hold grip for left)
    //   * Right B Button: Move up/down (hold grip for down)
    // - This is your PRIMARY calibration tool for Quest deployment
    [Tooltip(
        "Offset for corner-based positioning (primary method). Saves to PlayerPrefs. Adjustable at runtime with Quest controllers when configuration tool enabled."
    )]
    [SerializeField]
    private Vector3 m_cornerPositionOffset = new(0.030f, 0.010f, 0.000f);

    [Tooltip("Save runtime offset to PlayerPrefs for persistence")]
    [SerializeField]
    private bool m_saveRuntimeOffset = true;

    // ROTATION OFFSET: Euler angle offset applied to tag rotations (both corner-based and fallback methods)
    // - Multiplied into world rotation quaternion after converting Vector3 Euler angles
    // - Applied to BOTH positioning methods when m_enableRotationOffset is true
    // - Use for correcting systematic rotation errors (e.g., tags mounted at 90° intervals)
    // - Use for camera coordinate system alignment with Unity world space
    // - Does NOT save to PlayerPrefs
    [Tooltip(
        "Rotation offset (Euler angles) applied to all tag rotations. Use for correcting systematic rotation errors or camera alignment."
    )]
    [SerializeField]
    private Vector3 m_rotationOffset = Vector3.zero;

    [Tooltip("Quest-specific: Use proper passthrough camera raycasting for accurate positioning")]
    [SerializeField]
    private bool m_usePassthroughRaycasting = true;

    [Tooltip("Environment raycast manager for accurate 3D positioning")]
    [SerializeField]
    private EnvironmentRaycastManager m_environmentRaycastManager;

    [Tooltip(
        "Scale factor to adjust tag positioning (1.0 = normal, 0.5 = half size, 2.0 = double size)"
    )]
    [SerializeField]
    private float m_positionScaleFactor = 1.0f;

    [Tooltip("Minimum detection distance in meters (for very close tags)")]
    [SerializeField]
    private float m_minDetectionDistance = 0.3f;

    [Tooltip("Maximum detection distance in meters (for very far tags)")]
    [SerializeField]
    private float m_maxDetectionDistance = 20.0f;

    [Tooltip("Enable distance-based scaling adjustments")]
    [SerializeField]
    private bool m_enableDistanceScaling = true;

    [Tooltip("Enable Quest debugging with controller input")]
    [SerializeField]
    private bool m_enableQuestDebugging = true;

    [Tooltip("Use improved camera intrinsics for better tag alignment")]
    [SerializeField]
    private bool m_useImprovedIntrinsics = true;

    [Tooltip("Scale multiplier for tag visualization (1.0 = normal size)")]
    [SerializeField]
    private float m_visualizationScaleMultiplier = 1.0f;

    [Header("Detection")]
    [Tooltip("Tag family to detect. Tag36h11 is recommended for ArUcO compatibility.")]
    [SerializeField]
    private AprilTag.Interop.TagFamily m_tagFamily = AprilTag.Interop.TagFamily.Tag36h11;

    [Tooltip("Physical tag edge length (meters).")]
    [SerializeField]
    private float m_tagSizeMeters = 0.165f;

    [Tooltip("Downscale factor for detection (1 = full res, 2 = half, etc.).")]
    [Range(1, 8)]
    [SerializeField]
    private int m_decimate = 4;

    [Tooltip("Max detection updates per second.")]
    [SerializeField]
    private float m_maxDetectionsPerSecond = 30f; // Increased from 15 to 30 for better tracking

    [Header("Async Detection")]
    [Tooltip("Run detection on background thread to prevent main thread blocking")]
    [SerializeField]
    private bool m_useAsyncDetection = true;

    [Tooltip("Horizontal FOV (degrees) of the passthrough camera.")]
    [SerializeField]
    private float m_horizontalFovDeg = 78f;

    [Header("Calibration Offsets")]
    [Tooltip("Enable position offset")]
    [SerializeField]
    private bool m_enablePositionOffset = true;

    [Tooltip("Enable rotation offset")]
    [SerializeField]
    private bool m_enableRotationOffset = true;

    [Header("Diagnostics")]
    [Tooltip("Enable all debug logging (can be toggled at runtime)")]
    [SerializeField]
    private bool m_enableAllDebugLogging = true;

    [Tooltip("Enable configuration tool for fine-tuning cube positioning")]
    [SerializeField]
    private bool m_enableConfigurationTool = false; // Disabled by default to avoid input conflicts

    // Public accessors for shared configuration (consumed by AprilTagTransforms)
    /// <summary>
    /// Enable or disable detailed debug logging.
    /// </summary>
    public bool EnableAllDebugLogging => m_enableAllDebugLogging;

    /// <summary>
    /// Position offset applied to tag world positions.
    /// </summary>
    public Vector3 PositionOffset => m_positionOffset;

    /// <summary>
    /// Rotation offset applied to tag world rotations.
    /// </summary>
    public Vector3 RotationOffset => m_rotationOffset;

    /// <summary>
    /// Global scale factor applied to tag positions.
    /// </summary>
    public float PositionScaleFactor => m_positionScaleFactor;

    /// <summary>
    /// Minimum detection distance (meters).
    /// </summary>
    public float MinDetectionDistance => m_minDetectionDistance;

    /// <summary>
    /// Maximum detection distance (meters).
    /// </summary>
    public float MaxDetectionDistance => m_maxDetectionDistance;

    /// <summary>
    /// Whether distance-based scaling on tag distances is enabled.
    /// </summary>
    public bool IsDistanceScalingEnabled => m_enableDistanceScaling;

    /// <summary>
    /// Environment raycast manager used for passthrough raycasting.
    /// </summary>
    public EnvironmentRaycastManager EnvironmentRaycastManager => m_environmentRaycastManager;

    [Header("GPU Preprocessing Settings")]
    [Tooltip("Enable GPU-accelerated image preprocessing for better detection quality")]
    [SerializeField]
    private bool m_enableGPUPreprocessing = true; // Fixed and re-enabled

    [Tooltip("GPU preprocessing settings")]
    [SerializeField]
    private AprilTagGPUPreprocessor.PreprocessingSettings m_gpuPreprocessingSettings = new();

    [Tooltip("Save preprocessed image for debugging (saves to persistent data path on Quest)")]
    [SerializeField]
    private bool m_debugSavePreprocessedImage = false;

    [Tooltip("Include detection overlays in debug image (draws detected tag outlines)")]
    [SerializeField]
    private bool m_debugIncludeDetectionOverlay = true;

    [Tooltip("Debug image save interval (frames between saves, 0 = save every detection)")]
    [SerializeField]
    private int m_debugImageSaveInterval = 300; // Every 5 seconds at 60fps

    [Tooltip("Maximum debug images to keep (older ones are deleted)")]
    [SerializeField]
    private int m_maxDebugImages = 10;

    [Tooltip("Save both raw and preprocessed images for comparison")]
    [SerializeField]
    private bool m_debugSaveBothRawAndProcessed = false;

    [Tooltip("Maximum image width allowed for GPU processing (to prevent crashes)")]
    [SerializeField]
    private int m_gpuMaxImageWidth = 1280; // Allow full camera resolution for GPU preprocessing

    [Tooltip("Maximum image height allowed for GPU processing (to prevent crashes)")]
    [SerializeField]
    private int m_gpuMaxImageHeight = 1280; // Allow full camera resolution for GPU preprocessing

    [Tooltip("Path to the main preprocessing compute shader (relative to Resources folder)")]
    [SerializeField]
    private string m_preprocessorShaderPath = "AprilTagPreprocessor";

    [Tooltip("Path to the histogram compute shader (relative to Resources folder)")]
    [SerializeField]
    private string m_histogramShaderPath = "AprilTagHistogram";

    [Header("Debug Logging Intervals")]
    [Tooltip("Frame interval for periodic debug logs (e.g., 60 = every second at 60fps)")]
    [SerializeField]
    private int m_debugLogInterval = 60;

    [Tooltip("Frame interval for verbose debug logs (e.g., 300 = every 5 seconds at 60fps)")]
    [SerializeField]
    private int m_verboseDebugLogInterval = 300;

    [Tooltip("Frame interval for corner quality logs (e.g., 180 = every 3 seconds at 60fps)")]
    [SerializeField]
    private int m_cornerQualityLogInterval = 180;

    [Header("PhotonVision-Inspired Filtering")]
    // Note: These temporal filters work on detection results and complement GPU preprocessing
    // GPU preprocessing improves image quality BEFORE detection
    // These filters improve stability AFTER detection by analyzing temporal consistency
    [Tooltip("Enable pose smoothing filter (reduces jitter)")]
    [SerializeField]
    private bool m_enablePoseSmoothing = true;

    [Tooltip("Position smoothing time constant (seconds)")]
    [SerializeField]
    private float m_positionSmoothingTime = 0.1f;

    [Tooltip("Rotation smoothing time constant (seconds)")]
    [SerializeField]
    private float m_rotationSmoothingTime = 0.15f;

    [Tooltip("Enable multi-frame validation (rejects inconsistent detections)")]
    [SerializeField]
    private bool m_enableMultiFrameValidation = true;

    [Tooltip("Number of frames to validate against")]
    [SerializeField]
    private int m_validationFrameCount = 3;

    [Tooltip("Maximum position deviation for validation (meters)")]
    [SerializeField]
    private float m_maxPositionDeviation = 0.2f; // Increased from 0.05f for Quest jitter

    [Tooltip("Maximum rotation deviation for validation (degrees)")]
    [SerializeField]
    private float m_maxRotationDeviation = 30f; // Increased from 15f for Quest jitter

    [Tooltip(
        "Enable corner quality assessment (Note: GPU preprocessing provides similar benefits through noise reduction and edge enhancement)"
    )]
    [SerializeField]
    private bool m_enableCornerQualityAssessment = false; // Disabled by default when GPU preprocessing is enabled

    [Tooltip("Minimum corner quality threshold (0-1)")]
    [SerializeField]
    private float m_minCornerQuality = 0.3f;

    [Header("Corner Quality Thresholds")]
    [Tooltip("Minimum side length in pixels (smaller tags are likely false detections)")]
    [SerializeField]
    private float m_minCornerSideLength = 5.0f;

    [Tooltip("Maximum side length in pixels (larger tags are likely false detections)")]
    [SerializeField]
    private float m_maxCornerSideLength = 500.0f;

    [Tooltip("Maximum aspect ratio for tag detection (tags should be roughly square)")]
    [SerializeField]
    private float m_maxAspectRatio = 3.0f;

    [Tooltip("Maximum angle deviation from 90 degrees for corners")]
    [SerializeField]
    private float m_maxCornerAngleDeviation = 30.0f;

    [Tooltip("Quality penalty for small tags")]
    [SerializeField]
    private float m_smallTagQualityPenalty = 0.3f;

    [Tooltip("Quality penalty for large tags")]
    [SerializeField]
    private float m_largeTagQualityPenalty = 0.5f;

    [Tooltip("Quality penalty for elongated tags")]
    [SerializeField]
    private float m_elongatedTagQualityPenalty = 0.4f;

    [Tooltip("Quality penalty for non-convex tags")]
    [SerializeField]
    private float m_nonConvexQualityPenalty = 0.3f;

    [Header("Spatial Anchors")]
    [Tooltip("Enable spatial anchor creation for detected tags")]
    [SerializeField]
    private bool m_enableSpatialAnchors = true;

    [Tooltip("Spatial anchor manager component (auto-created if null)")]
    [SerializeField]
    private AprilTagSpatialAnchorManager m_spatialAnchorManager;

    [Tooltip("Detection confidence threshold for anchor placement (0.0 - 1.0)")]
    [Range(0.0f, 1.0f)]
    [SerializeField]
    private float m_anchorConfidenceThreshold = 0.1f; // Lowered to allow low-confidence tags

    [Header("Validation Time Thresholds")]
    [Tooltip("Time window for considering detections as recent (seconds)")]
    [SerializeField]
    private float m_validationRecentDetectionTime = 1.0f;

    [Tooltip("Confidence value for single detections")]
    [Range(0.0f, 1.0f)]
    [SerializeField]
    private float m_singleDetectionConfidence = 0.5f;

    [Tooltip("Distance-based quality decay factor")]
    [SerializeField]
    private float m_distanceQualityDecayFactor = 0.01f;

    [Tooltip("Stability confidence decay factor (per second)")]
    [SerializeField]
    private float m_stabilityDecayFactor = 0.01f;

    [Tooltip("Minimum confidence threshold")]
    [Range(0.0f, 1.0f)]
    [SerializeField]
    private float m_minimumConfidenceThreshold = 0.1f;

    [Tooltip(
        "Place spatial anchors at the exact center of tags (subtracting the corner position offset)"
    )]
    [SerializeField]
    private bool m_placeAnchorsAtTagCenter = true;

    [Header("Keep Out Zone Settings")]
    [Tooltip("Multiplier for keep out zone radius based on tag size (e.g., 0.3 = 0.3x tag size)")]
    [Range(0.1f, 2.0f)]
    [SerializeField]
    private float m_keepOutZoneMultiplier = 0.3f;

    [Tooltip("Minimum keep out zone radius in meters (prevents too small zones)")]
    [Range(0.01f, 0.5f)]
    [SerializeField]
    private float m_minKeepOutRadius = 0.02f;

    [Tooltip("Maximum keep out zone radius in meters (prevents too large zones)")]
    [Range(0.1f, 1.0f)]
    [SerializeField]
    private float m_maxKeepOutRadius = 0.1f;

    [Tooltip("Runtime calibration step size")]
    [SerializeField]
    private float m_runtimeCalibrationStep = 0.01f;

    // CPU buffers
    private Color32[] m_rgba;

    // GPU preprocessor
    private AprilTagGPUPreprocessor m_gpuPreprocessor;

    // Async detection state (using coroutine pattern)
    private bool m_detectionInProgress = false;
    private System.Collections.IEnumerator m_detectionCoroutine = null;

    // Shared transforms helper (single source of truth for transform math)
    private AprilTagTransforms m_transforms;

    // Headset pose tracking for continuous adjustment
    private Quaternion m_lastHeadsetRotation = Quaternion.identity;
    private Vector3 m_lastHeadsetPosition = Vector3.zero;

    // Detector (recreated when size/decimate changes)
    private TagDetector m_detector;
    private int m_detW,
        m_detH,
        m_detDecim;

    private float m_nextDetectT;
    private readonly Dictionary<int, Transform> m_vizById = new();
    private int m_previousTagCount = 0;

    // PhotonVision-inspired filtering data structures
    [Serializable]
    public class TagDetectionHistory
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Timestamp;
        public float CornerQuality;
        public bool IsValid;

        public TagDetectionHistory(Vector3 pos, Quaternion rot, float quality)
        {
            Position = pos;
            Rotation = rot;
            Timestamp = Time.time;
            CornerQuality = quality;
            IsValid = true;
        }
    }

    [Serializable]
    public class FilteredTagPose
    {
        public Vector3 FilteredPosition;
        public Quaternion FilteredRotation;
        public Vector3 RawPosition;
        public Quaternion RawRotation;
        public float LastUpdateTime;
        public bool IsInitialized;
        public int FramesSinceFirstDetection; // Track how many frames this tag has been tracked

        public FilteredTagPose()
        {
            FilteredPosition = Vector3.zero;
            FilteredRotation = Quaternion.identity;
            RawPosition = Vector3.zero;
            RawRotation = Quaternion.identity;
            LastUpdateTime = 0f;
            IsInitialized = false;
            FramesSinceFirstDetection = 0;
        }
    }

    // Detection history for multi-frame validation (PhotonVision approach)
    private readonly Dictionary<int, Queue<TagDetectionHistory>> m_detectionHistory = new();

    // Filtered poses for smoothing (PhotonVision approach)
    private readonly Dictionary<int, FilteredTagPose> m_filteredPoses = new();

    // Track when visualizations were last active (for cleanup)
    private readonly Dictionary<int, float> m_vizLastActiveTime = new();

    // PERFORMANCE: Reusable buffers to avoid allocations per frame
    private readonly HashSet<int> m_seenTagsBuffer = new();
    private readonly HashSet<int> m_currentTagIdsBuffer = new();
    private readonly List<int> m_tagsToRemoveBuffer = new();
    private TagDetectionHistory[] m_recentDetectionsBuffer; // Allocated on first use

    private void OnDisable() => DisposeDetector();

    /// <summary>
    /// Expose the active passthrough camera eye from the pipeline.
    /// </summary>
    public PassthroughCameraEye GetWebCamManagerEye()
    {
        return m_webcamPipeline != null
            ? m_webcamPipeline.GetWebCamManagerEye()
            : PassthroughCameraEye.Left;
    }

    /// <summary>
    /// Returns the appropriate camera transform for world coordinate conversion on Quest.
    /// </summary>
    public Transform GetCorrectCameraReference()
    {
        if (m_webcamPipeline != null)
        {
            return m_webcamPipeline.GetCorrectCameraReference();
        }
        return Camera.main != null ? Camera.main.transform : transform;
    }

    private void Awake()
    {
        // Fix Input System issues on startup
        AprilTag.InputSystemFixer.FixAllEventSystems();

        // Load saved runtime offset
        LoadRuntimeOffset();

        // Subscribe to permission events
        AprilTagPermissionsManager.OnAllPermissionsGranted += OnAllPermissionsGranted;
        AprilTagPermissionsManager.OnPermissionsDenied += OnPermissionsDenied;

        // Auto-find EnvironmentRaycastManager if not assigned
        if (m_environmentRaycastManager == null && m_usePassthroughRaycasting)
        {
            m_environmentRaycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
            if (m_environmentRaycastManager == null && m_enableAllDebugLogging)
            {
                Debug.LogWarning(
                    "[AprilTag] No EnvironmentRaycastManager found. Passthrough raycasting will not work properly. Please assign one or disable usePassthroughRaycasting."
                );
            }
        }

        // Initialize spatial anchor manager
        InitializeSpatialAnchorManager();

        // Ensure we have a transforms helper to delegate calculations
        if (m_transforms == null)
        {
            m_transforms = FindFirstObjectByType<AprilTagTransforms>();
            if (m_transforms == null)
            {
                m_transforms = gameObject.GetComponent<AprilTagTransforms>();
            }
            if (m_transforms == null)
            {
                m_transforms = gameObject.AddComponent<AprilTagTransforms>();
            }
            // Wire controller into transforms for shared config
            var controllerField = typeof(AprilTagTransforms).GetField(
                "m_controller",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            controllerField?.SetValue(m_transforms, this);
        }

        // Ensure we have the new pipeline helpers
        if (m_webcamPipeline == null)
        {
            m_webcamPipeline =
                FindFirstObjectByType<AprilTagWebcamPipeline>()
                ?? gameObject.GetComponent<AprilTagWebcamPipeline>()
                ?? gameObject.AddComponent<AprilTagWebcamPipeline>();
        }

        if (m_visualizationHelper == null)
        {
            m_visualizationHelper =
                FindFirstObjectByType<AprilTagVisualization>()
                ?? gameObject.GetComponent<AprilTagVisualization>()
                ?? gameObject.AddComponent<AprilTagVisualization>();
        }
    }

    /// <summary>
    /// Initialize the spatial anchor manager for tag-based anchor creation
    /// </summary>
    private void InitializeSpatialAnchorManager()
    {
        if (!m_enableSpatialAnchors)
            return;

        // Find or create spatial anchor manager if not assigned
        if (m_spatialAnchorManager == null)
        {
            // First try to find existing manager in the scene
            m_spatialAnchorManager = FindFirstObjectByType<AprilTagSpatialAnchorManager>();

            // If not found, try as a component on this object
            if (m_spatialAnchorManager == null)
            {
                m_spatialAnchorManager = GetComponent<AprilTagSpatialAnchorManager>();
            }

            // If still not found, create one as a component (fallback)
            if (m_spatialAnchorManager == null)
            {
                m_spatialAnchorManager = gameObject.AddComponent<AprilTagSpatialAnchorManager>();

                if (m_enableAllDebugLogging)
                {
                    Debug.Log(
                        "[AprilTag] Created AprilTagSpatialAnchorManager as component (fallback)"
                    );
                }
            }
            else
            {
                if (m_enableAllDebugLogging)
                {
                    Debug.Log("[AprilTag] Found existing AprilTagSpatialAnchorManager in scene");
                }
            }
        }

        // Configure the spatial anchor manager
        if (m_spatialAnchorManager != null)
        {
            // Set debug logging state
            m_spatialAnchorManager.EnableDebugLogging = m_enableAllDebugLogging;

            // CRITICAL: Subscribe to anchor events for visualization
            // This allows us to create visualizations for loaded anchors on startup
            AprilTagSpatialAnchorManager.OnAnchorCreated += OnSpatialAnchorCreated;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[AprilTag] Spatial anchor manager initialized - "
                        + $"confidence threshold: {m_anchorConfidenceThreshold}, "
                        + $"keep-out zone: {m_keepOutZoneMultiplier}x tag size "
                        + $"(min: {m_minKeepOutRadius}m, max: {m_maxKeepOutRadius}m)"
                );
                Debug.Log("[AprilTag] Subscribed to OnAnchorCreated event for visualizations");
            }
        }
    }

    /// <summary>
    /// Handle spatial anchor creation/loading - creates visualization for the anchor
    /// </summary>
    private void OnSpatialAnchorCreated(int tagId, OVRSpatialAnchor anchor)
    {
        if (anchor == null || anchor.gameObject == null)
        {
            Debug.LogWarning(
                $"[AprilTag] OnSpatialAnchorCreated called with null anchor for tag {tagId}"
            );
            return;
        }

        if (m_enableAllDebugLogging)
        {
            Debug.Log(
                $"[AprilTag] OnSpatialAnchorCreated event received for tag {tagId} at position {anchor.transform.position}"
            );
        }

        // Check if visualization already exists
        if (m_vizById.ContainsKey(tagId))
        {
            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[AprilTag] Visualization already exists for tag {tagId}, skipping creation"
                );
            }
            return;
        }

        // Create visualization for the anchor
        if (!m_tagVizPrefab)
        {
            Debug.LogWarning(
                $"[AprilTag] No tag visualization prefab assigned! Cannot create visualization for loaded anchor tag {tagId}"
            );
            return;
        }

        // Instantiate visualization
        if (m_enableAllDebugLogging)
        {
            Debug.Log($"[AprilTag] Instantiating visualization prefab for tag {tagId}");
        }

        var vizTransform = Instantiate(m_tagVizPrefab).transform;
        vizTransform.name = $"AprilTag_{tagId}_Loaded";

        if (m_enableAllDebugLogging)
        {
            Debug.Log(
                $"[AprilTag] Visualization instantiated: {vizTransform.name}, active: {vizTransform.gameObject.activeSelf}"
            );
        }

        // Configure visualization to ignore occlusion
        if (m_visualizationHelper != null)
        {
            m_visualizationHelper.ConfigureVisualizationForNoOcclusion(vizTransform);

            if (m_enableAllDebugLogging)
            {
                Debug.Log($"[AprilTag] Configured visualization for no occlusion");
            }
        }

        // Parent the visualization to the anchor so it moves with it
        vizTransform.SetParent(anchor.transform, false);
        vizTransform.localPosition = Vector3.zero;
        vizTransform.localRotation = Quaternion.identity;
        vizTransform.localScale = Vector3.one * m_visualizationScaleMultiplier;

        // Track the visualization
        m_vizById[tagId] = vizTransform;

        if (m_enableAllDebugLogging)
        {
            Debug.Log(
                $"[AprilTag] Created visualization for loaded anchor tag {tagId} at {anchor.transform.position}. "
                    + $"Parent: {anchor.gameObject.name}, "
                    + $"Viz local pos: {vizTransform.localPosition}, scale: {vizTransform.localScale}, "
                    + $"Total tracked visualizations: {m_vizById.Count}"
            );
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        AprilTagSpatialAnchorManager.OnAnchorCreated -= OnSpatialAnchorCreated;

        // Dispose detector resources
        DisposeDetector();

        // Unsubscribe from permission events
        AprilTagPermissionsManager.OnAllPermissionsGranted -= OnAllPermissionsGranted;
        AprilTagPermissionsManager.OnPermissionsDenied -= OnPermissionsDenied;

        // PERFORMANCE: Force GC to clean up any accumulated resources
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }

    private void OnAllPermissionsGranted()
    {
        if (m_enableAllDebugLogging)
            Debug.Log("[AprilTag] All required permissions granted - ready to start detection");
        // Permissions are now available, detection will start automatically in Update()
    }

    private void OnPermissionsDenied()
    {
        if (m_enableAllDebugLogging)
            Debug.LogWarning(
                "[AprilTag] Required permissions denied - detection will not work properly"
            );
        // Could show UI message to user here
    }

    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Unity lifecycle)
    private void Update()
    {
        // Quest debugging input handling
        if (m_enableQuestDebugging)
        {
            HandleQuestDebugInput();
        }

        // Check permissions before proceeding with detection
        if (!AprilTagPermissionsManager.HasAllPermissions)
        {
            // Only log this warning occasionally to avoid spam
            if (m_enableAllDebugLogging && Time.frameCount % m_verboseDebugLogInterval == 0)
            {
                Debug.LogWarning("[AprilTag] Waiting for required permissions to be granted");
            }
            return;
        }

        var wct = m_webcamPipeline != null ? m_webcamPipeline.GetActiveWebCamTexture() : null;
        if (wct == null)
        {
            if (m_enableAllDebugLogging)
                Debug.LogWarning("[AprilTag] No WebCamTexture available");
            return;
        }

        if (!wct.isPlaying)
        {
            if (m_enableAllDebugLogging)
                Debug.LogWarning("[AprilTag] WebCamTexture is not playing");
            return;
        }

        if (wct.width <= 16 || wct.height <= 16)
        {
            if (m_enableAllDebugLogging)
                Debug.LogWarning(
                    $"[AprilTag] WebCamTexture dimensions too small: {wct.width}x{wct.height}"
                );
            return;
        }

        // Additional check: ensure WebCamTexture has been initialized for at least a few frames
        if (Time.frameCount < 10)
        {
            return;
        }

        // CRITICAL: Only proceed with detection at the specified rate (e.g., 15-30 FPS)
        // This prevents expensive GetPixels32() calls every frame (72-90 FPS)
        if (Time.time < m_nextDetectT)
            return;

        // Ensure detector matches the feed dimensions
        if (
            m_detector == null
            || m_detW != wct.width
            || m_detH != wct.height
            || m_detDecim != m_decimate
        )
        {
            if (m_enableAllDebugLogging)
                Debug.Log(
                    $"[AprilTag] Recreating detector: {wct.width}x{wct.height}, decimate={m_decimate}"
                );
            // Recreate detector using pipeline factory
            DisposeDetector();
            m_detector =
                m_webcamPipeline != null
                    ? m_webcamPipeline.CreateDetector(
                        wct.width,
                        wct.height,
                        m_tagFamily,
                        m_decimate
                    )
                    : new TagDetector(wct.width, wct.height, m_tagFamily, Mathf.Max(1, m_decimate));
            m_detW = wct.width;
            m_detH = wct.height;
            m_detDecim = Mathf.Max(1, m_decimate);
        }

        // Ensure GPU preprocessor matches the feed dimensions
        if (m_enableGPUPreprocessing)
        {
            if (m_gpuPreprocessor == null || m_detW != wct.width || m_detH != wct.height)
            {
                m_gpuPreprocessor?.Dispose();
                m_gpuPreprocessor = new AprilTagGPUPreprocessor(
                    wct.width,
                    wct.height,
                    m_gpuPreprocessingSettings,
                    m_gpuMaxImageWidth,
                    m_gpuMaxImageHeight,
                    m_preprocessorShaderPath,
                    m_histogramShaderPath
                );

                if (m_gpuPreprocessor.IsInitialized)
                {
                    if (m_enableAllDebugLogging)
                        Debug.Log($"[AprilTag] Created GPU preprocessor: {wct.width}x{wct.height}");
                }
                else
                {
                    Debug.LogError(
                        "[AprilTag] Failed to initialize GPU preprocessor - falling back to CPU processing"
                    );
                    m_gpuPreprocessor = null;
                    m_enableGPUPreprocessing = false;
                }
            }
        }

        // Get pixels - either preprocessed or raw
        // PERFORMANCE FIX: This expensive operation now only runs at detection rate (15-30 FPS),
        // not every frame (72-90 FPS), saving ~10-30ms per frame!
        // NOTE: Actual resolution is limited by m_gpuMaxImageWidth/Height (set to 640x640 for Quest)
        try
        {
            if (
                m_enableGPUPreprocessing
                && m_gpuPreprocessor != null
                && m_gpuPreprocessor.IsInitialized
            )
            {
                try
                {
                    // Process image on GPU (will be downscaled to max dimensions automatically)
                    var processedTexture = m_gpuPreprocessor.ProcessTexture(wct);
                    if (processedTexture != null)
                    {
                        m_rgba = m_gpuPreprocessor.GetProcessedPixels();
                        if (m_rgba != null && m_rgba.Length > 0)
                        {
                            // Validate pixel count matches expected size
                            var expectedPixels = wct.width * wct.height;
                            if (m_rgba.Length == expectedPixels)
                            {
                                if (
                                    m_enableAllDebugLogging
                                    && Time.frameCount % m_debugLogInterval == 0
                                )
                                {
                                    Debug.Log(
                                        $"[AprilTag] GPU preprocessing completed in {m_gpuPreprocessor.LastProcessingTimeMs:F2}ms, processed {m_rgba.Length} pixels"
                                    );
                                }

                                // Debug image saving moved to after detection for overlay support
                            }
                            else
                            {
                                // Pixel count mismatch - fallback to raw
                                Debug.LogError(
                                    $"[AprilTag] GPU preprocessing pixel count mismatch: expected {expectedPixels}, got {m_rgba.Length}. Falling back to raw pixels."
                                );
                                m_rgba = wct.GetPixels32();
                            }
                        }
                        else
                        {
                            // GPU processing returned no pixels, fallback to raw
                            m_rgba = wct.GetPixels32();
                            if (m_enableAllDebugLogging)
                                Debug.LogWarning(
                                    "[AprilTag] GPU preprocessing returned no pixels, using raw pixels"
                                );
                        }
                    }
                    else
                    {
                        // Fallback to raw pixels if GPU processing failed
                        m_rgba = wct.GetPixels32();
                        if (m_enableAllDebugLogging)
                            Debug.LogWarning(
                                "[AprilTag] GPU preprocessing texture was null, using raw pixels"
                            );
                    }
                }
                catch (Exception e)
                {
                    // GPU processing crashed - disable it and fallback to raw
                    Debug.LogError(
                        $"[AprilTag] GPU preprocessing crashed: {e.Message}. Disabling GPU preprocessing and using raw pixels."
                    );
                    m_enableGPUPreprocessing = false;
                    m_gpuPreprocessor?.Dispose();
                    m_gpuPreprocessor = null;
                    m_rgba = wct.GetPixels32();
                }
            }
            else
            {
                // Get pixels directly from WebCamTexture (original path)
                m_rgba = wct.GetPixels32();

                // Debug image saving moved to after detection for overlay support
            }

            if (m_rgba == null || m_rgba.Length == 0)
            {
                if (m_enableAllDebugLogging && Time.frameCount % m_verboseDebugLogInterval == 0)
                    Debug.LogWarning("[AprilTag] No pixel data available");
                return;
            }
        }
        catch (Exception ex)
        {
            if (m_enableAllDebugLogging && Time.frameCount % m_verboseDebugLogInterval == 0)
                Debug.LogWarning($"[AprilTag] Failed to get pixels: {ex.Message}");
            return;
        }

        // Run detection (async or sync based on settings)
        if (m_useAsyncDetection)
        {
            // Async path: Start detection coroutine if not already running
            if (!m_detectionInProgress)
            {
                m_detectionCoroutine = DetectTagsAsync(m_rgba);
                StartCoroutine(m_detectionCoroutine);
            }
            // If detection is in progress, skip this frame (use previous results)
        }
        else
        {
            // Sync path: Run detection on main thread (original behavior)
            m_detector.ProcessImage(m_rgba.AsSpan(), m_horizontalFovDeg, m_tagSizeMeters);
            m_nextDetectT = Time.time + 1f / Mathf.Max(1f, m_maxDetectionsPerSecond);
        }

        // Store whether we should save debug images this frame
        bool shouldSaveDebugImage =
            m_debugSavePreprocessedImage
            && (m_debugImageSaveInterval == 0 || Time.frameCount % m_debugImageSaveInterval == 0);

        // Debug logging for detection count
        if (Time.frameCount % m_debugLogInterval == 0) // Log periodically regardless of enableAllDebugLogging
        {
            var tagCount = m_detector.DetectedTags?.Count() ?? 0;
            if (tagCount == 0)
            {
                Debug.Log(
                    $"[AprilTag] No tags detected. Detector: {m_detW}x{m_detH}, decimation={m_detDecim}, tagSize={m_tagSizeMeters}m, FOV={m_horizontalFovDeg}°, GPU={m_enableGPUPreprocessing}"
                );

                // Additional debug info
                if (Time.frameCount % m_verboseDebugLogInterval == 0)
                {
                    Debug.Log(
                        $"[AprilTag] Detection params: Family={m_tagFamily}, MaxDetections/sec={m_maxDetectionsPerSecond}"
                    );
                    Debug.Log(
                        $"[AprilTag] WebCamTexture: {wct?.width}x{wct?.height}, isPlaying={wct?.isPlaying}"
                    );
                    Debug.Log($"[AprilTag] Pixel buffer size: {m_rgba?.Length ?? 0}");

                    // Check if we have a viz prefab
                    if (!m_tagVizPrefab)
                    {
                        Debug.LogWarning(
                            "[AprilTag] WARNING: No tag visualization prefab assigned!"
                        );
                    }
                }
            }
            else
            {
                Debug.Log($"[AprilTag] SUCCESS! Detected {tagCount} tags!");
                foreach (var tag in m_detector.DetectedTags.Take(5)) // Log first 5 tags
                {
                    Debug.Log(
                        $"[AprilTag] - Tag ID: {tag.ID}, Position: {tag.Position}, Rotation: {tag.Rotation.eulerAngles}"
                    );
                }
            }
        }

        // Visualize detected tags using corner-based positioning
        // PERFORMANCE: Reuse buffer instead of allocating new HashSet each frame
        m_seenTagsBuffer.Clear();
        var detectedCount = 0;

        // Try to get raw detection data for corner-based positioning
        var rawDetections =
            m_webcamPipeline != null
                ? m_webcamPipeline.GetRawDetections(m_detector)
                : new System.Collections.Generic.List<object>();

        // Save debug images now that we have detection data
        if (shouldSaveDebugImage)
        {
            // Save the processed/raw image with detection overlays
            SaveDebugImage(m_rgba, m_detW, m_detH, m_enableGPUPreprocessing);

            // Also save raw image for comparison if requested
            if (m_debugSaveBothRawAndProcessed && m_enableGPUPreprocessing && wct != null)
            {
                var rawPixels = wct.GetPixels32();
                SaveDebugImage(rawPixels, wct.width, wct.height, false);
            }
        }

        foreach (var t in m_detector.DetectedTags)
        {
            detectedCount++;
            m_seenTagsBuffer.Add(t.ID);

            // Try to find corresponding raw detection data for corner coordinates
            Vector2? cornerCenter = null;
            if (m_useImprovedIntrinsics && m_usePassthroughRaycasting)
            {
                // Use improved intrinsics-based corner detection
                var eye = GetWebCamManagerEye();
                var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
                cornerCenter = m_transforms.TryGetCornerBasedCenterWithIntrinsics(
                    t.ID,
                    rawDetections,
                    intrinsics
                );
            }
            else
            {
                // Use standard corner detection
                cornerCenter = m_transforms.TryGetCornerBasedCenter(t.ID, rawDetections);
            }

            if (m_enableAllDebugLogging && cornerCenter.HasValue)
            {
                Debug.Log($"[AprilTag] Tag {t.ID}: Corner center found at {cornerCenter.Value}");
            }
            else if (m_enableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[AprilTag] Tag {t.ID}: No corner center found, using fallback positioning"
                );
            }

            if (m_enableAllDebugLogging)
            {
                if (m_usePassthroughRaycasting)
                {
                    var debugWorldPos = m_transforms.GetWorldPositionUsingPassthroughRaycasting(t);
                    // Debug.Log($"[AprilTag] id={t.ID} camera_pos={t.Position:F3} passthrough_world_pos={debugWorldPos:F3} camera_euler={t.Rotation.eulerAngles:F1} use_raycasting={usePassthroughRaycasting} corner_center={cornerCenter:F3}");
                }
                else
                {
                    var debugCam = GetCorrectCameraReference();
                    var debugAdjustedPosition =
                        (t.Position + m_positionOffset) * m_positionScaleFactor;
                    var debugWorldPos =
                        debugCam.position + debugCam.rotation * debugAdjustedPosition;
                    // Debug.Log($"[AprilTag] id={t.ID} camera_pos={t.Position:F3} world_pos={debugWorldPos:F3} camera_euler={t.Rotation.eulerAngles:F1} corner_center={cornerCenter:F3}");
                }
            }

            if (!m_vizById.TryGetValue(t.ID, out var tr) || tr == null)
            {
                if (!m_tagVizPrefab)
                {
                    if (m_enableAllDebugLogging && Time.frameCount % m_verboseDebugLogInterval == 0)
                    {
                        Debug.LogWarning(
                            $"[AprilTag] No tag visualization prefab assigned! Cannot create visualization for tag {t.ID}"
                        );
                    }
                    continue;
                }
                tr = Instantiate(m_tagVizPrefab).transform;
                tr.name = $"AprilTag_{t.ID}";

                // Configure visualization to ignore occlusion
                if (m_visualizationHelper != null)
                {
                    m_visualizationHelper.ConfigureVisualizationForNoOcclusion(tr);
                }

                m_vizById[t.ID] = tr;
            }

            // Quest-specific positioning using corner-based approach for better accuracy
            Vector3 worldPosition;
            Quaternion worldRotation;

            // Try corner-based positioning first (more accurate for Quest)
            var cornerCenterResult = m_transforms.TryGetCornerBasedCenter(t.ID, rawDetections);
            if (cornerCenterResult.HasValue)
            {
                // Use corner-based positioning which works better with Quest's coordinate system
                worldPosition =
                    m_transforms.GetWorldPositionFromCornerCenter(cornerCenterResult.Value, t)
                    + m_cornerPositionOffset;
                worldRotation = m_transforms.GetCornerBasedRotation(
                    t.ID,
                    rawDetections,
                    worldPosition
                );

                // Apply rotation offset if enabled
                if (m_enableRotationOffset)
                {
                    worldRotation *= Quaternion.Euler(m_rotationOffset);
                }

                if (m_enableAllDebugLogging && detectedCount != m_previousTagCount)
                {
                    Debug.Log(
                        $"[AprilTag] Tag {t.ID}: Position={worldPosition}, Offset={m_cornerPositionOffset}"
                    );
                }
            }
            else
            {
                if (m_enableAllDebugLogging && detectedCount != m_previousTagCount)
                {
                    Debug.Log(
                        $"[AprilTag] Tag {t.ID}: Corner-based positioning failed, falling back to direct pose"
                    );
                }

                // Fallback to direct pose approach
                var cam = GetCorrectCameraReference();

                // Apply position offset and scaling
                var adjustedPosition = t.Position * m_positionScaleFactor;
                if (m_enablePositionOffset)
                {
                    adjustedPosition += m_positionOffset;
                }

                // Apply distance scaling if enabled
                if (m_enableDistanceScaling)
                {
                    var distance = adjustedPosition.magnitude;
                    var scaledDistance = AprilTagTransforms.ApplyDistanceScaling(distance);
                    adjustedPosition = adjustedPosition.normalized * scaledDistance;
                }

                // Transform from camera space to world space
                worldPosition = cam.position + cam.rotation * adjustedPosition;
                worldRotation = m_transforms.GetCornerBasedRotation(
                    t.ID,
                    rawDetections,
                    worldPosition
                );

                // Apply rotation offset if enabled
                if (m_enableRotationOffset)
                {
                    worldRotation *= Quaternion.Euler(m_rotationOffset);
                }

                if (m_enableAllDebugLogging && detectedCount != m_previousTagCount)
                {
                    var camRef = GetCorrectCameraReference();
                    var offsetTagPosition = camRef.position + camRef.rotation * t.Position;
                    var offsetTagRotation = camRef.rotation * t.Rotation;

                    Debug.Log(
                        $"[AprilTag] Tag {t.ID}: Using direct pose positioning at {worldPosition}, AprilTag pos: {t.Position}, adjusted pos: {adjustedPosition}"
                    );
                    Debug.Log(
                        $"[AprilTag] Tag {t.ID}: Direct pose - Raw: {t.Position}, {t.Rotation.eulerAngles}"
                    );
                    Debug.Log(
                        $"[AprilTag] Tag {t.ID}: Direct pose - Offset: {offsetTagPosition}, {offsetTagRotation.eulerAngles}"
                    );
                }
            }

            // PhotonVision-inspired filtering and validation
            float cornerQuality = 1.0f; // Default to perfect quality

            // Only perform CPU-based corner quality assessment if GPU preprocessing is disabled
            // or if corner quality assessment is explicitly enabled
            if (
                m_enableCornerQualityAssessment
                && (!m_enableGPUPreprocessing || m_enableCornerQualityAssessment)
            )
            {
                var corners = m_transforms.ExtractCornersFromRawDetection(t.ID, rawDetections);
                cornerQuality = CalculateCornerQuality(corners);

                // Check corner quality threshold
                if (cornerQuality < m_minCornerQuality)
                {
                    if (m_enableAllDebugLogging)
                    {
                        Debug.LogWarning(
                            $"[AprilTag] Tag {t.ID} rejected - Corner quality {cornerQuality:F3} < {m_minCornerQuality:F3}"
                        );
                    }
                    continue; // Skip this detection
                }
            }
            else if (
                m_enableGPUPreprocessing
                && m_enableAllDebugLogging
                && Time.frameCount % m_verboseDebugLogInterval == 0
            )
            {
                Debug.Log(
                    "[AprilTag] Corner quality assessment skipped - GPU preprocessing provides image quality enhancement"
                );
            }

            // Multi-frame validation (PhotonVision approach)
            if (!ValidateTagDetection(t.ID, worldPosition, worldRotation, cornerQuality))
            {
                continue; // Skip this detection - failed validation
            }

            // Apply pose smoothing filter (PhotonVision approach)
            var finalPosition = worldPosition;
            var finalRotation = worldRotation;

            if (m_enablePoseSmoothing)
            {
                // Initialize or get existing filtered pose
                if (!m_filteredPoses.ContainsKey(t.ID))
                {
                    m_filteredPoses[t.ID] = new FilteredTagPose();
                }

                var filteredPose = m_filteredPoses[t.ID];
                var deltaTime = Time.time - filteredPose.LastUpdateTime;

                // Increment frame counter
                filteredPose.FramesSinceFirstDetection++;

                // Only mark as initialized after sufficient stable frames to prevent
                // anchors from being placed during the initial position stabilization period
                const int MIN_FRAMES_FOR_INITIALIZATION = 10; // ~0.17s at 60fps
                if (filteredPose.FramesSinceFirstDetection >= MIN_FRAMES_FOR_INITIALIZATION)
                {
                    filteredPose.IsInitialized = true;
                }

                // Apply PhotonVision-inspired temporal filtering
                finalPosition = FilterTagPosition(
                    worldPosition,
                    filteredPose.FilteredPosition,
                    deltaTime,
                    filteredPose.IsInitialized
                );
                finalRotation = FilterTagRotation(
                    worldRotation,
                    filteredPose.FilteredRotation,
                    deltaTime,
                    filteredPose.IsInitialized
                );

                // Update filtered pose data
                filteredPose.RawPosition = worldPosition;
                filteredPose.RawRotation = worldRotation;
                filteredPose.FilteredPosition = finalPosition;
                filteredPose.FilteredRotation = finalRotation;
                filteredPose.LastUpdateTime = Time.time;

                // Log initialization progress
                if (m_enableAllDebugLogging && !filteredPose.IsInitialized)
                {
                    Debug.Log(
                        $"[AprilTag] Tag {t.ID} initializing: {filteredPose.FramesSinceFirstDetection}/{MIN_FRAMES_FOR_INITIALIZATION} frames"
                    );
                }
            }

            if (m_enableAllDebugLogging && detectedCount != m_previousTagCount)
            {
                Debug.Log(
                    $"[AprilTag] Tag {t.ID}: Raw={worldPosition:F3}, Filtered={finalPosition:F3}, Quality={cornerQuality:F3}"
                );
            }

            tr.SetPositionAndRotation(finalPosition, finalRotation);
            if (m_scaleVizToTagSize)
                tr.localScale = Vector3.one * m_tagSizeMeters * m_visualizationScaleMultiplier;
            tr.gameObject.SetActive(true);

            // Track when this visualization was last active
            m_vizLastActiveTime[t.ID] = Time.time;
        }

        // Log detection results only when tag count changes
        if (detectedCount != m_previousTagCount)
        {
            if (detectedCount > 0)
            {
                Debug.Log($"[AprilTag] Detected {detectedCount} tags");
            }
            else if (m_previousTagCount > 0)
            {
                Debug.Log($"[AprilTag] All tags lost");
            }
        }

        // Update previous tag count for next frame
        m_previousTagCount = detectedCount;

        // PERFORMANCE FIX: Only process spatial anchors when detection runs (not every frame)
        // This was being called at 72-90 FPS but should only run at detection rate (15-30 FPS)
        ProcessSpatialAnchors(m_seenTagsBuffer);

        // Hide those not seen this frame
        foreach (var kv in m_vizById)
            if (!m_seenTagsBuffer.Contains(kv.Key) && kv.Value)
                kv.Value.gameObject.SetActive(false);

        // MEMORY LEAK FIX: Periodically clean up old visualizations that haven't been seen
        // This prevents m_vizById from growing indefinitely as different tags are detected over time
        if (Time.frameCount % 900 == 0) // Every ~12 seconds at 72 FPS
        {
            CleanupOldVisualizations(m_seenTagsBuffer);
        }

        // PERFORMANCE: Force periodic garbage collection to prevent buildup
        // Quest has limited RAM and progressive degradation suggests memory pressure
        if (Time.frameCount % 3600 == 0) // Every ~50 seconds at 72 FPS
        {
            System.GC.Collect();
            if (m_enableAllDebugLogging)
            {
                float memory = System.GC.GetTotalMemory(false) / 1024f / 1024f;
                Debug.Log($"[AprilTag] Forced GC cleanup. Memory: {memory:F1}MB");
            }
        }
    }

    /// <summary>
    /// Clean up visualizations for tags that haven't been detected recently
    /// Prevents memory leaks from m_vizById dictionary growing indefinitely
    /// </summary>
    private void CleanupOldVisualizations(HashSet<int> currentlySeenTags)
    {
        const float InactiveTimeoutSeconds = 30f; // Only destroy after 30 seconds of inactivity
        // PERFORMANCE: Reuse buffer instead of allocating new List
        m_tagsToRemoveBuffer.Clear();

        foreach (var kv in m_vizById)
        {
            // If visualization exists but tag not currently detected AND not an anchor
            if (kv.Value != null && !currentlySeenTags.Contains(kv.Key))
            {
                // Check if this tag has an anchor (if so, keep visualization)
                if (
                    m_spatialAnchorManager != null
                    && m_spatialAnchorManager.GetAnchorForTag(kv.Key) != null
                )
                {
                    continue; // Keep visualization for anchored tags
                }

                // Check how long visualization has been inactive
                if (m_vizLastActiveTime.TryGetValue(kv.Key, out float lastActiveTime))
                {
                    float inactiveTime = Time.time - lastActiveTime;

                    // Only destroy if inactive for timeout period
                    if (inactiveTime > InactiveTimeoutSeconds && !kv.Value.gameObject.activeSelf)
                    {
                        // Destroy and mark for removal
                        Destroy(kv.Value.gameObject);
                        m_tagsToRemoveBuffer.Add(kv.Key);
                    }
                }
            }
        }

        // Remove from dictionaries
        foreach (var tagId in m_tagsToRemoveBuffer)
        {
            m_vizById.Remove(tagId);
            m_vizLastActiveTime.Remove(tagId);
        }

        if (m_tagsToRemoveBuffer.Count > 0 && m_enableAllDebugLogging)
        {
            Debug.Log(
                $"[AprilTag] Cleaned up {m_tagsToRemoveBuffer.Count} old visualizations. Remaining: {m_vizById.Count}"
            );
        }
    }

    /// <summary>
    /// Process spatial anchors for detected tags
    /// </summary>
    private void ProcessSpatialAnchors(HashSet<int> seenTags)
    {
        if (!m_enableSpatialAnchors || m_spatialAnchorManager == null)
            return;

        if (m_enableAllDebugLogging && Time.frameCount % m_debugLogInterval == 0)
        {
            Debug.Log(
                $"[AprilTag] ProcessSpatialAnchors: Processing {m_detector.DetectedTags.Count()} detected tags"
            );
            foreach (var tag in m_detector.DetectedTags)
            {
                Debug.Log($"[AprilTag]   - Tag {tag.ID} at position {tag.Position}");
            }
        }

        // Process each detected tag for spatial anchor creation
        foreach (var tag in m_detector.DetectedTags)
        {
            // CRITICAL: Only process anchors for tags with initialized filtered poses
            // This prevents placing anchors at unstable initial positions during the
            // pose smoothing "warm-up" period when visualizations appear "frozen"
            if (
                !m_filteredPoses.TryGetValue(tag.ID, out var filteredPose)
                || !filteredPose.IsInitialized
            )
            {
                if (m_enableAllDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTag] Tag {tag.ID}: Skipping anchor processing - filtered pose not yet initialized"
                    );
                }
                continue; // Skip this tag until filtered pose is ready
            }

            // Calculate confidence based on corner quality and detection stability
            var confidence = CalculateDetectionConfidence(tag);

            // Debug logging for confidence values
            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[AprilTag] Tag {tag.ID} confidence: {confidence:F3} (threshold: {m_anchorConfidenceThreshold:F3})"
                );
            }

            // Use the stable filtered pose for anchor placement
            var worldPosition = filteredPose.FilteredPosition;
            var worldRotation = filteredPose.FilteredRotation;

            // Process the tag detection for spatial anchor creation
            m_spatialAnchorManager.ProcessTagDetection(
                tag.ID,
                worldPosition,
                worldRotation,
                confidence,
                m_tagSizeMeters,
                m_cornerPositionOffset,
                m_placeAnchorsAtTagCenter,
                m_anchorConfidenceThreshold,
                m_keepOutZoneMultiplier,
                m_minKeepOutRadius,
                m_maxKeepOutRadius,
                m_enableAllDebugLogging
            );
        }

        // PERFORMANCE: Remove tracking for tags that are no longer detected
        // Reuse buffer instead of allocating new HashSet
        m_currentTagIdsBuffer.Clear();
        foreach (var tag in m_detector.DetectedTags)
        {
            m_currentTagIdsBuffer.Add(tag.ID);
        }

        // Clean up filtered poses for tags no longer detected
        foreach (var tagId in m_filteredPoses.Keys.ToArray()) // ToArray() to avoid modification during iteration
        {
            if (!m_currentTagIdsBuffer.Contains(tagId))
            {
                m_spatialAnchorManager.RemoveTagTracking(tagId);

                // MEMORY LEAK FIX: Remove from filtered poses dictionary
                m_filteredPoses.Remove(tagId);

                // MEMORY LEAK FIX: Remove from detection history dictionary
                m_detectionHistory.Remove(tagId);
            }
        }
    }

    /// <summary>
    /// Calculate detection confidence for a tag based on various factors
    /// </summary>
    private float CalculateDetectionConfidence(TagPose tag)
    {
        var confidence = 1.0f; // Start with maximum confidence

        if (m_enableAllDebugLogging)
        {
            Debug.Log($"[AprilTag] Calculating confidence for tag {tag.ID}:");
        }

        // Apply corner quality assessment if enabled
        if (m_enableCornerQualityAssessment)
        {
            // Use a simplified corner quality calculation
            // In a real implementation, you might want to access actual corner quality data
            var cornerQuality = Mathf.Clamp01(
                1.0f - tag.Position.magnitude * m_distanceQualityDecayFactor
            ); // Distance-based quality
            confidence *= cornerQuality;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[AprilTag]   Corner quality: {cornerQuality:F3}, confidence after: {confidence:F3}"
                );
            }
        }

        // Apply multi-frame validation confidence
        if (m_enableMultiFrameValidation && m_detectionHistory.TryGetValue(tag.ID, out var history))
        {
            var validationConfidence = CalculateValidationConfidence(history);
            confidence *= validationConfidence;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[AprilTag]   Validation confidence: {validationConfidence:F3}, confidence after: {confidence:F3}"
                );
            }
        }

        // Apply pose smoothing confidence
        if (m_enablePoseSmoothing && m_filteredPoses.TryGetValue(tag.ID, out var filteredPose))
        {
            if (filteredPose.IsInitialized)
            {
                // Higher confidence for more stable poses - much gentler decay
                var stabilityConfidence = Mathf.Clamp01(
                    1.0f - (Time.time - filteredPose.LastUpdateTime) * m_stabilityDecayFactor
                );
                confidence *= stabilityConfidence;

                if (m_enableAllDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTag]   Stability confidence: {stabilityConfidence:F3}, confidence after: {confidence:F3}"
                    );
                }
            }
        }

        // Ensure minimum confidence to prevent 0.0f values
        var finalConfidence = Mathf.Clamp01(confidence);
        if (finalConfidence < m_minimumConfidenceThreshold)
        {
            finalConfidence = m_minimumConfidenceThreshold;
            if (m_enableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[AprilTag] Confidence clamped to minimum 0.1f for tag {tag.ID} (was {confidence:F3})"
                );
            }
        }

        return finalConfidence;
    }

    /// <summary>
    /// Calculate validation confidence based on detection history
    /// </summary>
    private float CalculateValidationConfidence(Queue<TagDetectionHistory> history)
    {
        if (history.Count < 2)
            return m_singleDetectionConfidence; // Confidence for single detections

        // PERFORMANCE: Avoid LINQ allocation - iterate queue directly using reusable buffer
        if (
            m_recentDetectionsBuffer == null
            || m_recentDetectionsBuffer.Length < m_validationFrameCount
        )
        {
            m_recentDetectionsBuffer = new TagDetectionHistory[m_validationFrameCount];
        }

        int count = 0;
        foreach (var detection in history)
        {
            if (count >= m_validationFrameCount)
                break;
            m_recentDetectionsBuffer[count++] = detection;
        }

        if (count < 2)
            return m_singleDetectionConfidence;

        // Calculate position consistency
        var positionVariance = 0f;
        var rotationVariance = 0f;

        for (var i = 1; i < count; i++)
        {
            positionVariance += Vector3.Distance(
                m_recentDetectionsBuffer[i].Position,
                m_recentDetectionsBuffer[i - 1].Position
            );
            rotationVariance += Quaternion.Angle(
                m_recentDetectionsBuffer[i].Rotation,
                m_recentDetectionsBuffer[i - 1].Rotation
            );
        }

        positionVariance /= count - 1;
        rotationVariance /= count - 1;

        // Convert variance to confidence (lower variance = higher confidence)
        var positionConfidence = Mathf.Clamp01(1.0f - positionVariance / m_maxPositionDeviation);
        var rotationConfidence = Mathf.Clamp01(1.0f - rotationVariance / m_maxRotationDeviation);

        var finalConfidence = (positionConfidence + rotationConfidence) * 0.5f;

        if (m_enableAllDebugLogging)
        {
            Debug.Log($"[AprilTag] Validation confidence calculation:");
            Debug.Log(
                $"[AprilTag]   Position variance: {positionVariance:F3}m, max: {m_maxPositionDeviation:F3}m, confidence: {positionConfidence:F3}"
            );
            Debug.Log(
                $"[AprilTag]   Rotation variance: {rotationVariance:F1}°, max: {m_maxRotationDeviation:F1}°, confidence: {rotationConfidence:F3}"
            );
            Debug.Log($"[AprilTag]   Final validation confidence: {finalConfidence:F3}");
        }

        return finalConfidence;
    }

    /// <summary>
    /// Update GPU preprocessing settings at runtime
    /// </summary>
    public void UpdateGPUPreprocessingSettings(
        AprilTagGPUPreprocessor.PreprocessingSettings newSettings
    )
    {
        m_gpuPreprocessingSettings = newSettings;

        if (m_gpuPreprocessor != null)
        {
            m_gpuPreprocessor.UpdateSettings(newSettings);

            if (m_enableAllDebugLogging)
            {
                Debug.Log("[AprilTag] GPU preprocessing settings updated");
            }
        }
    }

    /// <summary>
    /// Toggle GPU preprocessing at runtime
    /// </summary>
    public void SetGPUPreprocessingEnabled(bool enabled)
    {
        m_enableGPUPreprocessing = enabled;

        if (!enabled && m_gpuPreprocessor != null)
        {
            m_gpuPreprocessor.Dispose();
            m_gpuPreprocessor = null;

            if (m_enableAllDebugLogging)
            {
                Debug.Log("[AprilTag] GPU preprocessing disabled");
            }
        }
        else if (enabled && m_enableAllDebugLogging)
        {
            Debug.Log("[AprilTag] GPU preprocessing enabled - will initialize on next frame");
        }
    }

    // Quest-compatible debugging methods
    public void ToggleDistanceScalingRuntime()
    {
        m_enableDistanceScaling = !m_enableDistanceScaling;
        Debug.Log(
            $"[AprilTag] Distance scaling {(m_enableDistanceScaling ? "enabled" : "disabled")} via runtime call"
        );
    }

    /// <summary>
    /// Toggle debug image saving at runtime (useful for Quest debugging)
    /// </summary>
    public void ToggleDebugImageSaving()
    {
        m_debugSavePreprocessedImage = !m_debugSavePreprocessedImage;
        if (m_debugSavePreprocessedImage)
        {
            var path = GetDebugImagePath();
            Debug.Log($"[AprilTag] Debug image saving ENABLED. Images will be saved to: {path}");
            Debug.Log("[AprilTag] On Quest, use 'adb pull' to retrieve images:");
            Debug.Log($"[AprilTag] adb pull \"{path}\" .");
        }
        else
        {
            Debug.Log("[AprilTag] Debug image saving DISABLED");
        }
    }

    /// <summary>
    /// Force save a debug image immediately (useful for Quest debugging)
    /// </summary>
    public void ForceSaveDebugImage()
    {
        if (m_rgba != null && m_rgba.Length > 0 && m_detW > 0 && m_detH > 0)
        {
            Debug.Log("[AprilTag] Force saving debug image...");

            // Log current detection state
            var detectionCount = m_detector?.DetectedTags?.Count() ?? 0;
            Debug.Log($"[AprilTag] Current detections: {detectionCount}");

            SaveDebugImage(m_rgba, m_detW, m_detH, m_enableGPUPreprocessing);

            if (m_debugSaveBothRawAndProcessed && m_webcamPipeline != null)
            {
                var wct = m_webcamPipeline.GetActiveWebCamTexture();
                if (wct != null && wct.isPlaying)
                {
                    var rawPixels = wct.GetPixels32();
                    SaveDebugImage(rawPixels, wct.width, wct.height, false);
                }
            }
        }
        else
        {
            Debug.LogWarning("[AprilTag] Cannot save debug image - no valid image data available");
        }
    }

    public void SetPositionScaleFactor(float scale)
    {
        m_positionScaleFactor = scale;
        Debug.Log($"[AprilTag] Position scale factor set to {scale} via runtime call");
    }

    public void LogCurrentSettings()
    {
        var cam = GetCorrectCameraReference();
        Debug.Log($"[AprilTag] Current Settings:");
        Debug.Log($"  - Position Scale Factor: {m_positionScaleFactor}");
        Debug.Log($"  - Distance Scaling: {m_enableDistanceScaling}");
        Debug.Log($"  - Passthrough Raycasting: {m_usePassthroughRaycasting}");
        Debug.Log($"  - Min Detection Distance: {m_minDetectionDistance}");
        Debug.Log($"  - Max Detection Distance: {m_maxDetectionDistance}");
        Debug.Log($"  - Camera: {cam.name} at {cam.position:F3}");
    }

    private void HandleQuestDebugInput()
    {
        // Quest controller input handling for runtime calibration
        if (m_enableConfigurationTool)
        {
            // Check if right grip is being held
            var rightGripHeld = OVRInput.Get(
                OVRInput.RawButton.RHandTrigger,
                OVRInput.Controller.RTouch
            );

            // Right A button = move cube right (or left if grip is held)
            if (OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch))
            {
                if (rightGripHeld)
                {
                    m_cornerPositionOffset += new Vector3(-m_runtimeCalibrationStep, 0f, 0f); // Move left
                }
                else
                {
                    m_cornerPositionOffset += new Vector3(m_runtimeCalibrationStep, 0f, 0f); // Move right
                }
                SaveRuntimeOffset();
                Debug.Log(
                    $"[AprilTag] Runtime Offset: X={m_cornerPositionOffset.x:F3}, Y={m_cornerPositionOffset.y:F3}, Z={m_cornerPositionOffset.z:F3}"
                );
            }

            // Right B button = move cube up (or down if grip is held)
            if (OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch))
            {
                if (rightGripHeld)
                {
                    m_cornerPositionOffset += new Vector3(0f, -m_runtimeCalibrationStep, 0f); // Move down
                }
                else
                {
                    m_cornerPositionOffset += new Vector3(0f, m_runtimeCalibrationStep, 0f); // Move up
                }
                SaveRuntimeOffset();
                Debug.Log(
                    $"[AprilTag] Runtime Offset: X={m_cornerPositionOffset.x:F3}, Y={m_cornerPositionOffset.y:F3}, Z={m_cornerPositionOffset.z:F3}"
                );
            }
        }

        // Debug image capture controls (always available when Quest debugging is enabled)
        // Left controller trigger + A button = Toggle debug image saving
        if (
            OVRInput.Get(OVRInput.RawButton.LIndexTrigger, OVRInput.Controller.LTouch)
            && OVRInput.GetDown(OVRInput.RawButton.X, OVRInput.Controller.LTouch)
        )
        {
            ToggleDebugImageSaving();
        }

        // Left controller trigger + B button = Force save debug image
        if (
            OVRInput.Get(OVRInput.RawButton.LIndexTrigger, OVRInput.Controller.LTouch)
            && OVRInput.GetDown(OVRInput.RawButton.Y, OVRInput.Controller.LTouch)
        )
        {
            ForceSaveDebugImage();
        }

        // Log the current settings every 5 seconds when debugging is enabled
        if (m_enableAllDebugLogging && Time.frameCount % m_verboseDebugLogInterval == 0)
        {
            LogCurrentSettings();
        }
    }

    // PhotonVision-inspired pose filtering implementation
    // Based on PhotonVision's temporal filtering approach for stable pose estimation
    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Temporal smoothing)
    private Vector3 FilterTagPosition(
        Vector3 rawPosition,
        Vector3 previousPosition,
        float deltaTime,
        bool isInitialized
    )
    {
        if (!m_enablePoseSmoothing || !isInitialized)
        {
            return rawPosition;
        }

        // Exponential smoothing filter similar to PhotonVision's approach
        // Uses time-based smoothing factor for frame-rate independence
        var smoothingFactor = Mathf.Exp(-deltaTime / m_positionSmoothingTime);

        // Clamp smoothing factor to prevent instability
        smoothingFactor = Mathf.Clamp01(smoothingFactor);

        // Apply exponential smoothing
        var filteredPosition = Vector3.Lerp(rawPosition, previousPosition, smoothingFactor);

        if (m_enableAllDebugLogging && Time.frameCount % m_verboseDebugLogInterval == 0)
        {
            Debug.Log(
                $"[AprilTag] Position Filter - Raw: {rawPosition:F3}, Filtered: {filteredPosition:F3}, Factor: {smoothingFactor:F3}"
            );
        }

        return filteredPosition;
    }

    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Temporal smoothing)
    private Quaternion FilterTagRotation(
        Quaternion rawRotation,
        Quaternion previousRotation,
        float deltaTime,
        bool isInitialized
    )
    {
        if (!m_enablePoseSmoothing || !isInitialized)
        {
            return rawRotation;
        }

        // Spherical linear interpolation for rotation smoothing
        // Similar to PhotonVision's rotation filtering approach
        var smoothingFactor = Mathf.Exp(-deltaTime / m_rotationSmoothingTime);
        smoothingFactor = Mathf.Clamp01(smoothingFactor);

        // Use Slerp for smooth rotation interpolation
        var filteredRotation = Quaternion.Slerp(rawRotation, previousRotation, smoothingFactor);

        return filteredRotation;
    }

    // PhotonVision-inspired multi-frame validation
    // Validates detections against recent history to reject outliers
    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Validation gate)
    private bool ValidateTagDetection(
        int tagId,
        Vector3 position,
        Quaternion rotation,
        float cornerQuality
    )
    {
        if (!m_enableMultiFrameValidation)
        {
            return true;
        }

        // Initialize history queue if needed
        if (!m_detectionHistory.ContainsKey(tagId))
        {
            m_detectionHistory[tagId] = new Queue<TagDetectionHistory>();
        }

        var history = m_detectionHistory[tagId];

        // If we don't have enough history, accept the detection
        if (history.Count < 2)
        {
            history.Enqueue(new TagDetectionHistory(position, rotation, cornerQuality));

            // Limit history size (PhotonVision approach)
            while (history.Count > m_validationFrameCount)
            {
                _ = history.Dequeue();
            }

            return true;
        }

        // Calculate average position and rotation from recent history
        var avgPosition = Vector3.zero;
        var avgEulerAngles = Vector3.zero;
        var validCount = 0;

        foreach (var detection in history)
        {
            if (
                detection.IsValid
                && (Time.time - detection.Timestamp) < m_validationRecentDetectionTime
            ) // Only use recent detections
            {
                avgPosition += detection.Position;
                avgEulerAngles += detection.Rotation.eulerAngles;
                validCount++;
            }
        }

        if (validCount == 0)
        {
            return true; // No valid history, accept detection
        }

        avgPosition /= validCount;
        avgEulerAngles /= validCount;

        // Check position deviation (PhotonVision's consistency check approach)
        var positionDeviation = Vector3.Distance(position, avgPosition);
        if (positionDeviation > m_maxPositionDeviation)
        {
            if (m_enableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[AprilTag] Tag {tagId} rejected - Position deviation: {positionDeviation:F3}m > {m_maxPositionDeviation:F3}m"
                );
            }
            return false;
        }

        // Check rotation deviation
        var currentEuler = rotation.eulerAngles;
        var rotationDeviation = Mathf.Max(
            Mathf.Abs(Mathf.DeltaAngle(currentEuler.x, avgEulerAngles.x)),
            Mathf.Abs(Mathf.DeltaAngle(currentEuler.y, avgEulerAngles.y)),
            Mathf.Abs(Mathf.DeltaAngle(currentEuler.z, avgEulerAngles.z))
        );

        if (rotationDeviation > m_maxRotationDeviation)
        {
            if (m_enableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[AprilTag] Tag {tagId} rejected - Rotation deviation: {rotationDeviation:F1}° > {m_maxRotationDeviation:F1}°"
                );
            }
            return false;
        }

        // Detection passed validation, add to history
        history.Enqueue(new TagDetectionHistory(position, rotation, cornerQuality));

        // Limit history size
        while (history.Count > m_validationFrameCount)
        {
            _ = history.Dequeue();
        }

        return true;
    }

    // PhotonVision-inspired corner quality assessment
    // Analyzes corner sharpness and geometric consistency
    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Quality metric)
    private float CalculateCornerQuality(Vector2[] corners)
    {
        if (!m_enableCornerQualityAssessment || corners == null || corners.Length != 4)
        {
            return 1.0f; // Default quality if assessment disabled
        }

        var quality = 1.0f;

        // Check geometric consistency (PhotonVision approach)
        // Measure how close the corners are to forming a proper quadrilateral

        // Calculate side lengths
        var sideLengths = new float[4];
        for (var i = 0; i < 4; i++)
        {
            var nextIndex = (i + 1) % 4;
            sideLengths[i] = Vector2.Distance(corners[i], corners[nextIndex]);
        }

        // Check for degenerate cases (very small or very large sides)
        var minSide = Mathf.Min(sideLengths);
        var maxSide = Mathf.Max(sideLengths);

        if (minSide < m_minCornerSideLength) // Too small in pixels
        {
            quality *= m_smallTagQualityPenalty;
        }

        if (maxSide > m_maxCornerSideLength) // Too large, likely false detection
        {
            quality *= m_largeTagQualityPenalty;
        }

        // Check aspect ratio consistency (should be roughly square for AprilTags)
        var aspectRatio = maxSide / Mathf.Max(minSide, 0.1f);
        if (aspectRatio > m_maxAspectRatio) // Too elongated
        {
            quality *= m_elongatedTagQualityPenalty;
        }

        // Check corner angles (should be close to 90 degrees for AprilTags)
        var totalAngleDeviation = 0f;
        for (var i = 0; i < 4; i++)
        {
            var prev = corners[(i + 3) % 4];
            var curr = corners[i];
            var next = corners[(i + 1) % 4];

            var v1 = (prev - curr).normalized;
            var v2 = (next - curr).normalized;

            var angle = Vector2.Angle(v1, v2);
            var angleDeviation = Mathf.Abs(angle - 90f);
            totalAngleDeviation += angleDeviation;
        }

        var avgAngleDeviation = totalAngleDeviation / 4f;
        if (avgAngleDeviation > m_maxCornerAngleDeviation) // Corners too far from 90 degrees
        {
            quality *= Mathf.Lerp(
                1.0f,
                0.2f,
                (avgAngleDeviation - m_maxCornerAngleDeviation) / (m_maxCornerAngleDeviation * 2f)
            );
        }

        // Check for convexity (corners should form a convex quadrilateral)
        var isConvex = true;
        for (var i = 0; i < 4; i++)
        {
            var p1 = corners[i];
            var p2 = corners[(i + 1) % 4];
            var p3 = corners[(i + 2) % 4];

            // Cross product to check turn direction
            var cross = (p2.x - p1.x) * (p3.y - p1.y) - (p2.y - p1.y) * (p3.x - p1.x);
            if (i == 0)
            {
                // Set expected sign
            }
            else if ((cross > 0) != (i % 2 == 1))
            {
                isConvex = false;
                break;
            }
        }

        if (!isConvex)
        {
            quality *= m_nonConvexQualityPenalty;
        }

        // Clamp quality to valid range
        quality = Mathf.Clamp01(quality);

        if (m_enableAllDebugLogging && Time.frameCount % m_cornerQualityLogInterval == 0)
        {
            Debug.Log(
                $"[AprilTag] Corner Quality Assessment - Quality: {quality:F3}, AspectRatio: {aspectRatio:F2}, AngleDeviation: {avgAngleDeviation:F1}°, Convex: {isConvex}"
            );
        }

        return quality;
    }

    private void SaveRuntimeOffset()
    {
        if (m_saveRuntimeOffset)
        {
            PlayerPrefs.SetFloat("AprilTag_CornerOffset_X", m_cornerPositionOffset.x);
            PlayerPrefs.SetFloat("AprilTag_CornerOffset_Y", m_cornerPositionOffset.y);
            PlayerPrefs.SetFloat("AprilTag_CornerOffset_Z", m_cornerPositionOffset.z);
            PlayerPrefs.Save();
        }
    }

    private void LoadRuntimeOffset()
    {
        if (m_saveRuntimeOffset && PlayerPrefs.HasKey("AprilTag_CornerOffset_X"))
        {
            m_cornerPositionOffset = new Vector3(
                PlayerPrefs.GetFloat("AprilTag_CornerOffset_X", 0f),
                PlayerPrefs.GetFloat("AprilTag_CornerOffset_Y", 0f),
                PlayerPrefs.GetFloat("AprilTag_CornerOffset_Z", 0f)
            );
            Debug.Log($"[AprilTag] Loaded runtime offset: {m_cornerPositionOffset}");
        }
    }

    private void SaveDebugImage(
        Color32[] pixels,
        int width,
        int height,
        bool isPreprocessed = false
    )
    {
        try
        {
            // Create texture from pixels
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);

            // Draw detection overlays if enabled and we have detections
            if (m_debugIncludeDetectionOverlay && m_detector?.DetectedTags != null)
            {
                DrawDetectionOverlays(tex);
            }

            tex.Apply();

            // Generate filename with timestamp
            var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var imageType = isPreprocessed ? "preprocessed" : "raw";
            var filename = $"AprilTag_Debug_{imageType}_{timestamp}.png";

            // Use persistent data path for Quest compatibility
            string debugPath = GetDebugImagePath();
            if (!System.IO.Directory.Exists(debugPath))
            {
                System.IO.Directory.CreateDirectory(debugPath);
            }

            var fullPath = System.IO.Path.Combine(debugPath, filename);

            // Save the image
            var bytes = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes(fullPath, bytes);

            Debug.Log($"[AprilTag] Saved debug image to: {fullPath}");

            // Clean up old debug images
            CleanupOldDebugImages(debugPath);

            // Log additional debug info
            if (m_enableAllDebugLogging)
            {
                var detectionCount = m_detector?.DetectedTags?.Count() ?? 0;
                Debug.Log(
                    $"[AprilTag] Debug image info - Type: {imageType}, Size: {width}x{height}, Detections: {detectionCount}"
                );
            }

            Destroy(tex);
        }
        catch (Exception e)
        {
            Debug.LogError($"[AprilTag] Failed to save debug image: {e.Message}");
        }
    }

    private string GetDebugImagePath()
    {
        // Use persistent data path which works on all platforms including Quest
#if UNITY_ANDROID && !UNITY_EDITOR
        // On Quest, this will be something like: /storage/emulated/0/Android/data/com.yourcompany.appname/files/AprilTagDebug
        return System.IO.Path.Combine(Application.persistentDataPath, "AprilTagDebug");
#else
        // In editor or other platforms, use a more accessible location
        return System.IO.Path.Combine(Application.dataPath, "..", "AprilTagDebug");
#endif
    }

    private void DrawDetectionOverlays(Texture2D tex)
    {
        try
        {
            if (m_detector?.DetectedTags == null || !m_detector.DetectedTags.Any())
            {
                if (m_enableAllDebugLogging)
                {
                    Debug.Log("[AprilTag] No detections to draw overlays for");
                }
                return;
            }

            // Get raw detections for corner data
            var rawDetections =
                m_webcamPipeline != null
                    ? m_webcamPipeline.GetRawDetections(m_detector)
                    : new System.Collections.Generic.List<object>();

            var overlayCount = 0;
            var totalDetections = m_detector.DetectedTags?.Count() ?? 0;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[AprilTag] Starting overlay drawing for {totalDetections} detected tags"
                );
                foreach (var tag in m_detector.DetectedTags)
                {
                    Debug.Log($"[AprilTag] Processing tag {tag.ID} for overlay drawing");
                }
            }

            foreach (var tag in m_detector.DetectedTags)
            {
                // Use corner-based center since it's working and gives correct image coordinates
                Vector2? center = null;
                string centerSource = "corner-based";

                // Get corner center using the same method that works in Update()
                var cornerCenter = m_transforms.TryGetCornerBasedCenter(tag.ID, rawDetections);
                if (cornerCenter.HasValue)
                {
                    center = cornerCenter.Value;
                }
                else if (m_enableAllDebugLogging)
                {
                    Debug.LogWarning(
                        $"[AprilTag] Could not extract corner center for tag {tag.ID}"
                    );
                }

                if (center.HasValue)
                {
                    // Convert center to debug image coordinates
                    var scaleX = (float)tex.width / m_detW;
                    var scaleY = (float)tex.height / m_detH;

                    // Apply scaling to the center point
                    var scaledCenter = new Vector2(
                        center.Value.x * scaleX,
                        center.Value.y * scaleY
                    );

                    // Use a larger tag size for better visibility
                    var tagSizePixels = 60f; // Increased size for better debugging visibility
                    var halfSize = tagSizePixels * 0.5f;

                    if (m_enableAllDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTag] Tag {tag.ID} size calculation: tagSizePixels={tagSizePixels}, halfSize={halfSize}"
                        );
                    }

                    // Check if the overlay would be within image bounds
                    var minX = scaledCenter.x - halfSize;
                    var maxX = scaledCenter.x + halfSize;
                    var minY = scaledCenter.y - halfSize;
                    var maxY = scaledCenter.y + halfSize;

                    if (minX >= 0 && maxX < tex.width && minY >= 0 && maxY < tex.height)
                    {
                        // Create 4 corners for a square around the scaled center
                        var corners = new Vector2[]
                        {
                            new Vector2(scaledCenter.x - halfSize, scaledCenter.y - halfSize), // Top-left
                            new Vector2(scaledCenter.x + halfSize, scaledCenter.y - halfSize), // Top-right
                            new Vector2(scaledCenter.x + halfSize, scaledCenter.y + halfSize), // Bottom-right
                            new Vector2(scaledCenter.x - halfSize, scaledCenter.y + halfSize), // Bottom-left
                        };

                        if (m_enableAllDebugLogging)
                        {
                            Debug.Log(
                                $"[AprilTag] Created corners for tag {tag.ID}: TL=({corners[0].x:F1}, {corners[0].y:F1}), TR=({corners[1].x:F1}, {corners[1].y:F1}), BR=({corners[2].x:F1}, {corners[2].y:F1}), BL=({corners[3].x:F1}, {corners[3].y:F1})"
                            );
                        }

                        // Draw tag outline
                        DrawTagOutline(tex, corners, tag.ID);
                        overlayCount++;

                        if (m_enableAllDebugLogging)
                        {
                            Debug.Log(
                                $"[AprilTag] Drew overlay for tag {tag.ID} - Source: {centerSource}, Original center: {center.Value}, Scaled center: {scaledCenter}, Tag size: {tagSizePixels}px"
                            );
                        }

                        // Draw tag ID and position info
                        DrawTagInfo(tex, scaledCenter, tag);
                    }
                    else if (m_enableAllDebugLogging)
                    {
                        Debug.LogWarning(
                            $"[AprilTag] Tag {tag.ID} overlay would be outside image bounds - Source: {centerSource}, Center: {scaledCenter}, Size: {tagSizePixels}px, Bounds: ({minX:F1}, {minY:F1}) to ({maxX:F1}, {maxY:F1}), Image: {tex.width}x{tex.height}"
                        );
                    }
                }
                else if (m_enableAllDebugLogging)
                {
                    Debug.LogWarning(
                        $"[AprilTag] Could not determine center for tag {tag.ID} using any method"
                    );
                }
            }

            if (m_enableAllDebugLogging)
            {
                Debug.Log($"[AprilTag] Drew {overlayCount} detection overlays on debug image");
                Debug.Log(
                    $"[AprilTag] Debug image dimensions: {tex.width}x{tex.height}, Detection dimensions: {m_detW}x{m_detH}"
                );
                Debug.Log(
                    $"[AprilTag] Scale factors: X={((float)tex.width / m_detW):F3}, Y={((float)tex.height / m_detH):F3}"
                );
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                $"[AprilTag] Failed to draw detection overlays: {e.Message}\n{e.StackTrace}"
            );
        }
    }

    private void DrawTagOutline(Texture2D tex, Vector2[] corners, int tagId)
    {
        // Choose color based on tag ID
        var color = GetDebugColorForTag(tagId);

        if (m_enableAllDebugLogging)
        {
            Debug.Log(
                $"[AprilTag] Drawing outline for tag {tagId} with color {color} at corners: [{corners[0]}, {corners[1]}, {corners[2]}, {corners[3]}]"
            );
        }

        // Draw lines between corners
        for (int i = 0; i < 4; i++)
        {
            var start = corners[i];
            var end = corners[(i + 1) % 4];
            DrawLine(tex, start, end, color, 2);
        }

        // Draw corner markers
        for (int i = 0; i < 4; i++)
        {
            DrawCircle(tex, corners[i], 5, color);
        }
    }

    private void DrawTagInfo(Texture2D tex, Vector2 position, TagPose tag)
    {
        // This is a simplified version - in a real implementation you might want to use TextMeshPro
        // For now, just draw a colored square to indicate the tag ID
        var color = GetDebugColorForTag(tag.ID);
        var infoPos = position + new Vector2(10, -10);

        if (m_enableAllDebugLogging)
        {
            Debug.Log(
                $"[AprilTag] Drawing tag info for tag {tag.ID} at position {infoPos} with color {color} (20x10 rect)"
            );
        }

        DrawFilledRect(tex, infoPos, 20, 10, color);
    }

    private Color GetDebugColorForTag(int tagId)
    {
        // Generate consistent colors for tag IDs
        var colors = new Color[]
        {
            Color.red,
            Color.green,
            Color.blue,
            Color.yellow,
            Color.magenta,
            Color.cyan,
        };
        var color = colors[tagId % colors.Length];

        if (m_enableAllDebugLogging)
        {
            Debug.Log(
                $"[AprilTag] Tag {tagId} assigned color {color} (index {tagId % colors.Length})"
            );
        }

        return color;
    }

    private void DrawLine(Texture2D tex, Vector2 start, Vector2 end, Color color, int thickness = 1)
    {
        // Simple line drawing algorithm
        int x0 = (int)start.x;
        int y0 = (int)start.y;
        int x1 = (int)end.x;
        int y1 = (int)end.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            // Draw with thickness
            for (int tx = -thickness / 2; tx <= thickness / 2; tx++)
            {
                for (int ty = -thickness / 2; ty <= thickness / 2; ty++)
                {
                    SetPixelSafe(tex, x0 + tx, y0 + ty, color);
                }
            }

            if (x0 == x1 && y0 == y1)
                break;
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private void DrawCircle(Texture2D tex, Vector2 center, int radius, Color color)
    {
        int cx = (int)center.x;
        int cy = (int)center.y;

        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                if (x * x + y * y <= radius * radius)
                {
                    SetPixelSafe(tex, cx + x, cy + y, color);
                }
            }
        }
    }

    private void DrawFilledRect(Texture2D tex, Vector2 position, int width, int height, Color color)
    {
        int x = (int)position.x;
        int y = (int)position.y;

        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                SetPixelSafe(tex, x + dx, y + dy, color);
            }
        }
    }

    private void SetPixelSafe(Texture2D tex, int x, int y, Color color)
    {
        if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
        {
            tex.SetPixel(x, y, color);
        }
        else if (m_enableAllDebugLogging)
        {
            Debug.LogWarning(
                $"[AprilTag] Attempted to set pixel at ({x}, {y}) outside bounds ({tex.width}x{tex.height})"
            );
        }
    }

    private void CleanupOldDebugImages(string debugPath)
    {
        try
        {
            var files = System
                .IO.Directory.GetFiles(debugPath, "AprilTag_Debug_*.png")
                .OrderBy(f => System.IO.File.GetCreationTime(f))
                .ToArray();

            // Delete oldest files if we exceed the limit
            while (files.Length > m_maxDebugImages)
            {
                System.IO.File.Delete(files[0]);
                Debug.Log($"[AprilTag] Deleted old debug image: {files[0]}");
                files = files.Skip(1).ToArray();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AprilTag] Failed to cleanup old debug images: {e.Message}");
        }
    }

    /// <summary>
    /// Async detection coroutine (Unity-safe pattern)
    /// Spreads detection work across multiple frames to prevent blocking
    /// </summary>
    private System.Collections.IEnumerator DetectTagsAsync(Color32[] pixels)
    {
        m_detectionInProgress = true;

        // Yield to next frame before heavy processing
        // This allows Unity to render current frame before we do expensive detection
        yield return null;

        // Run detection on main thread (no copy needed - we're already on main thread)
        try
        {
            if (m_detector != null)
            {
                // PERFORMANCE: No Array.Copy needed - pixels array is already on heap
                // and won't be modified until next detection cycle
                m_detector.ProcessImage(pixels.AsSpan(), m_horizontalFovDeg, m_tagSizeMeters);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AprilTag] Async detection error: {ex.Message}");
        }

        // Schedule next detection (safe - we're on main thread)
        m_nextDetectT = Time.time + 1f / Mathf.Max(1f, m_maxDetectionsPerSecond);
        m_detectionInProgress = false;
    }

    private void DisposeDetector()
    {
        // Stop any running detection coroutine
        if (m_detectionCoroutine != null)
        {
            StopCoroutine(m_detectionCoroutine);
            m_detectionCoroutine = null;
        }
        m_detectionInProgress = false;

        m_detector?.Dispose();
        m_detector = null;

        m_gpuPreprocessor?.Dispose();
        m_gpuPreprocessor = null;
    }
}
