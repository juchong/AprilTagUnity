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

    [Tooltip("Quest-specific: Use proper passthrough camera raycasting for accurate positioning")]
    [SerializeField]
    private bool m_usePassthroughRaycasting = true;

    [Tooltip("Environment raycast manager for accurate 3D positioning (auto-created if null)")]
    [SerializeField]
    private EnvironmentRaycastManager m_environmentRaycastManager;

    [Tooltip("Auto-create EnvironmentRaycastManager if not found")]
    [SerializeField]
    private bool m_autoCreateRaycastManager = true;

    [Tooltip(
        "Scale factor to adjust tag positioning (1.0 = normal, 0.5 = half size, 2.0 = double size)"
    )]
    [SerializeField]
    private float m_positionScaleFactor = 1.0f;

    [Tooltip("Minimum detection distance in meters (for very close tags)")]
    [SerializeField]
    private float m_minDetectionDistance = 0.3f;

    [Tooltip("Maximum detection distance in meters (optimized for 1280x1280 resolution)")]
    [SerializeField]
    private float m_maxDetectionDistance = 5.0f;

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

    [Header("Gravity-Aligned Constraints")]
    [Tooltip(
        "Enable gravity-aligned constraints (tags mounted flat on walls perpendicular to gravity)\n"
            + "FIRST Robotics: ENABLE - tags are always on vertical walls\n"
            + "General use: Disable if tags may be on angled surfaces, ceilings, or floors"
    )]
    [SerializeField]
    private bool m_enableGravityAlignedConstraints = true;

    [Tooltip(
        "Gravity direction in world space\n"
            + "Default: Vector3.down (0, -1, 0)\n"
            + "Quest uses standard Unity coordinates where Y+ is up"
    )]
    [SerializeField]
    private Vector3 m_gravityDirection = Vector3.down;

    [Tooltip(
        "Tolerance angle (degrees) for perpendicular alignment to gravity\n"
            + "15° = accepts walls within ±15° of vertical\n"
            + "Lower = stricter (may reject good detections on slightly angled walls)\n"
            + "Higher = more permissive (may accept tags on angled surfaces)"
    )]
    [SerializeField]
    private float m_gravityAlignmentTolerance = 15f;

    [Tooltip(
        "Apply gravity constraint to position estimation\n"
            + "Validates that raycasted surfaces are vertical before accepting position\n"
            + "Rejects positions on horizontal surfaces (floor/ceiling)"
    )]
    [SerializeField]
    private bool m_applyGravityConstraintToPosition = true;

    [Tooltip(
        "Apply gravity constraint to rotation estimation\n"
            + "Enforces tag surface normal is horizontal (perpendicular to gravity)\n"
            + "Corrects rotations to align with vertical wall assumption"
    )]
    [SerializeField]
    private bool m_applyGravityConstraintToRotation = true;

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
    private float m_maxDetectionsPerSecond = 15f;

    [Header("Adaptive Decimation (Phase 1)")]
    [Tooltip(
        "Enable adaptive decimation based on tag distance (0.3m-5m range). Currently framework only - full implementation pending."
    )]
    [SerializeField]
    private bool m_enableAdaptiveDecimation = false; // Disabled until two-pass detection implemented

    [Header("Async Detection")]
    [Tooltip("Run detection on background thread to prevent main thread blocking")]
    [SerializeField]
    private bool m_useAsyncDetection = true;

    // Fallback FOV value if camera intrinsics unavailable (not exposed in inspector)
    private const float FALLBACK_HORIZONTAL_FOV_DEG = 78f;

    [Header("Diagnostics")]
    [Tooltip("Enable all debug logging (can be toggled at runtime)")]
    [SerializeField]
    private bool m_enableAllDebugLogging = false;

    [Tooltip(
        "Frame interval for debug logs (higher = less frequent, e.g., 60 = ~1 second at 60 FPS)"
    )]
    [SerializeField]
    private int m_logInterval = 60;

    [Tooltip("Frame interval for verbose debug logs (e.g., 300 = ~4 seconds at 72 FPS)")]
    [SerializeField]
    private int m_verboseLogInterval = 300;

    // Public accessors for shared configuration (consumed by AprilTagTransforms)
    /// <summary>
    /// Enable or disable detailed debug logging.
    /// </summary>
    public bool EnableAllDebugLogging => m_enableAllDebugLogging;

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

    /// <summary>
    /// Whether gravity-aligned constraints are enabled (tags perpendicular to gravity).
    /// </summary>
    public bool EnableGravityAlignedConstraints => m_enableGravityAlignedConstraints;

    /// <summary>
    /// Gravity direction in world space.
    /// </summary>
    public Vector3 GravityDirection => m_gravityDirection.normalized;

    /// <summary>
    /// Tolerance angle for gravity alignment (degrees).
    /// </summary>
    public float GravityAlignmentTolerance => m_gravityAlignmentTolerance;

    /// <summary>
    /// Whether to apply gravity constraint to position estimation.
    /// </summary>
    public bool ApplyGravityConstraintToPosition => m_applyGravityConstraintToPosition;

    /// <summary>
    /// Whether to apply gravity constraint to rotation estimation.
    /// </summary>
    public bool ApplyGravityConstraintToRotation => m_applyGravityConstraintToRotation;

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

    [Header("PhotonVision-Inspired Filtering")]
    // Note: Pose filtering configuration now in AprilTagPoseFilter component
    // This component is auto-created and configured automatically

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

    [Tooltip(
        "Tag IDs to ignore (e.g., tags on curved surfaces or damaged tags). Array of tag numbers like [42, 99, 15]. Ignored tags will not be detected, visualized, or create anchors."
    )]
    [SerializeField]
    private int[] m_ignoredTagIds = new int[0];

    [Tooltip(
        "How good does a tag need to be to create an anchor? Higher = stricter (fewer anchors, higher quality). Competition: 0.6-0.7, Practice: 0.3-0.4. Range: 0.0-1.0"
    )]
    [Range(0.0f, 1.0f)]
    [SerializeField]
    private float m_anchorConfidenceThreshold = 0.1f; // GATE: Minimum confidence to create anchor

    [Tooltip(
        "Distance-based quality decay factor. Higher = more penalty for far tags. Default 0.01 means 1% confidence loss per meter."
    )]
    [SerializeField]
    private float m_distanceQualityDecayFactor = 0.01f;

    [Tooltip(
        "Stability confidence decay factor (per second). Higher = faster confidence loss when tag not updated. Default 0.01 means 1% loss per second."
    )]
    [SerializeField]
    private float m_stabilityDecayFactor = 0.01f;

    [Tooltip(
        "What's the absolute worst confidence we'll ever report? Safety floor to prevent 0.0 confidence. Rarely needs adjustment. Keep at 0.1 unless you have specific reason to change."
    )]
    [Range(0.0f, 1.0f)]
    [SerializeField]
    private float m_minimumConfidenceThreshold = 0.1f; // FLOOR: Clamp confidence to this minimum

    [Tooltip(
        "Place spatial anchors at the exact center of tags (subtracting the corner position offset). Should be enabled for accurate field localization."
    )]
    [SerializeField]
    private bool m_placeAnchorsAtTagCenter = true;

    [Header("Keep Out Zone Settings")]
    [Tooltip(
        "Multiplier for keep out zone radius based on tag size. Prevents duplicate anchors for same tag. FIRST Robotics: 0.5 (tags spaced apart), Dense mounting: 0.3. Formula: radius = tag_size × multiplier"
    )]
    [Range(0.1f, 2.0f)]
    [SerializeField]
    private float m_keepOutZoneMultiplier = 0.3f;

    [Tooltip(
        "Minimum keep out zone radius in meters. Prevents zones from being too small to be effective. Typical: 0.02m (2cm) for small tags."
    )]
    [Range(0.01f, 0.5f)]
    [SerializeField]
    private float m_minKeepOutRadius = 0.02f;

    [Tooltip(
        "Maximum keep out zone radius in meters. Prevents zones from blocking adjacent tags. Typical: 0.1m (10cm) for standard spacing."
    )]
    [Range(0.1f, 1.0f)]
    [SerializeField]
    private float m_maxKeepOutRadius = 0.1f;

    // CPU buffers
    private Color32[] m_rgba;

    // GPU preprocessor
    private AprilTagGPUPreprocessor m_gpuPreprocessor;

    // Async detection state (using coroutine pattern)
    private bool m_detectionInProgress = false;
    private System.Collections.IEnumerator m_detectionCoroutine = null;

    // Shared transforms helper (single source of truth for transform math)
    private AprilTagTransforms m_transforms;

    // Pose filter for temporal smoothing and validation
    private AprilTagPoseFilter m_poseFilter;

    // Debug image saver for Quest debugging
    private AprilTagDebugImageSaver m_debugImageSaver;

    // Distance adaptation system (Phase 1 & 3)
    private AprilTagDistanceAdaptation m_distanceAdaptation;

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

    // Track when visualizations were last active (for cleanup)
    private readonly Dictionary<int, float> m_vizLastActiveTime = new();

    // PERFORMANCE: Reusable buffers to avoid allocations per frame
    private readonly HashSet<int> m_seenTagsBuffer = new();
    private readonly HashSet<int> m_currentTagIdsBuffer = new();
    private readonly List<int> m_tagsToRemoveBuffer = new();

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

    /// <summary>
    /// Get horizontal FOV calculated from camera intrinsics
    /// </summary>
    private float GetCalculatedFOV()
    {
        try
        {
            var eye =
                m_webcamPipeline != null
                    ? m_webcamPipeline.GetWebCamManagerEye()
                    : PassthroughCameraEye.Left;

            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
            float focalLengthX = intrinsics.FocalLength.x;
            float imageWidth = intrinsics.Resolution.x;

            // Calculate FOV: FOV = 2 * atan(imageWidth / (2 * focalLength))
            float fovRadians = 2f * Mathf.Atan(imageWidth / (2f * focalLengthX));
            return fovRadians * Mathf.Rad2Deg;
        }
        catch (System.Exception e)
        {
            if (m_enableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[AprilTag] Failed to calculate FOV from intrinsics: {e.Message}. Using fallback: {FALLBACK_HORIZONTAL_FOV_DEG}°"
                );
            }
            return FALLBACK_HORIZONTAL_FOV_DEG; // Fallback to constant value
        }
    }

    private void Awake()
    {
        // Fix Input System issues on startup
        FixEventSystemInputModules();

        // Start the permissions manager
        StartPermissionsManager();

        // Subscribe to permission events
        AprilTagPermissionsManager.OnAllPermissionsGranted += OnAllPermissionsGranted;
        AprilTagPermissionsManager.OnPermissionsDenied += OnPermissionsDenied;

        // Auto-find or create EnvironmentRaycastManager if not assigned
        if (m_environmentRaycastManager == null && m_usePassthroughRaycasting)
        {
            m_environmentRaycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();

            if (m_environmentRaycastManager == null && m_autoCreateRaycastManager)
            {
                // Create EnvironmentRaycastManager automatically
                var raycastManagerObj = new GameObject("EnvironmentRaycastManager");
                m_environmentRaycastManager =
                    raycastManagerObj.AddComponent<EnvironmentRaycastManager>();

                if (m_enableAllDebugLogging)
                {
                    Debug.Log(
                        "[Controller] Auto-created EnvironmentRaycastManager for passthrough raycasting"
                    );
                }
            }
            else if (m_environmentRaycastManager == null && m_enableAllDebugLogging)
            {
                Debug.LogWarning(
                    "[Controller] No EnvironmentRaycastManager found and auto-create disabled. Passthrough raycasting will not work properly."
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

        // Ensure we have pose filter
        if (m_poseFilter == null)
        {
            m_poseFilter =
                FindFirstObjectByType<AprilTagPoseFilter>()
                ?? gameObject.GetComponent<AprilTagPoseFilter>()
                ?? gameObject.AddComponent<AprilTagPoseFilter>();
        }

        // Ensure we have debug image saver
        if (m_debugImageSaver == null)
        {
            m_debugImageSaver =
                FindFirstObjectByType<AprilTagDebugImageSaver>()
                ?? gameObject.GetComponent<AprilTagDebugImageSaver>()
                ?? gameObject.AddComponent<AprilTagDebugImageSaver>();
        }

        // Initialize distance adaptation system (Phase 1 & 3)
        // Will be fully configured once camera dimensions are known in Update()
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
            if (m_enableAllDebugLogging && Time.frameCount % m_verboseLogInterval == 0)
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

        // Initialize distance adaptation system if needed (once camera dimensions are known)
        if (m_distanceAdaptation == null)
        {
            // Get camera intrinsics from Meta Passthrough Camera API
            var eye =
                m_webcamPipeline != null
                    ? m_webcamPipeline.GetWebCamManagerEye()
                    : PassthroughCameraEye.Left;

            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
            float focalLengthX = intrinsics.FocalLength.x;

            // Calculate actual FOV from intrinsics for logging
            float calculatedFovDeg =
                2f * Mathf.Atan(wct.width / (2f * focalLengthX)) * Mathf.Rad2Deg;

            // Initialize with focal length from intrinsics
            m_distanceAdaptation = new AprilTagDistanceAdaptation(
                wct.width,
                wct.height,
                focalLengthX,
                m_enableAllDebugLogging
            );

            if (m_enableAllDebugLogging)
            {
                Debug.Log($"[AprilTag] Distance adaptation initialized for 0.3m-5m range");
                Debug.Log(
                    $"[AprilTag] Camera intrinsics: focal length = {focalLengthX:F1}px, calculated FOV = {calculatedFovDeg:F1}°"
                );
                Debug.Log(
                    $"[AprilTag] Using camera eye: {eye}, resolution: {intrinsics.Resolution.x}x{intrinsics.Resolution.y}"
                );

                // Log detectability info for reference tag size
                float maxDistance = m_distanceAdaptation.GetMaximumDetectableDistance(
                    m_tagSizeMeters
                );
                Debug.Log(
                    $"[AprilTag] Tag size {m_tagSizeMeters}m: max detectable distance = {maxDistance:F2}m"
                );
            }
        }

        // Determine decimation factor (adaptive or fixed)
        int targetDecimation = m_decimate;

        // Note: For initial detection, we use fixed decimation
        // Adaptive decimation will be applied per-tag in visualization phase
        // This allows us to detect tags at all distances first, then refine if needed

        // Ensure detector matches the feed dimensions
        if (
            m_detector == null
            || m_detW != wct.width
            || m_detH != wct.height
            || m_detDecim != targetDecimation
        )
        {
            if (m_enableAllDebugLogging)
                Debug.Log(
                    $"[AprilTag] Recreating detector: {wct.width}x{wct.height}, decimate={targetDecimation}, adaptive={m_enableAdaptiveDecimation}"
                );
            // Recreate detector using pipeline factory
            DisposeDetector();
            m_detector =
                m_webcamPipeline != null
                    ? m_webcamPipeline.CreateDetector(
                        wct.width,
                        wct.height,
                        m_tagFamily,
                        targetDecimation
                    )
                    : new TagDetector(
                        wct.width,
                        wct.height,
                        m_tagFamily,
                        Mathf.Max(1, targetDecimation)
                    );
            m_detW = wct.width;
            m_detH = wct.height;
            m_detDecim = Mathf.Max(1, targetDecimation);
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
                                if (m_enableAllDebugLogging && Time.frameCount % m_logInterval == 0)
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
                if (m_enableAllDebugLogging && Time.frameCount % m_verboseLogInterval == 0)
                    Debug.LogWarning("[AprilTag] No pixel data available");
                return;
            }
        }
        catch (Exception ex)
        {
            if (m_enableAllDebugLogging && Time.frameCount % m_verboseLogInterval == 0)
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
            float calculatedFov = GetCalculatedFOV();
            m_detector.ProcessImage(m_rgba.AsSpan(), calculatedFov, m_tagSizeMeters);
            m_nextDetectT = Time.time + 1f / Mathf.Max(1f, m_maxDetectionsPerSecond);
        }

        // Store whether we should save debug images this frame
        bool shouldSaveDebugImage =
            m_debugSavePreprocessedImage
            && (m_debugImageSaveInterval == 0 || Time.frameCount % m_debugImageSaveInterval == 0);

        // Debug logging for detection count
        if (Time.frameCount % m_logInterval == 0) // Log periodically regardless of enableAllDebugLogging
        {
            var tagCount = m_detector.DetectedTags?.Count() ?? 0;
            if (tagCount == 0)
            {
                float currentFov = GetCalculatedFOV();
                Debug.Log(
                    $"[AprilTag] No tags detected. Detector: {m_detW}x{m_detH}, decimation={m_detDecim}, tagSize={m_tagSizeMeters}m, FOV={currentFov:F1}°, GPU={m_enableGPUPreprocessing}"
                );

                // Additional debug info
                if (Time.frameCount % m_verboseLogInterval == 0)
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
                    var distance = tag.Position.magnitude;
                    var diagnostics =
                        m_distanceAdaptation != null
                            ? m_distanceAdaptation.GetDistanceDiagnostics(distance, m_tagSizeMeters)
                            : $"Distance: {distance:F2}m (no adaptation)";

                    Debug.Log($"[AprilTag] - Tag ID: {tag.ID}, {diagnostics}");
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
            // Skip ignored tag IDs (e.g., tags on curved surfaces)
            if (IsTagIgnored(t.ID))
            {
                if (m_enableAllDebugLogging && detectedCount == 0)
                {
                    Debug.Log($"[AprilTag] Tag {t.ID} is ignored (configured in ignore list)");
                }
                continue;
            }

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
                    var debugAdjustedPosition = t.Position * m_positionScaleFactor;
                    var debugWorldPos =
                        debugCam.position + debugCam.rotation * debugAdjustedPosition;
                    // Debug.Log($"[AprilTag] id={t.ID} camera_pos={t.Position:F3} world_pos={debugWorldPos:F3} camera_euler={t.Rotation.eulerAngles:F1} corner_center={cornerCenter:F3}");
                }
            }

            if (!m_vizById.TryGetValue(t.ID, out var tr) || tr == null)
            {
                if (!m_tagVizPrefab)
                {
                    if (m_enableAllDebugLogging && Time.frameCount % m_verboseLogInterval == 0)
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

            // Try multi-corner raycasting first (best for flat surfaces)
            var multiCornerPosition = m_transforms.TryGetWorldPositionFromMultipleCorners(
                t.ID,
                t,
                rawDetections
            );
            if (multiCornerPosition.HasValue)
            {
                // Use multi-corner positioning for maximum accuracy
                worldPosition = multiCornerPosition.Value;
                worldRotation = m_transforms.GetCornerBasedRotation(
                    t.ID,
                    rawDetections,
                    worldPosition
                );

                if (m_enableAllDebugLogging && detectedCount != m_previousTagCount)
                {
                    Debug.Log($"[AprilTag] Tag {t.ID}: Multi-corner position={worldPosition}");
                }
            }
            // Fallback: Try single corner-center positioning
            else
            {
                var cornerCenterResult = m_transforms.TryGetCornerBasedCenter(t.ID, rawDetections);
                if (cornerCenterResult.HasValue)
                {
                    // Use corner-based positioning which works better with Quest's coordinate system
                    worldPosition = m_transforms.GetWorldPositionFromCornerCenter(
                        cornerCenterResult.Value,
                        t
                    );
                    worldRotation = m_transforms.GetCornerBasedRotation(
                        t.ID,
                        rawDetections,
                        worldPosition
                    );

                    if (m_enableAllDebugLogging && detectedCount != m_previousTagCount)
                    {
                        Debug.Log($"[AprilTag] Tag {t.ID}: Single-corner position={worldPosition}");
                    }
                }
                else
                {
                    // Last resort: fallback to direct pose
                    if (m_enableAllDebugLogging && detectedCount != m_previousTagCount)
                    {
                        Debug.Log(
                            $"[AprilTag] Tag {t.ID}: No corner data, falling back to direct pose"
                        );
                    }
                    goto DirectPoseFallback;
                }
            }

            // Skip the direct pose fallback section
            goto AfterPositioning;

            DirectPoseFallback:
            // Direct pose fallback when corner-based positioning fails
            {
                if (m_enableAllDebugLogging && detectedCount != m_previousTagCount)
                {
                    Debug.Log(
                        $"[AprilTag] Tag {t.ID}: Corner-based positioning failed, falling back to direct pose"
                    );
                }

                // Fallback to direct pose approach
                var cam = GetCorrectCameraReference();

                // Apply scaling
                var adjustedPosition = t.Position * m_positionScaleFactor;

                // Apply distance scaling if enabled (Phase 3: Physics-based scaling)
                if (m_enableDistanceScaling)
                {
                    var distance = adjustedPosition.magnitude;

                    // Use distance adaptation system for physics-based correction
                    float scaledDistance;
                    if (m_distanceAdaptation != null)
                    {
                        scaledDistance = m_distanceAdaptation.ApplyDistanceScaling(
                            distance,
                            m_tagSizeMeters
                        );

                        if (m_enableAllDebugLogging && detectedCount != m_previousTagCount)
                        {
                            var diagnostics = m_distanceAdaptation.GetDistanceDiagnostics(
                                distance,
                                m_tagSizeMeters
                            );
                            Debug.Log($"[AprilTag] Tag {t.ID} distance adaptation: {diagnostics}");
                        }
                    }
                    else
                    {
                        // Fallback to static method if adaptation not initialized
                        scaledDistance = AprilTagTransforms.ApplyDistanceScaling(distance);
                    }

                    adjustedPosition = adjustedPosition.normalized * scaledDistance;
                }

                // Transform from camera space to world space
                worldPosition = cam.position + cam.rotation * adjustedPosition;
                worldRotation = m_transforms.GetCornerBasedRotation(
                    t.ID,
                    rawDetections,
                    worldPosition
                );

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

            AfterPositioning:

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
                cornerQuality = m_transforms.CalculateCornerQuality(
                    corners,
                    m_minCornerSideLength,
                    m_maxCornerSideLength,
                    m_maxAspectRatio,
                    m_maxCornerAngleDeviation,
                    m_smallTagQualityPenalty,
                    m_largeTagQualityPenalty,
                    m_elongatedTagQualityPenalty,
                    m_nonConvexQualityPenalty
                );

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
                && Time.frameCount % m_verboseLogInterval == 0
            )
            {
                Debug.Log(
                    "[AprilTag] Corner quality assessment skipped - GPU preprocessing provides image quality enhancement"
                );
            }

            // Phase 2: Calculate distance for adaptive filtering
            float tagDistance = t.Position.magnitude;

            // Multi-frame validation (PhotonVision approach + Phase 2: Distance-aware)
            if (
                m_poseFilter != null
                && !m_poseFilter.ValidateTagDetection(
                    t.ID,
                    worldPosition,
                    worldRotation,
                    cornerQuality,
                    tagDistance // Phase 2: Pass distance for adaptive thresholds
                )
            )
            {
                continue; // Skip this detection - failed validation
            }

            // Apply pose smoothing filter (PhotonVision approach + Phase 2: Distance-aware)
            var finalPosition = worldPosition;
            var finalRotation = worldRotation;

            if (m_poseFilter != null)
            {
                // Get or create filtered pose
                var filteredPose = m_poseFilter.GetFilteredPose(t.ID);
                var deltaTime = Time.time - filteredPose.LastUpdateTime;

                // Phase 2: Update distance tracking
                filteredPose.LastKnownDistance = tagDistance;

                // Increment frame counter
                filteredPose.FramesSinceFirstDetection++;

                // Only mark as initialized after sufficient stable frames
                const int MIN_FRAMES_FOR_INITIALIZATION = 10;
                if (filteredPose.FramesSinceFirstDetection >= MIN_FRAMES_FOR_INITIALIZATION)
                {
                    filteredPose.IsInitialized = true;
                }

                // Apply temporal filtering (Phase 2: Distance-aware smoothing)
                finalPosition = m_poseFilter.FilterTagPosition(
                    t.ID,
                    worldPosition,
                    filteredPose.FilteredPosition,
                    deltaTime,
                    filteredPose.IsInitialized,
                    tagDistance // Phase 2: Pass distance for adaptive smoothing
                );
                finalRotation = m_poseFilter.FilterTagRotation(
                    t.ID,
                    worldRotation,
                    filteredPose.FilteredRotation,
                    deltaTime,
                    filteredPose.IsInitialized,
                    tagDistance // Phase 2: Pass distance for adaptive smoothing
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
                        $"[Controller] Tag {t.ID} initializing: {filteredPose.FramesSinceFirstDetection}/{MIN_FRAMES_FOR_INITIALIZATION} frames"
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
        if (Time.frameCount % (m_verboseLogInterval * 3) == 0) // Every ~12 seconds at 72 FPS (900 frames)
        {
            CleanupOldVisualizations(m_seenTagsBuffer);
        }

        // PERFORMANCE: Force periodic garbage collection to prevent buildup
        // Quest has limited RAM and progressive degradation suggests memory pressure
        if (Time.frameCount % (m_verboseLogInterval * 12) == 0) // Every ~50 seconds at 72 FPS (3600 frames)
        {
            System.GC.Collect();
            if (m_enableAllDebugLogging)
            {
                float memory = System.GC.GetTotalMemory(false) / 1024f / 1024f;
                Debug.Log($"[Controller] Forced GC cleanup. Memory: {memory:F1}MB");
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

        if (m_enableAllDebugLogging && Time.frameCount % m_logInterval == 0)
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
            var filteredPose = m_poseFilter?.GetFilteredPose(tag.ID);
            if (filteredPose == null || !filteredPose.IsInitialized)
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
                Vector3.zero, // No position offset needed - positioning is now accurate
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

        // Clean up tracking for tags no longer detected
        if (m_poseFilter != null)
        {
            foreach (var tagId in m_poseFilter.GetTrackedTagIds().ToArray())
            {
                if (!m_currentTagIdsBuffer.Contains(tagId))
                {
                    m_spatialAnchorManager?.RemoveTagTracking(tagId);
                    m_poseFilter.RemoveTagTracking(tagId);
                }
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

        // Phase 3: Use distance adaptation for physics-based confidence
        if (m_distanceAdaptation != null)
        {
            var distance = tag.Position.magnitude;
            var distanceConfidence = m_distanceAdaptation.CalculateDetectionConfidence(
                distance,
                m_tagSizeMeters
            );
            confidence *= distanceConfidence;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[AprilTag]   Distance confidence ({distance:F2}m): {distanceConfidence:F3}, total: {confidence:F3}"
                );
            }
        }
        else if (m_enableCornerQualityAssessment)
        {
            // Fallback: Use simplified distance-based quality
            var cornerQuality = Mathf.Clamp01(
                1.0f - tag.Position.magnitude * m_distanceQualityDecayFactor
            );
            confidence *= cornerQuality;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[AprilTag]   Corner quality: {cornerQuality:F3}, confidence after: {confidence:F3}"
                );
            }
        }

        // Apply surface alignment quality (perpendicularity check for flat surfaces)
        if (m_transforms != null && m_poseFilter != null)
        {
            var filteredPose = m_poseFilter.GetFilteredPose(tag.ID);
            if (filteredPose != null && filteredPose.IsInitialized)
            {
                // Try to get raw detections for surface alignment check
                var rawDetections =
                    m_webcamPipeline != null
                        ? m_webcamPipeline.GetRawDetections(m_detector)
                        : new System.Collections.Generic.List<object>();

                var surfaceAlignmentQuality = m_transforms.CalculateSurfaceAlignmentQuality(
                    tag.ID,
                    rawDetections,
                    filteredPose.FilteredPosition,
                    filteredPose.FilteredRotation
                );

                // Weight surface alignment heavily for FIRST Robotics flat surfaces
                confidence *= Mathf.Lerp(0.5f, 1.0f, surfaceAlignmentQuality);

                if (m_enableAllDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTag]   Surface alignment quality: {surfaceAlignmentQuality:F3}, confidence after: {confidence:F3}"
                    );
                }
            }
        }

        // Apply multi-frame validation confidence
        if (m_poseFilter != null)
        {
            var validationConfidence = m_poseFilter.CalculateValidationConfidence(tag.ID);
            confidence *= validationConfidence;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[Controller]   Validation confidence: {validationConfidence:F3}, confidence after: {confidence:F3}"
                );
            }
        }

        // Apply pose smoothing confidence
        var filteredPose2 = m_poseFilter?.GetFilteredPose(tag.ID);
        if (filteredPose2 != null && filteredPose2.IsInitialized)
        {
            // Higher confidence for more stable poses
            var stabilityConfidence = Mathf.Clamp01(
                1.0f - (Time.time - filteredPose2.LastUpdateTime) * m_stabilityDecayFactor
            );
            confidence *= stabilityConfidence;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[Controller]   Stability confidence: {stabilityConfidence:F3}, confidence after: {confidence:F3}"
                );
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

    public void ToggleGravityConstraintsRuntime()
    {
        m_enableGravityAlignedConstraints = !m_enableGravityAlignedConstraints;
        Debug.Log(
            $"[AprilTag] Gravity-aligned constraints {(m_enableGravityAlignedConstraints ? "enabled" : "disabled")} via runtime call"
        );
        Debug.Log($"[AprilTag]   - Position constraint: {m_applyGravityConstraintToPosition}");
        Debug.Log($"[AprilTag]   - Rotation constraint: {m_applyGravityConstraintToRotation}");
        Debug.Log($"[AprilTag]   - Tolerance: {m_gravityAlignmentTolerance}°");
    }

    public void SetGravityConstraintsEnabled(bool enabled)
    {
        m_enableGravityAlignedConstraints = enabled;
        Debug.Log(
            $"[AprilTag] Gravity-aligned constraints {(enabled ? "enabled" : "disabled")} via runtime call"
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
        Debug.Log($"  - Min Detection Distance: {m_minDetectionDistance}m");
        Debug.Log($"  - Max Detection Distance: {m_maxDetectionDistance}m");
        Debug.Log($"  - Adaptive Decimation: {m_enableAdaptiveDecimation}");
        Debug.Log($"  - Tag Size: {m_tagSizeMeters}m");
        Debug.Log($"  - Camera: {cam.name} at {cam.position:F3}");

        // Log gravity constraint settings
        Debug.Log($"[AprilTag] Gravity-Aligned Constraints:");
        Debug.Log($"  - Enabled: {m_enableGravityAlignedConstraints}");
        if (m_enableGravityAlignedConstraints)
        {
            Debug.Log($"  - Gravity Direction: {m_gravityDirection.normalized}");
            Debug.Log($"  - Alignment Tolerance: {m_gravityAlignmentTolerance}°");
            Debug.Log($"  - Apply to Position: {m_applyGravityConstraintToPosition}");
            Debug.Log($"  - Apply to Rotation: {m_applyGravityConstraintToRotation}");
        }

        // Log distance adaptation diagnostics if available
        if (m_distanceAdaptation != null)
        {
            Debug.Log($"[AprilTag] Distance Adaptation Diagnostics:");
            float maxDist = m_distanceAdaptation.GetMaximumDetectableDistance(m_tagSizeMeters);
            Debug.Log($"  - Max detectable distance for {m_tagSizeMeters}m tag: {maxDist:F2}m");

            // Test distances
            float[] testDistances = { 0.5f, 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };
            foreach (var dist in testDistances)
            {
                if (dist <= maxDist)
                {
                    string diag = m_distanceAdaptation.GetDistanceDiagnostics(
                        dist,
                        m_tagSizeMeters
                    );
                    Debug.Log($"  - {diag}");
                }
            }
        }
    }

    private void HandleQuestDebugInput()
    {
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
        if (m_enableAllDebugLogging && Time.frameCount % m_verboseLogInterval == 0)
        {
            LogCurrentSettings();
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
                // Get camera intrinsics for proper coordinate transformation
                var eye = GetWebCamManagerEye();
                var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);

                // Get the actual detected corners (same as visualization pipeline uses)
                var corners = m_transforms.TryGetCornersWithIntrinsics(
                    tag.ID,
                    rawDetections,
                    intrinsics
                );

                if (corners == null || corners.Length != 4)
                {
                    if (m_enableAllDebugLogging)
                    {
                        Debug.LogWarning(
                            $"[AprilTag] Could not extract corners for tag {tag.ID} overlay"
                        );
                    }
                    continue;
                }

                // Scale corners to texture dimensions
                var scaleX = (float)tex.width / m_detW;
                var scaleY = (float)tex.height / m_detH;

                var scaledCorners = new Vector2[4];
                for (int i = 0; i < 4; i++)
                {
                    scaledCorners[i] = new Vector2(corners[i].x * scaleX, corners[i].y * scaleY);
                }

                // Calculate center from corners for tag info placement
                var scaledCenter = Vector2.zero;
                foreach (var corner in scaledCorners)
                {
                    scaledCenter += corner;
                }
                scaledCenter /= 4;

                if (m_enableAllDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTag] Tag {tag.ID} actual corners: "
                            + $"C1=({scaledCorners[0].x:F1},{scaledCorners[0].y:F1}), "
                            + $"C2=({scaledCorners[1].x:F1},{scaledCorners[1].y:F1}), "
                            + $"C3=({scaledCorners[2].x:F1},{scaledCorners[2].y:F1}), "
                            + $"C4=({scaledCorners[3].x:F1},{scaledCorners[3].y:F1})"
                    );
                }

                // Draw tag outline using actual detected corners
                DrawTagOutline(tex, scaledCorners, tag.ID);
                overlayCount++;

                // Draw tag ID at center
                DrawTagInfo(tex, scaledCenter, tag);
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
                float calculatedFov = GetCalculatedFOV();
                m_detector.ProcessImage(pixels.AsSpan(), calculatedFov, m_tagSizeMeters);
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

    /// <summary>
    /// Fix EventSystem input modules for Input System package compatibility
    /// Replaces StandaloneInputModule with InputSystemUIInputModule when new Input System is active
    /// </summary>
    private void FixEventSystemInputModules()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var eventSystems = FindObjectsByType<UnityEngine.EventSystems.EventSystem>(
            FindObjectsSortMode.None
        );

        foreach (var eventSystem in eventSystems)
        {
            var legacyInputModule =
                eventSystem.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (legacyInputModule != null)
            {
                if (m_enableAllDebugLogging)
                {
                    Debug.Log(
                        $"[Controller] Replacing StandaloneInputModule with InputSystemUIInputModule on {eventSystem.name}"
                    );
                }
                DestroyImmediate(legacyInputModule);
                eventSystem.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
        }
#endif
    }

    /// <summary>
    /// Ensure AprilTagPermissionsManager exists in scene (auto-create if needed)
    /// </summary>
    private void StartPermissionsManager()
    {
        var permissionsManager = FindFirstObjectByType<AprilTagPermissionsManager>();

        if (permissionsManager == null)
        {
            // Create permissions manager automatically
            var permManagerObj = new GameObject("AprilTagPermissionsManager");
            permissionsManager = permManagerObj.AddComponent<AprilTagPermissionsManager>();

            if (m_enableAllDebugLogging)
            {
                Debug.Log("[Controller] Auto-created AprilTagPermissionsManager");
            }
        }
    }

    /// <summary>
    /// Check if a tag ID should be ignored
    /// </summary>
    private bool IsTagIgnored(int tagId)
    {
        if (m_ignoredTagIds == null || m_ignoredTagIds.Length == 0)
            return false;

        foreach (var ignoredId in m_ignoredTagIds)
        {
            if (tagId == ignoredId)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Add a tag ID to the ignore list at runtime
    /// </summary>
    public void AddIgnoredTag(int tagId)
    {
        if (m_ignoredTagIds == null)
        {
            m_ignoredTagIds = new int[] { tagId };
        }
        else
        {
            // Check if already ignored
            foreach (var id in m_ignoredTagIds)
            {
                if (id == tagId)
                    return;
            }

            // Add to array
            var newArray = new int[m_ignoredTagIds.Length + 1];
            m_ignoredTagIds.CopyTo(newArray, 0);
            newArray[m_ignoredTagIds.Length] = tagId;
            m_ignoredTagIds = newArray;
        }

        Debug.Log($"[AprilTag] Added tag {tagId} to ignore list");
    }

    /// <summary>
    /// Remove a tag ID from the ignore list at runtime
    /// </summary>
    public void RemoveIgnoredTag(int tagId)
    {
        if (m_ignoredTagIds == null || m_ignoredTagIds.Length == 0)
            return;

        var newList = new System.Collections.Generic.List<int>();
        bool found = false;

        foreach (var id in m_ignoredTagIds)
        {
            if (id == tagId)
            {
                found = true;
                continue;
            }
            newList.Add(id);
        }

        if (found)
        {
            m_ignoredTagIds = newList.ToArray();
            Debug.Log($"[AprilTag] Removed tag {tagId} from ignore list");
        }
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
