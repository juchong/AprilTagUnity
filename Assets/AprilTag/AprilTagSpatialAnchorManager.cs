// Assets/AprilTag/AprilTagSpatialAnchorManager.cs
// Spatial anchor management system for AprilTag detection with confidence-based placement
// Uses Meta XR Building Blocks SpatialAnchorCoreBuildingBlock

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Meta.XR.BuildingBlocks;

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
        [SerializeField] private bool enableSpatialAnchors = true;
        
        [Tooltip("Minimum confidence threshold for anchor placement (0.0 - 1.0)")]
        [Range(0.0f, 1.0f)]
        [SerializeField] private float minConfidenceThreshold = 0.3f; // Lowered for easier anchor placement
        
        [Tooltip("Number of consecutive high-confidence detections required before placing anchor")]
        [SerializeField] private int requiredStableFrames = 2; // Reduced for easier placement
        
        [Tooltip("Maximum time to wait for stable detection before giving up (seconds)")]
        [SerializeField] private float maxDetectionTimeout = 30f; // Increased for better success rate
        
        [Tooltip("Enable anchor persistence across app sessions")]
        [SerializeField] private bool persistAnchors = true;
        
        [Header("Spatial Anchor Core")]
        [Tooltip("Spatial Anchor Core building block (auto-found if null)")]
        [SerializeField] private SpatialAnchorCoreBuildingBlock spatialAnchorCore;
        
        [Tooltip("Prefab to use for spatial anchor creation")]
        [SerializeField] private GameObject anchorPrefab;
        
        [Header("Keep Out Zone")]
        [Tooltip("Enable keep out zone around tags to prevent duplicate anchor placement")]
        [SerializeField] private bool enableKeepOutZone = true; // Re-enabled with appropriate settings for 16.5cm tags
        
        [Tooltip("Multiplier for keep out zone radius based on tag size (e.g., 0.3 = 0.3x tag size)")]
        [Range(0.1f, 2.0f)]
        [SerializeField] private float keepOutZoneMultiplier = 0.3f; // Very small multiplier for 16.5cm tags
        
        [Tooltip("Minimum keep out zone radius in meters (prevents too small zones)")]
        [Range(0.01f, 0.5f)]
        [SerializeField] private float minKeepOutRadius = 0.02f; // 2cm minimum
        
        [Tooltip("Maximum keep out zone radius in meters (prevents too large zones)")]
        [Range(0.1f, 1.0f)]
        [SerializeField] private float maxKeepOutRadius = 0.1f; // 10cm maximum
        
        [Header("Quest Controller Input")]
        [Tooltip("Enable A button on right controller to clear all anchors")]
        [SerializeField] private bool enableClearAnchorsInput = true;
        
        [Header("Debug")]
        [Tooltip("Enable debug logging for anchor operations")]
        [SerializeField] private bool enableDebugLogging = true;

        // Core data structures
        private Dictionary<int, global::OVRSpatialAnchor> _anchorsById = new Dictionary<int, global::OVRSpatialAnchor>();
        private Dictionary<int, AnchorPlacementState> _placementStates = new Dictionary<int, AnchorPlacementState>();
        private Dictionary<int, Guid> _anchorUuids = new Dictionary<int, Guid>(); // For persistence
        private Dictionary<int, Guid> _pendingLoadData = new Dictionary<int, Guid>(); // For loading from storage
        
        // Keep out zone tracking
        private Dictionary<int, KeepOutZone> _keepOutZones = new Dictionary<int, KeepOutZone>();
        
        // Quest controller input
        private OVRInput.Controller _rightController = OVRInput.Controller.RTouch;
        private bool _lastAButtonState = false;
        
        // Events
        public static event Action<int, global::OVRSpatialAnchor> OnAnchorCreated;
        public static event Action<int> OnAnchorRemoved;
        public static event Action OnAllAnchorsCleared;

        /// <summary>
        /// Tracks the placement state for a specific tag ID
        /// </summary>
        [System.Serializable]
        private class AnchorPlacementState
        {
            public int tagId;
            public int stableFrameCount;
            public float firstDetectionTime;
            public float lastDetectionTime;
            public Vector3 lastPosition;
            public Quaternion lastRotation;
            public float lastConfidence;
            public bool isPlaced;
            public bool isPlacementInProgress;
            
            public AnchorPlacementState(int id)
            {
                tagId = id;
                stableFrameCount = 0;
                firstDetectionTime = Time.time;
                lastDetectionTime = Time.time;
                lastPosition = Vector3.zero;
                lastRotation = Quaternion.identity;
                lastConfidence = 0f;
                isPlaced = false;
                isPlacementInProgress = false;
            }
            
            public bool ShouldPlaceAnchor(float confidenceThreshold, int requiredFrames, float timeout)
            {
                if (isPlaced || isPlacementInProgress) 
                {
                    // Debug why placement is blocked
                    if (isPlaced) Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId}: Already placed, skipping");
                    if (isPlacementInProgress) Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId}: Placement in progress, skipping");
                    return false;
                }
                
                if (stableFrameCount >= requiredFrames && lastConfidence >= confidenceThreshold) 
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId}: Should place anchor - {stableFrameCount}/{requiredFrames} frames, confidence {lastConfidence:F3}/{confidenceThreshold:F3}");
                    return true;
                }
                
                if (Time.time - firstDetectionTime > timeout) 
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId}: Timeout reached, giving up");
                    return false;
                }
                
                return false;
            }
            
            public bool ShouldRemoveAnchor(float currentTime, float timeout)
            {
                if (!isPlaced) return false;
                return (currentTime - lastDetectionTime) > timeout;
            }
        }
        
        /// <summary>
        /// Represents a keep out zone around a placed anchor to prevent duplicates
        /// </summary>
        [System.Serializable]
        private class KeepOutZone
        {
            public int tagId;
            public Vector3 center;
            public float radius;
            public float creationTime;
            
            public KeepOutZone(int id, Vector3 position, float radius)
            {
                tagId = id;
                center = position;
                this.radius = radius;
                creationTime = Time.time;
            }
            
            public bool Contains(Vector3 position)
            {
                float distance = Vector3.Distance(center, position);
                return distance <= radius;
            }
            
            public bool IsExpired(float maxAge)
            {
                return (Time.time - creationTime) > maxAge;
            }
        }

        void Start()
        {
            // Initialize Spatial Anchor Core building block
            InitializeSpatialAnchorCore();
            
            // Load persisted anchors if enabled
            if (persistAnchors)
            {
                LoadAnchorsFromStorage();
            }
            
            if (enableDebugLogging)
            {
                Debug.Log($"[AprilTagSpatialAnchorManager] Initialized with confidence threshold: {minConfidenceThreshold:F3}, required stable frames: {requiredStableFrames}, timeout: {maxDetectionTimeout:F1}s");
            }
        }
        
        /// <summary>
        /// Initialize the Spatial Anchor Core building block
        /// </summary>
        private void InitializeSpatialAnchorCore()
        {
            if (enableDebugLogging)
            {
                Debug.Log("[AprilTagSpatialAnchorManager] Initializing SpatialAnchorCore building block...");
            }
            
            // Find or create Spatial Anchor Core building block
            if (spatialAnchorCore == null)
            {
                // Try to find existing Spatial Anchor Core building block
                spatialAnchorCore = FindFirstObjectByType<SpatialAnchorCoreBuildingBlock>();
                if (spatialAnchorCore == null)
                {
                    // Create a new Spatial Anchor Core building block
                    var spatialAnchorCoreGO = new GameObject("SpatialAnchorCore");
                    spatialAnchorCore = spatialAnchorCoreGO.AddComponent<SpatialAnchorCoreBuildingBlock>();
                    
                    if (enableDebugLogging)
                    {
                        Debug.Log("[AprilTagSpatialAnchorManager] Created new SpatialAnchorCore building block");
                    }
                }
                else
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log("[AprilTagSpatialAnchorManager] Found existing SpatialAnchorCore building block");
                    }
                }
            }
            
            // Subscribe to events
            if (spatialAnchorCore != null)
            {
                // Unsubscribe first to avoid duplicate subscriptions
                spatialAnchorCore.OnAnchorCreateCompleted.RemoveListener(OnAnchorCreatedFromBuildingBlock);
                spatialAnchorCore.OnAnchorsLoadCompleted.RemoveListener(OnAnchorsLoaded);
                spatialAnchorCore.OnAnchorsEraseAllCompleted.RemoveListener(OnAllAnchorsErased);
                spatialAnchorCore.OnAnchorEraseCompleted.RemoveListener(OnAnchorErased);
                
                // Subscribe to events
                spatialAnchorCore.OnAnchorCreateCompleted.AddListener(OnAnchorCreatedFromBuildingBlock);
                spatialAnchorCore.OnAnchorsLoadCompleted.AddListener(OnAnchorsLoaded);
                spatialAnchorCore.OnAnchorsEraseAllCompleted.AddListener(OnAllAnchorsErased);
                spatialAnchorCore.OnAnchorEraseCompleted.AddListener(OnAnchorErased);
                
                if (enableDebugLogging)
                {
                    Debug.Log("[AprilTagSpatialAnchorManager] Successfully subscribed to SpatialAnchorCore events");
                    Debug.Log($"[AprilTagSpatialAnchorManager] SpatialAnchorCore GameObject: {spatialAnchorCore.gameObject.name}");
                    Debug.Log($"[AprilTagSpatialAnchorManager] SpatialAnchorCore active: {spatialAnchorCore.gameObject.activeInHierarchy}");
                }
            }
            else
            {
                Debug.LogError("[AprilTagSpatialAnchorManager] Failed to initialize SpatialAnchorCore building block");
            }
        }

        void Update()
        {
            // Handle Quest controller input for clearing anchors
            if (enableClearAnchorsInput)
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

        void OnDestroy()
        {
            // Save anchors before destruction
            if (persistAnchors)
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
        public void ProcessTagDetection(int tagId, Vector3 position, Quaternion rotation, float confidence, float tagSize = 0.08f)
        {
            if (!enableSpatialAnchors || _isClearingAnchors) return;
            
            // Debug log to show what threshold is actually being used (reduced frequency)
            if (enableDebugLogging && Time.frameCount % 30 == 0) // Log every 30 frames (0.5 seconds at 60fps)
            {
                Debug.Log($"[AprilTagSpatialAnchorManager] Processing tag {tagId} with confidence {confidence:F3}, threshold: {minConfidenceThreshold:F3}");
                Debug.Log($"[AprilTagSpatialAnchorManager] Currently tracking {_anchorsById.Count} anchors: [{string.Join(", ", _anchorsById.Keys)}]");
            }

            // Get or create placement state for this tag
            if (!_placementStates.TryGetValue(tagId, out var state))
            {
                state = new AnchorPlacementState(tagId);
                _placementStates[tagId] = state;
                
                if (enableDebugLogging)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Started tracking tag {tagId} for anchor placement");
                }
            }

            // Update state with current detection
            state.lastDetectionTime = Time.time;
            state.lastPosition = position;
            state.lastRotation = rotation;
            state.lastConfidence = confidence;
            
            // Reset placement in progress if it's been too long (timeout protection)
            if (state.isPlacementInProgress && (Time.time - state.firstDetectionTime) > 10f)
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"[AprilTagSpatialAnchorManager] Tag {tagId}: Placement timeout, resetting placement in progress flag");
                }
                state.isPlacementInProgress = false;
            }

            // Check if we should increment stable frame count
            if (confidence >= minConfidenceThreshold)
            {
                state.stableFrameCount++;
                
                if (enableDebugLogging && Time.frameCount % 60 == 0) // Log every 60 frames (1 second at 60fps)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId}: {state.stableFrameCount}/{requiredStableFrames} stable frames, confidence: {confidence:F3} (threshold: {minConfidenceThreshold:F3})");
                }
            }
            else
            {
                // Reset stable frame count if confidence drops
                if (enableDebugLogging && state.stableFrameCount > 0)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId}: Confidence dropped below threshold ({confidence:F3} < {minConfidenceThreshold:F3}), resetting stable frames");
                }
                state.stableFrameCount = 0;
            }
            
            // Log timeout progress (reduced frequency)
            if (enableDebugLogging && (Time.time - state.firstDetectionTime) > 5f && Time.frameCount % 120 == 0) // Log every 2 seconds
            {
                float timeRemaining = maxDetectionTimeout - (Time.time - state.firstDetectionTime);
                Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId}: Timeout in {timeRemaining:F1}s, frames: {state.stableFrameCount}/{requiredStableFrames}, confidence: {confidence:F3}");
            }

            // Check if we should place an anchor
            if (state.ShouldPlaceAnchor(minConfidenceThreshold, requiredStableFrames, maxDetectionTimeout))
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId}: Should place anchor - checking keep out zones");
                }
                
                // Check if position is within any existing keep out zone
                if (IsPositionInKeepOutZone(position, tagId))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId}: Position {position} is within keep out zone, skipping anchor creation");
                        Debug.Log($"[AprilTagSpatialAnchorManager] Current keep out zones: {_keepOutZones.Count}");
                        foreach (var kvp in _keepOutZones)
                        {
                            float distance = Vector3.Distance(kvp.Value.center, position);
                            Debug.Log($"[AprilTagSpatialAnchorManager] - Tag {kvp.Key}: center={kvp.Value.center}, radius={kvp.Value.radius:F3}m, distance={distance:F3}m");
                        }
                    }
                    return; // Skip anchor creation
                }
                
                if (enableDebugLogging)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId}: No keep out zone conflict - creating anchor");
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
            if (_placementStates.TryGetValue(tagId, out var state))
            {
                // Don't immediately remove anchors when tags are lost - they should persist
                // Only remove from active tracking, but keep the anchor
                if (enableDebugLogging)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Tag {tagId} temporarily lost - keeping anchor if placed");
                }
                
                // Remove from active tracking but don't remove the anchor
                _placementStates.Remove(tagId);
            }
        }

        /// <summary>
        /// Create a spatial anchor for a specific tag using direct instantiation
        /// </summary>
        private void CreateAnchorForTag(int tagId, Vector3 position, Quaternion rotation, float tagSize)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[AprilTagSpatialAnchorManager] CreateAnchorForTag called for tag {tagId} at position {position}");
            }
            
            if (_anchorsById.ContainsKey(tagId))
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"[AprilTagSpatialAnchorManager] Anchor already exists for tag {tagId}, skipping creation");
                }
                return;
            }

            var state = _placementStates[tagId];
            state.isPlacementInProgress = true;

            // Use assigned prefab
            var prefab = anchorPrefab;
            if (prefab == null)
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning("[AprilTagSpatialAnchorManager] No anchor prefab assigned - creating minimal anchor without visualization");
                }
                
                // Create minimal anchor GameObject without visual representation
                prefab = new GameObject("AprilTag_SpatialAnchor");
                prefab.AddComponent<global::OVRSpatialAnchor>();
            }

            // Create the anchor GameObject directly
            var anchorGO = Instantiate(prefab, position, rotation);
            var spatialAnchor = anchorGO.GetComponent<global::OVRSpatialAnchor>();
            
            if (spatialAnchor == null)
            {
                // Add OVRSpatialAnchor component if it doesn't exist
                spatialAnchor = anchorGO.AddComponent<global::OVRSpatialAnchor>();
            }

            // Store the anchor
            _anchorsById[tagId] = spatialAnchor;
            _anchorUuids[tagId] = spatialAnchor.Uuid;
            
            // Update state
            state.isPlaced = true;
            state.isPlacementInProgress = false;
            state.lastPosition = position;
            state.lastRotation = rotation;
            
            // Create keep out zone for this tag
            CreateOrUpdateKeepOutZone(tagId, position, tagSize);

            if (enableDebugLogging)
            {
                Debug.Log($"[AprilTagSpatialAnchorManager] Successfully created spatial anchor for tag {tagId} at {position} with confidence {state.lastConfidence:F2}");
                Debug.Log($"[AprilTagSpatialAnchorManager] Anchor GameObject name: {anchorGO.name}, active: {anchorGO.activeInHierarchy}");
                Debug.Log($"[AprilTagSpatialAnchorManager] Total anchors tracked: {_anchorsById.Count}");
            }
            
            // Fire event
            OnAnchorCreated?.Invoke(tagId, spatialAnchor);
        }

        /// <summary>
        /// Remove a spatial anchor for a specific tag
        /// </summary>
        private void RemoveAnchorForTag(int tagId)
        {
            if (_anchorsById.TryGetValue(tagId, out var anchor))
            {
                if (anchor != null && anchor.gameObject != null)
                {
                    DestroyImmediate(anchor.gameObject);
                }
                
                _anchorsById.Remove(tagId);
                _anchorUuids.Remove(tagId);
                
                // Remove keep out zone for this tag
                RemoveKeepOutZone(tagId);
                
                if (enableDebugLogging)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Removed spatial anchor for tag {tagId}");
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
            if (_isClearingAnchors)
            {
                if (enableDebugLogging)
                {
                    Debug.Log("[AprilTagSpatialAnchorManager] Clear operation already in progress, skipping");
                }
                return;
            }

            var tagIds = _anchorsById.Keys.ToList();
            
            if (enableDebugLogging)
            {
                Debug.Log($"[AprilTagSpatialAnchorManager] Requested clearing all spatial anchors ({tagIds.Count} tracked)");
            }
            
            // Temporarily disable anchor creation to prevent immediate recreation
            _isClearingAnchors = true;
            
            // Always use the fallback method since the building block erase is unreliable
            FallbackClearAllAnchors();
        }
        
        private bool _isClearingAnchors = false;
        
        private System.Collections.IEnumerator ReenableAnchorCreationAfterDelay()
        {
            yield return new WaitForSeconds(2.0f); // Wait 2 seconds before re-enabling
            _isClearingAnchors = false;
            
            if (enableDebugLogging)
            {
                Debug.Log("[AprilTagSpatialAnchorManager] Re-enabled anchor creation after clearing");
            }
        }
        
        
        /// <summary>
        /// Fallback method to manually clear all anchors and their visual representations
        /// </summary>
        private void FallbackClearAllAnchors()
        {
            if (enableDebugLogging)
            {
                Debug.Log("[AprilTagSpatialAnchorManager] Using fallback method to clear all anchors");
            }
            
            // Get count before clearing for logging
            var anchorCount = _anchorsById.Count;
            
            // Log all GameObjects in the scene that might be spatial anchors
            if (enableDebugLogging)
            {
                var allGameObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                var spatialAnchorObjects = allGameObjects.Where(go => go.name.Contains("SpatialAnchor") || go.name.Contains("AprilTag")).ToArray();
                Debug.Log($"[AprilTagSpatialAnchorManager] Found {spatialAnchorObjects.Length} potential spatial anchor GameObjects in scene:");
                foreach (var go in spatialAnchorObjects)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] - {go.name} (active: {go.activeInHierarchy})");
                }
            }
            
            // Manually destroy all anchor GameObjects
            var anchorsToDestroy = new List<global::OVRSpatialAnchor>(_anchorsById.Values);
            
            foreach (var anchor in anchorsToDestroy)
            {
                if (anchor != null && anchor.gameObject != null)
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[AprilTagSpatialAnchorManager] Manually destroying tracked anchor GameObject: {anchor.gameObject.name}");
                    }
                    DestroyImmediate(anchor.gameObject);
                }
            }
            
            // Also destroy any GameObjects with OVRSpatialAnchor components that might not be tracked
            var allSpatialAnchors = FindObjectsByType<global::OVRSpatialAnchor>(FindObjectsSortMode.None);
            foreach (var anchor in allSpatialAnchors)
            {
                if (anchor != null && anchor.gameObject != null)
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[AprilTagSpatialAnchorManager] Destroying untracked spatial anchor: {anchor.gameObject.name}");
                    }
                    DestroyImmediate(anchor.gameObject);
                }
            }
            
            // Clear our tracking data
            _anchorsById.Clear();
            _anchorUuids.Clear();
            _placementStates.Clear();
            _keepOutZones.Clear();
            
            if (enableDebugLogging)
            {
                Debug.Log($"[AprilTagSpatialAnchorManager] Fallback clearing completed - {anchorCount} anchors and their visual representations removed");
            }
            
            // Re-enable anchor creation
            StartCoroutine(ReenableAnchorCreationAfterDelay());
            
            // Fire event
            OnAllAnchorsCleared?.Invoke();
        }

        /// <summary>
        /// Handle Quest controller input for clearing anchors
        /// </summary>
        private void HandleClearAnchorsInput()
        {
            // Check for A button press on right controller
            bool aButtonPressed = OVRInput.GetDown(OVRInput.Button.One, _rightController);
            
            if (aButtonPressed && !_lastAButtonState)
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] A button pressed - clearing all anchors (currently tracking {_anchorsById.Count} anchors)");
                }
                ClearAllAnchors();
            }
            
            _lastAButtonState = aButtonPressed;
        }

        /// <summary>
        /// Handle anchor creation completion from Spatial Anchor Core building block
        /// </summary>
        private void OnAnchorCreatedFromBuildingBlock(global::OVRSpatialAnchor anchor, global::OVRSpatialAnchor.OperationResult result)
        {
            if (result == global::OVRSpatialAnchor.OperationResult.Success)
            {
                // Find the tag ID for this anchor based on position
                int tagId = FindTagIdForAnchor(anchor);
                if (tagId != -1)
                {
                    _anchorsById[tagId] = anchor;
                    _anchorUuids[tagId] = anchor.Uuid;
                    
                    if (_placementStates.TryGetValue(tagId, out var state))
                    {
                        state.isPlaced = true;
                        state.isPlacementInProgress = false;
                    }
                    
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[AprilTagSpatialAnchorManager] Successfully created spatial anchor for tag {tagId} at {anchor.transform.position}");
                    }
                    
                    // Fire event
                    OnAnchorCreated?.Invoke(tagId, anchor);
                }
            }
            else
            {
                Debug.LogError($"[AprilTagSpatialAnchorManager] Failed to create spatial anchor: {result}");
            }
        }

        /// <summary>
        /// Handle anchors loaded completion from Spatial Anchor Core building block
        /// </summary>
        private void OnAnchorsLoaded(List<global::OVRSpatialAnchor> loadedAnchors)
        {
            foreach (var anchor in loadedAnchors)
            {
                // Find the tag ID for this anchor using the pending load data
                int tagId = -1;
                foreach (var kvp in _pendingLoadData)
                {
                    if (kvp.Value == anchor.Uuid)
                    {
                        tagId = kvp.Key;
                        break;
                    }
                }
                
                if (tagId != -1)
                {
                    _anchorsById[tagId] = anchor;
                    _anchorUuids[tagId] = anchor.Uuid;
                    
                    // Mark as placed
                    var state = new AnchorPlacementState(tagId);
                    state.isPlaced = true;
                    _placementStates[tagId] = state;
                    
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[AprilTagSpatialAnchorManager] Successfully loaded spatial anchor for tag {tagId} at {anchor.transform.position}");
                    }
                }
            }
            
            // Clear pending load data
            _pendingLoadData.Clear();
            
            if (enableDebugLogging)
            {
                Debug.Log($"[AprilTagSpatialAnchorManager] Successfully loaded {loadedAnchors.Count} anchors from storage");
            }
        }

        /// <summary>
        /// Handle all anchors erased completion from Spatial Anchor Core building block
        /// Note: This method is kept for compatibility but is not used in the current implementation
        /// </summary>
        private void OnAllAnchorsErased(global::OVRSpatialAnchor.OperationResult result)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[AprilTagSpatialAnchorManager] OnAllAnchorsErased called with result: {result} (not used in current implementation)");
            }
        }

        /// <summary>
        /// Handle individual anchor erased completion from Spatial Anchor Core building block
        /// </summary>
        private void OnAnchorErased(global::OVRSpatialAnchor anchor, global::OVRSpatialAnchor.OperationResult result)
        {
            if (result == global::OVRSpatialAnchor.OperationResult.Success)
            {
                // Find and remove the tag ID for this anchor
                int tagId = FindTagIdForAnchor(anchor);
                if (tagId != -1)
                {
                    _anchorsById.Remove(tagId);
                    _anchorUuids.Remove(tagId);
                    
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[AprilTagSpatialAnchorManager] Successfully removed spatial anchor for tag {tagId}");
                    }
                    
                    // Fire event
                    OnAnchorRemoved?.Invoke(tagId);
                }
            }
            else
            {
                Debug.LogError($"[AprilTagSpatialAnchorManager] Failed to erase spatial anchor: {result}");
            }
        }

        /// <summary>
        /// Calculate the keep out zone radius based on tag size and configuration
        /// </summary>
        /// <param name="tagSize">The physical size of the AprilTag in meters</param>
        /// <returns>The radius of the keep out zone in meters</returns>
        private float CalculateKeepOutRadius(float tagSize)
        {
            if (!enableKeepOutZone) return 0f;
            
            // Calculate radius based on tag size and multiplier
            float radius = tagSize * keepOutZoneMultiplier;
            
            // Apply min/max constraints
            radius = Mathf.Max(radius, minKeepOutRadius);
            radius = Mathf.Min(radius, maxKeepOutRadius);
            
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
            if (!enableKeepOutZone) return false;
            
            foreach (var kvp in _keepOutZones)
            {
                if (kvp.Key == excludeTagId) continue; // Skip the tag we're updating
                
                if (kvp.Value.Contains(position))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[AprilTagSpatialAnchorManager] Position {position} is within keep out zone for tag {kvp.Key} (radius: {kvp.Value.radius:F3}m)");
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
            if (!enableKeepOutZone) return;
            
            float radius = CalculateKeepOutRadius(tagSize);
            
            if (_keepOutZones.ContainsKey(tagId))
            {
                // Update existing zone
                _keepOutZones[tagId].center = position;
                _keepOutZones[tagId].radius = radius;
                _keepOutZones[tagId].creationTime = Time.time;
            }
            else
            {
                // Create new zone
                _keepOutZones[tagId] = new KeepOutZone(tagId, position, radius);
            }
            
            if (enableDebugLogging)
            {
                Debug.Log($"[AprilTagSpatialAnchorManager] Created/updated keep out zone for tag {tagId}: center={position}, radius={radius:F3}m");
            }
        }
        
        /// <summary>
        /// Remove a keep out zone for a tag
        /// </summary>
        /// <param name="tagId">The tag ID</param>
        private void RemoveKeepOutZone(int tagId)
        {
            if (_keepOutZones.ContainsKey(tagId))
            {
                _keepOutZones.Remove(tagId);
                
                if (enableDebugLogging)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Removed keep out zone for tag {tagId}");
                }
            }
        }
        
        /// <summary>
        /// Clean up expired keep out zones
        /// </summary>
        private void CleanupExpiredKeepOutZones()
        {
            if (!enableKeepOutZone) return;
            
            var expiredZones = new List<int>();
            float maxAge = 300f; // 5 minutes
            
            foreach (var kvp in _keepOutZones)
            {
                if (kvp.Value.IsExpired(maxAge))
                {
                    expiredZones.Add(kvp.Key);
                }
            }
            
            foreach (var tagId in expiredZones)
            {
                _keepOutZones.Remove(tagId);
                
                if (enableDebugLogging)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Cleaned up expired keep out zone for tag {tagId}");
                }
            }
        }
        

        /// <summary>
        /// Find the tag ID for a given anchor based on position
        /// </summary>
        private int FindTagIdForAnchor(global::OVRSpatialAnchor anchor)
        {
            // Find the tag ID by matching position with pending placement states
            foreach (var kvp in _placementStates)
            {
                if (kvp.Value.isPlacementInProgress && 
                    Vector3.Distance(kvp.Value.lastPosition, anchor.transform.position) < 0.01f)
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
            var staleTimeout = maxDetectionTimeout * 2; // Give extra time before cleanup
            
            var staleTags = _placementStates
                .Where(kv => !kv.Value.isPlaced && (currentTime - kv.Value.lastDetectionTime) > staleTimeout)
                .Select(kv => kv.Key)
                .ToList();
            
            foreach (var tagId in staleTags)
            {
                _placementStates.Remove(tagId);
                
                if (enableDebugLogging)
                {
                    Debug.Log($"[AprilTagSpatialAnchorManager] Cleaned up stale placement state for tag {tagId}");
                }
            }
        }

        /// <summary>
        /// Save anchors to persistent storage
        /// </summary>
        private void SaveAnchorsToStorage()
        {
            if (!persistAnchors) return;
            
            var anchorData = new List<AnchorData>();
            
            foreach (var kvp in _anchorUuids)
            {
                var tagId = kvp.Key;
                var uuid = kvp.Value;
                
                if (_anchorsById.TryGetValue(tagId, out var anchor) && anchor != null)
                {
                    anchorData.Add(new AnchorData
                    {
                        tagId = tagId,
                        uuid = uuid.ToString(),
                        position = anchor.transform.position,
                        rotation = anchor.transform.rotation
                    });
                }
            }
            
            var json = JsonUtility.ToJson(new AnchorDataCollection { anchors = anchorData });
            PlayerPrefs.SetString("AprilTag_Anchors", json);
            PlayerPrefs.Save();
            
            if (enableDebugLogging)
            {
                Debug.Log($"[AprilTagSpatialAnchorManager] Saved {anchorData.Count} anchors to storage");
            }
        }

        /// <summary>
        /// Load anchors from persistent storage using Spatial Anchor Core building block
        /// </summary>
        private void LoadAnchorsFromStorage()
        {
            if (!persistAnchors || spatialAnchorCore == null) return;
            
            var json = PlayerPrefs.GetString("AprilTag_Anchors", "");
            if (string.IsNullOrEmpty(json)) return;
            
            try
            {
                var data = JsonUtility.FromJson<AnchorDataCollection>(json);
                if (data.anchors.Count == 0) return;
                
                // Convert string UUIDs to Guids
                var uuids = new List<Guid>();
                var tagIdToUuid = new Dictionary<int, Guid>();
                
                foreach (var anchorData in data.anchors)
                {
                    if (Guid.TryParse(anchorData.uuid, out var uuid))
                    {
                        uuids.Add(uuid);
                        tagIdToUuid[anchorData.tagId] = uuid;
                    }
                }
                
                if (uuids.Count > 0)
                {
                    // Use Spatial Anchor Core building block to load anchors
                    spatialAnchorCore.LoadAndInstantiateAnchors(anchorPrefab, uuids);
                    
                    // Store mapping for when anchors are loaded
                    _pendingLoadData = tagIdToUuid;
                    
                    if (enableDebugLogging)
                    {
                        Debug.Log($"[AprilTagSpatialAnchorManager] Requested loading {uuids.Count} anchors from storage");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AprilTagSpatialAnchorManager] Failed to load anchors from storage: {e.Message}");
            }
        }

        /// <summary>
        /// Get the current number of placed anchors
        /// </summary>
        public int GetAnchorCount()
        {
            return _anchorsById.Count;
        }

        /// <summary>
        /// Check if an anchor exists for a specific tag ID
        /// </summary>
        public bool HasAnchorForTag(int tagId)
        {
            return _anchorsById.ContainsKey(tagId);
        }

        /// <summary>
        /// Get the spatial anchor for a specific tag ID
        /// </summary>
        public global::OVRSpatialAnchor GetAnchorForTag(int tagId)
        {
            _anchorsById.TryGetValue(tagId, out var anchor);
            return anchor;
        }

        // Data structures for persistence
        [System.Serializable]
        private class AnchorData
        {
            public int tagId;
            public string uuid;
            public Vector3 position;
            public Quaternion rotation;
        }

        [System.Serializable]
        private class AnchorDataCollection
        {
            public List<AnchorData> anchors;
        }
    }
}
