# Dual Camera Volumetric Blending for Unity

This project implements a dual camera system in Unity where a main RGB camera is blended with a volumetric rendering camera using alpha blending.

## Files Included

1. **DualCameraVolumetricBlend.cs** - Main script that manages both cameras and performs the blending
2. **VolumetricBlend.shader** - Custom shader for alpha blending the two camera outputs
3. **UNITY_SETUP_INSTRUCTIONS.txt** - Detailed step-by-step setup guide

## Quick Overview

### What It Does
- **Camera 1 (Main)**: Renders standard RGB scene
- **Camera 2 (Volumetric)**: Renders volumetric effects separately
- **Blending**: Uses Graphics.Blit with custom shader to blend both outputs with alpha blending
- **Result**: RGB + V combined into final image

### Key Features
- Real-time alpha adjustment via Inspector
- Automatic render texture management
- Resolution-independent rendering
- HDR support
- Cleanup on destroy

## Setup Summary

1. Import scripts into Unity project
2. Add `DualCameraVolumetricBlend.cs` to Main Camera
3. Create a second Camera for volumetric rendering
4. Assign shader and camera references
5. Configure layer masks (optional)
6. Adjust blend alpha to taste

## Technical Details

### Rendering Pipeline
```
Main Camera → RenderTexture (RGB)
Volumetric Camera → RenderTexture (RGBA)
↓
Graphics.Blit with VolumetricBlend.shader
↓
Final Output (RGB + V)
```

### Blend Formula
```glsl
finalColor.rgb = lerp(mainRGB, volumetricRGB, alpha * volumetricAlpha)
```

## For Complete Instructions

See **UNITY_SETUP_INSTRUCTIONS.txt** for detailed setup steps, troubleshooting, and optimization tips.

## Requirements

- Unity 2020.3 or newer
- Built-in Render Pipeline or URP
- No additional packages required

## License

Free to use and modify for any purpose.
