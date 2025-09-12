// Copyright (c) Meta Platforms, Inc. and affiliates.

using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Simple script that automatically fixes Input System issues when added to any GameObject in a scene.
    /// Just add this component to any GameObject and it will fix EventSystem input module issues.
    /// </summary>
    public class AutoInputSystemFixer : MonoBehaviour
    {
        [Header("Auto Input System Fixer")]
        [Tooltip("Automatically fixes EventSystem input modules to work with Input System package")]
        [SerializeField] private bool autoFixOnStart = true;
        
        [Tooltip("Fix EventSystems every frame (useful for dynamically created EventSystems)")]
        [SerializeField] private bool fixEveryFrame = false;

        private void Start()
        {
            if (autoFixOnStart)
            {
                InputSystemFixer.FixAllEventSystems();
            }
        }

        private void Update()
        {
            if (fixEveryFrame)
            {
                InputSystemFixer.FixAllEventSystems();
            }
        }

        private void OnEnable()
        {
            // Fix when this component is enabled
            InputSystemFixer.FixAllEventSystems();
        }

        // Public method to manually trigger the fix
        [ContextMenu("Fix Input System Now")]
        public void FixNow()
        {
            InputSystemFixer.FixAllEventSystems();
        }
    }
}
