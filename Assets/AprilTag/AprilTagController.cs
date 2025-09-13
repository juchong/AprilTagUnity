// Assets/AprilTag/AprilTagController.cs
// Quest-only AprilTag tracker using Meta Passthrough + locally integrated AprilTag library.
// Uses reflection to read WebCamTexture so there's no compile-time dependency on WebCamTextureManager.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using AprilTag; // locally integrated AprilTag library
using PassthroughCameraSamples;
using Unity.XR.CoreUtils;
using Meta.XR.Samples;
using Meta.XR;

public class AprilTagController : MonoBehaviour
{
    [Header("Passthrough Feed")]
    [Tooltip("Assign the WebCamTextureManager component from Meta's Passthrough Camera API samples.")]
    [SerializeField] private UnityEngine.Object webCamManager;  // reflection target
    [Tooltip("Optional: override the feed with your own WebCamTexture.")]
    [SerializeField] private WebCamTexture webCamTextureOverride;

    [Header("Visualization")]
    [SerializeField] private GameObject tagVizPrefab;
    [SerializeField] private bool scaleVizToTagSize = true;
    [Tooltip("Optional: Override the camera used for coordinate transformation. If null, will auto-detect.")]
    [SerializeField] private Camera referenceCamera;
    [Tooltip("Offset to apply to tag positions (useful for calibration)")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [Tooltip("Rotation offset to apply to tag rotations (useful for calibration)")]
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;
    [Tooltip("Quest-specific: Use the center eye transform for better positioning")]
    [SerializeField] private bool useCenterEyeTransform = true;
    [Tooltip("Quest-specific: Use proper passthrough camera raycasting for accurate positioning")]
    [SerializeField] private bool usePassthroughRaycasting = true;
    [Tooltip("Environment raycast manager for accurate 3D positioning")]
    [SerializeField] private EnvironmentRaycastManager environmentRaycastManager;
    [Tooltip("Ignore occlusion - visualizations will always be visible")]
    [SerializeField] private bool ignoreOcclusion = true;
    [Tooltip("Scale factor to adjust tag positioning (1.0 = normal, 0.5 = half size, 2.0 = double size)")]
    [SerializeField] private float positionScaleFactor = 1.0f;
    [Tooltip("Minimum detection distance in meters (for very close tags)")]
    [SerializeField] private float minDetectionDistance = 0.3f;
    [Tooltip("Maximum detection distance in meters (for very far tags)")]
    [SerializeField] private float maxDetectionDistance = 20.0f;
    [Tooltip("Enable distance-based scaling adjustments")]
    [SerializeField] private bool enableDistanceScaling = true;
    [Tooltip("Enable Quest debugging with controller input")]
    [SerializeField] private bool enableQuestDebugging = true;
    [Tooltip("Use improved camera intrinsics for better tag alignment")]
    [SerializeField] private bool useImprovedIntrinsics = false;
    [Tooltip("Make tags world-locked (rotation independent of headset movement) - inspired by PhotonVision's stable pose estimation")]
    [SerializeField] private bool worldLockedRotation = true;
    [Tooltip("Scale multiplier for tag visualization (1.0 = normal size)")]
    [SerializeField] private float visualizationScaleMultiplier = 1.0f;
    [Tooltip("Test mode: Use identity rotation to see if positioning is correct")]
    [SerializeField] private bool testModeIdentityRotation = false;
    [Tooltip("Force use of corner-based positioning (disable fallback)")]
    [SerializeField] private bool forceCornerBasedPositioning = false;

    [Header("Detection")]
    [Tooltip("Tag family to detect. Tag36h11 is recommended for ArUcO compatibility.")]
    [SerializeField] private AprilTag.Interop.TagFamily tagFamily = AprilTag.Interop.TagFamily.Tag36h11;
    [Tooltip("Physical tag edge length (meters).")]
    [SerializeField] private float tagSizeMeters = 0.08f;
    [Tooltip("Downscale factor for detection (1 = full res, 2 = half, etc.).")]
    [Range(1, 8)][SerializeField] private int decimate = 2;
    [Tooltip("Max detection updates per second.")]
    [SerializeField] private float maxDetectionsPerSecond = 15f;
    [Tooltip("Horizontal FOV (degrees) of the passthrough camera.")]
    [SerializeField] private float horizontalFovDeg = 78f;

    [Header("Diagnostics")]
    [Tooltip("Log individual tag detection details (position, euler angles, quaternions)")]
    [SerializeField] private bool logDetections = true;
    [Tooltip("Log system debug information (reduced frequency for performance)")]
    [SerializeField] private bool logDebugInfo = true;
    [Tooltip("Enable all debug logging (can be toggled at runtime)")]
    [SerializeField] private bool enableAllDebugLogging = true;

    // CPU buffers
    private Color32[] _rgba;
    
    // Headset pose tracking for continuous adjustment
    private Quaternion _lastHeadsetRotation = Quaternion.identity;
    private Vector3 _lastHeadsetPosition = Vector3.zero;
    private bool _headsetPoseInitialized = false;

    // Detector (recreated when size/decimate changes)
    private TagDetector _detector;
    private int _detW, _detH, _detDecim;

    private float _nextDetectT;
    private readonly Dictionary<int, Transform> _vizById = new();
    private int _previousTagCount = 0;

    void OnDisable() => DisposeDetector();

    void Awake()
    {
        // Fix Input System issues on startup
        InputSystemFixer.FixAllEventSystems();
        
        // Subscribe to permission events
        AprilTagPermissionsManager.OnAllPermissionsGranted += OnAllPermissionsGranted;
        AprilTagPermissionsManager.OnPermissionsDenied += OnPermissionsDenied;
        
        
        // Auto-find EnvironmentRaycastManager if not assigned
        if (environmentRaycastManager == null && usePassthroughRaycasting)
        {
            environmentRaycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
            if (environmentRaycastManager == null && logDebugInfo)
            {
                Debug.LogWarning("[AprilTag] No EnvironmentRaycastManager found. Passthrough raycasting will not work properly. Please assign one or disable usePassthroughRaycasting.");
            }
        }
    }
    
    void OnDestroy()
    {
        // Dispose detector resources
        DisposeDetector();
        
        // Unsubscribe from permission events
        AprilTagPermissionsManager.OnAllPermissionsGranted -= OnAllPermissionsGranted;
        AprilTagPermissionsManager.OnPermissionsDenied -= OnPermissionsDenied;
    }
    
    private void OnAllPermissionsGranted()
    {
        if (logDebugInfo) Debug.Log("[AprilTag] All required permissions granted - ready to start detection");
        // Permissions are now available, detection will start automatically in Update()
    }
    
    private void OnPermissionsDenied()
    {
        if (logDebugInfo) Debug.LogWarning("[AprilTag] Required permissions denied - detection will not work properly");
        // Could show UI message to user here
    }

    void Update()
    {
        // Quest debugging input handling
        if (enableQuestDebugging)
        {
            HandleQuestDebugInput();
        }

        // Check permissions before proceeding with detection
        if (!AprilTagPermissionsManager.HasAllPermissions)
        {
            // Only log this warning occasionally to avoid spam
            if (logDebugInfo && Time.frameCount % 300 == 0) 
            {
                Debug.LogWarning("[AprilTag] Waiting for required permissions to be granted");
            }
            return;
        }
        
        var wct = GetActiveWebCamTexture();
        if (wct == null)
        {
            if (logDebugInfo) Debug.LogWarning("[AprilTag] No WebCamTexture available");
            return;
        }
        
        if (!wct.isPlaying)
        {
            if (logDebugInfo) Debug.LogWarning("[AprilTag] WebCamTexture is not playing");
            return;
        }
        
        if (wct.width <= 16 || wct.height <= 16)
        {
            if (logDebugInfo) Debug.LogWarning($"[AprilTag] WebCamTexture dimensions too small: {wct.width}x{wct.height}");
            return;
        }

        // Additional check: ensure WebCamTexture has been initialized for at least a few frames
        if (Time.frameCount < 10)
        {
            return;
        }

        if (Time.time < _nextDetectT) return;
        _nextDetectT = Time.time + 1f / Mathf.Max(1f, maxDetectionsPerSecond);
        
        // Removed verbose frame processing log - only show tag detection results

        // Ensure detector matches the feed dimensions
        if (_detector == null || _detW != wct.width || _detH != wct.height || _detDecim != decimate)
        {
            if (logDebugInfo) Debug.Log($"[AprilTag] Recreating detector: {wct.width}x{wct.height}, decimate={decimate}");
            RecreateDetectorIfNeeded(wct.width, wct.height, decimate);
        }

        // Get pixels directly from WebCamTexture (avoids GPU initialization issues with Graphics.CopyTexture)
        try
        {
            _rgba = wct.GetPixels32();
            if (_rgba == null || _rgba.Length == 0)
            {
                if (logDebugInfo && Time.frameCount % 300 == 0) Debug.LogWarning("[AprilTag] WebCamTexture returned no pixel data");
                return;
            }
        }
        catch (System.Exception ex)
        {
            if (logDebugInfo && Time.frameCount % 300 == 0) Debug.LogWarning($"[AprilTag] Failed to get pixels from WebCamTexture: {ex.Message}");
            return;
        }

        // NOTE: Correct usage � DO NOT pass _rgba to the constructor.
        // Constructor takes (width, height, decimation).
        // Detection call takes (pixels, fovDeg, tagSizeMeters).
        _detector.ProcessImage(_rgba.AsSpan(), horizontalFovDeg, tagSizeMeters);

        // Visualize detected tags using corner-based positioning
        var seen = new HashSet<int>();
        var detectedCount = 0;
        
        // Try to get raw detection data for corner-based positioning
        var rawDetections = GetRawDetections();
        
        foreach (var t in _detector.DetectedTags)
        {
            detectedCount++;
            seen.Add(t.ID);
            
            // Try to find corresponding raw detection data for corner coordinates
            Vector2? cornerCenter = null;
            if (useImprovedIntrinsics && usePassthroughRaycasting)
            {
                // Use improved intrinsics-based corner detection
                var eye = GetWebCamManagerEye();
                var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
                cornerCenter = TryGetCornerBasedCenterWithIntrinsics(t.ID, rawDetections, intrinsics);
            }
            else
            {
                // Use standard corner detection
                cornerCenter = TryGetCornerBasedCenter(t.ID, rawDetections);
            }
            
            if (logDebugInfo && cornerCenter.HasValue)
            {
                Debug.Log($"[AprilTag] Tag {t.ID}: Corner center found at {cornerCenter.Value}");
            }
            else if (logDebugInfo)
            {
                Debug.LogWarning($"[AprilTag] Tag {t.ID}: No corner center found, using fallback positioning");
            }
            
            if (enableAllDebugLogging && logDetections) 
            {
                if (usePassthroughRaycasting)
                {
                    var debugWorldPos = GetWorldPositionUsingPassthroughRaycasting(t);
                    // Debug.Log($"[AprilTag] id={t.ID} camera_pos={t.Position:F3} passthrough_world_pos={debugWorldPos:F3} camera_euler={t.Rotation.eulerAngles:F1} use_raycasting={usePassthroughRaycasting} corner_center={cornerCenter:F3}");
                }
                else
                {
                    var debugCam = GetCorrectCameraReference();
                    var debugAdjustedPosition = (t.Position + positionOffset) * positionScaleFactor;
                    var debugWorldPos = debugCam.position + debugCam.rotation * debugAdjustedPosition;
                    // Debug.Log($"[AprilTag] id={t.ID} camera_pos={t.Position:F3} world_pos={debugWorldPos:F3} camera_euler={t.Rotation.eulerAngles:F1} corner_center={cornerCenter:F3}");
                }
            }

            if (!_vizById.TryGetValue(t.ID, out var tr) || tr == null)
            {
                if (!tagVizPrefab) continue;
                tr = Instantiate(tagVizPrefab).transform;
                tr.name = $"AprilTag_{t.ID}";
                
                // Configure visualization to ignore occlusion
                ConfigureVisualizationForNoOcclusion(tr);
                
                _vizById[t.ID] = tr;
            }

            // Quest-specific positioning using corner-based approach for better accuracy
            Vector3 worldPosition;
            Quaternion worldRotation;
            
            // Try corner-based positioning first (more accurate for Quest)
            var cornerCenterResult = TryGetCornerBasedCenter(t.ID, rawDetections);
            if (cornerCenterResult.HasValue)
            {
                // Use corner-based positioning which works better with Quest's coordinate system
                worldPosition = GetWorldPositionFromCornerCenter(cornerCenterResult.Value, t);
                worldRotation = GetCornerBasedRotation(t.ID, rawDetections, worldPosition) * Quaternion.Euler(rotationOffset);
                
                if (enableAllDebugLogging && logDebugInfo && detectedCount != _previousTagCount)
                {
                    Debug.Log($"[AprilTag] Tag {t.ID}: Using corner-based positioning at {worldPosition}, corner center: {cornerCenterResult.Value}");
                    Debug.Log($"[AprilTag] Tag {t.ID}: Corner-based - Raw AprilTag pose: {t.Position}, {t.Rotation.eulerAngles}");
                }
            }
            else
            {
                if (enableAllDebugLogging && logDebugInfo && detectedCount != _previousTagCount)
                {
                    Debug.Log($"[AprilTag] Tag {t.ID}: Corner-based positioning failed, falling back to direct pose");
                }
                
                // Fallback to direct pose approach
                var cam = GetCorrectCameraReference();
                
                // Apply position offset and scaling
                var adjustedPosition = (t.Position + positionOffset) * positionScaleFactor;
                
                // Apply distance scaling if enabled
                if (enableDistanceScaling)
                {
                    var distance = adjustedPosition.magnitude;
                    var scaledDistance = ApplyDistanceScaling(distance);
                    adjustedPosition = adjustedPosition.normalized * scaledDistance;
                }
                
                // Transform from camera space to world space
                worldPosition = cam.position + cam.rotation * adjustedPosition;
                worldRotation = GetCornerBasedRotation(t.ID, rawDetections, worldPosition) * Quaternion.Euler(rotationOffset);
                
                if (enableAllDebugLogging && logDebugInfo && detectedCount != _previousTagCount)
                {
                    var camRef = GetCorrectCameraReference();
                    var offsetTagPosition = camRef.position + camRef.rotation * t.Position;
                    var offsetTagRotation = camRef.rotation * t.Rotation;
                    
                    Debug.Log($"[AprilTag] Tag {t.ID}: Using direct pose positioning at {worldPosition}, AprilTag pos: {t.Position}, adjusted pos: {adjustedPosition}");
                    Debug.Log($"[AprilTag] Tag {t.ID}: Direct pose - Raw: {t.Position}, {t.Rotation.eulerAngles}");
                    Debug.Log($"[AprilTag] Tag {t.ID}: Direct pose - Offset: {offsetTagPosition}, {offsetTagRotation.eulerAngles}");
                }
            }
            
            if (enableAllDebugLogging && logDebugInfo && detectedCount != _previousTagCount)
            {
                var camDebug = GetCorrectCameraReference();
                var rawTagPosition = t.Position;
                var rawTagRotation = t.Rotation;
                var offsetTagPosition = camDebug.position + camDebug.rotation * rawTagPosition;
                var offsetTagRotation = camDebug.rotation * rawTagRotation;
                
                Debug.Log($"[AprilTag] Tag {t.ID}: Final world position={worldPosition}, rotation={worldRotation.eulerAngles}");
                Debug.Log($"[AprilTag] Tag {t.ID}: Raw AprilTag pose - Position: {rawTagPosition}, Rotation: {rawTagRotation.eulerAngles}");
                Debug.Log($"[AprilTag] Tag {t.ID}: Offset by headset - Position: {offsetTagPosition}, Rotation: {offsetTagRotation.eulerAngles}");
                Debug.Log($"[AprilTag] Tag {t.ID}: Camera - Position: {camDebug.position}, Rotation: {camDebug.rotation.eulerAngles}");
            }
            
            tr.SetPositionAndRotation(worldPosition, worldRotation);
            if (scaleVizToTagSize) tr.localScale = Vector3.one * tagSizeMeters * visualizationScaleMultiplier;
            tr.gameObject.SetActive(true);
        }

        // Log detection results only when tag count changes
        if (detectedCount != _previousTagCount)
        {
            if (detectedCount > 0)
            {
                Debug.Log($"[AprilTag] Detected {detectedCount} tags");
            }
            else if (_previousTagCount > 0)
            {
                Debug.Log($"[AprilTag] All tags lost");
            }
        }
        
        // Update previous tag count for next frame
        _previousTagCount = detectedCount;

        // Hide those not seen this frame
        foreach (var kv in _vizById)
            if (!seen.Contains(kv.Key) && kv.Value) kv.Value.gameObject.SetActive(false);
    }

    private void RecreateDetectorIfNeeded(int width, int height, int dec)
    {
        DisposeDetector();
        _detector = new TagDetector(width, height, tagFamily, Mathf.Max(1, dec)); // <� width, height, decimation
        _detW = width; _detH = height; _detDecim = Mathf.Max(1, dec);
        
        if (logDebugInfo) Debug.Log($"[AprilTag] Created detector: {width}x{height}, family={tagFamily}, decimate={Mathf.Max(1, dec)}");
    }

    private void DisposeDetector()
    {
        _detector?.Dispose();
        _detector = null;
    }

    private WebCamTexture GetActiveWebCamTexture()
    {
        if (webCamTextureOverride) 
        {
            return webCamTextureOverride;
        }
        
        // First try to get WebCamTexture from assigned webCamManager
        if (webCamManager) 
        {
            // Try to read WebCamTextureManager.WebCamTexture (Meta sample) via reflection
            var t = webCamManager.GetType();
            var prop = t.GetProperty("WebCamTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && typeof(WebCamTexture).IsAssignableFrom(prop.PropertyType))
            {
                var wct = prop.GetValue(webCamManager) as WebCamTexture;
                if (wct != null) return wct;
            }

            // Fallbacks (if your provider exposes Texture/SourceTexture)
            var texProp = t.GetProperty("Texture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? t.GetProperty("SourceTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var fallbackWct = texProp?.GetValue(webCamManager) as WebCamTexture;
            if (fallbackWct != null) return fallbackWct;
        }

        // If no assigned manager or it didn't work, try to find WebCamTextureManager in the scene
        var webCamTextureManager = FindFirstObjectByType<PassthroughCameraSamples.WebCamTextureManager>();
        if (webCamTextureManager != null)
        {
            var wct = webCamTextureManager.WebCamTexture;
            return wct;
        }
        return null;
    }

    private Transform GetCorrectCameraReference()
    {
        // If a specific reference camera is assigned, use it
        if (referenceCamera != null)
        {
            return referenceCamera.transform;
        }

        // Quest-specific: Try to use the center eye transform for better positioning
        if (useCenterEyeTransform)
        {
            // Look for OVRCameraRig or similar VR camera rig
            var cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig != null)
            {
                // Use the center eye anchor for better positioning
                var centerEyeAnchor = cameraRig.centerEyeAnchor;
                if (centerEyeAnchor != null)
                {
                    if (logDebugInfo) Debug.Log($"[AprilTag] Using OVRCameraRig center eye anchor for Quest positioning");
                    return centerEyeAnchor;
                }
            }

            // Alternative: Look for XR Origin or similar
            var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                if (logDebugInfo) Debug.Log($"[AprilTag] Using XR Origin camera for Quest positioning");
                return xrOrigin.Camera.transform;
            }
        }

        // Try to find the correct camera for VR/AR applications
        // First, try to find cameras with specific tags or names that might indicate passthrough/AR cameras
        var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        
        // Look for cameras that might be the passthrough camera
        foreach (var cam in cameras)
        {
            // Check if this camera has a name that suggests it's the passthrough camera
            if (cam.name.ToLower().Contains("passthrough") || 
                cam.name.ToLower().Contains("ar") || 
                cam.name.ToLower().Contains("xr") ||
                cam.name.ToLower().Contains("center") ||
                cam.name.ToLower().Contains("main"))
            {
                if (logDebugInfo) Debug.Log($"[AprilTag] Using camera '{cam.name}' as reference for tag positioning");
                return cam.transform;
            }
        }

        // If no specific camera found, try to get the camera from the WebCam manager
        if (webCamManager != null)
        {
            var managerType = webCamManager.GetType();
            var cameraField = managerType.GetField("Camera", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (cameraField != null)
            {
                var cam = cameraField.GetValue(webCamManager) as Camera;
                if (cam != null)
                {
                    if (logDebugInfo) Debug.Log($"[AprilTag] Using WebCam manager camera '{cam.name}' as reference for tag positioning");
                    return cam.transform;
                }
            }
        }

        // Fallback to Camera.main or this transform
        var fallbackCam = Camera.main ? Camera.main.transform : transform;
        if (logDebugInfo) Debug.Log($"[AprilTag] Using fallback camera '{fallbackCam.name}' as reference for tag positioning");
        return fallbackCam;
    }

    [ContextMenu("Reset Position Offsets")]
    public void ResetPositionOffsets()
    {
        positionOffset = Vector3.zero;
        rotationOffset = Vector3.zero;
        Debug.Log("[AprilTag] Position and rotation offsets reset to zero");
    }

    [ContextMenu("Log Current Camera Info")]
    public void LogCurrentCameraInfo()
    {
        var cam = GetCorrectCameraReference();
        Debug.Log($"[AprilTag] Current reference camera: {cam.name}");
        Debug.Log($"[AprilTag] Camera position: {cam.position}");
        Debug.Log($"[AprilTag] Camera rotation: {cam.rotation.eulerAngles}");
        Debug.Log($"[AprilTag] Position offset: {positionOffset}");
        Debug.Log($"[AprilTag] Rotation offset: {rotationOffset}");
        Debug.Log($"[AprilTag] Use center eye transform: {useCenterEyeTransform}");
        
        // Log Quest-specific information
        if (webCamManager != null)
        {
            var managerType = webCamManager.GetType();
            var eyeField = managerType.GetField("Eye", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (eyeField != null)
            {
                var eye = eyeField.GetValue(webCamManager);
                Debug.Log($"[AprilTag] WebCam manager eye: {eye}");
            }
        }
    }

    private PassthroughCameraEye GetWebCamManagerEye()
    {
        if (webCamManager != null)
        {
            var managerType = webCamManager.GetType();
            var eyeField = managerType.GetField("Eye", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (eyeField != null)
            {
                return (PassthroughCameraEye)eyeField.GetValue(webCamManager);
            }
        }
        return PassthroughCameraEye.Left; // Default to left eye
    }


    private Vector3? GetWorldPositionUsingPassthroughRaycasting(TagPose tagPose)
    {
        try
        {
            // Get the camera eye from the WebCam manager
            var eye = GetWebCamManagerEye();
            
            // Get camera intrinsics for proper coordinate conversion
            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
            var camRes = intrinsics.Resolution;
            
            // Try to use corner coordinates if available (more accurate)
            Vector2Int screenPoint;
            if (TryGetTagCenterFromCorners(tagPose, intrinsics, out screenPoint))
            {
                // Use corner-based center point
                Debug.Log($"[AprilTag] Using corner-based center point: {screenPoint}");
            }
            else
            {
                // Fallback: Convert the 3D tag position to 2D screen coordinates
                // The tag position is in camera space, so we need to project it to screen space
                var scaledPosition = tagPose.Position * positionScaleFactor;
                screenPoint = Project3DToScreen(scaledPosition, intrinsics);
            }
            
            // Convert 2D screen coordinates to 3D ray using passthrough camera utils
            var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(eye, screenPoint);
            
            // Use environment raycasting to find the actual 3D world position
            if (environmentRaycastManager != null && environmentRaycastManager.Raycast(ray, out var hitInfo))
            {
                return hitInfo.point;
            }
            else
            {
                // Fallback: project the ray forward to a reasonable distance
                // Use the actual tag distance with proper bounds checking
                var rawDistance = tagPose.Position.magnitude;
                var clampedDistance = Mathf.Clamp(rawDistance, minDetectionDistance, maxDetectionDistance);
                
                // Apply distance-based scaling if enabled
                if (enableDistanceScaling)
                {
                    clampedDistance = ApplyDistanceScaling(clampedDistance);
                }
                
                return ray.origin + ray.direction * clampedDistance;
            }
        }
        catch (System.Exception ex)
        {
            if (logDebugInfo) Debug.LogWarning($"[AprilTag] Passthrough raycasting failed: {ex.Message}");
            return null;
        }
    }

    private bool TryGetTagCenterFromCorners(TagPose tagPose, PassthroughCameraIntrinsics intrinsics, out Vector2Int centerPoint)
    {
        centerPoint = Vector2Int.zero;
        
        try
        {
            // Try to access corner properties on the TagPose object
            var tagPoseType = tagPose.GetType();
            
            // Try different possible corner property names
            var cornerPropertyNames = new[] { "Corners", "CornerPoints", "Points", "Vertices", "CornerCoordinates" };
            
            foreach (var propName in cornerPropertyNames)
            {
                var cornersProperty = tagPoseType.GetProperty(propName);
                if (cornersProperty != null)
                {
                    var corners = cornersProperty.GetValue(tagPose);
                    if (corners != null)
                    {
                        // Try to convert to Vector2 array or similar
                        if (corners is Vector2[] vector2Corners && vector2Corners.Length >= 4)
                        {
                            // Calculate center point from corners
                            var center = Vector2.zero;
                            foreach (var corner in vector2Corners)
                            {
                                center += corner;
                            }
                            center /= vector2Corners.Length;
                            
                            // Convert to screen coordinates
                            centerPoint = new Vector2Int(
                                Mathf.RoundToInt(center.x),
                                Mathf.RoundToInt(center.y)
                            );
                            
                            Debug.Log($"[AprilTag] Found {propName} with {vector2Corners.Length} corners, center: {centerPoint}");
                            return true;
                        }
                        else if (corners is Vector2Int[] vector2IntCorners && vector2IntCorners.Length >= 4)
                        {
                            // Calculate center point from corners
                            var center = Vector2.zero;
                            foreach (var corner in vector2IntCorners)
                            {
                                center += new Vector2(corner.x, corner.y);
                            }
                            center /= vector2IntCorners.Length;
                            
                            centerPoint = new Vector2Int(
                                Mathf.RoundToInt(center.x),
                                Mathf.RoundToInt(center.y)
                            );
                            
                            Debug.Log($"[AprilTag] Found {propName} with {vector2IntCorners.Length} corners, center: {centerPoint}");
                            return true;
                        }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[AprilTag] Error accessing corner coordinates: {ex.Message}");
        }
        
        return false;
    }

    private Vector2Int Project3DToScreen(Vector3 worldPos, PassthroughCameraIntrinsics intrinsics)
    {
        // Convert 3D world position to 2D screen coordinates using camera intrinsics
        // This method projects the 3D tag position to 2D screen coordinates with proper distortion handling
        
        var fx = intrinsics.FocalLength.x;
        var fy = intrinsics.FocalLength.y;
        var cx = intrinsics.PrincipalPoint.x;
        var cy = intrinsics.PrincipalPoint.y;
        var skew = intrinsics.Skew;
        
        // Ensure we have a valid depth (z should be positive and within detection range)
        var z = Mathf.Clamp(Mathf.Abs(worldPos.z), minDetectionDistance, maxDetectionDistance);
        
        // Basic perspective projection
        var x = worldPos.x / z;
        var y = worldPos.y / z;
        
        // Apply camera intrinsics with skew correction
        var u = fx * x + skew * y + cx;
        var v = fy * y + cy;
        
        // Clamp to valid screen coordinates
        var screenX = Mathf.Clamp(Mathf.RoundToInt(u), 0, intrinsics.Resolution.x - 1);
        var screenY = Mathf.Clamp(Mathf.RoundToInt(v), 0, intrinsics.Resolution.y - 1);
        
        return new Vector2Int(screenX, screenY);
    }

    private float ApplyDistanceScaling(float distance)
    {
        // Apply non-linear scaling to improve accuracy across the wide distance range
        // This helps with both very close (0.5m) and very far (18m) tags
        
        if (distance <= 1.0f)
        {
            // For close tags (0.5m - 1m), use slight compression to prevent overshooting
            return distance * 0.9f;
        }
        else if (distance <= 5.0f)
        {
            // For medium distance tags (1m - 5m), use linear scaling
            return distance;
        }
        else if (distance <= 10.0f)
        {
            // For far tags (5m - 10m), use slight expansion
            return distance * 1.1f;
        }
        else
        {
            // For very far tags (10m - 18m), use more expansion
            return distance * 1.2f;
        }
    }




    private void ConfigureVisualizationForNoOcclusion(Transform visualization)
    {
        if (!ignoreOcclusion) return;

        // Configure all renderers to ignore occlusion
        var renderers = visualization.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            // Set render queue to be on top of everything else
            var materials = renderer.materials;
            foreach (var material in materials)
            {
                if (material != null)
                {
                    // Use a high but valid render queue value to render on top
                    material.renderQueue = 2000; // High but within valid range
                    
                    // Make sure the material doesn't write to depth buffer for occlusion
                    material.SetInt("_ZWrite", 0);
                    material.SetInt("_ZTest", 0); // Always pass depth test
                }
            }
        }

        // Configure Canvas components to render on top
        var canvases = visualization.GetComponentsInChildren<Canvas>();
        foreach (var canvas in canvases)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000; // High sorting order
        }

        // Configure UI elements to ignore raycast
        var graphicRaycasters = visualization.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>();
        foreach (var raycaster in graphicRaycasters)
        {
            raycaster.ignoreReversedGraphics = true;
        }
    }

    [ContextMenu("Setup Environment Raycast Manager")]
    public void SetupEnvironmentRaycastManager()
    {
        if (environmentRaycastManager == null)
        {
            environmentRaycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
            if (environmentRaycastManager != null)
            {
                Debug.Log($"[AprilTag] Found and assigned EnvironmentRaycastManager: {environmentRaycastManager.name}");
            }
            else
            {
                Debug.LogWarning("[AprilTag] No EnvironmentRaycastManager found in scene. Please add one from the MultiObjectDetection sample or disable usePassthroughRaycasting.");
            }
        }
        else
        {
            Debug.Log($"[AprilTag] EnvironmentRaycastManager already assigned: {environmentRaycastManager.name}");
        }
    }

    [ContextMenu("Calibrate Position Scale")]
    public void CalibratePositionScale()
    {
        Debug.Log("[AprilTag] Position Scale Calibration Helper");
        Debug.Log($"Current position scale factor: {positionScaleFactor}");
        Debug.Log("Try these values to fix scaling issues:");
        Debug.Log("  - If tags appear too far apart: Try 0.5 or 0.25");
        Debug.Log("  - If tags appear too close together: Try 2.0 or 4.0");
        Debug.Log("  - If tags appear at wrong distance: Try 0.1 to 10.0");
        Debug.Log("Adjust the 'Position Scale Factor' in the inspector and test with your tags.");
    }

    [ContextMenu("Set Scale Factor 0.5")]
    public void SetScaleFactorHalf()
    {
        positionScaleFactor = 0.5f;
        Debug.Log("[AprilTag] Position scale factor set to 0.5 (half size)");
    }

    [ContextMenu("Set Scale Factor 2.0")]
    public void SetScaleFactorDouble()
    {
        positionScaleFactor = 2.0f;
        Debug.Log("[AprilTag] Position scale factor set to 2.0 (double size)");
    }

    [ContextMenu("Reset Scale Factor")]
    public void ResetScaleFactor()
    {
        positionScaleFactor = 1.0f;
        Debug.Log("[AprilTag] Position scale factor reset to 1.0 (normal size)");
    }

    [ContextMenu("Set Range 0.5-18m")]
    public void SetWideRange()
    {
        minDetectionDistance = 0.5f;
        maxDetectionDistance = 18.0f;
        enableDistanceScaling = true;
        Debug.Log("[AprilTag] Detection range set to 0.5m - 18m with distance scaling enabled");
    }

    [ContextMenu("Set Range 1-10m")]
    public void SetMediumRange()
    {
        minDetectionDistance = 1.0f;
        maxDetectionDistance = 10.0f;
        enableDistanceScaling = true;
        Debug.Log("[AprilTag] Detection range set to 1m - 10m with distance scaling enabled");
    }

    [ContextMenu("Disable Distance Scaling")]
    public void DisableDistanceScaling()
    {
        enableDistanceScaling = false;
        Debug.Log("[AprilTag] Distance scaling disabled - using raw distances");
    }

    [ContextMenu("Enable Distance Scaling")]
    public void EnableDistanceScaling()
    {
        enableDistanceScaling = true;
        Debug.Log("[AprilTag] Distance scaling enabled");
    }


    [ContextMenu("Debug Headset Movement")]
    public void DebugHeadsetMovement()
    {
        var cam = GetCorrectCameraReference();
        Debug.Log($"[AprilTag] Headset Debug Info:");
        Debug.Log($"  - Camera Transform: {cam.name}");
        Debug.Log($"  - Camera Position: {cam.position:F3}");
        Debug.Log($"  - Camera Rotation: {cam.eulerAngles:F1}");
        Debug.Log($"  - Camera Forward: {cam.forward:F3}");
        Debug.Log($"  - Camera Right: {cam.right:F3}");
        Debug.Log($"  - Camera Up: {cam.up:F3}");
        Debug.Log($"  - Coordinate Correction: Disabled (removed to fix headset movement issues)");
        Debug.Log($"  - Use Passthrough Raycasting: {usePassthroughRaycasting}");
        
        if (cam.GetComponent<Camera>() != null)
        {
            var camera = cam.GetComponent<Camera>();
            Debug.Log($"  - Camera FOV: {camera.fieldOfView:F1}");
            Debug.Log($"  - Camera Near: {camera.nearClipPlane:F3}");
            Debug.Log($"  - Camera Far: {camera.farClipPlane:F3}");
        }
    }

    // Quest-compatible debugging methods
    public void ToggleDistanceScalingRuntime()
    {
        enableDistanceScaling = !enableDistanceScaling;
        Debug.Log($"[AprilTag] Distance scaling {(enableDistanceScaling ? "enabled" : "disabled")} via runtime call");
    }

    public void SetPositionScaleFactor(float scale)
    {
        positionScaleFactor = scale;
        Debug.Log($"[AprilTag] Position scale factor set to {scale} via runtime call");
    }

    public void LogCurrentSettings()
    {
        var cam = GetCorrectCameraReference();
        Debug.Log($"[AprilTag] Current Settings:");
        Debug.Log($"  - Position Scale Factor: {positionScaleFactor}");
        Debug.Log($"  - Distance Scaling: {enableDistanceScaling}");
        Debug.Log($"  - Passthrough Raycasting: {usePassthroughRaycasting}");
        Debug.Log($"  - Min Detection Distance: {minDetectionDistance}");
        Debug.Log($"  - Max Detection Distance: {maxDetectionDistance}");
        Debug.Log($"  - Camera: {cam.name} at {cam.position:F3}");
    }

    private void HandleQuestDebugInput()
    {
        // Quest controller input handling for debugging
        // This allows debugging on the actual headset without inspector access
        
        // Press X button to toggle debug logging
        if (OVRInput.GetDown(OVRInput.RawButton.X))
        {
            logDebugInfo = !logDebugInfo;
            Debug.Log($"[AprilTag] Debug logging: {logDebugInfo}");
        }
        
        // Press Y button to toggle detection logging
        if (OVRInput.GetDown(OVRInput.RawButton.Y))
        {
            logDetections = !logDetections;
            Debug.Log($"[AprilTag] Detection logging: {logDetections}");
        }
        
        // Press A button to toggle passthrough raycasting
        if (OVRInput.GetDown(OVRInput.RawButton.A))
        {
            usePassthroughRaycasting = !usePassthroughRaycasting;
            Debug.Log($"[AprilTag] Passthrough raycasting: {usePassthroughRaycasting}");
        }
        
        // Press right controller A button to toggle all debug logging
        if (OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch))
        {
            enableAllDebugLogging = !enableAllDebugLogging;
            logDetections = enableAllDebugLogging;
            logDebugInfo = enableAllDebugLogging;
            Debug.Log($"[AprilTag] All debug logging: {enableAllDebugLogging}");
        }
        
        // Press right controller B button to reset headset pose tracking
        if (OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch))
        {
            ResetHeadsetPoseTracking();
            Debug.Log($"[AprilTag] Reset headset pose tracking");
        }
        
        // Press B button to toggle improved intrinsics
        if (OVRInput.GetDown(OVRInput.RawButton.B))
        {
            useImprovedIntrinsics = !useImprovedIntrinsics;
            Debug.Log($"[AprilTag] Improved intrinsics: {useImprovedIntrinsics}");
        }
        
        // Press Left Trigger to toggle test mode identity rotation
        if (OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger))
        {
            testModeIdentityRotation = !testModeIdentityRotation;
            Debug.Log($"[AprilTag] Test mode identity rotation: {testModeIdentityRotation}");
        }
        
        // Press Right Trigger to toggle world-locked rotation
        if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
        {
            worldLockedRotation = !worldLockedRotation;
            Debug.Log($"[AprilTag] World-locked rotation: {worldLockedRotation}");
        }
        
        // Press Left Grip to increase visualization scale
        if (OVRInput.GetDown(OVRInput.RawButton.LHandTrigger))
        {
            visualizationScaleMultiplier *= 1.5f;
            Debug.Log($"[AprilTag] Visualization scale: {visualizationScaleMultiplier:F2}");
        }
        
        // Press Right Grip to decrease visualization scale
        if (OVRInput.GetDown(OVRInput.RawButton.RHandTrigger))
        {
            visualizationScaleMultiplier /= 1.5f;
            Debug.Log($"[AprilTag] Visualization scale: {visualizationScaleMultiplier:F2}");
        }
        
        // Press Left Thumbstick to toggle force corner-based positioning
        if (OVRInput.GetDown(OVRInput.RawButton.LThumbstick))
        {
            forceCornerBasedPositioning = !forceCornerBasedPositioning;
            Debug.Log($"[AprilTag] Force corner-based positioning: {forceCornerBasedPositioning}");
        }
        
        // Press Start button to reset all settings
        if (OVRInput.GetDown(OVRInput.RawButton.Start))
        {
            ResetDebugSettings();
        }
        
        // Log the current settings every 5 seconds when debugging is enabled
        if (logDebugInfo && Time.frameCount % 300 == 0) // Every 5 seconds at 60fps
        {
            LogCurrentSettings();
        }
        
        // Show current settings on screen every 2 seconds
        if (logDebugInfo && Time.frameCount % 120 == 0) // Every 2 seconds at 60fps
        {
            ShowCurrentSettingsOnScreen();
        }
    }
    
    private void ResetDebugSettings()
    {
        logDebugInfo = true;
        logDetections = true;
        usePassthroughRaycasting = true;
        useImprovedIntrinsics = false;
        testModeIdentityRotation = false;
        forceCornerBasedPositioning = false;
        worldLockedRotation = true;
        visualizationScaleMultiplier = 1.0f;
        Debug.Log("[AprilTag] Debug settings reset to defaults");
    }
    
    private void ShowCurrentSettingsOnScreen()
    {
        // Show current settings as debug text on screen
        Debug.Log($"[AprilTag] Current Settings - Scale: {visualizationScaleMultiplier:F2}, TestMode: {testModeIdentityRotation}, WorldLocked: {worldLockedRotation}, Passthrough: {usePassthroughRaycasting}, Intrinsics: {useImprovedIntrinsics}");
    }
    
    private List<object> GetRawDetections()
    {
        // Try to access raw detection data from the TagDetector using reflection
        try
        {
            if (_detector == null)
            {
                return new List<object>();
            }
            
            var detectorType = _detector.GetType();
            
            // Look for properties or fields that might contain raw detection data
            var properties = detectorType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var fields = detectorType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            // Try to find detection-related properties
            foreach (var prop in properties)
            {
                if (prop.Name.ToLower().Contains("detection") && !prop.Name.ToLower().Contains("detectedtags"))
                {
                    try
                    {
                        var value = prop.GetValue(_detector);
                        if (value != null)
                        {
                            if (value is System.Collections.IEnumerable enumerable)
                            {
                                var detections = new List<object>();
                                foreach (var item in enumerable)
                                {
                                    detections.Add(item);
                                }
                                return detections;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AprilTag] Error accessing property {prop.Name}: {e.Message}");
                    }
                }
            }
            
            // Try fields as well
            foreach (var field in fields)
            {
                if (field.Name.ToLower().Contains("detection") && !field.Name.ToLower().Contains("detectedtags"))
                {
                    try
                    {
                        var value = field.GetValue(_detector);
                        if (value != null)
                        {
                            if (value is System.Collections.IEnumerable enumerable)
                            {
                                var detections = new List<object>();
                                foreach (var item in enumerable)
                                {
                                    detections.Add(item);
                                }
                                return detections;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AprilTag] Error accessing field {field.Name}: {e.Message}");
                    }
                }
            }
            
            if (logDebugInfo)
            {
                Debug.LogWarning("[AprilTag] No raw detection data found - corner detection will not work");
            }
        }
        catch (Exception e)
        {
            if (logDebugInfo)
            {
                Debug.LogWarning($"[AprilTag] Error accessing raw detections: {e.Message}");
            }
        }
        
        return new List<object>();
    }
    
    private Vector2? TryGetCornerBasedCenter(int tagId, List<object> rawDetections)
    {
        // Try to find the raw detection data for this specific tag ID and extract corner coordinates
        try
        {
            foreach (var detection in rawDetections)
            {
                var detectionType = detection.GetType();
                
                // Try to get the ID field/property
                var idProperty = detectionType.GetProperty("ID") ?? detectionType.GetProperty("Id") ?? detectionType.GetProperty("id");
                var idField = detectionType.GetField("ID") ?? detectionType.GetField("Id") ?? detectionType.GetField("id");
                
                int detectionId = -1;
                if (idProperty != null)
                {
                    detectionId = (int)idProperty.GetValue(detection);
                }
                else if (idField != null)
                {
                    detectionId = (int)idField.GetValue(detection);
                }
                
                if (detectionId == tagId)
                {
                    // Found the matching detection, try to extract corner coordinates
                    return ExtractCornerCenter(detection);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AprilTag] Error extracting corner center for tag {tagId}: {e.Message}");
        }
        
        return null;
    }
    
    private Vector2? TryGetCornerBasedCenterWithIntrinsics(int tagId, List<object> rawDetections, PassthroughCameraIntrinsics intrinsics)
    {
        // Try to find the raw detection data for this specific tag ID and extract corner coordinates with intrinsics
        try
        {
            foreach (var detection in rawDetections)
            {
                var detectionType = detection.GetType();
                
                // Try to get the ID field/property
                var idProperty = detectionType.GetProperty("ID") ?? detectionType.GetProperty("Id") ?? detectionType.GetProperty("id");
                var idField = detectionType.GetField("ID") ?? detectionType.GetField("Id") ?? detectionType.GetField("id");
                
                int detectionId = -1;
                if (idProperty != null)
                {
                    detectionId = (int)idProperty.GetValue(detection);
                }
                else if (idField != null)
                {
                    detectionId = (int)idField.GetValue(detection);
                }
                
                if (detectionId == tagId)
                {
                    // Found the matching detection, try to extract corner coordinates with intrinsics
                    return ExtractCornerCenterWithIntrinsics(detection, intrinsics);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AprilTag] Error extracting corner center for tag {tagId}: {e.Message}");
        }
        
        return null;
    }
    
    private Vector2? ExtractCornerCenterWithIntrinsics(object detection, PassthroughCameraIntrinsics intrinsics)
    {
        // Extract corner coordinates from the Detection object and calculate center using camera intrinsics
        try
        {
            var detectionType = detection.GetType();
            
            // Try to access corner coordinates based on the Detection structure we found
            // The structure has: c0, c1, p00, p01, p10, p11, p20, p21, p30, p31
            // But they might be stored as arrays or in a different format
            var cornerFields = new[]
            {
                ("c0", "c1"),    // Corner 0
                ("p00", "p01"),  // Corner 1  
                ("p10", "p11"),  // Corner 2
                ("p20", "p21")   // Corner 3
            };
            
            // Also try alternative field names that might be used
            var alternativeFields = new[]
            {
                ("c", "c"),      // Single field with array
                ("p", "p"),      // Single field with array
                ("corners", "corners"), // Array of corners
                ("points", "points")    // Array of points
            };
            
            var corners = new List<Vector2>();
            
            foreach (var (xField, yField) in cornerFields)
            {
                // Try to get field first, then property with more permissive binding flags
                var xFieldRef = detectionType.GetField(xField, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var yFieldRef = detectionType.GetField(yField, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                double x = 0, y = 0;
                bool xFound = false, yFound = false;
                
                // Try to get X coordinate
                if (xFieldRef != null)
                {
                    try
                    {
                        var xValue = xFieldRef.GetValue(detection);
                        x = (double)xValue;
                        xFound = true;
                    }
                    catch (Exception e)
                    {
                        if (logDebugInfo)
                        {
                            Debug.LogWarning($"[AprilTag] Error getting {xField} field value: {e.Message}");
                        }
                    }
                }
                else
                {
                    var xProp = detectionType.GetProperty(xField);
                    if (xProp != null)
                    {
                        try
                        {
                            var xValue = xProp.GetValue(detection);
                            x = (double)xValue;
                            xFound = true;
                        }
                        catch (Exception e)
                        {
                            if (logDebugInfo)
                            {
                                Debug.LogWarning($"[AprilTag] Error getting {xField} property value: {e.Message}");
                            }
                        }
                    }
                }
                
                // Try to get Y coordinate
                if (yFieldRef != null)
                {
                    try
                    {
                        var yValue = yFieldRef.GetValue(detection);
                        y = (double)yValue;
                        yFound = true;
                    }
                    catch (Exception e)
                    {
                        if (logDebugInfo)
                        {
                            Debug.LogWarning($"[AprilTag] Error getting {yField} field value: {e.Message}");
                        }
                    }
                }
                else
                {
                    var yProp = detectionType.GetProperty(yField);
                    if (yProp != null)
                    {
                        try
                        {
                            var yValue = yProp.GetValue(detection);
                            y = (double)yValue;
                            yFound = true;
                        }
                        catch (Exception e)
                        {
                            if (logDebugInfo)
                            {
                                Debug.LogWarning($"[AprilTag] Error getting {yField} property value: {e.Message}");
                            }
                        }
                    }
                }
                
                if (xFound && yFound)
                {
                    // Convert coordinates using camera intrinsics for better alignment
                    var unityCorner = ConvertAprilTagToUnityCoordinatesWithIntrinsics(x, y, intrinsics);
                    corners.Add(unityCorner);
                }
            }
            
            if (corners.Count >= 4)
            {
                // Calculate center point from corners
                var center = Vector2.zero;
                foreach (var corner in corners)
                {
                    center += corner;
                }
                center /= corners.Count;
                
                return center;
            }
            else
            {
                // Try alternative field names
                foreach (var (xField, yField) in alternativeFields)
                {
                    var xFieldRef = detectionType.GetField(xField, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var yFieldRef = detectionType.GetField(yField, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (xFieldRef != null && yFieldRef != null)
                    {
                        try
                        {
                            var xValue = xFieldRef.GetValue(detection);
                            var yValue = yFieldRef.GetValue(detection);
                            
                            // Check if these are arrays
                            if (xValue is System.Array xArray && yValue is System.Array yArray)
                            {
                                if (xArray.Length >= 4 && yArray.Length >= 4)
                                {
                                    for (int i = 0; i < 4; i++)
                                    {
                                        var x = Convert.ToDouble(xArray.GetValue(i));
                                        var y = Convert.ToDouble(yArray.GetValue(i));
                                        // Convert coordinates using camera intrinsics for better alignment
                                        var unityCorner = ConvertAprilTagToUnityCoordinatesWithIntrinsics(x, y, intrinsics);
                                        corners.Add(unityCorner);
                                    }
                                    
                                    break;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            if (logDebugInfo)
                            {
                                Debug.LogWarning($"[AprilTag] Error with alternative fields {xField}, {yField}: {e.Message}");
                            }
                        }
                    }
                }
                
                if (corners.Count >= 4)
                {
                    // Calculate center point from corners
                    var center = Vector2.zero;
                    foreach (var corner in corners)
                    {
                        center += corner;
                    }
                    center /= corners.Count;
                    
                    return center;
                }
            }
        }
        catch (Exception e)
        {
            if (logDebugInfo)
            {
                Debug.LogWarning($"[AprilTag] Error extracting corner center: {e.Message}");
            }
        }
        
        return null;
    }
    
    private Vector2 ConvertAprilTagToUnityCoordinates(double x, double y)
    {
        // Convert from AprilTag image coordinates to Unity screen coordinates
        // Following MultiObjectDetection example exactly
        // AprilTag: X-right, Y-down (image space)
        // Unity: X-right, Y-up (screen space)
        // MultiObjectDetection uses: (1.0f - perY) for Y flip
        
        return new Vector2((float)x, (float)y);
    }
    
    private Vector2 ConvertAprilTagToUnityCoordinatesWithIntrinsics(double x, double y, PassthroughCameraIntrinsics intrinsics)
    {
        // Convert from AprilTag image coordinates to Unity screen coordinates using camera intrinsics
        // This provides better alignment by accounting for camera-specific parameters
        
        // Normalize coordinates to [0,1] range
        var perX = (float)x / intrinsics.Resolution.x;
        var perY = (float)y / intrinsics.Resolution.y;
        
        // Apply Y-flip transformation like MultiObjectDetection: (1.0f - perY)
        var flippedPerY = 1.0f - perY;
        
        // Convert back to pixel coordinates
        var screenX = perX * intrinsics.Resolution.x;
        var screenY = flippedPerY * intrinsics.Resolution.y;
        
        return new Vector2(screenX, screenY);
    }
    
    private Quaternion GetWorldRotation(Quaternion aprilTagRotation, Vector3 tagWorldPosition)
    {
        if (testModeIdentityRotation)
        {
            // Test mode: use identity rotation to check if positioning is correct
            return Quaternion.identity;
        }
        else if (worldLockedRotation)
        {
            // For world-locked tags, use a fixed rotation that doesn't change with headset movement
            // This prevents the cube from rotating when the headset pose is reset
            return Quaternion.identity * Quaternion.Euler(rotationOffset);
        }
        else
        {
            // For normal tags, use corner-based rotation calculation
            // This ensures the cube sits flat on the tag surface using actual corner coordinates
            return GetCornerBasedRotation(0, new List<object>(), tagWorldPosition) * Quaternion.Euler(rotationOffset);
        }
    }
    
    private Quaternion GetHeadsetRelativeRotation(Quaternion aprilTagRotation, Vector3 tagWorldPosition)
    {
        // Use corner-based rotation calculation similar to PhotonVision
        // This ensures the cube sits flat on the tag surface using the actual corner coordinates
        
        // Get the current headset pose
        var cam = GetCorrectCameraReference();
        var currentHeadsetRotation = cam.rotation;
        var currentHeadsetPosition = cam.position;
        
        // Initialize headset pose tracking on first frame
        if (!_headsetPoseInitialized)
        {
            _lastHeadsetRotation = currentHeadsetRotation;
            _lastHeadsetPosition = currentHeadsetPosition;
            _headsetPoseInitialized = true;
            
            if (enableAllDebugLogging && logDebugInfo)
            {
                Debug.Log($"[AprilTag] Initialized headset pose tracking - Rotation: {currentHeadsetRotation.eulerAngles}, Position: {currentHeadsetPosition}");
            }
        }
        
        // Calculate the headset's rotation change since last frame
        var headsetRotationDelta = Quaternion.Inverse(_lastHeadsetRotation) * currentHeadsetRotation;
        
        // Apply the headset rotation change to the AprilTag rotation
        // This keeps the cube orientation consistent with the headset's movement
        var adjustedRotation = headsetRotationDelta * aprilTagRotation;
        
        // Update the last headset pose for next frame
        _lastHeadsetRotation = currentHeadsetRotation;
        _lastHeadsetPosition = currentHeadsetPosition;
        
        if (enableAllDebugLogging && logDebugInfo)
        {
            Debug.Log($"[AprilTag] Headset-relative rotation - AprilTag: {aprilTagRotation.eulerAngles}, Headset Delta: {headsetRotationDelta.eulerAngles}, Adjusted: {adjustedRotation.eulerAngles}");
        }
        
        return adjustedRotation;
    }
    
    private Quaternion GetCornerBasedRotation(int tagId, List<object> rawDetections, Vector3 tagWorldPosition)
    {
        // Use corner coordinates to calculate proper tag orientation
        // This approach is similar to PhotonVision's method for ensuring tags sit flat
        
        try
        {
            // Find the detection for this tag
            foreach (var detection in rawDetections)
            {
                var idField = detection.GetType().GetField("id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (idField != null)
                {
                    var detectedId = (int)idField.GetValue(detection);
                    if (detectedId == tagId)
                    {
                        // Extract corner coordinates
                        var corners = ExtractCornerCoordinates(detection);
                        if (corners.Count >= 4)
                        {
                            // Calculate tag orientation from corner coordinates
                            return CalculateTagOrientationFromCorners(corners, tagWorldPosition);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (enableAllDebugLogging && logDebugInfo)
            {
                Debug.LogWarning($"[AprilTag] Error calculating corner-based rotation: {e.Message}");
            }
        }
        
        // Fallback to AprilTag rotation if corner-based calculation fails
        return Quaternion.identity;
    }
    
    private List<Vector2> ExtractCornerCoordinates(object detection)
    {
        var corners = new List<Vector2>();
        
        try
        {
            // Try to extract corner coordinates from the detection
            var cornerFields = new[] { "c0", "c1", "p00", "p01", "p10", "p11", "p20", "p21", "p30", "p31" };
            
            for (int i = 0; i < cornerFields.Length; i += 2)
            {
                if (i + 1 < cornerFields.Length)
                {
                    var xField = detection.GetType().GetField(cornerFields[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var yField = detection.GetType().GetField(cornerFields[i + 1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (xField != null && yField != null)
                    {
                        var x = (double)xField.GetValue(detection);
                        var y = (double)yField.GetValue(detection);
                        corners.Add(new Vector2((float)x, (float)y));
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (enableAllDebugLogging && logDebugInfo)
            {
                Debug.LogWarning($"[AprilTag] Error extracting corner coordinates: {e.Message}");
            }
        }
        
        return corners;
    }
    
    private Quaternion CalculateTagOrientationFromCorners(List<Vector2> corners, Vector3 tagWorldPosition)
    {
        if (corners.Count < 4) return Quaternion.identity;
        
        // Calculate the tag's orientation from corner coordinates
        // This ensures the cube sits flat on the tag surface
        
        // Get camera reference for coordinate transformation
        var cam = GetCorrectCameraReference();
        
        // Convert corner coordinates to world space using proper raycasting
        var worldCorners = new List<Vector3>();
        foreach (var corner in corners)
        {
            // Convert 2D corner to 3D world position using raycasting
            var screenPos = new Vector2(corner.x, corner.y);
            
            // Use the existing GetWorldPositionFromCornerCenter method for consistency
            // Create a temporary TagPose for the raycasting
            var tempTagPose = new TagPose(0, tagWorldPosition, Quaternion.identity);
            var worldPos = GetWorldPositionFromCornerCenter(screenPos, tempTagPose);
            worldCorners.Add(worldPos);
        }
        
        // Calculate the tag's normal vector from the corners
        // This gives us the direction the tag is facing
        if (worldCorners.Count >= 4)
        {
            // Calculate two vectors on the tag surface
            var v1 = (worldCorners[1] - worldCorners[0]);
            var v2 = (worldCorners[3] - worldCorners[0]);
            
            // Check if vectors are valid (not zero length)
            if (v1.magnitude > 0.001f && v2.magnitude > 0.001f)
            {
                v1 = v1.normalized;
                v2 = v2.normalized;
                
                // Calculate the normal vector (perpendicular to the tag surface)
                var normal = Vector3.Cross(v1, v2);
                
                // Check if normal is valid (not zero length)
                if (normal.magnitude > 0.001f)
                {
                    normal = normal.normalized;
                    
                    // Create a rotation that aligns the cube with the tag surface
                    // The cube should face the same direction as the tag
                    
                    // Calculate the tag's orientation from the corner vectors
                    // This gives us the proper rotation including Z-axis
                    var tagRight = v1; // First edge vector
                    var tagUp = Vector3.Cross(normal, tagRight).normalized; // Perpendicular to normal and right
                    
                    // Create a rotation matrix from the tag's coordinate system
                    var tagRotation = Quaternion.LookRotation(normal, tagUp);
                    
                    // Apply a 90-degree rotation around X-axis to align with AprilTag orientation
                    // This ensures the cube sits flat on the tag surface
                    var cubeRotation = tagRotation * Quaternion.Euler(90f, 0f, 0f);
                    
                    if (enableAllDebugLogging && logDebugInfo)
                    {
                        Debug.Log($"[AprilTag] Corner-based rotation - Normal: {normal}, Tag Rotation: {tagRotation.eulerAngles}, Cube Rotation: {cubeRotation.eulerAngles}");
                        Debug.Log($"[AprilTag] Corner vectors - v1: {v1}, v2: {v2}, tagUp: {tagUp}");
                        Debug.Log($"[AprilTag] World corners: {string.Join(", ", worldCorners.Select(c => c.ToString("F3")))}");
                    }
                    
                    return cubeRotation;
                }
                else
                {
                    if (enableAllDebugLogging && logDebugInfo)
                    {
                        Debug.LogWarning($"[AprilTag] Invalid normal vector from corners - v1: {v1}, v2: {v2}");
                    }
                }
            }
            else
            {
                if (enableAllDebugLogging && logDebugInfo)
                {
                    Debug.LogWarning($"[AprilTag] Invalid corner vectors - v1: {v1}, v2: {v2}");
                }
            }
        }
        
        return Quaternion.identity;
    }
    
    private void ResetHeadsetPoseTracking()
    {
        // Reset headset pose tracking - useful when the headset pose is reset
        _headsetPoseInitialized = false;
        _lastHeadsetRotation = Quaternion.identity;
        _lastHeadsetPosition = Vector3.zero;
    }
    
    private Quaternion ConvertAprilTagRotationToWorldSpace(Quaternion aprilTagRotation)
    {
        // Convert AprilTag rotation to world-locked rotation
        // For world-locked tags, we want the rotation to be independent of camera movement
        
        // Simply convert AprilTag rotation to Unity coordinate system
        // This gives us the tag's orientation relative to the world, not the camera
        var worldRotation = ConvertAprilTagRotationToUnity(aprilTagRotation);
        
        return worldRotation;
    }
    
    private Quaternion ConvertAprilTagRotationToUnity(Quaternion aprilTagRotation)
    {
        // Convert AprilTag rotation to Unity rotation
        // Apply coordinate system transformation
        // This handles the Z-axis rotation mapping to X-axis rotation issue
        var convertedRotation = aprilTagRotation;
        // Apply 180-degree rotation around Y-axis to align coordinate systems
        var coordinateTransform = Quaternion.Euler(0f, 0f, 0f);
        convertedRotation = coordinateTransform * convertedRotation;
        
        // Reassign axes for coordinate system mapping
        var eulerAngles = convertedRotation.eulerAngles;
        var x = eulerAngles.x;
        var y = eulerAngles.y;
        var z = eulerAngles.z;
        
        // Reassign axes (modify these to test different mappings)
        var newX = -x;  // Try: y, z, -x, -y, -z
        var newY = y;  // Try: x, z, -x, -y, -z
        var newZ = z;  // Try: x, y, -x, -y, -z
        
        convertedRotation = Quaternion.Euler(newX, newY, newZ);
        
        return convertedRotation;
    }
    
    private Vector2? ExtractCornerCenter(object detection)
    {
        // Extract corner coordinates from the Detection object and calculate center
        try
        {
            var detectionType = detection.GetType();
            
            
            // Try to access corner coordinates based on the Detection structure we found
            // The structure has: c0, c1, p00, p01, p10, p11, p20, p21, p30, p31
            // But they might be stored as arrays or in a different format
            var cornerFields = new[]
            {
                ("c0", "c1"),    // Corner 0
                ("p00", "p01"),  // Corner 1  
                ("p10", "p11"),  // Corner 2
                ("p20", "p21")   // Corner 3
            };
            
            // Also try alternative field names that might be used
            var alternativeFields = new[]
            {
                ("c", "c"),      // Single field with array
                ("p", "p"),      // Single field with array
                ("corners", "corners"), // Array of corners
                ("points", "points")    // Array of points
            };
            
            var corners = new List<Vector2>();
            
            foreach (var (xField, yField) in cornerFields)
            {
                // Try to get field first, then property with more permissive binding flags
                var xFieldRef = detectionType.GetField(xField, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var yFieldRef = detectionType.GetField(yField, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                
                double x = 0, y = 0;
                bool xFound = false, yFound = false;
                
                // Try to get X coordinate
                if (xFieldRef != null)
                {
                    try
                    {
                        var xValue = xFieldRef.GetValue(detection);
                        x = (double)xValue;
                        xFound = true;
                    }
                    catch (Exception e)
                    {
                        if (logDebugInfo)
                        {
                            Debug.LogWarning($"[AprilTag] Error getting {xField} field value: {e.Message}");
                        }
                    }
                }
                else
                {
                    var xProp = detectionType.GetProperty(xField);
                    if (xProp != null)
                    {
                        try
                        {
                            var xValue = xProp.GetValue(detection);
                            x = (double)xValue;
                            xFound = true;
                        }
                        catch (Exception e)
                        {
                            if (logDebugInfo)
                            {
                                Debug.LogWarning($"[AprilTag] Error getting {xField} property value: {e.Message}");
                            }
                        }
                    }
                }
                
                // Try to get Y coordinate
                if (yFieldRef != null)
                {
                    try
                    {
                        var yValue = yFieldRef.GetValue(detection);
                        y = (double)yValue;
                        yFound = true;
                    }
                    catch (Exception e)
                    {
                        if (logDebugInfo)
                        {
                            Debug.LogWarning($"[AprilTag] Error getting {yField} field value: {e.Message}");
                        }
                    }
                }
                else
                {
                    var yProp = detectionType.GetProperty(yField);
                    if (yProp != null)
                    {
                        try
                        {
                            var yValue = yProp.GetValue(detection);
                            y = (double)yValue;
                            yFound = true;
                        }
                        catch (Exception e)
                        {
                            if (logDebugInfo)
                            {
                                Debug.LogWarning($"[AprilTag] Error getting {yField} property value: {e.Message}");
                            }
                        }
                    }
                }
                
                if (xFound && yFound)
                {
                    // Convert coordinates from AprilTag's right-handed to Unity's left-handed coordinate system
                    var unityCorner = ConvertAprilTagToUnityCoordinates(x, y);
                    corners.Add(unityCorner);
                }
            }
            
            if (corners.Count >= 4)
            {
                // Calculate center point from corners
                var center = Vector2.zero;
                foreach (var corner in corners)
                {
                    center += corner;
                }
                center /= corners.Count;
                
                
                return center;
            }
            else
            {
                // Try alternative field names
                foreach (var (xField, yField) in alternativeFields)
                {
                    var xFieldRef = detectionType.GetField(xField, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var yFieldRef = detectionType.GetField(yField, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (xFieldRef != null && yFieldRef != null)
                    {
                        try
                        {
                            var xValue = xFieldRef.GetValue(detection);
                            var yValue = yFieldRef.GetValue(detection);
                            
                            // Check if these are arrays
                            if (xValue is System.Array xArray && yValue is System.Array yArray)
                            {
                                if (xArray.Length >= 4 && yArray.Length >= 4)
                                {
                                    for (int i = 0; i < 4; i++)
                                    {
                                        var x = Convert.ToDouble(xArray.GetValue(i));
                                        var y = Convert.ToDouble(yArray.GetValue(i));
                                        // Convert coordinates from AprilTag's right-handed to Unity's left-handed coordinate system
                                        var unityCorner = ConvertAprilTagToUnityCoordinates(x, y);
                                        corners.Add(unityCorner);
                                    }
                                    
                                    break;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            if (logDebugInfo)
                            {
                                Debug.LogWarning($"[AprilTag] Error with alternative fields {xField}, {yField}: {e.Message}");
                            }
                        }
                    }
                }
                
                if (corners.Count >= 4)
                {
                    // Calculate center point from corners
                    var center = Vector2.zero;
                    foreach (var corner in corners)
                    {
                        center += corner;
                    }
                    center /= corners.Count;
                    
                    return center;
                }
            }
        }
        catch (Exception e)
        {
            if (logDebugInfo)
            {
                Debug.LogWarning($"[AprilTag] Error extracting corner center: {e.Message}");
            }
        }
        
        return null;
    }
    
    private Vector3 GetWorldPositionFromCornerCenter(Vector2 cornerCenter, TagPose tagPose)
    {
        // Follow MultiObjectDetection pattern exactly for 2D-to-3D projection
        try
        {
            // Get camera intrinsics and resolution
            var eye = GetWebCamManagerEye();
            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
            var camRes = intrinsics.Resolution;
            
            // Convert corner center to normalized coordinates (0-1 range)
            var perX = cornerCenter.x / camRes.x;
            var perY = cornerCenter.y / camRes.y;
            
            // Apply Y-flip transformation like MultiObjectDetection: (1.0f - perY)
            var flippedPerY = 1.0f - perY;
            
            // Convert to pixel coordinates with Y-flip
            var centerPixel = new Vector2Int(
                Mathf.RoundToInt(perX * camRes.x), 
                Mathf.RoundToInt(flippedPerY * camRes.y)
            );
            
            // Create ray from screen point using proper camera intrinsics
            var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(eye, centerPixel);
            
            // Use environment raycasting to place object on ground (like the working method)
            if (environmentRaycastManager != null)
            {
                if (environmentRaycastManager.Raycast(ray, out var hitInfo))
                {
                    if (logDebugInfo)
                    {
                        Debug.Log($"[AprilTag] Corner-based positioning hit at: {hitInfo.point}");
                    }
                    return hitInfo.point;
                }
                else
                {
                    if (logDebugInfo)
                    {
                        Debug.LogWarning("[AprilTag] Corner-based positioning: Environment raycast missed, using fallback");
                    }
                }
            }
            
            // Fallback: use AprilTag's 3D pose directly for more accurate positioning
            var cam = GetCorrectCameraReference();
            var adjustedPosition = (tagPose.Position + positionOffset) * positionScaleFactor;
            var worldPosition = cam.position + cam.rotation * adjustedPosition;
            
            if (logDebugInfo)
            {
                Debug.Log($"[AprilTag] Corner-based positioning fallback: {worldPosition}");
            }
            
            return worldPosition;
        }
        catch (Exception e)
        {
            if (logDebugInfo)
            {
                Debug.LogWarning($"[AprilTag] Error in corner-based positioning: {e.Message}");
            }
            
            // Final fallback to 3D pose estimation
            return tagPose.Position * positionScaleFactor;
        }
    }
}
