# AR Object Detection Integration

This integration combines ARCore Depth Lab point cloud visualization with YOLOv8 real-time object detection.

## Features

✅ **AR Camera Feed** - Live camera view from ARCore
✅ **Point Cloud Mesh** - Wireframe depth visualization (from DepthMeshWireframeRenderer)
✅ **Pink-White Stripes** - Visual overlay on unscanned/incomplete areas
✅ **Object Detection** - Real-time YOLO bounding boxes on detected objects

## Setup Instructions

### Option 1: Combined Overlay (Recommended)

1. **Remove old scripts** from AR Camera:
   - Remove `SimpleIncompleteOverlay` component if attached

2. **Add ARCombinedOverlay**:
   - Select your AR Camera GameObject
   - Add Component → `ARCombinedOverlay`

3. **Assign YOLO Model**:
   - In Inspector, find the "Yolo Model" field
   - Drag `Assets/ARRealismDemos/ObjectDetection/Models/yolov8s-seg.onnx` into this field

4. **Configure Settings** (optional):
   - **Enable Stripes**: Toggle pink-white stripe overlay
   - **Enable Object Detection**: Toggle YOLO detection
   - **Min Confidence**: Adjust detection threshold (0.3-0.7 recommended)
   - **Detection Interval**: Higher = better performance (10-15 recommended for mobile)
   - **Box Thickness**: Visual thickness of bounding boxes

### Option 2: Separate Components

If you want independent control:

1. **Stripe Overlay**:
   - Keep `SimpleIncompleteOverlay` on AR Camera

2. **Object Detection**:
   - Add `ARYOLODetector` component to AR Camera
   - Assign YOLO model file

**Note**: Both scripts use `OnRenderImage`, so execution order matters. Combined overlay handles this automatically.

## Performance Optimization

For smooth performance on mobile (Samsung S9 and similar):

- **Detection Interval**: 10-15 frames (updates every 0.3-0.5 seconds)
- **Mask Update Interval**: 10 frames
- **Min Confidence**: 0.5 (filters out weak detections)
- **Texture Sizes**: 128x128 for mask, 256x256 for stripes, 640x640 for YOLO

## File Structure

```
Assets/ARRealismDemos/ObjectDetection/
├── Models/
│   ├── yolov8s-seg.onnx          # YOLO model
│   └── classes.txt                # COCO class names
├── Scripts/
│   ├── ARCombinedOverlay.cs       # Combined overlay script
│   ├── ARYOLODetector.cs          # Standalone YOLO detector
│   ├── TextureTools.cs            # Texture utilities
│   └── NN/
│       ├── YOLOv8.cs              # YOLO inference engine
│       ├── YOLOv8OutputReader.cs  # Output parsing
│       ├── NNHandler.cs           # Model handler
│       ├── ResultBox.cs           # Detection result
│       ├── DuplicatesSupressor.cs # NMS implementation
│       └── IntersectionOverUnion.cs # IOU calculation
```

## Detected Object Classes

YOLOv8 can detect 80 COCO dataset classes including:
- People, vehicles, animals, furniture, electronics, sports equipment, etc.
- See `classes.txt` for full list

## API Usage

```csharp
// Get current overlay component
ARCombinedOverlay overlay = Camera.main.GetComponent<ARCombinedOverlay>();

// Get last detections
List<ResultBox> detections = overlay.GetLastDetections();

// Update confidence threshold at runtime
overlay.SetConfidenceThreshold(0.6f);

// Toggle features
overlay.enableStripes = true;
overlay.enableObjectDetection = true;
```

## Troubleshooting

**"YOLO model not assigned"**
- Make sure you dragged the .onnx file into the Inspector field

**"PointCloudComposite shader not found"**
- Shader should be at: `Assets/ARRealismDemos/PointCloud/Shaders/PointCloudComposite.shader`

**Slow performance**
- Increase `detectionInterval` to 15-20
- Increase `maskUpdateInterval` to 15-20
- Lower `minConfidence` to reduce false positives

**No bounding boxes visible**
- Check that objects are in the COCO dataset
- Lower `minConfidence` threshold
- Ensure `enableObjectDetection` is checked

**Stripes not visible**
- Ensure depth data is available (move camera around to scan)
- Check that `enableStripes` is checked
- Verify AROcclusionManager is on AR Camera

## Dependencies

- Unity AR Foundation 4.2.0+
- ARCore XR Plugin
- Unity Barracuda (for YOLO inference)
- ARCore-compatible Android device

## Credits

- ARCore Depth Lab: Google
- YOLOv8: Ultralytics
- Integration: MA-Renganathan
