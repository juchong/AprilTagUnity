// Assets/AprilTag/Scripts/FRCFieldLocalizer.cs
// Simple field localization using spatial anchors and known field layout
// Transforms headset pose from Quest space to FRC field coordinates

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.IO;

namespace AprilTag
{
    /// <summary>
    /// Localizes Quest headset to FRC field coordinates using spatial anchors.
    /// Uses existing AprilTagSpatialAnchorManager and field layout to calculate transform.
    /// </summary>
    public class FRCFieldLocalizer : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Spatial anchor manager (auto-found if null)")]
        [SerializeField]
        private AprilTagSpatialAnchorManager m_anchorManager;

        [Tooltip("Field layout with tag positions")]
        [SerializeField]
        private AprilTagFieldLayout m_fieldLayout;

        [Header("Settings")]
        [Tooltip("Field layout name to load from Resources/FieldLayouts/")]
        [SerializeField]
        private string m_fieldLayoutName = "2025-reefscape";

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

        [Header("Field Loading")]
        [Tooltip("Use embedded 2025-reefscape-welded field layout")]
        [SerializeField]
        private bool m_useEmbeddedLayout = true;

        // Embedded 2025 Reefscape Welded field layout JSON
        private const string EMBEDDED_REEFSCAPE_WELDED_JSON = @"{
  ""tags"": [
    {
      ""ID"": 1,
      ""pose"": {
        ""translation"": {
          ""x"": 16.697198,
          ""y"": 0.65532,
          ""z"": 1.4859
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 0.4539904997395468,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 0.8910065241883678
          }
        }
      }
    },
    {
      ""ID"": 2,
      ""pose"": {
        ""translation"": {
          ""x"": 16.697198,
          ""y"": 7.3964799999999995,
          ""z"": 1.4859
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": -0.45399049973954675,
            ""X"": -0.0,
            ""Y"": 0.0,
            ""Z"": 0.8910065241883679
          }
        }
      }
    },
    {
      ""ID"": 3,
      ""pose"": {
        ""translation"": {
          ""x"": 11.560809999999998,
          ""y"": 8.05561,
          ""z"": 1.30175
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": -0.7071067811865475,
            ""X"": -0.0,
            ""Y"": 0.0,
            ""Z"": 0.7071067811865476
          }
        }
      }
    },
    {
      ""ID"": 4,
      ""pose"": {
        ""translation"": {
          ""x"": 9.276079999999999,
          ""y"": 6.137656,
          ""z"": 1.8679160000000001
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 0.9659258262890683,
            ""X"": 0.0,
            ""Y"": 0.25881904510252074,
            ""Z"": 0.0
          }
        }
      }
    },
    {
      ""ID"": 5,
      ""pose"": {
        ""translation"": {
          ""x"": 9.276079999999999,
          ""y"": 1.914906,
          ""z"": 1.8679160000000001
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 0.9659258262890683,
            ""X"": 0.0,
            ""Y"": 0.25881904510252074,
            ""Z"": 0.0
          }
        }
      }
    },
    {
      ""ID"": 6,
      ""pose"": {
        ""translation"": {
          ""x"": 13.474446,
          ""y"": 3.3063179999999996,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": -0.8660254037844387,
            ""X"": -0.0,
            ""Y"": 0.0,
            ""Z"": 0.49999999999999994
          }
        }
      }
    },
    {
      ""ID"": 7,
      ""pose"": {
        ""translation"": {
          ""x"": 13.890498,
          ""y"": 4.0259,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 1.0,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 0.0
          }
        }
      }
    },
    {
      ""ID"": 8,
      ""pose"": {
        ""translation"": {
          ""x"": 13.474446,
          ""y"": 4.745482,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 0.8660254037844387,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 0.49999999999999994
          }
        }
      }
    },
    {
      ""ID"": 9,
      ""pose"": {
        ""translation"": {
          ""x"": 12.643358,
          ""y"": 4.745482,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 0.5000000000000001,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 0.8660254037844386
          }
        }
      }
    },
    {
      ""ID"": 10,
      ""pose"": {
        ""translation"": {
          ""x"": 12.227305999999999,
          ""y"": 4.0259,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 6.123233995736766e-17,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 1.0
          }
        }
      }
    },
    {
      ""ID"": 11,
      ""pose"": {
        ""translation"": {
          ""x"": 12.643358,
          ""y"": 3.3063179999999996,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": -0.4999999999999998,
            ""X"": -0.0,
            ""Y"": 0.0,
            ""Z"": 0.8660254037844387
          }
        }
      }
    },
    {
      ""ID"": 12,
      ""pose"": {
        ""translation"": {
          ""x"": 0.851154,
          ""y"": 0.65532,
          ""z"": 1.4859
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 0.8910065241883679,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 0.45399049973954675
          }
        }
      }
    },
    {
      ""ID"": 13,
      ""pose"": {
        ""translation"": {
          ""x"": 0.851154,
          ""y"": 7.3964799999999995,
          ""z"": 1.4859
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": -0.8910065241883678,
            ""X"": -0.0,
            ""Y"": 0.0,
            ""Z"": 0.45399049973954686
          }
        }
      }
    },
    {
      ""ID"": 14,
      ""pose"": {
        ""translation"": {
          ""x"": 8.272272,
          ""y"": 6.137656,
          ""z"": 1.8679160000000001
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 5.914589856893349e-17,
            ""X"": -0.25881904510252074,
            ""Y"": 1.5848095757158825e-17,
            ""Z"": 0.9659258262890683
          }
        }
      }
    },
    {
      ""ID"": 15,
      ""pose"": {
        ""translation"": {
          ""x"": 8.272272,
          ""y"": 1.914906,
          ""z"": 1.8679160000000001
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 5.914589856893349e-17,
            ""X"": -0.25881904510252074,
            ""Y"": 1.5848095757158825e-17,
            ""Z"": 0.9659258262890683
          }
        }
      }
    },
    {
      ""ID"": 16,
      ""pose"": {
        ""translation"": {
          ""x"": 5.9875419999999995,
          ""y"": -0.0038099999999999996,
          ""z"": 1.30175
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 0.7071067811865476,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 0.7071067811865476
          }
        }
      }
    },
    {
      ""ID"": 17,
      ""pose"": {
        ""translation"": {
          ""x"": 4.073905999999999,
          ""y"": 3.3063179999999996,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": -0.4999999999999998,
            ""X"": -0.0,
            ""Y"": 0.0,
            ""Z"": 0.8660254037844387
          }
        }
      }
    },
    {
      ""ID"": 18,
      ""pose"": {
        ""translation"": {
          ""x"": 3.6576,
          ""y"": 4.0259,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 6.123233995736766e-17,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 1.0
          }
        }
      }
    },
    {
      ""ID"": 19,
      ""pose"": {
        ""translation"": {
          ""x"": 4.073905999999999,
          ""y"": 4.745482,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 0.5000000000000001,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 0.8660254037844386
          }
        }
      }
    },
    {
      ""ID"": 20,
      ""pose"": {
        ""translation"": {
          ""x"": 4.904739999999999,
          ""y"": 4.745482,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 0.8660254037844387,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 0.49999999999999994
          }
        }
      }
    },
    {
      ""ID"": 21,
      ""pose"": {
        ""translation"": {
          ""x"": 5.321046,
          ""y"": 4.0259,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": 1.0,
            ""X"": 0.0,
            ""Y"": 0.0,
            ""Z"": 0.0
          }
        }
      }
    },
    {
      ""ID"": 22,
      ""pose"": {
        ""translation"": {
          ""x"": 4.904739999999999,
          ""y"": 3.3063179999999996,
          ""z"": 0.308102
        },
        ""rotation"": {
          ""quaternion"": {
            ""W"": -0.8660254037844387,
            ""X"": -0.0,
            ""Y"": 0.0,
            ""Z"": 0.49999999999999994
          }
        }
      }
    }
  ],
  ""field"": {
    ""length"": 17.548,
    ""width"": 8.052
  }
}";

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

        [Header("Debug")]
        [Tooltip("Enable debug logging")]
        [SerializeField]
        private bool m_enableDebug = true;

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

        private void Start()
        {
            // Find anchor manager
            if (m_anchorManager == null)
                m_anchorManager = FindFirstObjectByType<AprilTagSpatialAnchorManager>();

            // Load field layout
            if (m_useEmbeddedLayout)
            {
                // Use embedded JSON (works on all platforms)
                m_fieldLayout = AprilTagFieldLayout.FromWPILibJson(
                    EMBEDDED_REEFSCAPE_WELDED_JSON,
                    "2025-reefscape-welded"
                );

                if (m_fieldLayout == null)
                {
                    Debug.LogError("[FRCFieldLocalizer] Failed to parse embedded field layout");
                    enabled = false;
                    return;
                }
            }
            else
            {
                // Load via name lookup from Resources
                if (m_fieldLayout == null)
                {
                    m_fieldLayout = AprilTagFieldLayout.LoadFromResources(m_fieldLayoutName);
                    if (m_fieldLayout == null)
                    {
                        Debug.LogError(
                            $"[FRCFieldLocalizer] Failed to load field layout '{m_fieldLayoutName}' - localization will not work"
                        );
                        enabled = false;
                        return;
                    }
                }

                // If a serialized (inline) field layout exists but has no tags, try loading by name
                if (m_fieldLayout != null && (m_fieldLayout.tags == null || m_fieldLayout.tags.Count == 0))
                {
                    var loadedLayout = AprilTagFieldLayout.LoadFromResources(m_fieldLayoutName);
                    if (loadedLayout != null)
                    {
                        m_fieldLayout = loadedLayout;
                    }
                    else
                    {
                        Debug.LogError(
                            $"[FRCFieldLocalizer] Field layout '{m_fieldLayoutName}' is empty or missing - localization will not work"
                        );
                        enabled = false;
                        return;
                    }
                }
            }

            if (m_enableDebug)
            {
                if (m_useEmbeddedLayout)
                {
                    Debug.Log($"[FRCFieldLocalizer] Using EMBEDDED field layout '2025-reefscape-welded' with {m_fieldLayout.tags?.Count ?? 0} tags");
                }
                else
                {
                    Debug.Log(
                        $"[FRCFieldLocalizer] Using field layout '{m_fieldLayout.fieldName}' with {m_fieldLayout.tags?.Count ?? 0} tags"
                    );
                }
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
                if (m_enableDebug && Time.frameCount % 300 == 0)
                    Debug.LogWarning("[FRCFieldLocalizer] No anchor manager - cannot align");
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
                    if (m_enableDebug)
                        Debug.Log(
                            $"[FRCFieldLocalizer] Attempting to improve alignment with {currentAnchorCount} anchors (had {m_lastSuccessfulAnchorCount})"
                        );
                    CalculateAlignment();
                }
            }

            // Log field position continuously once aligned and minimum anchors are met (for adb logcat filtering)
            if (m_isAligned && m_anchorManager != null)
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
                            var trackingSpacePos = cameraRig.trackingSpace != null ? cameraRig.trackingSpace.position : Vector3.zero;
                            Debug.Log($"[FIELD_DEBUG] Center eye world pos: {centerEyePos:F3}, Tracking space: {trackingSpacePos:F3}, Field origin: {m_fieldOrigin.position:F3}");
                        }
                    }
                    
                    Debug.Log($"[FIELD_POSE] pos_ft:{fieldPosFeet.x:F3},{fieldPosFeet.y:F3},{fieldPosFeet.z:F3} rot:{fieldRot.eulerAngles.x:F1},{fieldRot.eulerAngles.y:F1},{fieldRot.eulerAngles.z:F1} anchors:{currentAnchorCount}");
                }
            }
        }

        private void OnAnchorCreated(int tagId, OVRSpatialAnchor anchor)
        {
            if (m_enableDebug)
                Debug.Log($"[FRCFieldLocalizer] New anchor for tag {tagId}");

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

                // Validate anchor position
                if (
                    float.IsNaN(anchor.transform.position.x)
                    || float.IsNaN(anchor.transform.position.y)
                    || float.IsNaN(anchor.transform.position.z)
                )
                {
                    Debug.LogWarning(
                        $"[FRCFieldLocalizer] Tag {tagId} anchor has invalid position - skipping"
                    );
                    continue;
                }

                pairs.Add((anchor.transform.position, fieldTag.position, tagId));
            }

            if (pairs.Count < m_minAnchors)
            {
                if (m_enableDebug && Time.frameCount % 60 == 0)
                    Debug.Log(
                        $"[FRCFieldLocalizer] Need {m_minAnchors} anchors, have {pairs.Count}"
                    );
                return;
            }

            // Apply outlier rejection if enabled
            var filteredPairs = pairs;
            if (m_enableOutlierRejection && pairs.Count > m_minAnchors)
            {
                filteredPairs = RejectOutliers(pairs);
                if (filteredPairs.Count < m_minAnchors)
                {
                    Debug.LogWarning(
                        $"[FRCFieldLocalizer] Outlier rejection left only {filteredPairs.Count} anchors - using all {pairs.Count} anchors"
                    );
                    filteredPairs = pairs;
                }
                else if (filteredPairs.Count < pairs.Count && m_enableDebug)
                {
                    Debug.Log(
                        $"[FRCFieldLocalizer] Rejected {pairs.Count - filteredPairs.Count} outliers, using {filteredPairs.Count} anchors"
                    );
                }
            }

            // Calculate transform
            var result = CalculateTransform(
                filteredPairs.Select(p => (p.questPos, p.fieldPos)).ToList()
            );
            if (!result.HasValue)
            {
                Debug.LogWarning("[FRCFieldLocalizer] Transform calculation failed");
                return;
            }

            var (translation, rotation) = result.Value;

            // Validate transform
            var error = CalculateAlignmentError(filteredPairs, translation, rotation);

            if (error > m_maxAlignmentError)
            {
                Debug.LogWarning(
                    $"[FRCFieldLocalizer] Alignment error {error:F3}m exceeds threshold {m_maxAlignmentError:F3}m - rejecting"
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
                        $"[FRCFieldLocalizer] New alignment error {error:F3}m worse than current {m_currentAlignmentError:F3}m - keeping existing alignment"
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
                Debug.Log(
                    $"[FRCFieldLocalizer] ✓ Aligned using {filteredPairs.Count} anchors with error {error:F3}m"
                );

            // Save and notify
            if (m_persistAlignment)
                SaveAlignment();
            OnAligned?.Invoke();
        }

        /// <summary>
        /// Reject outlier anchors based on distance consistency
        /// </summary>
        private List<(Vector3 questPos, Vector3 fieldPos, int tagId)> RejectOutliers(
            List<(Vector3 questPos, Vector3 fieldPos, int tagId)> pairs
        )
        {
            var errors = new List<(int index, float error)>();

            // Calculate pairwise distance errors
            for (int i = 0; i < pairs.Count; i++)
            {
                float totalError = 0f;
                int comparisons = 0;

                for (int j = 0; j < pairs.Count; j++)
                {
                    if (i == j)
                        continue;

                    var questDist = Vector3.Distance(pairs[i].questPos, pairs[j].questPos);
                    var fieldDist = Vector3.Distance(pairs[i].fieldPos, pairs[j].fieldPos);
                    var distError = Mathf.Abs(questDist - fieldDist);

                    totalError += distError;
                    comparisons++;
                }

                var avgError = comparisons > 0 ? totalError / comparisons : 0f;
                errors.Add((i, avgError));
            }

            // Remove anchors with high average error
            var filtered = new List<(Vector3 questPos, Vector3 fieldPos, int tagId)>();
            foreach (var (index, error) in errors)
            {
                if (error <= m_maxOutlierError)
                {
                    filtered.Add(pairs[index]);
                }
                else if (m_enableDebug)
                {
                    Debug.Log(
                        $"[FRCFieldLocalizer] Rejecting tag {pairs[index].tagId} as outlier (error: {error:F3}m)"
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
        /// Calculate transform from Quest space to field space
        /// </summary>
        private (Vector3 translation, Quaternion rotation)? CalculateTransform(
            List<(Vector3 questPos, Vector3 fieldPos)> pairs
        )
        {
            if (pairs == null || pairs.Count < 2)
                return null;

            // Calculate centroids
            Vector3 questCenter = Vector3.zero;
            Vector3 fieldCenter = Vector3.zero;

            foreach (var (q, f) in pairs)
            {
                questCenter += q;
                fieldCenter += f;
            }
            questCenter /= pairs.Count;
            fieldCenter /= pairs.Count;

            // Validate centroids
            if (float.IsNaN(questCenter.x) || float.IsNaN(fieldCenter.x))
            {
                Debug.LogError("[FRCFieldLocalizer] Invalid centroid calculation");
                return null;
            }

            // Calculate rotation (yaw only for flat field)
            float totalYaw = 0f;
            int count = 0;

            for (int i = 0; i < pairs.Count - 1; i++)
            {
                for (int j = i + 1; j < pairs.Count; j++)
                {
                    var questVec = pairs[j].questPos - pairs[i].questPos;
                    var fieldVec = pairs[j].fieldPos - pairs[i].fieldPos;

                    // Project to XZ plane (ignore height)
                    questVec.y = 0;
                    fieldVec.y = 0;

                    if (questVec.magnitude > 0.1f && fieldVec.magnitude > 0.1f)
                    {
                        var angle = Vector3.SignedAngle(questVec, fieldVec, Vector3.up);

                        // Validate angle
                        if (!float.IsNaN(angle) && !float.IsInfinity(angle))
                        {
                            totalYaw += angle;
                            count++;
                        }
                    }
                }
            }

            if (count == 0)
            {
                Debug.LogWarning(
                    "[FRCFieldLocalizer] Could not calculate rotation - anchors too close together"
                );
                return null;
            }

            var rotation = Quaternion.Euler(0, totalYaw / count, 0);
            var translation = fieldCenter - rotation * questCenter;

            // Validate final transform
            if (
                float.IsNaN(translation.x)
                || float.IsNaN(translation.y)
                || float.IsNaN(translation.z)
            )
            {
                Debug.LogError("[FRCFieldLocalizer] Invalid translation calculation");
                return null;
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
                Debug.Log("[FRCFieldLocalizer] Alignment reset");
        }

        private void SaveAlignment()
        {
            PlayerPrefs.SetString("FRC_Field_Name", m_fieldLayoutName);
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
            if (savedField != m_fieldLayoutName)
            {
                if (m_enableDebug)
                    Debug.LogWarning(
                        $"[FRCFieldLocalizer] Saved alignment for '{savedField}' doesn't match current field '{m_fieldLayoutName}' - ignoring"
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
                Debug.Log("[FRCFieldLocalizer] Loaded saved alignment");
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
                var size = new Vector3(m_fieldLayout.fieldSize.x, 0.1f, m_fieldLayout.fieldSize.y);
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
