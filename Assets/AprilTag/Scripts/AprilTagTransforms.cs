// Assets/AprilTag/Scripts/AprilTagTransforms.cs
// AirTag Processing Transformations for Quest Headsets

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AprilTag; // locally integrated AprilTag library
using Meta.XR;
using PassthroughCameraSamples;
using Unity.XR.CoreUtils;
using UnityEngine;

public class AprilTagTransforms : MonoBehaviour
{
    [Header("Controller")]
    [Tooltip("AprilTagController providing shared configuration and helpers")]
    [SerializeField]
    private AprilTagController m_controller;

    // Cache for last known raycast distances per tag to ensure consistent positioning
    // When environment raycast misses, we use the last successful distance instead of
    // switching to a completely different positioning method
    private Dictionary<int, float> m_lastRaycastDistance = new Dictionary<int, float>();

    // Controller-backed accessors to avoid duplicated state
    private bool m_enableAllDebugLogging =>
        m_controller != null && m_controller.EnableAllDebugLogging;
    private EnvironmentRaycastManager m_environmentRaycastManager =>
        m_controller != null ? m_controller.EnvironmentRaycastManager : null;
    private Vector3 m_positionOffset =>
        m_controller != null ? m_controller.PositionOffset : Vector3.zero;
    private Vector3 m_rotationOffset =>
        m_controller != null ? m_controller.RotationOffset : Vector3.zero;
    private float m_positionScaleFactor =>
        m_controller != null ? m_controller.PositionScaleFactor : 1.0f;
    private float m_minDetectionDistance =>
        m_controller != null ? m_controller.MinDetectionDistance : 0.3f;
    private float m_maxDetectionDistance =>
        m_controller != null ? m_controller.MaxDetectionDistance : 15.0f;
    private bool m_enableDistanceScaling =>
        m_controller != null && m_controller.IsDistanceScalingEnabled;

    private PassthroughCameraEye GetWebCamManagerEye()
    {
        return m_controller != null
            ? m_controller.GetWebCamManagerEye()
            : PassthroughCameraEye.Left;
    }

    private Transform GetCorrectCameraReference()
    {
        return m_controller != null ? m_controller.GetCorrectCameraReference() : transform;
    }

    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Alternate intrinsics path)
    public Vector2? TryGetCornerBasedCenterWithIntrinsics(
        int tagId,
        List<object> rawDetections,
        PassthroughCameraIntrinsics intrinsics
    )
    {
        // Try to find the raw detection data for this specific tag ID and extract corner coordinates with intrinsics
        try
        {
            foreach (var detection in rawDetections)
            {
                var detectionType = detection.GetType();

                // Try to get the ID field/property
                var idProperty =
                    detectionType.GetProperty("ID")
                    ?? detectionType.GetProperty("Id")
                    ?? detectionType.GetProperty("id");
                var idField =
                    detectionType.GetField("ID")
                    ?? detectionType.GetField("Id")
                    ?? detectionType.GetField("id");

                var detectionId = -1;
                if (idProperty != null)
                {
                    detectionId = (int)idProperty.GetValue(detection);
                }
                else if (idField != null)
                {
                    detectionId = (int)idField.GetValue(detection);
                }

                if (detectionId == tagId)
                {
                    // Found the matching detection, try to extract corner coordinates with intrinsics
                    return ExtractCornerCenterWithIntrinsics(detection, intrinsics);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                $"[Transforms] Error extracting corner center for tag {tagId}: {e.Message}"
            );
        }

        return null;
    }

    private Vector2? ExtractCornerCenterWithIntrinsics(
        object detection,
        PassthroughCameraIntrinsics intrinsics
    )
    {
        // Extract corner coordinates from the Detection object and calculate center using camera intrinsics
        try
        {
            var detectionType = detection.GetType();

            // Try to access corner coordinates based on the Detection structure we found
            // The structure has: c0, c1, p00, p01, p10, p11, p20, p21, p30, p31
            // But they might be stored as arrays or in a different format
            var cornerFields = new[]
            {
                ("c0", "c1"), // Corner 0
                ("p00", "p01"), // Corner 1
                ("p10", "p11"), // Corner 2
                ("p20", "p21"), // Corner 3
            };

            // Also try alternative field names that might be used
            var alternativeFields = new[]
            {
                ("c", "c"), // Single field with array
                ("p", "p"), // Single field with array
                ("corners", "corners"), // Array of corners
                ("points", "points"), // Array of points
            };

            var corners = new List<Vector2>();

            foreach (var (xField, yField) in cornerFields)
            {
                // Try to get field first, then property with more permissive binding flags
                var xFieldRef = detectionType.GetField(
                    xField,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                var yFieldRef = detectionType.GetField(
                    yField,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );

                double x = 0,
                    y = 0;
                bool xFound = false,
                    yFound = false;

                // Try to get X coordinate
                if (xFieldRef != null)
                {
                    try
                    {
                        var xValue = xFieldRef.GetValue(detection);
                        x = (double)xValue;
                        xFound = true;
                    }
                    catch (Exception e)
                    {
                        if (m_enableAllDebugLogging)
                        {
                            Debug.LogWarning(
                                $"[Transforms] Error getting {xField} field value: {e.Message}"
                            );
                        }
                    }
                }
                else
                {
                    var xProp = detectionType.GetProperty(xField);
                    if (xProp != null)
                    {
                        try
                        {
                            var xValue = xProp.GetValue(detection);
                            x = (double)xValue;
                            xFound = true;
                        }
                        catch (Exception e)
                        {
                            if (m_enableAllDebugLogging)
                            {
                                Debug.LogWarning(
                                    $"[Transforms] Error getting {xField} property value: {e.Message}"
                                );
                            }
                        }
                    }
                }

                // Try to get Y coordinate
                if (yFieldRef != null)
                {
                    try
                    {
                        var yValue = yFieldRef.GetValue(detection);
                        y = (double)yValue;
                        yFound = true;
                    }
                    catch (Exception e)
                    {
                        if (m_enableAllDebugLogging)
                        {
                            Debug.LogWarning(
                                $"[Transforms] Error getting {yField} field value: {e.Message}"
                            );
                        }
                    }
                }
                else
                {
                    var yProp = detectionType.GetProperty(yField);
                    if (yProp != null)
                    {
                        try
                        {
                            var yValue = yProp.GetValue(detection);
                            y = (double)yValue;
                            yFound = true;
                        }
                        catch (Exception e)
                        {
                            if (m_enableAllDebugLogging)
                            {
                                Debug.LogWarning(
                                    $"[Transforms] Error getting {yField} property value: {e.Message}"
                                );
                            }
                        }
                    }
                }

                if (xFound && yFound)
                {
                    // Convert coordinates using camera intrinsics for better alignment
                    var unityCorner = ConvertAprilTagToUnityCoordinatesWithIntrinsics(
                        x,
                        y,
                        intrinsics
                    );
                    corners.Add(unityCorner);
                }
            }

            if (corners.Count >= 4)
            {
                // Calculate center point from corners
                var center = Vector2.zero;
                foreach (var corner in corners)
                {
                    center += corner;
                }
                center /= corners.Count;

                return center;
            }
            else
            {
                // Try alternative field names
                foreach (var (xField, yField) in alternativeFields)
                {
                    var xFieldRef = detectionType.GetField(
                        xField,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                    var yFieldRef = detectionType.GetField(
                        yField,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                    if (xFieldRef != null && yFieldRef != null)
                    {
                        try
                        {
                            var xValue = xFieldRef.GetValue(detection);
                            var yValue = yFieldRef.GetValue(detection);

                            // Check if these are arrays
                            if (xValue is Array xArray && yValue is Array yArray)
                            {
                                if (xArray.Length >= 4 && yArray.Length >= 4)
                                {
                                    for (var i = 0; i < 4; i++)
                                    {
                                        var x = Convert.ToDouble(xArray.GetValue(i));
                                        var y = Convert.ToDouble(yArray.GetValue(i));
                                        // Convert coordinates using camera intrinsics for better alignment
                                        var unityCorner =
                                            ConvertAprilTagToUnityCoordinatesWithIntrinsics(
                                                x,
                                                y,
                                                intrinsics
                                            );
                                        corners.Add(unityCorner);
                                    }

                                    break;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            if (m_enableAllDebugLogging)
                            {
                                Debug.LogWarning(
                                    $"[Transforms] Error with alternative fields {xField}, {yField}: {e.Message}"
                                );
                            }
                        }
                    }
                }

                if (corners.Count >= 4)
                {
                    // Calculate center point from corners
                    var center = Vector2.zero;
                    foreach (var corner in corners)
                    {
                        center += corner;
                    }
                    center /= corners.Count;

                    return center;
                }
            }
        }
        catch (Exception e)
        {
            if (m_enableAllDebugLogging)
            {
                Debug.LogWarning($"[Transforms] Error extracting corner center: {e.Message}");
            }
        }

        return null;
    }

    private Vector2 ConvertAprilTagToUnityCoordinatesWithIntrinsics(
        double x,
        double y,
        PassthroughCameraIntrinsics intrinsics
    )
    {
        // Convert from AprilTag image coordinates to Unity screen coordinates using camera intrinsics
        // This provides better alignment by accounting for camera-specific parameters

        // Normalize coordinates to [0,1] range
        var perX = (float)x / intrinsics.Resolution.x;
        var perY = (float)y / intrinsics.Resolution.y;

        // Apply Y-flip transformation like MultiObjectDetection: (1.0f - perY)
        var flippedPerY = 1.0f - perY;

        // Convert back to pixel coordinates
        var screenX = perX * intrinsics.Resolution.x;
        var screenY = flippedPerY * intrinsics.Resolution.y;

        return new Vector2(screenX, screenY);
    }

    /// USAGE: REFERENCED (supporting conversion). Keep if using non-intrinsics path.
    /// <summary>
    /// Converts AprilTag image coordinates to Unity screen coordinates (non-intrinsics path).
    /// </summary>
    public Vector2 ConvertAprilTagToUnityCoordinates(double x, double y)
    {
        // Convert from AprilTag image coordinates to Unity screen coordinates
        // Following MultiObjectDetection example exactly
        // AprilTag: X-right, Y-down (image space)
        // Unity: X-right, Y-up (screen space)
        // MultiObjectDetection uses: (1.0f - perY) for Y flip

        return new Vector2((float)x, (float)y);
    }

    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Corner-based world position)
    public Vector3 GetWorldPositionFromCornerCenter(Vector2 cornerCenter, TagPose tagPose)
    {
        // Follow MultiObjectDetection pattern exactly for 2D-to-3D projection
        try
        {
            // Get camera intrinsics and resolution
            var eye = GetWebCamManagerEye();
            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
            var camRes = intrinsics.Resolution;

            // Convert corner center to normalized coordinates (0-1 range)
            var perX = cornerCenter.x / camRes.x;
            var perY = cornerCenter.y / camRes.y;

            // Apply Y-flip transformation like MultiObjectDetection: (1.0f - perY)
            var flippedPerY = 1.0f - perY;

            // Convert to pixel coordinates with Y-flip
            var centerPixel = new Vector2Int(
                Mathf.RoundToInt(perX * camRes.x),
                Mathf.RoundToInt(flippedPerY * camRes.y)
            );

            // Create ray from screen point using proper camera intrinsics
            var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(eye, centerPixel);

            // Use environment raycasting to place object on ground (like the working method)
            if (m_environmentRaycastManager != null)
            {
                if (m_environmentRaycastManager.Raycast(ray, out var hitInfo))
                {
                    // Raycast hit - cache the distance for this tag
                    var raycastDistance = Vector3.Distance(ray.origin, hitInfo.point);
                    m_lastRaycastDistance[tagPose.ID] = raycastDistance;

                    if (m_enableAllDebugLogging)
                    {
                        Debug.Log(
                            $"[Transforms] Tag {tagPose.ID} raycast HIT at: {hitInfo.point}, distance: {raycastDistance:F3}m"
                        );
                    }
                    return hitInfo.point;
                }
                else
                {
                    // Raycast missed - use last known distance if available
                    if (m_lastRaycastDistance.TryGetValue(tagPose.ID, out var lastDistance))
                    {
                        var consistentPosition = ray.origin + ray.direction * lastDistance;

                        if (m_enableAllDebugLogging)
                        {
                            Debug.Log(
                                $"[Transforms] Tag {tagPose.ID} raycast MISS, using last known distance: {lastDistance:F3}m -> {consistentPosition}"
                            );
                        }

                        return consistentPosition;
                    }

                    if (m_enableAllDebugLogging)
                    {
                        Debug.LogWarning(
                            $"[Transforms] Tag {tagPose.ID} raycast MISS with no history, using tag distance fallback"
                        );
                    }
                }
            }

            // Fallback: use AprilTag's 3D pose distance for initial positioning
            // This ensures we use the ray direction but with the tag's reported distance
            var tagDistance = tagPose.Position.magnitude;
            var clampedDistance = Mathf.Clamp(
                tagDistance,
                m_minDetectionDistance,
                m_maxDetectionDistance
            );

            if (m_enableDistanceScaling)
            {
                clampedDistance = ApplyDistanceScaling(clampedDistance);
            }

            var fallbackPosition = ray.origin + ray.direction * clampedDistance;

            // Cache this distance for future frames
            m_lastRaycastDistance[tagPose.ID] = clampedDistance;

            if (m_enableAllDebugLogging)
            {
                Debug.Log(
                    $"[Transforms] Tag {tagPose.ID} using tag distance fallback: {clampedDistance:F3}m -> {fallbackPosition}"
                );
            }

            return fallbackPosition;
        }
        catch (Exception e)
        {
            if (m_enableAllDebugLogging)
            {
                Debug.LogWarning($"[Transforms] Error in corner-based positioning: {e.Message}");
            }

            // Final fallback to 3D pose estimation
            return tagPose.Position * m_positionScaleFactor;
        }
    }

    private Vector2? ExtractCornerCenter(object detection)
    {
        // Extract corner coordinates from the Detection object and calculate center
        try
        {
            var detectionType = detection.GetType();

            // Try to access corner coordinates based on the Detection structure we found
            // The structure has: c0, c1, p00, p01, p10, p11, p20, p21, p30, p31
            // But they might be stored as arrays or in a different format
            var cornerFields = new[]
            {
                ("c0", "c1"), // Corner 0
                ("p00", "p01"), // Corner 1
                ("p10", "p11"), // Corner 2
                ("p20", "p21"), // Corner 3
            };

            // Also try alternative field names that might be used
            var alternativeFields = new[]
            {
                ("c", "c"), // Single field with array
                ("p", "p"), // Single field with array
                ("corners", "corners"), // Array of corners
                ("points", "points"), // Array of points
            };

            var corners = new List<Vector2>();

            foreach (var (xField, yField) in cornerFields)
            {
                // Try to get field first, then property with more permissive binding flags
                var xFieldRef = detectionType.GetField(
                    xField,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                var yFieldRef = detectionType.GetField(
                    yField,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );

                double x = 0,
                    y = 0;
                bool xFound = false,
                    yFound = false;

                // Try to get X coordinate
                if (xFieldRef != null)
                {
                    try
                    {
                        var xValue = xFieldRef.GetValue(detection);
                        x = (double)xValue;
                        xFound = true;
                    }
                    catch (Exception e)
                    {
                        if (m_enableAllDebugLogging)
                        {
                            Debug.LogWarning(
                                $"[Transforms] Error getting {xField} field value: {e.Message}"
                            );
                        }
                    }
                }
                else
                {
                    var xProp = detectionType.GetProperty(xField);
                    if (xProp != null)
                    {
                        try
                        {
                            var xValue = xProp.GetValue(detection);
                            x = (double)xValue;
                            xFound = true;
                        }
                        catch (Exception e)
                        {
                            if (m_enableAllDebugLogging)
                            {
                                Debug.LogWarning(
                                    $"[Transforms] Error getting {xField} property value: {e.Message}"
                                );
                            }
                        }
                    }
                }

                // Try to get Y coordinate
                if (yFieldRef != null)
                {
                    try
                    {
                        var yValue = yFieldRef.GetValue(detection);
                        y = (double)yValue;
                        yFound = true;
                    }
                    catch (Exception e)
                    {
                        if (m_enableAllDebugLogging)
                        {
                            Debug.LogWarning(
                                $"[Transforms] Error getting {yField} field value: {e.Message}"
                            );
                        }
                    }
                }
                else
                {
                    var yProp = detectionType.GetProperty(yField);
                    if (yProp != null)
                    {
                        try
                        {
                            var yValue = yProp.GetValue(detection);
                            y = (double)yValue;
                            yFound = true;
                        }
                        catch (Exception e)
                        {
                            if (m_enableAllDebugLogging)
                            {
                                Debug.LogWarning(
                                    $"[Transforms] Error getting {yField} property value: {e.Message}"
                                );
                            }
                        }
                    }
                }

                if (xFound && yFound)
                {
                    // Convert coordinates from AprilTag's right-handed to Unity's left-handed coordinate system
                    var unityCorner = ConvertAprilTagToUnityCoordinates(x, y);
                    corners.Add(unityCorner);
                }
            }

            if (corners.Count >= 4)
            {
                // Calculate center point from corners
                var center = Vector2.zero;
                foreach (var corner in corners)
                {
                    center += corner;
                }
                center /= corners.Count;

                return center;
            }
            else
            {
                // Try alternative field names
                foreach (var (xField, yField) in alternativeFields)
                {
                    var xFieldRef = detectionType.GetField(
                        xField,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                    var yFieldRef = detectionType.GetField(
                        yField,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                    if (xFieldRef != null && yFieldRef != null)
                    {
                        try
                        {
                            var xValue = xFieldRef.GetValue(detection);
                            var yValue = yFieldRef.GetValue(detection);

                            // Check if these are arrays
                            if (xValue is Array xArray && yValue is Array yArray)
                            {
                                if (xArray.Length >= 4 && yArray.Length >= 4)
                                {
                                    for (var i = 0; i < 4; i++)
                                    {
                                        var x = Convert.ToDouble(xArray.GetValue(i));
                                        var y = Convert.ToDouble(yArray.GetValue(i));
                                        // Convert coordinates from AprilTag's right-handed to Unity's left-handed coordinate system
                                        var unityCorner = ConvertAprilTagToUnityCoordinates(x, y);
                                        corners.Add(unityCorner);
                                    }

                                    break;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            if (m_enableAllDebugLogging)
                            {
                                Debug.LogWarning(
                                    $"[Transforms] Error with alternative fields {xField}, {yField}: {e.Message}"
                                );
                            }
                        }
                    }
                }

                if (corners.Count >= 4)
                {
                    // Calculate center point from corners
                    var center = Vector2.zero;
                    foreach (var corner in corners)
                    {
                        center += corner;
                    }
                    center /= corners.Count;

                    return center;
                }
            }
        }
        catch (Exception e)
        {
            if (m_enableAllDebugLogging)
            {
                Debug.LogWarning($"[Transforms] Error extracting corner center: {e.Message}");
            }
        }

        return null;
    }

    private Quaternion CalculateTagOrientationFromCorners(
        List<Vector2> corners,
        Vector3 tagWorldPosition
    )
    {
        if (corners.Count < 4)
            return Quaternion.identity;

        // Calculate the tag's orientation from corner coordinates
        // This ensures the cube sits flat on the tag surface

        // Get camera reference for coordinate transformation
        _ = GetCorrectCameraReference();

        // Convert corner coordinates to world space using proper raycasting
        var worldCorners = new List<Vector3>();
        foreach (var corner in corners)
        {
            // Convert 2D corner to 3D world position using raycasting
            var screenPos = new Vector2(corner.x, corner.y);

            // Use the existing GetWorldPositionFromCornerCenter method for consistency
            // Create a temporary TagPose for the raycasting
            var tempTagPose = new TagPose(0, tagWorldPosition, Quaternion.identity);
            var worldPos = GetWorldPositionFromCornerCenter(screenPos, tempTagPose);
            worldCorners.Add(worldPos);
        }

        // Calculate the tag's surface normal from the corners
        // This gives us the direction perpendicular to the tag surface
        if (worldCorners.Count >= 4)
        {
            // Calculate two vectors on the tag surface using the correct corner order
            // AprilTag corners are typically ordered: top-left, top-right, bottom-right, bottom-left
            var v1 = worldCorners[1] - worldCorners[0]; // top-right to top-left
            var v2 = worldCorners[2] - worldCorners[1]; // bottom-right to top-right

            // Check if vectors are valid (not zero length)
            if (v1.magnitude > 0.001f && v2.magnitude > 0.001f)
            {
                v1 = v1.normalized;
                v2 = v2.normalized;

                // Calculate the normal vector (perpendicular to the tag surface)
                var normal = Vector3.Cross(v1, v2);

                // Check if normal is valid (not zero length)
                if (normal.magnitude > 0.001f)
                {
                    normal = normal.normalized;

                    // Create a rotation that aligns the cube with the tag surface
                    // The cube should face the same direction as the tag

                    // Calculate the tag's orientation from the corner vectors
                    // Use the tag's actual edge directions for proper alignment
                    var tagRight = v1; // First edge vector (top edge)
                    var tagUp = Vector3.Cross(normal, tagRight).normalized; // Perpendicular to normal and right

                    // Create a rotation matrix from the tag's coordinate system
                    var tagRotation = Quaternion.LookRotation(normal, tagUp);

                    // Apply a 90-degree rotation around X-axis to align with AprilTag orientation
                    // and a 45-degree counterclockwise rotation around Z-axis to fix alignment
                    // This ensures the cube sits flat on the tag surface
                    var cubeRotation = tagRotation * Quaternion.Euler(0f, 0f, -225f);

                    if (m_enableAllDebugLogging)
                    {
                        Debug.Log(
                            $"[Transforms] Corner-based rotation - Normal: {normal}, Cube Rotation: {cubeRotation.eulerAngles}"
                        );
                    }

                    return cubeRotation;
                }
                else
                {
                    if (m_enableAllDebugLogging)
                    {
                        Debug.LogWarning(
                            $"[Transforms] Invalid normal vector from corners - v1: {v1}, v2: {v2}"
                        );
                    }
                }
            }
            else
            {
                if (m_enableAllDebugLogging)
                {
                    Debug.LogWarning($"[Transforms] Invalid corner vectors - v1: {v1}, v2: {v2}");
                }
            }
        }

        return Quaternion.identity;
    }

    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Corner-based world rotation)
    public Quaternion GetCornerBasedRotation(
        int tagId,
        List<object> rawDetections,
        Vector3 tagWorldPosition
    )
    {
        // Use corner coordinates to calculate proper tag orientation
        // This approach is similar to PhotonVision's method for ensuring tags sit flat

        try
        {
            // Find the detection for this tag
            foreach (var detection in rawDetections)
            {
                var idField = detection
                    .GetType()
                    .GetField(
                        "id",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                if (idField != null)
                {
                    var detectedId = (int)idField.GetValue(detection);
                    if (detectedId == tagId)
                    {
                        // Extract corner coordinates
                        var corners = ExtractCornerCoordinates(detection);
                        if (corners.Count >= 4)
                        {
                            // Calculate tag orientation from corner coordinates
                            return CalculateTagOrientationFromCorners(corners, tagWorldPosition);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (m_enableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[Transforms] Error calculating corner-based rotation: {e.Message}"
                );
            }
        }

        // Fallback to AprilTag rotation if corner-based calculation fails
        return Quaternion.identity;
    }

    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Corner extraction)
    private List<Vector2> ExtractCornerCoordinates(object detection)
    {
        var corners = new List<Vector2>();

        try
        {
            // Try to extract corner coordinates from the detection
            var cornerFields = new[]
            {
                "c0",
                "c1",
                "p00",
                "p01",
                "p10",
                "p11",
                "p20",
                "p21",
                "p30",
                "p31",
            };

            for (var i = 0; i < cornerFields.Length; i += 2)
            {
                if (i + 1 < cornerFields.Length)
                {
                    var xField = detection
                        .GetType()
                        .GetField(
                            cornerFields[i],
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                        );
                    var yField = detection
                        .GetType()
                        .GetField(
                            cornerFields[i + 1],
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                        );

                    if (xField != null && yField != null)
                    {
                        var x = (double)xField.GetValue(detection);
                        var y = (double)yField.GetValue(detection);
                        corners.Add(new Vector2((float)x, (float)y));
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (m_enableAllDebugLogging)
            {
                Debug.LogWarning($"[Transforms] Error extracting corner coordinates: {e.Message}");
            }
        }

        return corners;
    }

    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Called from Update)
    public Vector2? TryGetCornerBasedCenter(int tagId, List<object> rawDetections)
    {
        // Try to find the raw detection data for this specific tag ID and extract corner coordinates
        try
        {
            foreach (var detection in rawDetections)
            {
                var detectionType = detection.GetType();

                // Try to get the ID field/property
                var idProperty =
                    detectionType.GetProperty("ID")
                    ?? detectionType.GetProperty("Id")
                    ?? detectionType.GetProperty("id");
                var idField =
                    detectionType.GetField("ID")
                    ?? detectionType.GetField("Id")
                    ?? detectionType.GetField("id");

                var detectionId = -1;
                if (idProperty != null)
                {
                    detectionId = (int)idProperty.GetValue(detection);
                }
                else if (idField != null)
                {
                    detectionId = (int)idField.GetValue(detection);
                }

                if (detectionId == tagId)
                {
                    // Found the matching detection, try to extract corner coordinates
                    return ExtractCornerCenter(detection);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                $"[Transforms] Error extracting corner center for tag {tagId}: {e.Message}"
            );
        }

        return null;
    }

    // Extract corner coordinates from raw detection data (PhotonVision approach)
    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Corner extraction)
    public Vector2[] ExtractCornersFromRawDetection(int tagId, List<object> rawDetections)
    {
        if (rawDetections == null || rawDetections.Count == 0)
        {
            return null;
        }

        try
        {
            // Look for detection with matching ID
            foreach (var detection in rawDetections)
            {
                if (detection == null)
                    continue;

                var detectionType = detection.GetType();

                // Try to get ID field
                var idField = detectionType.GetField("ID") ?? detectionType.GetField("id");
                if (idField != null)
                {
                    var detectionId = idField.GetValue(detection);
                    if (detectionId != null && detectionId.Equals(tagId))
                    {
                        // Found matching detection, extract corners
                        var cornersField =
                            detectionType.GetField("Corners")
                            ?? detectionType.GetField("corners")
                            ?? detectionType.GetField("Corner")
                            ?? detectionType.GetField("corner");

                        if (cornersField != null)
                        {
                            var cornersValue = cornersField.GetValue(detection);
                            if (cornersValue is Vector2[] corners)
                            {
                                return corners;
                            }
                            else if (cornersValue is Array cornerArray && cornerArray.Length >= 4)
                            {
                                // Convert to Vector2 array
                                var convertedCorners = new Vector2[4];
                                for (var i = 0; i < 4 && i < cornerArray.Length; i++)
                                {
                                    var corner = cornerArray.GetValue(i);
                                    if (corner is Vector2 v2)
                                    {
                                        convertedCorners[i] = v2;
                                    }
                                    else
                                    {
                                        // Try to extract x, y fields
                                        var cornerType = corner.GetType();
                                        var xField =
                                            cornerType.GetField("x") ?? cornerType.GetField("X");
                                        var yField =
                                            cornerType.GetField("y") ?? cornerType.GetField("Y");

                                        if (xField != null && yField != null)
                                        {
                                            var x = Convert.ToSingle(xField.GetValue(corner));
                                            var y = Convert.ToSingle(yField.GetValue(corner));
                                            convertedCorners[i] = new Vector2(x, y);
                                        }
                                    }
                                }
                                return convertedCorners;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (m_enableAllDebugLogging && Time.frameCount % 300 == 0)
            {
                Debug.LogWarning(
                    $"[Transforms] Failed to extract corners for tag {tagId}: {ex.Message}"
                );
            }
        }

        return null;
    }

    private Vector2Int Project3DToScreen(Vector3 worldPos, PassthroughCameraIntrinsics intrinsics)
    {
        // Convert 3D world position to 2D screen coordinates using camera intrinsics
        // This method projects the 3D tag position to 2D screen coordinates with proper distortion handling

        var fx = intrinsics.FocalLength.x;
        var fy = intrinsics.FocalLength.y;
        var cx = intrinsics.PrincipalPoint.x;
        var cy = intrinsics.PrincipalPoint.y;
        var skew = intrinsics.Skew;

        // Ensure we have a valid depth (z should be positive and within detection range)
        var z = Mathf.Clamp(Mathf.Abs(worldPos.z), m_minDetectionDistance, m_maxDetectionDistance);

        // Basic perspective projection
        var x = worldPos.x / z;
        var y = worldPos.y / z;

        // Apply camera intrinsics with skew correction
        var u = fx * x + skew * y + cx;
        var v = fy * y + cy;

        // Clamp to valid screen coordinates
        var screenX = Mathf.Clamp(Mathf.RoundToInt(u), 0, intrinsics.Resolution.x - 1);
        var screenY = Mathf.Clamp(Mathf.RoundToInt(v), 0, intrinsics.Resolution.y - 1);

        return new Vector2Int(screenX, screenY);
    }

    private bool TryGetTagCenterFromCorners(
        TagPose tagPose,
        PassthroughCameraIntrinsics intrinsics,
        out Vector2Int centerPoint
    )
    {
        centerPoint = Vector2Int.zero;

        try
        {
            // Try to access corner properties on the TagPose object
            var tagPoseType = tagPose.GetType();

            // Try different possible corner property names
            var cornerPropertyNames = new[]
            {
                "Corners",
                "CornerPoints",
                "Points",
                "Vertices",
                "CornerCoordinates",
            };

            foreach (var propName in cornerPropertyNames)
            {
                var cornersProperty = tagPoseType.GetProperty(propName);
                if (cornersProperty != null)
                {
                    var corners = cornersProperty.GetValue(tagPose);
                    if (corners != null)
                    {
                        // Try to convert to Vector2 array or similar
                        if (corners is Vector2[] vector2Corners && vector2Corners.Length >= 4)
                        {
                            // Calculate center point from corners
                            var center = Vector2.zero;
                            foreach (var corner in vector2Corners)
                            {
                                center += corner;
                            }
                            center /= vector2Corners.Length;

                            // Convert to screen coordinates
                            centerPoint = new Vector2Int(
                                Mathf.RoundToInt(center.x),
                                Mathf.RoundToInt(center.y)
                            );

                            Debug.Log(
                                $"[Transforms] Found {propName} with {vector2Corners.Length} corners, center: {centerPoint}"
                            );
                            return true;
                        }
                        else if (
                            corners is Vector2Int[] vector2IntCorners
                            && vector2IntCorners.Length >= 4
                        )
                        {
                            // Calculate center point from corners
                            var center = Vector2.zero;
                            foreach (var corner in vector2IntCorners)
                            {
                                center += new Vector2(corner.x, corner.y);
                            }
                            center /= vector2IntCorners.Length;

                            centerPoint = new Vector2Int(
                                Mathf.RoundToInt(center.x),
                                Mathf.RoundToInt(center.y)
                            );

                            Debug.Log(
                                $"[Transforms] Found {propName} with {vector2IntCorners.Length} corners, center: {centerPoint}"
                            );
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Transforms] Error accessing corner coordinates: {ex.Message}");
        }

        return false;
    }

    public Vector3? GetWorldPositionUsingPassthroughRaycasting(TagPose tagPose)
    {
        try
        {
            // Get the camera eye from the WebCam manager
            var eye = GetWebCamManagerEye();

            // Get camera intrinsics for proper coordinate conversion
            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
            var camRes = intrinsics.Resolution;

            // Try to use corner coordinates if available (more accurate)
            if (TryGetTagCenterFromCorners(tagPose, intrinsics, out var screenPoint))
            {
                // Use corner-based center point
                Debug.Log($"[Transforms] Using corner-based center point: {screenPoint}");
            }
            else
            {
                // Fallback: Convert the 3D tag position to 2D screen coordinates
                // The tag position is in camera space, so we need to project it to screen space
                var scaledPosition = tagPose.Position * m_positionScaleFactor;
                screenPoint = Project3DToScreen(scaledPosition, intrinsics);
            }

            // Convert 2D screen coordinates to 3D ray using passthrough camera utils
            var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(eye, screenPoint);

            // Use environment raycasting to find the actual 3D world position
            if (
                m_environmentRaycastManager != null
                && m_environmentRaycastManager.Raycast(ray, out var hitInfo)
            )
            {
                // Raycast hit - cache the distance for consistent fallback
                var raycastDistance = Vector3.Distance(ray.origin, hitInfo.point);
                m_lastRaycastDistance[tagPose.ID] = raycastDistance;

                return hitInfo.point;
            }
            else
            {
                // Raycast missed - use last known distance if available
                if (m_lastRaycastDistance.TryGetValue(tagPose.ID, out var lastDistance))
                {
                    if (m_enableAllDebugLogging)
                    {
                        Debug.Log(
                            $"[Transforms] Tag {tagPose.ID} passthrough raycast MISS, using last distance: {lastDistance:F3}m"
                        );
                    }

                    return ray.origin + ray.direction * lastDistance;
                }

                // No history - use tag's reported distance as initial estimate
                var rawDistance = tagPose.Position.magnitude;
                var clampedDistance = Mathf.Clamp(
                    rawDistance,
                    m_minDetectionDistance,
                    m_maxDetectionDistance
                );

                // Apply distance-based scaling if enabled
                if (m_enableDistanceScaling)
                {
                    clampedDistance = ApplyDistanceScaling(clampedDistance);
                }

                // Cache this initial distance
                m_lastRaycastDistance[tagPose.ID] = clampedDistance;

                return ray.origin + ray.direction * clampedDistance;
            }
        }
        catch (Exception ex)
        {
            if (m_enableAllDebugLogging)
                Debug.LogWarning($"[Transforms] Passthrough raycasting failed: {ex.Message}");
            return null;
        }
    }

    public static float ApplyDistanceScaling(float distance)
    {
        // Apply non-linear scaling to improve accuracy across the wide distance range
        // This helps with both very close (0.5m) and very far (18m) tags

        if (distance <= 1.0f)
        {
            // For close tags (0.5m - 1m), use slight compression to prevent overshooting
            return distance * 0.9f;
        }
        else if (distance <= 5.0f)
        {
            // For medium distance tags (1m - 5m), use linear scaling
            return distance;
        }
        else if (distance <= 10.0f)
        {
            // For far tags (5m - 10m), use slight expansion
            return distance * 1.1f;
        }
        else
        {
            // For very far tags (10m - 18m), use more expansion
            return distance * 1.2f;
        }
    }

    /// <summary>
    /// Calculate world position for a tag (fallback method)
    /// </summary>
    /// USAGE: REFERENCED (alternate path). Verify runtime use before pruning.
    public Vector3 CalculateWorldPosition(TagPose tag)
    {
        // Use the existing world position calculation logic
        var camRef = GetCorrectCameraReference();
        if (camRef != null)
        {
            // Convert AprilTag position to world space
            var adjustedPosition = camRef.rotation * tag.Position;
            return camRef.position + adjustedPosition + m_positionOffset;
        }

        // Fallback to tag position if no camera reference
        return tag.Position + m_positionOffset;
    }

    /// <summary>
    /// Calculate world rotation for a tag (fallback method)
    /// </summary>
    /// USAGE: REFERENCED (alternate path). Verify runtime use before pruning.
    public Quaternion CalculateWorldRotation(TagPose tag)
    {
        // Use the existing world rotation calculation logic
        var camRef = GetCorrectCameraReference();
        if (camRef != null)
        {
            // Convert AprilTag rotation to world space
            var adjustedRotation = camRef.rotation * tag.Rotation;
            return adjustedRotation * Quaternion.Euler(m_rotationOffset);
        }

        // Fallback to tag rotation if no camera reference
        return tag.Rotation * Quaternion.Euler(m_rotationOffset);
    }
}
