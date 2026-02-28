# Stripe Overlay Troubleshooting Guide

## Problem: Stripes don't disappear / No camera feed appears

If you see pink-white stripes that never transition to camera feed, follow these steps:

---

## Fix 1: Assign TSDF Compute Shader (CRITICAL)

The TSDF-based spatial coverage system requires a compute shader to be assigned.

### Steps:
1. Open Unity Editor and load the project
2. In Hierarchy, find and select: **AR Camera** GameObject
3. Look in Inspector for **TSDF Volume Fusion** component
4. Find the field: **Volume Integration CS**
5. If it says "None (Compute Shader)", do the following:
   - In Project window, navigate to: `Assets/ObjectDetection/Scripts/`
   - Find: `TSDFVolumeIntegration.compute`
   - Drag it into the **Volume Integration CS** field
6. Save the scene (Ctrl+S / Cmd+S)
7. Rebuild APK and reinstall

### Verification:
After rebuilding, check logcat for:
```
✓ TSDF Volumes created successfully
✓ TSDF Volume Fusion initialized
[TSDF COVERAGE] Using TSDF weight volume for spatial coverage (IntelliCap method)
```

If you see `TSDF Volume Integration Compute Shader not assigned!`, the compute shader is still missing.

---

## Fix 2: Verify AROcclusionManager Configuration

The depth acquisition system needs ARCore depth API to be enabled.

### Steps:
1. In Hierarchy, find: **AR Session Origin** → **AR Camera**
2. Look in Inspector for **AR Occlusion Manager** component
3. Verify settings:
   - **Environment Depth Mode**: Should be set to **Fastest** or **Medium/Best**
   - **Occlusion Preference Mode**: Optional (High/Low Quality)
4. If AR Occlusion Manager is missing:
   - Select AR Camera GameObject
   - Click "Add Component" button
   - Search for: **AR Occlusion Manager**
   - Add it, then set Environment Depth Mode to **Fastest**
5. Save scene and rebuild

### Device Compatibility Note:
- Not all devices support ARCore Depth API
- Samsung Galaxy S24 Ultra **should support** depth API
- If depth consistently unavailable, test on different ARCore-enabled device

---

## Expected Behavior (After Fixes)

### Startup Phase (0-5 seconds):
- App shows **pink-white stripes over entire screen**
- This is normal - app waiting for ARCore to initialize depth data
- Log: `[STRIPE] FULL OVERLAY MODE ACTIVE - forceFullOverlay=true`

### After Depth Available (1-3 seconds typically):
- Stripes **automatically transition** to show camera feed
- Stripes appear only on **unscanned areas** (areas without depth data)
- Log: `✓ Depth data available - switched to scanning mode`

### Timeout Fallback (5+ seconds):
- If depth still unavailable after 5 seconds, app gives up waiting
- Automatically switches to show **full camera feed** (no more stripes)
- Log: `⚠ Depth data unavailable after 5.0s - disabling stripes to show camera feed`
- This prevents infinite stripe overlay when depth API not supported

---

## Debugging Commands

### Check if TSDF is initializing:
```bash
adb logcat -s Unity:D | grep -i "TSDF"
```
Expected output:
```
✓ TSDF Volumes created successfully
[TSDF COVERAGE] Using TSDF weight volume for spatial coverage
```

### Check depth acquisition:
```bash
adb logcat -s Unity:D | grep -i "depth\|occlusion"
```
Expected output:
```
[DEPTH INIT] ARCameraManager already present
[DEPTH INIT] Set depth mode to Fastest, current mode: Fastest
```

### Check stripe system status:
```bash
adb logcat -s Unity:D | grep "STRIPE"
```
Expected output (startup):
```
[STRIPE] FULL OVERLAY MODE ACTIVE - forceFullOverlay=true, showing stripes everywhere
```
Then within 3 seconds:
```
[STRIPE] ✓ Depth data available - switched to scanning mode
```

### Monitor all mask generation:
```bash
adb logcat -s Unity:D | grep "TSDF COVERAGE\|ACCUMULATIVE\|STRIPE"
```

---

## Three-Tier Coverage System Explained

The app uses a **fallback hierarchy** to determine which areas to show stripes:

### Priority 1: TSDF Weight Volume (IntelliCap Method)
- **What**: Queries TSDF volumetric reconstruction weight volume
- **How**: For each pixel, raycasts into TSDF volume and checks if voxels have been integrated
- **Requirement**: TSDFVolumeIntegration.compute must be assigned
- **Most Accurate**: Directly queries which areas have been 3D reconstructed

### Priority 2: AR Mesh Raycasting
- **What**: Raycasts against ARCore generated meshes
- **How**: Physics.Raycast from camera to check if AR mesh exists
- **Requirement**: ARMeshManager generating meshes
- **Fallback**: If TSDF fails

### Priority 3: Accumulative Depth (0.4m threshold)
- **What**: Marks pixels as "scanned" if depth < 0.4 meters detected
- **How**: Accumulates over time, once marked scanned = stays scanned
- **Requirement**: AROcclusionManager providing depth data
- **Last Resort**: If both TSDF and AR Mesh fail

**Current Issue**: All three tiers failing because:
1. TSDF: Compute shader not assigned → component disabled → no weight volume
2. AR Mesh: ARMeshManager generates 0 meshes
3. Accumulative: TryAcquireEnvironmentDepthCpuImage() returns false

---

## Quick Test: Disable Stripes Temporarily

If you want to verify object detection works without stripe issues:

### In ARCombinedOverlay.cs:
Find line with `public bool enableStripes = true;` and change to:
```csharp
public bool enableStripes = false;
```

This will show pure camera feed with object detection only (no stripes).

---

## Still Not Working?

### Check ARCore Version:
- Samsung S24 Ultra should have ARCore preinstalled
- Version should be 1.30+
- Update from Google Play Store if outdated

### Verify AR Foundation Setup:
1. Window → Package Manager
2. Find: AR Foundation (should be v4.2+)
3. Find: ARCore XR Plugin (should be v4.2+)
4. If missing or outdated, update/reinstall

### Test with Simplified Scene:
- Create new scene with just: ARSession, ARSessionOrigin, AR Camera, AROcclusionManager
- Attach simple script that calls TryAcquireEnvironmentDepthCpuImage()
- If this fails, device may not support depth API

---

## Contact / Support

If issues persist after following all steps:
1. Attach full logcat output from app startup to 10 seconds
2. Confirm TSDFVolumeIntegration.compute is assigned (screenshot)
3. Confirm AROcclusionManager settings (screenshot)
4. Specify device model and ARCore version

**Expected successful startup logs:**
```
=== ARCombinedOverlay Starting ===
Stripes: True, Detection: True, Spheres: True
[TSDF COVERAGE] Using TSDF weight volume for spatial coverage (IntelliCap method)
[DEPTH INIT] ARCameraManager already present
[DEPTH INIT] Set depth mode to Fastest, current mode: Fastest
[STRIPE] Generated stripe texture: True, size: 256x256
✓ TSDF Volumes created successfully
✓ TSDF Volume Fusion initialized
[STRIPE] FULL OVERLAY MODE ACTIVE - forceFullOverlay=true, showing stripes everywhere
[STRIPE] ✓ Depth data available - switched to scanning mode (stripes on unscanned areas)
```
