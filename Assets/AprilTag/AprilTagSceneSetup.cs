// Assets/AprilTag/AprilTagSceneSetupSimplified.cs
// Simplified setup script for AprilTag detection system on Quest
// Automatically creates all necessary components with sensible defaults

using UnityEngine;
using UnityEngine.UI;
using AprilTag;
using PassthroughCameraSamples;
using Meta.XR.Samples;
using Meta.XR;

public class AprilTagSceneSetup : MonoBehaviour
{
    [Header("Essential Settings")]
    [Tooltip("Automatically set up the complete system when the scene starts")]
    [SerializeField] private bool setupOnAwake = true;
    
    [Header("AprilTag Detection")]
    [Tooltip("Physical size of your AprilTag markers in meters")]
    [SerializeField] private float tagSizeMeters = 0.165f;
    [Tooltip("Tag visualization prefab (optional - will create default if not set)")]
    [SerializeField] private GameObject tagVizPrefab;
    
    [Header("Advanced Settings")]
    [Tooltip("Detection performance vs accuracy (1=highest quality, 8=fastest)")]
    [Range(1, 8)][SerializeField] private int decimation = 2;
    [Tooltip("Maximum detections per second")]
    [SerializeField] private float maxDetectionsPerSecond = 30f;
    [Tooltip("Position offset for calibration")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [Tooltip("Rotation offset for calibration")]
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    // Fixed settings that work well for Quest
    private const AprilTag.Interop.TagFamily tagFamily = AprilTag.Interop.TagFamily.Tag36h11;
    private const float horizontalFovDeg = 78f;
    private const PassthroughCameraSamples.PassthroughCameraEye cameraEye = PassthroughCameraSamples.PassthroughCameraEye.Left;
    
    void Awake()
    {
        if (setupOnAwake)
        {
            SetupCompleteSystem();
        }
    }

    [ContextMenu("Setup Complete AprilTag System")]
    public void SetupCompleteSystem()
    {
        Debug.Log("[AprilTagSetup] Starting complete system setup...");
        
        // 1. Create Permissions Manager (always needed)
        SetupPermissionsManager();
        
        // 2. Create Permission UI (always needed)
        SetupPermissionUI();
        
        // 3. Create WebCam Manager (always needed)
        SetupWebCamManager();
        
        // 4. Create AprilTag Controller (always needed)
        SetupAprilTagController();
        
        // 5. Setup Quest-specific features
        SetupQuestFeatures();
        
        Debug.Log("[AprilTagSetup] Complete system setup finished!");
    }

    private void SetupPermissionsManager()
    {
        var existing = FindFirstObjectByType<AprilTagPermissionsManager>();
        if (existing == null)
        {
            var go = new GameObject("AprilTagPermissionsManager");
            var manager = go.AddComponent<AprilTagPermissionsManager>();
            
            // Configure with sensible defaults via reflection
            var type = typeof(AprilTagPermissionsManager);
            SetPrivateField(manager, type, "requestPermissionsOnStart", true);
            SetPrivateField(manager, type, "retryOnDenial", true);
            SetPrivateField(manager, type, "retryDelaySeconds", 2f);
            
            Debug.Log("[AprilTagSetup] Created AprilTagPermissionsManager");
        }
    }

    private void SetupPermissionUI()
    {
        var existing = FindFirstObjectByType<AprilTagPermissionUI>();
        if (existing == null)
        {
            // Ensure we have a canvas
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("Canvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
            }

            // Create permission UI
            var go = new GameObject("AprilTagPermissionUI");
            go.transform.SetParent(canvas.transform, false);
            var ui = go.AddComponent<AprilTagPermissionUI>();
            
            // Configure with sensible defaults
            var type = typeof(AprilTagPermissionUI);
            SetPrivateField(ui, type, "showPanelOnStart", true);
            SetPrivateField(ui, type, "autoHideOnGranted", true);
            SetPrivateField(ui, type, "autoHideDelay", 3f);
            
            Debug.Log("[AprilTagSetup] Created AprilTagPermissionUI");
        }
    }

    private void SetupWebCamManager()
    {
        var existing = FindFirstObjectByType<PassthroughCameraSamples.WebCamTextureManager>();
        if (existing == null)
        {
            var go = new GameObject("WebCamTextureManager");
            var manager = go.AddComponent<PassthroughCameraSamples.WebCamTextureManager>();
            
            // Configure for Quest passthrough
            manager.Eye = cameraEye;
            manager.RequestedResolution = new Vector2Int(0, 0); // Highest resolution
            
            // Create and assign PassthroughCameraPermissions component
            var permissions = go.AddComponent<PassthroughCameraSamples.PassthroughCameraPermissions>();
            manager.CameraPermissions = permissions;
            
            Debug.Log("[AprilTagSetup] Created WebCamTextureManager");
        }
    }

    private void SetupAprilTagController()
    {
        var existing = FindFirstObjectByType<AprilTagController>();
        if (existing == null)
        {
            var go = new GameObject("AprilTagController");
            var controller = go.AddComponent<AprilTagController>();
            
            // Configure via reflection
            var type = typeof(AprilTagController);
            
            // Set detection parameters
            SetPrivateField(controller, type, "tagFamily", tagFamily);
            SetPrivateField(controller, type, "tagSizeMeters", tagSizeMeters);
            SetPrivateField(controller, type, "decimate", decimation);
            SetPrivateField(controller, type, "maxDetectionsPerSecond", maxDetectionsPerSecond);
            SetPrivateField(controller, type, "horizontalFovDeg", horizontalFovDeg);
            
            // Set visualization
            GameObject vizPrefab = tagVizPrefab ?? CreateDefaultVisualizationPrefab();
            SetPrivateField(controller, type, "tagVizPrefab", vizPrefab);
            SetPrivateField(controller, type, "scaleVizToTagSize", true);
            
            // Set offsets
            SetPrivateField(controller, type, "positionOffset", positionOffset);
            SetPrivateField(controller, type, "rotationOffset", rotationOffset);
            
            // Set Quest-optimized settings
            SetPrivateField(controller, type, "useCenterEyeTransform", true);
            SetPrivateField(controller, type, "usePassthroughRaycasting", true);
            SetPrivateField(controller, type, "ignoreOcclusion", true);
            SetPrivateField(controller, type, "enableQuestDebugging", false);
            SetPrivateField(controller, type, "enableAllDebugLogging", false);
            
            // Connect to WebCamManager
            var webCamManager = FindFirstObjectByType<PassthroughCameraSamples.WebCamTextureManager>();
            if (webCamManager != null)
            {
                SetPrivateField(controller, type, "webCamManager", webCamManager);
            }
            
            Debug.Log("[AprilTagSetup] Created and configured AprilTagController");
        }
    }

    private void SetupQuestFeatures()
    {
        // Create Environment Raycast Manager if needed
        var raycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
        if (raycastManager == null)
        {
            var go = new GameObject("EnvironmentRaycastManager");
            raycastManager = go.AddComponent<EnvironmentRaycastManager>();
            Debug.Log("[AprilTagSetup] Created EnvironmentRaycastManager");
        }
        
        // Update AprilTag controller with raycast manager
        var controller = FindFirstObjectByType<AprilTagController>();
        if (controller != null && raycastManager != null)
        {
            var type = typeof(AprilTagController);
            SetPrivateField(controller, type, "environmentRaycastManager", raycastManager);
        }
    }

    private GameObject CreateDefaultVisualizationPrefab()
    {
        var prefab = new GameObject("AprilTagVisualizationPrefab");
        
        // Create a simple cube for visualization
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "TagVisualizer";
        cube.transform.SetParent(prefab.transform);
        cube.transform.localPosition = Vector3.zero;
        cube.transform.localScale = new Vector3(1f, 1f, 0.02f); // Thin cube
        
        // Apply red translucent material
        var renderer = cube.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            var material = CreateRedMaterial(0.4f); // 40% opacity red
            if (material != null)
            {
                renderer.material = material;
            }
        }
        
        // Remove collider (handle both edit and play mode)
        var collider = cube.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }
        
        // Make the prefab inactive so it doesn't render in the scene
        prefab.SetActive(false);
        
        Debug.Log("[AprilTagSetup] Created default visualization prefab");
        return prefab;
    }
    
    private Material CreateRedMaterial(float opacity)
    {
        // Try to find a suitable shader with fallbacks
        var shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            shader = Shader.Find("Legacy Shaders/Diffuse");
        }
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        
        if (shader != null)
        {
            var material = new Material(shader);
            material.color = new Color(1f, 0f, 0f, opacity);
            material.renderQueue = 3000; // Render on top
            
            // Enable transparency if using Standard shader and not fully opaque
            if (shader.name == "Standard" && opacity < 1f)
            {
                material.SetFloat("_Mode", 3); // Transparent mode
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_ALPHABLEND_ON");
            }
            
            return material;
        }
        
        return null;
    }

    // Helper method to set private fields via reflection
    private void SetPrivateField(object target, System.Type type, string fieldName, object value)
    {
        var field = type.GetField(fieldName, 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
        field?.SetValue(target, value);
    }
}
