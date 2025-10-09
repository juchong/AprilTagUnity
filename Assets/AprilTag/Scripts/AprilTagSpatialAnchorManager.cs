// Assets/AprilTag/AprilTagSpatialAnchorManager.cs
// Spatial anchor management system for AprilTag detection with confidence-based placement
// Uses Meta XR Building Blocks SpatialAnchorCoreBuildingBlock

using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.BuildingBlocks;
using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Manages spatial anchors for detected AprilTags with confidence-based placement
    /// Only places anchors once per tag ID when confidence is high
    /// Uses Meta XR Building Blocks SpatialAnchorCoreBuildingBlock
    /// </summary>
    public class AprilTagSpatialAnchorManager : MonoBehaviour
    {
        [Header("Anchor Configuration")]
        [Tooltip("Enable automatic spatial anchor creation for detected tags")]
        [SerializeField]
        private bool m_enableSpatialAnchors = true;

        [Tooltip("Minimum confidence threshold for anchor placement (0.0 - 1.0)")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float m_minConfidenceThreshold = 0.3f; // Lowered for easier anchor placement

        [Tooltip("Number of consecutive high-confidence detections required before placing anchor")]
        [SerializeField]
        private int m_requiredStableFrames = 8; // Increased to allow pose smoothing to stabilize

        [Tooltip("Maximum time to wait for stable detection before giving up (seconds)")]
        [SerializeField]
        private float m_maxDetectionTimeout = 30f; // Increased for better success rate

        [Tooltip("Enable anchor persistence across app sessions")]
        [SerializeField]
        private bool m_persistAnchors = true;

        [Header("Spatial Anchor Core")]
        [Tooltip("Spatial Anchor Core building block (auto-found if null)")]
        [SerializeField]
        private SpatialAnchorCoreBuildingBlock m_spatialAnchorCore;

        [Tooltip("Prefab to use for spatial anchor creation")]
        [SerializeField]
        private GameObject m_anchorPrefab;

        [Header("Keep Out Zone")]
        [Tooltip("Enable keep out zone around tags to prevent duplicate anchor placement")]
        [SerializeField]
        private bool m_enableKeepOutZone = true; // Re-enabled with appropriate settings for 16.5cm tags

        [Tooltip(
            "Multiplier for keep out zone radius based on tag size (e.g., 0.3 = 0.3x tag size)"
        )]
        [Range(0.1f, 2.0f)]
        [SerializeField]
        private float m_keepOutZoneMultiplier = 0.3f; // Very small multiplier for 16.5cm tags

        [Tooltip("Minimum keep out zone radius in meters (prevents too small zones)")]
        [Range(0.01f, 0.5f)]
        [SerializeField]
        private float m_minKeepOutRadius = 0.02f; // 2cm minimum

        [Tooltip("Maximum keep out zone radius in meters (prevents too large zones)")]
        [Range(0.1f, 1.0f)]
        [SerializeField]
        private float m_maxKeepOutRadius = 0.1f; // 10cm maximum

        [Header("Quest Controller Input")]
        [Tooltip("Enable A button on right controller to clear all anchors")]
        [SerializeField]
        private bool m_enableClearAnchorsInput = true;

        [Header("Debug")]
        [Tooltip("Enable debug logging for anchor operations")]
        [SerializeField]
        private bool m_enableDebugLogging = true;

        // Core data structures
        private Dictionary<int, OVRSpatialAnchor> m_anchorsById = new();
        private Dictionary<int, AnchorPlacementState> m_placementStates = new();
        private Dictionary<int, Guid> m_anchorUuids = new(); // For persistence
        private Dictionary<int, Guid> m_pendingLoadData = new(); // For loading from storage

        // Keep out zone tracking
        private Dictionary<int, KeepOutZone> m_keepOutZones = new();

        // Quest controller input
        private OVRInput.Controller m_rightController = OVRInput.Controller.RTouch;
        private bool m_lastAButtonState = false;

        // Events
        public static event Action<int, OVRSpatialAnchor> OnAnchorCreated;
        public static event Action<int> OnAnchorRemoved;
        public static event Action OnAllAnchorsCleared;

        /// <summary>
        /// Tracks the placement state for a specific tag ID
        /// </summary>
        [Serializable]
        private class AnchorPlacementState
        {
            public int TagId;
            public int StableFrameCount;
            public float FirstDetectionTime;
            public float LastDetectionTime;
            public Vector3 LastPosition;
            public Quaternion LastRotation;
            public float LastConfidence;
            public bool IsPlaced;
            public bool IsPlacementInProgress;

            public AnchorPlacementState(int id)
            {
                TagId = id;
                StableFrameCount = 0;
                FirstDetectionTime = Time.time;
                LastDetectionTime = Time.time;
                LastPosition = Vector3.zero;
                LastRotation = Quaternion.identity;
                LastConfidence = 0f;
                IsPlaced = false;
                IsPlacementInProgress = false;
            }

            public bool ShouldPlaceAnchor(
                float confidenceThreshold,
                int requiredFrames,
                float timeout
            )
            {
                if (IsPlaced || IsPlacementInProgress)
                {
                    // Debug why placement is blocked
                    if (IsPlaced)
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Tag {TagId}: Already placed, skipping"
                        );
                    if (IsPlacementInProgress)
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Tag {TagId}: Placement in progress, skipping"
                        );
                    return false;
                }

                if (StableFrameCount >= requiredFrames && LastConfidence >= confidenceThreshold)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Tag {TagId}: Should place anchor - {StableFrameCount}/{requiredFrames} frames, confidence {LastConfidence:F3}/{confidenceThreshold:F3}"
                    );
                    return true;
                }

                if (Time.time - FirstDetectionTime > timeout)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Tag {TagId}: Timeout reached, giving up"
                    );
                    return false;
                }

                return false;
            }

            public bool ShouldRemoveAnchor(float currentTime, float timeout)
            {
                return IsPlaced && (currentTime - LastDetectionTime) > timeout;
            }
        }

        /// <summary>
        /// Represents a keep out zone around a placed anchor to prevent duplicates
        /// </summary>
        [Serializable]
        private class KeepOutZone
        {
            public int TagId;
            public Vector3 Center;
            public float Radius;
            public float CreationTime;

            public KeepOutZone(int id, Vector3 position, float radius)
            {
                TagId = id;
                Center = position;
                Radius = radius;
                CreationTime = Time.time;
            }

            public bool Contains(Vector3 position)
            {
                var distance = Vector3.Distance(Center, position);
                return distance <= Radius;
            }

            public bool IsExpired(float maxAge)
            {
                return (Time.time - CreationTime) > maxAge;
            }
        }

        private void Start()
        {
            // Initialize Spatial Anchor Core building block
            InitializeSpatialAnchorCore();

            // Load persisted anchors if enabled
            if (m_persistAnchors)
            {
                LoadAnchorsFromStorage();
            }

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Initialized with confidence threshold: {m_minConfidenceThreshold:F3}, required stable frames: {m_requiredStableFrames}, timeout: {m_maxDetectionTimeout:F1}s"
                );
            }
        }

        /// <summary>
        /// Initialize the Spatial Anchor Core building block
        /// </summary>
        private void InitializeSpatialAnchorCore()
        {
            if (m_enableDebugLogging)
            {
                Debug.Log(
                    "[AprilTagSpatialAnchorManager] Initializing SpatialAnchorCore building block..."
                );
            }

            // Find or create Spatial Anchor Core building block
            if (m_spatialAnchorCore == null)
            {
                // Try to find existing Spatial Anchor Core building block
                m_spatialAnchorCore = FindFirstObjectByType<SpatialAnchorCoreBuildingBlock>();
                if (m_spatialAnchorCore == null)
                {
                    // Create a new Spatial Anchor Core building block
                    var spatialAnchorCoreGO = new GameObject("SpatialAnchorCore");
                    m_spatialAnchorCore =
                        spatialAnchorCoreGO.AddComponent<SpatialAnchorCoreBuildingBlock>();

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            "[AprilTagSpatialAnchorManager] Created new SpatialAnchorCore building block"
                        );
                    }
                }
                else
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            "[AprilTagSpatialAnchorManager] Found existing SpatialAnchorCore building block"
                        );
                    }
                }
            }

            // Subscribe to events
            if (m_spatialAnchorCore != null)
            {
                // Unsubscribe first to avoid duplicate subscriptions
                m_spatialAnchorCore.OnAnchorCreateCompleted.RemoveListener(
                    OnAnchorCreatedFromBuildingBlock
                );
                m_spatialAnchorCore.OnAnchorsLoadCompleted.RemoveListener(OnAnchorsLoaded);
                m_spatialAnchorCore.OnAnchorsEraseAllCompleted.RemoveListener(OnAllAnchorsErased);
                m_spatialAnchorCore.OnAnchorEraseCompleted.RemoveListener(OnAnchorErased);

                // Subscribe to events
                m_spatialAnchorCore.OnAnchorCreateCompleted.AddListener(
                    OnAnchorCreatedFromBuildingBlock
                );
                m_spatialAnchorCore.OnAnchorsLoadCompleted.AddListener(OnAnchorsLoaded);
                m_spatialAnchorCore.OnAnchorsEraseAllCompleted.AddListener(OnAllAnchorsErased);
                m_spatialAnchorCore.OnAnchorEraseCompleted.AddListener(OnAnchorErased);

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        "[AprilTagSpatialAnchorManager] Successfully subscribed to SpatialAnchorCore events"
                    );
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] SpatialAnchorCore GameObject: {m_spatialAnchorCore.gameObject.name}"
                    );
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] SpatialAnchorCore active: {m_spatialAnchorCore.gameObject.activeInHierarchy}"
                    );
                }
            }
            else
            {
                Debug.LogError(
                    "[AprilTagSpatialAnchorManager] Failed to initialize SpatialAnchorCore building block"
                );
            }
        }

        private void Update()
        {
            // Handle Quest controller input for clearing anchors
            if (m_enableClearAnchorsInput)
            {
                HandleClearAnchorsInput();
            }

            // Clean up stale placement states
            CleanupStalePlacementStates();

            // Clean up expired keep out zones (every 30 seconds)
            if (Time.frameCount % 1800 == 0) // 30 seconds at 60 FPS
            {
                CleanupExpiredKeepOutZones();
            }
        }

        private void OnDestroy()
        {
            // Save anchors before destruction
            if (m_persistAnchors)
            {
                SaveAnchorsToStorage();
            }
        }

        /// <summary>
        /// Process a detected tag and potentially create an anchor
        /// </summary>
        /// <param name="tagId">The AprilTag ID</param>
        /// <param name="position">World position of the tag</param>
        /// <param name="rotation">World rotation of the tag</param>
        /// <param name="confidence">Detection confidence (0.0 - 1.0)</param>
        /// <param name="tagSize">The physical size of the tag in meters</param>
        public void ProcessTagDetection(
            int tagId,
            Vector3 position,
            Quaternion rotation,
            float confidence,
            float tagSize = 0.08f
        )
        {
            if (!m_enableSpatialAnchors || m_isClearingAnchors)
                return;

            // Debug log to show what threshold is actually being used (reduced frequency)
            if (m_enableDebugLogging && Time.frameCount % 30 == 0) // Log every 30 frames (0.5 seconds at 60fps)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Processing tag {tagId} with confidence {confidence:F3}, threshold: {m_minConfidenceThreshold:F3}"
                );
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Currently tracking {m_anchorsById.Count} anchors: [{string.Join(", ", m_anchorsById.Keys)}]"
                );
            }

            // Get or create placement state for this tag
            if (!m_placementStates.TryGetValue(tagId, out var state))
            {
                state = new AnchorPlacementState(tagId);
                m_placementStates[tagId] = state;

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Started tracking tag {tagId} for anchor placement"
                    );
                }
            }

            // Check if position has moved significantly since last frame
            // This prevents anchors from being placed while position is still stabilizing
            var positionDelta = Vector3.Distance(position, state.LastPosition);
            var hasSignificantMovement = positionDelta > 0.05f; // 5cm threshold

            // Update state with current detection
            state.LastDetectionTime = Time.time;
            state.LastPosition = position;
            state.LastRotation = rotation;
            state.LastConfidence = confidence;

            // Reset placement in progress if it's been too long (timeout protection)
            if (state.IsPlacementInProgress && (Time.time - state.FirstDetectionTime) > 10f)
            {
                if (m_enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[AprilTagSpatialAnchorManager] Tag {tagId}: Placement timeout, resetting placement in progress flag"
                    );
                }
                state.IsPlacementInProgress = false;
            }

            // Check if we should increment stable frame count
            if (confidence >= m_minConfidenceThreshold)
            {
                // Only increment if position hasn't moved significantly
                if (hasSignificantMovement && state.StableFrameCount > 0)
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Tag {tagId}: Position moved {positionDelta:F3}m, resetting stable frames (was {state.StableFrameCount})"
                        );
                    }
                    state.StableFrameCount = 0; // Reset if position is still moving
                }
                else
                {
                    state.StableFrameCount++;

                    if (m_enableDebugLogging && Time.frameCount % 60 == 0) // Log every 60 frames (1 second at 60fps)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Tag {tagId}: {state.StableFrameCount}/{m_requiredStableFrames} stable frames, confidence: {confidence:F3}, position delta: {positionDelta:F3}m"
                        );
                    }
                }
            }
            else
            {
                // Reset stable frame count if confidence drops
                if (m_enableDebugLogging && state.StableFrameCount > 0)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Tag {tagId}: Confidence dropped below threshold ({confidence:F3} < {m_minConfidenceThreshold:F3}), resetting stable frames"
                    );
                }
                state.StableFrameCount = 0;
            }

            // Log timeout progress (reduced frequency)
            if (
                m_enableDebugLogging
                && (Time.time - state.FirstDetectionTime) > 5f
                && Time.frameCount % 120 == 0
            ) // Log every 2 seconds
            {
                var timeRemaining = m_maxDetectionTimeout - (Time.time - state.FirstDetectionTime);
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Tag {tagId}: Timeout in {timeRemaining:F1}s, frames: {state.StableFrameCount}/{m_requiredStableFrames}, confidence: {confidence:F3}"
                );
            }

            // Check if we should place an anchor
            if (
                state.ShouldPlaceAnchor(
                    m_minConfidenceThreshold,
                    m_requiredStableFrames,
                    m_maxDetectionTimeout
                )
            )
            {
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Tag {tagId}: Should place anchor - checking keep out zones"
                    );
                }

                // Check if position is within any existing keep out zone
                if (IsPositionInKeepOutZone(position, tagId))
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Tag {tagId}: Position {position} is within keep out zone, skipping anchor creation"
                        );
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Current keep out zones: {m_keepOutZones.Count}"
                        );
                        foreach (var kvp in m_keepOutZones)
                        {
                            var distance = Vector3.Distance(kvp.Value.Center, position);
                            Debug.Log(
                                $"[AprilTagSpatialAnchorManager] - Tag {kvp.Key}: center={kvp.Value.Center}, radius={kvp.Value.Radius:F3}m, distance={distance:F3}m"
                            );
                        }
                    }
                    return; // Skip anchor creation
                }

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Tag {tagId}: No keep out zone conflict - creating anchor"
                    );
                }

                CreateAnchorForTag(tagId, position, rotation, tagSize);
            }
        }

        /// <summary>
        /// Remove tracking for a tag that is no longer detected
        /// </summary>
        /// <param name="tagId">The AprilTag ID to stop tracking</param>
        public void RemoveTagTracking(int tagId)
        {
            if (m_placementStates.TryGetValue(tagId, out _))
            {
                // Don't immediately remove anchors when tags are lost - they should persist
                // Only remove from active tracking, but keep the anchor
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Tag {tagId} temporarily lost - keeping anchor if placed"
                    );
                }

                // Remove from active tracking but don't remove the anchor
                _ = m_placementStates.Remove(tagId);
            }
        }

        /// <summary>
        /// Create a spatial anchor for a specific tag using direct instantiation
        /// </summary>
        private void CreateAnchorForTag(
            int tagId,
            Vector3 position,
            Quaternion rotation,
            float tagSize
        )
        {
            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] CreateAnchorForTag called for tag {tagId} at position {position}"
                );
            }

            if (m_anchorsById.ContainsKey(tagId))
            {
                if (m_enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[AprilTagSpatialAnchorManager] Anchor already exists for tag {tagId}, skipping creation"
                    );
                }
                return;
            }

            var state = m_placementStates[tagId];
            state.IsPlacementInProgress = true;

            // Create or use default prefab
            var prefab = m_anchorPrefab ?? CreateDefaultAnchorPrefab();

            // Create the anchor GameObject directly
            var anchorGO = Instantiate(prefab, position, rotation);
            var spatialAnchor =
                anchorGO.GetComponent<OVRSpatialAnchor>()
                ?? anchorGO.AddComponent<OVRSpatialAnchor>();

            // Store the anchor
            m_anchorsById[tagId] = spatialAnchor;
            m_anchorUuids[tagId] = spatialAnchor.Uuid;

            // Update state
            state.IsPlaced = true;
            state.IsPlacementInProgress = false;
            state.LastPosition = position;
            state.LastRotation = rotation;

            // Create keep out zone for this tag
            CreateOrUpdateKeepOutZone(tagId, position, tagSize);

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Successfully created spatial anchor for tag {tagId} at {position} with confidence {state.LastConfidence:F2}"
                );
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Anchor GameObject name: {anchorGO.name}, active: {anchorGO.activeInHierarchy}"
                );
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Total anchors tracked: {m_anchorsById.Count}"
                );
            }

            // Fire event
            OnAnchorCreated?.Invoke(tagId, spatialAnchor);
        }

        /// <summary>
        /// Remove a spatial anchor for a specific tag
        /// </summary>
        private void RemoveAnchorForTag(int tagId)
        {
            if (m_anchorsById.TryGetValue(tagId, out var anchor))
            {
                if (anchor != null && anchor.gameObject != null)
                {
                    DestroyImmediate(anchor.gameObject);
                }

                _ = m_anchorsById.Remove(tagId);
                _ = m_anchorUuids.Remove(tagId);

                // Remove keep out zone for this tag
                RemoveKeepOutZone(tagId);

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Removed spatial anchor for tag {tagId}"
                    );
                }

                // Fire event
                OnAnchorRemoved?.Invoke(tagId);
            }
        }

        /// <summary>
        /// Clear all existing spatial anchors and their visual representations
        /// </summary>
        public void ClearAllAnchors()
        {
            // Prevent multiple clearing operations
            if (m_isClearingAnchors)
            {
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        "[AprilTagSpatialAnchorManager] Clear operation already in progress, skipping"
                    );
                }
                return;
            }

            var tagIds = m_anchorsById.Keys.ToList();

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Requested clearing all spatial anchors ({tagIds.Count} tracked)"
                );
            }

            // Temporarily disable anchor creation to prevent immediate recreation
            m_isClearingAnchors = true;

            // Always use the fallback method since the building block erase is unreliable
            FallbackClearAllAnchors();
        }

        private bool m_isClearingAnchors = false;

        private System.Collections.IEnumerator ReenableAnchorCreationAfterDelay()
        {
            yield return new WaitForSeconds(2.0f); // Wait 2 seconds before re-enabling
            m_isClearingAnchors = false;

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    "[AprilTagSpatialAnchorManager] Re-enabled anchor creation after clearing"
                );
            }
        }

        /// <summary>
        /// Fallback method to manually clear all anchors and their visual representations
        /// </summary>
        private void FallbackClearAllAnchors()
        {
            if (m_enableDebugLogging)
            {
                Debug.Log(
                    "[AprilTagSpatialAnchorManager] Using fallback method to clear all anchors"
                );
            }

            // Get count before clearing for logging
            var anchorCount = m_anchorsById.Count;

            // Log all GameObjects in the scene that might be spatial anchors
            if (m_enableDebugLogging)
            {
                var allGameObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                var spatialAnchorObjects = allGameObjects
                    .Where(go => go.name.Contains("SpatialAnchor") || go.name.Contains("AprilTag"))
                    .ToArray();
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Found {spatialAnchorObjects.Length} potential spatial anchor GameObjects in scene:"
                );
                foreach (var go in spatialAnchorObjects)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] - {go.name} (active: {go.activeInHierarchy})"
                    );
                }
            }

            // Manually destroy all anchor GameObjects
            var anchorsToDestroy = new List<OVRSpatialAnchor>(m_anchorsById.Values);

            foreach (var anchor in anchorsToDestroy)
            {
                if (anchor != null && anchor.gameObject != null)
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Manually destroying tracked anchor GameObject: {anchor.gameObject.name}"
                        );
                    }
                    DestroyImmediate(anchor.gameObject);
                }
            }

            // Also destroy any GameObjects with OVRSpatialAnchor components that might not be tracked
            var allSpatialAnchors = FindObjectsByType<OVRSpatialAnchor>(FindObjectsSortMode.None);
            foreach (var anchor in allSpatialAnchors)
            {
                if (anchor != null && anchor.gameObject != null)
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Destroying untracked spatial anchor: {anchor.gameObject.name}"
                        );
                    }
                    DestroyImmediate(anchor.gameObject);
                }
            }

            // Clear our tracking data
            m_anchorsById.Clear();
            m_anchorUuids.Clear();
            m_placementStates.Clear();
            m_keepOutZones.Clear();

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Fallback clearing completed - {anchorCount} anchors and their visual representations removed"
                );
            }

            // Re-enable anchor creation
            _ = StartCoroutine(ReenableAnchorCreationAfterDelay());

            // Fire event
            OnAllAnchorsCleared?.Invoke();
        }

        /// <summary>
        /// Handle Quest controller input for clearing anchors
        /// </summary>
        private void HandleClearAnchorsInput()
        {
            // Check for A button press on right controller
            var aButtonPressed = OVRInput.GetDown(OVRInput.Button.One, m_rightController);

            if (aButtonPressed && !m_lastAButtonState)
            {
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] A button pressed - clearing all anchors (currently tracking {m_anchorsById.Count} anchors)"
                    );
                }
                ClearAllAnchors();
            }

            m_lastAButtonState = aButtonPressed;
        }

        /// <summary>
        /// Handle anchor creation completion from Spatial Anchor Core building block
        /// </summary>
        private void OnAnchorCreatedFromBuildingBlock(
            OVRSpatialAnchor anchor,
            OVRSpatialAnchor.OperationResult result
        )
        {
            if (result == OVRSpatialAnchor.OperationResult.Success)
            {
                // Find the tag ID for this anchor based on position
                var tagId = FindTagIdForAnchor(anchor);
                if (tagId != -1)
                {
                    m_anchorsById[tagId] = anchor;
                    m_anchorUuids[tagId] = anchor.Uuid;

                    if (m_placementStates.TryGetValue(tagId, out var state))
                    {
                        state.IsPlaced = true;
                        state.IsPlacementInProgress = false;
                    }

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Successfully created spatial anchor for tag {tagId} at {anchor.transform.position}"
                        );
                    }

                    // Fire event
                    OnAnchorCreated?.Invoke(tagId, anchor);
                }
            }
            else
            {
                Debug.LogError(
                    $"[AprilTagSpatialAnchorManager] Failed to create spatial anchor: {result}"
                );
            }
        }

        /// <summary>
        /// Handle anchors loaded completion from Spatial Anchor Core building block
        /// </summary>
        private void OnAnchorsLoaded(List<OVRSpatialAnchor> loadedAnchors)
        {
            foreach (var anchor in loadedAnchors)
            {
                // Find the tag ID for this anchor using the pending load data
                var tagId = -1;
                foreach (var kvp in m_pendingLoadData)
                {
                    if (kvp.Value == anchor.Uuid)
                    {
                        tagId = kvp.Key;
                        break;
                    }
                }

                if (tagId != -1)
                {
                    m_anchorsById[tagId] = anchor;
                    m_anchorUuids[tagId] = anchor.Uuid;

                    // Mark as placed
                    var state = new AnchorPlacementState(tagId) { IsPlaced = true };
                    m_placementStates[tagId] = state;

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Successfully loaded spatial anchor for tag {tagId} at {anchor.transform.position}"
                        );
                    }
                }
            }

            // Clear pending load data
            m_pendingLoadData.Clear();

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Successfully loaded {loadedAnchors.Count} anchors from storage"
                );
            }
        }

        /// <summary>
        /// Handle all anchors erased completion from Spatial Anchor Core building block
        /// Note: This method is kept for compatibility but is not used in the current implementation
        /// </summary>
        private void OnAllAnchorsErased(OVRSpatialAnchor.OperationResult result)
        {
            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] OnAllAnchorsErased called with result: {result} (not used in current implementation)"
                );
            }
        }

        /// <summary>
        /// Handle individual anchor erased completion from Spatial Anchor Core building block
        /// </summary>
        private void OnAnchorErased(
            OVRSpatialAnchor anchor,
            OVRSpatialAnchor.OperationResult result
        )
        {
            if (result == OVRSpatialAnchor.OperationResult.Success)
            {
                // Find and remove the tag ID for this anchor
                var tagId = FindTagIdForAnchor(anchor);
                if (tagId != -1)
                {
                    _ = m_anchorsById.Remove(tagId);
                    _ = m_anchorUuids.Remove(tagId);

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Successfully removed spatial anchor for tag {tagId}"
                        );
                    }

                    // Fire event
                    OnAnchorRemoved?.Invoke(tagId);
                }
            }
            else
            {
                Debug.LogError(
                    $"[AprilTagSpatialAnchorManager] Failed to erase spatial anchor: {result}"
                );
            }
        }

        /// <summary>
        /// Calculate the keep out zone radius based on tag size and configuration
        /// </summary>
        /// <param name="tagSize">The physical size of the AprilTag in meters</param>
        /// <returns>The radius of the keep out zone in meters</returns>
        private float CalculateKeepOutRadius(float tagSize)
        {
            if (!m_enableKeepOutZone)
                return 0f;

            // Calculate radius based on tag size and multiplier
            var radius = tagSize * m_keepOutZoneMultiplier;

            // Apply min/max constraints
            radius = Mathf.Max(radius, m_minKeepOutRadius);
            radius = Mathf.Min(radius, m_maxKeepOutRadius);

            return radius;
        }

        /// <summary>
        /// Check if a position is within any existing keep out zone
        /// </summary>
        /// <param name="position">The position to check</param>
        /// <param name="excludeTagId">Tag ID to exclude from the check (for updating existing zones)</param>
        /// <returns>True if the position is within a keep out zone</returns>
        private bool IsPositionInKeepOutZone(Vector3 position, int excludeTagId = -1)
        {
            if (!m_enableKeepOutZone)
                return false;

            foreach (var kvp in m_keepOutZones)
            {
                if (kvp.Key == excludeTagId)
                    continue; // Skip the tag we're updating

                if (kvp.Value.Contains(position))
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Position {position} is within keep out zone for tag {kvp.Key} (radius: {kvp.Value.Radius:F3}m)"
                        );
                    }
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Create or update a keep out zone for a tag
        /// </summary>
        /// <param name="tagId">The tag ID</param>
        /// <param name="position">The anchor position</param>
        /// <param name="tagSize">The tag size in meters</param>
        private void CreateOrUpdateKeepOutZone(int tagId, Vector3 position, float tagSize)
        {
            if (!m_enableKeepOutZone)
                return;

            var radius = CalculateKeepOutRadius(tagSize);

            if (m_keepOutZones.ContainsKey(tagId))
            {
                // Update existing zone
                m_keepOutZones[tagId].Center = position;
                m_keepOutZones[tagId].Radius = radius;
                m_keepOutZones[tagId].CreationTime = Time.time;
            }
            else
            {
                // Create new zone
                m_keepOutZones[tagId] = new KeepOutZone(tagId, position, radius);
            }

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Created/updated keep out zone for tag {tagId}: center={position}, radius={radius:F3}m"
                );
            }
        }

        /// <summary>
        /// Remove a keep out zone for a tag
        /// </summary>
        /// <param name="tagId">The tag ID</param>
        private void RemoveKeepOutZone(int tagId)
        {
            if (m_keepOutZones.ContainsKey(tagId))
            {
                _ = m_keepOutZones.Remove(tagId);

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Removed keep out zone for tag {tagId}"
                    );
                }
            }
        }

        /// <summary>
        /// Clean up expired keep out zones
        /// </summary>
        private void CleanupExpiredKeepOutZones()
        {
            if (!m_enableKeepOutZone)
                return;

            var expiredZones = new List<int>();
            var maxAge = 300f; // 5 minutes

            foreach (var kvp in m_keepOutZones)
            {
                if (kvp.Value.IsExpired(maxAge))
                {
                    expiredZones.Add(kvp.Key);
                }
            }

            foreach (var tagId in expiredZones)
            {
                _ = m_keepOutZones.Remove(tagId);

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Cleaned up expired keep out zone for tag {tagId}"
                    );
                }
            }
        }

        /// <summary>
        /// Create a default anchor prefab with visual representation
        /// </summary>
        private GameObject CreateDefaultAnchorPrefab()
        {
            // Create the main anchor GameObject
            var anchorGO = new GameObject("AprilTag_SpatialAnchor");

            // Add the required OVRSpatialAnchor component
            _ = anchorGO.AddComponent<OVRSpatialAnchor>();

            // Create a visual representation child object
            var visualGO = new GameObject("Visual");
            visualGO.transform.SetParent(anchorGO.transform);
            visualGO.transform.localPosition = Vector3.zero;
            visualGO.transform.localRotation = Quaternion.identity;
            visualGO.transform.localScale = Vector3.one;

            // Add a simple cube mesh for visualization
            var meshFilter = visualGO.AddComponent<MeshFilter>();
            var meshRenderer = visualGO.AddComponent<MeshRenderer>();

            // Create a simple cube mesh
            var cubeMesh = CreateCubeMesh();
            meshFilter.mesh = cubeMesh;

            // Create a material for the anchor
            var material = CreateAnchorMaterial();
            meshRenderer.material = material;

            // Scale the visual to be small and unobtrusive
            visualGO.transform.localScale = Vector3.one * 0.1f; // 10cm cube

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    "[AprilTagSpatialAnchorManager] Created default anchor prefab with visual representation"
                );
            }

            return anchorGO;
        }

        /// <summary>
        /// Create a simple cube mesh for anchor visualization
        /// </summary>
        private Mesh CreateCubeMesh()
        {
            var mesh = new Mesh { name = "AnchorCube" };

            // Simple cube vertices
            var vertices = new Vector3[]
            {
                // Front face
                new(-0.5f, -0.5f, 0.5f),
                new(0.5f, -0.5f, 0.5f),
                new(0.5f, 0.5f, 0.5f),
                new(-0.5f, 0.5f, 0.5f),
                // Back face
                new(-0.5f, -0.5f, -0.5f),
                new(-0.5f, 0.5f, -0.5f),
                new(0.5f, 0.5f, -0.5f),
                new(0.5f, -0.5f, -0.5f),
            };

            // Simple cube triangles
            var triangles = new int[]
            {
                // Front face
                0,
                2,
                1,
                0,
                3,
                2,
                // Back face
                4,
                6,
                5,
                4,
                7,
                6,
                // Left face
                4,
                3,
                0,
                4,
                5,
                3,
                // Right face
                1,
                2,
                6,
                1,
                6,
                7,
                // Top face
                3,
                5,
                2,
                2,
                5,
                6,
                // Bottom face
                0,
                1,
                4,
                1,
                7,
                4,
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            return mesh;
        }

        /// <summary>
        /// Create a material for the anchor visualization
        /// </summary>
        private Material CreateAnchorMaterial()
        {
            var material = new Material(Shader.Find("Standard"))
            {
                name = "AprilTagAnchorMaterial",

                // Set a distinctive color for spatial anchors
                color = new Color(0.2f, 0.8f, 1.0f, 0.8f), // Light blue with transparency
            };
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

        /// <summary>
        /// Find the tag ID for a given anchor based on position
        /// </summary>
        private int FindTagIdForAnchor(OVRSpatialAnchor anchor)
        {
            // Find the tag ID by matching position with pending placement states
            foreach (var kvp in m_placementStates)
            {
                if (
                    kvp.Value.IsPlacementInProgress
                    && Vector3.Distance(kvp.Value.LastPosition, anchor.transform.position) < 0.01f
                )
                {
                    return kvp.Key;
                }
            }
            return -1;
        }

        /// <summary>
        /// Clean up stale placement states for tags that haven't been detected recently
        /// </summary>
        private void CleanupStalePlacementStates()
        {
            var currentTime = Time.time;
            var staleTimeout = m_maxDetectionTimeout * 2; // Give extra time before cleanup

            var staleTags = m_placementStates
                .Where(kv =>
                    !kv.Value.IsPlaced && (currentTime - kv.Value.LastDetectionTime) > staleTimeout
                )
                .Select(kv => kv.Key)
                .ToList();

            foreach (var tagId in staleTags)
            {
                _ = m_placementStates.Remove(tagId);

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Cleaned up stale placement state for tag {tagId}"
                    );
                }
            }
        }

        /// <summary>
        /// Save anchors to persistent storage
        /// </summary>
        private void SaveAnchorsToStorage()
        {
            if (!m_persistAnchors)
                return;

            var anchorData = new List<AnchorData>();

            foreach (var kvp in m_anchorUuids)
            {
                var tagId = kvp.Key;
                var uuid = kvp.Value;

                if (m_anchorsById.TryGetValue(tagId, out var anchor) && anchor != null)
                {
                    anchorData.Add(
                        new AnchorData
                        {
                            TagId = tagId,
                            Uuid = uuid.ToString(),
                            Position = anchor.transform.position,
                            Rotation = anchor.transform.rotation,
                        }
                    );
                }
            }

            var json = JsonUtility.ToJson(new AnchorDataCollection { Anchors = anchorData });
            PlayerPrefs.SetString("AprilTag_Anchors", json);
            PlayerPrefs.Save();

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Saved {anchorData.Count} anchors to storage"
                );
            }
        }

        /// <summary>
        /// Load anchors from persistent storage using Spatial Anchor Core building block
        /// </summary>
        private void LoadAnchorsFromStorage()
        {
            if (!m_persistAnchors || m_spatialAnchorCore == null)
                return;

            var json = PlayerPrefs.GetString("AprilTag_Anchors", "");
            if (string.IsNullOrEmpty(json))
                return;

            try
            {
                var data = JsonUtility.FromJson<AnchorDataCollection>(json);
                if (data.Anchors.Count == 0)
                    return;

                // Convert string UUIDs to Guids
                var uuids = new List<Guid>();
                var tagIdToUuid = new Dictionary<int, Guid>();

                foreach (var anchorData in data.Anchors)
                {
                    if (Guid.TryParse(anchorData.Uuid, out var uuid))
                    {
                        uuids.Add(uuid);
                        tagIdToUuid[anchorData.TagId] = uuid;
                    }
                }

                if (uuids.Count > 0)
                {
                    // Use Spatial Anchor Core building block to load anchors
                    m_spatialAnchorCore.LoadAndInstantiateAnchors(m_anchorPrefab, uuids);

                    // Store mapping for when anchors are loaded
                    m_pendingLoadData = tagIdToUuid;

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Requested loading {uuids.Count} anchors from storage"
                        );
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[AprilTagSpatialAnchorManager] Failed to load anchors from storage: {e.Message}"
                );
            }
        }

        /// <summary>
        /// Get the current number of placed anchors
        /// </summary>
        public int GetAnchorCount()
        {
            return m_anchorsById.Count;
        }

        /// <summary>
        /// Check if an anchor exists for a specific tag ID
        /// </summary>
        public bool HasAnchorForTag(int tagId)
        {
            return m_anchorsById.ContainsKey(tagId);
        }

        /// <summary>
        /// Get the spatial anchor for a specific tag ID
        /// </summary>
        public OVRSpatialAnchor GetAnchorForTag(int tagId)
        {
            _ = m_anchorsById.TryGetValue(tagId, out var anchor);
            return anchor;
        }

        // Data structures for persistence
        [Serializable]
        private class AnchorData
        {
            public int TagId;
            public string Uuid;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        [Serializable]
        private class AnchorDataCollection
        {
            public List<AnchorData> Anchors;
        }
    }
}
