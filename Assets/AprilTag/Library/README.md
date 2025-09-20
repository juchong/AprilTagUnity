# AprilTag Library Integration

This directory contains the locally integrated AprilTag library files, originally from Keijiro Takahashi's `jp.keijiro.apriltag` Unity package.

## Source

- **Original Package**: `jp.keijiro.apriltag` v1.0.2
- **Author**: Keijiro Takahashi
- **License**: BSD-2-Clause (see LICENSE file)
- **Original Repository**: https://github.com/keijiro/jp.keijiro.apriltag

## Contents

### Runtime/
- **AprilTag.Runtime.asmdef**: Assembly definition for the AprilTag runtime library
- **Interop/**: Interop classes for native library communication
  - Config.cs, Detection.cs, Detector.cs, etc.
- **Unity/**: Unity-specific integration classes
  - TagDetector.cs: Main Unity API for tag detection
  - TagPose.cs: Tag pose data structures
  - Internal/: Internal Unity utilities and jobs

### Plugin/
- **Android/**: libAprilTag.so for Android (ARM64)
- **iOS/**: libAprilTag.a for iOS (ARM64)
- **Linux/**: libAprilTag.so for Linux (x86-64)
- **macOS/**: AprilTag.bundle for macOS (x86-64)
- **Windows/**: AprilTag.dll for Windows (x86-64)

## Usage

The AprilTag library provides real-time marker detection capabilities with support for multiple tag families:

```csharp
using AprilTag;
using AprilTag.Interop;

// Create detector with tag36h11 (recommended for ArUcO compatibility)
var detector = new TagDetector(imageWidth, imageHeight, TagFamily.Tag36h11, decimation);

// Or use the default constructor (tag36h11)
var detector = new TagDetector(imageWidth, imageHeight, decimation);

// Process image
detector.ProcessImage(imageBuffer, fovDegrees, tagSizeMeters);

// Get detected tags
foreach (var tag in detector.DetectedTags)
{
    Debug.Log($"Tag {tag.ID} at {tag.Position}");
}

// Cleanup
detector.Dispose();
```

### Supported Tag Families

- **Tag36h11**: Recommended for ArUcO detector compatibility
- **TagStandard41h12**: Original AprilTag format with higher data density

## Dependencies

- Unity Burst (for performance optimization)
- Unity Mathematics (for vector/matrix operations)

## Integration Notes

This library has been manually integrated into the project to remove external package dependencies. All original functionality is preserved while maintaining independence from the original package registry.
