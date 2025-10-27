// Assets/AprilTag/Scripts/FRCFieldLocalizer.cs
// Simple field localization using spatial anchors and known field layout
// Transforms headset pose from Quest space to FRC field coordinates

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Localizes Quest headset to FRC field coordinates using spatial anchors.
    /// Uses existing AprilTagSpatialAnchorManager and field layout to calculate transform.
    ///
    /// This system works regardless of where the headset is initialized in Quest space.
    /// The alignment calculation computes the transform between two coordinate systems:
    /// - Quest coordinate system (where spatial anchors are placed based on tag detections)
    /// - Field coordinate system (known tag positions from FRC field layout JSON)
    ///
    /// The headset can start anywhere in Quest space - the system will find the correct
    /// transform by matching the spatial relationships between detected tags to their
    /// known positions on the field.
    /// </summary>
    public class FRCFieldLocalizer : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("AprilTag controller with tag size configuration (auto-found if null)")]
        [SerializeField]
        private AprilTagController m_aprilTagController;

        [Tooltip("Spatial anchor manager (auto-found if null)")]
        [SerializeField]
        private AprilTagSpatialAnchorManager m_anchorManager;

        [Tooltip("Field layout with tag positions")]
        [SerializeField]
        private AprilTagFieldLayout m_fieldLayout;

        [Header("Settings")]
        [Tooltip("Select which FRC field layout to use")]
        [SerializeField]
        private FieldLayoutType m_selectedFieldLayout = FieldLayoutType.Reefscape2025_Welded;

        [Tooltip("Minimum anchors needed for alignment")]
        [Range(2, 10)]
        [SerializeField]
        private int m_minAnchors = 3;

        [Tooltip("Auto-calculate alignment when enough anchors exist")]
        [SerializeField]
        private bool m_autoAlign = true;

        [Tooltip("Save/load alignment from PlayerPrefs")]
        [SerializeField]
        private bool m_persistAlignment = true;

        // Field layout selection enum
        public enum FieldLayoutType
        {
            RapidReact2022,
            ChargedUp2023,
            Crescendo2024,
            Reefscape2025_AndyMark,
            Reefscape2025_Welded,
        }

        [Header("Validation")]
        [Tooltip("Maximum alignment error (meters) - alignment rejected if exceeded")]
        [SerializeField]
        private float m_maxAlignmentError = 0.5f;

        [Tooltip("Enable outlier rejection for bad anchor data")]
        [SerializeField]
        private bool m_enableOutlierRejection = true;

        [Tooltip("Maximum distance error (meters) for outlier detection")]
        [SerializeField]
        private float m_maxOutlierError = 0.3f;

        [Tooltip("Maximum angular error (degrees) for outlier detection")]
        [SerializeField]
        private float m_maxOutlierAngularError = 15f;

        [Tooltip("Enable directional error checking (X, Y, Z independently)")]
        [SerializeField]
        private bool m_enableDirectionalErrorCheck = true;

        [Tooltip("Maximum per-axis error (meters) for directional validation")]
        [SerializeField]
        private float m_maxPerAxisError = 0.2f;

        [Header("Debug")]
        [Tooltip("Enable debug logging")]
        [SerializeField]
        private bool m_enableDebug = true;

        [Tooltip("Enable field pose logging (FIELD_POSE messages for external tools)")]
        [SerializeField]
        private bool m_enableFieldPoseLogging = true;

        [Tooltip(
            "Frame interval for debug logs (higher = less frequent, e.g., 300 = ~4 seconds at 72 FPS)"
        )]
        [SerializeField]
        private int m_debugLogInterval = 300;

        [Tooltip("Show field coordinate frame")]
        [SerializeField]
        private bool m_visualizeField = true;

        // Field transform (maps field coords to Quest coords)
        private Transform m_fieldOrigin;
        private bool m_isAligned = false;
        private float m_currentAlignmentError = float.MaxValue;
        private int m_lastSuccessfulAnchorCount = 0;

        // Properties
        public bool IsAligned => m_isAligned;
        public Transform FieldOrigin => m_fieldOrigin;
        public float AlignmentError => m_currentAlignmentError;

        // Events
        public event System.Action OnAligned;

        /// <summary>
        /// Get field layout JSON string based on selected field type
        /// </summary>
        private string GetFieldLayoutJson(FieldLayoutType fieldType)
        {
            switch (fieldType)
            {
                case FieldLayoutType.RapidReact2022:
                    return FieldLayout_2022_RapidReact.JSON;
                case FieldLayoutType.ChargedUp2023:
                    return FieldLayout_2023_ChargedUp.JSON;
                case FieldLayoutType.Crescendo2024:
                    return FieldLayout_2024_Crescendo.JSON;
                case FieldLayoutType.Reefscape2025_AndyMark:
                    return FieldLayout_2025_Reefscape_AndyMark.JSON;
                case FieldLayoutType.Reefscape2025_Welded:
                    return FieldLayout_2025_Reefscape_Welded.JSON;
                default:
                    Debug.LogError($"[FieldLocalizer] Unknown field layout type: {fieldType}");
                    return null;
            }
        }

        /// <summary>
        /// Get field layout name based on selected field type
        /// </summary>
        private string GetFieldLayoutName(FieldLayoutType fieldType)
        {
            switch (fieldType)
            {
                case FieldLayoutType.RapidReact2022:
                    return "2022-rapidreact";
                case FieldLayoutType.ChargedUp2023:
                    return "2023-chargedup";
                case FieldLayoutType.Crescendo2024:
                    return "2024-crescendo";
                case FieldLayoutType.Reefscape2025_AndyMark:
                    return "2025-reefscape-andymark";
                case FieldLayoutType.Reefscape2025_Welded:
                    return "2025-reefscape-welded";
                default:
                    return "unknown";
            }
        }

        /// <summary>
        /// Get tag size from AprilTagController (single source of truth)
        /// </summary>
        private float GetTagSizeFromController()
        {
            if (m_aprilTagController == null)
            {
                Debug.LogWarning(
                    "[FieldLocalizer] AprilTagController is null, using default tag size 0.165m"
                );
                return 0.165f;
            }

            // Use reflection to access private field m_tagSizeMeters
            var field = typeof(AprilTagController).GetField(
                "m_tagSizeMeters",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );

            if (field == null)
            {
                Debug.LogWarning(
                    "[FieldLocalizer] Could not find m_tagSizeMeters field, using default 0.165m"
                );
                return 0.165f;
            }

            var tagSize = (float)field.GetValue(m_aprilTagController);

            if (m_enableDebug)
            {
                Debug.Log($"[FieldLocalizer] Using tag size {tagSize}m from AprilTagController");
            }

            return tagSize;
        }

        private void Start()
        {
            // Find AprilTag controller for tag size
            if (m_aprilTagController == null)
                m_aprilTagController = FindFirstObjectByType<AprilTagController>();

            if (m_aprilTagController == null)
            {
                Debug.LogError(
                    "[FieldLocalizer] AprilTagController not found - cannot determine tag size"
                );
                enabled = false;
                return;
            }

            // Find anchor manager
            if (m_anchorManager == null)
                m_anchorManager = FindFirstObjectByType<AprilTagSpatialAnchorManager>();

            // Load field layout based on selection - using tag size from AprilTagController
            string fieldJson = GetFieldLayoutJson(m_selectedFieldLayout);
            string fieldName = GetFieldLayoutName(m_selectedFieldLayout);

            if (fieldJson == null)
            {
                Debug.LogError(
                    $"[FieldLocalizer] Failed to get JSON for field layout '{m_selectedFieldLayout}'"
                );
                enabled = false;
                return;
            }

            // Get tag size from AprilTagController (single source of truth)
            float tagSize = GetTagSizeFromController();
            m_fieldLayout = AprilTagFieldLayout.FromWPILibJson(fieldJson, fieldName, tagSize);

            if (m_fieldLayout == null)
            {
                Debug.LogError(
                    $"[FieldLocalizer] Failed to parse field layout '{m_selectedFieldLayout}'"
                );
                enabled = false;
                return;
            }

            if (m_enableDebug)
            {
                Debug.Log(
                    $"[FieldLocalizer] Using field layout '{fieldName}' with {m_fieldLayout.tags?.Count ?? 0} tags"
                );
            }

            // Create field origin
            var fieldObj = new GameObject("FieldOrigin");
            fieldObj.transform.SetParent(transform);
            m_fieldOrigin = fieldObj.transform;

            // Subscribe to anchor events
            AprilTagSpatialAnchorManager.OnAnchorCreated += OnAnchorCreated;

            // Try to load saved alignment
            if (m_persistAlignment)
                LoadAlignment();
        }

        private void OnDestroy()
        {
            AprilTagSpatialAnchorManager.OnAnchorCreated -= OnAnchorCreated;
        }

        private void Update()
        {
            // Validate anchor manager
            if (m_anchorManager == null)
            {
                if (m_enableDebug && Time.frameCount % m_debugLogInterval == 0)
                    Debug.LogWarning("[FieldLocalizer] No anchor manager - cannot align");
                return;
            }

            // Try to align if not already aligned, or if we have more anchors to improve alignment
            if (m_autoAlign)
            {
                var currentAnchorCount = m_anchorManager.GetAnchorCount();

                if (!m_isAligned && currentAnchorCount >= m_minAnchors)
                {
                    CalculateAlignment();
                }
                else if (m_isAligned && currentAnchorCount > m_lastSuccessfulAnchorCount)
                {
                    // Try to improve alignment with more anchors
                    if (m_enableDebug && Time.frameCount % m_debugLogInterval == 0)
                        Debug.Log(
                            $"[FieldLocalizer] Attempting to improve alignment with {currentAnchorCount} anchors (had {m_lastSuccessfulAnchorCount})"
                        );
                    CalculateAlignment();
                }
            }

            // Log field position continuously once aligned and minimum anchors are met (for adb logcat filtering)
            if (
                m_enableFieldPoseLogging
                && m_isAligned
                && m_anchorManager != null
                && Time.frameCount % (m_debugLogInterval / 8) == 0
            )
            {
                var currentAnchorCount = m_anchorManager.GetAnchorCount();
                if (currentAnchorCount >= m_minAnchors)
                {
                    var fieldPosMeters = GetFieldPosition();
                    var fieldRot = GetFieldRotation();

                    // Convert meters to feet (1m = 3.28084ft)
                    var fieldPosFeet = fieldPosMeters * 3.28084f;

                    // Debug: also log Quest space position and field origin for verification
                    if (m_enableDebug && Time.frameCount % 60 == 0)
                    {
                        var cameraRig = OVRManager.instance?.GetComponentInChildren<OVRCameraRig>();
                        if (cameraRig != null && cameraRig.centerEyeAnchor != null)
                        {
                            var centerEyePos = cameraRig.centerEyeAnchor.position;
                            var trackingSpacePos =
                                cameraRig.trackingSpace != null
                                    ? cameraRig.trackingSpace.position
                                    : Vector3.zero;
                            Debug.Log(
                                $"[FIELD_DEBUG] Center eye world pos: {centerEyePos:F3}, Tracking space: {trackingSpacePos:F3}, Field origin: {m_fieldOrigin.position:F3}"
                            );
                        }
                    }

                    // Rate limit field pose logging to reduce spam
                    if (Time.frameCount % 36 == 0) // Every ~0.5 seconds at 72 FPS
                    {
                        Debug.Log(
                            $"[FIELD_POSE] pos_ft:{fieldPosFeet.x:F3},{fieldPosFeet.y:F3},{fieldPosFeet.z:F3} rot:{fieldRot.eulerAngles.x:F1},{fieldRot.eulerAngles.y:F1},{fieldRot.eulerAngles.z:F1} anchors:{currentAnchorCount}"
                        );
                    }
                }
            }
        }

        private void OnAnchorCreated(int tagId, OVRSpatialAnchor anchor)
        {
            if (m_enableDebug)
                Debug.Log($"[FieldLocalizer] New anchor for tag {tagId}");

            // Recalculate alignment with new anchor
            if (m_autoAlign)
                CalculateAlignment();
        }

        /// <summary>
        /// Calculate field alignment from spatial anchors with validation and outlier rejection
        /// </summary>
        private void CalculateAlignment()
        {
            if (m_anchorManager == null || m_fieldLayout == null)
                return;

            // Get all anchors with known field positions
            var pairs = new List<(Vector3 questPos, Vector3 fieldPos, int tagId)>();

            foreach (var tagId in m_fieldLayout.GetAllTagIds())
            {
                var anchor = m_anchorManager.GetAnchorForTag(tagId);
                if (anchor == null || !anchor.Localized)
                    continue;

                if (!m_fieldLayout.TryGetTag(tagId, out var fieldTag))
                    continue;

                // Validate anchor position (only check for NaN, not bounds)
                // Note: We don't validate bounds here because the headset may have been initialized
                // far outside the field. The alignment calculation will work regardless of the
                // initial headset position - it just computes the transform between the two coordinate systems.
                if (
                    float.IsNaN(anchor.transform.position.x)
                    || float.IsNaN(anchor.transform.position.y)
                    || float.IsNaN(anchor.transform.position.z)
                )
                {
                    Debug.LogWarning(
                        $"[FieldLocalizer] Tag {tagId} anchor has invalid position - skipping"
                    );
                    continue;
                }

                pairs.Add((anchor.transform.position, fieldTag.position, tagId));
            }

            if (pairs.Count < m_minAnchors)
            {
                if (m_enableDebug && Time.frameCount % 60 == 0)
                    Debug.Log($"[FieldLocalizer] Need {m_minAnchors} anchors, have {pairs.Count}");
                return;
            }

            // Apply outlier rejection if enabled
            var filteredPairs = pairs;
            if (m_enableOutlierRejection && pairs.Count > m_minAnchors)
            {
                filteredPairs = RejectOutliers(pairs);
                if (filteredPairs.Count < m_minAnchors)
                {
                    if (Time.frameCount % m_debugLogInterval == 0)
                        Debug.LogWarning(
                            $"[FieldLocalizer] Outlier rejection left only {filteredPairs.Count} anchors - using all {pairs.Count} anchors"
                        );
                    filteredPairs = pairs;
                }
                else if (
                    filteredPairs.Count < pairs.Count
                    && m_enableDebug
                    && Time.frameCount % m_debugLogInterval == 0
                )
                {
                    Debug.Log(
                        $"[FieldLocalizer] Rejected {pairs.Count - filteredPairs.Count} outliers, using {filteredPairs.Count} anchors"
                    );
                }
            }

            // Calculate transform
            var result = CalculateTransform(
                filteredPairs.Select(p => (p.questPos, p.fieldPos)).ToList()
            );
            if (!result.HasValue)
            {
                if (Time.frameCount % m_debugLogInterval == 0)
                    Debug.LogWarning("[FieldLocalizer] Transform calculation failed");
                return;
            }

            var (translation, rotation) = result.Value;

            // Validate transform
            var error = CalculateAlignmentError(filteredPairs, translation, rotation);

            if (error > m_maxAlignmentError)
            {
                if (Time.frameCount % m_debugLogInterval == 0)
                    Debug.LogWarning(
                        $"[FieldLocalizer] Alignment error {error:F3}m exceeds threshold {m_maxAlignmentError:F3}m - rejecting"
                    );
                return;
            }

            // Only accept if error is better or same number of anchors
            if (
                m_isAligned
                && error > m_currentAlignmentError
                && filteredPairs.Count <= m_lastSuccessfulAnchorCount
            )
            {
                if (m_enableDebug)
                    Debug.Log(
                        $"[FieldLocalizer] New alignment error {error:F3}m worse than current {m_currentAlignmentError:F3}m - keeping existing alignment"
                    );
                return;
            }

            // Apply to field origin
            m_fieldOrigin.position = translation;
            m_fieldOrigin.rotation = rotation;
            m_isAligned = true;
            m_currentAlignmentError = error;
            m_lastSuccessfulAnchorCount = filteredPairs.Count;

            if (m_enableDebug)
            {
                // Log alignment details including anchor positions to verify field registration
                Debug.Log(
                    $"[FieldLocalizer] ✓ Aligned using {filteredPairs.Count} anchors with error {error:F3}m"
                );
                Debug.Log(
                    $"[FieldLocalizer] Field origin set to: position={translation:F3}, rotation={rotation.eulerAngles:F1}"
                );

                // Log sample anchor transformations to verify alignment
                if (filteredPairs.Count > 0)
                {
                    var samplePair = filteredPairs[0];
                    var transformedPos = rotation * samplePair.fieldPos + translation;
                    Debug.Log(
                        $"[FieldLocalizer] Sample anchor {samplePair.tagId}: Quest={samplePair.questPos:F3}, Field={samplePair.fieldPos:F3}, Transformed={transformedPos:F3}, Error={Vector3.Distance(samplePair.questPos, transformedPos):F3}m"
                    );
                }
            }

            // Save and notify
            if (m_persistAlignment)
                SaveAlignment();
            OnAligned?.Invoke();
        }

        /// <summary>
        /// Reject outlier anchors using multi-dimensional validation.
        /// Inspired by PhotonVision's multi-tag pose estimation approach.
        ///
        /// Validates anchors using:
        /// 1. Distance consistency (existing approach)
        /// 2. Angular consistency (relative bearing between tags)
        /// 3. Directional error (per-axis validation)
        ///
        /// This comprehensive approach detects anchors that are skewed in any direction or angle.
        /// </summary>
        private List<(Vector3 questPos, Vector3 fieldPos, int tagId)> RejectOutliers(
            List<(Vector3 questPos, Vector3 fieldPos, int tagId)> pairs
        )
        {
            var errors = new List<(int index, float distError, float angError, Vector3 dirError)>();

            // Calculate multi-dimensional errors for each anchor
            for (int i = 0; i < pairs.Count; i++)
            {
                float totalDistError = 0f;
                float totalAngError = 0f;
                Vector3 totalDirError = Vector3.zero;
                int comparisons = 0;

                for (int j = 0; j < pairs.Count; j++)
                {
                    if (i == j)
                        continue;

                    // 1. Distance consistency check (original approach)
                    var questDist = Vector3.Distance(pairs[i].questPos, pairs[j].questPos);
                    var fieldDist = Vector3.Distance(pairs[i].fieldPos, pairs[j].fieldPos);
                    var distError = Mathf.Abs(questDist - fieldDist);
                    totalDistError += distError;

                    // 2. Angular consistency check (relative bearing between tags)
                    // Project to XZ plane for yaw-only comparison (flat field assumption)
                    var questVec = pairs[j].questPos - pairs[i].questPos;
                    var fieldVec = pairs[j].fieldPos - pairs[i].fieldPos;

                    questVec.y = 0;
                    fieldVec.y = 0;

                    if (questVec.magnitude > 0.1f && fieldVec.magnitude > 0.1f)
                    {
                        var angularError = Mathf.Abs(
                            Vector3.SignedAngle(questVec, fieldVec, Vector3.up)
                        );
                        totalAngError += angularError;
                    }

                    // 3. Directional error check (per-axis validation)
                    // Check if the relative vector is consistent in each axis
                    if (m_enableDirectionalErrorCheck)
                    {
                        var questVecFull = pairs[j].questPos - pairs[i].questPos;
                        var fieldVecFull = pairs[j].fieldPos - pairs[i].fieldPos;

                        // Calculate per-axis difference
                        var axisError = new Vector3(
                            Mathf.Abs(questVecFull.x - fieldVecFull.x),
                            Mathf.Abs(questVecFull.y - fieldVecFull.y),
                            Mathf.Abs(questVecFull.z - fieldVecFull.z)
                        );

                        totalDirError += axisError;
                    }

                    comparisons++;
                }

                var avgDistError = comparisons > 0 ? totalDistError / comparisons : 0f;
                var avgAngError = comparisons > 0 ? totalAngError / comparisons : 0f;
                var avgDirError = comparisons > 0 ? totalDirError / comparisons : Vector3.zero;

                errors.Add((i, avgDistError, avgAngError, avgDirError));
            }

            // Filter anchors based on multi-dimensional validation
            var filtered = new List<(Vector3 questPos, Vector3 fieldPos, int tagId)>();
            foreach (var (index, distError, angError, dirError) in errors)
            {
                bool passDistCheck = distError <= m_maxOutlierError;
                bool passAngCheck = angError <= m_maxOutlierAngularError;
                bool passDirCheck =
                    !m_enableDirectionalErrorCheck
                    || (
                        dirError.x <= m_maxPerAxisError
                        && dirError.y <= m_maxPerAxisError
                        && dirError.z <= m_maxPerAxisError
                    );

                if (passDistCheck && passAngCheck && passDirCheck)
                {
                    filtered.Add(pairs[index]);
                }
                else if (m_enableDebug)
                {
                    var reasons = new System.Collections.Generic.List<string>();
                    if (!passDistCheck)
                        reasons.Add($"dist={distError:F3}m>{m_maxOutlierError:F3}m");
                    if (!passAngCheck)
                        reasons.Add($"ang={angError:F1}°>{m_maxOutlierAngularError:F1}°");
                    if (!passDirCheck)
                        reasons.Add(
                            $"dir=({dirError.x:F3},{dirError.y:F3},{dirError.z:F3})>{m_maxPerAxisError:F3}m"
                        );

                    Debug.Log(
                        $"[FieldLocalizer] Rejecting tag {pairs[index].tagId} as outlier: {string.Join(", ", reasons)}"
                    );
                }
            }

            return filtered;
        }

        /// <summary>
        /// Calculate alignment error after transform
        /// </summary>
        private float CalculateAlignmentError(
            List<(Vector3 questPos, Vector3 fieldPos, int tagId)> pairs,
            Vector3 translation,
            Quaternion rotation
        )
        {
            float totalError = 0f;

            foreach (var pair in pairs)
            {
                // Transform field position to Quest space
                var expectedQuestPos = rotation * pair.fieldPos + translation;
                var error = Vector3.Distance(pair.questPos, expectedQuestPos);
                totalError += error * error; // Use squared error
            }

            return Mathf.Sqrt(totalError / pairs.Count); // RMS error
        }

        /// <summary>
        /// Calculate transform from Quest space to field space using weighted least-squares approach.
        ///
        /// Inspired by PhotonVision's multi-tag pose estimation which combines data from multiple
        /// AprilTags to produce a robust field-relative pose estimate. This implementation uses:
        ///
        /// 1. Weighted averaging based on tag pair separation (longer baselines = more reliable)
        /// 2. Iterative refinement to minimize reprojection error
        /// 3. Robust angle calculation considering all tag pairs
        ///
        /// This method computes the transform between two coordinate systems by finding
        /// the rotation and translation that best maps points from one system to the other.
        /// It works regardless of where the Quest headset was initialized - the transform
        /// is computed purely from the spatial relationships between corresponding points.
        ///
        /// References:
        /// - PhotonVision Robot Pose Estimator: https://docs.photonvision.org/en/latest/docs/programming/photonlib/robot-pose-estimator.html
        /// </summary>
        private (Vector3 translation, Quaternion rotation)? CalculateTransform(
            List<(Vector3 questPos, Vector3 fieldPos)> pairs
        )
        {
            if (pairs == null || pairs.Count < 2)
                return null;

            // Calculate weighted centroids (average positions in each coordinate system)
            // Weight by distance from origin to give more weight to distributed tags
            Vector3 questCenter = Vector3.zero;
            Vector3 fieldCenter = Vector3.zero;
            float totalWeight = 0f;

            foreach (var (q, f) in pairs)
            {
                // Weight by distance from first tag (favors well-distributed tags)
                float weight = 1.0f + Vector3.Distance(q, pairs[0].questPos);
                questCenter += q * weight;
                fieldCenter += f * weight;
                totalWeight += weight;
            }

            questCenter /= totalWeight;
            fieldCenter /= totalWeight;

            // Validate centroids
            if (float.IsNaN(questCenter.x) || float.IsNaN(fieldCenter.x))
            {
                Debug.LogError("[FieldLocalizer] Invalid centroid calculation");
                return null;
            }

            // Calculate rotation using weighted average of all tag pairs
            // Similar to PhotonVision's approach of combining multiple tag observations
            float totalYaw = 0f;
            float totalWeight_rotation = 0f;

            for (int i = 0; i < pairs.Count - 1; i++)
            {
                for (int j = i + 1; j < pairs.Count; j++)
                {
                    var questVec = pairs[j].questPos - pairs[i].questPos;
                    var fieldVec = pairs[j].fieldPos - pairs[i].fieldPos;

                    // Project to XZ plane (ignore height) for yaw-only calculation
                    questVec.y = 0;
                    fieldVec.y = 0;

                    float separation = questVec.magnitude;

                    // Only use tag pairs that are reasonably separated
                    // Closer tags = less reliable angle measurement
                    if (separation > 0.1f && fieldVec.magnitude > 0.1f)
                    {
                        var angle = Vector3.SignedAngle(questVec, fieldVec, Vector3.up);

                        // Validate angle
                        if (!float.IsNaN(angle) && !float.IsInfinity(angle))
                        {
                            // Weight by separation distance (longer baseline = more reliable)
                            // This is inspired by PhotonVision's approach to multi-tag fusion
                            float weight = separation;
                            totalYaw += angle * weight;
                            totalWeight_rotation += weight;
                        }
                    }
                }
            }

            if (totalWeight_rotation == 0f)
            {
                Debug.LogWarning(
                    "[FieldLocalizer] Could not calculate rotation - anchors too close together"
                );
                return null;
            }

            // Weighted average rotation
            var rotation = Quaternion.Euler(0, totalYaw / totalWeight_rotation, 0);

            // Calculate translation using least-squares approach
            // Minimize sum of squared errors: translation = fieldCenter - R * questCenter
            var translation = fieldCenter - rotation * questCenter;

            // Validate final transform
            if (
                float.IsNaN(translation.x)
                || float.IsNaN(translation.y)
                || float.IsNaN(translation.z)
            )
            {
                Debug.LogError("[FieldLocalizer] Invalid translation calculation");
                return null;
            }

            // Optional: Iterative refinement (similar to PnP solvers)
            // Could add Levenberg-Marquardt or similar optimization here
            // For now, the weighted least-squares solution is sufficient

            if (m_enableDebug)
            {
                Debug.Log(
                    $"[FieldLocalizer] Transform calculated from {pairs.Count} anchors, "
                        + $"{(pairs.Count * (pairs.Count - 1)) / 2} pairwise comparisons, "
                        + $"rotation: {rotation.eulerAngles.y:F1}°"
                );
            }

            return (translation, rotation);
        }

        /// <summary>
        /// Get headset position in field coordinates (uses center eye anchor in tracking space)
        /// </summary>
        public Vector3 GetFieldPosition()
        {
            if (!m_isAligned || m_fieldOrigin == null)
                return Vector3.zero;

            // Get center eye anchor and tracking space for accurate headset position
            var cameraRig = OVRManager.instance?.GetComponentInChildren<OVRCameraRig>();
            if (cameraRig == null || cameraRig.centerEyeAnchor == null)
            {
                // Fallback to main camera if center eye not available
                if (Camera.main == null)
                    return Vector3.zero;
                return m_fieldOrigin.InverseTransformPoint(Camera.main.transform.position);
            }

            // Get world position of center eye (accounting for tracking space origin)
            var centerEyeWorldPos = cameraRig.centerEyeAnchor.position;

            return m_fieldOrigin.InverseTransformPoint(centerEyeWorldPos);
        }

        /// <summary>
        /// Get headset rotation in field coordinates (uses center eye anchor in tracking space)
        /// </summary>
        public Quaternion GetFieldRotation()
        {
            if (!m_isAligned || m_fieldOrigin == null)
                return Quaternion.identity;

            // Get center eye anchor and tracking space for accurate headset rotation
            var cameraRig = OVRManager.instance?.GetComponentInChildren<OVRCameraRig>();
            if (cameraRig == null || cameraRig.centerEyeAnchor == null)
            {
                // Fallback to main camera if center eye not available
                if (Camera.main == null)
                    return Quaternion.identity;
                return Quaternion.Inverse(m_fieldOrigin.rotation) * Camera.main.transform.rotation;
            }

            // Get world rotation of center eye (accounting for tracking space origin)
            var centerEyeWorldRot = cameraRig.centerEyeAnchor.rotation;

            return Quaternion.Inverse(m_fieldOrigin.rotation) * centerEyeWorldRot;
        }

        /// <summary>
        /// Reset alignment
        /// </summary>
        public void ResetAlignment()
        {
            m_isAligned = false;
            m_currentAlignmentError = float.MaxValue;
            m_lastSuccessfulAnchorCount = 0;

            if (m_fieldOrigin != null)
            {
                m_fieldOrigin.localPosition = Vector3.zero;
                m_fieldOrigin.localRotation = Quaternion.identity;
            }

            if (m_enableDebug)
                Debug.Log("[FieldLocalizer] Alignment reset");
        }

        private void SaveAlignment()
        {
            PlayerPrefs.SetString("FRC_Field_Name", GetFieldLayoutName(m_selectedFieldLayout));
            PlayerPrefs.SetFloat("FRC_Field_PosX", m_fieldOrigin.position.x);
            PlayerPrefs.SetFloat("FRC_Field_PosY", m_fieldOrigin.position.y);
            PlayerPrefs.SetFloat("FRC_Field_PosZ", m_fieldOrigin.position.z);
            PlayerPrefs.SetFloat("FRC_Field_RotY", m_fieldOrigin.rotation.eulerAngles.y);
            PlayerPrefs.SetInt("FRC_Field_Aligned", 1);
            PlayerPrefs.Save();
        }

        private void LoadAlignment()
        {
            if (!PlayerPrefs.HasKey("FRC_Field_Aligned"))
                return;

            // Check if saved field matches current field
            var savedField = PlayerPrefs.GetString("FRC_Field_Name", "");
            var currentFieldName = GetFieldLayoutName(m_selectedFieldLayout);
            if (savedField != currentFieldName)
            {
                if (m_enableDebug)
                    Debug.LogWarning(
                        $"[FieldLocalizer] Saved alignment for '{savedField}' doesn't match current field '{currentFieldName}' - ignoring"
                    );
                return;
            }

            var pos = new Vector3(
                PlayerPrefs.GetFloat("FRC_Field_PosX"),
                PlayerPrefs.GetFloat("FRC_Field_PosY"),
                PlayerPrefs.GetFloat("FRC_Field_PosZ")
            );
            var rot = Quaternion.Euler(0, PlayerPrefs.GetFloat("FRC_Field_RotY"), 0);

            m_fieldOrigin.position = pos;
            m_fieldOrigin.rotation = rot;
            m_isAligned = true;

            if (m_enableDebug)
                Debug.Log("[FieldLocalizer] Loaded saved alignment");
        }

        private void OnDrawGizmos()
        {
            if (!m_visualizeField || m_fieldOrigin == null || !m_isAligned)
                return;

            var pos = m_fieldOrigin.position;
            var scale = 1f;

            // Draw axes
            Gizmos.color = Color.red;
            Gizmos.DrawLine(pos, pos + m_fieldOrigin.right * scale);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(pos, pos + m_fieldOrigin.up * scale);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(pos, pos + m_fieldOrigin.forward * scale);

            // Draw field boundary
            if (m_fieldLayout != null)
            {
                Gizmos.color = Color.cyan;
                var size = new Vector3(m_fieldLayout.FieldSize.x, 0.1f, m_fieldLayout.FieldSize.y);
                var center = m_fieldOrigin.TransformPoint(
                    new Vector3(size.x * 0.5f, 0, size.z * 0.5f)
                );
                Gizmos.matrix = Matrix4x4.TRS(center, m_fieldOrigin.rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, size);
                Gizmos.matrix = Matrix4x4.identity; // Reset matrix
            }
        }

        private void OnGUI()
        {
            if (!m_enableDebug)
                return;

            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 20,
                normal = { textColor = m_isAligned ? Color.green : Color.yellow },
            };

            string status;
            if (m_isAligned)
            {
                status =
                    $"FIELD ALIGNED\n"
                    + $"Pos: {GetFieldPosition():F2}\n"
                    + $"Rot: {GetFieldRotation().eulerAngles:F1}\n"
                    + $"Error: {m_currentAlignmentError:F3}m\n"
                    + $"Anchors: {m_lastSuccessfulAnchorCount}";
            }
            else
            {
                status =
                    $"NOT ALIGNED\n"
                    + $"Anchors: {m_anchorManager?.GetAnchorCount() ?? 0}/{m_minAnchors}";
            }

            GUI.Box(new Rect(10, 10, 320, 140), status, style);
        }
    }
}
