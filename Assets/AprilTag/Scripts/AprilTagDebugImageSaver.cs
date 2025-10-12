// Assets/AprilTag/Scripts/AprilTagDebugImageSaver.cs
// Debug image saving and visualization for AprilTag detection
// Handles debug image capture, detection overlays, and file management

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Manages debug image saving with detection overlays for AprilTag debugging
    /// </summary>
    public class AprilTagDebugImageSaver : MonoBehaviour
    {
        [Header("Debug Image Settings")]
        [Tooltip("Save preprocessed image for debugging (saves to persistent data path on Quest)")]
        [SerializeField]
        private bool m_enableImageSaving = false;

        [Tooltip("Include detection overlays in debug image (draws detected tag outlines)")]
        [SerializeField]
        private bool m_includeDetectionOverlay = true;

        [Tooltip("Maximum debug images to keep (older ones are deleted)")]
        [SerializeField]
        private int m_maxImages = 10;

        [Header("References")]
        [Tooltip("AprilTag controller for detection data")]
        [SerializeField]
        private AprilTagController m_controller;

        [Tooltip("Webcam pipeline for raw detections")]
        [SerializeField]
        private AprilTagWebcamPipeline m_webcamPipeline;

        [Tooltip("Transforms helper for corner extraction")]
        [SerializeField]
        private AprilTagTransforms m_transforms;

        public bool EnableImageSaving
        {
            get => m_enableImageSaving;
            set => m_enableImageSaving = value;
        }

        private void Start()
        {
            // Auto-find references if not assigned
            if (m_controller == null)
                m_controller = GetComponent<AprilTagController>();
            if (m_webcamPipeline == null)
                m_webcamPipeline = GetComponent<AprilTagWebcamPipeline>();
            if (m_transforms == null)
                m_transforms = GetComponent<AprilTagTransforms>();
        }

        /// <summary>
        /// Save debug image with optional detection overlays
        /// </summary>
        public void SaveDebugImage(
            Color32[] pixels,
            int width,
            int height,
            TagDetector detector,
            bool isPreprocessed = false
        )
        {
            if (!m_enableImageSaving)
                return;

            try
            {
                // Create texture from pixels
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.SetPixels32(pixels);

                // Draw detection overlays if enabled
                if (m_includeDetectionOverlay && detector?.DetectedTags != null)
                {
                    DrawDetectionOverlays(tex, detector, width, height);
                }

                tex.Apply();

                // Generate filename with timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var imageType = isPreprocessed ? "preprocessed" : "raw";
                var filename = $"AprilTag_Debug_{imageType}_{timestamp}.png";

                // Get debug path
                string debugPath = GetDebugImagePath();
                if (!System.IO.Directory.Exists(debugPath))
                {
                    System.IO.Directory.CreateDirectory(debugPath);
                }

                var fullPath = System.IO.Path.Combine(debugPath, filename);

                // Save the image
                var bytes = tex.EncodeToPNG();
                System.IO.File.WriteAllBytes(fullPath, bytes);

                Debug.Log($"[DebugImageSaver] Saved debug image to: {fullPath}");

                // Clean up old images
                CleanupOldDebugImages(debugPath);

                Destroy(tex);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DebugImageSaver] Failed to save debug image: {e.Message}");
            }
        }

        /// <summary>
        /// Get debug image save path (Quest-compatible)
        /// </summary>
        private string GetDebugImagePath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return System.IO.Path.Combine(Application.persistentDataPath, "AprilTagDebug");
#else
            return System.IO.Path.Combine(Application.dataPath, "..", "AprilTagDebug");
#endif
        }

        /// <summary>
        /// Draw detection overlays on texture
        /// </summary>
        private void DrawDetectionOverlays(Texture2D tex, TagDetector detector, int detW, int detH)
        {
            try
            {
                if (detector?.DetectedTags == null || !detector.DetectedTags.Any())
                    return;

                // Get raw detections for corner data
                var rawDetections =
                    m_webcamPipeline != null
                        ? m_webcamPipeline.GetRawDetections(detector)
                        : new List<object>();

                foreach (var tag in detector.DetectedTags)
                {
                    // Get corner center
                    var cornerCenter = m_transforms?.TryGetCornerBasedCenter(tag.ID, rawDetections);
                    if (!cornerCenter.HasValue)
                        continue;

                    // Convert to texture coordinates
                    var scaleX = (float)tex.width / detW;
                    var scaleY = (float)tex.height / detH;
                    var scaledCenter = new Vector2(
                        cornerCenter.Value.x * scaleX,
                        cornerCenter.Value.y * scaleY
                    );

                    // Draw tag outline
                    var tagSizePixels = 60f;
                    var halfSize = tagSizePixels * 0.5f;
                    var corners = new Vector2[]
                    {
                        new Vector2(scaledCenter.x - halfSize, scaledCenter.y - halfSize),
                        new Vector2(scaledCenter.x + halfSize, scaledCenter.y - halfSize),
                        new Vector2(scaledCenter.x + halfSize, scaledCenter.y + halfSize),
                        new Vector2(scaledCenter.x - halfSize, scaledCenter.y + halfSize),
                    };

                    DrawTagOutline(tex, corners, tag.ID);
                    DrawTagInfo(tex, scaledCenter, tag);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DebugImageSaver] Failed to draw overlays: {e.Message}");
            }
        }

        private void DrawTagOutline(Texture2D tex, Vector2[] corners, int tagId)
        {
            var color = GetDebugColorForTag(tagId);

            // Draw lines between corners
            for (int i = 0; i < 4; i++)
            {
                DrawLine(tex, corners[i], corners[(i + 1) % 4], color, 2);
            }

            // Draw corner markers
            for (int i = 0; i < 4; i++)
            {
                DrawCircle(tex, corners[i], 5, color);
            }
        }

        private void DrawTagInfo(Texture2D tex, Vector2 position, TagPose tag)
        {
            var color = GetDebugColorForTag(tag.ID);
            var infoPos = position + new Vector2(10, -10);
            DrawFilledRect(tex, infoPos, 20, 10, color);
        }

        private Color GetDebugColorForTag(int tagId)
        {
            var colors = new Color[]
            {
                Color.red,
                Color.green,
                Color.blue,
                Color.yellow,
                Color.magenta,
                Color.cyan,
            };
            return colors[tagId % colors.Length];
        }

        private void DrawLine(
            Texture2D tex,
            Vector2 start,
            Vector2 end,
            Color color,
            int thickness = 1
        )
        {
            int x0 = (int)start.x;
            int y0 = (int)start.y;
            int x1 = (int)end.x;
            int y1 = (int)end.y;

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                for (int tx = -thickness / 2; tx <= thickness / 2; tx++)
                {
                    for (int ty = -thickness / 2; ty <= thickness / 2; ty++)
                    {
                        SetPixelSafe(tex, x0 + tx, y0 + ty, color);
                    }
                }

                if (x0 == x1 && y0 == y1)
                    break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private void DrawCircle(Texture2D tex, Vector2 center, int radius, Color color)
        {
            int cx = (int)center.x;
            int cy = (int)center.y;

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    if (x * x + y * y <= radius * radius)
                    {
                        SetPixelSafe(tex, cx + x, cy + y, color);
                    }
                }
            }
        }

        private void DrawFilledRect(
            Texture2D tex,
            Vector2 position,
            int width,
            int height,
            Color color
        )
        {
            int x = (int)position.x;
            int y = (int)position.y;

            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    SetPixelSafe(tex, x + dx, y + dy, color);
                }
            }
        }

        private void SetPixelSafe(Texture2D tex, int x, int y, Color color)
        {
            if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
            {
                tex.SetPixel(x, y, color);
            }
        }

        private void CleanupOldDebugImages(string debugPath)
        {
            try
            {
                var files = System
                    .IO.Directory.GetFiles(debugPath, "AprilTag_Debug_*.png")
                    .OrderBy(f => System.IO.File.GetCreationTime(f))
                    .ToArray();

                while (files.Length > m_maxImages)
                {
                    System.IO.File.Delete(files[0]);
                    Debug.Log($"[DebugImageSaver] Deleted old debug image: {files[0]}");
                    files = files.Skip(1).ToArray();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DebugImageSaver] Failed to cleanup old images: {e.Message}");
            }
        }

        /// <summary>
        /// Toggle debug image saving at runtime
        /// </summary>
        public void ToggleImageSaving()
        {
            m_enableImageSaving = !m_enableImageSaving;
            if (m_enableImageSaving)
            {
                var path = GetDebugImagePath();
                Debug.Log($"[DebugImageSaver] Image saving ENABLED. Path: {path}");
                Debug.Log($"[DebugImageSaver] adb pull \"{path}\" .");
            }
            else
            {
                Debug.Log("[DebugImageSaver] Image saving DISABLED");
            }
        }
    }
}
