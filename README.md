# AprilTagUnity

A Unity project that implements AprilTag detection and FRC field localization for Meta Quest VR headsets using the Meta Passthrough Camera API. This project enables real-time marker detection, spatial anchor placement, and precise headset localization on FIRST Robotics Competition fields.

## Overview

AprilTagUnity combines the power of:
- **AprilTag detection** using locally integrated AprilTag library (originally from Keijiro Takahashi)
- **Meta Passthrough Camera API** for accessing the Quest's camera feed
- **Spatial Anchors** for persistent, drift-free localization
- **FRC Field Localization** using official WPILib field layouts
- **Unity XR** for VR/MR application development

The project provides a seamless way to detect and track AprilTag markers in real-time within VR environments, enabling applications like:
- **FIRST Robotics field localization** - Track headset position in field coordinates
- Spatial tracking and calibration using Quest's inside-out tracking
- Mixed reality interactions with persistent anchors
- Augmented reality overlays on physical fields

## Features

### Core Detection
- 🎯 **Real-time AprilTag Detection**: Detect multiple Tag36h11 markers simultaneously
- 📱 **Meta Quest Integration**: Works with Quest 2, Quest Pro, and Quest 3
- 🔧 **Easy Setup**: Automatic configuration with setup helper scripts
- 🎨 **Visual Feedback**: Configurable visualization for detected tags
- ⚡ **Performance Optimized**: GPU preprocessing, configurable detection frequency and decimation
- 🔍 **Reflection-based Integration**: No compile-time dependencies on Meta's Passthrough Camera API

### Spatial Anchors
- ⚓ **Automatic Anchor Placement**: Creates persistent OVRSpatialAnchors at detected tag locations
- 🎯 **Confidence-based Placement**: Only places anchors after stable, high-confidence detections
- 💾 **Persistent Storage**: Anchors survive app restarts and device reboots
- 🚫 **Keep-out Zones**: Prevents duplicate anchor placement near existing anchors
- 🔄 **Anchor Loading**: Automatically loads previously saved anchors on startup

### FRC Field Localization
- 🏟️ **Field Coordinate Tracking**: Transform headset pose to FRC field coordinates
- 📐 **Multi-anchor Alignment**: Uses 3+ spatial anchors for robust field alignment
- 🎲 **Outlier Rejection**: Automatically detects and removes bad anchor data
- ✅ **Alignment Validation**: RMS error checking ensures high-quality alignment
- 🔄 **Continuous Improvement**: Refines alignment as more anchors become available
- 💾 **Persistent Alignment**: Saves alignment across sessions
- 📊 **Quality Metrics**: Real-time display of alignment error and anchor count
- 🗺️ **Official Field Layouts**: Supports all WPILib field layouts (2022-2025)
- 🎮 **Quest Inside-out Tracking**: Leverages Quest's SLAM for drift-free localization

## Requirements

### Hardware
- Meta Quest 2, Quest Pro, or Quest 3 headset
- Android development environment (for building APKs)

### Software
- Unity 6000.2.6f2 or later
- Meta XR SDK v78.0.0 or later
- Android SDK with API level 32+

## Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/AprilTagUnity.git
   cd AprilTagUnity
   ```

2. **Open in Unity**
   - Launch Unity Hub
   - Click "Add" and select the project folder
   - Open the project with Unity 6000.2.6f2 or later

3. **Install Dependencies**
   The project uses Unity Package Manager with the following key dependencies:
   - `com.meta.xr.sdk.all` (v78.0.0) - Meta XR SDK
   - `com.unity.xr.openxr` (v1.13.2) - OpenXR support
   
   Note: The AprilTag library is locally integrated and doesn't require external package installation.

4. **Build and Deploy**
   - Connect your Quest headset via USB
   - Enable Developer Mode in the Quest settings
   - Build and deploy to your headset

## Quick Start

### Basic AprilTag Detection

1. **Add AprilTagController to your scene**
   ```csharp
   // Create an empty GameObject and add the AprilTagController component
   var aprilTagController = gameObject.AddComponent<AprilTagController>();
   ```

2. **Configure the controller**
   - Assign a `WebCamTextureManager` from Meta's Passthrough Camera samples
   - Set the tag size in meters (default: 0.08m for 8cm tags)
   - Configure detection parameters (decimation, frequency, etc.)

3. **Use the setup helper (recommended)**
   ```csharp
   // Add AprilTagSetupHelper to automatically configure the controller
   var setupHelper = gameObject.AddComponent<AprilTagSetupHelper>();
   setupHelper.SetupAprilTagController();
   ```

### FRC Field Localization

1. **Add FRCFieldLocalizer to your AprilTagController GameObject**
   ```csharp
   var localizer = aprilTagController.gameObject.AddComponent<FRCFieldLocalizer>();
   ```

2. **Add field layout JSON to Resources**
   - Place WPILib field layout JSON files in `Assets/AprilTag/Resources/FieldLayouts/`
   - Supported fields: `2022-rapidreact`, `2023-chargedup`, `2024-crescendo`, `2025-reefscape-andymark`, `2025-reefscape-welded`

3. **Configure the localizer (in Inspector)**
   - Set `Field Layout Name` to your field (e.g., "2025-reefscape-andymark")
   - Set `Min Anchors` to 3 (default, minimum for alignment)
   - Enable `Auto Align` for automatic field alignment

4. **Look at AprilTags on the field**
   - Point Quest headset at 3+ different AprilTags on the field
   - Spatial anchors are automatically created
   - Field alignment happens automatically once enough anchors exist

5. **Access field coordinates**
   ```csharp
   if (localizer.IsAligned)
   {
       Vector3 fieldPos = localizer.GetFieldPosition();
       Quaternion fieldRot = localizer.GetFieldRotation();
       Debug.Log($"Headset at field position: {fieldPos}");
   }
   ```

### Sample Scenes

The project includes several sample scenes in `Assets/PassthroughCameraApiSamples/`:
- **CameraViewer**: Basic camera feed display
- **MultiObjectDetection**: Advanced object detection examples
- **StartScene**: Main menu with navigation to all samples

## Configuration

### AprilTagController Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `tagFamily` | Tag family to detect (Tag36h11 or TagStandard41h12) | Tag36h11 |
| `tagSizeMeters` | Physical size of AprilTag markers | 0.08m |
| `decimate` | Downscale factor for detection (1-8) | 2 |
| `maxDetectionsPerSecond` | Detection frequency limit | 15 fps |
| `horizontalFovDeg` | Camera field of view | 78° |
| `scaleVizToTagSize` | Scale visualizations to tag size | true |

### Tag Family Selection

- **Tag36h11** (default): Recommended for ArUcO detector compatibility and general use
- **TagStandard41h12**: Original AprilTag format with higher data density but requires more processing

**Note**: You can download tag images from the [AprilTag Images repository](https://github.com/AprilRobotics/apriltag-imgs). Print them or display them on a screen for testing.

### Performance Tuning

- **Decimation**: Higher values (4-8) improve performance but reduce detection accuracy
- **Detection Frequency**: Lower values (5-10 fps) reduce CPU usage
- **Tag Size**: Accurate physical measurements improve pose estimation

## Usage Examples

### Basic Detection
```csharp
public class MyAprilTagHandler : MonoBehaviour
{
    [SerializeField] private AprilTagController aprilTagController;
    
    void Start()
    {
        // Configure for tag36h11 (default) or tagStandard41h12
        aprilTagController.tagFamily = AprilTag.Interop.TagFamily.Tag36h11;
    }
    
    void Update()
    {
        // Access detected tags through the controller
        var detector = aprilTagController.GetDetector();
        foreach (var tag in detector.DetectedTags)
        {
            Debug.Log($"Detected tag {tag.ID} at position {tag.Position}");
        }
    }
}
```

### Custom Visualization
```csharp
// Create custom visualizations for detected tags
public GameObject customTagPrefab;

void OnTagDetected(int tagId, Vector3 position, Quaternion rotation)
{
    var viz = Instantiate(customTagPrefab);
    viz.transform.SetPositionAndRotation(position, rotation);
    viz.name = $"Tag_{tagId}";
}
```

## Project Structure

```
Assets/
├── AprilTag/
│   ├── AprilTagController.cs                 # Main detection controller
│   ├── AprilTagPermissionsManager.cs         # Android permission handling
│   ├── AprilTagSetupHelper.cs                # Automatic setup helper
│   ├── AprilTagSceneSetup.cs                 # Quest runtime configuration
│   ├── Scripts/
│   │   ├── AprilTagSpatialAnchorManager.cs   # Spatial anchor management
│   │   ├── AprilTagFieldLayout.cs            # FRC field layout parser
│   │   ├── FRCFieldLocalizer.cs              # Field coordinate localization
│   │   ├── AprilTagConfidenceManager.cs      # Detection confidence tracking
│   │   ├── AprilTagGPUPreprocessor.cs        # GPU image preprocessing
│   │   ├── AprilTagVisualization.cs          # Tag visualization
│   │   └── AprilTagAnchorInteraction.cs      # Controller-based anchor mgmt
│   ├── Resources/
│   │   └── FieldLayouts/                     # WPILib field layout JSONs
│   │       ├── 2022-rapidreact.json
│   │       ├── 2023-chargedup.json
│   │       ├── 2024-crescendo.json
│   │       ├── 2025-reefscape-andymark.json
│   │       └── 2025-reefscape-welded.json
│   └── Library/                              # Locally integrated AprilTag library
│       ├── Runtime/                          # C# API and Unity integration
│       └── Plugin/                           # Native libraries (Android/Windows)
├── PassthroughCameraApiSamples/              # Meta's official samples
│   ├── CameraViewer/                         # Basic camera viewer
│   ├── MultiObjectDetection/                 # AI object detection
│   └── StartScene/                           # Main menu scene
└── Resources/                                # Project settings
```

## Troubleshooting

### Common Issues

1. **No WebCamTexture Available**
   - Ensure Meta's Passthrough Camera API is properly initialized
   - Check that the WebCamTextureManager is present in the scene
   - Verify camera permissions are granted on Quest

2. **WebCamTexture GPU Initialization Errors**
   - The system now automatically waits for WebCamTexture to initialize
   - Uses direct pixel access instead of Graphics.CopyTexture for better reliability
   - Allow a few seconds for the camera feed to stabilize

3. **Poor Detection Performance**
   - Increase decimation value (try 4-8)
   - Reduce detection frequency
   - Ensure good lighting conditions
   - Enable GPU preprocessing for better image quality

4. **Inaccurate Pose Estimation**
   - Verify the tag size parameter matches your physical tags
   - Check camera calibration and FOV settings
   - Ensure tags are not too small or far away

5. **Spatial Anchors Not Persisting**
   - Verify spatial permissions are granted on Quest
   - Check that anchors are being saved (see logs)
   - Ensure sufficient stable detection frames before anchor placement

6. **Field Alignment Fails**
   - Need at least 3 spatial anchors with known field positions
   - Tags must be from the correct field layout
   - Check alignment error threshold (default 0.5m)
   - Verify field layout JSON is loaded correctly

7. **Alignment Error Too High**
   - Increase outlier rejection threshold
   - Check physical tag placement matches field layout
   - Ensure tags are measured accurately (6.5 inches = 0.1651m)
   - Look at more tags to improve alignment quality

### Debug Logging

Enable debug logging to troubleshoot issues:
```csharp
// AprilTag detection
aprilTagController.logDebugInfo = true;
aprilTagController.logDetections = true;

// Field localization
frcFieldLocalizer.m_enableDebug = true;
```

### Quest ADB Logging

Monitor field position on Quest via adb:
```bash
adb logcat -s Unity:I | grep FieldPos
```

This will show field position updates in real-time:
```
[FieldPos] 1.234,0.500,4.567
```

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Key Design Decisions

### Quest-Focused Development
This project is designed **specifically for Meta Quest headsets**, not Unity Editor debugging:
- All development targets Quest runtime, not editor play mode
- Configuration happens programmatically via `AprilTagSceneSetup.cs`
- No editor-only features (context menus, inspector-only tools)
- Testing must be done on actual Quest hardware

### Anchor-Based Localization (Not Continuous Vision)
Unlike PhotonVision's continuous vision tracking approach:
- **AprilTags are used ONLY for establishing spatial anchors** at known field positions
- **Quest's inside-out tracking** handles all movement after anchor placement
- **Vision-based detection is disabled** after alignment - anchors provide the reference frame
- This provides **drift-free, sub-millimeter precision** using Quest's world-class SLAM system

### Why This Approach is Superior
- ✅ No drift from camera movements or lighting changes
- ✅ Works in any lighting condition once anchors are placed
- ✅ Leverages Quest's native spatial understanding
- ✅ Sub-millimeter precision from spatial anchors
- ✅ Minimal CPU overhead - no continuous tag detection needed

## Acknowledgments

- **Keijiro Takahashi** for the excellent [AprilTag Unity package](https://github.com/keijiro/jp.keijiro.apriltag) (now locally integrated)
- **April Robotics Laboratory** for the original [AprilTag system](https://github.com/juchong/apriltag.git)
- **Meta** for the Passthrough Camera API, Spatial Anchor system, and XR SDK
- **WPILib** for the official FRC field layout definitions
- **PhotonVision** for inspiration on multi-tag localization approaches
- **Unity Technologies** for the XR framework

## Support

- Create an issue for bug reports or feature requests
- Check the [Meta XR Documentation](https://developer.oculus.com/documentation/unity/unity-passthrough-camera-api/)
- Visit the [AprilTag Documentation](https://april.eecs.umich.edu/software/apriltag)
- Review [WPILib AprilTag Documentation](https://docs.wpilib.org/en/stable/docs/software/vision-processing/apriltag/apriltag-intro.html)

---

**Note**: This project requires a Meta Quest headset with Developer Mode enabled. It is designed for VR/MR applications running on Quest hardware and will not work in standard Unity editor play mode without proper XR setup and Quest device connection.