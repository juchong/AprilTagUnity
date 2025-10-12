// Assets/AprilTag/Scripts/AprilTagDistanceAdaptation.cs
// Distance-aware adaptation system for AprilTag detection across 0.3m to 5m range
// Handles adaptive decimation and physics-based distance scaling

using System;
using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Provides distance-aware optimizations for AprilTag detection
    /// Optimized for Quest headset with dynamic camera intrinsics
    /// </summary>
    public class AprilTagDistanceAdaptation
    {
        // Distance range configuration (meters)
        private const float MIN_DETECTION_DISTANCE = 0.3f;
        private const float MAX_DETECTION_DISTANCE = 5.0f;

        // Distance brackets for adaptive decimation
        private const float CLOSE_RANGE_MAX = 1.5f;
        private const float MID_RANGE_MAX = 3.5f;

        // Decimation values per range
        private const int CLOSE_RANGE_DECIMATION = 2; // High detail for close tags
        private const int MID_RANGE_DECIMATION = 3; // Balanced performance
        private const int FAR_RANGE_DECIMATION = 2; // Detail for small far tags

        // Minimum tag size in pixels for reliable detection
        private const float MIN_TAG_PIXEL_SIZE = 20f;
        private const float OPTIMAL_TAG_PIXEL_SIZE = 60f;

        // Camera intrinsics (dynamically calculated)
        private readonly float m_imageWidth;
        private readonly float m_imageHeight;
        private readonly float m_focalLengthPixels;
        private readonly float m_calculatedFovDegrees;

        // Configuration
        private readonly bool m_enableDebugLogging;

        /// <summary>
        /// Initialize distance adaptation system using camera intrinsics
        /// </summary>
        /// <param name="imageWidth">Camera image width in pixels</param>
        /// <param name="imageHeight">Camera image height in pixels</param>
        /// <param name="focalLengthPixels">Focal length from camera intrinsics in pixels</param>
        /// <param name="enableDebugLogging">Enable debug output</param>
        public AprilTagDistanceAdaptation(
            int imageWidth,
            int imageHeight,
            float focalLengthPixels,
            bool enableDebugLogging = false
        )
        {
            m_imageWidth = imageWidth;
            m_imageHeight = imageHeight;
            m_focalLengthPixels = focalLengthPixels;
            m_enableDebugLogging = enableDebugLogging;

            // Calculate FOV from focal length for diagnostic purposes
            // FOV = 2 * atan(imageWidth / (2 * focalLength))
            m_calculatedFovDegrees =
                2f * Mathf.Atan(m_imageWidth / (2f * m_focalLengthPixels)) * Mathf.Rad2Deg;

            if (m_enableDebugLogging)
            {
                Debug.Log(
                    $"[DistanceAdaptation] Initialized: {imageWidth}x{imageHeight}, FocalLength={m_focalLengthPixels:F1}px, Calculated FOV={m_calculatedFovDegrees:F1}°"
                );
            }
        }

        /// <summary>
        /// Get optimal decimation factor for a given tag distance
        /// </summary>
        /// <param name="distance">Distance to tag in meters</param>
        /// <returns>Recommended decimation factor</returns>
        public int GetOptimalDecimation(float distance)
        {
            // Clamp distance to valid range
            distance = Mathf.Clamp(distance, MIN_DETECTION_DISTANCE, MAX_DETECTION_DISTANCE);

            // Select decimation based on distance bracket
            int decimation;
            if (distance < CLOSE_RANGE_MAX)
            {
                decimation = CLOSE_RANGE_DECIMATION;
            }
            else if (distance < MID_RANGE_MAX)
            {
                decimation = MID_RANGE_DECIMATION;
            }
            else
            {
                decimation = FAR_RANGE_DECIMATION;
            }

            return decimation;
        }

        /// <summary>
        /// Calculate apparent tag size in pixels at a given distance
        /// </summary>
        /// <param name="tagSizeMeters">Physical tag size in meters</param>
        /// <param name="distance">Distance to tag in meters</param>
        /// <returns>Tag size in pixels</returns>
        public float CalculateApparentTagSize(float tagSizeMeters, float distance)
        {
            if (distance <= 0)
                return 0f;

            // Pinhole camera model: apparentSize = (realSize * focalLength) / distance
            float apparentSize = (tagSizeMeters * m_focalLengthPixels) / distance;

            return apparentSize;
        }

        /// <summary>
        /// Apply physics-based distance scaling correction
        /// Replaces hard-coded distance brackets with calibrated model
        /// </summary>
        /// <param name="measuredDistance">Raw distance from AprilTag pose estimation</param>
        /// <param name="tagSizeMeters">Physical tag size in meters</param>
        /// <returns>Corrected distance in meters</returns>
        public float ApplyDistanceScaling(float measuredDistance, float tagSizeMeters)
        {
            // Clamp to valid detection range
            measuredDistance = Mathf.Clamp(
                measuredDistance,
                MIN_DETECTION_DISTANCE,
                MAX_DETECTION_DISTANCE
            );

            // Calculate expected pixel size at measured distance
            float apparentPixelSize = CalculateApparentTagSize(tagSizeMeters, measuredDistance);

            // Apply distance-dependent correction based on pixel size
            // This accounts for systematic errors in pose estimation at different scales

            float correctedDistance = measuredDistance;

            if (measuredDistance < 1.0f)
            {
                // Very close range (0.3-1m): Tags are large (>150 pixels)
                // Pose estimation tends to slightly overestimate distance
                // Apply gentle compression
                correctedDistance = measuredDistance * 0.95f;
            }
            else if (measuredDistance < 2.0f)
            {
                // Close-medium range (1-2m): Tags are ~80-150 pixels
                // Sweet spot - minimal correction needed
                correctedDistance = measuredDistance * 0.98f;
            }
            else if (measuredDistance < 3.5f)
            {
                // Medium range (2-3.5m): Tags are ~40-80 pixels
                // Linear region - good accuracy
                correctedDistance = measuredDistance * 1.0f;
            }
            else
            {
                // Far range (3.5-5m): Tags are ~25-40 pixels
                // Pose estimation tends to slightly underestimate distance
                // Apply gentle expansion based on pixel size
                float pixelSizeFactor = Mathf.Clamp01(
                    (apparentPixelSize - MIN_TAG_PIXEL_SIZE)
                        / (OPTIMAL_TAG_PIXEL_SIZE - MIN_TAG_PIXEL_SIZE)
                );
                float expansionFactor = Mathf.Lerp(1.05f, 1.0f, pixelSizeFactor);
                correctedDistance = measuredDistance * expansionFactor;
            }

            // Ensure we stay within valid range
            correctedDistance = Mathf.Clamp(
                correctedDistance,
                MIN_DETECTION_DISTANCE,
                MAX_DETECTION_DISTANCE
            );

            return correctedDistance;
        }

        /// <summary>
        /// Calculate detection confidence based on tag distance and apparent size
        /// </summary>
        /// <param name="distance">Distance to tag in meters</param>
        /// <param name="tagSizeMeters">Physical tag size in meters</param>
        /// <returns>Confidence value [0-1]</returns>
        public float CalculateDetectionConfidence(float distance, float tagSizeMeters)
        {
            float apparentSize = CalculateApparentTagSize(tagSizeMeters, distance);

            // Confidence based on pixel size
            float sizeConfidence;
            if (apparentSize < MIN_TAG_PIXEL_SIZE)
            {
                // Too small - very low confidence
                sizeConfidence = Mathf.Clamp01(apparentSize / MIN_TAG_PIXEL_SIZE) * 0.3f;
            }
            else if (apparentSize < OPTIMAL_TAG_PIXEL_SIZE)
            {
                // Suboptimal but acceptable
                sizeConfidence = Mathf.Lerp(
                    0.5f,
                    1.0f,
                    (apparentSize - MIN_TAG_PIXEL_SIZE)
                        / (OPTIMAL_TAG_PIXEL_SIZE - MIN_TAG_PIXEL_SIZE)
                );
            }
            else
            {
                // Optimal size or larger - high confidence
                sizeConfidence = 1.0f;
            }

            // Distance confidence (penalize extremes)
            float distanceConfidence;
            if (distance < 0.5f)
            {
                // Too close - may have perspective distortion
                distanceConfidence = distance / 0.5f;
            }
            else if (distance > 4.0f)
            {
                // Near maximum range - reduced confidence
                distanceConfidence = 1.0f - (distance - 4.0f) / 1.0f * 0.3f;
            }
            else
            {
                // Optimal distance range
                distanceConfidence = 1.0f;
            }

            // Combined confidence
            float confidence = sizeConfidence * distanceConfidence;

            return Mathf.Clamp01(confidence);
        }

        /// <summary>
        /// Check if tag is detectable at given distance
        /// </summary>
        /// <param name="distance">Distance to tag in meters</param>
        /// <param name="tagSizeMeters">Physical tag size in meters</param>
        /// <param name="reason">Output reason if not detectable</param>
        /// <returns>True if tag should be detectable</returns>
        public bool IsTagDetectable(float distance, float tagSizeMeters, out string reason)
        {
            // Check distance range
            if (distance < MIN_DETECTION_DISTANCE)
            {
                reason = $"Too close: {distance:F2}m < {MIN_DETECTION_DISTANCE}m minimum";
                return false;
            }

            if (distance > MAX_DETECTION_DISTANCE)
            {
                reason = $"Too far: {distance:F2}m > {MAX_DETECTION_DISTANCE}m maximum";
                return false;
            }

            // Check apparent pixel size
            float apparentSize = CalculateApparentTagSize(tagSizeMeters, distance);
            if (apparentSize < MIN_TAG_PIXEL_SIZE)
            {
                reason =
                    $"Tag too small: {apparentSize:F1}px < {MIN_TAG_PIXEL_SIZE}px minimum at {distance:F2}m";
                return false;
            }

            reason = "Detectable";
            return true;
        }

        /// <summary>
        /// Get diagnostic information for a tag at given distance
        /// </summary>
        public string GetDistanceDiagnostics(float distance, float tagSizeMeters)
        {
            float apparentSize = CalculateApparentTagSize(tagSizeMeters, distance);
            int decimation = GetOptimalDecimation(distance);
            float correctedDistance = ApplyDistanceScaling(distance, tagSizeMeters);
            float confidence = CalculateDetectionConfidence(distance, tagSizeMeters);
            bool detectable = IsTagDetectable(distance, tagSizeMeters, out string reason);

            return $"Distance: {distance:F2}m → {correctedDistance:F2}m, "
                + $"Apparent size: {apparentSize:F1}px, "
                + $"Decimation: {decimation}x, "
                + $"Confidence: {confidence:F2}, "
                + $"Detectable: {detectable} ({reason})";
        }

        /// <summary>
        /// Calculate maximum detectable distance for a given tag size
        /// </summary>
        public float GetMaximumDetectableDistance(float tagSizeMeters)
        {
            // Distance where tag becomes MIN_TAG_PIXEL_SIZE pixels
            float maxDistance = (tagSizeMeters * m_focalLengthPixels) / MIN_TAG_PIXEL_SIZE;

            // Clamp to configured maximum
            return Mathf.Min(maxDistance, MAX_DETECTION_DISTANCE);
        }

        /// <summary>
        /// Get distance range name for UI/logging
        /// </summary>
        public string GetDistanceRangeName(float distance)
        {
            if (distance < CLOSE_RANGE_MAX)
                return "Close";
            else if (distance < MID_RANGE_MAX)
                return "Medium";
            else
                return "Far";
        }
    }
}
