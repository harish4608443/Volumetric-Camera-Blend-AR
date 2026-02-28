# Spatial Coverage Toggle Button Setup Guide

## Overview
This guide shows you how to add a button to toggle spatial coverage (pink-white stripes + object detection) on and off.

**Initial State**: Camera feed only  
**After button press**: Spatial coverage + sphere generation enabled

---

## Step-by-Step Setup

### 1. Create the UI Button

1. **Right-click in Hierarchy** → `UI` → `Button`
   - This automatically creates:
     - Canvas (if not already present)
     - EventSystem (if not already present)
     - Button with Text child

2. **Rename the Button** (optional):
   - Select the Button in Hierarchy
   - Rename to: `SpatialCoverageToggleButton`

3. **Position the Button**:
   - Select the Button in Hierarchy
   - In Inspector → Rect Transform:
     - **Anchor Preset**: Bottom-Center (or your preference)
     - **Pos Y**: 50 (to lift it off bottom edge)
     - **Width**: 300
     - **Height**: 60

4. **Style the Button Text**:
   - Expand the Button in Hierarchy
   - Select the **Text** child
   - In Inspector → Text component:
     - **Text**: "Enable Spatial Coverage"
     - **Font Size**: 20
     - **Alignment**: Center + Middle
     - **Color**: White (or your preference)

### 2. Add the Toggle Script to AR Camera

1. **Select your AR Camera** in Hierarchy (usually named "Main Camera" or "AR Camera")

2. **Add Component**:
   - Click **Add Component** in Inspector
   - Type: `SpatialCoverageToggle`
   - Select it to add the script

3. **Assign the Button**:
   - In the **SpatialCoverageToggle** component:
     - Find the **Toggle Button** field
     - Drag your button from Hierarchy into this field
   
   *The other fields (TSDF Volume, TSDF Renderer, AR Overlay) will auto-find themselves.*

### 3. Configure Components to Start Disabled

To ensure spatial coverage starts OFF, you need to check the following components on your AR Camera:

#### Option A: Disable Components Manually (Recommended)

1. **Select AR Camera** in Hierarchy

2. **Find and DISABLE these components** (uncheck the box at the top-left of each component):
   - ☐ **TSDFVolumeAtlas**
   - ☐ **TSDFReliableRenderer** (or TSDFRayMarching)
   - ☐ **ARCombinedOverlay**

3. **Leave the SpatialCoverageToggle ENABLED** ✅

The toggle script will enable these components when the button is pressed.

#### Option B: Check Current State

Run the app and check the logs:
```
[TOGGLE] ✅ Initialization complete - spatial coverage starts DISABLED
```

If you see this, components are properly disabled.

---

## Testing

### Expected Behavior:

**1. App Starts:**
- ✅ Only camera feed visible
- ❌ No pink-white stripes
- ❌ No object detection / spheres
- Button says: **"Enable Spatial Coverage"**

**2. Press Button:**
- ✅ Pink-white stripes appear on unscanned areas
- ✅ Object detection starts
- ✅ Spheres appear on detected objects
- Button says: **"Disable Spatial Coverage"**

**3. Press Button Again:**
- Returns to camera-only view
- Button says: **"Enable Spatial Coverage"**

---

## Troubleshooting

### Button doesn't appear
- Check that Canvas is present in Hierarchy
- Check that Canvas → Render Mode is set to "Screen Space - Overlay"
- Make sure Button is a child of Canvas

### Button click doesn't work
- Check that EventSystem exists in Hierarchy
- Check that Button is assigned in SpatialCoverageToggle component
- Look for error logs in Unity Console

### Components still active at start
1. Select AR Camera
2. Manually disable the components listed in Step 3 above
3. Restart the app

### Nothing happens when button pressed
Check Unity Console for logs:
```
[TOGGLE] 🔘 Spatial coverage toggled: ON
[TOGGLE] TSDFVolumeAtlas: ENABLED
[TOGGLE] TSDFReliableRenderer: ENABLED
[TOGGLE] ARCombinedOverlay: ENABLED (stripes + detection)
```

If you don't see these logs, the script may not be finding the components.

### Components not auto-found
If auto-find fails, manually assign them:
1. Select AR Camera
2. In SpatialCoverageToggle component:
   - Drag TSDFVolumeAtlas component to **Tsdf Volume** field
   - Drag TSDFReliableRenderer (or TSDFRayMarching) to **Tsdf Renderer** or **Tsdf Ray Marching** field
   - Drag ARCombinedOverlay to **Ar Overlay** field

---

## Advanced: Programmatic Control

You can also control spatial coverage from other scripts:

```csharp
// Get reference to toggle
SpatialCoverageToggle toggle = Camera.main.GetComponent<SpatialCoverageToggle>();

// Enable spatial coverage
toggle.EnableSpatialCoverage();

// Disable spatial coverage
toggle.DisableSpatialCoverage();

// Check current state
bool isActive = toggle.spatialCoverageActive;
```

---

## Component Descriptions

### What each toggled component does:

1. **TSDFVolumeAtlas**
   - Integrates depth data into 3D volume
   - Tracks spatial coverage
   - Required for stripes and reconstruction

2. **TSDFReliableRenderer** (or TSDFRayMarching)
   - Renders the pink-white stripe visualization
   - Shows through regions with low confidence data
   - Shows what has been scanned vs unscanned

3. **ARCombinedOverlay**
   - Generates pink-white stripes on unscanned areas
   - Runs YOLO object detection
   - Creates spheres on detected objects
   - Main coordinator for all features

All three must be enabled for full spatial coverage functionality.

---

## Files Created

- `Assets/ObjectDetection/Scripts/SpatialCoverageToggle.cs` - Toggle script
- `Assets/ObjectDetection/Scripts/SpatialCoverageToggle.cs.meta` - Unity metadata
- `SPATIAL_COVERAGE_BUTTON_SETUP.md` - This guide

---

## Summary Checklist

- [ ] UI Button created and positioned
- [ ] Button text set to "Enable Spatial Coverage"
- [ ] SpatialCoverageToggle script added to AR Camera
- [ ] Button assigned in script's Toggle Button field
- [ ] TSDFVolumeAtlas, TSDFReliableRenderer, ARCombinedOverlay disabled on AR Camera
- [ ] App tested - starts with camera-only view
- [ ] Button press enables spatial coverage features
- [ ] Button press again disables features

Once all items are checked, your toggle button is ready!
