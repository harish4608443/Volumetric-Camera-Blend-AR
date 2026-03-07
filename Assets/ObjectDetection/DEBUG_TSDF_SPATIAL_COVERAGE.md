# 🔍 Debugging TSDF Weight-Based Spatial Coverage

## **Quick Check: What Logs to Look For**

### ✅ **SUCCESS LOGS** (TSDF Weight Mode Working):

```
[STRIPE] 📊 SPATIAL COVERAGE METHOD: TSDF WEIGHTS (IntelliCap)
[STRIPE] ✅ TSDF WEIGHTS ENABLED: minWeight=1.0, atlas=True
[STRIPE] ✅ TSDF atlas found: TSDFVolumeAtlas

[STRIPE] ✅✅✅ FOUND TSDFVolumeAtlas on frame {X}
[STRIPE] 📊 TSDF WEIGHTS MODE ACTIVATED (IntelliCap method)

[STRIPE MASK] F20 ✅ Using TSDF WEIGHTS path (useTSDFWeights=True, atlas=True)
[STRIPE MASK TSDF] F30 GenerateMaskFromTSDFWeights() CALLED
[STRIPE MASK TSDF] ✅ Weight atlas found: 512x512
[STRIPE MASK TSDF] ✅✅✅ Created TSDF weight coverage material (GPU-accelerated)
[STRIPE MASK TSDF] F60 TSDF GPU: Cam=15.3% Threshold=1.0 Atlas=512x512
```

**If you see these:** 🎉 TSDF weight mode is working!

---

### ❌ **FAILURE LOGS** (Distance-Based Fallback):

#### **Problem 1: TSDF Atlas Not Found**
```
[STRIPE] ⚠️ TSDF atlas is NULL at startup - will retry for 60 frames
[STRIPE] ❌❌❌ TSDFVolumeAtlas NOT FOUND after 60 frames!
[STRIPE] ❌ Falling back to DEPTH THRESHOLD method
[STRIPE MASK] F20 ⚠️ Using DEPTH THRESHOLD fallback. Reason: useTSDFWeights=false (atlas not found or disabled)
```

**Solution:** TSDFVolumeAtlas component is missing or disabled.
1. Check AR Camera has `TSDFVolumeAtlas` component attached
2. Make sure it's **enabled** (checkbox checked)
3. Verify it's on the **same GameObject** as ARCombinedOverlay (or in scene)

---

#### **Problem 2: Shader Not Found**
```
[STRIPE MASK TSDF] ❌❌❌ TSDFWeightCoverageMask shader NOT FOUND!
[STRIPE MASK TSDF] ❌ Looking for: 'Hidden/TSDFWeightCoverageMask'
[STRIPE MASK TSDF] ❌ Make sure shader exists at: Assets/ObjectDetection/Shaders/TSDFWeightCoverageMask.shader
```

**Solution:** Shader file missing or wrong name.
1. Check file exists: `Assets/ObjectDetection/Shaders/TSDFWeightCoverageMask.shader`
2. Open shader file, verify first line: `Shader "Hidden/TSDFWeightCoverageMask"`
3. Right-click shader in Unity → Reimport

---

#### **Problem 3: Weight Atlas NULL**
```
[STRIPE MASK TSDF] ⚠️ Weight atlas is null (TSDF may not be initialized yet). Waiting...
```

**Solution:** TSDF volume is still initializing (normal for first 1-2 seconds).
- **Wait 2-3 seconds** after app starts
- TSDF initialization takes ~20-30 frames
- Should resolve automatically

---

#### **Problem 4: Depth Texture NULL**
```
[STRIPE MASK TSDF] ⚠️ Depth texture is null (ARCore depth not ready)!
```

**Solution:** ARCore depth sensor not initialized.
1. Check device supports ARCore depth (Pixel 4+, Galaxy S20+, etc.)
2. Verify `AROcclusionManager` is enabled on AR Camera
3. Check `Environment Depth Mode` is set to "Fastest" or "Best"

---

## **Step-by-Step Debugging Guide**

### **Step 1: Check Unity Inspector**

**On AR Camera GameObject:**
1. **TSDFVolumeAtlas** component:
   - ✅ Checkbox **ENABLED**
   - Volume Resolution: 128×128×128 (or similar)
   - Integration Interval: 30 frames
   
2. **ARCombinedOverlay** component:
   - ✅ **Enable Stripes** checked
   - ✅ **Use TSDF Weights** checked  ← **MUST BE CHECKED!**
   - Min TSDF Weight: 1.0
   - Tsdf Atlas: (Auto-found, can be empty)
   - Tsdf Weight Mask Shader: (Auto-found, can be empty)

3. **AROcclusionManager** component:
   - ✅ Component **ENABLED**
   - Environment Depth Mode: **Fastest** or **Best**

---

### **Step 2: Build & Run**

1. Build APK
2. Install on ARCore-compatible device
3. Run app
4. **IMMEDIATELY** open `adb logcat` to see logs:

```powershell
# In PowerShell (Windows):
adb logcat -s Unity

# Filter for relevant logs:
adb logcat -s Unity | Select-String "STRIPE|TSDF"
```

---

### **Step 3: Analyze Logs**

Look for logs at **frame 20, 50, 100**:

#### **Scenario A: TSDF Weights Working** ✅
```
F20: [STRIPE MASK] Using TSDF WEIGHTS path ✅
F30: [STRIPE MASK TSDF] GenerateMaskFromTSDFWeights() CALLED
F60: [STRIPE MASK TSDF] TSDF GPU: Cam=12.5%
```
**Result:** You're using TSDF weights! Scan surfaces to see coverage grow.

---

#### **Scenario B: TSDF Atlas Not Found** ❌
```
F20: [STRIPE MASK] Using DEPTH THRESHOLD fallback. Reason: tsdfAtlas=null
F50: [STRIPE MASK] Using DEPTH THRESHOLD fallback. Reason: tsdfAtlas=null
F60: [STRIPE] ❌❌❌ TSDFVolumeAtlas NOT FOUND after 60 frames!
```
**Result:** Falling back to distance-based.

**Fix:**
1. Open scene in Unity
2. Select AR Camera
3. Add Component → `TSDFVolumeAtlas` (if missing)
4. Enable the component (checkbox)
5. Rebuild and test

---

#### **Scenario C: Weight Atlas NULL** ⚠️
```
F20: [STRIPE MASK] Using TSDF WEIGHTS path ✅  ← Good!
F30: [STRIPE MASK TSDF] GenerateMaskFromTSDFWeights() CALLED  ← Good!
F30: [STRIPE MASK TSDF] ⚠️ Weight atlas is null (TSDF may not be initialized yet)  ← Waiting...
```
**Result:** TSDF is initializing, should work after 60 frames.

**Fix:** Wait 2-3 seconds, check logs again around frame 100-150.

---

### **Step 4: Verify TSDF Volume is Integrating**

Look for these logs every ~30 frames:
```
[TSDF] Frame 1 integrated
[TSDF] Frame 2 integrated
[TSDF] Frame 3 integrated
```

If **NOT** present:
- TSDF volume is disabled or not running
- Check `TSDFVolumeAtlas` component is **enabled**

---

## **Common Issues & Solutions**

### **Issue 1: "Using DEPTH THRESHOLD path" at frame 20+**

**Diagnosis:** Not using TSDF weights.

**Fixes:**
1. **Unity Inspector:**
   - AR Camera → ARCombinedOverlay → ✅ **Use TSDF Weights** (check this!)
   
2. **TSDF Component:**
   - AR Camera → Add Component → `TSDFVolumeAtlas`
   - Enable it (checkbox)
   
3. **Rebuild:**
   - Unity → File → Build and Run
   - Fresh install on device

---

### **Issue 2: "TSDFWeightCoverageMask shader NOT FOUND"**

**Diagnosis:** Shader missing or wrong path.

**Fixes:**
1. **Check file exists:**
   - `Assets/ObjectDetection/Shaders/TSDFWeightCoverageMask.shader`
   
2. **Verify shader name:**
   - Open shader file
   - First line MUST be: `Shader "Hidden/TSDFWeightCoverageMask"`
   
3. **Reimport:**
   - Right-click shader in Unity
   - Reimport
   
4. **Check build:**
   - Unity → Edit → Project Settings → Graphics
   - Ensure shader is included in build

---

### **Issue 3: Stripes never disappear (entire screen is stripes)**

**Diagnosis:** TSDF weight always below threshold.

**Fixes:**
1. **Lower threshold:**
   - AR Camera → ARCombinedOverlay → Min TSDF Weight: **0.5** (instead of 1.0)
   
2. **Scan more:**
   - Scan surfaces slowly and thoroughly
   - TSDF needs time to accumulate weight
   
3. **Check TSDF integration:**
   - Look for `[TSDF] Frame X integrated` logs
   - If missing, TSDF is not running

---

### **Issue 4: Stripes disappear immediately (entire screen is camera)**

**Diagnosis:** TSDF weight always above threshold (or not using TSDF at all).

**Fixes:**
1. **If using depth threshold fallback:**
   - You're NOT using TSDF weights
   - See Issue 1 above
   
2. **If using TSDF weights:**
   - Increase threshold: Min TSDF Weight: **2.0** (instead of 1.0)
   - May indicate TSDF is integrating too aggressively

---

## **Expected Visual Behavior**

### **Frame 1-60: Initialization**
- Pink-white stripes cover entire screen
- TSDF volume initializing
- No spatial coverage yet

### **Frame 60-150: First Scans**
- Start scanning surfaces (table, walls, etc.)
- Small regions of camera feed appear where you scan
- Stripes remain in unscanned areas
- Coverage is **persistent** (once scanned, stays scanned)

### **Frame 150+: Full Scanning**
- Camera feed regions grow as you scan
- Stripes only at edges and unscanned corners
- Coverage map visualizes what TSDF volume has reconstructed

### **Expected Coverage Growth:**
```
Frame 60:   5% camera feed,  95% stripes  (just started)
Frame 120: 20% camera feed,  80% stripes  (scanning)
Frame 300: 50% camera feed,  50% stripes  (half scanned)
Frame 600: 80% camera feed,  20% stripes  (mostly scanned)
```

---

## **Performance Monitoring**

### **Look for these logs every 60 frames:**
```
[STRIPE MASK TSDF] F60 TSDF GPU: Cam=12.5% Threshold=1.0 Atlas=512x512
[STRIPE MASK TSDF] F120 TSDF GPU: Cam=28.3% Threshold=1.0 Atlas=512x512
[STRIPE MASK TSDF] F180 TSDF GPU: Cam=45.7% Threshold=1.0 Atlas=512x512
```

**Cam%** should **gradually increase** as you scan surfaces.

If **Cam% stays at 0%** after 200+ frames:
- TSDF weight is always below threshold
- Lower `minTSDFWeight` to 0.5 or 0.3

If **Cam% jumps to 100%** immediately:
- You're likely using depth threshold fallback (not TSDF)
- Check logs for "DEPTH THRESHOLD path"

---

## **Test Checklist**

Before reporting issues, verify:

- [ ] `TSDFVolumeAtlas` component exists on AR Camera
- [ ] `TSDFVolumeAtlas` component is **enabled** (checkbox)
- [ ] `ARCombinedOverlay → Use TSDF Weights` is **checked**
- [ ] `TSDFWeightCoverageMask.shader` exists in project
- [ ] Logs show: `[STRIPE MASK] Using TSDF WEIGHTS path`
- [ ] Logs show: `[STRIPE MASK TSDF] GenerateMaskFromTSDFWeights() CALLED`
- [ ] Logs show TSDF integrating: `[TSDF] Frame X integrated`
- [ ] Device supports ARCore depth (Pixel 4+, Galaxy S20+, etc.)
- [ ] Built on device, NOT Unity Editor (Editor doesn't have real depth)

---

## **Compare: TSDF Weights vs Depth Threshold**

| Feature | TSDF Weights (IntelliCap) | Depth Threshold (Simple) |
|---------|--------------------------|-------------------------|
| **Log prefix** | `[STRIPE MASK TSDF]` | `[STRIPE MASK]` (no TSDF) |
| **Main log** | "Using TSDF WEIGHTS path" | "Using DEPTH THRESHOLD path" |
| **Coverage growth** | Gradual (5% → 80% over time) | Immediate (based on distance) |
| **Memory** | Persistent (once scanned, stays) | Real-time (changes each frame) |
| **Stripes behavior** | Disappear as you scan surfaces | Disappear when within 2m |
| **Performance** | GPU-accelerated (~0.5ms) | CPU per-pixel (~1-2ms) |

---

## **Still Having Issues?**

### **Provide these logs:**
```powershell
# Capture first 300 frames:
adb logcat -s Unity > logs.txt
# Then stop after ~10 seconds

# Check logs.txt for:
# - Frame 20, 50, 100 "STRIPE MASK" logs
# - Any "❌" or "⚠️" errors/warnings
# - "TSDF WEIGHTS" vs "DEPTH THRESHOLD"
```

### **Provide Unity Inspector screenshot:**
- AR Camera GameObject selected
- Show `TSDFVolumeAtlas` component (if present)
- Show `ARCombinedOverlay` component fields

---

Last Updated: 2026-02-28
