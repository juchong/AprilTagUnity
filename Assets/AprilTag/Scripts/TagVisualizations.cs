// Assets/AprilTag/Scripts/TagVisualizations.cs

using System.Reflection;
using UnityEngine;

/// <summary>
/// Generates a runtime visualization template equivalent to SimpleTagVizPrefab
/// and assigns it to AprilTagController so tags are visualized without a prefab asset.
/// </summary>
public class TagVisualizations : MonoBehaviour
{
    [Header("Auto Generation")]
    [Tooltip("Automatically generate and assign visualization template on Start")]
    [SerializeField]
    private bool autoGenerateOnStart = true;

    [Tooltip("Name for the generated visualization template GameObject")]
    [SerializeField]
    private string templateName = "RuntimeTagVizTemplate";

    [Header("Template Appearance")]
    [Tooltip("Base color of the tag body")]
    [SerializeField]
    private Color bodyColor = new Color(1.0f, 0.0f, 0.0f, 0.5f); // flat red, 50% alpha

    [Tooltip("Add XYZ axes gizmos to the visualization")]
    [SerializeField]
    private bool addAxes = false;

    [Tooltip("Relative length of axis gizmos (scaled by tag size later)")]
    [SerializeField]
    private float axisLength = 0.5f;

    private void Start()
    {
        // Ensure bodyColor has transparency
        if (bodyColor.a >= 1f)
        {
            Debug.LogWarning(
                $"[Visualizations] Body color alpha was {bodyColor.a}, setting to 0.5 for transparency"
            );
            bodyColor = new Color(bodyColor.r, bodyColor.g, bodyColor.b, 0.5f);
        }

        if (autoGenerateOnStart)
        {
            GenerateAndAssign();
        }
    }

    /// <summary>
    /// Generates the visualization template and assigns it to AprilTagController (m_tagVizPrefab).
    /// Also generates and assigns an anchor prefab to the SpatialAnchorSpawnerBuildingBlock.
    /// </summary>
    public void GenerateAndAssign()
    {
        var controller = FindFirstObjectByType<AprilTagController>();
        if (controller == null)
        {
            Debug.LogWarning("[Visualizations] No AprilTagController found in scene.");
            return;
        }

        // Build a simple tag visualization (root + body + optional axes)
        var template = BuildSimpleTagVisualization();
        template.name = templateName;

        // Keep template hidden; controller will Instantiate() it per tag
        template.SetActive(false);

        // Assign to controller's private prefab field via reflection
        var prefabField = typeof(AprilTagController).GetField(
            "m_tagVizPrefab",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        if (prefabField == null)
        {
            Debug.LogError("[Visualizations] Could not find m_tagVizPrefab on AprilTagController.");
            Destroy(template);
            return;
        }

        prefabField.SetValue(controller, template);
        Debug.Log(
            $"[Visualizations] Assigned runtime visualization template '{template.name}' to AprilTagController."
        );

        // Also generate and assign anchor prefab to the building block
        GenerateAndAssignAnchorPrefab();
    }

    /// <summary>
    /// Generates an anchor prefab similar to DemoAnchorPlacementBuildingBlock and assigns it
    /// to the SpatialAnchorSpawnerBuildingBlock
    /// </summary>
    private void GenerateAndAssignAnchorPrefab()
    {
        // Find the spatial anchor spawner building block
        var spawner =
            FindFirstObjectByType<Meta.XR.BuildingBlocks.SpatialAnchorSpawnerBuildingBlock>();
        if (spawner == null)
        {
            Debug.LogWarning(
                "[Visualizations] No SpatialAnchorSpawnerBuildingBlock found in scene."
            );
            return;
        }

        // Create anchor prefab: root with a simple cube visualization
        var anchorPrefab = new GameObject("RuntimeAnchorPrefab");

        // Add a cube child for visualization
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Visualization";
        cube.transform.SetParent(anchorPrefab.transform, false);
        cube.transform.localPosition = Vector3.zero;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f); // 10cm cube

        // Keep collider for controller interaction (AprilTagAnchorInteraction uses Physics.Raycast)
        // The collider allows the anchor to be selected, grabbed, and manipulated with controllers

        // Create a material with semi-transparent cyan color
        var renderer = cube.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            // Try to find a suitable transparent shader
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            var mat = new Material(shader);

            // Set color: cyan with 50% transparency
            Color anchorColor = new Color(0f, 1f, 1f, 0.5f); // cyan

            // For Standard shader, explicitly set to transparent mode
            if (shader.name.Contains("Standard"))
            {
                mat.SetFloat("_Mode", 3); // 3 = Transparent
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Glossiness", 0.3f);
            }

            // Set blend mode for transparency
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // Apply color
            mat.color = anchorColor;
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", anchorColor);
            }

            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // Keep prefab hidden; spawner will instantiate it
        anchorPrefab.SetActive(false);

        // Assign to spawner
        spawner.AnchorPrefab = anchorPrefab;

        Debug.Log(
            $"[Visualizations] Generated and assigned runtime anchor prefab to SpatialAnchorSpawnerBuildingBlock"
        );
    }

    private GameObject BuildSimpleTagVisualization()
    {
        var root = new GameObject("TagViz");

        // Body: flat quad (no thickness)
        var body = GameObject.CreatePrimitive(PrimitiveType.Quad);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = new Vector3(1f, 1f, 1f); // flat; scaled later by controller

        // Keep collider for anchor interaction (when visualization is parented to anchor)
        // The MeshCollider on the quad allows controller raycasts to detect it
        // This is needed for AprilTagAnchorInteraction to grab/move anchors

        var bodyRenderer = body.GetComponent<MeshRenderer>();
        if (bodyRenderer != null)
        {
            // Priority order: shaders that definitely support transparency
            Shader shader = null;
            string[] shaderNames =
            {
                "Sprites/Default", // This definitely supports transparency
                "Unlit/Transparent",
                "Legacy Shaders/Transparent/Diffuse",
                "Mobile/Particles/Alpha Blended",
                "UI/Default", // UI shaders always support transparency
                "Unlit/Color",
            };

            foreach (var shaderName in shaderNames)
            {
                shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    Debug.Log($"[Visualizations] Found shader: {shaderName}");
                    break;
                }
            }

            // If no shader found, create a basic transparent one
            if (shader == null)
            {
                Debug.LogWarning(
                    "[Visualizations] No suitable shader found, using Standard shader"
                );
                shader = Shader.Find("Standard");
            }

            // Create material
            var mat = new Material(shader);

            // For Standard shader, explicitly set to transparent mode
            if (shader.name.Contains("Standard"))
            {
                mat.SetFloat("_Mode", 3); // 3 = Transparent
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Glossiness", 0f);
            }

            // Set blend mode for transparency
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // Apply color with all possible property names
            mat.color = bodyColor;
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", bodyColor);
            }
            if (mat.HasProperty("_TintColor"))
            {
                mat.SetColor("_TintColor", bodyColor);
            }
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", bodyColor);
            }

            // For particle/sprite shaders
            if (mat.HasProperty("_MainTex"))
            {
                mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            }

            bodyRenderer.material = mat;

            // Configure renderer for transparency
            bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;

            // Force material to update
            bodyRenderer.enabled = false;
            bodyRenderer.enabled = true;

            // Extensive debugging
            Debug.Log($"[Visualizations] Material setup complete:");
            Debug.Log($"  Shader: {shader.name}");
            Debug.Log($"  Color: RGBA({bodyColor.r}, {bodyColor.g}, {bodyColor.b}, {bodyColor.a})");
            Debug.Log(
                $"  Material color: RGBA({mat.color.r}, {mat.color.g}, {mat.color.b}, {mat.color.a})"
            );
            Debug.Log($"  Render queue: {mat.renderQueue}");
            Debug.Log($"  Keywords: {string.Join(", ", mat.shaderKeywords)}");
        }

        if (addAxes)
        {
            AddAxis(root.transform, Color.red, Vector3.right, new Vector3(0.5f, 0f, 0f), 90f);
            AddAxis(root.transform, Color.green, Vector3.up, new Vector3(0f, 0.5f, 0f), 0f);
            AddAxis(
                root.transform,
                Color.blue,
                Vector3.forward,
                new Vector3(0f, 0f, 0.5f),
                0f,
                true
            );
        }

        return root;
    }

    private void AddAxis(
        Transform parent,
        Color color,
        Vector3 axisDir,
        Vector3 localEndPos,
        float xRot,
        bool zAxis = false
    )
    {
        // Cylinder points up (Y); rotate for X/Z as needed
        var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cyl.name = zAxis ? "Axis_Z" : (axisDir == Vector3.right ? "Axis_X" : "Axis_Y");
        cyl.transform.SetParent(parent, false);

        // Position cylinder between origin and end point
        var half = localEndPos * axisLength;
        var length = half.magnitude * 2f;

        cyl.transform.localPosition = half;
        cyl.transform.localScale = new Vector3(0.04f, length * 0.5f, 0.04f); // radius, half-height, radius

        // Orientation
        if (axisDir == Vector3.right)
        {
            cyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        else if (zAxis)
        {
            cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        else
        {
            cyl.transform.localRotation = Quaternion.identity;
        }

        var renderer = cyl.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            var mat = new Material(renderer.sharedMaterial);
            mat.color = color;
            renderer.material = mat;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // Prevent duplicates across scene reloads
        if (FindFirstObjectByType<TagVisualizations>() != null)
            return;

        var host = new GameObject("TagVisualizations_Auto");
        DontDestroyOnLoad(host);
        host.AddComponent<TagVisualizations>();
    }
}
