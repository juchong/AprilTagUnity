// Assets/AprilTag/Scripts/AprilTagAnchorInteraction.cs
// Controller-based ray interaction system for manipulating AprilTag spatial anchors
// Allows grabbing, moving, rotating, and deleting anchors using Quest controllers

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Handles controller-based ray interactions for AprilTag spatial anchor manipulation.
    /// Features:
    /// - Ray-based highlighting and selection
    /// - Grab/move/rotate anchors with trigger
    /// - Delete anchors in view with B button
    /// - Clear all anchors with thumbstick button
    /// - Auto-setup on Start (finds required components automatically)
    /// </summary>
    [AddComponentMenu("AprilTag/AprilTag Anchor Interaction")]
    public class AprilTagAnchorInteraction : MonoBehaviour
    {
        [Header("Auto-Setup")]
        [Tooltip("Automatically find required components on Start")]
        [SerializeField]
        private bool m_autoSetup = true;

        [Header("References")]
        [Tooltip("Spatial anchor manager to interface with (auto-found if null)")]
        [SerializeField]
        private AprilTagSpatialAnchorManager m_spatialAnchorManager;

        [Tooltip("AprilTag controller for debug logging (auto-found if null)")]
        [SerializeField]
        private AprilTagController m_aprilTagController;

        [Header("Ray Interaction Settings")]
        [Tooltip("Maximum distance for ray interaction (meters)")]
        [SerializeField]
        private float m_maxRayDistance = 10f;

        [Tooltip("Layer mask for anchor raycast detection")]
        [SerializeField]
        private LayerMask m_anchorLayerMask = ~0;

        [Tooltip("Highlight color for hovered anchors")]
        [SerializeField]
        private Color m_highlightColor = new Color(1f, 1f, 0f, 0.5f);

        [Header("Visual Feedback")]
        [Tooltip("Enable ray visualization (line renderer)")]
        [SerializeField]
        private bool m_showRayVisual = true;

        [Tooltip("Ray line color")]
        [SerializeField]
        private Color m_rayColor = new Color(1f, 1f, 1f, 0.3f);

        [Tooltip("Ray line width")]
        [SerializeField]
        private float m_rayWidth = 0.005f;

        [Header("Controller Selection")]
        [Tooltip("Which controller to use for interactions")]
        [SerializeField]
        private ControllerHand m_activeHand = ControllerHand.Right;

        public enum ControllerHand
        {
            Left,
            Right,
            Both,
        }

        // Ray interaction state
        private GameObject m_hoveredAnchor;

        // Original materials for highlighting
        private readonly Dictionary<GameObject, Material[]> m_originalMaterials = new();
        private readonly Dictionary<GameObject, Material[]> m_highlightMaterials = new();

        // Line renderers for ray visualization
        private LineRenderer m_leftRayLine;
        private LineRenderer m_rightRayLine;

        // Controller transforms
        private Transform m_leftControllerTransform;
        private Transform m_rightControllerTransform;

        private bool EnableDebugLogging =>
            m_aprilTagController != null && m_aprilTagController.EnableAllDebugLogging;

        private void Start()
        {
            if (m_autoSetup)
            {
                SetupComponents();
            }

            if (EnableDebugLogging)
            {
                Debug.Log("[AnchorInteraction] Initialized - Ready for anchor manipulation");
            }
        }

        /// <summary>
        /// Auto-setup: Find all required components and initialize
        /// </summary>
        private void SetupComponents()
        {
            // Auto-find references if not assigned
            if (m_spatialAnchorManager == null)
            {
                m_spatialAnchorManager = FindFirstObjectByType<AprilTagSpatialAnchorManager>();
                if (m_spatialAnchorManager == null)
                {
                    Debug.LogError(
                        "[AnchorInteraction] No AprilTagSpatialAnchorManager found in scene!"
                    );
                    enabled = false;
                    return;
                }
            }

            if (m_aprilTagController == null)
            {
                m_aprilTagController = FindFirstObjectByType<AprilTagController>();
            }

            // Find controller transforms
            FindControllerTransforms();

            // Create ray visualizers
            if (m_showRayVisual)
            {
                CreateRayVisualizers();
            }
        }

        /// <summary>
        /// Context menu command to manually trigger setup
        /// </summary>
        [ContextMenu("Setup Anchor Interaction")]
        private void SetupNow()
        {
            SetupComponents();
            Debug.Log("[AnchorInteraction] Manual setup complete");
        }

        private void FindControllerTransforms()
        {
            // Try to find OVR camera rig
            var ovrCameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (ovrCameraRig != null)
            {
                m_leftControllerTransform = ovrCameraRig.leftControllerAnchor;
                m_rightControllerTransform = ovrCameraRig.rightControllerAnchor;

                if (EnableDebugLogging)
                {
                    Debug.Log(
                        $"[AnchorInteraction] Found controller transforms - Left: {m_leftControllerTransform != null}, Right: {m_rightControllerTransform != null}"
                    );
                }
            }
            else
            {
                Debug.LogWarning(
                    "[AnchorInteraction] OVRCameraRig not found - controller tracking may not work properly"
                );
            }
        }

        private void CreateRayVisualizers()
        {
            if (m_leftControllerTransform != null)
            {
                m_leftRayLine = CreateRayLine(m_leftControllerTransform, "LeftRayVisualizer");
            }

            if (m_rightControllerTransform != null)
            {
                m_rightRayLine = CreateRayLine(m_rightControllerTransform, "RightRayVisualizer");
            }
        }

        private LineRenderer CreateRayLine(Transform parent, string name)
        {
            var rayObj = new GameObject(name);
            rayObj.transform.SetParent(parent);
            rayObj.transform.localPosition = Vector3.zero;
            rayObj.transform.localRotation = Quaternion.identity;

            var lineRenderer = rayObj.AddComponent<LineRenderer>();
            lineRenderer.startWidth = m_rayWidth;
            lineRenderer.endWidth = m_rayWidth;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = m_rayColor;
            lineRenderer.endColor = m_rayColor;
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;

            return lineRenderer;
        }

        private void Update()
        {
            // Process left controller
            if (m_activeHand == ControllerHand.Left || m_activeHand == ControllerHand.Both)
            {
                ProcessController(
                    m_leftControllerTransform,
                    OVRInput.Controller.LTouch,
                    OVRInput.RawButton.LIndexTrigger,
                    m_leftRayLine
                );
            }

            // Process right controller
            if (m_activeHand == ControllerHand.Right || m_activeHand == ControllerHand.Both)
            {
                ProcessController(
                    m_rightControllerTransform,
                    OVRInput.Controller.RTouch,
                    OVRInput.RawButton.RIndexTrigger,
                    m_rightRayLine
                );
            }

            // Global actions (only process once, not per controller)
            ProcessGlobalActions();
        }

        private void ProcessController(
            Transform controllerTransform,
            OVRInput.Controller controller,
            OVRInput.RawButton triggerButton,
            LineRenderer rayLine
        )
        {
            if (controllerTransform == null)
                return;

            // Get ray from controller
            var rayOrigin = controllerTransform.position;
            var rayDirection = controllerTransform.forward;

            // Update ray visualization
            if (rayLine != null && m_showRayVisual)
            {
                rayLine.SetPosition(0, rayOrigin);
                rayLine.SetPosition(1, rayOrigin + rayDirection * m_maxRayDistance);
            }

            // Check for ray hits to highlight anchors
            if (
                Physics.Raycast(
                    rayOrigin,
                    rayDirection,
                    out var hit,
                    m_maxRayDistance,
                    m_anchorLayerMask
                )
            )
            {
                var hitAnchor = hit.collider.gameObject;

                // Check if this is an AprilTag anchor (has OVRSpatialAnchor in parent hierarchy)
                var spatialAnchor = hitAnchor.GetComponentInParent<OVRSpatialAnchor>();
                if (spatialAnchor != null)
                {
                    // Highlight hovered anchor
                    if (m_hoveredAnchor != hitAnchor)
                    {
                        ClearHighlight();
                        HighlightAnchor(hitAnchor);
                    }
                }
            }
            else
            {
                // No hit - clear highlight
                ClearHighlight();
            }
        }

        private void ProcessGlobalActions()
        {
            // B button (right controller) - Delete currently highlighted anchor
            if (OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch))
            {
                DeleteHighlightedAnchor();
            }

            // Thumbstick button (right controller) - Clear all anchors
            if (OVRInput.GetDown(OVRInput.RawButton.RThumbstick, OVRInput.Controller.RTouch))
            {
                ClearAllAnchors();
            }
        }

        private void HighlightAnchor(GameObject anchor)
        {
            m_hoveredAnchor = anchor;

            // Store and replace materials
            var renderers = anchor.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;

            var originalMats = new List<Material>();
            var highlightMats = new List<Material>();

            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    originalMats.Add(mat);

                    // Create highlight material
                    var highlightMat = new Material(mat);
                    highlightMat.color = m_highlightColor;
                    highlightMat.SetFloat("_Mode", 2); // Fade mode for transparency
                    highlightMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    highlightMat.SetInt(
                        "_DstBlend",
                        (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
                    );
                    highlightMat.SetInt("_ZWrite", 0);
                    highlightMat.DisableKeyword("_ALPHATEST_ON");
                    highlightMat.EnableKeyword("_ALPHABLEND_ON");
                    highlightMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    highlightMat.renderQueue = 3000;

                    highlightMats.Add(highlightMat);
                }

                renderer.materials = highlightMats.ToArray();
            }

            m_originalMaterials[anchor] = originalMats.ToArray();
            m_highlightMaterials[anchor] = highlightMats.ToArray();

            if (EnableDebugLogging)
            {
                Debug.Log($"[AnchorInteraction] Highlighted anchor: {anchor.name}");
            }
        }

        private void ClearHighlight()
        {
            if (m_hoveredAnchor != null && m_originalMaterials.ContainsKey(m_hoveredAnchor))
            {
                // Restore original materials
                var renderers = m_hoveredAnchor.GetComponentsInChildren<Renderer>();
                var originalMats = m_originalMaterials[m_hoveredAnchor];

                int matIndex = 0;
                foreach (var renderer in renderers)
                {
                    var mats = new Material[renderer.materials.Length];
                    for (int i = 0; i < mats.Length && matIndex < originalMats.Length; i++)
                    {
                        mats[i] = originalMats[matIndex++];
                    }
                    renderer.materials = mats;
                }

                m_originalMaterials.Remove(m_hoveredAnchor);
                m_highlightMaterials.Remove(m_hoveredAnchor);
            }

            m_hoveredAnchor = null;
        }


        private void DeleteHighlightedAnchor()
        {
            if (m_spatialAnchorManager == null)
                return;

            // Delete the currently highlighted anchor
            if (m_hoveredAnchor != null)
            {
                // Get the spatial anchor component
                var spatialAnchor = m_hoveredAnchor.GetComponentInParent<OVRSpatialAnchor>();
                if (spatialAnchor != null)
                {
                    var tagId = m_spatialAnchorManager.GetTagIdForAnchor(spatialAnchor);
                    
                    if (EnableDebugLogging)
                    {
                        Debug.Log($"[AnchorInteraction] Deleting highlighted anchor for tag {tagId}");
                    }

                    // Clear highlight first
                    ClearHighlight();

                    // Erase the anchor
                    m_spatialAnchorManager.EraseAnchor(spatialAnchor);

                    // Haptic feedback
                    OVRInput.SetControllerVibration(0.5f, 0.1f);
                }
            }
            else
            {
                if (EnableDebugLogging)
                {
                    Debug.Log("[AnchorInteraction] No anchor highlighted to delete");
                }
            }
        }

        private void ClearAllAnchors()
        {
            if (m_spatialAnchorManager == null)
                return;

            Debug.Log("[AnchorInteraction] Clearing ALL anchors");

            m_spatialAnchorManager.EraseAllAnchors();

            // Strong haptic feedback
            OVRInput.SetControllerVibration(1f, 0.3f);
        }


        private void OnDestroy()
        {
            // Clean up materials
            foreach (var kvp in m_highlightMaterials)
            {
                foreach (var mat in kvp.Value)
                {
                    if (mat != null)
                    {
                        Destroy(mat);
                    }
                }
            }

            m_originalMaterials.Clear();
            m_highlightMaterials.Clear();
        }
    }
}
