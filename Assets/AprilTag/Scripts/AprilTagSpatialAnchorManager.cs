// Assets/AprilTag/AprilTagSpatialAnchorManager.cs
// Spatial anchor management system for AprilTag detection with confidence-based placement
// Integrates with Meta XR Building Blocks for controller-based anchor management

using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.BuildingBlocks;
using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Manages spatial anchors for detected AprilTags with confidence-based placement
    /// Integrates with Meta XR Building Blocks for anchor creation, loading, and erasing
    /// </summary>
    public class AprilTagSpatialAnchorManager : MonoBehaviour
    {
        [Header("Anchor Configuration")]
        [Tooltip("Enable automatic spatial anchor creation for detected tags")]
        [SerializeField]
        private bool m_enableSpatialAnchors = true;

        [Tooltip("Number of consecutive high-confidence detections required before placing anchor")]
        [SerializeField]
        private int m_requiredStableFrames = 8;

        [Tooltip("Maximum time to wait for stable detection before giving up (seconds)")]
        [SerializeField]
        private float m_maxDetectionTimeout = 30f;

        [Header("Meta XR Building Blocks")]
        [Tooltip("Spatial Anchor Spawner building block (auto-found if null)")]
        [SerializeField]
        private SpatialAnchorSpawnerBuildingBlock m_spatialAnchorSpawner;

        [Tooltip("Spatial Anchor Loader building block (auto-found if null)")]
        [SerializeField]
        private SpatialAnchorLoaderBuildingBlock m_spatialAnchorLoader;

        [Tooltip("Spatial Anchor Core building block (auto-found if null)")]
        [SerializeField]
        private SpatialAnchorCoreBuildingBlock m_spatialAnchorCore;

        [Header("Keep Out Zone")]
        [Tooltip("Enable keep out zone around tags to prevent duplicate anchor placement")]
        [SerializeField]
        private bool m_enableKeepOutZone = true;

        // Core data structures
        private Dictionary<int, OVRSpatialAnchor> m_anchorsById = new();
        private Dictionary<int, AnchorPlacementState> m_placementStates = new();
        private Dictionary<int, KeepOutZone> m_keepOutZones = new();
        private Dictionary<Guid, int> m_anchorGuidToTagId = new(); // Map anchor GUID to tag ID

        // Property to store debug logging state from controller
        public bool EnableDebugLogging { get; set; } = false;

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
                    return false;
                }

                if (StableFrameCount >= requiredFrames && LastConfidence >= confidenceThreshold)
                {
                    return true;
                }

                if (Time.time - FirstDetectionTime > timeout)
                {
                    return false;
                }

                return false;
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
            // Initialize Meta XR Building Blocks
            InitializeBuildingBlocks();

            // Subscribe to building block events
            SubscribeToBuildingBlockEvents();

            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Initialized - anchor management enabled: {m_enableSpatialAnchors}, "
                        + $"required stable frames: {m_requiredStableFrames}, timeout: {m_maxDetectionTimeout:F1}s"
                );
            }

            // Automatically load saved anchors on startup after a short delay
            // This ensures all systems are initialized before loading
            StartCoroutine(LoadAnchorsOnStartup());
        }

        /// <summary>
        /// Coroutine to load saved anchors after a short startup delay
        /// </summary>
        private System.Collections.IEnumerator LoadAnchorsOnStartup()
        {
            // Wait a bit to ensure all systems are initialized
            yield return new WaitForSeconds(1.0f);

            if (m_spatialAnchorLoader != null)
            {
                if (EnableDebugLogging)
                {
                    Debug.Log(
                        "[AprilTagSpatialAnchorManager] Loading saved anchors from default local storage..."
                    );
                }

                // Load anchors from default local storage
                m_spatialAnchorLoader.LoadAnchorsFromDefaultLocalStorage();

                // Wait a moment and check if callback was triggered
                yield return new WaitForSeconds(2.0f);

                if (EnableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] After load attempt: {m_anchorsById.Count} anchors tracked. "
                            + $"If this is 0 and you expect anchors, they may not have been saved previously."
                    );
                }
            }
            else
            {
                Debug.LogWarning(
                    "[AprilTagSpatialAnchorManager] Cannot load anchors - SpatialAnchorLoaderBuildingBlock not found"
                );
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe from building block events
            UnsubscribeFromBuildingBlockEvents();
        }

        /// <summary>
        /// Initialize the Meta XR Building Blocks
        /// </summary>
        private void InitializeBuildingBlocks()
        {
            // Find building blocks if not assigned
            if (m_spatialAnchorSpawner == null)
            {
                m_spatialAnchorSpawner = FindFirstObjectByType<SpatialAnchorSpawnerBuildingBlock>();
                if (m_spatialAnchorSpawner == null)
                {
                    Debug.LogWarning(
                        "[AprilTagSpatialAnchorManager] SpatialAnchorSpawnerBuildingBlock not found"
                    );
                }
            }

            if (m_spatialAnchorLoader == null)
            {
                m_spatialAnchorLoader = FindFirstObjectByType<SpatialAnchorLoaderBuildingBlock>();
                if (m_spatialAnchorLoader == null)
                {
                    Debug.LogWarning(
                        "[AprilTagSpatialAnchorManager] SpatialAnchorLoaderBuildingBlock not found"
                    );
                }
            }

            if (m_spatialAnchorCore == null)
            {
                m_spatialAnchorCore = FindFirstObjectByType<SpatialAnchorCoreBuildingBlock>();
                if (m_spatialAnchorCore == null)
                {
                    Debug.LogWarning(
                        "[AprilTagSpatialAnchorManager] SpatialAnchorCoreBuildingBlock not found"
                    );
                }
            }
        }

        /// <summary>
        /// Subscribe to building block events
        /// </summary>
        private void SubscribeToBuildingBlockEvents()
        {
            if (m_spatialAnchorCore != null)
            {
                m_spatialAnchorCore.OnAnchorCreateCompleted.AddListener(
                    OnBuildingBlockAnchorCreated
                );
                m_spatialAnchorCore.OnAnchorsLoadCompleted.AddListener(
                    OnBuildingBlockAnchorsLoaded
                );
                m_spatialAnchorCore.OnAnchorsEraseAllCompleted.AddListener(
                    OnBuildingBlockAllAnchorsErased
                );
                m_spatialAnchorCore.OnAnchorEraseCompleted.AddListener(OnBuildingBlockAnchorErased);
            }

            // Note: SpatialAnchorSpawnerBuildingBlock doesn't have events
            // Anchors are tracked via SpatialAnchorCoreBuildingBlock events
        }

        /// <summary>
        /// Unsubscribe from building block events
        /// </summary>
        private void UnsubscribeFromBuildingBlockEvents()
        {
            if (m_spatialAnchorCore != null)
            {
                m_spatialAnchorCore.OnAnchorCreateCompleted.RemoveListener(
                    OnBuildingBlockAnchorCreated
                );
                m_spatialAnchorCore.OnAnchorsLoadCompleted.RemoveListener(
                    OnBuildingBlockAnchorsLoaded
                );
                m_spatialAnchorCore.OnAnchorsEraseAllCompleted.RemoveListener(
                    OnBuildingBlockAllAnchorsErased
                );
                m_spatialAnchorCore.OnAnchorEraseCompleted.RemoveListener(
                    OnBuildingBlockAnchorErased
                );
            }
        }

        private void Update()
        {
            // Clean up stale placement states
            CleanupStalePlacementStates();

            // Clean up expired keep out zones (every 30 seconds)
            if (Time.frameCount % 1800 == 0) // 30 seconds at 60 FPS
            {
                CleanupExpiredKeepOutZones();
            }
        }

        /// <summary>
        /// Process a detected tag and potentially create an anchor
        /// </summary>
        /// <param name="tagId">The AprilTag ID</param>
        /// <param name="position">World position of the tag (may include offset from AprilTagController)</param>
        /// <param name="rotation">World rotation of the tag</param>
        /// <param name="confidence">Detection confidence</param>
        /// <param name="tagSize">Physical size of the tag in meters</param>
        /// <param name="positionOffset">Position offset applied by the controller (for centering anchor)</param>
        /// <param name="placeAtTagCenter">Whether to place anchors at exact tag center</param>
        /// <param name="confidenceThreshold">Minimum confidence threshold for anchor placement</param>
        /// <param name="keepOutZoneMultiplier">Multiplier for keep out zone radius</param>
        /// <param name="minKeepOutRadius">Minimum keep out zone radius</param>
        /// <param name="maxKeepOutRadius">Maximum keep out zone radius</param>
        /// <param name="enableDebugLogging">Whether to enable debug logging</param>
        public void ProcessTagDetection(
            int tagId,
            Vector3 position,
            Quaternion rotation,
            float confidence,
            float tagSize,
            Vector3 positionOffset,
            bool placeAtTagCenter,
            float confidenceThreshold,
            float keepOutZoneMultiplier,
            float minKeepOutRadius,
            float maxKeepOutRadius,
            bool enableDebugLogging
        )
        {
            if (!m_enableSpatialAnchors)
                return;

            // Early exit if anchor already exists for this tag
            if (m_anchorsById.ContainsKey(tagId))
            {
                if (enableDebugLogging && Time.frameCount % 300 == 0) // Log occasionally
                {
                    var anchor = m_anchorsById[tagId];
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Tag {tagId}: Anchor already exists at {anchor?.transform.position}, skipping. "
                            + $"Total anchors: {m_anchorsById.Count}"
                    );
                }
                return;
            }

            // Debug log to show what threshold is actually being used (reduced frequency)
            if (enableDebugLogging && Time.frameCount % 30 == 0)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Processing tag {tagId} with confidence {confidence:F3}, "
                        + $"threshold: {confidenceThreshold:F3}"
                );
            }

            // Get or create placement state for this tag
            if (!m_placementStates.TryGetValue(tagId, out var state))
            {
                state = new AnchorPlacementState(tagId);

                // If anchor already exists (e.g., from previous session), mark as placed
                if (m_anchorsById.ContainsKey(tagId))
                {
                    state.IsPlaced = true;
                    state.IsPlacementInProgress = false;
                }

                m_placementStates[tagId] = state;

                if (enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Started tracking tag {tagId} for anchor placement"
                            + $" (already placed: {state.IsPlaced})"
                    );
                }
            }

            // Early exit if anchor is already placed or creation is in progress
            if (state.IsPlaced || state.IsPlacementInProgress)
            {
                if (enableDebugLogging && Time.frameCount % 300 == 0) // Log occasionally
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Tag {tagId}: Anchor already "
                            + $"{(state.IsPlaced ? "placed" : "being placed")}, skipping"
                    );
                }
                return;
            }

            // Check keep-out zone early to prevent multiple concurrent anchor creations
            if (m_enableKeepOutZone)
            {
                // Adjust position to tag center for keep-out zone check if enabled
                var checkPosition = position;
                if (placeAtTagCenter)
                {
                    checkPosition = position - positionOffset;
                }

                // Check if position is within any existing keep out zone
                if (IsPositionInKeepOutZone(checkPosition, tagId))
                {
                    if (enableDebugLogging && Time.frameCount % 60 == 0) // Log less frequently
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Tag {tagId}: Position {checkPosition} is within keep out zone, "
                                + "skipping processing"
                        );
                    }
                    return;
                }
            }

            // Check if position has moved significantly since last frame
            var positionDelta = Vector3.Distance(position, state.LastPosition);
            var hasSignificantMovement = positionDelta > 0.05f; // 5cm threshold

            // Update state with current detection
            state.LastDetectionTime = Time.time;
            state.LastPosition = position;
            state.LastRotation = rotation;
            state.LastConfidence = confidence;

            // Check if we should increment stable frame count
            if (confidence >= confidenceThreshold)
            {
                if (hasSignificantMovement && state.StableFrameCount > 0)
                {
                    state.StableFrameCount = 0; // Reset if position is still moving
                }
                else
                {
                    state.StableFrameCount++;
                }
            }
            else
            {
                state.StableFrameCount = 0;
            }

            // Check if we should place an anchor
            if (
                state.ShouldPlaceAnchor(
                    confidenceThreshold,
                    m_requiredStableFrames,
                    m_maxDetectionTimeout
                )
            )
            {
                CreateAnchorForTag(
                    tagId,
                    position,
                    rotation,
                    tagSize,
                    positionOffset,
                    placeAtTagCenter,
                    keepOutZoneMultiplier,
                    minKeepOutRadius,
                    maxKeepOutRadius,
                    enableDebugLogging
                );
            }
        }

        /// <summary>
        /// Remove tracking for a tag that is no longer detected
        /// </summary>
        public void RemoveTagTracking(int tagId)
        {
            if (m_placementStates.TryGetValue(tagId, out var state))
            {
                var hasAnchor = m_anchorsById.ContainsKey(tagId);

                // Only remove from active tracking if no anchor has been placed yet
                // This prevents duplicate anchors when tags are temporarily lost and redetected
                if (!state.IsPlaced && !hasAnchor && !state.IsPlacementInProgress)
                {
                    m_placementStates.Remove(tagId);

                    if (EnableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Removed placement state for unplaced tag {tagId}"
                        );
                    }
                }
                // Only log occasionally for placed anchors to avoid spam
                else if (EnableDebugLogging && Time.frameCount % 300 == 0)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Tag {tagId} tracking: placed={state.IsPlaced}, "
                            + $"has anchor={hasAnchor}, in progress={state.IsPlacementInProgress}"
                    );
                }
            }
        }

        // Store tag sizes for use in keep-out zones
        private readonly Dictionary<int, float> m_tagSizes = new();

        // Store keep-out zone parameters for each tag
        private readonly Dictionary<
            int,
            (float multiplier, float minRadius, float maxRadius)
        > m_keepOutZoneParams = new();

        /// <summary>
        /// Create a spatial anchor for a specific tag using the building blocks
        /// </summary>
        private void CreateAnchorForTag(
            int tagId,
            Vector3 position,
            Quaternion rotation,
            float tagSize,
            Vector3 positionOffset,
            bool placeAtTagCenter,
            float keepOutZoneMultiplier,
            float minKeepOutRadius,
            float maxKeepOutRadius,
            bool enableDebugLogging
        )
        {
            if (enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] CreateAnchorForTag called for tag {tagId} at position {position}"
                );
            }

            // Get the placement state
            if (!m_placementStates.TryGetValue(tagId, out var state))
            {
                Debug.LogError(
                    $"[AprilTagSpatialAnchorManager] No placement state for tag {tagId}"
                );
                return;
            }

            // Check if anchor already exists
            if (m_anchorsById.ContainsKey(tagId))
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[AprilTagSpatialAnchorManager] Anchor already exists for tag {tagId}, skipping creation"
                    );
                }
                return;
            }

            // Prevent race condition - check if placement is already in progress
            if (state.IsPlacementInProgress)
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[AprilTagSpatialAnchorManager] Anchor creation already in progress for tag {tagId}, skipping"
                    );
                }
                return;
            }

            // Also check if already placed (additional safety check)
            if (state.IsPlaced)
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[AprilTagSpatialAnchorManager] Anchor already placed for tag {tagId}, skipping creation"
                    );
                }
                return;
            }

            state.IsPlacementInProgress = true;

            // Store tag size for later use
            m_tagSizes[tagId] = tagSize;

            // Use the position directly - it's already been calculated correctly
            // by AprilTagController and matches where the visualization is placed
            var anchorPosition = position;

            if (enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Placing anchor at position: {anchorPosition}"
                );
            }

            // Store keep-out zone parameters for later use with temporary multiplier
            var tempMultiplier = keepOutZoneMultiplier * 1.5f; // 50% larger during placement
            StoreKeepOutZoneParams(tagId, tempMultiplier, minKeepOutRadius, maxKeepOutRadius);

            // Create keep-out zone IMMEDIATELY to prevent duplicate anchor creation
            // This will be updated when the anchor is actually created
            CreateOrUpdateKeepOutZone(tagId, anchorPosition, tagSize);

            var tempRadius = CalculateKeepOutRadius(
                tagSize,
                tempMultiplier,
                minKeepOutRadius,
                maxKeepOutRadius
            );
            if (enableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Created temporary keep-out zone for tag {tagId} "
                        + $"at {anchorPosition} with radius {tempRadius:F3}m to prevent duplicates"
                );
            }

            // Store the tag ID for this position (will be used when anchor is created)
            StorePositionForTagId(tagId, anchorPosition, rotation);

            // Use the spawner building block to create the anchor at the specified position
            if (m_spatialAnchorSpawner != null)
            {
                // Ensure the spawner is configured to not follow hand
                m_spatialAnchorSpawner.FollowHand = false;

                // Store the original prefab to restore later
                var originalPrefab = m_spatialAnchorSpawner.AnchorPrefab;

                // Create a temporary prefab with the tag ID in the name
                var tempPrefab = CreateTaggedAnchorPrefab(tagId, originalPrefab);
                m_spatialAnchorSpawner.AnchorPrefab = tempPrefab;

                // Use the SpawnSpatialAnchor method with position and rotation parameters
                m_spatialAnchorSpawner.SpawnSpatialAnchor(anchorPosition, rotation);

                // Restore the original prefab
                m_spatialAnchorSpawner.AnchorPrefab = originalPrefab;

                // Clean up temporary prefab
                if (tempPrefab != originalPrefab)
                {
                    DestroyImmediate(tempPrefab);
                }

                if (enableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Requested anchor spawn for tag {tagId} via building block at {position}"
                    );
                }
            }
            else
            {
                Debug.LogError(
                    "[AprilTagSpatialAnchorManager] SpatialAnchorSpawnerBuildingBlock not available for anchor creation"
                );
                state.IsPlacementInProgress = false;
            }
        }

        /// <summary>
        /// Store position data for tag ID mapping when anchor is created
        /// </summary>
        private readonly Dictionary<string, int> m_pendingPositionToTagId = new();

        private void StorePositionForTagId(int tagId, Vector3 position, Quaternion rotation)
        {
            var positionKey = $"{position.x:F3},{position.y:F3},{position.z:F3}";
            m_pendingPositionToTagId[positionKey] = tagId;
        }

        private int GetTagIdForPosition(Vector3 position)
        {
            var positionKey = $"{position.x:F3},{position.y:F3},{position.z:F3}";
            if (m_pendingPositionToTagId.TryGetValue(positionKey, out var tagId))
            {
                m_pendingPositionToTagId.Remove(positionKey);
                return tagId;
            }
            return -1;
        }

        private void StoreKeepOutZoneParams(
            int tagId,
            float multiplier,
            float minRadius,
            float maxRadius
        )
        {
            m_keepOutZoneParams[tagId] = (multiplier, minRadius, maxRadius);
        }

        /// <summary>
        /// Handle anchor created event from building block
        /// </summary>
        private void OnBuildingBlockAnchorCreated(
            OVRSpatialAnchor anchor,
            OVRSpatialAnchor.OperationResult result
        )
        {
            if (result == OVRSpatialAnchor.OperationResult.Success && anchor != null)
            {
                // Try to find the tag ID for this anchor based on position
                var tagId = GetTagIdForPosition(anchor.transform.position);
                if (tagId >= 0)
                {
                    // Store the anchor
                    m_anchorsById[tagId] = anchor;
                    m_anchorGuidToTagId[anchor.Uuid] = tagId;

                    // Set the anchor name to include tag ID for persistence
                    if (anchor.gameObject != null)
                    {
                        var oldName = anchor.gameObject.name;
                        anchor.gameObject.name = $"AprilTagAnchor_Tag{tagId}";

                        if (EnableDebugLogging)
                        {
                            Debug.Log(
                                $"[AprilTagSpatialAnchorManager] Renamed anchor from '{oldName}' to '{anchor.gameObject.name}'"
                            );
                        }
                    }

                    // Update state
                    if (m_placementStates.TryGetValue(tagId, out var state))
                    {
                        state.IsPlaced = true;
                        state.IsPlacementInProgress = false;
                    }

                    // Save UUID to tag ID mapping for persistence
                    SaveUuidToTagIdMapping(anchor.Uuid, tagId);

                    // Save the anchor to local storage for persistence
                    SaveAnchorToLocalStorage(anchor);

                    // Update keep out zone at the anchor's actual position with normal radius
                    // First restore the original (non-temporary) multiplier
                    if (m_keepOutZoneParams.TryGetValue(tagId, out var zoneParams))
                    {
                        // Restore normal multiplier (remove the 1.5x temporary buffer)
                        var normalMultiplier = zoneParams.Item1 / 1.5f;
                        StoreKeepOutZoneParams(
                            tagId,
                            normalMultiplier,
                            zoneParams.Item2,
                            zoneParams.Item3
                        );
                    }

                    // Use stored tag size or default if not available
                    var tagSize = m_tagSizes.TryGetValue(tagId, out var size) ? size : 0.165f;
                    CreateOrUpdateKeepOutZone(tagId, anchor.transform.position, tagSize);

                    if (EnableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Successfully created anchor for tag {tagId} at {anchor.transform.position}. "
                                + $"State: IsPlaced={state.IsPlaced}, Total anchors: {m_anchorsById.Count}, "
                                + $"Keep-out zones: {m_keepOutZones.Count}"
                        );
                    }

                    // Fire event
                    OnAnchorCreated?.Invoke(tagId, anchor);
                }
                else
                {
                    if (EnableDebugLogging)
                    {
                        Debug.Log(
                            "[AprilTagSpatialAnchorManager] Anchor created but not associated with AprilTag detection"
                        );
                    }
                }
            }
            else if (result != OVRSpatialAnchor.OperationResult.Success)
            {
                // Anchor creation failed - clean up
                var tagId = GetTagIdForPosition(anchor?.transform.position ?? Vector3.zero);
                if (tagId >= 0 && m_placementStates.TryGetValue(tagId, out var state))
                {
                    state.IsPlacementInProgress = false;
                    // Remove the temporary keep-out zone
                    RemoveKeepOutZone(tagId);

                    if (EnableDebugLogging)
                    {
                        Debug.LogError(
                            $"[AprilTagSpatialAnchorManager] Failed to create anchor for tag {tagId}: {result}"
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Handle anchors loaded event from building block
        /// </summary>
        private void OnBuildingBlockAnchorsLoaded(List<OVRSpatialAnchor> loadedAnchors)
        {
            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] {loadedAnchors.Count} anchors loaded from storage"
                );

                // Log all anchor names and UUIDs to help with debugging
                foreach (var anchor in loadedAnchors)
                {
                    if (anchor != null && anchor.gameObject != null)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Loaded anchor: '{anchor.gameObject.name}' "
                                + $"at {anchor.transform.position}, UUID: {anchor.Uuid}"
                        );
                    }
                }
            }

            // Extract tag IDs from anchor names and restore associations
            foreach (var anchor in loadedAnchors)
            {
                if (anchor == null || anchor.gameObject == null)
                    continue;

                // Try to get tag ID from name first
                var tagId = ExtractTagIdFromAnchorName(anchor.gameObject.name);

                // If name doesn't contain tag ID, try UUID mapping (fallback)
                if (tagId < 0)
                {
                    tagId = LoadTagIdFromUuid(anchor.Uuid);

                    if (EnableDebugLogging)
                    {
                        if (tagId >= 0)
                        {
                            Debug.Log(
                                $"[AprilTagSpatialAnchorManager] Restored tag ID {tagId} from UUID mapping for anchor '{anchor.gameObject.name}' (UUID: {anchor.Uuid})"
                            );
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[AprilTagSpatialAnchorManager] No tag ID mapping found for UUID: {anchor.Uuid}"
                            );
                        }
                    }
                }

                if (tagId >= 0)
                {
                    // Check if anchor already exists for this tag (prevent duplicates)
                    if (m_anchorsById.ContainsKey(tagId))
                    {
                        if (EnableDebugLogging)
                        {
                            Debug.LogWarning(
                                $"[AprilTagSpatialAnchorManager] Duplicate anchor detected for tag {tagId}, keeping first one"
                            );
                        }
                        continue;
                    }

                    // Restore the association
                    m_anchorsById[tagId] = anchor;
                    m_anchorGuidToTagId[anchor.Uuid] = tagId;

                    // Mark as placed or update existing state
                    if (m_placementStates.TryGetValue(tagId, out var existingState))
                    {
                        existingState.IsPlaced = true;
                        existingState.IsPlacementInProgress = false;
                    }
                    else
                    {
                        var state = new AnchorPlacementState(tagId) { IsPlaced = true };
                        m_placementStates[tagId] = state;
                    }

                    // Create keep out zone for loaded anchor
                    // Use stored tag size or default if not available
                    var tagSize = m_tagSizes.TryGetValue(tagId, out var size) ? size : 0.165f;
                    CreateOrUpdateKeepOutZone(tagId, anchor.transform.position, tagSize);

                    if (EnableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Restored association for tag {tagId} from loaded anchor '{anchor.gameObject.name}' at position {anchor.transform.position}"
                        );
                    }

                    // Fire event so visualization is created
                    OnAnchorCreated?.Invoke(tagId, anchor);
                }
                else
                {
                    if (EnableDebugLogging)
                    {
                        Debug.LogWarning(
                            $"[AprilTagSpatialAnchorManager] Could not extract tag ID from loaded anchor name: '{anchor.gameObject.name}'. "
                                + $"Only anchors with names like 'AprilTagAnchor_Tag12' will be recognized."
                        );
                    }
                }
            }

            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Anchor loading complete: {m_anchorsById.Count} AprilTag anchors restored, "
                        + $"{loadedAnchors.Count - m_anchorsById.Count} other anchors ignored"
                );
            }
        }

        /// <summary>
        /// Handle all anchors erased event from building block
        /// </summary>
        private void OnBuildingBlockAllAnchorsErased(OVRSpatialAnchor.OperationResult result)
        {
            if (result == OVRSpatialAnchor.OperationResult.Success)
            {
                // Clear our tracking data
                m_anchorsById.Clear();
                m_anchorGuidToTagId.Clear();
                m_placementStates.Clear();
                m_keepOutZones.Clear();

                if (EnableDebugLogging)
                {
                    Debug.Log("[AprilTagSpatialAnchorManager] All anchors erased successfully");
                }

                // Fire event
                OnAllAnchorsCleared?.Invoke();
            }
        }

        /// <summary>
        /// Handle individual anchor erased event from building block
        /// </summary>
        private void OnBuildingBlockAnchorErased(
            OVRSpatialAnchor anchor,
            OVRSpatialAnchor.OperationResult result
        )
        {
            if (result == OVRSpatialAnchor.OperationResult.Success && anchor != null)
            {
                // Find and remove the tag ID for this anchor
                if (m_anchorGuidToTagId.TryGetValue(anchor.Uuid, out var tagId))
                {
                    m_anchorsById.Remove(tagId);
                    m_anchorGuidToTagId.Remove(anchor.Uuid);
                    RemoveKeepOutZone(tagId);

                    if (EnableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Anchor for tag {tagId} erased successfully"
                        );
                    }

                    // Fire event
                    OnAnchorRemoved?.Invoke(tagId);
                }
            }
        }

        /// <summary>
        /// Calculate the keep out zone radius based on tag size and configuration
        /// </summary>
        private float CalculateKeepOutRadius(
            float tagSize,
            float multiplier,
            float minRadius,
            float maxRadius
        )
        {
            if (!m_enableKeepOutZone)
                return 0f;

            var radius = tagSize * multiplier;
            radius = Mathf.Max(radius, minRadius);
            radius = Mathf.Min(radius, maxRadius);

            return radius;
        }

        /// <summary>
        /// Check if a position is within any existing keep out zone
        /// </summary>
        private bool IsPositionInKeepOutZone(Vector3 position, int excludeTagId = -1)
        {
            if (!m_enableKeepOutZone)
                return false;

            foreach (var kvp in m_keepOutZones)
            {
                if (kvp.Key == excludeTagId)
                    continue;

                if (kvp.Value.Contains(position))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Create or update a keep out zone for a tag
        /// </summary>
        private void CreateOrUpdateKeepOutZone(int tagId, Vector3 position, float tagSize)
        {
            if (!m_enableKeepOutZone)
                return;

            // Get stored parameters or use defaults
            var keepOutParams = m_keepOutZoneParams.TryGetValue(tagId, out var p)
                ? p
                : (0.3f, 0.02f, 0.1f); // Default values
            var multiplier = keepOutParams.Item1;
            var minRadius = keepOutParams.Item2;
            var maxRadius = keepOutParams.Item3;

            var radius = CalculateKeepOutRadius(tagSize, multiplier, minRadius, maxRadius);

            if (m_keepOutZones.ContainsKey(tagId))
            {
                m_keepOutZones[tagId].Center = position;
                m_keepOutZones[tagId].Radius = radius;
                m_keepOutZones[tagId].CreationTime = Time.time;
            }
            else
            {
                m_keepOutZones[tagId] = new KeepOutZone(tagId, position, radius);
            }
        }

        /// <summary>
        /// Remove a keep out zone for a tag
        /// </summary>
        private void RemoveKeepOutZone(int tagId)
        {
            m_keepOutZones.Remove(tagId);
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
                // Don't remove keep-out zones for tags that have anchors
                if (m_anchorsById.ContainsKey(kvp.Key))
                    continue;

                if (kvp.Value.IsExpired(maxAge))
                {
                    expiredZones.Add(kvp.Key);
                }
            }

            foreach (var tagId in expiredZones)
            {
                m_keepOutZones.Remove(tagId);

                if (EnableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Removed expired keep-out zone for tag {tagId}"
                    );
                }
            }
        }

        /// <summary>
        /// Clean up stale placement states for tags that haven't been detected recently
        /// </summary>
        private void CleanupStalePlacementStates()
        {
            var currentTime = Time.time;
            var staleTimeout = m_maxDetectionTimeout * 2;

            var staleTags = m_placementStates
                .Where(kv =>
                    !kv.Value.IsPlaced
                    && !kv.Value.IsPlacementInProgress
                    && !m_anchorsById.ContainsKey(kv.Key)
                    && // Extra safety: don't remove if anchor exists
                    (currentTime - kv.Value.LastDetectionTime) > staleTimeout
                )
                .Select(kv => kv.Key)
                .ToList();

            foreach (var tagId in staleTags)
            {
                // Final check before removal
                if (!m_anchorsById.ContainsKey(tagId))
                {
                    m_placementStates.Remove(tagId);

                    if (EnableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Cleaned up stale placement state for tag {tagId}"
                        );
                    }
                }
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
            m_anchorsById.TryGetValue(tagId, out var anchor);
            return anchor;
        }

        /// <summary>
        /// Get all tracked spatial anchors
        /// </summary>
        public List<OVRSpatialAnchor> GetAllAnchors()
        {
            return m_anchorsById.Values.Where(a => a != null).ToList();
        }

        /// <summary>
        /// Get tag ID for a given spatial anchor
        /// </summary>
        public int GetTagIdForAnchor(OVRSpatialAnchor anchor)
        {
            if (anchor == null)
                return -1;

            if (m_anchorGuidToTagId.TryGetValue(anchor.Uuid, out var tagId))
            {
                return tagId;
            }

            // Fallback: search by reference
            foreach (var kvp in m_anchorsById)
            {
                if (kvp.Value == anchor)
                {
                    return kvp.Key;
                }
            }

            return -1;
        }

        /// <summary>
        /// Update anchor mapping after manual repositioning
        /// </summary>
        public void UpdateAnchorMapping(int tagId, OVRSpatialAnchor anchor)
        {
            if (anchor == null)
                return;

            // Update the mapping
            m_anchorGuidToTagId[anchor.Uuid] = tagId;
            m_anchorsById[tagId] = anchor;

            // Save UUID mapping to PlayerPrefs
            SaveUuidToTagIdMapping(anchor.Uuid, tagId);

            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Updated anchor mapping for tag {tagId} at position {anchor.transform.position}"
                );
            }
        }

        /// <summary>
        /// Erase a specific anchor from Meta storage and tracking
        /// </summary>
        public async void EraseAnchor(OVRSpatialAnchor anchor)
        {
            if (anchor == null)
            {
                Debug.LogWarning("[AprilTagSpatialAnchorManager] Cannot erase null anchor");
                return;
            }

            var tagId = GetTagIdForAnchor(anchor);

            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Erasing anchor for tag {tagId} (UUID: {anchor.Uuid})"
                );
            }

            try
            {
                // Erase from Meta storage
                var eraseResult = await anchor.EraseAnchorAsync();

                if (eraseResult.Success)
                {
                    // Remove from tracking
                    if (tagId >= 0)
                    {
                        m_anchorsById.Remove(tagId);
                        m_placementStates.Remove(tagId);
                        RemoveKeepOutZone(tagId);
                    }
                    m_anchorGuidToTagId.Remove(anchor.Uuid);

                    // Remove from PlayerPrefs
                    var key = $"AprilTag_UUID_{anchor.Uuid}";
                    if (PlayerPrefs.HasKey(key))
                    {
                        PlayerPrefs.DeleteKey(key);
                        PlayerPrefs.Save();
                    }

                    if (EnableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Successfully erased anchor for tag {tagId}"
                        );
                    }

                    // Fire event
                    OnAnchorRemoved?.Invoke(tagId);

                    // Destroy the GameObject
                    if (anchor.gameObject != null)
                    {
                        Destroy(anchor.gameObject);
                    }
                }
                else
                {
                    Debug.LogError(
                        $"[AprilTagSpatialAnchorManager] Failed to erase anchor for tag {tagId}: {eraseResult.Status}"
                    );
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"[AprilTagSpatialAnchorManager] Exception erasing anchor: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Erase all AprilTag anchors from Meta storage and tracking
        /// </summary>
        public void EraseAllAnchors()
        {
            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Erasing all {m_anchorsById.Count} AprilTag anchors"
                );
            }

            // Use the building block to erase all anchors
            if (m_spatialAnchorCore != null)
            {
                m_spatialAnchorCore.EraseAllAnchors();

                // Clear local tracking immediately
                var anchorGuids = m_anchorGuidToTagId.Keys.ToList();
                m_anchorsById.Clear();
                m_anchorGuidToTagId.Clear();
                m_placementStates.Clear();
                m_keepOutZones.Clear();

                // Remove all from PlayerPrefs
                foreach (var guid in anchorGuids)
                {
                    var key = $"AprilTag_UUID_{guid}";
                    if (PlayerPrefs.HasKey(key))
                    {
                        PlayerPrefs.DeleteKey(key);
                    }
                }
                PlayerPrefs.Save();

                if (EnableDebugLogging)
                {
                    Debug.Log(
                        "[AprilTagSpatialAnchorManager] All anchors erased and tracking cleared"
                    );
                }
            }
            else
            {
                Debug.LogError(
                    "[AprilTagSpatialAnchorManager] Cannot erase anchors - SpatialAnchorCoreBuildingBlock not available"
                );
            }
        }

        /// <summary>
        /// Create a temporary anchor prefab with tag ID in the name
        /// </summary>
        private GameObject CreateTaggedAnchorPrefab(int tagId, GameObject originalPrefab)
        {
            if (originalPrefab == null)
            {
                // Create a simple default prefab if none provided
                // NOTE: Do NOT add OVRSpatialAnchor here - the spawner will add it
                var defaultGO = new GameObject($"AprilTagAnchor_Tag{tagId}");
                return defaultGO;
            }

            // If the original prefab already has the correct name pattern, use it directly
            if (originalPrefab.name.Contains($"Tag{tagId}"))
            {
                return originalPrefab;
            }

            // Create a temporary GameObject with the tag ID in the name
            var tempGO = new GameObject($"AprilTagAnchor_Tag{tagId}");

            // NOTE: Do NOT add OVRSpatialAnchor component here
            // The SpatialAnchorSpawnerBuildingBlock will add it automatically
            // Adding it here causes "component already added" errors

            // Copy visual components from the original prefab if it has children
            if (originalPrefab.transform.childCount > 0)
            {
                foreach (Transform child in originalPrefab.transform)
                {
                    Instantiate(child.gameObject, tempGO.transform);
                }
            }

            return tempGO;
        }

        /// <summary>
        /// Extract tag ID from anchor name
        /// </summary>
        private int ExtractTagIdFromAnchorName(string anchorName)
        {
            if (string.IsNullOrEmpty(anchorName))
                return -1;

            // Look for pattern "Tag{number}" in the name
            var match = System.Text.RegularExpressions.Regex.Match(anchorName, @"Tag(\d+)");
            if (match.Success && match.Groups.Count > 1)
            {
                if (int.TryParse(match.Groups[1].Value, out var tagId))
                {
                    return tagId;
                }
            }

            return -1;
        }

        /// <summary>
        /// Save UUID to tag ID mapping in PlayerPrefs for persistence
        /// </summary>
        private void SaveUuidToTagIdMapping(Guid uuid, int tagId)
        {
            var key = $"AprilTag_UUID_{uuid}";
            PlayerPrefs.SetInt(key, tagId);
            PlayerPrefs.Save();

            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Saved UUID mapping: {uuid} -> Tag {tagId}"
                );
            }
        }

        /// <summary>
        /// Load tag ID from UUID mapping in PlayerPrefs
        /// </summary>
        private int LoadTagIdFromUuid(Guid uuid)
        {
            var key = $"AprilTag_UUID_{uuid}";
            if (PlayerPrefs.HasKey(key))
            {
                var tagId = PlayerPrefs.GetInt(key);
                if (EnableDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTagSpatialAnchorManager] Found PlayerPrefs mapping: {uuid} -> Tag {tagId}"
                    );
                }
                return tagId;
            }

            if (EnableDebugLogging)
            {
                Debug.LogWarning(
                    $"[AprilTagSpatialAnchorManager] PlayerPrefs key not found: {key}"
                );
            }
            return -1;
        }

        /// <summary>
        /// Save an anchor to local storage for persistence across sessions
        /// </summary>
        private async void SaveAnchorToLocalStorage(OVRSpatialAnchor anchor)
        {
            if (anchor == null)
            {
                Debug.LogError("[AprilTagSpatialAnchorManager] Cannot save null anchor");
                return;
            }

            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"[AprilTagSpatialAnchorManager] Saving anchor '{anchor.gameObject.name}' to local storage..."
                );
            }

            // Save anchor to local storage using the new async API
            try
            {
                var saveResult = await anchor.SaveAnchorAsync();

                if (saveResult.Success)
                {
                    if (EnableDebugLogging)
                    {
                        Debug.Log(
                            $"[AprilTagSpatialAnchorManager] Successfully saved anchor '{anchor.gameObject.name}' to local storage (UUID: {anchor.Uuid})"
                        );
                    }
                }
                else
                {
                    Debug.LogError(
                        $"[AprilTagSpatialAnchorManager] Failed to save anchor '{anchor.gameObject.name}' to local storage. "
                            + $"Success: {saveResult.Success}, Status: {saveResult.Status}"
                    );
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"[AprilTagSpatialAnchorManager] Exception while saving anchor '{anchor.gameObject.name}': {ex.Message}\n{ex.StackTrace}"
                );
            }
        }
    }
}
