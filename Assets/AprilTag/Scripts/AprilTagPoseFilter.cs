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
        private float m_positionSmoothingTime = 0.1f;

        [Tooltip("Rotation smoothing time constant (seconds)")]
        [SerializeField]
        private float m_rotationSmoothingTime = 0.15f;

        [Header("Multi-Frame Validation")]
        [Tooltip("Enable multi-frame validation (rejects inconsistent detections)")]
        [SerializeField]
        private bool m_enableMultiFrameValidation = true;

        [Tooltip("Number of frames to validate against")]
        [SerializeField]
        private int m_validationFrameCount = 3;

        [Tooltip("Maximum position deviation for validation (meters)")]
        [SerializeField]
        private float m_maxPositionDeviation = 0.2f;

        [Tooltip("Maximum rotation deviation for validation (degrees)")]
        [SerializeField]
        private float m_maxRotationDeviation = 30f;

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

        [Tooltip("Frame interval for debug logs")]
        [SerializeField]
        private int m_logInterval = 300;

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

            public FilteredTagPose()
            {
                FilteredPosition = Vector3.zero;
                FilteredRotation = Quaternion.identity;
                RawPosition = Vector3.zero;
                RawRotation = Quaternion.identity;
                LastUpdateTime = 0f;
                IsInitialized = false;
                FramesSinceFirstDetection = 0;
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
        /// Validate tag detection against history
        /// </summary>
        public bool ValidateTagDetection(
            int tagId,
            Vector3 position,
            Quaternion rotation,
            float cornerQuality
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

            // Check position deviation
            var positionDeviation = Vector3.Distance(position, avgPosition);
            if (positionDeviation > m_maxPositionDeviation)
            {
                if (m_enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[PoseFilter] Tag {tagId} rejected - Position deviation: {positionDeviation:F3}m > {m_maxPositionDeviation:F3}m"
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

            if (rotationDeviation > m_maxRotationDeviation)
            {
                if (m_enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[PoseFilter] Tag {tagId} rejected - Rotation deviation: {rotationDeviation:F1}° > {m_maxRotationDeviation:F1}°"
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
        /// Apply pose smoothing filter to position
        /// </summary>
        public Vector3 FilterTagPosition(
            int tagId,
            Vector3 rawPosition,
            Vector3 previousPosition,
            float deltaTime,
            bool isInitialized
        )
        {
            if (!m_enablePoseSmoothing || !isInitialized)
                return rawPosition;

            var smoothingFactor = Mathf.Exp(-deltaTime / m_positionSmoothingTime);
            smoothingFactor = Mathf.Clamp01(smoothingFactor);

            return Vector3.Lerp(rawPosition, previousPosition, smoothingFactor);
        }

        /// <summary>
        /// Apply pose smoothing filter to rotation
        /// </summary>
        public Quaternion FilterTagRotation(
            int tagId,
            Quaternion rawRotation,
            Quaternion previousRotation,
            float deltaTime,
            bool isInitialized
        )
        {
            if (!m_enablePoseSmoothing || !isInitialized)
                return rawRotation;

            var smoothingFactor = Mathf.Exp(-deltaTime / m_rotationSmoothingTime);
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
