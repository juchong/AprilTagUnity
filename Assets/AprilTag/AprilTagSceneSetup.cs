// Assets/AprilTag/AprilTagSceneSetup.cs
// Complete setup script for AprilTag permission system and detection functionality

using UnityEngine;
using UnityEngine.UI;
using AprilTag; // For tag family enum
using PassthroughCameraSamples;
using Meta.XR.Samples;
using Meta.XR;
using System.Linq;

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
    [SerializeField] private float tagSizeMeters = 0.165f;
    [Range(1, 8)][SerializeField] private int decimate = 2;
    [SerializeField] private float maxDetectionsPerSecond = 72f;
    [SerializeField] private float horizontalFovDeg = 78f;
    [SerializeField] private bool scaleVizToTagSize = true;

    [Header("Tag Visualization")]
    [SerializeField] private GameObject tagVizPrefab;
    [SerializeField] private bool createSimpleTagViz = true;
    [SerializeField] private Camera referenceCamera;
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;
    [SerializeField] private bool useCenterEyeTransform = true;
    [SerializeField] private float cameraHeightOffset = 0.0f;
    [SerializeField] private bool enableIPDCompensation = true;
    
    [Header("Tuned Configuration (Default Values)")]
    [SerializeField] private Vector3 cornerPositionOffset = new Vector3(0.000f, 0.000f, 0.000f);
    [SerializeField] private bool enableConfigurationTool = true;
    [SerializeField] private bool enableAllDebugLogging = false;
    [SerializeField] private bool usePassthroughRaycasting = true;
    [SerializeField] private bool ignoreOcclusion = true;
    [SerializeField] private float positionScaleFactor = 1.0f;
    [SerializeField] private float minDetectionDistance = 0.3f;
    [SerializeField] private float maxDetectionDistance = 20.0f;
    [SerializeField] private bool enableDistanceScaling = true;
    [SerializeField] private bool enableQuestDebugging = true;

    [Header("User Runtime Offset Configuration")]
    [Tooltip("Enable user-specified runtime offset (overrides default cornerPositionOffset)")]
    [SerializeField] private bool useUserRuntimeOffset = false;
    [Tooltip("User-measured runtime offset values (X, Y, Z in meters)")]
    [SerializeField] private Vector3 userRuntimeOffset = new Vector3(0.000f, 0.000f, 0.000f);
    [Tooltip("Apply user offset immediately when setting up the controller")]
    [SerializeField] private bool applyUserOffsetOnSetup = true;
    [Tooltip("Save user offset to PlayerPrefs for persistence")]
    [SerializeField] private bool saveUserOffsetToPersistence = true;

    [Header("Quest Runtime Cleanup")]
    [Tooltip("Enable automatic cleanup of duplicate permission panels on Quest")]
    [SerializeField] private bool enableRuntimeCleanup = true;


    [Header("WebCam Manager Settings")]
    [SerializeField] private PassthroughCameraSamples.PassthroughCameraEye cameraEye = PassthroughCameraSamples.PassthroughCameraEye.Left;
    [SerializeField] private Vector2Int requestedResolution = new Vector2Int(0, 0); // 0,0 = highest resolution

    [Header("Quest-Specific Settings")]
    [SerializeField] private bool enablePassthroughRaycasting = true;
    [SerializeField] private bool autoFindEnvironmentRaycastManager = true;
    [SerializeField] private EnvironmentRaycastManager environmentRaycastManager;
    [SerializeField] private bool createSimpleEnvironmentRaycastManager = true;
    [SerializeField] private bool setupCenterEyeAnchor = true;
    [SerializeField] private GameObject centerEyeAnchor;

    void Awake()
    {
        // Load user offset from PlayerPrefs if enabled
        if (useUserRuntimeOffset && saveUserOffsetToPersistence)
        {
            LoadUserOffsetFromPlayerPrefs();
        }
        
        if (setupOnAwake)
        {
            SetupCompleteAprilTagSystem();
        }
    }

    void Start()
    {
        // Run cleanup on Quest if enabled
        if (enableRuntimeCleanup)
        {
            CleanUpDuplicatePermissionPanels();
        }
    }

    [ContextMenu("Setup Complete AprilTag System")]
    public void SetupCompleteAprilTagSystem()
    {

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

    }

    [ContextMenu("Setup AprilTag Permissions Only")]
    public void SetupAprilTagPermissions()
    {

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
            var cornerPositionOffsetField = controllerType.GetField("cornerPositionOffset", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enableConfigurationToolField = controllerType.GetField("enableConfigurationTool", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enableAllDebugLoggingField = controllerType.GetField("enableAllDebugLogging", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enableDistanceScalingField = controllerType.GetField("enableDistanceScaling", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enableQuestDebuggingField = controllerType.GetField("enableQuestDebugging", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // GPU preprocessing fields
            var enableGPUPreprocessingField = controllerType.GetField("enableGPUPreprocessing", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var gpuPreprocessingSettingsField = controllerType.GetField("gpuPreprocessingSettings", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // PhotonVision-inspired filtering fields
            var enablePoseSmoothingField = controllerType.GetField("enablePoseSmoothing", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enableMultiFrameValidationField = controllerType.GetField("enableMultiFrameValidation", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enableCornerQualityAssessmentField = controllerType.GetField("enableCornerQualityAssessment", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Quest perspective correction fields
            var useImprovedIntrinsicsField = controllerType.GetField("useImprovedIntrinsics", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            tagFamilyField?.SetValue(controller, tagFamily);
            tagSizeField?.SetValue(controller, tagSizeMeters);
            decimateField?.SetValue(controller, decimate);
            maxDetectionsField?.SetValue(controller, maxDetectionsPerSecond);
            fovField?.SetValue(controller, horizontalFovDeg);
            scaleVizField?.SetValue(controller, scaleVizToTagSize);
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
            
            // Apply GPU preprocessing settings (start disabled for initial testing)
            enableGPUPreprocessingField?.SetValue(controller, false); // Start with GPU preprocessing disabled until verified working
            
            // Create and configure GPU preprocessing settings
            if (gpuPreprocessingSettingsField != null)
            {
                var settingsType = System.Type.GetType("AprilTag.AprilTagGPUPreprocessor+PreprocessingSettings, Assembly-CSharp");
                if (settingsType != null)
                {
                    var settings = System.Activator.CreateInstance(settingsType);
                    
                    // Configure optimal settings for Quest
                    var enableAdaptiveThresholdField = settingsType.GetField("enableAdaptiveThreshold");
                    var enableHistogramEqualizationField = settingsType.GetField("enableHistogramEqualization");
                    var enableNoiseReductionField = settingsType.GetField("enableNoiseReduction");
                    var enableEdgeEnhancementField = settingsType.GetField("enableEdgeEnhancement");
                    var useHalfPrecisionField = settingsType.GetField("useHalfPrecision");
                    
                    // Enable working features, disable problematic ones
                    enableAdaptiveThresholdField?.SetValue(settings, false); // Binary output can be too aggressive
                    enableHistogramEqualizationField?.SetValue(settings, true); // Working feature
                    enableNoiseReductionField?.SetValue(settings, true); // Working feature
                    enableEdgeEnhancementField?.SetValue(settings, false); // Problematic - causes crashes
                    useHalfPrecisionField?.SetValue(settings, true); // Better performance on Quest
                    
                    gpuPreprocessingSettingsField.SetValue(controller, settings);
                }
            }
            
            // Apply PhotonVision-inspired filtering settings (enabled by default for better accuracy)
            enablePoseSmoothingField?.SetValue(controller, true);
            enableMultiFrameValidationField?.SetValue(controller, true);
            enableCornerQualityAssessmentField?.SetValue(controller, true);
            
            // Apply Quest-specific fixes for wall-mounted tag parallax
            usePassthroughRaycastingField?.SetValue(controller, true);
            useImprovedIntrinsicsField?.SetValue(controller, true);
            
            // Use center eye transform for better head angle compensation
            useCenterEyeTransformField?.SetValue(controller, true);
            
            // Set tuned configuration values
            Vector3 finalCornerOffset = useUserRuntimeOffset ? userRuntimeOffset : cornerPositionOffset;
            cornerPositionOffsetField?.SetValue(controller, finalCornerOffset);
            enableConfigurationToolField?.SetValue(controller, enableConfigurationTool);
            enableAllDebugLoggingField?.SetValue(controller, enableAllDebugLogging);
            
            // Apply user runtime offset if enabled
            if (useUserRuntimeOffset && applyUserOffsetOnSetup)
            {
                ApplyUserRuntimeOffset(controller);
            }

            // Set up tag visualization prefab
            GameObject vizPrefab = tagVizPrefab;
            if (vizPrefab == null && createSimpleTagViz)
            {
                vizPrefab = CreateSimpleTagVisualizationPrefab();
            }
            vizPrefabField?.SetValue(controller, vizPrefab);

            // Position the controller appropriately
            controllerGO.transform.position = Vector3.zero;

        }
        else
        {
        }
    }

    private GameObject CreateSimpleTagVisualizationPrefab()
    {
        // Create a flat red square prefab for tag visualization
        var prefab = new GameObject("SimpleTagVizPrefab");
        
        // Start with unit scale - will be scaled by tagSizeMeters in positioning logic
        prefab.transform.localScale = Vector3.one;
        
        // Create the flat red square
        var redSquare = CreateFlatRedSquare(prefab.transform);
        
        // Remove any colliders (not needed for visualization)
        var colliders = prefab.GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            if (col != null) DestroyImmediate(col);
        }

        return prefab;
    }
    
    private GameObject CreateFlatRedSquare(Transform parent)
    {
        // Create a flat red square for tag visualization
        var squareGO = new GameObject("FlatRedSquare");
        squareGO.transform.SetParent(parent);
        squareGO.transform.localPosition = Vector3.zero;
        squareGO.transform.localScale = Vector3.one;
        squareGO.transform.localRotation = Quaternion.identity;
        
        // Create a quad mesh for the flat square
        var quadGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadGO.transform.SetParent(squareGO.transform);
        quadGO.transform.localPosition = Vector3.zero;
        quadGO.transform.localScale = Vector3.one;
        quadGO.transform.localRotation = Quaternion.identity;
        
        // Remove the collider
        var collider = quadGO.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyImmediate(collider);
        }
        
        // Create transparent red material
        var renderer = quadGO.GetComponent<Renderer>();
        if (renderer != null)
        {
            var material = CreateTransparentRedMaterial();
            renderer.material = material;
        }
        
        return squareGO;
    }
    
    private Material CreateTransparentRedMaterial()
    {
        // Create a transparent red material for the flat square
        var shader = Shader.Find("Standard");
        if (shader == null)
        {
            // Fallback to default shader
            shader = Shader.Find("Legacy Shaders/Diffuse");
        }
        
        if (shader == null)
        {
            // Final fallback
            shader = Shader.Find("Unlit/Color");
        }
        
        var material = new Material(shader);
        
        // Set to transparent red
        material.color = new Color(1f, 0f, 0f, 0.5f); // Red with 50% transparency
        
        // Configure for transparency
        material.SetFloat("_Mode", 3); // Transparent mode
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0); // Don't write to depth buffer
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000; // Transparent render queue
        
        // Configure to ignore occlusion
        material.SetInt("_ZTest", 0); // Always pass depth test
        
        return material;
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

        }
        else
        {
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

        }
        else
        {
        }
    }

    private void SetupPermissionUI()
    {
        // Check for existing permission UI component
        var existingUI = FindFirstObjectByType<AprilTagPermissionUI>();
        if (existingUI != null)
        {
            Debug.Log("[AprilTagSceneSetup] AprilTagPermissionUI already exists, skipping creation");
            return;
        }

        // Check for existing permission panel GameObject (even without component)
        var existingPanel = GameObject.Find("AprilTagPermissionPanel");
        if (existingPanel != null)
        {
            Debug.LogWarning("[AprilTagSceneSetup] Found existing AprilTagPermissionPanel GameObject without AprilTagPermissionUI component. Adding component to existing panel.");
            
            // Add the component to the existing panel
            var existingPermissionUI = existingPanel.GetComponent<AprilTagPermissionUI>();
            if (existingPermissionUI == null)
            {
                existingPermissionUI = existingPanel.AddComponent<AprilTagPermissionUI>();
            }
            
            // Configure the existing panel
            ConfigurePermissionUI(existingPermissionUI);
            return;
        }

        // Create a simple UI canvas if none exists
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            var canvasGO = new GameObject("Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            
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
        
        // Configure the new panel
        ConfigurePermissionUI(permissionUI);

        // Initially hide the panel (it will show automatically if permissions are needed)
        panelGO.SetActive(false);
    }

    private void ConfigurePermissionUI(AprilTagPermissionUI permissionUI)
    {
        if (permissionUI == null) return;

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
    }







    [ContextMenu("Setup Quest-Specific Features")]
    public void SetupQuestSpecificFeatures()
    {
        // Setup Center Eye Anchor
        if (setupCenterEyeAnchor)
        {
            SetupCenterEyeAnchor();
        }

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
            }
        }

        // Update AprilTag Controller with Quest settings
        UpdateAprilTagControllerWithQuestSettings();

    }
    
    private void SetupCenterEyeAnchor()
    {
        // Try to find existing CenterEyeAnchor
        if (centerEyeAnchor == null)
        {
            // Look for XROrigin first
            var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                // Found XROrigin with camera - this is our center eye
                centerEyeAnchor = xrOrigin.Camera.gameObject;
                Debug.Log($"[AprilTagSceneSetup] Found CenterEyeAnchor from XROrigin: {centerEyeAnchor.name}");
            }
            else
            {
                // Look for OVRCameraRig (older Oculus Integration)
                var ovrCameraRig = GameObject.Find("OVRCameraRig");
                if (ovrCameraRig != null)
                {
                    var centerEye = ovrCameraRig.transform.Find("TrackingSpace/CenterEyeAnchor");
                    if (centerEye != null)
                    {
                        centerEyeAnchor = centerEye.gameObject;
                        Debug.Log($"[AprilTagSceneSetup] Found CenterEyeAnchor from OVRCameraRig: {centerEyeAnchor.name}");
                    }
                }
            }
            
            // Final fallback - look for main camera
            if (centerEyeAnchor == null && Camera.main != null)
            {
                centerEyeAnchor = Camera.main.gameObject;
                Debug.Log($"[AprilTagSceneSetup] Using Main Camera as CenterEyeAnchor: {centerEyeAnchor.name}");
            }
            
            if (centerEyeAnchor == null)
            {
                Debug.LogWarning("[AprilTagSceneSetup] Could not find CenterEyeAnchor. Please ensure XROrigin or OVRCameraRig is in the scene.");
            }
        }
        
        // Update reference camera if using center eye transform
        if (useCenterEyeTransform && centerEyeAnchor != null)
        {
            var camera = centerEyeAnchor.GetComponent<Camera>();
            if (camera != null)
            {
                referenceCamera = camera;
                Debug.Log($"[AprilTagSceneSetup] Set reference camera to CenterEyeAnchor camera: {referenceCamera.name}");
            }
        }
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
                
            }
            else
            {
                Debug.LogWarning("[AprilTagSceneSetup] No EnvironmentRaycastManager found and createSimpleEnvironmentRaycastManager is disabled. Please add one manually or enable createSimpleEnvironmentRaycastManager.");
            }
        }
        else
        {
            environmentRaycastManager = existingManager;
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
        var referenceCameraField = controllerType.GetField("referenceCamera", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var useCenterEyeTransformField = controllerType.GetField("useCenterEyeTransform", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        usePassthroughRaycastingField?.SetValue(controller, enablePassthroughRaycasting);
        environmentRaycastManagerField?.SetValue(controller, environmentRaycastManager);
        
        // Update reference camera from center eye anchor if available
        if (useCenterEyeTransform && centerEyeAnchor != null)
        {
            var camera = centerEyeAnchor.GetComponent<Camera>();
            if (camera != null)
            {
                referenceCameraField?.SetValue(controller, camera);
                useCenterEyeTransformField?.SetValue(controller, true);
                Debug.Log($"[AprilTagSceneSetup] Updated AprilTagController reference camera to CenterEyeAnchor: {camera.name}");
            }
        }

    }

    [ContextMenu("Setup Environment Raycast Manager Only")]
    public void SetupEnvironmentRaycastManagerOnly()
    {
        SetupEnvironmentRaycastManager();
    }
    
    [ContextMenu("Setup Center Eye Anchor Only")]
    public void SetupCenterEyeAnchorOnly()
    {
        SetupCenterEyeAnchor();
        UpdateAprilTagControllerWithQuestSettings();
    }

    /// <summary>
    /// Quest-compatible method to clean up duplicate permission panels
    /// Call this from other scripts or UI buttons on Quest
    /// </summary>
    public void CleanUpDuplicatePanelsOnQuest()
    {
        CleanUpDuplicatePermissionPanels();
    }

    /// <summary>
    /// Static method to clean up duplicate permission panels from anywhere
    /// </summary>
    public static void CleanUpDuplicatePermissionPanelsStatic()
    {
        // Find all AprilTagPermissionPanel GameObjects
        var allPanels = GameObject.FindGameObjectsWithTag("Untagged")
            .Where(go => go.name == "AprilTagPermissionPanel")
            .ToArray();

        if (allPanels.Length <= 1)
        {
            Debug.Log("[AprilTagSceneSetup] No duplicate permission panels found");
            return;
        }

        Debug.Log($"[AprilTagSceneSetup] Found {allPanels.Length} permission panels, cleaning up duplicates...");

        // Keep the first one with a valid AprilTagPermissionUI component
        GameObject keepPanel = null;
        for (int i = 0; i < allPanels.Length; i++)
        {
            var ui = allPanels[i].GetComponent<AprilTagPermissionUI>();
            if (ui != null)
            {
                keepPanel = allPanels[i];
                break;
            }
        }

        // If no panel has the component, keep the first one and add the component
        if (keepPanel == null)
        {
            keepPanel = allPanels[0];
            keepPanel.AddComponent<AprilTagPermissionUI>();
            
            // Get a scene setup instance to configure the UI
            var sceneSetup = FindFirstObjectByType<AprilTagSceneSetup>();
            if (sceneSetup != null)
            {
                sceneSetup.ConfigurePermissionUI(keepPanel.GetComponent<AprilTagPermissionUI>());
            }
        }

        // Destroy all other panels
        int destroyedCount = 0;
        for (int i = 0; i < allPanels.Length; i++)
        {
            if (allPanels[i] != keepPanel)
            {
                if (Application.isPlaying)
                {
                    Destroy(allPanels[i]);
                }
                else
                {
                    DestroyImmediate(allPanels[i]);
                }
                destroyedCount++;
            }
        }

        Debug.Log($"[AprilTagSceneSetup] Cleaned up {destroyedCount} duplicate permission panels. Kept: {keepPanel.name}");
    }

    private void CleanUpDuplicatePermissionPanels()
    {
        // Find all AprilTagPermissionPanel GameObjects
        var allPanels = GameObject.FindGameObjectsWithTag("Untagged")
            .Where(go => go.name == "AprilTagPermissionPanel")
            .ToArray();

        if (allPanels.Length <= 1)
        {
            Debug.Log("[AprilTagSceneSetup] No duplicate permission panels found");
            return;
        }

        Debug.Log($"[AprilTagSceneSetup] Found {allPanels.Length} permission panels, cleaning up duplicates...");

        // Keep the first one with a valid AprilTagPermissionUI component
        GameObject keepPanel = null;
        for (int i = 0; i < allPanels.Length; i++)
        {
            var ui = allPanels[i].GetComponent<AprilTagPermissionUI>();
            if (ui != null)
            {
                keepPanel = allPanels[i];
                break;
            }
        }

        // If no panel has the component, keep the first one and add the component
        if (keepPanel == null)
        {
            keepPanel = allPanels[0];
            keepPanel.AddComponent<AprilTagPermissionUI>();
            ConfigurePermissionUI(keepPanel.GetComponent<AprilTagPermissionUI>());
        }

        // Destroy all other panels
        int destroyedCount = 0;
        for (int i = 0; i < allPanels.Length; i++)
        {
            if (allPanels[i] != keepPanel)
            {
                if (Application.isPlaying)
                {
                    Destroy(allPanels[i]);
                }
                else
                {
                    DestroyImmediate(allPanels[i]);
                }
                destroyedCount++;
            }
        }

        Debug.Log($"[AprilTagSceneSetup] Cleaned up {destroyedCount} duplicate permission panels. Kept: {keepPanel.name}");
    }

    // User Runtime Offset Management Methods
    private void ApplyUserRuntimeOffset(AprilTagController controller)
    {
        if (controller == null) return;
        
        // Use reflection to set the cornerPositionOffset field directly
        var controllerType = typeof(AprilTagController);
        var cornerPositionOffsetField = controllerType.GetField("cornerPositionOffset", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        cornerPositionOffsetField?.SetValue(controller, userRuntimeOffset);
        
        // Save to PlayerPrefs if enabled
        if (saveUserOffsetToPersistence)
        {
            SaveUserOffsetToPlayerPrefs();
        }
        
        Debug.Log($"[AprilTagSceneSetup] Applied user runtime offset: {userRuntimeOffset}");
    }
    
    private void SaveUserOffsetToPlayerPrefs()
    {
        PlayerPrefs.SetFloat("AprilTag_CornerOffset_X", userRuntimeOffset.x);
        PlayerPrefs.SetFloat("AprilTag_CornerOffset_Y", userRuntimeOffset.y);
        PlayerPrefs.SetFloat("AprilTag_CornerOffset_Z", userRuntimeOffset.z);
        PlayerPrefs.Save();
        Debug.Log($"[AprilTagSceneSetup] Saved user runtime offset to PlayerPrefs: {userRuntimeOffset}");
    }
    
    private void LoadUserOffsetFromPlayerPrefs()
    {
        if (PlayerPrefs.HasKey("AprilTag_CornerOffset_X"))
        {
            userRuntimeOffset = new Vector3(
                PlayerPrefs.GetFloat("AprilTag_CornerOffset_X", 0f),
                PlayerPrefs.GetFloat("AprilTag_CornerOffset_Y", 0f),
                PlayerPrefs.GetFloat("AprilTag_CornerOffset_Z", 0f)
            );
            Debug.Log($"[AprilTagSceneSetup] Loaded user runtime offset from PlayerPrefs: {userRuntimeOffset}");
        }
    }
    
    /// <summary>
    /// Set the user runtime offset values (X, Y, Z in meters)
    /// </summary>
    /// <param name="offset">The offset values to apply</param>
    public void SetUserRuntimeOffset(Vector3 offset)
    {
        userRuntimeOffset = offset;
        useUserRuntimeOffset = true;
        
        // Apply to existing controller if available
        var controller = FindFirstObjectByType<AprilTagController>();
        if (controller != null)
        {
            ApplyUserRuntimeOffset(controller);
        }
        
        Debug.Log($"[AprilTagSceneSetup] Set user runtime offset: {userRuntimeOffset}");
    }
    
    /// <summary>
    /// Set individual user runtime offset components
    /// </summary>
    /// <param name="x">X offset in meters</param>
    /// <param name="y">Y offset in meters</param>
    /// <param name="z">Z offset in meters</param>
    public void SetUserRuntimeOffset(float x, float y, float z)
    {
        SetUserRuntimeOffset(new Vector3(x, y, z));
    }
    
    /// <summary>
    /// Get the current user runtime offset values
    /// </summary>
    /// <returns>The current user runtime offset</returns>
    public Vector3 GetUserRuntimeOffset()
    {
        return userRuntimeOffset;
    }
    
    /// <summary>
    /// Enable or disable user runtime offset
    /// </summary>
    /// <param name="enabled">Whether to use user runtime offset</param>
    public void SetUserRuntimeOffsetEnabled(bool enabled)
    {
        useUserRuntimeOffset = enabled;
        
        // Apply changes to existing controller if available
        var controller = FindFirstObjectByType<AprilTagController>();
        if (controller != null)
        {
            Vector3 finalOffset = useUserRuntimeOffset ? userRuntimeOffset : cornerPositionOffset;
            ApplyUserRuntimeOffset(controller);
        }
        
        Debug.Log($"[AprilTagSceneSetup] User runtime offset {(enabled ? "enabled" : "disabled")}");
    }

    // Context Menu Methods for Easy Configuration
    [ContextMenu("Load User Offset from PlayerPrefs")]
    public void LoadUserOffsetFromPlayerPrefsMenu()
    {
        LoadUserOffsetFromPlayerPrefs();
    }
    
    [ContextMenu("Save User Offset to PlayerPrefs")]
    public void SaveUserOffsetToPlayerPrefsMenu()
    {
        SaveUserOffsetToPlayerPrefs();
    }
    
    [ContextMenu("Apply User Runtime Offset")]
    public void ApplyUserRuntimeOffsetMenu()
    {
        var controller = FindFirstObjectByType<AprilTagController>();
        if (controller != null)
        {
            ApplyUserRuntimeOffset(controller);
        }
        else
        {
            Debug.LogWarning("[AprilTagSceneSetup] No AprilTagController found to apply offset to");
        }
    }
    
    [ContextMenu("Reset User Runtime Offset")]
    public void ResetUserRuntimeOffset()
    {
        userRuntimeOffset = Vector3.zero;
        useUserRuntimeOffset = false;
        
        var controller = FindFirstObjectByType<AprilTagController>();
        if (controller != null)
        {
            // Reset to default corner offset
            var controllerType = typeof(AprilTagController);
            var cornerPositionOffsetField = controllerType.GetField("cornerPositionOffset", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            cornerPositionOffsetField?.SetValue(controller, cornerPositionOffset);
        }
        
        Debug.Log("[AprilTagSceneSetup] Reset user runtime offset to zero and disabled user offset");
    }
    
    [ContextMenu("Log Current Offset Settings")]
    public void LogCurrentOffsetSettings()
    {
        Debug.Log($"[AprilTagSceneSetup] Current Offset Settings:");
        Debug.Log($"  - Use User Runtime Offset: {useUserRuntimeOffset}");
        Debug.Log($"  - User Runtime Offset: {userRuntimeOffset}");
        Debug.Log($"  - Default Corner Offset: {cornerPositionOffset}");
        Debug.Log($"  - Apply On Setup: {applyUserOffsetOnSetup}");
        Debug.Log($"  - Save To Persistence: {saveUserOffsetToPersistence}");
        
        var controller = FindFirstObjectByType<AprilTagController>();
        if (controller != null)
        {
            var controllerType = typeof(AprilTagController);
            var cornerPositionOffsetField = controllerType.GetField("cornerPositionOffset", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var currentOffset = cornerPositionOffsetField?.GetValue(controller);
            Debug.Log($"  - Current Controller Offset: {currentOffset}");
        }
    }
    
    [ContextMenu("Set Common Experimental Offsets/Small Forward Adjustment")]
    public void SetSmallForwardAdjustment()
    {
        SetUserRuntimeOffset(0.01f, 0f, 0.02f); // Small forward and right adjustment
        Debug.Log("[AprilTagSceneSetup] Applied small forward adjustment offset");
    }
    
    [ContextMenu("Set Common Experimental Offsets/Medium Calibration")]
    public void SetMediumCalibration()
    {
        SetUserRuntimeOffset(0.03f, 0.01f, 0.05f); // Medium calibration
        Debug.Log("[AprilTagSceneSetup] Applied medium calibration offset");
    }
    
    [ContextMenu("Set Common Experimental Offsets/Large Calibration")]
    public void SetLargeCalibration()
    {
        SetUserRuntimeOffset(0.05f, 0.02f, 0.08f); // Large calibration
        Debug.Log("[AprilTagSceneSetup] Applied large calibration offset");
    }
    

}
