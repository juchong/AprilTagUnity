# PhotonVision Tag Tracking and Pose Estimation Methods

## Overview
PhotonVision is an open-source computer vision solution for the FIRST Robotics Competition that provides sophisticated AprilTag tracking and robot pose estimation capabilities.

## Tag Position Tracking

### 1. Image Capture & Processing
- Captures frames from cameras mounted on the robot
- Processes images using OpenCV for marker detection
- Utilizes the AprilTag detection library for fiducial marker identification

### 2. Tag Detection Algorithm
- Detects AprilTag corners in the captured images
- Extracts unique tag identifiers and orientations
- Uses the known physical size of tags for scale reference
- Applies camera intrinsic parameters for accurate positioning

### 3. Pose Estimation from Tags
- Employs **Perspective-n-Point (PnP)** algorithms (specifically `solvePnP` from OpenCV)
- Calculates 3D position and orientation of each detected tag relative to the camera
- Uses tag corner coordinates and known tag dimensions for accurate pose estimation

## Robot Pose Calculation

The core of PhotonVision's robot localization is the **`PhotonPoseEstimator`** class:

### 1. Field Layout Definition
- Predefined map of all AprilTag positions on the competition field
- Establishes a global coordinate system for the field
- Provides reference points for triangulation

### 2. Camera-to-Robot Transform
- Defines the spatial relationship between camera and robot coordinate frames
- Accounts for camera mounting position and orientation on the robot
- Enables transformation between camera and robot coordinate systems

### 3. Pose Estimation Process
- **Multi-tag Fusion**: Combines data from multiple detected tags for improved accuracy
- **Triangulation**: Uses detected tag positions and known field layout to calculate robot pose
- **Coordinate Transformation**: Converts camera-relative tag positions to field-relative robot pose
- **Continuous Updates**: Integrates vision measurements with existing pose estimates

## Key Technical Implementation Details

- **OpenCV Integration**: Uses OpenCV's `solvePnP` for robust pose estimation
- **AprilTag Library**: Leverages the AprilTag detection library for marker identification
- **Real-time Processing**: Designed for high-frequency updates (typically 30+ FPS)
- **Multi-tag Support**: Can process multiple tags simultaneously for enhanced accuracy
- **Error Handling**: Includes validation and filtering of pose estimates
- **Integration with WPILib**: Seamlessly integrates with FIRST Robotics Competition software stack

## Comparison with AprilTagUnity Project

### Similarities
- **Tag Detection**: Both use AprilTag libraries for marker detection
- **Pose Estimation**: Both calculate 3D positions from 2D image coordinates
- **Camera Integration**: Both handle camera calibration and intrinsic parameters
- **Real-time Processing**: Both are designed for live pose estimation

### Key Differences
- **Platform**: PhotonVision targets FRC robots (Java/C++), while AprilTagUnity targets Meta Quest VR (C#/Unity)
- **Coordinate Systems**: PhotonVision uses field-relative coordinates, while AprilTagUnity uses VR-relative coordinates
- **Integration**: PhotonVision integrates with robot control systems, while AprilTagUnity integrates with VR tracking systems

## References
- [PhotonVision GitHub Repository](https://github.com/PhotonVision/photonvision)
- PhotonVision Documentation: docs.photonvision.org
- PhotonVision UI Demo: photonvision.global

## Implementation Notes for AprilTagUnity

The transparent red square visualization in AprilTagUnity is similar to how PhotonVision provides visual feedback for detected tags, making it easier for operators to verify that the system is working correctly.

Key takeaways for AprilTagUnity development:
1. Consider implementing multi-tag fusion for improved accuracy
2. Explore PnP algorithms for robust pose estimation
3. Implement proper error handling and validation
4. Consider continuous pose updates rather than single-frame estimates
5. Maintain real-time processing performance for VR applications
