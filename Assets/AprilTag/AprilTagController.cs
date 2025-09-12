// Assets/AprilTag/AprilTagController.cs
// Quest-only AprilTag tracker using Meta Passthrough + locally integrated AprilTag library.
// Uses reflection to read WebCamTexture so there's no compile-time dependency on WebCamTextureManager.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using AprilTag; // locally integrated AprilTag library
using PassthroughCameraSamples;

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

    // CPU buffers
    private Color32[] _rgba;

    // Detector (recreated when size/decimate changes)
    private TagDetector _detector;
    private int _detW, _detH, _detDecim;

    private float _nextDetectT;
    private readonly Dictionary<int, Transform> _vizById = new();
    private int _previousTagCount = 0;

    void OnDisable() => DisposeDetector();
    void OnDestroy() => DisposeDetector();

    void Awake()
    {
        // Fix Input System issues on startup
        InputSystemFixer.FixAllEventSystems();
    }

    void Update()
    {
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

        // Visualize detected tags
        var seen = new HashSet<int>();
        var detectedCount = 0;
        foreach (var t in _detector.DetectedTags)
        {
            detectedCount++;
            seen.Add(t.ID);
            if (logDetections) Debug.Log($"[AprilTag] id={t.ID} pos={t.Position:F3} euler={t.Rotation.eulerAngles:F1} quat={t.Rotation:F4}");

            if (!_vizById.TryGetValue(t.ID, out var tr) || tr == null)
            {
                if (!tagVizPrefab) continue;
                tr = Instantiate(tagVizPrefab).transform;
                tr.name = $"AprilTag_{t.ID}";
                _vizById[t.ID] = tr;
            }

            // Tag poses are camera-relative; place them in world via the HMD camera.
            var cam = Camera.main ? Camera.main.transform : transform;
            tr.SetPositionAndRotation(cam.TransformPoint(t.Position), cam.rotation * t.Rotation);
            if (scaleVizToTagSize) tr.localScale = Vector3.one * tagSizeMeters;
            tr.gameObject.SetActive(true);
        }

        // Log detection results and track tag count changes
        if (logDebugInfo && detectedCount > 0)
        {
            Debug.Log($"[AprilTag] Detected {detectedCount} tags this frame");
        }
        
        // Notify when tag count drops from non-zero to zero
        if (logDebugInfo && _previousTagCount > 0 && detectedCount == 0)
        {
            Debug.Log($"[AprilTag] All tags lost - count dropped from {_previousTagCount} to 0");
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
}
