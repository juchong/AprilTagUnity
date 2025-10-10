// Assets/AprilTag/Scripts/AprilTagAnchorInteractionSetup.cs
// Helper script to automatically set up AprilTag anchor interaction system

using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Automatically sets up the AprilTag anchor interaction system in the scene.
    /// Attach this to any GameObject to enable controller-based anchor manipulation.
    /// </summary>
    [AddComponentMenu("AprilTag/AprilTag Anchor Interaction Setup")]
    public class AprilTagAnchorInteractionSetup : MonoBehaviour
    {
        [Header("Auto-Setup Configuration")]
        [Tooltip("Automatically create interaction component on Start")]
        [SerializeField]
        private bool m_autoSetup = true;

        [Tooltip("GameObject to attach interaction component to (null = this GameObject)")]
        [SerializeField]
        private GameObject m_targetObject;

        [Header("Interaction Settings")]
        [Tooltip("Which controller hand to use for interactions")]
        [SerializeField]
        private AprilTagAnchorInteraction.ControllerHand m_activeHand = AprilTagAnchorInteraction
            .ControllerHand
            .Right;

        [Tooltip("Maximum ray distance for anchor selection")]
        [SerializeField]
        private float m_maxRayDistance = 10f;

        [Tooltip("Enable rotation control with thumbstick while grabbing")]
        [SerializeField]
        private bool m_enableRotationControl = true;

        [Tooltip("Show visual ray from controllers")]
        [SerializeField]
        private bool m_showRayVisual = true;

        private void Start()
        {
            if (m_autoSetup)
            {
                SetupAnchorInteraction();
            }
        }

        /// <summary>
        /// Set up the anchor interaction system
        /// </summary>
        public void SetupAnchorInteraction()
        {
            var target = m_targetObject != null ? m_targetObject : gameObject;

            // Check if interaction component already exists
            var existingInteraction = target.GetComponent<AprilTagAnchorInteraction>();
            if (existingInteraction != null)
            {
                Debug.Log(
                    "[AprilTagAnchorInteractionSetup] Interaction component already exists, configuring..."
                );
                ConfigureInteraction(existingInteraction);
                return;
            }

            // Add interaction component
            var interaction = target.AddComponent<AprilTagAnchorInteraction>();
            ConfigureInteraction(interaction);

            Debug.Log(
                $"[AprilTagAnchorInteractionSetup] Successfully set up anchor interaction on {target.name}"
            );
        }

        private void ConfigureInteraction(AprilTagAnchorInteraction interaction)
        {
            // Use reflection to set private serialized fields
            var type = typeof(AprilTagAnchorInteraction);

            var activeHandField = type.GetField(
                "m_activeHand",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            activeHandField?.SetValue(interaction, m_activeHand);

            var maxRayDistanceField = type.GetField(
                "m_maxRayDistance",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            maxRayDistanceField?.SetValue(interaction, m_maxRayDistance);

            var enableRotationField = type.GetField(
                "m_enableRotationControl",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            enableRotationField?.SetValue(interaction, m_enableRotationControl);

            var showRayField = type.GetField(
                "m_showRayVisual",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );
            showRayField?.SetValue(interaction, m_showRayVisual);

            Debug.Log("[AprilTagAnchorInteractionSetup] Configured interaction settings");
        }

        [ContextMenu("Setup Now")]
        private void SetupNow()
        {
            SetupAnchorInteraction();
        }
    }
}

