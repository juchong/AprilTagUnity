// Copyright (c) Meta Platforms, Inc. and affiliates.

using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

namespace AprilTag
{
    /// <summary>
    /// Automatically fixes EventSystems to use the correct Input Module based on the active Input System.
    /// This prevents the InvalidOperationException when Input System package is active but EventSystems
    /// are still using StandaloneInputModule.
    /// </summary>
    [DefaultExecutionOrder(-1000)] // Run early to fix EventSystems before they cause errors
    public class InputSystemFixer : MonoBehaviour
    {
        private static bool _hasFixed = false;

        private void Awake()
        {
            // Only fix once per application session
            if (!_hasFixed)
            {
                FixEventSystems();
                _hasFixed = true;
            }
        }

        private void Start()
        {
            // Also fix EventSystems in case they're created after Awake
            FixEventSystems();
        }

        private void OnEnable()
        {
            // Fix EventSystems whenever this component is enabled
            FixEventSystems();
        }

        private void FixEventSystems()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            // Find all EventSystems in the scene
            EventSystem[] eventSystems = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            
            foreach (EventSystem eventSystem in eventSystems)
            {
                // Check if it has a StandaloneInputModule
                StandaloneInputModule legacyInputModule = eventSystem.GetComponent<StandaloneInputModule>();
                if (legacyInputModule != null)
                {
                    Debug.Log($"[InputSystemFixer] Replacing StandaloneInputModule with InputSystemUIInputModule on {eventSystem.name}");
                    
                    // Remove the legacy input module
                    DestroyImmediate(legacyInputModule);
                    
                    // Add the new input system UI input module
                    eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                }
                else
                {
                    // Check if it already has InputSystemUIInputModule
                    InputSystemUIInputModule inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();
                    if (inputSystemModule == null)
                    {
                        Debug.Log($"[InputSystemFixer] Adding InputSystemUIInputModule to {eventSystem.name}");
                        eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                    }
                }
            }
#else
            Debug.Log("[InputSystemFixer] Legacy Input Manager is active, no fixes needed");
#endif
        }

        // Static method to fix EventSystems from anywhere
        public static void FixAllEventSystems()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            EventSystem[] eventSystems = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            
            foreach (EventSystem eventSystem in eventSystems)
            {
                StandaloneInputModule legacyInputModule = eventSystem.GetComponent<StandaloneInputModule>();
                if (legacyInputModule != null)
                {
                    Debug.Log($"[InputSystemFixer] Replacing StandaloneInputModule with InputSystemUIInputModule on {eventSystem.name}");
                    DestroyImmediate(legacyInputModule);
                    eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                }
            }
#endif
        }
    }
}
