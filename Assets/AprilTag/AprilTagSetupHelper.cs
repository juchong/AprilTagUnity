// Copyright (c) Meta Platforms, Inc. and affiliates.

using PassthroughCameraSamples;
using UnityEngine;

namespace AprilTag
{
    /// <summary>
    /// Helper script to automatically set up AprilTagController in CameraViewer scenes.
    /// Add this to any GameObject and it will automatically find and configure the AprilTagController.
    /// </summary>
    public class AprilTagSetupHelper : MonoBehaviour
    {
        [Header("Auto Setup")]
        [Tooltip("Automatically find and configure AprilTagController on Start")]
        [SerializeField]
        private bool m_autoSetupOnStart = true;

        [Tooltip("Log setup process")]
        [SerializeField]
        private bool m_logSetup = true;

        private void Start()
        {
            if (m_autoSetupOnStart)
            {
                SetupAprilTagController();
            }
        }

        [ContextMenu("Setup AprilTag Controller")]
        public void SetupAprilTagController()
        {
            // Find AprilTagController in the scene
            var aprilTagController = FindFirstObjectByType<AprilTagController>();
            if (aprilTagController == null)
            {
                if (m_logSetup)
                    Debug.LogError("[AprilTagSetupHelper] No AprilTagController found in scene!");
                return;
            }

            // Find WebCamTextureManager in the scene
            var webCamTextureManager = FindFirstObjectByType<WebCamTextureManager>();
            if (webCamTextureManager == null)
            {
                if (m_logSetup)
                    Debug.LogError("[AprilTagSetupHelper] No WebCamTextureManager found in scene!");
                return;
            }

            // Set the webCamManager reference
            var field = typeof(AprilTagController).GetField(
                "webCamManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );

            if (field != null)
            {
                field.SetValue(aprilTagController, webCamTextureManager);
                if (m_logSetup)
                    Debug.Log(
                        $"[AprilTagSetupHelper] Successfully connected AprilTagController to WebCamTextureManager: {webCamTextureManager.name}"
                    );
            }
            else
            {
                if (m_logSetup)
                    Debug.LogError(
                        "[AprilTagSetupHelper] Could not access webCamManager field in AprilTagController"
                    );
            }
        }
    }
}
