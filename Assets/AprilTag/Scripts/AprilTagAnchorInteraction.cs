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
    /// </summary>
    public class AprilTagAnchorInteraction : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Spatial anchor manager to interface with")]
        [SerializeField]
        private AprilTagSpatialAnchorManager m_spatialAnchorManager;

        [Tooltip("AprilTag controller for debug logging")]
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

        [Tooltip("Grab color for grabbed anchors")]
        [SerializeField]
        private Color m_grabColor = new Color(0f, 1f, 0f, 0.5f);

        [Header("Grab Settings")]
        [Tooltip("Distance to hold grabbed anchor from controller")]
        [SerializeField]
        private float m_grabDistance = 0.5f;

        [Tooltip("Enable rotation control with thumbstick while grabbed")]
        [SerializeField]
        private bool m_enableRotationControl = true;

        [Tooltip("Rotation speed (degrees per second per thumbstick unit)")]
        [SerializeField]
        private float m_rotationSpeed = 90f;

        [Tooltip("Smooth movement damping for grabbed anchors")]
        [SerializeField]
        private float m_movementDamping = 10f;

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
        private GameObject m_grabbedAnchor;
        private Transform m_grabbedAnchorTransform;
        private Vector3 m_grabOffset;
        private Quaternion m_grabRotationOffset;
        private float m_grabStartDistance;
        private OVRSpatialAnchor m_grabbedSpatialAnchor;
        private int m_grabbedTagId = -1;

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
            // Auto-find references if not assigned
            if (m_spatialAnchorManager == null)
            {
                m_spatialAnchorManager = FindFirstObjectByType<AprilTagSpatialAnchorManager>();
                if (m_spatialAnchorManager == null)
                {
                    Debug.LogError(
                        "[AprilTagAnchorInteraction] No AprilTagSpatialAnchorManager found in scene!"
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

            if (EnableDebugLogging)
            {
                Debug.Log(
                    "[AprilTagAnchorInteraction] Initialized - Ready for anchor manipulation"
                );
            }
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
                        $"[AprilTagAnchorInteraction] Found controller transforms - Left: {m_leftControllerTransform != null}, Right: {m_rightControllerTransform != null}"
                    );
                }
            }
            else
            {
                Debug.LogWarning(
                    "[AprilTagAnchorInteraction] OVRCameraRig not found - controller tracking may not work properly"
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

            // Check if grabbing
            var triggerPressed = OVRInput.Get(triggerButton, controller);
            var triggerDown = OVRInput.GetDown(triggerButton, controller);
            var triggerUp = OVRInput.GetUp(triggerButton, controller);

            // Handle grab/release
            if (m_grabbedAnchor != null)
            {
                // Currently grabbing - update position
                UpdateGrabbedAnchor(controllerTransform, controller);

                // Release on trigger up
                if (triggerUp)
                {
                    ReleaseAnchor();
                }
            }
            else
            {
                // Not grabbing - check for ray hits and grab on trigger down
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

                        // Grab on trigger press
                        if (triggerDown)
                        {
                            GrabAnchor(hitAnchor, spatialAnchor, controllerTransform, hit.point);
                        }
                    }
                }
                else
                {
                    // No hit - clear highlight
                    ClearHighlight();
                }
            }
        }

        private void ProcessGlobalActions()
        {
            // B button (right controller) - Clear anchors in view
            if (OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch))
            {
                ClearAnchorsInView();
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

        private void GrabAnchor(
            GameObject anchor,
            OVRSpatialAnchor spatialAnchor,
            Transform controller,
            Vector3 hitPoint
        )
        {
            m_grabbedAnchor = anchor;
            m_grabbedAnchorTransform = spatialAnchor.transform;
            m_grabbedSpatialAnchor = spatialAnchor;

            // Calculate grab offset
            m_grabOffset = m_grabbedAnchorTransform.position - controller.position;
            m_grabRotationOffset =
                Quaternion.Inverse(controller.rotation) * m_grabbedAnchorTransform.rotation;
            m_grabStartDistance = Vector3.Distance(
                controller.position,
                m_grabbedAnchorTransform.position
            );

            // Get tag ID from anchor
            m_grabbedTagId = m_spatialAnchorManager.GetTagIdForAnchor(spatialAnchor);

            // Change to grab color
            ClearHighlight();
            HighlightAnchor(anchor);
            if (m_highlightMaterials.ContainsKey(anchor))
            {
                foreach (var mat in m_highlightMaterials[anchor])
                {
                    mat.color = m_grabColor;
                }
            }

            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagAnchorInteraction] Grabbed anchor for tag {m_grabbedTagId} at {m_grabbedAnchorTransform.position}"
                );
            }

            // Haptic feedback
            OVRInput.SetControllerVibration(0.5f, 0.1f, GetControllerFromTransform(controller));
        }

        private void UpdateGrabbedAnchor(Transform controller, OVRInput.Controller controllerType)
        {
            if (m_grabbedAnchorTransform == null)
                return;

            // Calculate target position (smooth follow)
            var targetPosition = controller.position + controller.forward * m_grabDistance;
            m_grabbedAnchorTransform.position = Vector3.Lerp(
                m_grabbedAnchorTransform.position,
                targetPosition,
                Time.deltaTime * m_movementDamping
            );

            // Handle rotation with thumbstick
            if (m_enableRotationControl)
            {
                var thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controllerType);
                if (thumbstick.magnitude > 0.1f)
                {
                    // Rotate around Y axis (yaw) with horizontal thumbstick
                    var yawRotation = Quaternion.Euler(
                        0f,
                        thumbstick.x * m_rotationSpeed * Time.deltaTime,
                        0f
                    );
                    m_grabbedAnchorTransform.rotation =
                        yawRotation * m_grabbedAnchorTransform.rotation;

                    // Rotate around X axis (pitch) with vertical thumbstick
                    var pitchRotation = Quaternion.Euler(
                        -thumbstick.y * m_rotationSpeed * Time.deltaTime,
                        0f,
                        0f
                    );
                    m_grabbedAnchorTransform.rotation =
                        m_grabbedAnchorTransform.rotation * pitchRotation;
                }
            }
        }

        private async void ReleaseAnchor()
        {
            if (m_grabbedAnchor == null || m_grabbedSpatialAnchor == null)
                return;

            var finalPosition = m_grabbedAnchorTransform.position;
            var finalRotation = m_grabbedAnchorTransform.rotation;

            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagAnchorInteraction] Releasing anchor for tag {m_grabbedTagId} at {finalPosition}, rotation {finalRotation.eulerAngles}"
                );
            }

            // Update anchor position in Meta API (save to persistent storage)
            try
            {
                // The anchor transform is already updated in world space
                // Now we need to save it to Meta's persistent storage
                var saveResult = await m_grabbedSpatialAnchor.SaveAnchorAsync();

                if (saveResult.Success)
                {
                    if (EnableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagAnchorInteraction] Successfully saved anchor for tag {m_grabbedTagId} to Meta storage at position {finalPosition}"
                        );
                    }

                    // Update UUID mapping in PlayerPrefs (in case it changed)
                    m_spatialAnchorManager.UpdateAnchorMapping(
                        m_grabbedTagId,
                        m_grabbedSpatialAnchor
                    );
                }
                else
                {
                    Debug.LogError(
                        $"[AprilTagAnchorInteraction] Failed to save anchor for tag {m_grabbedTagId} to Meta storage: {saveResult.Status}"
                    );
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"[AprilTagAnchorInteraction] Exception saving anchor: {ex.Message}"
                );
            }

            // Haptic feedback
            OVRInput.SetControllerVibration(0.3f, 0.1f);

            // Clear grab state
            ClearHighlight();
            m_grabbedAnchor = null;
            m_grabbedAnchorTransform = null;
            m_grabbedSpatialAnchor = null;
            m_grabbedTagId = -1;
        }

        private void ClearAnchorsInView()
        {
            if (m_spatialAnchorManager == null)
                return;

            // Get all anchors visible from the active controller
            var controllerTransform =
                m_activeHand == ControllerHand.Left
                    ? m_leftControllerTransform
                    : m_rightControllerTransform;
            if (controllerTransform == null)
                return;

            var anchorsInView = new List<OVRSpatialAnchor>();
            var camera = Camera.main;
            if (camera == null)
                return;

            // Get all anchors
            var allAnchors = m_spatialAnchorManager.GetAllAnchors();

            // Check which are in camera view
            foreach (var anchor in allAnchors)
            {
                if (anchor == null)
                    continue;

                var viewportPoint = camera.WorldToViewportPoint(anchor.transform.position);
                if (
                    viewportPoint.z > 0
                    && viewportPoint.x >= 0
                    && viewportPoint.x <= 1
                    && viewportPoint.y >= 0
                    && viewportPoint.y <= 1
                )
                {
                    anchorsInView.Add(anchor);
                }
            }

            if (anchorsInView.Count > 0)
            {
                Debug.Log(
                    $"[AprilTagAnchorInteraction] Clearing {anchorsInView.Count} anchors in view"
                );

                foreach (var anchor in anchorsInView)
                {
                    m_spatialAnchorManager.EraseAnchor(anchor);
                }

                // Haptic feedback
                OVRInput.SetControllerVibration(1f, 0.2f);
            }
            else
            {
                Debug.Log("[AprilTagAnchorInteraction] No anchors in view to clear");
            }
        }

        private void ClearAllAnchors()
        {
            if (m_spatialAnchorManager == null)
                return;

            Debug.Log("[AprilTagAnchorInteraction] Clearing ALL anchors");

            m_spatialAnchorManager.EraseAllAnchors();

            // Strong haptic feedback
            OVRInput.SetControllerVibration(1f, 0.3f);
        }

        private OVRInput.Controller GetControllerFromTransform(Transform controller)
        {
            if (controller == m_leftControllerTransform)
                return OVRInput.Controller.LTouch;
            if (controller == m_rightControllerTransform)
                return OVRInput.Controller.RTouch;
            return OVRInput.Controller.None;
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
