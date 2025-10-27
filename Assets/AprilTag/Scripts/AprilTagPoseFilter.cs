// Assets/AprilTag/Scripts/AprilTagPoseFilter.cs
// PhotonVision-inspired pose filtering and validation for AprilTag detection
// Handles temporal smoothing and multi-frame validation

using System;
using System.Collections.Generic;
using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Handles pose filtering and validation for AprilTag detections
    /// Implements PhotonVision-inspired temporal filtering approach
    /// </summary>
    public class AprilTagPoseFilter : MonoBehaviour
    {
        [Header("Pose Smoothing")]
        [Tooltip("Enable pose smoothing filter (reduces jitter)")]
        [SerializeField]
        private bool m_enablePoseSmoothing = true;

        [Tooltip("Position smoothing time constant (seconds)")]
        [SerializeField]
        private float m_positionSmoothingTime = 0.8f;

        [Tooltip("Rotation smoothing time constant (seconds)")]
        [SerializeField]
        private float m_rotationSmoothingTime = 0.1f;

        [Header("Multi-Frame Validation")]
        [Tooltip("Enable multi-frame validation (rejects inconsistent detections)")]
        [SerializeField]
        private bool m_enableMultiFrameValidation = true;

        [Tooltip("Number of frames to validate against")]
        [SerializeField]
        private int m_validationFrameCount = 3;

        [Tooltip("Base maximum position deviation for validation at 1m (meters)")]
        [SerializeField]
        private float m_maxPositionDeviation = 0.2f;

        [Tooltip("Base maximum rotation deviation for validation at 1m (degrees)")]
        [SerializeField]
        private float m_maxRotationDeviation = 30f;

        [Header("Phase 2: Distance-Aware Filtering")]
        [Tooltip("Enable distance-dependent threshold scaling")]
        [SerializeField]
        private bool m_enableDistanceAwareThresholds = true;

        [Tooltip("Position threshold scaling factor per meter (e.g., 0.1 = +10% per meter)")]
        [SerializeField]
        private float m_positionThresholdScalePerMeter = 0.1f;

        [Tooltip("Rotation threshold scaling factor per meter")]
        [SerializeField]
        private float m_rotationThresholdScalePerMeter = 5f;

        [Tooltip("Enable distance-dependent smoothing")]
        [SerializeField]
        private bool m_enableDistanceAwareSmoothing = true;

        [Tooltip("Smoothing strength multiplier for close tags (<1m) - higher = more responsive")]
        [SerializeField]
        private float m_closeSmoothingMultiplier = 0.5f;

        [Tooltip("Smoothing strength multiplier for far tags (>3m) - higher = more stable")]
        [SerializeField]
        private float m_farSmoothingMultiplier = 1.5f;

        [Tooltip("Time window for considering detections as recent (seconds)")]
        [SerializeField]
        private float m_validationRecentDetectionTime = 1.0f;

        [Tooltip("Confidence value for single detections")]
        [Range(0.0f, 1.0f)]
        [SerializeField]
        private float m_singleDetectionConfidence = 0.5f;

        [Header("Debug")]
        [Tooltip("Enable debug logging")]
        [SerializeField]
        private bool m_enableDebugLogging = false;

        // Detection history for multi-frame validation
        private readonly Dictionary<int, Queue<TagDetectionHistory>> m_detectionHistory = new();

        // Filtered poses for smoothing
        private readonly Dictionary<int, FilteredTagPose> m_filteredPoses = new();

        // Reusable buffer for validation
        private TagDetectionHistory[] m_recentDetectionsBuffer;

        [Serializable]
        public class TagDetectionHistory
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float Timestamp;
            public float CornerQuality;
            public bool IsValid;

            public TagDetectionHistory(Vector3 pos, Quaternion rot, float quality)
            {
                Position = pos;
                Rotation = rot;
                Timestamp = Time.time;
                CornerQuality = quality;
                IsValid = true;
            }
        }

        [Serializable]
        public class FilteredTagPose
        {
            public Vector3 FilteredPosition;
            public Quaternion FilteredRotation;
            public Vector3 RawPosition;
            public Quaternion RawRotation;
            public float LastUpdateTime;
            public bool IsInitialized;
            public int FramesSinceFirstDetection;
            public float LastKnownDistance; // Phase 2: Track distance for adaptive filtering

            public FilteredTagPose()
            {
                FilteredPosition = Vector3.zero;
                FilteredRotation = Quaternion.identity;
                RawPosition = Vector3.zero;
                RawRotation = Quaternion.identity;
                LastUpdateTime = 0f;
                IsInitialized = false;
                FramesSinceFirstDetection = 0;
                LastKnownDistance = 1.0f; // Default to 1m
            }
        }

        /// <summary>
        /// Get or create filtered pose for a tag
        /// </summary>
        public FilteredTagPose GetFilteredPose(int tagId)
        {
            if (!m_filteredPoses.ContainsKey(tagId))
            {
                m_filteredPoses[tagId] = new FilteredTagPose();
            }
            return m_filteredPoses[tagId];
        }

        /// <summary>
        /// Phase 2: Calculate distance-aware position threshold
        /// </summary>
        private float GetDistanceAwarePositionThreshold(float distance)
        {
            if (!m_enableDistanceAwareThresholds)
                return m_maxPositionDeviation;

            // Scale threshold based on distance
            // Formula: baseThreshold * (1.0 + distance * scalePerMeter)
            // At 1m: 0.2m * (1.0 + 1.0 * 0.1) = 0.22m
            // At 3m: 0.2m * (1.0 + 3.0 * 0.1) = 0.26m
            // At 5m: 0.2m * (1.0 + 5.0 * 0.1) = 0.30m
            float scaleFactor = 1.0f + distance * m_positionThresholdScalePerMeter;
            return m_maxPositionDeviation * scaleFactor;
        }

        /// <summary>
        /// Phase 2: Calculate distance-aware rotation threshold
        /// </summary>
        private float GetDistanceAwareRotationThreshold(float distance)
        {
            if (!m_enableDistanceAwareThresholds)
                return m_maxRotationDeviation;

            // Scale threshold based on distance
            // Formula: baseThreshold + (distance * scalePerMeter)
            // At 1m: 30° + (1.0 * 5°) = 35°
            // At 3m: 30° + (3.0 * 5°) = 45°
            // At 5m: 30° + (5.0 * 5°) = 55°
            return m_maxRotationDeviation + (distance * m_rotationThresholdScalePerMeter);
        }

        /// <summary>
        /// Phase 2: Calculate distance-aware smoothing multiplier
        /// </summary>
        private float GetDistanceAwareSmoothingMultiplier(float distance)
        {
            if (!m_enableDistanceAwareSmoothing)
                return 1.0f;

            // Close tags (<1m): More responsive (less smoothing)
            if (distance < 1.0f)
            {
                return m_closeSmoothingMultiplier;
            }
            // Far tags (>3m): More stable (more smoothing)
            else if (distance > 3.0f)
            {
                return m_farSmoothingMultiplier;
            }
            // Medium distance (1-3m): Linear interpolation
            else
            {
                float t = (distance - 1.0f) / 2.0f; // Normalize to [0,1]
                return Mathf.Lerp(m_closeSmoothingMultiplier, m_farSmoothingMultiplier, t);
            }
        }

        /// <summary>
        /// Validate tag detection against history (Phase 2: Distance-aware)
        /// </summary>
        public bool ValidateTagDetection(
            int tagId,
            Vector3 position,
            Quaternion rotation,
            float cornerQuality,
            float distance = 1.0f // Phase 2: Added distance parameter
        )
        {
            if (!m_enableMultiFrameValidation)
                return true;

            // Initialize history queue if needed
            if (!m_detectionHistory.ContainsKey(tagId))
            {
                m_detectionHistory[tagId] = new Queue<TagDetectionHistory>();
            }

            var history = m_detectionHistory[tagId];

            // If we don't have enough history, accept the detection
            if (history.Count < 2)
            {
                history.Enqueue(new TagDetectionHistory(position, rotation, cornerQuality));
                while (history.Count > m_validationFrameCount)
                {
                    history.Dequeue();
                }
                return true;
            }

            // Calculate average position and rotation from recent history
            var avgPosition = Vector3.zero;
            var avgEulerAngles = Vector3.zero;
            var validCount = 0;

            foreach (var detection in history)
            {
                if (
                    detection.IsValid
                    && (Time.time - detection.Timestamp) < m_validationRecentDetectionTime
                )
                {
                    avgPosition += detection.Position;
                    avgEulerAngles += detection.Rotation.eulerAngles;
                    validCount++;
                }
            }

            if (validCount == 0)
                return true;

            avgPosition /= validCount;
            avgEulerAngles /= validCount;

            // Phase 2: Get distance-aware thresholds
            float maxPositionDev = GetDistanceAwarePositionThreshold(distance);
            float maxRotationDev = GetDistanceAwareRotationThreshold(distance);

            // Check position deviation
            var positionDeviation = Vector3.Distance(position, avgPosition);
            if (positionDeviation > maxPositionDev)
            {
                if (m_enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[PoseFilter] Tag {tagId} rejected - Position deviation: {positionDeviation:F3}m > {maxPositionDev:F3}m (distance: {distance:F2}m)"
                    );
                }
                return false;
            }

            // Check rotation deviation
            var currentEuler = rotation.eulerAngles;
            var rotationDeviation = Mathf.Max(
                Mathf.Abs(Mathf.DeltaAngle(currentEuler.x, avgEulerAngles.x)),
                Mathf.Abs(Mathf.DeltaAngle(currentEuler.y, avgEulerAngles.y)),
                Mathf.Abs(Mathf.DeltaAngle(currentEuler.z, avgEulerAngles.z))
            );

            if (rotationDeviation > maxRotationDev)
            {
                if (m_enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[PoseFilter] Tag {tagId} rejected - Rotation deviation: {rotationDeviation:F1}° > {maxRotationDev:F1}° (distance: {distance:F2}m)"
                    );
                }
                return false;
            }

            // Detection passed validation, add to history
            history.Enqueue(new TagDetectionHistory(position, rotation, cornerQuality));
            while (history.Count > m_validationFrameCount)
            {
                history.Dequeue();
            }

            return true;
        }

        /// <summary>
        /// Apply pose smoothing filter to position (Phase 2: Distance-aware)
        /// </summary>
        public Vector3 FilterTagPosition(
            int tagId,
            Vector3 rawPosition,
            Vector3 previousPosition,
            float deltaTime,
            bool isInitialized,
            float distance = 1.0f // Phase 2: Added distance parameter
        )
        {
            if (!m_enablePoseSmoothing || !isInitialized)
                return rawPosition;

            // Phase 2: Apply distance-aware smoothing multiplier
            float smoothingMultiplier = GetDistanceAwareSmoothingMultiplier(distance);
            float adjustedSmoothingTime = m_positionSmoothingTime * smoothingMultiplier;

            var smoothingFactor = Mathf.Exp(-deltaTime / adjustedSmoothingTime);
            smoothingFactor = Mathf.Clamp01(smoothingFactor);

            return Vector3.Lerp(rawPosition, previousPosition, smoothingFactor);
        }

        /// <summary>
        /// Apply pose smoothing filter to rotation (Phase 2: Distance-aware)
        /// </summary>
        public Quaternion FilterTagRotation(
            int tagId,
            Quaternion rawRotation,
            Quaternion previousRotation,
            float deltaTime,
            bool isInitialized,
            float distance = 1.0f // Phase 2: Added distance parameter
        )
        {
            if (!m_enablePoseSmoothing || !isInitialized)
                return rawRotation;

            // Phase 2: Apply distance-aware smoothing multiplier
            float smoothingMultiplier = GetDistanceAwareSmoothingMultiplier(distance);
            float adjustedSmoothingTime = m_rotationSmoothingTime * smoothingMultiplier;

            var smoothingFactor = Mathf.Exp(-deltaTime / adjustedSmoothingTime);
            smoothingFactor = Mathf.Clamp01(smoothingFactor);

            return Quaternion.Slerp(rawRotation, previousRotation, smoothingFactor);
        }

        /// <summary>
        /// Calculate validation confidence based on detection history
        /// </summary>
        public float CalculateValidationConfidence(int tagId)
        {
            if (!m_detectionHistory.TryGetValue(tagId, out var history))
                return m_singleDetectionConfidence;

            if (history.Count < 2)
                return m_singleDetectionConfidence;

            // Avoid LINQ allocation - iterate queue directly
            if (
                m_recentDetectionsBuffer == null
                || m_recentDetectionsBuffer.Length < m_validationFrameCount
            )
            {
                m_recentDetectionsBuffer = new TagDetectionHistory[m_validationFrameCount];
            }

            int count = 0;
            foreach (var detection in history)
            {
                if (count >= m_validationFrameCount)
                    break;
                m_recentDetectionsBuffer[count++] = detection;
            }

            if (count < 2)
                return m_singleDetectionConfidence;

            // Calculate position and rotation consistency
            var positionVariance = 0f;
            var rotationVariance = 0f;

            for (var i = 1; i < count; i++)
            {
                positionVariance += Vector3.Distance(
                    m_recentDetectionsBuffer[i].Position,
                    m_recentDetectionsBuffer[i - 1].Position
                );
                rotationVariance += Quaternion.Angle(
                    m_recentDetectionsBuffer[i].Rotation,
                    m_recentDetectionsBuffer[i - 1].Rotation
                );
            }

            positionVariance /= count - 1;
            rotationVariance /= count - 1;

            var positionConfidence = Mathf.Clamp01(
                1.0f - positionVariance / m_maxPositionDeviation
            );
            var rotationConfidence = Mathf.Clamp01(
                1.0f - rotationVariance / m_maxRotationDeviation
            );

            return (positionConfidence + rotationConfidence) * 0.5f;
        }

        /// <summary>
        /// Get all currently tracked tag IDs
        /// </summary>
        public IEnumerable<int> GetTrackedTagIds()
        {
            return m_filteredPoses.Keys;
        }

        /// <summary>
        /// Remove tracking data for tags no longer detected
        /// </summary>
        public void RemoveTagTracking(int tagId)
        {
            m_filteredPoses.Remove(tagId);
            m_detectionHistory.Remove(tagId);
        }

        /// <summary>
        /// Clear all tracking data
        /// </summary>
        public void ClearAll()
        {
            m_filteredPoses.Clear();
            m_detectionHistory.Clear();
        }
    }
}
