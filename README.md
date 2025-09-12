# AprilTagUnity

A Unity project that implements AprilTag detection for Meta Quest VR headsets using the Meta Passthrough Camera API. This project enables real-time marker detection and tracking in mixed reality applications.

## Overview

AprilTagUnity combines the power of:
- **AprilTag detection** using Keijiro Takahashi's `jp.keijiro.apriltag` package
- **Meta Passthrough Camera API** for accessing the Quest's camera feed
- **Unity XR** for VR/MR application development

The project provides a seamless way to detect and track AprilTag markers in real-time within VR environments, enabling applications like:
- Augmented reality overlays
- Spatial tracking and calibration
- Mixed reality interactions
- Object placement and anchoring

## Features

- 🎯 **Real-time AprilTag Detection**: Detect multiple AprilTag markers simultaneously
- 📱 **Meta Quest Integration**: Works with Quest 2, Quest Pro, and Quest 3
- 🔧 **Easy Setup**: Automatic configuration with setup helper scripts
- 🎨 **Visual Feedback**: Configurable visualization for detected tags
- ⚡ **Performance Optimized**: Configurable detection frequency and resolution scaling
- 🔍 **Reflection-based Integration**: No compile-time dependencies on Meta's Passthrough Camera API

## Requirements

### Hardware
- Meta Quest 2, Quest Pro, or Quest 3 headset
- Android development environment (for building APKs)

### Software
- Unity 2022.3 LTS or later
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
   - Open the project with Unity 2022.3 LTS or later

3. **Install Dependencies**
   The project uses Unity Package Manager with the following key dependencies:
   - `com.meta.xr.sdk.all` (v78.0.0) - Meta XR SDK
   - `jp.keijiro.apriltag` (v1.0.2) - AprilTag detection library
   - `com.unity.xr.openxr` (v1.13.2) - OpenXR support

4. **Build and Deploy**
   - Connect your Quest headset via USB
   - Enable Developer Mode in the Quest settings
   - Build and deploy to your headset

## Quick Start

### Basic Setup

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

### Sample Scenes

The project includes several sample scenes in `Assets/PassthroughCameraApiSamples/`:
- **CameraViewer**: Basic camera feed display
- **MultiObjectDetection**: Advanced object detection examples
- **StartScene**: Main menu with navigation to all samples

## Configuration

### AprilTagController Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `tagSizeMeters` | Physical size of AprilTag markers | 0.08m |
| `decimate` | Downscale factor for detection (1-8) | 2 |
| `maxDetectionsPerSecond` | Detection frequency limit | 15 fps |
| `horizontalFovDeg` | Camera field of view | 78° |
| `scaleVizToTagSize` | Scale visualizations to tag size | true |

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
├── AprilTag/                    # Core AprilTag implementation
│   ├── AprilTagController.cs    # Main detection controller
│   ├── AprilTagSetupHelper.cs   # Automatic setup helper
│   └── InputSystemFixer.cs      # Input system compatibility
├── PassthroughCameraApiSamples/ # Meta's official samples
│   ├── CameraViewer/            # Basic camera viewer
│   ├── MultiObjectDetection/    # AI object detection
│   └── StartScene/              # Main menu scene
└── Resources/                   # Project resources and settings
```

## Troubleshooting

### Common Issues

1. **No WebCamTexture Available**
   - Ensure Meta's Passthrough Camera API is properly initialized
   - Check that the WebCamTextureManager is present in the scene

2. **Poor Detection Performance**
   - Increase decimation value (try 4-8)
   - Reduce detection frequency
   - Ensure good lighting conditions

3. **Inaccurate Pose Estimation**
   - Verify the tag size parameter matches your physical tags
   - Check camera calibration and FOV settings
   - Ensure tags are not too small or far away

### Debug Logging

Enable debug logging to troubleshoot issues:
```csharp
aprilTagController.logDebugInfo = true;
aprilTagController.logDetections = true;
```

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- **Keijiro Takahashi** for the excellent [AprilTag Unity package](https://github.com/keijiro/jp.keijiro.apriltag)
- **Meta** for the Passthrough Camera API and XR SDK
- **Unity Technologies** for the XR framework

## Support

- Create an issue for bug reports or feature requests
- Check the [Meta XR Documentation](https://developer.oculus.com/documentation/unity/unity-passthrough-camera-api/)
- Visit the [AprilTag Documentation](https://april.eecs.umich.edu/software/apriltag)

---

**Note**: This project requires a Meta Quest headset with Developer Mode enabled. It is designed for VR/MR applications and will not work in standard Unity editor play mode without proper XR setup.