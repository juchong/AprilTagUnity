// Assets/AprilTag/AprilTagPermissionUI.cs
// UI component for displaying permission status and requesting permissions

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AprilTagPermissionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject permissionPanel;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI detailText;
    [SerializeField] private Button requestPermissionsButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button closeButton;

    [Header("Settings")]
    [SerializeField] private bool showPanelOnStart = true;
    [SerializeField] private bool autoHideOnGranted = true;
    [SerializeField] private float autoHideDelay = 3f;

    private AprilTagPermissionsManager permissionsManager;

    void Start()
    {
        // Get or create permissions manager
        permissionsManager = FindFirstObjectByType<AprilTagPermissionsManager>();
        if (permissionsManager == null)
        {
            Debug.LogWarning("[AprilTagPermissionUI] No AprilTagPermissionsManager found in scene");
            return;
        }

        // Setup button listeners
        if (requestPermissionsButton != null)
            requestPermissionsButton.onClick.AddListener(RequestPermissions);
        
        if (retryButton != null)
            retryButton.onClick.AddListener(RequestPermissions);
        
        if (closeButton != null)
            closeButton.onClick.AddListener(HidePanel);

        // Subscribe to permission events
        AprilTagPermissionsManager.OnAllPermissionsGranted += OnAllPermissionsGranted;
        AprilTagPermissionsManager.OnPermissionsDenied += OnPermissionsDenied;
        AprilTagPermissionsManager.OnPermissionGranted += OnPermissionGranted;
        AprilTagPermissionsManager.OnPermissionDenied += OnPermissionDenied;

        // Initial update
        UpdateUI();

        // Show panel if needed
        if (showPanelOnStart && !AprilTagPermissionsManager.HasAllPermissions)
        {
            ShowPanel();
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        AprilTagPermissionsManager.OnAllPermissionsGranted -= OnAllPermissionsGranted;
        AprilTagPermissionsManager.OnPermissionsDenied -= OnPermissionsDenied;
        AprilTagPermissionsManager.OnPermissionGranted -= OnPermissionGranted;
        AprilTagPermissionsManager.OnPermissionDenied -= OnPermissionDenied;
    }

    private void OnAllPermissionsGranted()
    {
        UpdateUI();
        
        if (autoHideOnGranted)
        {
            Invoke(nameof(HidePanel), autoHideDelay);
        }
    }

    private void OnPermissionsDenied()
    {
        UpdateUI();
        ShowPanel();
    }

    private void OnPermissionGranted(string permission)
    {
        UpdateUI();
    }

    private void OnPermissionDenied(string permission)
    {
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (permissionsManager == null) return;

        bool hasAllPermissions = AprilTagPermissionsManager.HasAllPermissions;
        bool hasCameraPermissions = AprilTagPermissionsManager.HasCameraPermissions;
        bool hasSpatialPermissions = AprilTagPermissionsManager.HasSpatialPermissions;

        // Update status text
        if (statusText != null)
        {
            if (hasAllPermissions)
            {
                statusText.text = "✓ All Permissions Granted";
                statusText.color = Color.green;
            }
            else if (hasCameraPermissions && !hasSpatialPermissions)
            {
                statusText.text = "⚠ Missing Spatial Data Permission";
                statusText.color = Color.yellow;
            }
            else if (!hasCameraPermissions && hasSpatialPermissions)
            {
                statusText.text = "⚠ Missing Camera Permissions";
                statusText.color = Color.yellow;
            }
            else
            {
                statusText.text = "✗ Missing Required Permissions";
                statusText.color = Color.red;
            }
        }

        // Update detail text
        if (detailText != null)
        {
            string details = "Permission Status:\n";
            details += $"Camera Access: {(hasCameraPermissions ? "✓" : "✗")}\n";
            details += $"Spatial Data: {(hasSpatialPermissions ? "✓" : "✗")}\n\n";
            
            if (!hasAllPermissions)
            {
                details += "AprilTag detection requires:\n";
                if (!hasCameraPermissions)
                {
                    details += "• Camera access for passthrough video\n";
                }
                if (!hasSpatialPermissions)
                {
                    details += "• Spatial data access for AR features\n";
                }
                details += "\nTap 'Request Permissions' to enable these features.";
            }
            else
            {
                details += "All required permissions are granted.\nAprilTag detection is ready!";
            }
            
            detailText.text = details;
        }

        // Update button visibility
        if (requestPermissionsButton != null)
        {
            requestPermissionsButton.gameObject.SetActive(!hasAllPermissions);
        }
        
        if (retryButton != null)
        {
            retryButton.gameObject.SetActive(!hasAllPermissions);
        }

        // Show/hide panel based on permission status
        if (hasAllPermissions && autoHideOnGranted)
        {
            // Panel will be hidden automatically via Invoke
        }
        else if (!hasAllPermissions)
        {
            ShowPanel();
        }
    }

    public void RequestPermissions()
    {
        if (permissionsManager != null)
        {
            permissionsManager.RefreshPermissionStatus();
        }
    }

    public void ShowPanel()
    {
        if (permissionPanel != null)
        {
            permissionPanel.SetActive(true);
        }
    }

    public void HidePanel()
    {
        if (permissionPanel != null)
        {
            permissionPanel.SetActive(false);
        }
    }

    public void TogglePanel()
    {
        if (permissionPanel != null)
        {
            permissionPanel.SetActive(!permissionPanel.activeInHierarchy);
        }
    }

    // Public method to get current permission status (useful for other scripts)
    public string GetPermissionStatusString()
    {
        if (permissionsManager != null)
        {
            return permissionsManager.GetPermissionStatus();
        }
        return "Permission manager not available";
    }
}
