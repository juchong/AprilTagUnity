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
        [Tooltip("Enable X button on right controller to clear all anchors")]
        [SerializeField]
        private bool m_enableClearAnchorsInput = true;

        [Tooltip("Enable Y button on right controller to delete individual anchor by pointing")]
        [SerializeField]
        private bool m_enablePointAndDeleteInput = true;

        [Tooltip("Maximum distance for point-and-delete raycast (meters)")]
        [Range(0.5f, 10.0f)]
        [SerializeField]
        private float m_pointDeleteMaxDistance = 5.0f;

        [Tooltip("Maximum angle between controller and anchor for deletion (degrees)")]
        [Range(5.0f, 45.0f)]
        [SerializeField]
        private float m_pointDeleteMaxAngle = 15.0f;

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
        private bool m_lastXButtonState = false;
        private bool m_lastYButtonState = false;

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
            // Handle Quest controller input for clearing all anchors
            if (m_enableClearAnchorsInput)
            {
                HandleClearAnchorsInput();
            }

            // Handle Quest controller input for point-and-delete individual anchors
            if (m_enablePointAndDeleteInput)
            {
                HandlePointAndDeleteInput();
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

            // Check if anchor already exists or is being created (prevent race condition)
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
            
            // CRITICAL: Prevent race condition where multiple frames try to create anchor
            if (state.IsPlacementInProgress)
            {
                if (m_enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[AprilTagSpatialAnchorManager] Anchor creation already in progress for tag {tagId}, skipping"
                    );
                }
                return;
            }
            
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

            // CRITICAL: Wait for anchor to be created, then save to local device storage
            // The OVRSpatialAnchor component needs time to initialize and get a valid UUID
            // By default, SaveAnchorAsync() uses LOCAL storage (works offline, no cloud required)
            // Without proper saving, anchors only exist in memory and are lost on app restart
            if (m_persistAnchors)
            {
                WaitForAnchorCreationThenSave(spatialAnchor, tagId);
            }

            // Fire event
            OnAnchorCreated?.Invoke(tagId, spatialAnchor);
        }

        /// <summary>
        /// Wait for the OVRSpatialAnchor to be fully created, then save it
        /// </summary>
        private void WaitForAnchorCreationThenSave(OVRSpatialAnchor anchor, int tagId)
        {
            _ = StartCoroutine(WaitForAnchorCreationCoroutine(anchor, tagId));
        }

        /// <summary>
        /// Coroutine that waits for the anchor to have a valid UUID before saving
        /// </summary>
        private System.Collections.IEnumerator WaitForAnchorCreationCoroutine(
            OVRSpatialAnchor anchor,
            int tagId
        )
        {
            if (anchor == null)
            {
                Debug.LogError(
                    $"[AprilTagSpatialAnchorManager] Cannot wait for null anchor (tag {tagId})"
                );
                yield break;
            }

            // Wait for the OVRSpatialAnchor component to finish initializing
            // This happens in its Start() method, which runs after our code
            yield return null; // Wait one frame for Start() to run

            // Wait for a valid UUID (not empty GUID)
            var maxWaitFrames = 300; // 5 seconds at 60 FPS
            var framesWaited = 0;

            while (anchor != null && anchor.Uuid == System.Guid.Empty && framesWaited < maxWaitFrames)
            {
                framesWaited++;
                yield return null;
            }

            // Check if anchor was destroyed while waiting
            if (anchor == null || !m_anchorsById.ContainsKey(tagId))
            {
                if (m_enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[AprilTagSpatialAnchorManager] Anchor for tag {tagId} was destroyed while waiting for creation"
                    );
                }
                yield break;
            }

            // Check if we got a valid UUID
            if (anchor.Uuid == System.Guid.Empty)
            {
                Debug.LogWarning(
                    $"[AprilTagSpatialAnchorManager] Anchor for tag {tagId} failed to get valid UUID after {framesWaited} frames - anchor will remain active but won't persist"
                );
                
                // IMPORTANT: Keep the anchor even without UUID
                // The GameObject still exists and is world-locked by the OVRSpatialAnchor component
                // It just won't be saved for future sessions
                yield break;
            }

            // UUID is valid, store it and save to cloud
            m_anchorUuids[tagId] = anchor.Uuid;

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Anchor for tag {tagId} initialized with UUID {anchor.Uuid} after {framesWaited} frames"
                );
            }

            // Now save the anchor with the valid UUID
            SaveAnchorAsync(anchor, tagId);
        }

        /// <summary>
        /// Save a spatial anchor to local device storage for persistence (works offline)
        /// </summary>
        private async void SaveAnchorAsync(OVRSpatialAnchor anchor, int tagId)
        {
            if (anchor == null)
            {
                Debug.LogError(
                    $"[AprilTagSpatialAnchorManager] Cannot save null anchor for tag {tagId}"
                );
                return;
            }

            try
            {
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Saving anchor for tag {tagId} to local storage (UUID: {anchor.Uuid})..."
                    );
                }

                // Save the anchor to local device storage (no cloud/internet required)
                // This is the default behavior of SaveAnchorAsync() - anchors persist across app sessions
                var success = await anchor.SaveAnchorAsync();
                
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] SaveAnchorAsync completed for tag {tagId}: success={success}"
                    );
                }

                if (success)
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Successfully saved anchor for tag {tagId} to local device storage"
                        );
                    }

                    // After successful save, update PlayerPrefs with the anchor data
                    SaveAnchorsToStorage();
                }
                else
                {
                    Debug.LogWarning(
                        $"[AprilTagSpatialAnchorManager] Failed to save anchor for tag {tagId} to local device storage - anchor will remain active but won't persist across sessions"
                    );
                    
                    // IMPORTANT: Keep the anchor even if save fails
                    // The anchor still works as a world-locked object in the current session
                    // We just won't be able to load it on next app launch
                    // This prevents anchors from disappearing when save fails
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    $"[AprilTagSpatialAnchorManager] Exception while saving anchor for tag {tagId}: {e.Message} - anchor will remain active but won't persist"
                );
                
                // IMPORTANT: Keep the anchor even on exception
                // The anchor still works in the current session
            }
        }

        /// <summary>
        /// Erase a spatial anchor from local device storage
        /// </summary>
        private async void EraseAnchorAsync(OVRSpatialAnchor anchor, int tagId)
        {
            if (anchor == null)
            {
                Debug.LogError(
                    $"[AprilTagSpatialAnchorManager] Cannot erase null anchor for tag {tagId}"
                );
                return;
            }

            try
            {
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Erasing anchor for tag {tagId} from local storage (UUID: {anchor.Uuid})..."
                    );
                }

                // Erase the anchor from local device storage (no cloud/internet required)
                var success = await anchor.EraseAnchorAsync();

                if (success)
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Successfully erased anchor for tag {tagId} from local device storage"
                        );
                    }
                }
                else
                {
                    Debug.LogWarning(
                        $"[AprilTagSpatialAnchorManager] Failed to erase anchor for tag {tagId} from local device storage"
                    );
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError(
                    $"[AprilTagSpatialAnchorManager] Exception while erasing anchor for tag {tagId}: {e.Message}\n{e.StackTrace}"
                );
            }
        }

        /// <summary>
        /// Remove a spatial anchor for a specific tag
        /// Can be called from external scripts or via point-and-delete controller input
        /// </summary>
        public void RemoveAnchorForTag(int tagId)
        {
            if (m_anchorsById.TryGetValue(tagId, out var anchor))
            {
                // Erase from local device storage if persistence is enabled
                if (m_persistAnchors && anchor != null)
                {
                    EraseAnchorAsync(anchor, tagId);
                }

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

                // Update PlayerPrefs after removal
                if (m_persistAnchors)
                {
                    SaveAnchorsToStorage();
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

            // Erase tracked anchors from local device storage before destroying
            if (m_persistAnchors)
            {
                var anchorsToErase = new List<OVRSpatialAnchor>(m_anchorsById.Values);
                foreach (var anchor in anchorsToErase)
                {
                    if (anchor != null)
                    {
                        // Get tag ID for logging
                        var tagId = m_anchorsById.FirstOrDefault(x => x.Value == anchor).Key;
                        EraseAnchorAsync(anchor, tagId);
                    }
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

            // Clear PlayerPrefs storage
            if (m_persistAnchors)
            {
                PlayerPrefs.DeleteKey("AprilTag_Anchors");
                PlayerPrefs.Save();

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        "[AprilTagSpatialAnchorManager] Cleared anchor data from PlayerPrefs"
                    );
                }
            }

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
            // Check for X button press on right controller
            var xButtonPressed = OVRInput.GetDown(OVRInput.Button.Three, m_rightController);

            if (xButtonPressed && !m_lastXButtonState)
            {
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] X button pressed - clearing all anchors (currently tracking {m_anchorsById.Count} anchors)"
                    );
                }
                ClearAllAnchors();
            }

            m_lastXButtonState = xButtonPressed;
        }

        /// <summary>
        /// Handle Quest controller input for point-and-delete individual anchors
        /// </summary>
        private void HandlePointAndDeleteInput()
        {
            // Check for Y button press on right controller
            var yButtonPressed = OVRInput.GetDown(OVRInput.Button.Four, m_rightController);

            if (yButtonPressed && !m_lastYButtonState)
            {
                // Get the anchor the user is pointing at
                var targetAnchor = FindAnchorByPointing();

                if (targetAnchor.HasValue)
                {
                    var (tagId, anchor) = targetAnchor.Value;

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Y button pressed - deleting anchor for tag {tagId} at {anchor.transform.position}"
                        );
                    }

                    RemoveAnchorForTag(tagId);
                }
                else
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            "[AprilTagSpatialAnchorManager] Y button pressed but no anchor in range or pointing direction"
                        );
                    }
                }
            }

            m_lastYButtonState = yButtonPressed;
        }

        /// <summary>
        /// Find the closest anchor that the user is pointing at with the right controller
        /// </summary>
        /// <returns>Tuple of (tagId, anchor) if found, null otherwise</returns>
        private (int, OVRSpatialAnchor)? FindAnchorByPointing()
        {
            if (m_anchorsById.Count == 0)
                return null;

            // Get right controller position and forward direction
            var controllerPosition = OVRInput.GetLocalControllerPosition(m_rightController);
            var controllerRotation = OVRInput.GetLocalControllerRotation(m_rightController);
            var controllerForward = controllerRotation * Vector3.forward;

            // Convert from local to world space
            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning(
                    "[AprilTagSpatialAnchorManager] No main camera found for point-and-delete"
                );
                return null;
            }

            var worldControllerPosition = mainCamera.transform.TransformPoint(controllerPosition);
            var worldControllerForward = mainCamera.transform.TransformDirection(controllerForward);

            // Find the closest anchor within the pointing cone
            float closestDistance = float.MaxValue;
            int closestTagId = -1;
            OVRSpatialAnchor closestAnchor = null;

            foreach (var kvp in m_anchorsById)
            {
                var tagId = kvp.Key;
                var anchor = kvp.Value;

                if (anchor == null || anchor.gameObject == null)
                    continue;

                var anchorPosition = anchor.transform.position;

                // Calculate distance to anchor
                var toAnchor = anchorPosition - worldControllerPosition;
                var distance = toAnchor.magnitude;

                // Check if within max distance
                if (distance > m_pointDeleteMaxDistance)
                    continue;

                // Calculate angle between controller forward and direction to anchor
                var angle = Vector3.Angle(worldControllerForward, toAnchor.normalized);

                // Check if within pointing cone
                if (angle > m_pointDeleteMaxAngle)
                    continue;

                // Track closest anchor
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestTagId = tagId;
                    closestAnchor = anchor;
                }
            }

            if (closestAnchor != null)
            {
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Found anchor for tag {closestTagId} at distance {closestDistance:F2}m, angle: {Vector3.Angle(worldControllerForward, (closestAnchor.transform.position - worldControllerPosition).normalized):F1}°"
                    );
                }
                
                // Optional: Add visual feedback here (e.g., highlight the anchor)
                // This could be done by changing the material color or scale temporarily
                
                return (closestTagId, closestAnchor);
            }

            return null;
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
            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] OnAnchorsLoaded callback triggered with {loadedAnchors.Count} anchors"
                );
            }

            foreach (var anchor in loadedAnchors)
            {
                if (anchor == null)
                {
                    Debug.LogWarning(
                        "[AprilTagSpatialAnchorManager] Loaded anchor is null, skipping"
                    );
                    continue;
                }

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
                            $"[AprilTagSpatialAnchorManager] Successfully loaded spatial anchor for tag {tagId} at {anchor.transform.position} (UUID: {anchor.Uuid})"
                        );
                    }

                    // CRITICAL: Fire event so AprilTagController creates visualization
                    // Without this, loaded anchors have no visible representation
                    OnAnchorCreated?.Invoke(tagId, anchor);

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Fired OnAnchorCreated event for loaded tag {tagId}"
                        );
                    }
                }
                else
                {
                    Debug.LogWarning(
                        $"[AprilTagSpatialAnchorManager] Could not find tag ID for loaded anchor UUID {anchor.Uuid}"
                    );
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
