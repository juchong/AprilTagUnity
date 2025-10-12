// Assets/AprilTag/AprilTagPermissionsManager.cs
// Permission management for AprilTag functionality including camera and spatial data access

using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class AprilTagPermissionsManager : MonoBehaviour
{
    [Header("Permission Settings")]
    [SerializeField]
    private bool requestPermissionsOnStart = true;

    [SerializeField]
    private bool m_retryOnDenial = true;

    [SerializeField]
    private float m_retryDelaySeconds = 2f;

    // Permission constants
    public static readonly string[] RequiredPermissions =
    {
        "android.permission.CAMERA", // Required for WebCamTexture
        "horizonos.permission.HEADSET_CAMERA", // Required for Passthrough Camera API (Horizon OS v74+)
        "com.oculus.permission.USE_SCENE", // Required for spatial data access
    };

    // Permission state tracking
    public static bool HasAllPermissions { get; private set; } = false;
    public static bool HasCameraPermissions { get; private set; } = false;
    public static bool HasSpatialPermissions { get; private set; } = false;

    // Events for permission state changes
    public static event Action OnAllPermissionsGranted;
    public static event Action OnPermissionsDenied;
    public static event Action<string> OnPermissionGranted;
    public static event Action<string> OnPermissionDenied;

    private bool m_hasRequestedPermissions = false;
    private bool m_isCheckingPermissions = false;

    private void Start()
    {
        if (requestPermissionsOnStart)
        {
            _ = StartCoroutine(CheckAndRequestPermissions());
        }
    }

    /// <summary>
    /// Check current permission status and request if needed
    /// </summary>
    public IEnumerator CheckAndRequestPermissions()
    {
        if (m_isCheckingPermissions)
            yield break;

        m_isCheckingPermissions = true;

        // Wait a frame to ensure everything is initialized
        yield return null;

        // Check current permission state
        var hasCamera = CheckCameraPermissions();
        var hasSpatial = CheckSpatialPermissions();

        HasCameraPermissions = hasCamera;
        HasSpatialPermissions = hasSpatial;
        HasAllPermissions = hasCamera && hasSpatial;

        if (HasAllPermissions)
        {
            // Fix WebCamTextureManager permission state
            FixWebCamTextureManagerPermissionState();

            OnAllPermissionsGranted?.Invoke();
        }
        else
        {
            if (!m_hasRequestedPermissions)
            {
                yield return StartCoroutine(RequestMissingPermissions());
            }
        }

        m_isCheckingPermissions = false;
    }

    /// <summary>
    /// Request permissions that are missing
    /// </summary>
    private IEnumerator RequestMissingPermissions()
    {
#if UNITY_ANDROID
        m_hasRequestedPermissions = true;

        var missingPermissions = GetMissingPermissions();
        if (missingPermissions.Length == 0)
        {
            yield break;
        }

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += OnPermissionGrantedCallback;
        callbacks.PermissionDenied += OnPermissionDeniedCallback;

        // Request all missing permissions at once
        Permission.RequestUserPermissions(missingPermissions, callbacks);

        // Wait for permission response
        var timeout = 10f;
        var elapsed = 0f;

        while (elapsed < timeout && !HasAllPermissions)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        // Final check
        yield return StartCoroutine(CheckAndRequestPermissions());
#else
        HasAllPermissions = true;
        OnAllPermissionsGranted?.Invoke();
        yield break;
#endif
    }

#if UNITY_ANDROID
    /// <summary>
    /// Callback for when a permission is granted
    /// </summary>
    private void OnPermissionGrantedCallback(string permission)
    {
        OnPermissionGranted?.Invoke(permission);

        // Update individual permission states
        if (permission is "android.permission.CAMERA" or "horizonos.permission.HEADSET_CAMERA")
        {
            HasCameraPermissions = CheckCameraPermissions();
        }
        else if (permission == "com.oculus.permission.USE_SCENE")
        {
            HasSpatialPermissions = CheckSpatialPermissions();
        }

        // Check if all permissions are now granted
        var wasComplete = HasAllPermissions;
        HasAllPermissions = HasCameraPermissions && HasSpatialPermissions;

        if (HasAllPermissions && !wasComplete)
        {
            // Fix WebCamTextureManager permission state
            FixWebCamTextureManagerPermissionState();

            OnAllPermissionsGranted?.Invoke();
        }
    }

    /// <summary>
    /// Callback for when a permission is denied
    /// </summary>
    private void OnPermissionDeniedCallback(string permission)
    {
        OnPermissionDenied?.Invoke(permission);

        // Check if we can ask again
        if (Permission.ShouldShowRequestPermissionRationale(permission))
        {
            if (m_retryOnDenial)
            {
                _ = StartCoroutine(RetryPermissionAfterDelay());
            }
        }
        else
        {
            OnPermissionsDenied?.Invoke();
        }
    }

    /// <summary>
    /// Retry permission request after a delay
    /// </summary>
    private IEnumerator RetryPermissionAfterDelay()
    {
        yield return new WaitForSeconds(m_retryDelaySeconds);
        m_hasRequestedPermissions = false;
        yield return StartCoroutine(CheckAndRequestPermissions());
    }
#endif

    /// <summary>
    /// Check if camera permissions are granted
    /// </summary>
    public bool CheckCameraPermissions()
    {
#if UNITY_ANDROID
        var hasAndroidCamera = Permission.HasUserAuthorizedPermission("android.permission.CAMERA");
        var hasHeadsetsCamera = Permission.HasUserAuthorizedPermission(
            "horizonos.permission.HEADSET_CAMERA"
        );
        return hasAndroidCamera && hasHeadsetsCamera;
#else
        // In editor, always return true for testing
        return true;
#endif
    }

    /// <summary>
    /// Check if spatial permissions are granted
    /// </summary>
    public bool CheckSpatialPermissions()
    {
#if UNITY_ANDROID
        var hasSpatial = Permission.HasUserAuthorizedPermission("com.oculus.permission.USE_SCENE");
        return hasSpatial;
#else
        // In editor, always return true for testing
        return true;
#endif
    }

    /// <summary>
    /// Get array of missing permissions
    /// </summary>
    private string[] GetMissingPermissions()
    {
#if UNITY_ANDROID
        var missing = new System.Collections.Generic.List<string>();

        foreach (var permission in RequiredPermissions)
        {
            if (!Permission.HasUserAuthorizedPermission(permission))
            {
                missing.Add(permission);
            }
        }

        return missing.ToArray();
#else
        return new string[0];
#endif
    }

    /// <summary>
    /// Force re-check of all permissions (useful for external calls)
    /// </summary>
    public void RefreshPermissionStatus()
    {
        _ = StartCoroutine(CheckAndRequestPermissions());
    }

    /// <summary>
    /// Force update permission states immediately
    /// </summary>
    [ContextMenu("Force Permission State Update")]
    public void ForcePermissionStateUpdate()
    {
        var hasCamera = CheckCameraPermissions();
        var hasSpatial = CheckSpatialPermissions();

        var wasComplete = HasAllPermissions;
        HasCameraPermissions = hasCamera;
        HasSpatialPermissions = hasSpatial;
        HasAllPermissions = hasCamera && hasSpatial;

        if (HasAllPermissions && !wasComplete)
        {
            // Fix WebCamTextureManager permission state
            FixWebCamTextureManagerPermissionState();

            OnAllPermissionsGranted?.Invoke();
        }
    }

    /// <summary>
    /// Fix WebCamTextureManager's internal permission state to match actual permissions
    /// </summary>
    private void FixWebCamTextureManagerPermissionState()
    {
        var webCamManager = FindFirstObjectByType<PassthroughCameraSamples.WebCamTextureManager>();
        if (webCamManager == null)
            return;

        var managerType = typeof(PassthroughCameraSamples.WebCamTextureManager);
        var hasPermissionField = managerType.GetField(
            "m_hasPermission",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        if (hasPermissionField != null)
        {
            var currentState = hasPermissionField.GetValue(webCamManager);
            if (!(bool)currentState)
            {
                hasPermissionField.SetValue(webCamManager, true);

                // Force reinitialize WebCamTextureManager
                webCamManager.enabled = false;
                webCamManager.enabled = true;

                // Manually trigger initialization
                var initMethod = managerType.GetMethod(
                    "InitializeWebCamTexture",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (initMethod != null)
                {
                    _ = StartCoroutine((IEnumerator)initMethod.Invoke(webCamManager, null));
                }
            }
        }
    }

    /// <summary>
    /// Get detailed permission status for debugging
    /// </summary>
    public string GetPermissionStatus()
    {
        var status = "Permission Status:\n";
        status += $"  All Permissions: {HasAllPermissions}\n";
        status += $"  Camera Permissions: {HasCameraPermissions}\n";
        status += $"  Spatial Permissions: {HasSpatialPermissions}\n";

#if UNITY_ANDROID
        status += "  Individual Permissions:\n";
        foreach (var permission in RequiredPermissions)
        {
            var granted = Permission.HasUserAuthorizedPermission(permission);
            status += $"    {permission}: {granted}\n";
        }
#endif
        return status;
    }

    private void OnDestroy()
    {
        // Clean up static event subscriptions
        OnAllPermissionsGranted = null;
        OnPermissionsDenied = null;
        OnPermissionGranted = null;
        OnPermissionDenied = null;
    }
}
