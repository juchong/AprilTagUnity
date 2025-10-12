// Assets/AprilTag/AprilTagWebcamPipeline.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AprilTag;
using Meta.XR;
using PassthroughCameraSamples;
using Unity.XR.CoreUtils;
using UnityEngine;

public class AprilTagWebcamPipeline : MonoBehaviour
{
    // References and configuration needed by the pipeline
    [Header("Passthrough Feed (Pipeline)")]
    [Tooltip("WebCamTextureManager from Meta Passthrough samples (auto-created if null)")]
    [SerializeField]
    private UnityEngine.Object m_webCamManager; // Typically PassthroughCameraSamples.WebCamTextureManager

    [Tooltip("Auto-create WebCamTextureManager if not found")]
    [SerializeField]
    private bool m_autoCreateWebCamManager = true;

    [SerializeField]
    private WebCamTexture m_webCamTextureOverride;

    [Header("Camera Reference (Pipeline)")]
    [SerializeField]
    private Camera m_referenceCamera;

    [SerializeField]
    private bool m_useCenterEyeTransform = true;

    [Header("Debug Logging")]
    [Tooltip("Enable debug logging for webcam pipeline operations")]
    [SerializeField]
    private bool m_enableDebugLogging = false;

    // Detector state (only used if this component owns the detector)
    private TagDetector m_detector;
    private int m_detW,
        m_detH,
        m_detDecim;
    private AprilTagGPUPreprocessor m_gpuPreprocessor;

    [SerializeField]
    private AprilTag.Interop.TagFamily m_tagFamily = AprilTag.Interop.TagFamily.Tag36h11;

    private void Awake()
    {
        // Auto-create WebCamTextureManager if needed
        if (m_webCamManager == null && m_autoCreateWebCamManager && m_webCamTextureOverride == null)
        {
            // Try to find existing WebCamTextureManager
            var existingManager =
                FindFirstObjectByType<PassthroughCameraSamples.WebCamTextureManager>();

            if (existingManager != null)
            {
                m_webCamManager = existingManager;

                if (m_enableDebugLogging)
                {
                    Debug.Log("[WebcamPipeline] Found existing WebCamTextureManager");
                }
            }
            else
            {
                // Create WebCamTextureManager automatically
                var managerObj = new GameObject("WebCamTextureManager");
                var manager =
                    managerObj.AddComponent<PassthroughCameraSamples.WebCamTextureManager>();
                m_webCamManager = manager;

                if (m_enableDebugLogging)
                {
                    Debug.Log("[WebcamPipeline] Auto-created WebCamTextureManager");
                }
            }
        }
    }

    /// <summary>
    /// Returns the active passthrough camera eye from the WebCam manager.
    /// </summary>
    public PassthroughCameraEye GetWebCamManagerEye()
    {
        if (m_webCamManager != null)
        {
            var managerType = m_webCamManager.GetType();
            var eyeField = managerType.GetField(
                "Eye",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (eyeField != null)
            {
                return (PassthroughCameraEye)eyeField.GetValue(m_webCamManager);
            }
        }
        return PassthroughCameraEye.Left; // Default to left eye
    }

    public WebCamTexture GetActiveWebCamTexture()
    {
        if (m_webCamTextureOverride)
        {
            return m_webCamTextureOverride;
        }

        // First try to get WebCamTexture from assigned webCamManager
        if (m_webCamManager)
        {
            // Try to read WebCamTextureManager.WebCamTexture (Meta sample) via reflection
            var t = m_webCamManager.GetType();
            var prop = t.GetProperty(
                "WebCamTexture",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (prop != null && typeof(WebCamTexture).IsAssignableFrom(prop.PropertyType))
            {
                var wct = prop.GetValue(m_webCamManager) as WebCamTexture;
                if (wct != null)
                    return wct;
            }

            // Fallbacks (if your provider exposes Texture/SourceTexture)
            var texProp =
                t.GetProperty(
                    "Texture",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                )
                ?? t.GetProperty(
                    "SourceTexture",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            var fallbackWct = texProp?.GetValue(m_webCamManager) as WebCamTexture;
            if (fallbackWct != null)
                return fallbackWct;
        }

        // If no assigned manager or it didn't work, try to find WebCamTextureManager in the scene
        var webCamTextureManager = FindFirstObjectByType<WebCamTextureManager>();
        if (webCamTextureManager != null)
        {
            var wct = webCamTextureManager.WebCamTexture;
            return wct;
        }
        return null;
    }

    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Called in multiple helpers and Update)
    /// <summary>
    /// Returns the appropriate camera transform for world coordinate conversion on Quest.
    /// </summary>
    public Transform GetCorrectCameraReference()
    {
        // If a specific reference camera is assigned, use it
        if (m_referenceCamera != null)
        {
            return m_referenceCamera.transform;
        }

        // Quest-specific: Try to use the center eye transform for better positioning
        if (m_useCenterEyeTransform)
        {
            // Look for OVRCameraRig or similar VR camera rig
            var cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig != null)
            {
                // Use the center eye anchor for better positioning
                var centerEyeAnchor = cameraRig.centerEyeAnchor;
                if (centerEyeAnchor != null)
                {
                    if (m_enableDebugLogging)
                        Debug.Log(
                            $"[WebcamPipeline] Using OVRCameraRig center eye anchor for Quest positioning"
                        );
                    return centerEyeAnchor;
                }
            }

            // Alternative: Look for XR Origin or similar
            var xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                if (m_enableDebugLogging)
                    Debug.Log($"[WebcamPipeline] Using XR Origin camera for Quest positioning");
                return xrOrigin.Camera.transform;
            }
        }

        // Try to find the correct camera for VR/AR applications
        // First, try to find cameras with specific tags or names that might indicate passthrough/AR cameras
        var cameras = FindObjectsByType<Camera>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        // Look for cameras that might be the passthrough camera
        foreach (var cam in cameras)
        {
            // Check if this camera has a name that suggests it's the passthrough camera
            if (
                cam.name.ToLower().Contains("passthrough")
                || cam.name.ToLower().Contains("ar")
                || cam.name.ToLower().Contains("xr")
                || cam.name.ToLower().Contains("center")
                || cam.name.ToLower().Contains("main")
            )
            {
                if (m_enableDebugLogging)
                    Debug.Log(
                        $"[WebcamPipeline] Using camera '{cam.name}' as reference for tag positioning"
                    );
                return cam.transform;
            }
        }

        // If no specific camera found, try to get the camera from the WebCam manager
        if (m_webCamManager != null)
        {
            var managerType = m_webCamManager.GetType();
            var cameraField = managerType.GetField(
                "Camera",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (cameraField != null)
            {
                var cam = cameraField.GetValue(m_webCamManager) as Camera;
                if (cam != null)
                {
                    if (m_enableDebugLogging)
                        Debug.Log(
                            $"[WebcamPipeline] Using WebCam manager camera '{cam.name}' as reference for tag positioning"
                        );
                    return cam.transform;
                }
            }
        }

        // Fallback to Camera.main or this transform
        var fallbackCam = Camera.main ? Camera.main.transform : transform;
        if (m_enableDebugLogging)
            Debug.Log(
                $"[WebcamPipeline] Using fallback camera '{fallbackCam.name}' as reference for tag positioning"
            );
        return fallbackCam;
    }

    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Called from Update)
    private void RecreateDetectorIfNeeded(int width, int height, int dec)
    {
        DisposeDetector();
        m_detector = new TagDetector(width, height, m_tagFamily, Mathf.Max(1, dec)); // <� width, height, decimation
        m_detW = width;
        m_detH = height;
        m_detDecim = Mathf.Max(1, dec);

        if (m_enableDebugLogging)
            Debug.Log(
                $"[WebcamPipeline] Created detector: {width}x{height}, family={m_tagFamily}, decimate={Mathf.Max(1, dec)}"
            );
    }

    /// <summary>
    /// Helper to construct a detector without exposing TagDetector creation details to callers.
    /// The caller owns disposal of the returned detector.
    /// </summary>
    public TagDetector CreateDetector(
        int width,
        int height,
        AprilTag.Interop.TagFamily family,
        int dec
    )
    {
        return new TagDetector(width, height, family, Mathf.Max(1, dec));
    }

    /// <summary>
    /// Returns raw detections for a given detector instance if such data is available.
    /// Uses a permissive search to accommodate different implementations.
    /// </summary>
    public List<object> GetRawDetections(TagDetector detector)
    {
        try
        {
            if (detector == null)
            {
                return new List<object>();
            }

            var detectorType = detector.GetType();

            // Look for properties or fields that might contain raw detection data
            var properties = detectorType.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
            var fields = detectorType.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );

            foreach (var prop in properties)
            {
                if (
                    prop.Name.ToLower().Contains("detection")
                    && !prop.Name.ToLower().Contains("detectedtags")
                )
                {
                    try
                    {
                        var value = prop.GetValue(detector);
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
                        Debug.LogWarning(
                            $"[WebcamPipeline] Error accessing property {prop.Name}: {e.Message}"
                        );
                    }
                }
            }

            foreach (var field in fields)
            {
                if (
                    field.Name.ToLower().Contains("detection")
                    && !field.Name.ToLower().Contains("detectedtags")
                )
                {
                    try
                    {
                        var value = field.GetValue(detector);
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
                        Debug.LogWarning(
                            $"[WebcamPipeline] Error accessing field {field.Name}: {e.Message}"
                        );
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (m_enableDebugLogging)
            {
                Debug.LogWarning($"[WebcamPipeline] Error accessing raw detections: {e.Message}");
            }
        }

        return new List<object>();
    }

    private List<object> GetRawDetections()
    {
        // Try to access raw detection data from the TagDetector using reflection
        try
        {
            if (m_detector == null)
            {
                return new List<object>();
            }

            var detectorType = m_detector.GetType();

            // Look for properties or fields that might contain raw detection data
            var properties = detectorType.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
            var fields = detectorType.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );

            // Try to find detection-related properties
            foreach (var prop in properties)
            {
                if (
                    prop.Name.ToLower().Contains("detection")
                    && !prop.Name.ToLower().Contains("detectedtags")
                )
                {
                    try
                    {
                        var value = prop.GetValue(m_detector);
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
                        Debug.LogWarning(
                            $"[WebcamPipeline] Error accessing property {prop.Name}: {e.Message}"
                        );
                    }
                }
            }

            // Try fields as well
            foreach (var field in fields)
            {
                if (
                    field.Name.ToLower().Contains("detection")
                    && !field.Name.ToLower().Contains("detectedtags")
                )
                {
                    try
                    {
                        var value = field.GetValue(m_detector);
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
                        Debug.LogWarning(
                            $"[WebcamPipeline] Error accessing field {field.Name}: {e.Message}"
                        );
                    }
                }
            }

            if (m_enableDebugLogging)
            {
                Debug.LogWarning(
                    "[WebcamPipeline] No raw detection data found - corner detection will not work"
                );
            }
        }
        catch (Exception e)
        {
            if (m_enableDebugLogging)
            {
                Debug.LogWarning($"[WebcamPipeline] Error accessing raw detections: {e.Message}");
            }
        }

        return new List<object>();
    }

    private void DisposeDetector()
    {
        m_detector?.Dispose();
        m_detector = null;

        m_gpuPreprocessor?.Dispose();
        m_gpuPreprocessor = null;
    }
}
