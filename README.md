# Volumetric Camera Blend AR

Unity AR Foundation project implementing volumetric depth visualization for Android ARCore devices.

## Features

- **Real-time AR depth visualization** using ARCore Environment Depth API
- **RGB depth gradients** - Distance-based rainbow color mapping (close=red, far=blue)
- **Surface normals** - Orientation-based RGB coloring (X=red, Y=green, Z=blue)
- **Edge detection** - White highlights on object boundaries
- **Plane detection** - Detects horizontal and vertical surfaces
- **TSDF Volume Fusion** (experimental) - True volumetric reconstruction using compute shaders

## Requirements

- Unity 2020.3.6f1 or later
- AR Foundation 4.1.13
- ARCore XR Plugin 4.1.13
- Android device with ARCore support (tested on Samsung S24)
- Android API Level 24+
- OpenGLES3

## Project Structure

```
Assets/
├── ARVolumetricBlend.cs          # Main AR controller with depth visualization
├── DepthColorize.shader           # Depth-based RGB gradient shader
├── TSDFVolumeFusion.cs           # TSDF volume fusion system (experimental)
├── TSDFVolumeIntegration.compute # GPU-accelerated voxel updates
├── TSDFRayMarch.shader           # Ray marching renderer for TSDF
├── UnlitColorPlane.shader        # Simple plane visualization shader
└── VolumetricBlend.shader        # Legacy dual-camera blend shader
```

## Setup Instructions

### Basic Depth Visualization (Recommended for Mobile)

1. Open project in Unity 2020.3.6f1
2. Open the Scene in `Assets/Scenes/`
3. Select **AR Camera** GameObject
4. Ensure **ARVolumetricBlend** component is enabled
5. Configure parameters:
   - **Color Intensity**: 0-1 (blend strength, default 0.7)
   - **Normal Strength**: 0.1-5 (surface detail, default 1.0)
6. Build Settings:
   - Platform: Android
   - Target Architectures: ARM64
   - Graphics APIs: OpenGLES3
   - Minimum API Level: 24
7. Build and Run

### TSDF Volume Fusion (Experimental - Desktop/High-end Only)

**Note**: Requires compute shader support. Not recommended for mobile due to performance.

1. Select **AR Camera** GameObject
2. Disable **ARVolumetricBlend** component
3. Add **TSDFVolumeFusion** component
4. Assign references:
   - AR Occlusion Manager
   - AR Camera Manager
   - Volume Integration CS → `TSDFVolumeIntegration.compute`
   - Ray March Shader → `TSDFRayMarch.shader`
5. Configure volume:
   - **Volume Resolution**: 128x128x128 voxels (default)
   - **Voxel Size**: 0.05m (default)
   - **Truncation Distance**: 0.15m (default)

## How It Works

### Depth-Based RGB Visualization

The system uses ARCore's Environment Depth API to capture depth information at 160x90 resolution. A CommandBuffer applies a custom shader that:

1. **Samples depth texture** from ARCore
2. **Calculates surface normals** using finite differences on depth gradients
3. **Maps depth to colors**: Red (close) → Yellow → Green → Cyan → Blue (far)
4. **Combines normals × depth colors** for volumetric appearance
5. **Detects edges** via depth discontinuities
6. **Blends with camera feed** for AR overlay

### TSDF Volume Fusion (Experimental)

Uses Truncated Signed Distance Function to build true 3D reconstruction:

1. **3D Voxel Grid**: RenderTexture3D storing signed distances
2. **Compute Shader Integration**: GPU-accelerated depth frame fusion
3. **Weighted Averaging**: Multiple depth measurements improve accuracy
4. **Ray Marching**: Renders volumetric data by marching through voxel grid

## Controls

- **Color Intensity Slider**: Adjust RGB overlay strength
- **Normal Strength Slider**: Control surface detail sensitivity
- Move camera around to see depth changes
- Point at surfaces to see orientation-based colors

## Visual Guide

**What you should see:**

- **Red/Orange tones** on close objects (hand, phone)
- **Yellow/Green** on medium distance objects (furniture)
- **Cyan/Blue** on far surfaces (walls, floor)
- **Horizontal surfaces** (floor) show more green (Y-axis normal)
- **Vertical surfaces** (walls) show red/blue mix
- **White edges** around object boundaries

## Troubleshooting

### Black Screen
- Ensure ARCameraBackground component is on AR Camera
- Check that ARVolumetricBlend uses CommandBuffer (not OnRenderImage)
- Verify depth texture is available (check logs)

### App Crashes
- Disable TSDF components if using mobile device
- Ensure Target Architecture is ARM64 only
- Check Graphics API is OpenGLES3 (not Vulkan)

### No Depth Data
- Device must support ARCore Depth API
- Grant camera permissions
- Move camera to help ARCore initialize

## Performance

- **Depth Visualization**: 30-60 FPS on mid-range Android devices
- **TSDF Volume Fusion**: Requires high-end GPU, not recommended for mobile
- Depth resolution: 160x90 (ARCore limitation)
- Plane detection: 15-20 planes typical indoor environment

## Technical Details

**Rendering Pipeline:**
```
ARCameraBackground → CommandBuffer → DepthColorize Shader → Screen
```

**Shader Inputs:**
- `_MainTex`: Camera feed from ARCameraBackground
- `_EnvironmentDepth`: 160x90 depth texture from ARCore
- `_ColorIntensity`: Blend amount (0-1)
- `_NormalStrength`: Normal sensitivity (0.1-5)

**Normal Calculation:**
```glsl
// Finite difference method
float dx = (depthRight - depthCenter) * strength
float dy = (depthUp - depthCenter) * strength
float3 normal = normalize(-dx, -dy, 1.0)
```

## Known Issues

- TSDF volume fusion crashes on mobile (compute shader limitations)
- Depth resolution limited to 160x90 by ARCore
- Edge detection may show artifacts on low-texture surfaces

## Future Improvements

- [ ] Temporal depth filtering for smoother visualization
- [ ] Multi-frame depth accumulation
- [ ] Mobile-optimized pseudo-TSDF using texture arrays
- [ ] Custom color schemes
- [ ] Depth-based occlusion for virtual objects

## Credits

Developed for volumetric AR visualization research.
Based on ARCore Environment Depth API and Unity AR Foundation.

## License

MIT License - See LICENSE file for details
