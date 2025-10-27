// Assets/AprilTag/Scripts/AprilTagFieldLayout.cs
// FRC field layout manager for AprilTag positions
// Loads and manages the official FRC field layout with tag positions

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Represents the layout of AprilTags on an FRC competition field.
    /// Stores tag positions and provides lookup functionality for localization.
    /// Format matches WPILib's AprilTag field layout JSON.
    /// </summary>
    [Serializable]
    public class AprilTagFieldLayout
    {
        [Header("Field Information")]
        [Tooltip("Field name (e.g., '2025-reefscape')")]
        public string FieldName = "2025-reefscape";

        [Tooltip("Field dimensions in meters (width x length)")]
        public Vector2 FieldSize = new Vector2(16.54175f, 8.21055f);

        [Header("AprilTag Positions")]
        [Tooltip("List of all AprilTags on the field with their positions and rotations")]
        public List<FieldTag> tags = new List<FieldTag>();

        // Quick lookup dictionary for tag positions
        private Dictionary<int, FieldTag> m_tagLookup = new Dictionary<int, FieldTag>();

        /// <summary>
        /// Represents a single AprilTag on the field
        /// </summary>
        [Serializable]
        public class FieldTag
        {
            [Tooltip("AprilTag ID (matches the tag family)")]
            public int id;

            [Tooltip(
                "Position on the field in meters (Unity coordinates: X=right, Y=up, Z=forward)"
            )]
            public Vector3 position;

            [Tooltip("Rotation in Euler angles (degrees)")]
            public Vector3 rotation;

            [Tooltip("Tag size in meters (physical edge length) - set from AprilTagController")]
            public float size;

            public FieldTag(int id, Vector3 pos, Vector3 rot, float size)
            {
                this.id = id;
                this.position = pos;
                this.rotation = rot;
                this.size = size;
            }
        }

        /// <summary>
        /// Initialize the lookup dictionary for fast tag queries
        /// </summary>
        public void BuildLookup()
        {
            m_tagLookup.Clear();
            foreach (var tag in tags)
            {
                m_tagLookup[tag.id] = tag;
            }
        }

        /// <summary>
        /// Get a tag by its ID
        /// </summary>
        public bool TryGetTag(int id, out FieldTag tag)
        {
            if (m_tagLookup.Count == 0)
                BuildLookup();

            return m_tagLookup.TryGetValue(id, out tag);
        }

        /// <summary>
        /// Check if a tag ID exists in the field layout
        /// </summary>
        public bool HasTag(int id)
        {
            if (m_tagLookup.Count == 0)
                BuildLookup();

            return m_tagLookup.ContainsKey(id);
        }

        /// <summary>
        /// Get all tag IDs in the field layout
        /// </summary>
        public List<int> GetAllTagIds()
        {
            if (m_tagLookup.Count == 0)
                BuildLookup();

            return new List<int>(m_tagLookup.Keys);
        }

        /// <summary>
        /// Load field layout from Resources folder
        /// Example: LoadFromResources("2025-reefscape") loads "Resources/FieldLayouts/2025-reefscape.json"
        /// </summary>
        public static AprilTagFieldLayout LoadFromResources(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                Debug.LogError("[AprilTagFieldLayout] Field layout name is empty");
                return null;
            }

            // Normalize: allow names with or without .json extension
            fieldName = fieldName.Trim();
            if (fieldName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                fieldName = Path.GetFileNameWithoutExtension(fieldName);
            }

            // Try common resource paths
            TextAsset textAsset = null;
            var candidatePaths = new string[]
            {
                $"FieldLayouts/{fieldName}", // preferred subfolder
                fieldName, // root of Resources
            };

            foreach (var path in candidatePaths)
            {
                textAsset = Resources.Load<TextAsset>(path);
                if (textAsset != null)
                {
                    break;
                }
            }

            if (textAsset == null)
            {
                Debug.LogError(
                    $"[AprilTagFieldLayout] Could not find field layout: 'FieldLayouts/{fieldName}.json' or '{fieldName}.json' in a Resources folder"
                );
                // List available layouts to help debugging (both FieldLayouts/ and root)
                var availableInSubdir = Resources.LoadAll<TextAsset>("FieldLayouts");
                var availableInRoot = Resources.LoadAll<TextAsset>(string.Empty);

                if (
                    (availableInSubdir != null && availableInSubdir.Length > 0)
                    || (availableInRoot != null && availableInRoot.Length > 0)
                )
                {
                    var names = string.Empty;
                    if (availableInSubdir != null)
                    {
                        for (int i = 0; i < availableInSubdir.Length; i++)
                        {
                            if (names.Length > 0)
                                names += ", ";
                            names += availableInSubdir[i].name;
                        }
                    }
                    if (availableInRoot != null)
                    {
                        for (int i = 0; i < availableInRoot.Length; i++)
                        {
                            // Avoid duplicates
                            if (!names.Contains(availableInRoot[i].name))
                            {
                                if (names.Length > 0)
                                    names += ", ";
                                names += availableInRoot[i].name;
                            }
                        }
                    }
                    Debug.LogError($"[AprilTagFieldLayout] Available layouts: {names}");
                }
                Debug.LogError(
                    $"[AprilTagFieldLayout] Please add the WPILib JSON file under any Resources folder, e.g., 'Assets/AprilTag/Resources/FieldLayouts/{fieldName}.json'"
                );
                return null;
            }

            return FromWPILibJson(textAsset.text, fieldName);
        }

        /// <summary>
        /// Parse WPILib JSON format and convert to Unity coordinates
        /// WPILib NWU: +X = away from alliance wall, +Y = left, +Z = up
        /// Unity: +X = right, +Y = up, +Z = forward
        /// Conversion: Unity.X = WPILib.X, Unity.Y = WPILib.Z, Unity.Z = -WPILib.Y
        /// </summary>
        /// <param name="json">WPILib field layout JSON</param>
        /// <param name="fieldName">Name of the field layout</param>
        /// <param name="tagSize">Physical tag size in meters (from AprilTagController)</param>
        public static AprilTagFieldLayout FromWPILibJson(
            string json,
            string fieldName,
            float tagSize = 0.165f
        )
        {
            try
            {
                var wpilibData = JsonUtility.FromJson<WPILibFieldLayout>(json);

                // Validate parsed data
                if (wpilibData == null)
                {
                    Debug.LogError("[AprilTagFieldLayout] Failed to parse JSON - null result");
                    return null;
                }

                if (wpilibData.Tags == null || wpilibData.Tags.Length == 0)
                {
                    Debug.LogError("[AprilTagFieldLayout] Invalid JSON - no tags found");
                    return null;
                }

                if (wpilibData.field == null)
                {
                    Debug.LogError("[AprilTagFieldLayout] Invalid JSON - missing field dimensions");
                    return null;
                }

                if (wpilibData.field.length <= 0 || wpilibData.field.width <= 0)
                {
                    Debug.LogError(
                        $"[AprilTagFieldLayout] Invalid field dimensions: {wpilibData.field.length}x{wpilibData.field.width}"
                    );
                    return null;
                }

                var layout = new AprilTagFieldLayout
                {
                    FieldName = fieldName, // Use the parameter name since JSON doesn't have it
                    FieldSize = new Vector2(
                        (float)wpilibData.field.length,
                        (float)wpilibData.field.width
                    ),
                };

                var seenIds = new HashSet<int>();

                foreach (var tag in wpilibData.Tags)
                {
                    // Validate tag data
                    if (
                        tag == null
                        || tag.pose == null
                        || tag.pose.translation == null
                        || tag.pose.rotation == null
                        || tag.pose.rotation.quaternion == null
                    )
                    {
                        Debug.LogWarning(
                            $"[AprilTagFieldLayout] Skipping tag with incomplete data"
                        );
                        continue;
                    }

                    // Check for duplicate IDs
                    if (seenIds.Contains(tag.ID))
                    {
                        Debug.LogWarning(
                            $"[AprilTagFieldLayout] Duplicate tag ID {tag.ID} - using first occurrence"
                        );
                        continue;
                    }
                    seenIds.Add(tag.ID);

                    // Validate tag ID
                    if (tag.ID < 0)
                    {
                        Debug.LogWarning(
                            $"[AprilTagFieldLayout] Invalid tag ID {tag.ID} - skipping"
                        );
                        continue;
                    }

                    // Convert WPILib NWU to Unity left-handed
                    var unityPos = new Vector3(
                        (float)tag.pose.translation.x, // X: away from alliance wall
                        (float)tag.pose.translation.z, // Y: up
                        -(float)tag.pose.translation.y // Z: -left = right (flip handedness)
                    );

                    // Note: Bounds checking is NOT performed here because the headset may be
                    // initialized far outside the field bounds. Validation happens after alignment
                    // in FRCFieldLocalizer when the field-to-Quest transform is established.

                    // Convert quaternion (NWU right-handed to Unity left-handed)
                    var wpilibQuat = new Quaternion(
                        (float)tag.pose.rotation.quaternion.X,
                        (float)tag.pose.rotation.quaternion.Y,
                        (float)tag.pose.rotation.quaternion.Z,
                        (float)tag.pose.rotation.quaternion.W
                    );

                    // Validate quaternion
                    if (
                        float.IsNaN(wpilibQuat.x)
                        || float.IsNaN(wpilibQuat.y)
                        || float.IsNaN(wpilibQuat.z)
                        || float.IsNaN(wpilibQuat.w)
                    )
                    {
                        Debug.LogWarning(
                            $"[AprilTagFieldLayout] Tag {tag.ID} has invalid rotation - using identity"
                        );
                        wpilibQuat = Quaternion.identity;
                    }

                    // Flip Y axis for handedness conversion
                    var unityQuat = new Quaternion(
                        wpilibQuat.x,
                        wpilibQuat.z,
                        -wpilibQuat.y,
                        wpilibQuat.w
                    );

                    layout.tags.Add(new FieldTag(tag.ID, unityPos, unityQuat.eulerAngles, tagSize));
                }

                if (layout.tags.Count == 0)
                {
                    Debug.LogError("[AprilTagFieldLayout] No valid tags loaded");
                    return null;
                }

                layout.BuildLookup();
                Debug.Log(
                    $"[AprilTagFieldLayout] Loaded {layout.tags.Count} tags from {wpilibData.field}"
                );
                return layout;
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[AprilTagFieldLayout] Failed to parse WPILib JSON: {e.Message}\n{e.StackTrace}"
                );
                return null;
            }
        }

        // WPILib JSON structure for deserialization
        [Serializable]
        private class WPILibFieldLayout
        {
            public WPILibTag[] Tags;
            public WPILibField field;
        }

        [Serializable]
        private class WPILibField
        {
            public double length;
            public double width;
        }

        [Serializable]
        private class WPILibTag
        {
            public int ID;
            public WPILibPose pose;
        }

        [Serializable]
        private class WPILibPose
        {
            public WPILibTranslation translation;
            public WPILibRotation rotation;
        }

        [Serializable]
        private class WPILibTranslation
        {
            public double x;
            public double y;
            public double z;
        }

        [Serializable]
        private class WPILibRotation
        {
            public WPILibQuaternion quaternion;
        }

        [Serializable]
        private class WPILibQuaternion
        {
            public double X;
            public double Y;
            public double Z;
            public double W;
        }

        /// <summary>
        /// Visualize the field layout in the Unity editor
        /// </summary>
        public void DrawGizmos(Transform origin)
        {
            if (tags == null || tags.Count == 0)
                return;

            foreach (var tag in tags)
            {
                // Draw tag position
                var worldPos = origin.TransformPoint(tag.position);
                var worldRot = origin.rotation * Quaternion.Euler(tag.rotation);

                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(worldPos, Vector3.one * tag.size);

                // Draw tag facing direction
                Gizmos.color = Color.red;
                Gizmos.DrawLine(worldPos, worldPos + worldRot * Vector3.forward * tag.size * 2f);

                // Draw tag ID label (in editor only)
#if UNITY_EDITOR
                UnityEditor.Handles.Label(worldPos, $"Tag {tag.id}");
#endif
            }

            // Draw field boundary
            Gizmos.color = Color.blue;
            var fieldCenter = origin.TransformPoint(
                new Vector3(FieldSize.x * 0.5f, 0f, FieldSize.y * 0.5f)
            );
            var fieldBounds = new Vector3(FieldSize.x, 0.1f, FieldSize.y);
            Gizmos.DrawWireCube(fieldCenter, fieldBounds);
        }
    }
}
