// Assets/AprilTag/AprilTagSceneSetup.cs
// Setup helper for AprilTag system - creates and configures all necessary components
// Focus: Only setup and creation, not runtime configuration

using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace AprilTag
{
public class AprilTagSceneSetup : MonoBehaviour
{
        [Header("Component Creation")]
        [Tooltip("Create WebCamTextureManager if not found in scene")]
    [SerializeField] private bool createWebCamManager = true;
        
        [Tooltip("Create AprilTagPermissionsManager if not found in scene")]
        [SerializeField] private bool createPermissionSystem = true;
        
        [Tooltip("Create AprilTagController if not found in scene")]
        [SerializeField] private bool createAprilTagController = true;
        
        [Tooltip("Create AprilTagSpatialAnchorManager if not found in scene")]
        [SerializeField] private bool createSpatialAnchorManager = true;
        
        
        [Header("Visualization Setup")]
        [Tooltip("Create tag visualization prefab automatically")]
        [SerializeField] private bool createTagVisualization = true;
        
        [Tooltip("Visualization type to create")]
        [SerializeField] private VisualizationType visualizationType = VisualizationType.FlatRedSquare;
        
        [Header("Anchor Visualization")]
        [Tooltip("Create anchor visualization prefab automatically")]
        [SerializeField] private bool createAnchorVisualization = true;
        
        [Tooltip("Anchor visualization type to create")]
        [SerializeField] private AnchorVisualizationType anchorVisualizationType = AnchorVisualizationType.SmallCube;
        
        [Header("Debug")]
        [Tooltip("Enable debug logging for setup process")]
        [SerializeField] private bool enableSetupLogging = true;
        
        
        public enum VisualizationType
        {
            FlatRedSquare,
            WireframeCube,
            Custom
        }
        
        public enum AnchorVisualizationType
        {
            SmallCube,
            WireframeSphere,
            Custom
        }
        
        void Start()
        {
            SetupCompleteAprilTagSystem();
    }
    

    public void SetupCompleteAprilTagSystem()
        {
            if (enableSetupLogging)
            {
                Debug.Log("[AprilTagSceneSetup] Starting complete AprilTag system setup...");
            }
            
            // Create components in dependency order
            var webCamManager = SetupWebCamManager();
            var permissionManager = SetupPermissionManager();
            var aprilTagController = SetupAprilTagController();
            var spatialAnchorManager = SetupSpatialAnchorManager();
            
            // Connect components
            ConnectComponents(webCamManager, permissionManager, aprilTagController, spatialAnchorManager);
            
            if (enableSetupLogging)
            {
                Debug.Log("[AprilTagSceneSetup] Complete AprilTag system setup finished!");
            }
        }
        
        private UnityEngine.Object SetupWebCamManager()
        {
            if (!createWebCamManager) return null;
            
            var existingManager = FindFirstObjectByType(System.Type.GetType("PassthroughCameraSamples.WebCamTextureManager"));
            if (existingManager != null)
            {
                if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Found existing WebCamTextureManager");
                return existingManager as UnityEngine.Object;
            }
            
            var webCamGO = new GameObject("WebCamTextureManager");
            var webCamManagerType = System.Type.GetType("PassthroughCameraSamples.WebCamTextureManager");
            if (webCamManagerType != null)
            {
                var component = webCamGO.AddComponent(webCamManagerType);
                if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Created WebCamTextureManager component");
                return component as UnityEngine.Object;
        }
        else
        {
                if (enableSetupLogging) Debug.LogWarning("[AprilTagSceneSetup] WebCamTextureManager type not found");
                DestroyImmediate(webCamGO);
                return null;
            }
        }
        
        private AprilTagPermissionsManager SetupPermissionManager()
        {
            if (!createPermissionSystem) return null;
            
            var existingManager = FindFirstObjectByType<AprilTagPermissionsManager>();
            if (existingManager != null)
            {
                if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Found existing AprilTagPermissionsManager");
                return existingManager;
            }
            
            var permissionGO = new GameObject("AprilTagPermissionsManager");
            var permissionManager = permissionGO.AddComponent<AprilTagPermissionsManager>();
            
            if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Created AprilTagPermissionsManager");
            return permissionManager;
        }
        
        private AprilTagController SetupAprilTagController()
        {
            if (!createAprilTagController) return null;
            
            var existingController = FindFirstObjectByType<AprilTagController>();
            if (existingController != null)
            {
                if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Found existing AprilTagController");
                return existingController;
            }
            
            var controllerGO = new GameObject("AprilTagController");
            var controller = controllerGO.AddComponent<AprilTagController>();
            
            if (createTagVisualization)
            {
                var vizPrefab = CreateVisualizationPrefab();
                if (vizPrefab != null)
                {
                    var controllerType = typeof(AprilTagController);
                    var vizField = controllerType.GetField("tagVizPrefab", BindingFlags.NonPublic | BindingFlags.Instance);
                    vizField?.SetValue(controller, vizPrefab);
                    
                    if (enableSetupLogging) Debug.Log($"[AprilTagSceneSetup] Set {visualizationType} visualization prefab");
                }
            }
            
            if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Created AprilTagController");
            return controller;
        }
        
        private AprilTagSpatialAnchorManager SetupSpatialAnchorManager()
        {
            if (!createSpatialAnchorManager) return null;
            
            var existingManager = FindFirstObjectByType<AprilTagSpatialAnchorManager>();
            if (existingManager != null)
            {
                if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Found existing AprilTagSpatialAnchorManager");
                
                // Set anchor visualization prefab if needed
                if (createAnchorVisualization)
                {
                    var anchorPrefab = CreateAnchorVisualizationPrefab();
                    if (anchorPrefab != null)
                    {
                        var managerType = typeof(AprilTagSpatialAnchorManager);
                        var prefabField = managerType.GetField("anchorPrefab", BindingFlags.NonPublic | BindingFlags.Instance);
                        prefabField?.SetValue(existingManager, anchorPrefab);
                        
                        if (enableSetupLogging) Debug.Log($"[AprilTagSceneSetup] Set {anchorVisualizationType} anchor visualization prefab");
                    }
                }
                
                return existingManager;
            }
            
            var anchorGO = new GameObject("AprilTagSpatialAnchorManager");
            var anchorManager = anchorGO.AddComponent<AprilTagSpatialAnchorManager>();
            
            if (createAnchorVisualization)
            {
                var anchorPrefab = CreateAnchorVisualizationPrefab();
                if (anchorPrefab != null)
                {
                    var managerType = typeof(AprilTagSpatialAnchorManager);
                    var prefabField = managerType.GetField("anchorPrefab", BindingFlags.NonPublic | BindingFlags.Instance);
                    prefabField?.SetValue(anchorManager, anchorPrefab);
                    
                    if (enableSetupLogging) Debug.Log($"[AprilTagSceneSetup] Set {anchorVisualizationType} anchor visualization prefab");
                }
            }
            
            if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Created AprilTagSpatialAnchorManager");
            return anchorManager;
        }
        
        
        private void ConnectComponents(UnityEngine.Object webCamManager, AprilTagPermissionsManager permissionManager, 
            AprilTagController aprilTagController, AprilTagSpatialAnchorManager spatialAnchorManager)
        {
            if (aprilTagController == null) return;
            
            var controllerType = typeof(AprilTagController);
            
            if (webCamManager != null)
            {
                var webCamField = controllerType.GetField("webCamManager", BindingFlags.NonPublic | BindingFlags.Instance);
                webCamField?.SetValue(aprilTagController, webCamManager);
                if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Connected WebCamTextureManager");
            }
            
            if (spatialAnchorManager != null)
            {
                var anchorField = controllerType.GetField("spatialAnchorManager", BindingFlags.NonPublic | BindingFlags.Instance);
                anchorField?.SetValue(aprilTagController, spatialAnchorManager);
                if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Connected AprilTagSpatialAnchorManager");
            }
            
            if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] All components connected successfully");
        }
        
        private GameObject CreateVisualizationPrefab()
        {
            switch (visualizationType)
            {
                case VisualizationType.FlatRedSquare:
                    return CreateFlatRedSquare();
                case VisualizationType.WireframeCube:
                    return CreateWireframeCube();
                default:
                    return CreateFlatRedSquare();
            }
        }
        
        private GameObject CreateFlatRedSquare()
        {
            var prefab = new GameObject("AprilTagVisualization_FlatRedSquare");
            
            var meshFilter = prefab.AddComponent<MeshFilter>();
            var meshRenderer = prefab.AddComponent<MeshRenderer>();
            
            meshFilter.mesh = CreateQuadMesh();
            meshRenderer.material = CreateTransparentRedMaterial();
            
            if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Created flat red square visualization prefab");
            return prefab;
        }
        
        private GameObject CreateWireframeCube()
        {
            var prefab = new GameObject("AprilTagVisualization_WireframeCube");
            
            var lineRenderer = prefab.AddComponent<LineRenderer>();
            var lineMaterial = new Material(Shader.Find("Sprites/Default"));
            lineMaterial.color = Color.green;
            lineRenderer.material = lineMaterial;
            lineRenderer.startWidth = 0.01f;
            lineRenderer.endWidth = 0.01f;
            lineRenderer.useWorldSpace = false;
            
            var cubeVertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
            };
            
            var linePoints = new Vector3[]
            {
                cubeVertices[0], cubeVertices[1], cubeVertices[2], cubeVertices[3], cubeVertices[0],
                cubeVertices[4], cubeVertices[5], cubeVertices[6], cubeVertices[7], cubeVertices[4],
                cubeVertices[0], cubeVertices[4], cubeVertices[1], cubeVertices[5],
                cubeVertices[2], cubeVertices[6], cubeVertices[3], cubeVertices[7]
            };
            
            lineRenderer.positionCount = linePoints.Length;
            lineRenderer.SetPositions(linePoints);
            
            if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Created wireframe cube visualization prefab");
            return prefab;
        }
        
        private Mesh CreateQuadMesh()
        {
            var mesh = new Mesh();
            mesh.name = "AprilTagQuad";
            
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0)
            };
            
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.uv = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.normals = new Vector3[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            
            return mesh;
        }
        
        private Material CreateTransparentRedMaterial()
        {
            var material = new Material(Shader.Find("Standard"));
            material.name = "AprilTagRedTransparent";
            
            material.SetFloat("_Mode", 3);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
            
            material.color = new Color(1f, 0f, 0f, 0.7f);
            
            return material;
        }
        
        private GameObject CreateAnchorVisualizationPrefab()
        {
            switch (anchorVisualizationType)
            {
                case AnchorVisualizationType.SmallCube:
                    return CreateSmallCubeAnchor();
                case AnchorVisualizationType.WireframeSphere:
                    return CreateWireframeSphereAnchor();
                default:
                    return CreateSmallCubeAnchor();
            }
        }
        
        private GameObject CreateSmallCubeAnchor()
        {
            var prefab = new GameObject("AprilTag_AnchorVisualization_SmallCube");
            
            // Add the required OVRSpatialAnchor component
            prefab.AddComponent<global::OVRSpatialAnchor>();
            
            // Create a visual representation child object
            var visualGO = new GameObject("Visual");
            visualGO.transform.SetParent(prefab.transform);
            visualGO.transform.localPosition = Vector3.zero;
            visualGO.transform.localRotation = Quaternion.identity;
            visualGO.transform.localScale = Vector3.one;
            
            // Add a simple cube mesh for visualization
            var meshFilter = visualGO.AddComponent<MeshFilter>();
            var meshRenderer = visualGO.AddComponent<MeshRenderer>();
            
            // Create a simple cube mesh
            var cubeMesh = CreateAnchorCubeMesh();
            meshFilter.mesh = cubeMesh;
            
            // Create a material for the anchor
            var material = CreateAnchorMaterial();
            meshRenderer.material = material;
            
            // Scale the visual to be small and unobtrusive
            visualGO.transform.localScale = Vector3.one * 0.1f; // 10cm cube
            
            if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Created small cube anchor visualization prefab");
            return prefab;
        }
        
        private GameObject CreateWireframeSphereAnchor()
        {
            var prefab = new GameObject("AprilTag_AnchorVisualization_WireframeSphere");
            
            // Add the required OVRSpatialAnchor component
            prefab.AddComponent<global::OVRSpatialAnchor>();
            
            // Create a visual representation child object
            var visualGO = new GameObject("Visual");
            visualGO.transform.SetParent(prefab.transform);
            visualGO.transform.localPosition = Vector3.zero;
            visualGO.transform.localRotation = Quaternion.identity;
            visualGO.transform.localScale = Vector3.one;
            
            var lineRenderer = visualGO.AddComponent<LineRenderer>();
            var lineMaterial = new Material(Shader.Find("Sprites/Default"));
            lineMaterial.color = new Color(0.2f, 0.8f, 1.0f, 0.8f); // Light blue
            lineRenderer.material = lineMaterial;
            lineRenderer.startWidth = 0.005f;
            lineRenderer.endWidth = 0.005f;
            lineRenderer.useWorldSpace = false;
            
            // Create wireframe sphere points
            var spherePoints = CreateWireframeSpherePoints();
            lineRenderer.positionCount = spherePoints.Length;
            lineRenderer.SetPositions(spherePoints);
            
            if (enableSetupLogging) Debug.Log("[AprilTagSceneSetup] Created wireframe sphere anchor visualization prefab");
            return prefab;
        }
        
        private Mesh CreateAnchorCubeMesh()
        {
            var mesh = new Mesh();
            mesh.name = "AnchorCube";
            
            // Simple cube vertices
            var vertices = new Vector3[]
            {
                // Front face
                new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f),
                // Back face
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f, -0.5f)
            };
            
            // Simple cube triangles
            var triangles = new int[]
            {
                // Front face
                0, 2, 1, 0, 3, 2,
                // Back face
                4, 6, 5, 4, 7, 6,
                // Left face
                4, 3, 0, 4, 5, 3,
                // Right face
                1, 2, 6, 1, 6, 7,
                // Top face
                3, 5, 2, 2, 5, 6,
                // Bottom face
                0, 1, 4, 1, 7, 4
            };
            
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            
            return mesh;
        }
        
        private Vector3[] CreateWireframeSpherePoints()
        {
            var points = new List<Vector3>();
            int segments = 16;
            float radius = 0.5f;
            
            // Create circles around the sphere
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                
                // XY plane circle
                points.Add(new Vector3(x, z, 0));
                
                // XZ plane circle  
                points.Add(new Vector3(x, 0, z));
                
                // YZ plane circle
                points.Add(new Vector3(0, x, z));
            }
            
            return points.ToArray();
        }
        
        private Material CreateAnchorMaterial()
        {
            var material = new Material(Shader.Find("Standard"));
            material.name = "AprilTagAnchorMaterial";
            
            // Set a distinctive color for spatial anchors
            material.color = new Color(0.2f, 0.8f, 1.0f, 0.8f); // Light blue with transparency
            material.SetFloat("_Mode", 3); // Transparent mode
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
            
            return material;
        }
        
        [ContextMenu("Setup Complete AprilTag System")]
        public void ManualSetup() { SetupCompleteAprilTagSystem(); }
    }
}
