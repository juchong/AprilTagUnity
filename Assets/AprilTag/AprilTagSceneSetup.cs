// Assets/AprilTag/AprilTagSceneSetup.cs
// Complete setup script for AprilTag permission system and detection functionality

using UnityEngine;
using UnityEngine.UI;
using AprilTag; // For tag family enum
using PassthroughCameraSamples;
using Meta.XR.Samples;
using Meta.XR;

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
    [SerializeField] private bool createEnvironmentRaycastManager = true;
    [SerializeField] private bool setupQuestSpecificFeatures = true;

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
    [SerializeField] private Camera referenceCamera;
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;
    [SerializeField] private bool useCenterEyeTransform = true;
    [SerializeField] private float cameraHeightOffset = 0.0f;
    [SerializeField] private bool enableIPDCompensation = true;
    [SerializeField] private bool usePassthroughRaycasting = true;
    [SerializeField] private bool ignoreOcclusion = true;
    [SerializeField] private float positionScaleFactor = 1.0f;
    [SerializeField] private float minDetectionDistance = 0.3f;
    [SerializeField] private float maxDetectionDistance = 20.0f;
    [SerializeField] private bool enableDistanceScaling = true;
    [SerializeField] private bool enableQuestDebugging = true;

    [Header("WebCam Manager Settings")]
    [SerializeField] private PassthroughCameraSamples.PassthroughCameraEye cameraEye = PassthroughCameraSamples.PassthroughCameraEye.Left;
    [SerializeField] private Vector2Int requestedResolution = new Vector2Int(0, 0); // 0,0 = highest resolution

    [Header("Quest-Specific Settings")]
    [SerializeField] private bool enablePassthroughRaycasting = true;
    [SerializeField] private bool autoFindEnvironmentRaycastManager = true;
    [SerializeField] private EnvironmentRaycastManager environmentRaycastManager;
    [SerializeField] private bool createSimpleEnvironmentRaycastManager = true;

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

        // Setup Quest-specific features
        if (setupQuestSpecificFeatures)
        {
            SetupQuestSpecificFeatures();
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
            var referenceCameraField = controllerType.GetField("referenceCamera", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var positionOffsetField = controllerType.GetField("positionOffset", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var rotationOffsetField = controllerType.GetField("rotationOffset", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var useCenterEyeTransformField = controllerType.GetField("useCenterEyeTransform", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var cameraHeightOffsetField = controllerType.GetField("cameraHeightOffset", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enableIPDCompensationField = controllerType.GetField("enableIPDCompensation", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var usePassthroughRaycastingField = controllerType.GetField("usePassthroughRaycasting", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var environmentRaycastManagerField = controllerType.GetField("environmentRaycastManager", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var ignoreOcclusionField = controllerType.GetField("ignoreOcclusion", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var positionScaleFactorField = controllerType.GetField("positionScaleFactor", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var minDetectionDistanceField = controllerType.GetField("minDetectionDistance", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var maxDetectionDistanceField = controllerType.GetField("maxDetectionDistance", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enableDistanceScalingField = controllerType.GetField("enableDistanceScaling", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enableQuestDebuggingField = controllerType.GetField("enableQuestDebugging", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            tagFamilyField?.SetValue(controller, tagFamily);
            tagSizeField?.SetValue(controller, tagSizeMeters);
            decimateField?.SetValue(controller, decimate);
            maxDetectionsField?.SetValue(controller, maxDetectionsPerSecond);
            fovField?.SetValue(controller, horizontalFovDeg);
            scaleVizField?.SetValue(controller, scaleVizToTagSize);
            logDetectionsField?.SetValue(controller, logDetections);
            logDebugField?.SetValue(controller, logDebugInfo);
            referenceCameraField?.SetValue(controller, referenceCamera);
            positionOffsetField?.SetValue(controller, positionOffset);
            rotationOffsetField?.SetValue(controller, rotationOffset);
            useCenterEyeTransformField?.SetValue(controller, useCenterEyeTransform);
            cameraHeightOffsetField?.SetValue(controller, cameraHeightOffset);
            enableIPDCompensationField?.SetValue(controller, enableIPDCompensation);
            usePassthroughRaycastingField?.SetValue(controller, usePassthroughRaycasting);
            environmentRaycastManagerField?.SetValue(controller, environmentRaycastManager);
            ignoreOcclusionField?.SetValue(controller, ignoreOcclusion);
            positionScaleFactorField?.SetValue(controller, positionScaleFactor);
            minDetectionDistanceField?.SetValue(controller, minDetectionDistance);
            maxDetectionDistanceField?.SetValue(controller, maxDetectionDistance);
            enableDistanceScalingField?.SetValue(controller, enableDistanceScaling);
            enableQuestDebuggingField?.SetValue(controller, enableQuestDebugging);

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
        // Create a wireframe cube prefab for tag visualization
        var prefab = new GameObject("SimpleTagVizPrefab");
        
        
        // Start with unit scale - will be scaled by tagSizeMeters in positioning logic
        prefab.transform.localScale = Vector3.one;
        
        // Create the wireframe cube using LineRenderer
        var wireframe = CreateWireframeCube(prefab.transform);
        
        // Remove any colliders (not needed for visualization)
        var colliders = prefab.GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            if (col != null) DestroyImmediate(col);
        }

        Debug.Log("[AprilTagSceneSetup] Created wireframe tag visualization prefab");
        return prefab;
    }
    
    private GameObject CreateWireframeCube(Transform parent)
    {
        // Create the main wireframe cube
        var wireframeGO = new GameObject("WireframeCube");
        wireframeGO.transform.SetParent(parent);
        wireframeGO.transform.localPosition = Vector3.zero;
        wireframeGO.transform.localScale = Vector3.one;
        wireframeGO.transform.localRotation = Quaternion.Euler(0f, 0f, 0f); // Rotated 90 degrees around X axis
        
        // Define cube vertices (8 corners) - positions after -90 degree X rotation
        // After rotating -90 degrees around X: Y becomes Z, Z becomes -Y
        var vertices = new Vector3[]
        {
            // Bottom face (red - touching the tag) - now at Z = -0.5
            new Vector3(-0.5f, -0.5f, -0.5f), // 0
            new Vector3(0.5f, -0.5f, -0.5f),  // 1
            new Vector3(0.5f, 0.5f, -0.5f),   // 2
            new Vector3(-0.5f, 0.5f, -0.5f),  // 3
            
            // Top face (green - above the tag) - now at Z = 0.5
            new Vector3(-0.5f, -0.5f, 0.5f),  // 4
            new Vector3(0.5f, -0.5f, 0.5f),   // 5
            new Vector3(0.5f, 0.5f, 0.5f),    // 6
            new Vector3(-0.5f, 0.5f, 0.5f)    // 7
        };
        
        // Create LineRenderer for green wireframe (top face and vertical edges)
        var greenLineRenderer = wireframeGO.AddComponent<LineRenderer>();
        greenLineRenderer.material = CreateWireframeMaterial(Color.green);
        greenLineRenderer.startWidth = 0.002f;
        greenLineRenderer.endWidth = 0.002f;
        greenLineRenderer.useWorldSpace = false;
        greenLineRenderer.loop = false;
        
        // Green lines: front face + vertical edges
        var greenLineIndices = new int[]
        {
            // Front face (green) - 4 lines  
            0, 1, 1, 2, 2, 3, 3, 0,
            // Vertical edges (green) - 4 lines
            0, 4, 1, 5, 2, 6, 3, 7
        };
        
        // Create green line positions array
        var greenLinePositions = new Vector3[greenLineIndices.Length];
        for (int i = 0; i < greenLineIndices.Length; i++)
        {
            greenLinePositions[i] = vertices[greenLineIndices[i]];
        }
        
        greenLineRenderer.positionCount = greenLinePositions.Length;
        greenLineRenderer.SetPositions(greenLinePositions);
        
        // Create separate GameObject for red bottom face
        var bottomGO = new GameObject("BottomFace");
        bottomGO.transform.SetParent(wireframeGO.transform);
        bottomGO.transform.localPosition = Vector3.zero;
        bottomGO.transform.localScale = Vector3.one;
        
        var bottomLineRenderer = bottomGO.AddComponent<LineRenderer>();
        bottomLineRenderer.material = CreateWireframeMaterial(Color.red);
        bottomLineRenderer.startWidth = 0.003f; // Slightly thicker for bottom
        bottomLineRenderer.endWidth = 0.003f;
        bottomLineRenderer.useWorldSpace = false;
        bottomLineRenderer.loop = false;
        
        // Back face only (red) - now the face at Z = 0.5
        var bottomLineIndices = new int[] { 4, 5, 5, 6, 6, 7, 7, 4 };
        var bottomLinePositions = new Vector3[bottomLineIndices.Length];
        for (int i = 0; i < bottomLineIndices.Length; i++)
        {
            bottomLinePositions[i] = vertices[bottomLineIndices[i]];
        }
        
        bottomLineRenderer.positionCount = bottomLinePositions.Length;
        bottomLineRenderer.SetPositions(bottomLinePositions);
        
        // Create a red dot to mark one of the corners of the AprilTag
        var dotGO = new GameObject("CornerDot");
        dotGO.transform.SetParent(wireframeGO.transform);
        dotGO.transform.localPosition = new Vector3(-0.5f, -0.5f, 0.5f); // Back face corner
        dotGO.transform.localScale = Vector3.one * 0.1f; // Double size for better visibility
        
        // Create a sphere for the dot
        var dotMesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dotMesh.transform.SetParent(dotGO.transform);
        dotMesh.transform.localPosition = Vector3.zero;
        dotMesh.transform.localScale = Vector3.one;
        
        // Make it red and configure for no occlusion
        var dotRenderer = dotMesh.GetComponent<Renderer>();
        if (dotRenderer != null)
        {
            var dotMaterial = CreateWireframeMaterial(Color.red);
            dotRenderer.material = dotMaterial;
        }
        
        // Remove the collider
        var dotCollider = dotMesh.GetComponent<Collider>();
        if (dotCollider != null)
        {
            DestroyImmediate(dotCollider);
        }
        
        return wireframeGO;
    }
    
    private Material CreateWireframeMaterial(Color color)
    {
        // Create a simple unlit material for wireframe
        var shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            // Fallback to default shader if Unlit/Color is not found
            shader = Shader.Find("Legacy Shaders/Diffuse");
        }
        
        if (shader == null)
        {
            // Final fallback to default material
            var material = new Material(Shader.Find("Standard"));
            material.color = color;
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0f);
            return material;
        }
        
        var wireframeMaterial = new Material(shader);
        wireframeMaterial.color = color;
        
        // Configure to ignore occlusion
        wireframeMaterial.renderQueue = 2000; // High but valid render queue
        wireframeMaterial.SetInt("_ZWrite", 0); // Don't write to depth buffer
        wireframeMaterial.SetInt("_ZTest", 0); // Always pass depth test
        
        return wireframeMaterial;
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
        var envRaycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
        
        Debug.Log($"Permissions Manager: {(manager != null ? "✓ Found" : "✗ Missing")}");
        Debug.Log($"AprilTag Controller: {(controller != null ? "✓ Found" : "✗ Missing")}");
        Debug.Log($"Permission UI: {(ui != null ? "✓ Found" : "✗ Missing")}");
        Debug.Log($"WebCam Manager: {(webCamManager != null ? "✓ Found" : "✗ Missing")}");
        Debug.Log($"Environment Raycast Manager: {(envRaycastManager != null ? "✓ Found" : "✗ Missing")}");
        
        // Check if controller is connected to WebCam Manager
        if (controller != null && webCamManager != null)
        {
            var controllerType = typeof(AprilTagController);
            var webCamManagerField = controllerType.GetField("webCamManager", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var connectedManager = webCamManagerField?.GetValue(controller);
            Debug.Log($"Controller-WebCam Connection: {(connectedManager != null ? "✓ Connected" : "✗ Not Connected")}");
        }
        
        // Check Quest-specific settings
        if (controller != null)
        {
            var controllerType = typeof(AprilTagController);
            var usePassthroughRaycastingField = controllerType.GetField("usePassthroughRaycasting", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var environmentRaycastManagerField = controllerType.GetField("environmentRaycastManager", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            var usePassthroughRaycasting = (bool)usePassthroughRaycastingField?.GetValue(controller);
            var connectedEnvRaycastManager = environmentRaycastManagerField?.GetValue(controller);
            
            Debug.Log($"Passthrough Raycasting: {(usePassthroughRaycasting ? "✓ Enabled" : "✗ Disabled")}");
            Debug.Log($"Controller-EnvironmentRaycast Connection: {(connectedEnvRaycastManager != null ? "✓ Connected" : "✗ Not Connected")}");
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

    [ContextMenu("Setup Quest-Specific Features")]
    public void SetupQuestSpecificFeatures()
    {
        Debug.Log("[AprilTagSceneSetup] Setting up Quest-specific features...");

        // Setup Environment Raycast Manager
        if (createEnvironmentRaycastManager)
        {
            SetupEnvironmentRaycastManager();
        }

        // Auto-find Environment Raycast Manager if needed
        if (autoFindEnvironmentRaycastManager && environmentRaycastManager == null)
        {
            environmentRaycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
            if (environmentRaycastManager != null)
            {
                Debug.Log($"[AprilTagSceneSetup] Auto-found EnvironmentRaycastManager: {environmentRaycastManager.name}");
            }
        }

        // Update AprilTag Controller with Quest settings
        UpdateAprilTagControllerWithQuestSettings();

        Debug.Log("[AprilTagSceneSetup] Quest-specific features setup complete!");
    }

    private void SetupEnvironmentRaycastManager()
    {
        var existingManager = FindFirstObjectByType<EnvironmentRaycastManager>();
        if (existingManager == null)
        {
            if (createSimpleEnvironmentRaycastManager)
            {
                // Create a simple Environment Raycast Manager
                var managerGO = new GameObject("EnvironmentRaycastManager");
                environmentRaycastManager = managerGO.AddComponent<EnvironmentRaycastManager>();
                
                Debug.Log("[AprilTagSceneSetup] Created simple EnvironmentRaycastManager");
            }
            else
            {
                Debug.LogWarning("[AprilTagSceneSetup] No EnvironmentRaycastManager found and createSimpleEnvironmentRaycastManager is disabled. Please add one manually or enable createSimpleEnvironmentRaycastManager.");
            }
        }
        else
        {
            environmentRaycastManager = existingManager;
            Debug.Log($"[AprilTagSceneSetup] Found existing EnvironmentRaycastManager: {existingManager.name}");
        }
    }

    private void UpdateAprilTagControllerWithQuestSettings()
    {
        var controller = FindFirstObjectByType<AprilTagController>();
        if (controller == null)
        {
            Debug.LogWarning("[AprilTagSceneSetup] No AprilTagController found to update with Quest settings");
            return;
        }

        // Update Quest-specific settings using reflection
        var controllerType = typeof(AprilTagController);
        var usePassthroughRaycastingField = controllerType.GetField("usePassthroughRaycasting", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var environmentRaycastManagerField = controllerType.GetField("environmentRaycastManager", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        usePassthroughRaycastingField?.SetValue(controller, enablePassthroughRaycasting);
        environmentRaycastManagerField?.SetValue(controller, environmentRaycastManager);

        Debug.Log($"[AprilTagSceneSetup] Updated AprilTagController with Quest settings - Passthrough Raycasting: {enablePassthroughRaycasting}, EnvironmentRaycastManager: {(environmentRaycastManager != null ? environmentRaycastManager.name : "None")}");
    }

    [ContextMenu("Setup Environment Raycast Manager Only")]
    public void SetupEnvironmentRaycastManagerOnly()
    {
        Debug.Log("[AprilTagSceneSetup] Setting up Environment Raycast Manager...");
        SetupEnvironmentRaycastManager();
        Debug.Log("[AprilTagSceneSetup] Environment Raycast Manager setup complete!");
    }

    [ContextMenu("Test Quest Setup")]
    public void TestQuestSetup()
    {
        Debug.Log("=== Quest Setup Test ===");
        
        var controller = FindFirstObjectByType<AprilTagController>();
        var webCamManager = FindFirstObjectByType<WebCamTextureManager>();
        var envRaycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
        
        Debug.Log($"AprilTag Controller: {(controller != null ? "✓ Found" : "✗ Missing")}");
        Debug.Log($"WebCam Manager: {(webCamManager != null ? "✓ Found" : "✗ Missing")}");
        Debug.Log($"Environment Raycast Manager: {(envRaycastManager != null ? "✓ Found" : "✗ Missing")}");
        
        if (controller != null)
        {
            var controllerType = typeof(AprilTagController);
            var usePassthroughRaycastingField = controllerType.GetField("usePassthroughRaycasting", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var usePassthroughRaycasting = (bool)usePassthroughRaycastingField?.GetValue(controller);
            Debug.Log($"Passthrough Raycasting Enabled: {usePassthroughRaycasting}");
        }
        
        Debug.Log("=== End Test ===");
    }

    [ContextMenu("Configure Optimal Quest Settings")]
    public void ConfigureOptimalQuestSettings()
    {
        Debug.Log("[AprilTagSceneSetup] Configuring optimal Quest settings...");
        
        // Set optimal Quest-specific settings
        useCenterEyeTransform = true;
        enablePassthroughRaycasting = true;
        autoFindEnvironmentRaycastManager = true;
        createSimpleEnvironmentRaycastManager = true;
        enableIPDCompensation = true;
        usePassthroughRaycasting = true;
        ignoreOcclusion = true;
        
        // Set optimal detection settings for Quest
        decimate = 2; // Good balance of performance and accuracy
        maxDetectionsPerSecond = 15f; // Reasonable for Quest performance
        horizontalFovDeg = 78f; // Quest's typical FOV
        
        // Set optimal WebCam settings
        cameraEye = PassthroughCameraSamples.PassthroughCameraEye.Left; // Default to left eye
        requestedResolution = new Vector2Int(0, 0); // Use highest available resolution
        
        Debug.Log("[AprilTagSceneSetup] Optimal Quest settings configured!");
        Debug.Log("  - Center Eye Transform: Enabled");
        Debug.Log("  - Passthrough Raycasting: Enabled");
        Debug.Log("  - Auto-find Environment Raycast Manager: Enabled");
        Debug.Log("  - IPD Compensation: Enabled");
        Debug.Log("  - Ignore Occlusion: Enabled");
        Debug.Log("  - Decimation: 2");
        Debug.Log("  - Max Detections/sec: 15");
        Debug.Log("  - FOV: 78 degrees");
        Debug.Log("  - Camera Eye: Left");
        Debug.Log("  - Resolution: Highest available");
    }

    [ContextMenu("Quick Quest Setup")]
    public void QuickQuestSetup()
    {
        Debug.Log("[AprilTagSceneSetup] Starting quick Quest setup...");
        
        // Configure optimal settings first
        ConfigureOptimalQuestSettings();
        
        // Run the complete setup
        SetupCompleteAprilTagSystem();
        
        Debug.Log("[AprilTagSceneSetup] Quick Quest setup complete! Your AprilTag system is ready for Quest.");
    }

}
