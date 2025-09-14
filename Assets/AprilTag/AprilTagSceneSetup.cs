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
    [SerializeField] private Vector3 cornerPositionOffset = new Vector3(0.030f, 0.010f, 0.000f);
    [SerializeField] private bool enableConfigurationTool = true;
    [SerializeField] private bool enableAllDebugLogging = false;
    [SerializeField] private float tunedRotationZ = -225f;
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
            
            // Set tuned configuration values
            cornerPositionOffsetField?.SetValue(controller, cornerPositionOffset);
            enableConfigurationToolField?.SetValue(controller, enableConfigurationTool);
            enableAllDebugLoggingField?.SetValue(controller, enableAllDebugLogging);

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

        }
        else
        {
        }
    }







    [ContextMenu("Setup Quest-Specific Features")]
    public void SetupQuestSpecificFeatures()
    {

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

        usePassthroughRaycastingField?.SetValue(controller, enablePassthroughRaycasting);
        environmentRaycastManagerField?.SetValue(controller, environmentRaycastManager);

    }

    [ContextMenu("Setup Environment Raycast Manager Only")]
    public void SetupEnvironmentRaycastManagerOnly()
    {
        SetupEnvironmentRaycastManager();
    }




}
