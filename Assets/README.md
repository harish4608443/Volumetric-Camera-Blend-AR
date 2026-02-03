# Volumetric Camera Blend - AR Foundation with TSDF Volume Fusion

This project implements real-time AR depth visualization and TSDF (Truncated Signed Distance Function) volume fusion for volumetric 3D reconstruction on Android devices.

## Features

### 1. Real-Time Depth Visualization (ARVolumetricBlend.cs)
- RGB color gradients based on distance (red=close, blue=far)
- Surface normal calculation from depth gradients
- Edge detection on depth discontinuities
- Works on OpenGLES3 and Vulkan

### 2. TSDF Volume Fusion (TSDFVolumeFusion.cs) **[Requires Vulkan]**
- True volumetric 3D reconstruction
- 256×64×256 voxel grid with temporal accumulation
- Ray marching visualization
- GPU-accelerated compute shaders
- **Requires Vulkan graphics API** (OpenGLES3 not supported)

## Requirements

- Unity 2020.3.6f1 or newer
- AR Foundation 4.1.13+
- ARCore XR Plugin 4.1.13+
- Android API Level 24+
- ARCore-compatible device
- **Vulkan graphics API enabled** (for TSDF)
- Samsung S24 or similar (with depth API support)

## License

Free to use and modify for any purpose.
