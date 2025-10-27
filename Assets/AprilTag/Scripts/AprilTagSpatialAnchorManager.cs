// Assets/AprilTag/AprilTagSpatialAnchorManager.cs
// Spatial anchor management system for AprilTag detection with confidence-based placement
// Integrates with Meta XR Building Blocks for controller-based anchor management

using System;
using System.Collections;
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

        [Header("Anchor Interaction")]
        [Tooltip(
            "Enable controller-based anchor manipulation (automatically creates interaction component)"
        )]
        [SerializeField]
        private bool m_enableAnchorInteraction = true;

        [Tooltip("Which controller to use for anchor interactions")]
        [SerializeField]
        private AprilTagAnchorInteraction.ControllerHand m_interactionHand =
            AprilTagAnchorInteraction.ControllerHand.Right;

        [Tooltip("Maximum distance for ray interaction (meters)")]
        [SerializeField]
        private float m_interactionRayDistance = 10f;

        [Tooltip("Enable ray visualization (line renderer)")]
        [SerializeField]
        private bool m_interactionShowRay = true;

        [Tooltip("Ray line color")]
        [SerializeField]
        private Color m_interactionRayColor = new Color(1f, 1f, 1f, 0.3f);

        [Tooltip("Ray line width")]
        [SerializeField]
        private float m_interactionRayWidth = 0.005f;

        [Tooltip("Highlight color for hovered anchors")]
        [SerializeField]
        private Color m_interactionHighlightColor = new Color(1f, 1f, 0f, 0.5f);

        [Header("Debug Logging")]
        [Tooltip("Enable debug logging for spatial anchor operations")]
        [SerializeField]
        private bool m_enableDebugLogging = false;

        [Tooltip("Frame interval for debug logs (higher = less frequent)")]
        [SerializeField]
        private int m_logInterval = 300;

        // Core data structures
        private Dictionary<int, OVRSpatialAnchor> m_anchorsById = new();
        private Dictionary<int, AnchorPlacementState> m_placementStates = new();
        private Dictionary<int, KeepOutZone> m_keepOutZones = new();
        private Dictionary<Guid, int> m_anchorGuidToTagId = new(); // Map anchor GUID to tag ID

        // Property for external access (compatibility)
        public bool EnableDebugLogging
        {
            get => m_enableDebugLogging;
            set => m_enableDebugLogging = value;
        }

        // Anchor save queue to prevent concurrent saves
        private readonly Queue<OVRSpatialAnchor> m_anchorSaveQueue = new();
        private bool m_isSavingAnchor = false;

        // Anchor interaction component (auto-created)
        private AprilTagAnchorInteraction m_anchorInteraction;

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
            StartCoroutine(InitializeAsync());
        }

        /// <summary>
        /// Initialize asynchronously to give building blocks time to set up
        /// </summary>
        private System.Collections.IEnumerator InitializeAsync()
        {
            // Initialize Meta XR Building Blocks
            InitializeBuildingBlocks();

            // Wait a frame for building blocks to initialize
            yield return null;

            // Subscribe to building block events
            SubscribeToBuildingBlockEvents();

            // Initialize anchor interaction if enabled
            InitializeAnchorInteraction();

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Initialized - anchor management enabled: {m_enableSpatialAnchors}, "
                        + $"required stable frames: {m_requiredStableFrames}, timeout: {m_maxDetectionTimeout:F1}s, "
                        + $"interaction enabled: {m_enableAnchorInteraction}"
                );
            }

            // Automatically load saved anchors on startup after a short delay
            // This ensures all systems are initialized before loading
            yield return LoadAnchorsOnStartup();
        }

        /// <summary>
        /// Initialize anchor interaction component (auto-create if enabled)
        /// </summary>
        private void InitializeAnchorInteraction()
        {
            if (!m_enableAnchorInteraction)
                return;

            // Check if interaction component already exists
            m_anchorInteraction = GetComponent<AprilTagAnchorInteraction>();

            if (m_anchorInteraction == null)
            {
                // Create the interaction component
                m_anchorInteraction = gameObject.AddComponent<AprilTagAnchorInteraction>();

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        "[SpatialAnchorManager] Auto-created AprilTagAnchorInteraction component"
                    );
                }
            }
            else
            {
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        "[SpatialAnchorManager] Found existing AprilTagAnchorInteraction component"
                    );
                }
            }

            // Configure interaction component with our settings using reflection
            ConfigureAnchorInteraction();
        }

        /// <summary>
        /// Configure the anchor interaction component with settings from this manager
        /// </summary>
        private void ConfigureAnchorInteraction()
        {
            if (m_anchorInteraction == null)
                return;

            var type = typeof(AprilTagAnchorInteraction);
            var bindingFlags =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            // Configure interaction settings via reflection
            type.GetField("m_activeHand", bindingFlags)
                ?.SetValue(m_anchorInteraction, m_interactionHand);
            type.GetField("m_maxRayDistance", bindingFlags)
                ?.SetValue(m_anchorInteraction, m_interactionRayDistance);
            type.GetField("m_showRayVisual", bindingFlags)
                ?.SetValue(m_anchorInteraction, m_interactionShowRay);
            type.GetField("m_rayColor", bindingFlags)
                ?.SetValue(m_anchorInteraction, m_interactionRayColor);
            type.GetField("m_rayWidth", bindingFlags)
                ?.SetValue(m_anchorInteraction, m_interactionRayWidth);
            type.GetField("m_highlightColor", bindingFlags)
                ?.SetValue(m_anchorInteraction, m_interactionHighlightColor);

            if (m_enableDebugLogging)
            {
                Debug.Log("[SpatialAnchorManager] Configured anchor interaction settings");
            }
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
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        "[SpatialAnchorManager] Loading saved anchors using explicit UUIDs from PlayerPrefs..."
                    );
                    Debug.Log(
                        $"[SpatialAnchorManager] Current tracked anchors before load: {m_anchorsById.Count}"
                    );
                }

                // Load anchors using explicit UUIDs from PlayerPrefs
                // This avoids the 4-anchor limit of LoadAnchorsFromDefaultLocalStorage()
                // Wait for the loading process to complete
                yield return LoadAnchorsFromPlayerPrefsCoroutine();

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[SpatialAnchorManager] After load attempt: {m_anchorsById.Count} anchors tracked. "
                            + $"If this is 0 and you expect anchors, they may not have been saved previously."
                    );
                }
            }
            else
            {
                Debug.LogWarning(
                    "[SpatialAnchorManager] Cannot load anchors - SpatialAnchorLoaderBuildingBlock not found"
                );
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe from building block events
            UnsubscribeFromBuildingBlockEvents();
        }

        /// <summary>
        /// Initialize the Meta XR Building Blocks (auto-create if needed)
        /// </summary>
        private void InitializeBuildingBlocks()
        {
            // Find or create SpatialAnchorSpawner
            if (m_spatialAnchorSpawner == null)
            {
                m_spatialAnchorSpawner = FindFirstObjectByType<SpatialAnchorSpawnerBuildingBlock>();
                if (m_spatialAnchorSpawner == null)
                {
                    var spawnerObj = new GameObject("SpatialAnchorSpawner");
                    m_spatialAnchorSpawner =
                        spawnerObj.AddComponent<SpatialAnchorSpawnerBuildingBlock>();

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            "[SpatialAnchorManager] Auto-created SpatialAnchorSpawnerBuildingBlock"
                        );
                    }
                }
            }

            // Find or create SpatialAnchorLoader
            if (m_spatialAnchorLoader == null)
            {
                m_spatialAnchorLoader = FindFirstObjectByType<SpatialAnchorLoaderBuildingBlock>();
                if (m_spatialAnchorLoader == null)
                {
                    var loaderObj = new GameObject("SpatialAnchorLoader");
                    m_spatialAnchorLoader =
                        loaderObj.AddComponent<SpatialAnchorLoaderBuildingBlock>();

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            "[SpatialAnchorManager] Auto-created SpatialAnchorLoaderBuildingBlock"
                        );
                    }
                }
            }

            // Find or create SpatialAnchorCore
            if (m_spatialAnchorCore == null)
            {
                m_spatialAnchorCore = FindFirstObjectByType<SpatialAnchorCoreBuildingBlock>();
                if (m_spatialAnchorCore == null)
                {
                    var coreObj = new GameObject("SpatialAnchorCore");
                    m_spatialAnchorCore = coreObj.AddComponent<SpatialAnchorCoreBuildingBlock>();

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            "[SpatialAnchorManager] Auto-created SpatialAnchorCoreBuildingBlock"
                        );
                    }
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
            // PERFORMANCE FIX: Clean up stale placement states infrequently (once per second)
            // Was running every frame (72-90 FPS) causing LINQ allocation pressure
            if (Time.frameCount % 72 == 0) // ~1 second at 72 FPS
            {
                CleanupStalePlacementStates();
            }

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
                if (enableDebugLogging && Time.frameCount % m_logInterval == 0)
                {
                    var anchor = m_anchorsById[tagId];
                    Debug.Log(
                        $"[SpatialAnchorManager] Tag {tagId}: Anchor already exists at {anchor?.transform.position}, skipping. "
                            + $"Total anchors: {m_anchorsById.Count}"
                    );
                }
                return;
            }

            // Debug log to show what threshold is actually being used (reduced frequency)
            if (enableDebugLogging && Time.frameCount % (m_logInterval / 10) == 0)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Processing tag {tagId} with confidence {confidence:F3}, "
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
                        $"[SpatialAnchorManager] Started tracking tag {tagId} for anchor placement"
                            + $" (already placed: {state.IsPlaced})"
                    );
                }
            }

            // Early exit if anchor is already placed or creation is in progress
            if (state.IsPlaced || state.IsPlacementInProgress)
            {
                if (enableDebugLogging && Time.frameCount % m_logInterval == 0)
                {
                    Debug.Log(
                        $"[SpatialAnchorManager] Tag {tagId}: Anchor already "
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
                    if (enableDebugLogging && Time.frameCount % (m_logInterval / 5) == 0)
                    {
                        Debug.Log(
                            $"[SpatialAnchorManager] Tag {tagId}: Position {checkPosition} is within keep out zone, "
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

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[SpatialAnchorManager] Removed placement state for unplaced tag {tagId}"
                        );
                    }
                }
                // Only log occasionally for placed anchors to avoid spam
                else if (m_enableDebugLogging && Time.frameCount % m_logInterval == 0)
                {
                    Debug.Log(
                        $"[SpatialAnchorManager] Tag {tagId} tracking: placed={state.IsPlaced}, "
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
                    $"[SpatialAnchorManager] CreateAnchorForTag called for tag {tagId} at position {position}"
                );
            }

            // Get the placement state
            if (!m_placementStates.TryGetValue(tagId, out var state))
            {
                Debug.LogError($"[SpatialAnchorManager] No placement state for tag {tagId}");
                return;
            }

            // Check if anchor already exists
            if (m_anchorsById.ContainsKey(tagId))
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[SpatialAnchorManager] Anchor already exists for tag {tagId}, skipping creation"
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
                        $"[SpatialAnchorManager] Anchor creation already in progress for tag {tagId}, skipping"
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
                        $"[SpatialAnchorManager] Anchor already placed for tag {tagId}, skipping creation"
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
                Debug.Log($"[SpatialAnchorManager] Placing anchor at position: {anchorPosition}");
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
                    $"[SpatialAnchorManager] Created temporary keep-out zone for tag {tagId} "
                        + $"at {anchorPosition} with radius {tempRadius:F3}m to prevent duplicates"
                );
            }

            // Store the tag ID for this position (will be used when anchor is created)
            StorePositionForTagId(tagId, anchorPosition, rotation);

            // Use the spawner building block to create the anchor at the specified position
            if (m_spatialAnchorSpawner != null && m_spatialAnchorSpawner.enabled)
            {
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
                        $"[SpatialAnchorManager] Requested anchor spawn for tag {tagId} via building block at {position}"
                    );
                }
            }
            else
            {
                Debug.LogError(
                    "[SpatialAnchorManager] SpatialAnchorSpawnerBuildingBlock not available for anchor creation"
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

                        if (m_enableDebugLogging)
                        {
                            Debug.Log(
                                $"[SpatialAnchorManager] Renamed anchor from '{oldName}' to '{anchor.gameObject.name}'"
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

                    // Queue the anchor for saving to prevent concurrent save issues
                    QueueAnchorForSave(anchor);

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

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[SpatialAnchorManager] Successfully created anchor for tag {tagId} at {anchor.transform.position}. "
                                + $"State: IsPlaced={state.IsPlaced}, Total anchors: {m_anchorsById.Count}, "
                                + $"Keep-out zones: {m_keepOutZones.Count}"
                        );
                    }

                    // Fire event
                    OnAnchorCreated?.Invoke(tagId, anchor);
                }
                else
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            "[SpatialAnchorManager] Anchor created but not associated with AprilTag detection"
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

                    if (m_enableDebugLogging)
                    {
                        Debug.LogError(
                            $"[SpatialAnchorManager] Failed to create anchor for tag {tagId}: {result}"
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
            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] {loadedAnchors.Count} anchors loaded from storage"
                );

                // Log all anchor names and UUIDs to help with debugging
                foreach (var anchor in loadedAnchors)
                {
                    if (anchor != null && anchor.gameObject != null)
                    {
                        Debug.Log(
                            $"[SpatialAnchorManager] Loaded anchor: '{anchor.gameObject.name}' "
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

                    if (m_enableDebugLogging)
                    {
                        if (tagId >= 0)
                        {
                            Debug.Log(
                                $"[SpatialAnchorManager] Restored tag ID {tagId} from UUID mapping for anchor '{anchor.gameObject.name}' (UUID: {anchor.Uuid})"
                            );
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[SpatialAnchorManager] No tag ID mapping found for UUID: {anchor.Uuid}"
                            );
                        }
                    }
                }

                if (tagId >= 0)
                {
                    // Check if anchor already exists for this tag (prevent duplicates)
                    if (m_anchorsById.ContainsKey(tagId))
                    {
                        if (m_enableDebugLogging)
                        {
                            Debug.LogWarning(
                                $"[SpatialAnchorManager] Duplicate anchor detected for tag {tagId}, keeping first one"
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

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[SpatialAnchorManager] Restored association for tag {tagId} from loaded anchor '{anchor.gameObject.name}' at position {anchor.transform.position}"
                        );
                    }

                    // Fire event so visualization is created
                    OnAnchorCreated?.Invoke(tagId, anchor);
                }
                else
                {
                    if (m_enableDebugLogging)
                    {
                        Debug.LogWarning(
                            $"[SpatialAnchorManager] Could not extract tag ID from loaded anchor name: '{anchor.gameObject.name}'. "
                                + $"Only anchors with names like 'AprilTagAnchor_Tag12' will be recognized."
                        );
                    }
                }
            }

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Anchor loading complete: {m_anchorsById.Count} AprilTag anchors restored, "
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

                if (m_enableDebugLogging)
                {
                    Debug.Log("[SpatialAnchorManager] All anchors erased successfully");
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

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[SpatialAnchorManager] Anchor for tag {tagId} erased successfully"
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

                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[SpatialAnchorManager] Removed expired keep-out zone for tag {tagId}"
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

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[SpatialAnchorManager] Cleaned up stale placement state for tag {tagId}"
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

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Updated anchor mapping for tag {tagId} at position {anchor.transform.position}"
                );
            }
        }

        /// <summary>
        /// Erase a specific anchor from Meta storage and tracking
        /// </summary>
        public void EraseAnchor(OVRSpatialAnchor anchor)
        {
            if (anchor == null)
            {
                Debug.LogWarning("[SpatialAnchorManager] Cannot erase null anchor");
                return;
            }

            StartCoroutine(EraseAnchorCoroutine(anchor));
        }

        /// <summary>
        /// Coroutine to erase an anchor from Meta storage
        /// </summary>
        private IEnumerator EraseAnchorCoroutine(OVRSpatialAnchor anchor)
        {
            var tagId = GetTagIdForAnchor(anchor);

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Erasing anchor for tag {tagId} (UUID: {anchor.Uuid})"
                );
            }

            // Start the erase operation
            var eraseTask = anchor.EraseAnchorAsync();

            // Wait for completion without blocking (OVRTask pattern)
            while (!eraseTask.IsCompleted)
            {
                yield return null;
            }

            // Get result from OVRTask
            var eraseResult = eraseTask.GetResult();

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

                if (m_enableDebugLogging)
                {
                    Debug.Log($"[SpatialAnchorManager] Successfully erased anchor for tag {tagId}");
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
                    $"[SpatialAnchorManager] Failed to erase anchor for tag {tagId}: {eraseResult.Status}"
                );
            }
        }

        /// <summary>
        /// Erase all AprilTag anchors from Meta storage and tracking
        /// </summary>
        public void EraseAllAnchors()
        {
            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Erasing all {m_anchorsById.Count} AprilTag anchors"
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

                if (m_enableDebugLogging)
                {
                    Debug.Log("[SpatialAnchorManager] All anchors erased and tracking cleared");
                }
            }
            else
            {
                Debug.LogError(
                    "[SpatialAnchorManager] Cannot erase anchors - SpatialAnchorCoreBuildingBlock not available"
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
        /// Stores bidirectional mapping for efficient lookup
        /// </summary>
        private void SaveUuidToTagIdMapping(Guid uuid, int tagId)
        {
            // Save UUID -> TagID mapping (for reverse lookup)
            var uuidKey = $"AprilTag_UUID_{uuid}";
            PlayerPrefs.SetInt(uuidKey, tagId);

            // Save TagID -> UUID mapping (for loading anchors on startup)
            var tagIdKey = $"AprilTag_TagID_{tagId}";
            PlayerPrefs.SetString(tagIdKey, uuid.ToString());

            PlayerPrefs.Save();

            if (m_enableDebugLogging)
            {
                Debug.Log($"[SpatialAnchorManager] Saved UUID mapping: {uuid} -> Tag {tagId}");
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
                if (m_enableDebugLogging)
                {
                    Debug.Log(
                        $"[SpatialAnchorManager] Found PlayerPrefs mapping: {uuid} -> Tag {tagId}"
                    );
                }
                return tagId;
            }

            if (m_enableDebugLogging)
            {
                Debug.LogWarning($"[SpatialAnchorManager] PlayerPrefs key not found: {key}");
            }
            return -1;
        }

        /// <summary>
        /// Load anchors by explicitly querying UUIDs stored in PlayerPrefs
        /// This bypasses the LoadAnchorsFromDefaultLocalStorage() 4-anchor limit
        /// Uses callback-based API with coroutine for non-blocking execution
        /// </summary>
        private IEnumerator LoadAnchorsFromPlayerPrefsCoroutine()
        {
            // Collect all stored UUIDs from PlayerPrefs
            var uuidsToLoad = new System.Collections.Generic.List<System.Guid>();

            // Check for common tag IDs (0-50 should be more than enough)
            for (int tagId = 0; tagId <= 50; tagId++)
            {
                string key = $"AprilTag_TagID_{tagId}";
                if (PlayerPrefs.HasKey(key))
                {
                    string uuidString = PlayerPrefs.GetString(key);
                    if (System.Guid.TryParse(uuidString, out System.Guid uuid))
                    {
                        uuidsToLoad.Add(uuid);

                        if (m_enableDebugLogging)
                        {
                            Debug.Log(
                                $"[SpatialAnchorManager] Found saved UUID for Tag {tagId}: {uuid}"
                            );
                        }
                    }
                }
            }

            if (uuidsToLoad.Count == 0)
            {
                if (m_enableDebugLogging)
                {
                    Debug.Log("[SpatialAnchorManager] No anchors found in PlayerPrefs to load");
                }
                yield break;
            }

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Loading {uuidsToLoad.Count} anchors from PlayerPrefs UUIDs..."
                );
            }

            // Track completion without blocking
            bool loadComplete = false;
            object lockObj = new object();
            int localizedCount = 0;

#pragma warning disable CS0618 // Type or member is obsolete
            var loadOptions = new OVRSpatialAnchor.LoadOptions { Uuids = uuidsToLoad };

            OVRSpatialAnchor.LoadUnboundAnchors(
                loadOptions,
                (unboundAnchors) =>
                {
                    if (unboundAnchors == null || unboundAnchors.Length == 0)
                    {
                        if (m_enableDebugLogging)
                        {
                            Debug.LogWarning(
                                $"[SpatialAnchorManager] No anchors were loaded from storage (requested {uuidsToLoad.Count} UUIDs)"
                            );
                        }
                        lock (lockObj)
                        {
                            loadComplete = true;
                        }
                        return;
                    }

                    if (m_enableDebugLogging)
                    {
                        Debug.Log(
                            $"[SpatialAnchorManager] Loaded {unboundAnchors.Length} unbound anchors (requested {uuidsToLoad.Count}), localizing..."
                        );
                    }

                    int totalToLocalize = unboundAnchors.Length;

                    foreach (var unboundAnchor in unboundAnchors)
                    {
                        // Store the UUID from the unbound anchor before localization
                        var anchorUuid = unboundAnchor.Uuid;

                        unboundAnchor.Localize(
                            (localizedUnbound, success) =>
                            {
                                if (success)
                                {
                                    // Create a GameObject for this anchor and bind it
                                    var anchorObject = new GameObject(
                                        $"AprilTagAnchor_Loaded_{anchorUuid}"
                                    );
                                    var spatialAnchor =
                                        anchorObject.AddComponent<OVRSpatialAnchor>();

                                    // Bind the unbound anchor to the GameObject
                                    localizedUnbound.BindTo(spatialAnchor);

                                    // Register the anchor
                                    int tagId = LoadTagIdFromUuid(anchorUuid);
                                    if (tagId >= 0)
                                    {
                                        // Rename to match the tag
                                        anchorObject.name = $"AprilTagAnchor_Tag{tagId}";

                                        m_anchorsById[tagId] = spatialAnchor;

                                        if (!m_placementStates.ContainsKey(tagId))
                                        {
                                            m_placementStates[tagId] = new AnchorPlacementState(
                                                tagId
                                            );
                                        }
                                        m_placementStates[tagId].IsPlaced = true;
                                        m_placementStates[tagId].LastPosition = spatialAnchor
                                            .transform
                                            .position;

                                        // Instantiate the visualization from the spawner's prefab as a child
                                        if (
                                            m_spatialAnchorSpawner != null
                                            && m_spatialAnchorSpawner.AnchorPrefab != null
                                        )
                                        {
                                            var prefab = m_spatialAnchorSpawner.AnchorPrefab;

                                            // Copy visual components from the prefab if it has children
                                            if (prefab.transform.childCount > 0)
                                            {
                                                foreach (Transform child in prefab.transform)
                                                {
                                                    var visualChild = Instantiate(
                                                        child.gameObject,
                                                        anchorObject.transform
                                                    );
                                                    visualChild.transform.localPosition =
                                                        Vector3.zero;
                                                    visualChild.transform.localRotation =
                                                        Quaternion.identity;
                                                }

                                                if (m_enableDebugLogging)
                                                {
                                                    Debug.Log(
                                                        $"[SpatialAnchorManager] Instantiated visualization for loaded Tag {tagId} anchor"
                                                    );
                                                }
                                            }
                                        }

                                        if (m_enableDebugLogging)
                                        {
                                            Debug.Log(
                                                $"[SpatialAnchorManager] Successfully loaded and bound Tag {tagId} anchor at {spatialAnchor.transform.position}"
                                            );
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning(
                                        $"[SpatialAnchorManager] Failed to localize anchor with UUID {anchorUuid}"
                                    );
                                }

                                bool shouldComplete = false;
                                lock (lockObj)
                                {
                                    localizedCount++;

                                    if (localizedCount >= totalToLocalize)
                                    {
                                        shouldComplete = true;
                                    }
                                }

                                if (shouldComplete)
                                {
                                    lock (lockObj)
                                    {
                                        loadComplete = true;
                                    }
                                }
                            }
                        );
                    }
                }
            );
#pragma warning restore CS0618 // Type or member is obsolete

            // Non-blocking wait - yield every frame until complete
            while (true)
            {
                bool isDone;
                lock (lockObj)
                {
                    isDone = loadComplete;
                }

                if (isDone)
                    break;

                yield return null; // Wait one frame
            }

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Finished loading anchors from PlayerPrefs. Total loaded: {m_anchorsById.Count}"
                );
            }
        }

        /// <summary>
        /// Queue an anchor for saving to prevent concurrent save issues
        /// </summary>
        private void QueueAnchorForSave(OVRSpatialAnchor anchor)
        {
            if (anchor == null)
                return;

            m_anchorSaveQueue.Enqueue(anchor);

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Queued anchor '{anchor.gameObject.name}' for saving (queue size: {m_anchorSaveQueue.Count})"
                );
            }

            // Start processing the queue if not already running
            if (!m_isSavingAnchor)
            {
                StartCoroutine(ProcessAnchorSaveQueueCoroutine());
            }
        }

        /// <summary>
        /// Process the anchor save queue one at a time to prevent concurrent saves
        /// </summary>
        private IEnumerator ProcessAnchorSaveQueueCoroutine()
        {
            m_isSavingAnchor = true;

            // Keep processing as long as there are items in the queue
            // This prevents restarting the processor and potential race conditions
            while (true)
            {
                // Check if queue is empty
                if (m_anchorSaveQueue.Count == 0)
                {
                    // Wait briefly to see if more items arrive
                    yield return new WaitForSeconds(0.1f);

                    // If still empty, we're done
                    if (m_anchorSaveQueue.Count == 0)
                    {
                        break;
                    }
                }

                var anchor = m_anchorSaveQueue.Dequeue();

                if (anchor != null)
                {
                    // Save the anchor and wait for it to complete
                    // SaveAnchorToLocalStorageCoroutine now properly waits for anchor.Created
                    yield return SaveAnchorToLocalStorageCoroutine(anchor);

                    // Small delay between saves to avoid overwhelming the SDK
                    yield return new WaitForSeconds(0.1f);
                }
            }

            // CRITICAL: Give Meta's SDK time to flush the last anchor to persistent storage
            // Even after SaveAnchorAsync reports success, the SDK needs time to commit
            // This is especially important for the last anchor in a batch
            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] All anchors saved, waiting for Meta SDK to flush to disk..."
                );
            }

            yield return new WaitForSeconds(1.0f);

            m_isSavingAnchor = false;

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Finished processing anchor save queue (processed until empty)"
                );
            }
        }

        /// <summary>
        /// Save an anchor to local storage for persistence across sessions
        /// </summary>
        private IEnumerator SaveAnchorToLocalStorageCoroutine(OVRSpatialAnchor anchor)
        {
            if (anchor == null)
            {
                Debug.LogError("[SpatialAnchorManager] Cannot save null anchor");
                yield break;
            }

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Saving anchor '{anchor.gameObject.name}' (UUID: {anchor.Uuid}) to local storage..."
                );
            }

            // Wait for anchor to be fully created AND localized before saving
            // This is CRITICAL - anchors must be both created and localized to persist correctly
            // Reference: Unity-StarterSamples SpatialAnchor example emphasizes checking Localized status
            float maxWaitTime = 5.0f; // Wait up to 5 seconds for localization
            float waitedTime = 0f;
            float checkInterval = 0.05f; // Check every 50ms

            while ((!anchor.Created || !anchor.Localized) && waitedTime < maxWaitTime)
            {
                yield return new WaitForSeconds(checkInterval);
                waitedTime += checkInterval;

                // Log progress for slow localization
                if (
                    Mathf.FloorToInt(waitedTime) > Mathf.FloorToInt(waitedTime - checkInterval)
                    && m_enableDebugLogging
                )
                {
                    Debug.Log(
                        $"[SpatialAnchorManager] Waiting for anchor '{anchor.gameObject.name}' localization... ({waitedTime:F1}s, created={anchor.Created}, localized={anchor.Localized})"
                    );
                }
            }

            if (!anchor.Created)
            {
                Debug.LogError(
                    $"[SpatialAnchorManager] Anchor '{anchor.gameObject.name}' (UUID: {anchor.Uuid}) was not created after waiting {waitedTime:F1}s. Cannot save."
                );
                yield break;
            }

            if (!anchor.Localized)
            {
                Debug.LogError(
                    $"[SpatialAnchorManager] Anchor '{anchor.gameObject.name}' (UUID: {anchor.Uuid}) created but NOT LOCALIZED after {waitedTime:F1}s. "
                        + $"Skipping save - unlocalized anchors will not persist across sessions."
                );
                yield break;
            }

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[SpatialAnchorManager] Anchor '{anchor.gameObject.name}' fully ready (created={anchor.Created}, localized={anchor.Localized}) after {waitedTime:F1}s, proceeding to save..."
                );
            }

            // Save anchor to local storage using OVRTask pattern
            var saveTask = anchor.SaveAnchorAsync();

            // Wait for the task to complete without blocking (OVRTask pattern)
            while (!saveTask.IsCompleted)
            {
                yield return null;
            }

            // Get result from OVRTask
            var saveResult = saveTask.GetResult();

            if (m_enableDebugLogging)
            {
                // Always log the full result for debugging
                Debug.Log(
                    $"[SpatialAnchorManager] Save result for anchor '{anchor.gameObject.name}' (UUID: {anchor.Uuid}): "
                        + $"Success={saveResult.Success}, Status={saveResult.Status}"
                );
            }

            if (!saveResult.Success)
            {
                Debug.LogError(
                    $"[SpatialAnchorManager] Failed to save anchor '{anchor.gameObject.name}' to local storage. "
                        + $"Success: {saveResult.Success}, Status: {saveResult.Status}"
                );
            }
        }
    }
}
