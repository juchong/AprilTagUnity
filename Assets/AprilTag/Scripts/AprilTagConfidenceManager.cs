// Assets/AprilTag/AprilTagConfidenceManager.cs

using System;
using System.Collections.Generic;
using System.Linq;
using AprilTag;
using UnityEngine;

public class AprilTagConfidenceManager : MonoBehaviour
{
    [Header("Confidence Settings")]
    [SerializeField]
    private bool m_enableAllDebugLogging = false;

    [SerializeField]
    private bool m_enableCornerQualityAssessment = true;

    [SerializeField]
    private bool m_enableMultiFrameValidation = true;

    [SerializeField]
    private bool m_enablePoseSmoothing = true;

    [SerializeField]
    private int m_validationFrameCount = 3;

    [SerializeField]
    private float m_maxPositionDeviation = 0.2f;

    [SerializeField]
    private float m_maxRotationDeviation = 30f;

    // Local copies for history and filtered poses so this helper compiles independently
    private readonly Dictionary<int, Queue<TagDetectionHistory>> m_detectionHistory = new();
    private readonly Dictionary<int, FilteredTagPose> m_filteredPoses = new();

    [Serializable]
    private class TagDetectionHistory
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
    private class FilteredTagPose
    {
        public float LastUpdateTime;
        public bool IsInitialized;
    }

    /// <summary>
    /// Calculate detection confidence for a tag based on various factors
    /// </summary>
    private float CalculateDetectionConfidence(TagPose tag)
    {
        var confidence = 1.0f; // Start with maximum confidence

        if (m_enableAllDebugLogging)
        {
            Debug.Log($"[AprilTag] Calculating confidence for tag {tag.ID}:");
        }

        // Apply corner quality assessment if enabled
        if (m_enableCornerQualityAssessment)
        {
            // Use a simplified corner quality calculation
            // In a real implementation, you might want to access actual corner quality data
            var cornerQuality = Mathf.Clamp01(1.0f - tag.Position.magnitude * 0.01f); // Much gentler distance-based quality
            confidence *= cornerQuality;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[AprilTag]   Corner quality: {cornerQuality:F3}, confidence after: {confidence:F3}"
                );
            }
        }

        // Apply multi-frame validation confidence
        if (m_enableMultiFrameValidation && m_detectionHistory.TryGetValue(tag.ID, out var history))
        {
            var validationConfidence = CalculateValidationConfidence(history);
            confidence *= validationConfidence;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[AprilTag]   Validation confidence: {validationConfidence:F3}, confidence after: {confidence:F3}"
                );
            }
        }

        // Apply pose smoothing confidence
        if (m_enablePoseSmoothing && m_filteredPoses.TryGetValue(tag.ID, out var filteredPose))
        {
            if (filteredPose.IsInitialized)
            {
                // Higher confidence for more stable poses - much gentler decay
                var stabilityConfidence = Mathf.Clamp01(
                    1.0f - (Time.time - filteredPose.LastUpdateTime) * 0.01f
                );
                confidence *= stabilityConfidence;

                if (m_enableAllDebugLogging)
                {
                    Debug.Log(
                        $"[AprilTag]   Stability confidence: {stabilityConfidence:F3}, confidence after: {confidence:F3}"
                    );
                }
            }
        }

        // Ensure minimum confidence to prevent 0.0f values
        var finalConfidence = Mathf.Clamp01(confidence);
        if (finalConfidence < 0.1f) // Minimum 10% confidence
        {
            finalConfidence = 0.1f;
            if (m_enableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[AprilTag] Confidence clamped to minimum 0.1f for tag {tag.ID} (was {confidence:F3})"
                );
            }
        }

        return finalConfidence;
    }

    /// <summary>
    /// Calculate validation confidence based on detection history
    /// </summary>
    private float CalculateValidationConfidence(Queue<TagDetectionHistory> history)
    {
        if (history.Count < 2)
            return 0.5f; // Low confidence for single detections

        var recentDetections = history.Take(m_validationFrameCount).ToList();
        if (recentDetections.Count < 2)
            return 0.5f;

        // Calculate position consistency
        var positionVariance = 0f;
        var rotationVariance = 0f;

        for (var i = 1; i < recentDetections.Count; i++)
        {
            positionVariance += Vector3.Distance(
                recentDetections[i].Position,
                recentDetections[i - 1].Position
            );
            rotationVariance += Quaternion.Angle(
                recentDetections[i].Rotation,
                recentDetections[i - 1].Rotation
            );
        }

        positionVariance /= recentDetections.Count - 1;
        rotationVariance /= recentDetections.Count - 1;

        // Convert variance to confidence (lower variance = higher confidence)
        var positionConfidence = Mathf.Clamp01(1.0f - positionVariance / m_maxPositionDeviation);
        var rotationConfidence = Mathf.Clamp01(1.0f - rotationVariance / m_maxRotationDeviation);

        var finalConfidence = (positionConfidence + rotationConfidence) * 0.5f;

        if (m_enableAllDebugLogging)
        {
            Debug.Log($"[AprilTag] Validation confidence calculation:");
            Debug.Log(
                $"[AprilTag]   Position variance: {positionVariance:F3}m, max: {m_maxPositionDeviation:F3}m, confidence: {positionConfidence:F3}"
            );
            Debug.Log(
                $"[AprilTag]   Rotation variance: {rotationVariance:F1}°, max: {m_maxRotationDeviation:F1}°, confidence: {rotationConfidence:F3}"
            );
            Debug.Log($"[AprilTag]   Final validation confidence: {finalConfidence:F3}");
        }

        return finalConfidence;
    }
}
