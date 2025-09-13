# AprilTag Permission System

This document explains the permission system implemented for the AprilTag Unity project to handle camera and spatial data access on Meta Quest devices.

## Overview

The AprilTag system requires specific permissions to access:
- **Camera data** for passthrough video functionality
- **Spatial data** for AR features and scene understanding

## Required Permissions

### Android Permissions (Meta Quest)
1. `android.permission.CAMERA` - Required to use WebCamTexture object
2. `horizonos.permission.HEADSET_CAMERA` - Required for Passthrough Camera API (Horizon OS v74+)
3. `com.oculus.permission.USE_SCENE` - Required for spatial data access (planes, meshes, occlusion)

## Components

### 1. AprilTagPermissionsManager
**Location**: `Assets/AprilTag/AprilTagPermissionsManager.cs`

The core permission management component that:
- Checks current permission status on startup
- Requests missing permissions from the user
- Handles permission grant/denial callbacks
- Provides retry functionality for denied permissions
- Exposes permission status through static properties and events

**Key Features**:
- Automatic permission checking on startup
- Configurable retry behavior for denied permissions
- Event-driven architecture for permission state changes
- Detailed permission status reporting

### 2. AprilTagPermissionUI
**Location**: `Assets/AprilTag/AprilTagPermissionUI.cs`

A UI component that provides:
- Visual feedback on permission status
- User-friendly permission request interface
- Automatic panel visibility management
- Detailed permission information display

**Key Features**:
- Real-time permission status display
- Automatic UI updates based on permission changes
- Configurable auto-hide behavior
- Support for custom UI layouts

### 3. AprilTagSceneSetup
**Location**: `Assets/AprilTag/AprilTagSceneSetup.cs`

A helper component for easy scene setup that:
- Automatically creates permission manager and UI components
- Configures permission settings
- Provides context menu options for testing

## Integration

### Automatic Integration
The permission system is automatically integrated into the `AprilTagController`:

```csharp
// In AprilTagController.Awake()
AprilTagPermissionsManager.OnAllPermissionsGranted += OnAllPermissionsGranted;
AprilTagPermissionsManager.OnPermissionsDenied += OnPermissionsDenied;

// In AprilTagController.Update()
if (!AprilTagPermissionsManager.HasAllPermissions)
{
    // Skip detection until permissions are granted
    return;
}
```

### Manual Setup
To manually set up the permission system:

1. **Add Permissions Manager**:
   ```csharp
   var managerGO = new GameObject("AprilTagPermissionsManager");
   managerGO.AddComponent<AprilTagPermissionsManager>();
   ```

2. **Add Permission UI** (optional):
   ```csharp
   var uiGO = new GameObject("AprilTagPermissionUI");
   uiGO.AddComponent<AprilTagPermissionUI>();
   ```

3. **Use Scene Setup Helper**:
   ```csharp
   var setupGO = new GameObject("AprilTagSceneSetup");
   setupGO.AddComponent<AprilTagSceneSetup>();
   ```

## Usage Examples

### Checking Permission Status
```csharp
// Check if all permissions are granted
bool ready = AprilTagPermissionsManager.HasAllPermissions;

// Check individual permission types
bool hasCamera = AprilTagPermissionsManager.HasCameraPermissions;
bool hasSpatial = AprilTagPermissionsManager.HasSpatialPermissions;

// Get detailed status
string status = permissionsManager.GetPermissionStatus();
```

### Subscribing to Permission Events
```csharp
void Start()
{
    AprilTagPermissionsManager.OnAllPermissionsGranted += OnPermissionsReady;
    AprilTagPermissionsManager.OnPermissionsDenied += OnPermissionsDenied;
}

void OnPermissionsReady()
{
    Debug.Log("All permissions granted - starting AprilTag detection");
}

void OnPermissionsDenied()
{
    Debug.Log("Permissions denied - showing user message");
}
```

### Manual Permission Request
```csharp
// Force permission check and request
permissionsManager.RefreshPermissionStatus();
```

## Configuration

### AprilTagPermissionsManager Settings
- `requestPermissionsOnStart`: Automatically request permissions on component start
- `retryOnDenial`: Automatically retry permission requests if denied
- `retryDelaySeconds`: Delay between retry attempts
- `enableDebugLogs`: Enable detailed logging for debugging

### AprilTagPermissionUI Settings
- `showPanelOnStart`: Show permission panel when component starts
- `autoHideOnGranted`: Automatically hide panel when permissions are granted
- `autoHideDelay`: Delay before auto-hiding the panel

## Android Manifest Requirements

The following permissions should be declared in your Android manifest:

```xml
<uses-permission android:name="android.permission.CAMERA" />
<uses-permission android:name="horizonos.permission.HEADSET_CAMERA" />
<uses-permission android:name="com.oculus.permission.USE_SCENE" />

<uses-feature android:name="com.oculus.feature.PASSTHROUGH" android:required="true" />
```

**Note**: The Meta Passthrough Camera API samples include an editor script that automatically adds these permissions to your manifest during build.

## Enhanced Setup Features

The `AprilTagSceneSetup` now provides complete automation:

### What Gets Created Automatically:
- ✅ `AprilTagPermissionsManager` - Handles all permissions
- ✅ `AprilTagPermissionUI` - User-friendly permission interface  
- ✅ `AprilTagController` - Main detection system
- ✅ `WebCamTextureManager` - Provides passthrough camera feed (if none exists)
- ✅ Simple tag visualization prefab (if none provided)
- ✅ Canvas and UI setup (if needed)
- ✅ Automatic connections between all components

### WebCam Manager Auto-Creation:
- **Automatically creates** `WebCamTextureManager` if none exists in the scene
- **Configurable settings**: Camera eye (Left/Right), resolution preferences
- **Permission integration**: Links with the permission system
- **Zero manual setup**: Everything works out of the box

### Complete Context Menu Options:
- `Setup Complete AprilTag System` - Full automated setup
- `Setup AprilTag Permissions Only` - Just permission system
- `Setup AprilTag Controller Only` - Just detection system
- `Setup WebCam Manager` - Just camera feed system
- `Connect to WebCam Manager` - Link existing components
- `Show System Status` - Complete system overview
- `Clean Up Setup Components` - Remove duplicates

## Best Practices

1. **Always check permissions before accessing camera or spatial data**
2. **Provide clear explanations to users about why permissions are needed**
3. **Handle permission denials gracefully with fallback functionality**
4. **Use the UI component to provide visual feedback to users**
5. **Test permission flows on actual Quest devices**

## Troubleshooting

### Common Issues

1. **Permissions not being requested**:
   - Ensure `requestPermissionsOnStart` is enabled
   - Check that the permissions manager is in the scene
   - Verify Android manifest contains required permissions

2. **Permission UI not showing**:
   - Check that Canvas exists in the scene
   - Ensure `showPanelOnStart` is enabled
   - Verify UI components are properly assigned

3. **Detection not starting**:
   - Check `AprilTagPermissionsManager.HasAllPermissions` status
   - Ensure permission events are properly subscribed
   - Verify WebCamTexture is available after permissions are granted

### Debug Information

Enable debug logging to see detailed permission information:
```csharp
permissionsManager.enableDebugLogs = true;
```

Use the context menu options in `AprilTagSceneSetup` to check permission status and force permission requests during development.

## Platform Notes

- **Android (Quest)**: Full permission system support
- **Editor**: Permissions are automatically granted for testing
- **Other Platforms**: Permission checks return true by default

## Memory Considerations

The permission system follows the user's memory preferences:
- Minimal debug output by default [[memory:8818636]]
- No .meta files are created during setup [[memory:8818643]]
- Only essential logging for tag detection results
