// Assets/AprilTag/AprilTagVisualization.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AprilTag;
using Meta.XR;
using PassthroughCameraSamples;
using Unity.XR.CoreUtils;
using UnityEngine;

public class AprilTagVisualization : MonoBehaviour
{
    [Header("Visualization Settings")]
    [SerializeField]
    private bool m_ignoreOcclusion = true;

    /// USAGE: REFERENCED in pose/visualization pipeline. Keep. (Called when instantiating visualization)
    public void ConfigureVisualizationForNoOcclusion(Transform visualization)
    {
        if (!m_ignoreOcclusion)
            return;

        // Configure all renderers to ignore occlusion
        var renderers = visualization.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            // Set render queue to be on top of everything else
            var materials = renderer.materials;
            foreach (var material in materials)
            {
                if (material != null)
                {
                    // Use a high but valid render queue value to render on top
                    material.renderQueue = 2000; // High but within valid range

                    // Make sure the material doesn't write to depth buffer for occlusion
                    material.SetInt("_ZWrite", 0);
                    material.SetInt("_ZTest", 0); // Always pass depth test
                }
            }
        }

        // Configure Canvas components to render on top
        var canvases = visualization.GetComponentsInChildren<Canvas>();
        foreach (var canvas in canvases)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000; // High sorting order
        }

        // Configure UI elements to ignore raycast
        var graphicRaycasters =
            visualization.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>();
        foreach (var raycaster in graphicRaycasters)
        {
            raycaster.ignoreReversedGraphics = true;
        }
    }
}
