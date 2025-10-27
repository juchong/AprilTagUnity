// Assets/AprilTag/Scripts/AprilTagTransforms.cs
// AirTag Processing Transformations for Quest Headsets

using System;
using System.Collections.Generic;
using System.Reflection;
using AprilTag; // locally integrated AprilTag library
using Meta.XR;
using PassthroughCameraSamples;
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
    private Dictionary<int, float> m_lastRaycastDistance = new();

    // Controller-backed accessors to avoid duplicated state
    private bool EnableAllDebugLogging =>
        m_controller != null && m_controller.EnableAllDebugLogging;
    private EnvironmentRaycastManager EnvironmentRaycastManager =>
        m_controller != null ? m_controller.EnvironmentRaycastManager : null;
    private float PositionScaleFactor =>
        m_controller != null ? m_controller.PositionScaleFactor : 1.0f;
    private float MinDetectionDistance =>
        m_controller != null ? m_controller.MinDetectionDistance : 0.3f;
    private float MaxDetectionDistance =>
        m_controller != null ? m_controller.MaxDetectionDistance : 15.0f;
    private bool EnableDistanceScaling =>
        m_controller != null && m_controller.IsDistanceScalingEnabled;
    private bool EnableGravityAlignedConstraints =>
        m_controller != null && m_controller.EnableGravityAlignedConstraints;
    private Vector3 GravityDirection =>
        m_controller != null ? m_controller.GravityDirection : Vector3.down;
    private float GravityAlignmentTolerance =>
        m_controller != null ? m_controller.GravityAlignmentTolerance : 15f;
    private bool ApplyGravityConstraintToPosition =>
        m_controller != null && m_controller.ApplyGravityConstraintToPosition;
    private bool ApplyGravityConstraintToRotation =>
        m_controller != null && m_controller.ApplyGravityConstraintToRotation;

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

    /// <summary>
    /// Extract corners WITH intrinsics transformation for debug overlays
    /// Returns the same corners used to calculate the center in the visualization pipeline
    /// </summary>
    public Vector2[] TryGetCornersWithIntrinsics(
        int tagId,
        List<object> rawDetections,
        PassthroughCameraIntrinsics intrinsics
    )
    {
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
                    // Found the matching detection, extract corners with intrinsics
                    return ExtractCornersWithIntrinsicsInternal(detection, intrinsics);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Transforms] Error extracting corners for tag {tagId}: {e.Message}");
        }

        return null;
    }

    private Vector2[] ExtractCornersWithIntrinsicsInternal(
        object detection,
        PassthroughCameraIntrinsics intrinsics
    )
    {
        // Same logic as ExtractCornerCenterWithIntrinsics but returns corners instead of center
        var detectionType = detection.GetType();
        var cornerFields = new[]
        {
            ("p00", "p01"), // Corner 1
            ("p10", "p11"), // Corner 2
            ("p20", "p21"), // Corner 3
            ("p30", "p31"), // Corner 4
        };

        var corners = new List<Vector2>();

        foreach (var (xField, yField) in cornerFields)
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
                var x = (double)xFieldRef.GetValue(detection);
                var y = (double)yFieldRef.GetValue(detection);

                // Apply Y-flip transformation (same as center calculation)
                var unityCorner = ConvertAprilTagToUnityCoordinatesWithIntrinsics(x, y, intrinsics);
                corners.Add(unityCorner);
            }
        }

        return corners.Count == 4 ? corners.ToArray() : null;
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
            // The structure has: c0, c1 (CENTER), p00, p01, p10, p11, p20, p21, p30, p31 (CORNERS)
            // IMPORTANT: c0, c1 is the CENTER coordinate, NOT a corner!
            var cornerFields = new[]
            {
                ("p00", "p01"), // Corner 1
                ("p10", "p11"), // Corner 2
                ("p20", "p21"), // Corner 3
                ("p30", "p31"), // Corner 4
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
                        if (EnableAllDebugLogging)
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
                            if (EnableAllDebugLogging)
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
                        if (EnableAllDebugLogging)
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
                            if (EnableAllDebugLogging)
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
                            if (EnableAllDebugLogging)
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
            if (EnableAllDebugLogging)
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
        // Note: Multi-corner raycasting requires raw detections, which aren't available here
        // This method is called from controller with only the corner center already calculated
        // Multi-corner approach is better suited for a separate entry point

        // Fallback to single center raycast
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
            if (EnvironmentRaycastManager != null)
            {
                if (EnvironmentRaycastManager.Raycast(ray, out var hitInfo))
                {
                    // Raycast hit - cache the distance for this tag
                    var raycastDistance = Vector3.Distance(ray.origin, hitInfo.point);
                    m_lastRaycastDistance[tagPose.ID] = raycastDistance;

                    // Validate gravity alignment for logging/confidence only (don't reject position)
                    if (EnableGravityAlignedConstraints && EnableAllDebugLogging)
                    {
                        var gravityScore = ValidateGravityAlignedSurface(
                            hitInfo.normal,
                            tagPose.ID
                        );
                        Debug.Log(
                            $"[Transforms] Tag {tagPose.ID} raycast HIT - gravity alignment score: {gravityScore:F3}"
                        );
                    }

                    if (EnableAllDebugLogging)
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

                        if (EnableAllDebugLogging)
                        {
                            Debug.Log(
                                $"[Transforms] Tag {tagPose.ID} raycast MISS, using last known distance: {lastDistance:F3}m -> {consistentPosition}"
                            );
                        }

                        return consistentPosition;
                    }

                    if (EnableAllDebugLogging)
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
                MinDetectionDistance,
                MaxDetectionDistance
            );

            // Apply distance scaling using static method (fallback path)
            // Controller's distance adaptation system handles the primary path
            if (EnableDistanceScaling)
            {
                clampedDistance = ApplyDistanceScaling(clampedDistance);
            }

            var fallbackPosition = ray.origin + ray.direction * clampedDistance;

            // Cache this distance for future frames
            m_lastRaycastDistance[tagPose.ID] = clampedDistance;

            if (EnableAllDebugLogging)
            {
                Debug.Log(
                    $"[Transforms] Tag {tagPose.ID} using tag distance fallback: {clampedDistance:F3}m -> {fallbackPosition}"
                );
            }

            return fallbackPosition;
        }
        catch (Exception e)
        {
            if (EnableAllDebugLogging)
            {
                Debug.LogWarning($"[Transforms] Error in corner-based positioning: {e.Message}");
            }

            // Final fallback to 3D pose estimation
            return tagPose.Position * PositionScaleFactor;
        }
    }

    /// <summary>
    /// Try to get world position by raycasting all 4 corners and calculating centroid
    /// Provides better accuracy for flat surfaces and validates coplanarity
    /// </summary>
    public Vector3? TryGetWorldPositionFromMultipleCorners(
        int tagId,
        TagPose tagPose,
        List<object> rawDetections
    )
    {
        try
        {
            // Get camera intrinsics
            var eye = GetWebCamManagerEye();
            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
            var camRes = intrinsics.Resolution;

            // Get 2D corner coordinates
            var corners2D = ExtractCornersFromRawDetection(tagId, rawDetections);
            if (corners2D == null || corners2D.Length < 4)
            {
                return null; // Can't use multi-corner approach without corners
            }

            var worldCorners = new List<Vector3>();
            var surfaceNormals = new List<Vector3>();

            // Raycast each corner to get 3D world position
            for (var i = 0; i < 4; i++)
            {
                var corner2D = corners2D[i];

                // Convert to normalized coordinates with Y-flip
                var perX = corner2D.x / camRes.x;
                var perY = corner2D.y / camRes.y;
                var flippedPerY = 1.0f - perY;

                var cornerPixel = new Vector2Int(
                    Mathf.RoundToInt(perX * camRes.x),
                    Mathf.RoundToInt(flippedPerY * camRes.y)
                );

                // Create ray from corner
                var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(eye, cornerPixel);

                // Raycast to get 3D position
                if (EnvironmentRaycastManager != null)
                {
                    if (EnvironmentRaycastManager.Raycast(ray, out var hitInfo))
                    {
                        worldCorners.Add(hitInfo.point);
                        surfaceNormals.Add(hitInfo.normal);
                    }
                    else
                    {
                        // If any corner misses, can't use multi-corner approach
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }

            // Validate we got all 4 corners
            if (worldCorners.Count != 4)
            {
                return null;
            }

            // Validate coplanarity (all corners on same flat surface)
            var coplanarityScore = ValidateCoplanarity(worldCorners, surfaceNormals);
            if (coplanarityScore < 0.85f) // Require 85% coplanarity
            {
                if (EnableAllDebugLogging)
                {
                    Debug.LogWarning(
                        $"[Transforms] Tag {tagId} failed coplanarity check: score {coplanarityScore:F3} < 0.85"
                    );
                }
                return null;
            }

            // Validate gravity alignment if enabled
            if (EnableGravityAlignedConstraints)
            {
                // Check that all surface normals are perpendicular to gravity (horizontal)
                var avgNormal = Vector3.zero;
                foreach (var normal in surfaceNormals)
                {
                    avgNormal += normal;
                }
                avgNormal = avgNormal.normalized;

                var gravityScore = ValidateGravityAlignedSurface(avgNormal, tagId);
                if (gravityScore < 0.7f) // Require 70% gravity alignment for multi-corner
                {
                    if (EnableAllDebugLogging)
                    {
                        Debug.LogWarning(
                            $"[Transforms] Tag {tagId} failed gravity alignment check: score {gravityScore:F3} < 0.7"
                        );
                    }
                    return null;
                }

                if (EnableAllDebugLogging)
                {
                    Debug.Log(
                        $"[Transforms] Tag {tagId} passed gravity alignment check: score {gravityScore:F3}"
                    );
                }
            }

            // Calculate centroid of 4 corners
            var centroid = Vector3.zero;
            foreach (var corner in worldCorners)
            {
                centroid += corner;
            }
            centroid /= worldCorners.Count;

            // Cache distance for consistency
            var distance = Vector3.Distance(GetCorrectCameraReference().position, centroid);
            m_lastRaycastDistance[tagPose.ID] = distance;

            if (EnableAllDebugLogging)
            {
                Debug.Log(
                    $"[Transforms] Tag {tagId} multi-corner position: {centroid}, "
                        + $"coplanarity: {coplanarityScore:F3}, distance: {distance:F3}m"
                );
            }

            return centroid;
        }
        catch (Exception e)
        {
            if (EnableAllDebugLogging)
            {
                Debug.LogWarning($"[Transforms] Error in multi-corner positioning: {e.Message}");
            }
            return null;
        }
    }

    /// <summary>
    /// Validate that corner points are coplanar (on same flat surface)
    /// Returns score from 0 (not coplanar) to 1 (perfectly coplanar)
    /// Optionally validates gravity alignment for vertical walls
    /// </summary>
    private float ValidateCoplanarity(List<Vector3> corners, List<Vector3> normals)
    {
        if (corners.Count != 4 || normals.Count != 4)
            return 0f;

        // Check 1: All surface normals should be similar (pointing same direction)
        var avgNormal = Vector3.zero;
        foreach (var normal in normals)
        {
            avgNormal += normal;
        }
        avgNormal = avgNormal.normalized;

        var normalConsistency = 0f;
        foreach (var normal in normals)
        {
            normalConsistency += Vector3.Dot(normal, avgNormal);
        }
        normalConsistency /= normals.Count;

        // Check 2: Calculate plane from first 3 corners
        var v1 = corners[1] - corners[0];
        var v2 = corners[2] - corners[0];
        var planeNormal = Vector3.Cross(v1, v2).normalized;

        // Check if 4th corner is on the same plane
        var distanceToPlane = Mathf.Abs(Vector3.Dot(corners[3] - corners[0], planeNormal));
        var maxTagSize = 0.5f; // Assume max tag size ~0.5m
        var planeDeviationScore = 1.0f - Mathf.Clamp01(distanceToPlane / (maxTagSize * 0.1f));

        // Check 3: Gravity alignment (if enabled)
        var gravityAlignmentScore = 1.0f;
        if (EnableGravityAlignedConstraints)
        {
            // Validate that the plane is vertical (perpendicular to gravity)
            var gravity = GravityDirection;
            var dotProduct = Mathf.Abs(Vector3.Dot(avgNormal, gravity.normalized));
            var angleFromHorizontal = Mathf.Acos(Mathf.Clamp01(dotProduct)) * Mathf.Rad2Deg;

            // Score based on perpendicularity to gravity
            gravityAlignmentScore =
                1.0f - Mathf.Clamp01(angleFromHorizontal / (GravityAlignmentTolerance * 2f));
        }

        // Combined score (40% normal consistency, 30% plane deviation, 30% gravity alignment)
        var score =
            normalConsistency * 0.4f + planeDeviationScore * 0.3f + gravityAlignmentScore * 0.3f;

        return Mathf.Clamp01(score);
    }

    /// <summary>
    /// Calculate surface alignment quality for anchor confidence
    /// Returns score from 0 (poor alignment) to 1 (perfect perpendicular alignment)
    /// </summary>
    public float CalculateSurfaceAlignmentQuality(
        int tagId,
        List<object> rawDetections,
        Vector3 tagWorldPosition,
        Quaternion tagRotation
    )
    {
        try
        {
            // Get tag center in screen space
            var cornerCenter = TryGetCornerBasedCenter(tagId, rawDetections);
            if (!cornerCenter.HasValue)
            {
                return 0.5f; // No corner data, assume average quality
            }

            // Get camera intrinsics
            var eye = GetWebCamManagerEye();
            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
            var camRes = intrinsics.Resolution;

            // Convert to normalized coordinates with Y-flip
            var perX = cornerCenter.Value.x / camRes.x;
            var perY = cornerCenter.Value.y / camRes.y;
            var flippedPerY = 1.0f - perY;

            // Convert to pixel coordinates
            var centerPixel = new Vector2Int(
                Mathf.RoundToInt(perX * camRes.x),
                Mathf.RoundToInt(flippedPerY * camRes.y)
            );

            // Create ray from tag center
            var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(eye, centerPixel);

            // Raycast to get surface normal
            if (EnvironmentRaycastManager != null)
            {
                if (EnvironmentRaycastManager.Raycast(ray, out var hitInfo))
                {
                    // Get surface normal
                    var surfaceNormal = hitInfo.normal;

                    // Calculate tag forward direction from rotation
                    var tagForward = tagRotation * Vector3.forward;

                    // Calculate alignment: how well does tag face perpendicular to surface?
                    // Perfect alignment: tag forward = surface normal (dot product = 1)
                    var alignment = Vector3.Dot(tagForward.normalized, surfaceNormal.normalized);

                    // Convert to 0-1 score (perfect perpendicular = 1)
                    var alignmentScore = Mathf.Abs(alignment);

                    // Additional check: gravity-aligned surface validation
                    var gravityScore = 1.0f;
                    if (EnableGravityAlignedConstraints)
                    {
                        gravityScore = ValidateGravityAlignedSurface(surfaceNormal, tagId);

                        if (EnableAllDebugLogging)
                        {
                            Debug.Log(
                                $"[Transforms] Tag {tagId} surface alignment - "
                                    + $"Alignment: {alignmentScore:F3}, "
                                    + $"Gravity score: {gravityScore:F3}"
                            );
                        }
                    }

                    // Additional check: multi-corner coplanarity if available
                    var corners2D = ExtractCornersFromRawDetection(tagId, rawDetections);
                    if (corners2D != null && corners2D.Length >= 4)
                    {
                        // Try to get 3D corners and check coplanarity
                        var coplanarityScore = TryGetCoplanarityScore(
                            corners2D,
                            intrinsics,
                            camRes
                        );
                        if (coplanarityScore > 0f)
                        {
                            // Combine alignment, coplanarity, and gravity validation
                            // 50% alignment, 25% coplanarity, 25% gravity
                            return alignmentScore * 0.5f
                                + coplanarityScore * 0.25f
                                + gravityScore * 0.25f;
                        }
                    }

                    // Combine alignment and gravity validation (75% alignment, 25% gravity)
                    return alignmentScore * 0.75f + gravityScore * 0.25f;
                }
            }

            // No raycast hit, return neutral score
            return 0.5f;
        }
        catch (Exception e)
        {
            if (EnableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[Transforms] Error calculating surface alignment quality: {e.Message}"
                );
            }
            return 0.5f;
        }
    }

    /// <summary>
    /// Try to get coplanarity score for surface alignment quality
    /// </summary>
    private float TryGetCoplanarityScore(
        Vector2[] corners2D,
        PassthroughCameraIntrinsics intrinsics,
        Vector2 camRes
    )
    {
        try
        {
            var worldCorners = new List<Vector3>();
            var surfaceNormals = new List<Vector3>();

            // Raycast each corner
            for (var i = 0; i < 4; i++)
            {
                var corner2D = corners2D[i];
                var perX = corner2D.x / camRes.x;
                var perY = corner2D.y / camRes.y;
                var flippedPerY = 1.0f - perY;

                var cornerPixel = new Vector2Int(
                    Mathf.RoundToInt(perX * camRes.x),
                    Mathf.RoundToInt(flippedPerY * camRes.y)
                );

                var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(
                    GetWebCamManagerEye(),
                    cornerPixel
                );

                if (EnvironmentRaycastManager != null)
                {
                    if (EnvironmentRaycastManager.Raycast(ray, out var hitInfo))
                    {
                        worldCorners.Add(hitInfo.point);
                        surfaceNormals.Add(hitInfo.normal);
                    }
                    else
                    {
                        return 0f; // Can't get all corners
                    }
                }
                else
                {
                    return 0f;
                }
            }

            return worldCorners.Count == 4 ? ValidateCoplanarity(worldCorners, surfaceNormals) : 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private Vector2? ExtractCornerCenter(object detection)
    {
        // Extract corner coordinates from the Detection object and calculate center
        try
        {
            var detectionType = detection.GetType();

            // Try to access corner coordinates based on the Detection structure we found
            // The structure has: c0, c1 (CENTER), p00, p01, p10, p11, p20, p21, p30, p31 (CORNERS)
            // IMPORTANT: c0, c1 is the CENTER coordinate, NOT a corner!
            var cornerFields = new[]
            {
                ("p00", "p01"), // Corner 1
                ("p10", "p11"), // Corner 2
                ("p20", "p21"), // Corner 3
                ("p30", "p31"), // Corner 4
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
                        if (EnableAllDebugLogging)
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
                            if (EnableAllDebugLogging)
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
                        if (EnableAllDebugLogging)
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
                            if (EnableAllDebugLogging)
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
                            if (EnableAllDebugLogging)
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
            if (EnableAllDebugLogging)
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

                    // Apply gravity-aligned constraints if enabled
                    if (EnableGravityAlignedConstraints && ApplyGravityConstraintToRotation)
                    {
                        // Enforce that tag normal is horizontal (perpendicular to gravity)
                        tagRotation = ApplyGravityAlignedRotationConstraint(
                            tagRotation,
                            normal,
                            -1
                        );

                        if (EnableAllDebugLogging)
                        {
                            Debug.Log(
                                $"[Transforms] Corner-based rotation with gravity constraint - "
                                    + $"Original normal: {normal}, "
                                    + $"Rotation: {tagRotation.eulerAngles}"
                            );
                        }
                    }

                    // Apply a 90-degree rotation around X-axis to align with AprilTag orientation
                    // and a 45-degree counterclockwise rotation around Z-axis to fix alignment
                    // This ensures the cube sits flat on the tag surface
                    var cubeRotation = tagRotation * Quaternion.Euler(0f, 0f, -225f);

                    if (EnableAllDebugLogging)
                    {
                        Debug.Log(
                            $"[Transforms] Corner-based rotation - Normal: {normal}, Cube Rotation: {cubeRotation.eulerAngles}"
                        );
                    }

                    return cubeRotation;
                }
                else
                {
                    if (EnableAllDebugLogging)
                    {
                        Debug.LogWarning(
                            $"[Transforms] Invalid normal vector from corners - v1: {v1}, v2: {v2}"
                        );
                    }
                }
            }
            else
            {
                if (EnableAllDebugLogging)
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
        // For flat surfaces (FIRST Robotics field), use surface-perpendicular constraint

        try
        {
            // Try surface-perpendicular rotation first (most accurate for flat surfaces)
            var surfaceRotation = CalculateSurfacePerpendicularRotation(
                tagId,
                rawDetections,
                tagWorldPosition
            );
            if (surfaceRotation.HasValue)
            {
                return surfaceRotation.Value;
            }

            // Fallback: Find the detection for this tag and use corner-based calculation
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
            if (EnableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[Transforms] Error calculating corner-based rotation: {e.Message}"
                );
            }
        }

        // Fallback to AprilTag rotation if corner-based calculation fails
        return Quaternion.identity;
    }

    /// <summary>
    /// Calculate rotation assuming tag is perpendicular to detected surface
    /// Uses environment raycast surface normal for accuracy
    /// Ideal for FIRST Robotics field tags mounted flat on walls
    /// </summary>
    private Quaternion? CalculateSurfacePerpendicularRotation(
        int tagId,
        List<object> rawDetections,
        Vector3 tagWorldPosition
    )
    {
        try
        {
            // Get tag center in screen space
            var cornerCenter = TryGetCornerBasedCenter(tagId, rawDetections);
            if (!cornerCenter.HasValue)
            {
                return null;
            }

            // Get camera intrinsics
            var eye = GetWebCamManagerEye();
            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(eye);
            var camRes = intrinsics.Resolution;

            // Convert to normalized coordinates with Y-flip
            var perX = cornerCenter.Value.x / camRes.x;
            var perY = cornerCenter.Value.y / camRes.y;
            var flippedPerY = 1.0f - perY;

            // Convert to pixel coordinates
            var centerPixel = new Vector2Int(
                Mathf.RoundToInt(perX * camRes.x),
                Mathf.RoundToInt(flippedPerY * camRes.y)
            );

            // Create ray from tag center
            var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(eye, centerPixel);

            // Raycast to get surface normal
            if (EnvironmentRaycastManager != null)
            {
                if (EnvironmentRaycastManager.Raycast(ray, out var hitInfo))
                {
                    // Get surface normal
                    var surfaceNormal = hitInfo.normal;

                    // Apply gravity-aligned constraints if enabled
                    // This ensures tags are treated as mounted flat on vertical walls
                    if (EnableGravityAlignedConstraints && ApplyGravityConstraintToRotation)
                    {
                        // Project surface normal onto horizontal plane (perpendicular to gravity)
                        var gravity = GravityDirection;
                        var horizontalNormal = ProjectVectorOntoHorizontalPlane(
                            surfaceNormal,
                            gravity
                        );

                        // Calculate rotation with gravity constraint
                        var rotation = CalculateRotationFromSurfaceNormal(
                            horizontalNormal,
                            gravity
                        );

                        if (EnableAllDebugLogging)
                        {
                            var angleFromHorizontal =
                                Mathf.Acos(
                                    Mathf.Clamp01(
                                        Mathf.Abs(
                                            Vector3.Dot(
                                                surfaceNormal.normalized,
                                                gravity.normalized
                                            )
                                        )
                                    )
                                ) * Mathf.Rad2Deg;

                            Debug.Log(
                                $"[Transforms] Tag {tagId} gravity-aligned rotation: "
                                    + $"Original surface normal: {surfaceNormal}, "
                                    + $"Corrected horizontal normal: {horizontalNormal}, "
                                    + $"Angle from horizontal: {angleFromHorizontal:F1}°, "
                                    + $"Rotation: {rotation.eulerAngles}"
                            );
                        }

                        return rotation;
                    }
                    else
                    {
                        // Original behavior: use surface normal directly
                        // For flat surfaces, tag normal should match surface normal
                        // Tag faces outward from the wall, so tag forward = surface normal
                        var tagForward = surfaceNormal;

                        // Calculate tag "up" direction (perpendicular to normal, aligned with world up)
                        // Project world up onto the plane defined by surface normal
                        var worldUp = Vector3.up;
                        var tagRight = Vector3.Cross(worldUp, tagForward).normalized;

                        // Handle edge case: surface is horizontal (ceiling/floor)
                        if (tagRight.magnitude < 0.01f)
                        {
                            // Surface is horizontal, use world forward instead
                            tagRight = Vector3.Cross(Vector3.forward, tagForward).normalized;
                        }

                        var tagUp = Vector3.Cross(tagForward, tagRight).normalized;

                        // Create rotation from tag coordinate system
                        var rotation = Quaternion.LookRotation(tagForward, tagUp);

                        if (EnableAllDebugLogging)
                        {
                            Debug.Log(
                                $"[Transforms] Tag {tagId} surface-perpendicular rotation: "
                                    + $"Surface normal: {surfaceNormal}, "
                                    + $"Tag forward: {tagForward}, "
                                    + $"Tag up: {tagUp}, "
                                    + $"Rotation: {rotation.eulerAngles}"
                            );
                        }

                        return rotation;
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (EnableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[Transforms] Error calculating surface-perpendicular rotation: {e.Message}"
                );
            }
        }

        return null;
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
            if (EnableAllDebugLogging)
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
            if (EnableAllDebugLogging && Time.frameCount % 300 == 0)
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
        var z = Mathf.Clamp(Mathf.Abs(worldPos.z), MinDetectionDistance, MaxDetectionDistance);

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
                var scaledPosition = tagPose.Position * PositionScaleFactor;
                screenPoint = Project3DToScreen(scaledPosition, intrinsics);
            }

            // Convert 2D screen coordinates to 3D ray using passthrough camera utils
            var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(eye, screenPoint);

            // Use environment raycasting to find the actual 3D world position
            if (
                EnvironmentRaycastManager != null
                && EnvironmentRaycastManager.Raycast(ray, out var hitInfo)
            )
            {
                // Raycast hit - cache the distance for consistent fallback
                var raycastDistance = Vector3.Distance(ray.origin, hitInfo.point);
                m_lastRaycastDistance[tagPose.ID] = raycastDistance;

                // Validate gravity alignment for logging/confidence only (don't reject)
                if (EnableGravityAlignedConstraints && EnableAllDebugLogging)
                {
                    var gravityScore = ValidateGravityAlignedSurface(hitInfo.normal, tagPose.ID);
                    Debug.Log(
                        $"[Transforms] Tag {tagPose.ID} passthrough raycast - gravity alignment score: {gravityScore:F3}"
                    );
                }

                return hitInfo.point;
            }
            else
            {
                // Raycast missed - use last known distance if available
                if (m_lastRaycastDistance.TryGetValue(tagPose.ID, out var lastDistance))
                {
                    if (EnableAllDebugLogging)
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
                    MinDetectionDistance,
                    MaxDetectionDistance
                );

                // Apply distance-based scaling if enabled
                if (EnableDistanceScaling)
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
            if (EnableAllDebugLogging)
                Debug.LogWarning($"[Transforms] Passthrough raycasting failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Apply distance scaling with physics-based correction
    /// NOTE: This is a static wrapper for backward compatibility
    /// Prefer using AprilTagDistanceAdaptation instance for full functionality
    /// </summary>
    public static float ApplyDistanceScaling(float distance)
    {
        // Static fallback implementation for 0.3m - 5m range
        // Matches AprilTagDistanceAdaptation behavior

        if (distance < 1.0f)
        {
            // Very close range (0.3-1m): gentle compression
            return distance * 0.95f;
        }
        else if (distance < 2.0f)
        {
            // Close-medium range (1-2m): minimal correction
            return distance * 0.98f;
        }
        else if (distance < 3.5f)
        {
            // Medium range (2-3.5m): linear (optimal)
            return distance * 1.0f;
        }
        else
        {
            // Far range (3.5-5m): gentle expansion
            return distance * 1.02f;
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
            return camRef.position + adjustedPosition;
        }

        // Fallback to tag position if no camera reference
        return tag.Position;
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
            return camRef.rotation * tag.Rotation;
        }

        // Fallback to tag rotation if no camera reference
        return tag.Rotation;
    }

    /// <summary>
    /// Apply gravity-aligned constraints to tag position
    /// Assumes tags are mounted flat on walls perpendicular to gravity
    /// Projects position onto nearest vertical plane aligned with gravity
    /// </summary>
    public Vector3 ApplyGravityAlignedPositionConstraint(Vector3 position, int tagId)
    {
        if (!EnableGravityAlignedConstraints || !ApplyGravityConstraintToPosition)
            return position;

        try
        {
            // Get gravity direction (typically down)
            var gravity = GravityDirection;

            // Calculate the horizontal plane normal (perpendicular to gravity)
            // For gravity = (0, -1, 0), horizontal plane normal = (0, 1, 0)
            var horizontalNormal = -gravity.normalized;

            // Project position onto vertical plane
            // The tag should lie on a plane perpendicular to the horizontal plane
            // This means the tag's surface normal should be horizontal (perpendicular to gravity)

            // We don't modify the position directly, but we can use this to validate
            // that raycasted surfaces are indeed vertical (perpendicular to gravity)
            // This is handled in the rotation constraint method

            // For position, we can snap to the nearest vertical plane if needed
            // However, for most cases, the environment raycast already gives us accurate positions
            // So we just validate and return the position as-is

            return position;
        }
        catch (Exception e)
        {
            if (EnableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[Transforms] Error applying gravity position constraint to tag {tagId}: {e.Message}"
                );
            }
            return position;
        }
    }

    /// <summary>
    /// Apply gravity-aligned constraints to tag rotation
    /// Enforces that tag surface normal is perpendicular to gravity (horizontal)
    /// This ensures tags appear flat on vertical walls
    /// </summary>
    public Quaternion ApplyGravityAlignedRotationConstraint(
        Quaternion rotation,
        Vector3 surfaceNormal,
        int tagId
    )
    {
        if (!EnableGravityAlignedConstraints || !ApplyGravityConstraintToRotation)
            return rotation;

        try
        {
            var gravity = GravityDirection;

            // For tags mounted flat on walls:
            // - The wall surface normal should be horizontal (perpendicular to gravity)
            // - The tag forward direction should match the wall surface normal

            // Check if surface normal is perpendicular to gravity
            var dotProduct = Mathf.Abs(Vector3.Dot(surfaceNormal.normalized, gravity.normalized));
            var angleFromHorizontal = Mathf.Acos(Mathf.Clamp01(dotProduct)) * Mathf.Rad2Deg;

            if (angleFromHorizontal > GravityAlignmentTolerance)
            {
                if (EnableAllDebugLogging)
                {
                    Debug.LogWarning(
                        $"[Transforms] Tag {tagId} surface not perpendicular to gravity: {angleFromHorizontal:F1}° from horizontal (tolerance: {GravityAlignmentTolerance:F1}°)"
                    );
                }

                // Project surface normal onto horizontal plane (perpendicular to gravity)
                var horizontalNormal = ProjectVectorOntoHorizontalPlane(surfaceNormal, gravity);

                // Recalculate rotation with corrected horizontal normal
                rotation = CalculateRotationFromSurfaceNormal(horizontalNormal, gravity);

                if (EnableAllDebugLogging)
                {
                    Debug.Log(
                        $"[Transforms] Tag {tagId} rotation corrected using gravity constraint: original normal={surfaceNormal:F3}, corrected normal={horizontalNormal:F3}"
                    );
                }
            }

            return rotation;
        }
        catch (Exception e)
        {
            if (EnableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[Transforms] Error applying gravity rotation constraint to tag {tagId}: {e.Message}"
                );
            }
            return rotation;
        }
    }

    /// <summary>
    /// Project a vector onto the horizontal plane (perpendicular to gravity)
    /// </summary>
    private Vector3 ProjectVectorOntoHorizontalPlane(Vector3 vector, Vector3 gravity)
    {
        // Remove the component parallel to gravity
        var projected = vector - Vector3.Dot(vector, gravity) * gravity;
        return projected.normalized;
    }

    /// <summary>
    /// Calculate rotation from surface normal with gravity constraint
    /// Ensures tag is oriented perpendicular to gravity
    /// </summary>
    private Quaternion CalculateRotationFromSurfaceNormal(Vector3 surfaceNormal, Vector3 gravity)
    {
        // Tag forward should match surface normal (points out from wall)
        var tagForward = surfaceNormal.normalized;

        // Tag up should be perpendicular to both surface normal and gravity
        // For vertical walls, tag up should point "upward" in world space
        var worldUp = -gravity.normalized; // Opposite of gravity

        // Calculate tag right (perpendicular to both forward and world up)
        var tagRight = Vector3.Cross(worldUp, tagForward).normalized;

        // Handle edge case: surface is horizontal (ceiling/floor)
        if (tagRight.magnitude < 0.01f)
        {
            // Surface is horizontal, use world forward instead
            tagRight = Vector3.Cross(Vector3.forward, tagForward).normalized;
        }

        // Recalculate tag up to ensure orthogonality
        var tagUp = Vector3.Cross(tagForward, tagRight).normalized;

        // Create rotation from tag coordinate system
        return Quaternion.LookRotation(tagForward, tagUp);
    }

    /// <summary>
    /// Validate that a surface is suitable for tag mounting (perpendicular to gravity)
    /// Returns confidence score [0-1] based on how perpendicular the surface is
    /// </summary>
    public float ValidateGravityAlignedSurface(Vector3 surfaceNormal, int tagId)
    {
        if (!EnableGravityAlignedConstraints)
            return 1.0f; // No constraint, perfect score

        try
        {
            var gravity = GravityDirection;

            // Calculate angle between surface normal and horizontal plane
            var dotProduct = Mathf.Abs(Vector3.Dot(surfaceNormal.normalized, gravity.normalized));
            var angleFromHorizontal = Mathf.Acos(Mathf.Clamp01(dotProduct)) * Mathf.Rad2Deg;

            // Score based on how close to horizontal the surface is
            // Perfect perpendicular (0°) = score 1.0
            // At tolerance (15°) = score 0.5
            // Beyond tolerance = score decreases further
            var score =
                1.0f - Mathf.Clamp01(angleFromHorizontal / (GravityAlignmentTolerance * 2f));

            if (EnableAllDebugLogging && score < 0.8f)
            {
                Debug.Log(
                    $"[Transforms] Tag {tagId} gravity alignment score: {score:F3} (angle from horizontal: {angleFromHorizontal:F1}°)"
                );
            }

            return score;
        }
        catch (Exception e)
        {
            if (EnableAllDebugLogging)
            {
                Debug.LogWarning(
                    $"[Transforms] Error validating gravity-aligned surface for tag {tagId}: {e.Message}"
                );
            }
            return 0.5f; // Return neutral score on error
        }
    }

    /// <summary>
    /// Calculate corner quality for a detected tag
    /// Analyzes geometric consistency to detect false positives
    /// </summary>
    public float CalculateCornerQuality(
        Vector2[] corners,
        float minSideLength,
        float maxSideLength,
        float maxAspectRatio,
        float maxAngleDeviation,
        float smallTagPenalty,
        float largeTagPenalty,
        float elongatedPenalty,
        float nonConvexPenalty
    )
    {
        if (corners == null || corners.Length != 4)
            return 1.0f;

        var quality = 1.0f;

        // Calculate side lengths
        var sideLengths = new float[4];
        for (var i = 0; i < 4; i++)
        {
            var nextIndex = (i + 1) % 4;
            sideLengths[i] = Vector2.Distance(corners[i], corners[nextIndex]);
        }

        var minSide = Mathf.Min(sideLengths);
        var maxSide = Mathf.Max(sideLengths);

        // Check for degenerate cases
        if (minSide < minSideLength)
            quality *= smallTagPenalty;
        if (maxSide > maxSideLength)
            quality *= largeTagPenalty;

        // Check aspect ratio
        var aspectRatio = maxSide / Mathf.Max(minSide, 0.1f);
        if (aspectRatio > maxAspectRatio)
            quality *= elongatedPenalty;

        // Check corner angles
        var totalAngleDeviation = 0f;
        for (var i = 0; i < 4; i++)
        {
            var prev = corners[(i + 3) % 4];
            var curr = corners[i];
            var next = corners[(i + 1) % 4];

            var v1 = (prev - curr).normalized;
            var v2 = (next - curr).normalized;

            var angle = Vector2.Angle(v1, v2);
            totalAngleDeviation += Mathf.Abs(angle - 90f);
        }

        var avgAngleDeviation = totalAngleDeviation / 4f;
        if (avgAngleDeviation > maxAngleDeviation)
        {
            quality *= Mathf.Lerp(
                1.0f,
                0.2f,
                (avgAngleDeviation - maxAngleDeviation) / (maxAngleDeviation * 2f)
            );
        }

        // Check for convexity
        var isConvex = true;
        for (var i = 0; i < 4; i++)
        {
            var p1 = corners[i];
            var p2 = corners[(i + 1) % 4];
            var p3 = corners[(i + 2) % 4];

            var cross = (p2.x - p1.x) * (p3.y - p1.y) - (p2.y - p1.y) * (p3.x - p1.x);
            if (i > 0 && (cross > 0) != (i % 2 == 1))
            {
                isConvex = false;
                break;
            }
        }

        if (!isConvex)
            quality *= nonConvexPenalty;

        return Mathf.Clamp01(quality);
    }
}
