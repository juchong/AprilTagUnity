// Assets/AprilTag/Scripts/AprilTagVisualizations.cs

using System.Reflection;
using UnityEngine;

/// <summary>
/// Generates a runtime visualization template equivalent to SimpleTagVizPrefab
/// and assigns it to AprilTagController so tags are visualized without a prefab asset.
/// </summary>
public class AprilTagVisualizations : MonoBehaviour
{
    [Header("Auto Generation")]
    [Tooltip("Automatically generate and assign visualization template on Start")] 
    [SerializeField] private bool autoGenerateOnStart = true;

    [Tooltip("Name for the generated visualization template GameObject")] 
    [SerializeField] private string templateName = "RuntimeTagVizTemplate";

    [Header("Template Appearance")]
    [Tooltip("Base color of the tag body")] 
    [SerializeField] private Color bodyColor = new Color(0.2f, 0.8f, 1.0f, 0.35f);

    [Tooltip("Add XYZ axes gizmos to the visualization")] 
    [SerializeField] private bool addAxes = true;

    [Tooltip("Relative length of axis gizmos (scaled by tag size later)")] 
    [SerializeField] private float axisLength = 0.5f;

    private void Start()
    {
        if (autoGenerateOnStart)
        {
            GenerateAndAssign();
        }
    }

    /// <summary>
    /// Generates the visualization template and assigns it to AprilTagController (m_tagVizPrefab).
    /// </summary>
    public void GenerateAndAssign()
    {
        var controller = FindFirstObjectByType<AprilTagController>();
        if (controller == null)
        {
            Debug.LogWarning("[AprilTagVisualizations] No AprilTagController found in scene.");
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
            Debug.LogError("[AprilTagVisualizations] Could not find m_tagVizPrefab on AprilTagController.");
            Destroy(template);
            return;
        }

        prefabField.SetValue(controller, template);
        Debug.Log($"[AprilTagVisualizations] Assigned runtime visualization template '{template.name}' to AprilTagController.");
    }

    private GameObject BuildSimpleTagVisualization()
    {
        var root = new GameObject("TagViz");

        // Body: thin quad-like plate (uses Cube to avoid asset dependencies)
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = new Vector3(1f, 1f, 0.02f); // thin plate; scaled later by controller

        var bodyRenderer = body.GetComponent<MeshRenderer>();
        if (bodyRenderer != null)
        {
            // Use an instance material to avoid affecting shared material
            var mat = new Material(bodyRenderer.sharedMaterial);
            mat.color = bodyColor;
            bodyRenderer.material = mat;
        }

        if (addAxes)
        {
            AddAxis(root.transform, Color.red, Vector3.right, new Vector3(0.5f, 0f, 0f), 90f);
            AddAxis(root.transform, Color.green, Vector3.up, new Vector3(0f, 0.5f, 0f), 0f);
            AddAxis(root.transform, Color.blue, Vector3.forward, new Vector3(0f, 0f, 0.5f), 0f, true);
        }

        return root;
    }

    private void AddAxis(Transform parent, Color color, Vector3 axisDir, Vector3 localEndPos, float xRot, bool zAxis = false)
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
        if (FindFirstObjectByType<AprilTagVisualizations>() != null)
            return;

        var host = new GameObject("AprilTagVisualizations_Auto");
        DontDestroyOnLoad(host);
        host.AddComponent<AprilTagVisualizations>();
    }
}


