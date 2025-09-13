// Assets/AprilTag/AprilTagSceneSetup.cs
// Complete setup script for AprilTag permission system and detection functionality

using UnityEngine;
using UnityEngine.UI;
using AprilTag; // For tag family enum

[System.Serializable]
public class AprilTagSceneSetup : MonoBehaviour
{
    [Header("Auto Setup")]
    [SerializeField] private bool setupOnAwake = true;
    [SerializeField] private bool createPermissionsManager = true;
    [SerializeField] private bool createPermissionUI = true;
    [SerializeField] private bool createAprilTagController = true;
    [SerializeField] private bool createWebCamManager = true;
    [SerializeField] private bool connectToWebCamManager = true;

    [Header("Permission Manager Settings")]
    [SerializeField] private bool requestPermissionsOnStart = true;
    [SerializeField] private bool retryOnDenial = true;
    [SerializeField] private float retryDelaySeconds = 2f;

    [Header("Permission UI Settings")]
    [SerializeField] private bool showPanelOnStart = true;
    [SerializeField] private bool autoHideOnGranted = true;
    [SerializeField] private float autoHideDelay = 3f;

    [Header("AprilTag Detection Settings")]
    [SerializeField] private AprilTag.Interop.TagFamily tagFamily = AprilTag.Interop.TagFamily.Tag36h11;
    [SerializeField] private float tagSizeMeters = 0.08f;
    [Range(1, 8)][SerializeField] private int decimate = 2;
    [SerializeField] private float maxDetectionsPerSecond = 15f;
    [SerializeField] private float horizontalFovDeg = 78f;
    [SerializeField] private bool scaleVizToTagSize = true;
    [SerializeField] private bool logDetections = true;
    [SerializeField] private bool logDebugInfo = true;

    [Header("Tag Visualization")]
    [SerializeField] private GameObject tagVizPrefab;
    [SerializeField] private bool createSimpleTagViz = true;

    [Header("WebCam Manager Settings")]
    [SerializeField] private PassthroughCameraSamples.PassthroughCameraEye cameraEye = PassthroughCameraSamples.PassthroughCameraEye.Left;
    [SerializeField] private Vector2Int requestedResolution = new Vector2Int(0, 0); // 0,0 = highest resolution

    void Awake()
    {
        if (setupOnAwake)
        {
            SetupCompleteAprilTagSystem();
        }
    }

    [ContextMenu("Setup Complete AprilTag System")]
    public void SetupCompleteAprilTagSystem()
    {
        Debug.Log("[AprilTagSceneSetup] Setting up complete AprilTag system...");

        // Create or configure permissions manager
        if (createPermissionsManager)
        {
            SetupPermissionsManager();
        }

        // Create or configure permission UI
        if (createPermissionUI)
        {
            SetupPermissionUI();
        }

        // Create or configure WebCam Manager
        if (createWebCamManager)
        {
            SetupWebCamManager();
        }

        // Create or configure AprilTag controller
        if (createAprilTagController)
        {
            SetupAprilTagController();
        }

        // Connect to WebCam Manager if available
        if (connectToWebCamManager)
        {
            ConnectToWebCamManager();
        }


        Debug.Log("[AprilTagSceneSetup] Complete AprilTag system setup finished!");
    }

    [ContextMenu("Setup AprilTag Permissions Only")]
    public void SetupAprilTagPermissions()
    {
        Debug.Log("[AprilTagSceneSetup] Setting up AprilTag permission system...");

        // Create or configure permissions manager
        if (createPermissionsManager)
        {
            SetupPermissionsManager();
        }

        // Create or configure permission UI
        if (createPermissionUI)
        {
            SetupPermissionUI();
        }

        Debug.Log("[AprilTagSceneSetup] AprilTag permission system setup complete!");
    }

    private void SetupAprilTagController()
    {
        var existingController = FindFirstObjectByType<AprilTagController>();
        if (existingController == null)
        {
            // Create new AprilTag controller
            var controllerGO = new GameObject("AprilTagController");
            var controller = controllerGO.AddComponent<AprilTagController>();

            // Configure settings via reflection
            var controllerType = typeof(AprilTagController);
            
            // Set detection settings
            var tagFamilyField = controllerType.GetField("tagFamily", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var tagSizeField = controllerType.GetField("tagSizeMeters", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var decimateField = controllerType.GetField("decimate", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var maxDetectionsField = controllerType.GetField("maxDetectionsPerSecond", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fovField = controllerType.GetField("horizontalFovDeg", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var scaleVizField = controllerType.GetField("scaleVizToTagSize", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var logDetectionsField = controllerType.GetField("logDetections", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var logDebugField = controllerType.GetField("logDebugInfo", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var vizPrefabField = controllerType.GetField("tagVizPrefab", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            tagFamilyField?.SetValue(controller, tagFamily);
            tagSizeField?.SetValue(controller, tagSizeMeters);
            decimateField?.SetValue(controller, decimate);
            maxDetectionsField?.SetValue(controller, maxDetectionsPerSecond);
            fovField?.SetValue(controller, horizontalFovDeg);
            scaleVizField?.SetValue(controller, scaleVizToTagSize);
            logDetectionsField?.SetValue(controller, logDetections);
            logDebugField?.SetValue(controller, logDebugInfo);

            // Set up tag visualization prefab
            GameObject vizPrefab = tagVizPrefab;
            if (vizPrefab == null && createSimpleTagViz)
            {
                vizPrefab = CreateSimpleTagVisualizationPrefab();
            }
            vizPrefabField?.SetValue(controller, vizPrefab);

            // Position the controller appropriately
            controllerGO.transform.position = Vector3.zero;

            Debug.Log("[AprilTagSceneSetup] Created AprilTagController with configured settings");
        }
        else
        {
            Debug.Log("[AprilTagSceneSetup] AprilTagController already exists in scene");
        }
    }

    private GameObject CreateSimpleTagVisualizationPrefab()
    {
        // Create a simple cube prefab for tag visualization
        var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prefab.name = "SimpleTagVizPrefab";
        
        // Scale it to be small and flat
        prefab.transform.localScale = new Vector3(0.08f, 0.001f, 0.08f);
        
        // Make it a bright color for visibility
        var renderer = prefab.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.green;
            renderer.material.SetFloat("_Metallic", 0f);
            renderer.material.SetFloat("_Smoothness", 0.5f);
        }

        // Remove the collider (not needed for visualization)
        var collider = prefab.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyImmediate(collider);
        }

        // Add a simple outline effect with a child object
        var outline = GameObject.CreatePrimitive(PrimitiveType.Cube);
        outline.name = "Outline";
        outline.transform.SetParent(prefab.transform);
        outline.transform.localScale = new Vector3(1.1f, 1f, 1.1f);
        outline.transform.localPosition = Vector3.zero;
        
        var outlineRenderer = outline.GetComponent<Renderer>();
        if (outlineRenderer != null)
        {
            outlineRenderer.material.color = Color.white;
            outlineRenderer.material.SetFloat("_Metallic", 0f);
            outlineRenderer.material.SetFloat("_Smoothness", 0.8f);
        }

        // Remove outline collider
        var outlineCollider = outline.GetComponent<Collider>();
        if (outlineCollider != null)
        {
            DestroyImmediate(outlineCollider);
        }

        Debug.Log("[AprilTagSceneSetup] Created simple tag visualization prefab");
        return prefab;
    }

    private void SetupWebCamManager()
    {
        var existingWebCamManager = FindFirstObjectByType<PassthroughCameraSamples.WebCamTextureManager>();
        if (existingWebCamManager == null)
        {
            // Create new WebCam Manager
            var webCamManagerGO = new GameObject("WebCamTextureManager");
            var webCamManager = webCamManagerGO.AddComponent<PassthroughCameraSamples.WebCamTextureManager>();

            // Configure settings via reflection
            var managerType = typeof(PassthroughCameraSamples.WebCamTextureManager);
            
            // Set camera eye
            var eyeField = managerType.GetField("Eye", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            eyeField?.SetValue(webCamManager, cameraEye);

            // Set requested resolution
            var resolutionField = managerType.GetField("RequestedResolution", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            resolutionField?.SetValue(webCamManager, requestedResolution);

            // Set up camera permissions reference
            var permissionsField = managerType.GetField("CameraPermissions", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Try to find existing permissions manager or create a reference
            var permissionsManager = FindFirstObjectByType<AprilTagPermissionsManager>();
            if (permissionsManager != null)
            {
                // Create a simple permissions component for the WebCam Manager
                var simplePermissions = webCamManagerGO.AddComponent<PassthroughCameraSamples.PassthroughCameraPermissions>();
                permissionsField?.SetValue(webCamManager, simplePermissions);
            }

            // Position the manager appropriately
            webCamManagerGO.transform.position = Vector3.zero;

            Debug.Log($"[AprilTagSceneSetup] Created WebCamTextureManager with eye: {cameraEye}, resolution: {requestedResolution}");
        }
        else
        {
            Debug.Log("[AprilTagSceneSetup] WebCamTextureManager already exists in scene");
        }
    }

    private void ConnectToWebCamManager()
    {
        // Find WebCamTextureManager in the scene
        var webCamTextureManager = FindFirstObjectByType<PassthroughCameraSamples.WebCamTextureManager>();
        if (webCamTextureManager == null)
        {
            Debug.LogWarning("[AprilTagSceneSetup] No WebCamTextureManager found in scene. AprilTag detection may not work without passthrough camera feed.");
            return;
        }

        // Find AprilTagController in the scene
        var aprilTagController = FindFirstObjectByType<AprilTagController>();
        if (aprilTagController == null)
        {
            Debug.LogWarning("[AprilTagSceneSetup] No AprilTagController found in scene. Cannot connect to WebCamTextureManager.");
            return;
        }

        // Connect the AprilTagController to the WebCamTextureManager
        var controllerType = typeof(AprilTagController);
        var webCamManagerField = controllerType.GetField("webCamManager", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (webCamManagerField != null)
        {
            webCamManagerField.SetValue(aprilTagController, webCamTextureManager);
            Debug.Log($"[AprilTagSceneSetup] Successfully connected AprilTagController to WebCamTextureManager: {webCamTextureManager.name}");
        }
        else
        {
            Debug.LogError("[AprilTagSceneSetup] Could not access webCamManager field in AprilTagController");
        }
    }

    private void SetupPermissionsManager()
    {
        var existingManager = FindFirstObjectByType<AprilTagPermissionsManager>();
        if (existingManager == null)
        {
            // Create new permissions manager
            var managerGO = new GameObject("AprilTagPermissionsManager");
            var manager = managerGO.AddComponent<AprilTagPermissionsManager>();
            
            // Configure settings via reflection (since fields are private)
            var managerType = typeof(AprilTagPermissionsManager);
            var requestOnStartField = managerType.GetField("requestPermissionsOnStart", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var retryOnDenialField = managerType.GetField("retryOnDenial", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var retryDelayField = managerType.GetField("retryDelaySeconds", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            requestOnStartField?.SetValue(manager, requestPermissionsOnStart);
            retryOnDenialField?.SetValue(manager, retryOnDenial);
            retryDelayField?.SetValue(manager, retryDelaySeconds);

            Debug.Log("[AprilTagSceneSetup] Created AprilTagPermissionsManager");
        }
        else
        {
            Debug.Log("[AprilTagSceneSetup] AprilTagPermissionsManager already exists in scene");
        }
    }

    private void SetupPermissionUI()
    {
        var existingUI = FindFirstObjectByType<AprilTagPermissionUI>();
        if (existingUI == null)
        {
            // Create a simple UI canvas if none exists
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("Canvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
                
                Debug.Log("[AprilTagSceneSetup] Created Canvas for permission UI");
            }

            // Create permission UI panel
            var panelGO = new GameObject("AprilTagPermissionPanel");
            panelGO.transform.SetParent(canvas.transform, false);
            
            // Add UI components
            var image = panelGO.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0, 0, 0, 0.8f);
            
            var rectTransform = panelGO.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(400, 300);
            rectTransform.anchoredPosition = Vector2.zero;

            // Add permission UI component
            var permissionUI = panelGO.AddComponent<AprilTagPermissionUI>();
            
            // Configure settings via reflection
            var uiType = typeof(AprilTagPermissionUI);
            var showPanelField = uiType.GetField("showPanelOnStart", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var autoHideField = uiType.GetField("autoHideOnGranted", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var hideDelayField = uiType.GetField("autoHideDelay", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            showPanelField?.SetValue(permissionUI, showPanelOnStart);
            autoHideField?.SetValue(permissionUI, autoHideOnGranted);
            hideDelayField?.SetValue(permissionUI, autoHideDelay);

            // Initially hide the panel (it will show automatically if permissions are needed)
            panelGO.SetActive(false);

            Debug.Log("[AprilTagSceneSetup] Created AprilTagPermissionUI");
        }
        else
        {
            Debug.Log("[AprilTagSceneSetup] AprilTagPermissionUI already exists in scene");
        }
    }

    [ContextMenu("Check Permission Status")]
    public void CheckPermissionStatus()
    {
        var manager = FindFirstObjectByType<AprilTagPermissionsManager>();
        if (manager != null)
        {
            Debug.Log(manager.GetPermissionStatus());
        }
        else
        {
            Debug.LogWarning("[AprilTagSceneSetup] No AprilTagPermissionsManager found in scene");
        }
    }

    [ContextMenu("Force Permission Request")]
    public void ForcePermissionRequest()
    {
        var manager = FindFirstObjectByType<AprilTagPermissionsManager>();
        if (manager != null)
        {
            manager.RefreshPermissionStatus();
        }
        else
        {
            Debug.LogWarning("[AprilTagSceneSetup] No AprilTagPermissionsManager found in scene");
        }
    }

    [ContextMenu("Setup AprilTag Controller Only")]
    public void SetupAprilTagControllerOnly()
    {
        Debug.Log("[AprilTagSceneSetup] Setting up AprilTag controller only...");
        SetupAprilTagController();
        Debug.Log("[AprilTagSceneSetup] AprilTag controller setup complete!");
    }

    [ContextMenu("Setup WebCam Manager")]
    public void SetupWebCamManagerOnly()
    {
        Debug.Log("[AprilTagSceneSetup] Setting up WebCam Manager...");
        SetupWebCamManager();
        Debug.Log("[AprilTagSceneSetup] WebCam Manager setup complete!");
    }

    [ContextMenu("Connect to WebCam Manager")]
    public void ConnectToWebCamManagerOnly()
    {
        Debug.Log("[AprilTagSceneSetup] Connecting to WebCam Manager...");
        ConnectToWebCamManager();
        Debug.Log("[AprilTagSceneSetup] WebCam Manager connection complete!");
    }

    [ContextMenu("Show System Status")]
    public void ShowSystemStatus()
    {
        Debug.Log("=== AprilTag System Status ===");
        
        var manager = FindFirstObjectByType<AprilTagPermissionsManager>();
        var controller = FindFirstObjectByType<AprilTagController>();
        var ui = FindFirstObjectByType<AprilTagPermissionUI>();
        var webCamManager = FindFirstObjectByType<PassthroughCameraSamples.WebCamTextureManager>();
        
        Debug.Log($"Permissions Manager: {(manager != null ? "✓ Found" : "✗ Missing")}");
        Debug.Log($"AprilTag Controller: {(controller != null ? "✓ Found" : "✗ Missing")}");
        Debug.Log($"Permission UI: {(ui != null ? "✓ Found" : "✗ Missing")}");
        Debug.Log($"WebCam Manager: {(webCamManager != null ? "✓ Found" : "✗ Missing")}");
        
        // Check if controller is connected to WebCam Manager
        if (controller != null && webCamManager != null)
        {
            var controllerType = typeof(AprilTagController);
            var webCamManagerField = controllerType.GetField("webCamManager", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var connectedManager = webCamManagerField?.GetValue(controller);
            Debug.Log($"Controller-WebCam Connection: {(connectedManager != null ? "✓ Connected" : "✗ Not Connected")}");
        }
        
        if (manager != null)
        {
            Debug.Log($"Permission Status: {manager.GetPermissionStatus()}");
        }
        
        Debug.Log("=== End Status ===");
    }

    [ContextMenu("Clean Up Setup Components")]
    public void CleanUpSetupComponents()
    {
        // Find and destroy this setup component
        var setupComponents = FindObjectsByType<AprilTagSceneSetup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var setup in setupComponents)
        {
            if (setup != this)
            {
                Debug.Log($"[AprilTagSceneSetup] Destroying duplicate setup component on {setup.gameObject.name}");
                DestroyImmediate(setup);
            }
        }
        
        Debug.Log("[AprilTagSceneSetup] Setup components cleaned up. You can now safely delete this GameObject.");
    }

    [ContextMenu("Fix WebCamTexture Issues")]
    public void FixWebCamTextureIssues()
    {
        Debug.Log("[AprilTagSceneSetup] Attempting to fix WebCamTexture issues...");
        
        // 1. Ensure we have all components
        SetupCompleteAprilTagSystem();
        
        // 2. Force permission request
        var permissionsManager = FindFirstObjectByType<AprilTagPermissionsManager>();
        if (permissionsManager != null)
        {
            Debug.Log("[AprilTagSceneSetup] Forcing permission refresh...");
            permissionsManager.RefreshPermissionStatus();
        }
        
        // 3. Ensure WebCam Manager is properly connected
        var webCamManager = FindFirstObjectByType<PassthroughCameraSamples.WebCamTextureManager>();
        var controller = FindFirstObjectByType<AprilTagController>();
        
        if (webCamManager != null && controller != null)
        {
            // Force connection
            var controllerType = typeof(AprilTagController);
            var webCamManagerField = controllerType.GetField("webCamManager", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            webCamManagerField?.SetValue(controller, webCamManager);
            Debug.Log($"[AprilTagSceneSetup] Forced connection between AprilTagController and WebCamTextureManager");
        }
        
        Debug.Log("[AprilTagSceneSetup] WebCamTexture fix attempts completed. Check console for results.");
    }

    [ContextMenu("Force WebCamTextureManager Reinitialize")]
    public void ForceWebCamTextureManagerReinitialize()
    {
        Debug.Log("[AprilTagSceneSetup] Forcing WebCamTextureManager reinitialization...");
        
        var webCamManager = FindFirstObjectByType<PassthroughCameraSamples.WebCamTextureManager>();
        if (webCamManager == null)
        {
            Debug.LogError("[AprilTagSceneSetup] No WebCamTextureManager found to reinitialize");
            return;
        }
        
        // Force reinitialization by disabling and re-enabling
        Debug.Log("[AprilTagSceneSetup] Disabling and re-enabling WebCamTextureManager...");
        webCamManager.enabled = false;
        webCamManager.enabled = true;
        
        // Force permission state update
        var permissionsManager = FindFirstObjectByType<AprilTagPermissionsManager>();
        if (permissionsManager != null)
        {
            Debug.Log("[AprilTagSceneSetup] Forcing permission state update...");
            permissionsManager.ForcePermissionStateUpdate();
        }
        
        Debug.Log("[AprilTagSceneSetup] WebCamTextureManager reinitialization completed. Check console for results.");
    }

}
