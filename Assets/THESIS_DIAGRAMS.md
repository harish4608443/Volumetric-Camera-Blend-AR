# Thesis Diagrams — Volumetric AR Object Detection System
## Draw.io XML + ASCII Reference for All Diagrams

---

## HOW TO USE IN DRAW.IO
1. Go to **https://app.diagrams.net**
2. Click **Extras → Edit Diagram** (or press Ctrl+Shift+X)
3. Paste the XML for each diagram and click **OK**
4. Arrange / style as needed

---

---

# DIAGRAM 1 — Full System Architecture (Top-Level Overview)

**Purpose:** Shows all major components and how they connect at a high level.

```
┌────────────────────────────────────────────────────────────────────────────┐
│                     ANDROID DEVICE (AR-capable)                            │
│                                                                            │
│  ┌──────────────┐    ┌──────────────────────────────────────────────────┐  │
│  │  HARDWARE    │    │            UNITY AR APPLICATION                  │  │
│  │              │    │                                                  │  │
│  │  RGB Camera  ├───▶│  ARCameraManager  ──▶  ARCameraBackground        │  │
│  │  ToF / LiDAR ├───▶│  AROcclusionManager ──▶ Depth Texture (float32)  │  │
│  │  IMU + VIO   ├───▶│  ARSession / ARSessionOrigin (6-DOF Pose)        │  │
│  └──────────────┘    │                                                  │  │
│                      │   ┌────────────────┐  ┌──────────────────────┐  │  │
│                      │   │  TSDFVolumeAtlas│  │  ARCombinedOverlay   │  │  │
│                      │   │  (background)   │  │  (AR Camera)         │  │  │
│                      │   │  every 10 frames│  │  OnRenderImage()     │  │  │
│                      │   └───────┬─────────┘  └──────────┬───────────┘  │  │
│                      │           │                        │              │  │
│                      │           ▼                        ▼              │  │
│                      │   TSDF Weight Atlas        Final Composited       │  │
│                      │   (GPU RenderTexture)       Output Frame          │  │
│                      └──────────────────────────────────────────────────┘  │
│                                                                            │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  USER sees: Camera feed | Pink stripes on unscanned areas | Spheres  │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────────┘
```

### Draw.io XML — Diagram 1

```xml
<mxGraphModel>
  <root>
    <mxCell id="0"/>
    <mxCell id="1" parent="0"/>

    <!-- DEVICE BOUNDARY -->
    <mxCell id="2" value="Android AR Device" style="rounded=1;whiteSpace=wrap;fillColor=#f5f5f5;strokeColor=#666666;fontColor=#333333;fontStyle=1;fontSize=14;" vertex="1" parent="1">
      <mxGeometry x="40" y="40" width="920" height="560" as="geometry"/>
    </mxCell>

    <!-- HARDWARE GROUP -->
    <mxCell id="3" value="HARDWARE" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="80" y="120" width="180" height="40" as="geometry"/>
    </mxCell>
    <mxCell id="4" value="RGB Camera" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="80" y="180" width="180" height="40" as="geometry"/>
    </mxCell>
    <mxCell id="5" value="ToF / LiDAR Depth Sensor" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="80" y="240" width="180" height="40" as="geometry"/>
    </mxCell>
    <mxCell id="6" value="IMU + VIO (6-DOF Pose)" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="80" y="300" width="180" height="40" as="geometry"/>
    </mxCell>

    <!-- AR FOUNDATION -->
    <mxCell id="10" value="AR Foundation (ARCore)" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="320" y="120" width="200" height="40" as="geometry"/>
    </mxCell>
    <mxCell id="11" value="ARCameraManager&#xa;(RGB frames)" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="320" y="180" width="200" height="40" as="geometry"/>
    </mxCell>
    <mxCell id="12" value="AROcclusionManager&#xa;(Depth + Confidence)" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="320" y="240" width="200" height="40" as="geometry"/>
    </mxCell>
    <mxCell id="13" value="ARSession / ARSessionOrigin&#xa;(6-DOF Camera Pose)" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="320" y="300" width="200" height="40" as="geometry"/>
    </mxCell>

    <!-- TSDF -->
    <mxCell id="20" value="TSDFVolumeAtlas&#xa;Background Integration&#xa;(every 10th frame)" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="580" y="180" width="200" height="80" as="geometry"/>
    </mxCell>
    <mxCell id="21" value="TSDF Weight Atlas&#xa;(GPU RenderTexture&#xa;128³ voxels, 15cm)" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="580" y="310" width="200" height="60" as="geometry"/>
    </mxCell>

    <!-- COMBINED OVERLAY -->
    <mxCell id="30" value="ARCombinedOverlay&#xa;OnRenderImage Pipeline&#xa;(every frame)" style="rounded=1;fillColor=#f8cecc;strokeColor=#b85450;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="580" y="420" width="200" height="80" as="geometry"/>
    </mxCell>

    <!-- OUTPUT -->
    <mxCell id="40" value="Final Display&#xa;Camera Feed + Stripes + 3D Spheres" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="820" y="310" width="100" height="80" as="geometry"/>
    </mxCell>

    <!-- EDGES -->
    <mxCell id="50" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="4" target="11" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="51" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="5" target="12" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="52" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="6" target="13" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="53" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="12" target="20" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="54" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="13" target="20" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="55" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="20" target="21" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="56" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="21" target="30" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="57" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="11" target="30" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="58" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="30" target="40" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
  </root>
</mxGraphModel>
```

---

---

# DIAGRAM 2 — Per-Frame Processing Pipeline (Detailed Flow)

**Purpose:** Shows exactly what happens each frame in sequence — the core contribution of the system.

```
  EVERY FRAME
  ┌────────────────────────────────────────────────────────────────┐
  │  ARCameraBackground renders RGB to screen                       │
  └───────────────────────────┬────────────────────────────────────┘
                              │ src (RenderTexture)
                              ▼
  ┌────────────────────────────────────────────────────────────────┐
  │  OnRenderImage() — ARCombinedOverlay                            │
  │                                                                 │
  │  STEP 1: YOLO Detection (every 7th frame)                       │
  │  ┌──────────────────────────────────────────────────────────┐   │
  │  │ Copy clean frame → resize to 640×640 → NNHandler         │   │
  │  │ YOLOv8s inference (Barracuda) → decode ResultBox list    │   │
  │  │ min confidence 60% → lastDetections[]                    │   │
  │  └──────────────────────────────────────────────────────────┘   │
  │                              │                                   │
  │                              ▼ detections (screen bounding boxes)│
  │  STEP 1.5: Sphere Compositing                                    │
  │  ┌──────────────────────────────────────────────────────────┐   │
  │  │ DetectionSphereManager.CompositeSpheres()                 │   │
  │  │ Dedicated SphereCamera renders sphere layer → RT         │   │
  │  │ Composite: GREEN sphere pixels → dark blue in output     │   │
  │  └──────────────────────────────────────────────────────────┘   │
  │                              │                                   │
  │                              ▼ frame + sphere layer              │
  │  STEP 2: Stripe Overlay (ApplyStripeOverlay)                     │
  │  ┌──────────────────────────────────────────────────────────┐   │
  │  │ RenderCoverageMaskToRT() → GPU shader reads TSDF weights  │   │
  │  │ Weight ≥ 5.0 → scanned (mask = white) → show camera feed │   │
  │  │ Weight < 5.0 → unscanned (mask = black) → show stripes   │   │
  │  │ Shader: PointCloudComposite — lerps camera ↔ stripe      │   │
  │  └──────────────────────────────────────────────────────────┘   │
  │                              │                                   │
  │                              ▼                                   │
  │  STEP 3: Bounding Box Labels (optional, skipBoxVisualization)    │
  │                              │                                   │
  │                              ▼                                   │
  │  Graphics.Blit(current, dest) → screen                           │
  └────────────────────────────────────────────────────────────────┘

  PARALLEL (every 10th frame, LateUpdate)
  ┌────────────────────────────────────────────────────────────────┐
  │  TSDFVolumeAtlas.IntegrateDepth()                               │
  │  depth texture + camera matrices → integration shader           │
  │  Graphics.Blit → tsdfAtlasTemp → swap ping-pong buffers        │
  └────────────────────────────────────────────────────────────────┘
```

### Draw.io XML — Diagram 2

```xml
<mxGraphModel>
  <root>
    <mxCell id="0"/><mxCell id="1" parent="0"/>

    <!-- TITLE -->
    <mxCell id="100" value="Per-Frame Processing Pipeline" style="text;fontStyle=1;fontSize=16;" vertex="1" parent="1">
      <mxGeometry x="200" y="20" width="400" height="30" as="geometry"/>
    </mxCell>

    <!-- INPUT -->
    <mxCell id="101" value="RGB Camera Frame (src RenderTexture)&#xa;ARCameraBackground renders to screen" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="200" y="60" width="380" height="50" as="geometry"/>
    </mxCell>

    <!-- ONRENDERIMAGE BOX -->
    <mxCell id="102" value="OnRenderImage() — ARCombinedOverlay" style="rounded=1;fillColor=#f5f5f5;strokeColor=#555;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="120" y="140" width="540" height="40" as="geometry"/>
    </mxCell>

    <!-- STEP 1 YOLO -->
    <mxCell id="110" value="STEP 1 — YOLO Detection&#xa;(runs every 7th frame only)" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="120" y="210" width="240" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="111" value="Copy frame → 640×640&#xa;YOLOv8s (Barracuda / Unity.Barracuda)&#xa;Decode boxes, NMS, min conf 60%&#xa;→ lastDetections[]" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="120" y="280" width="240" height="80" as="geometry"/>
    </mxCell>

    <!-- STEP 1.5 SPHERES -->
    <mxCell id="120" value="STEP 1.5 — Sphere Compositing" style="rounded=1;fillColor=#f8cecc;strokeColor=#b85450;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="420" y="210" width="240" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="121" value="DetectionSphereManager&#xa;SphereCamera → RenderTexture&#xa;Depth: ARKit/ARCore + averaging&#xa;GREEN sphere → dark blue composite" style="rounded=1;fillColor=#f8cecc;strokeColor=#b85450;" vertex="1" parent="1">
      <mxGeometry x="420" y="280" width="240" height="80" as="geometry"/>
    </mxCell>

    <!-- STEP 2 STRIPES -->
    <mxCell id="130" value="STEP 2 — TSDF Coverage Stripe Overlay" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="120" y="400" width="540" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="131" value="GPU shader reads TSDF weight atlas&#xa;weight ≥ 5.0 → scanned → show camera&#xa;weight &lt; 5.0 → unscanned → pink/white stripes&#xa;PointCloudComposite shader lerps camera ↔ stripe" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="120" y="470" width="540" height="80" as="geometry"/>
    </mxCell>

    <!-- STEP 3 OUTPUT -->
    <mxCell id="140" value="STEP 3 — Final Output: Graphics.Blit → Screen" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="120" y="590" width="540" height="50" as="geometry"/>
    </mxCell>

    <!-- PARALLEL TSDF -->
    <mxCell id="150" value="PARALLEL THREAD (LateUpdate, every 10th frame)&#xa;TSDFVolumeAtlas.IntegrateDepth()&#xa;Depth texture + camera matrices → GPU integration shader&#xa;Graphics.Blit → ping-pong atlas swap&#xa;128³ voxels @ 15cm, ±9.6m range" style="rounded=1;fillColor=#ffe6cc;strokeColor=#d79b00;" vertex="1" parent="1">
      <mxGeometry x="720" y="280" width="260" height="120" as="geometry"/>
    </mxCell>

    <!-- EDGES -->
    <mxCell id="200" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="101" target="102" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="201" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="102" target="110" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="202" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="102" target="120" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="203" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="110" target="111" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="204" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="120" target="121" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="205" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="111" target="130" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="206" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="121" target="130" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="207" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="130" target="131" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="208" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="131" target="140" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="209" style="edgeStyle=orthogonalEdgeStyle;dashed=1;" edge="1" source="150" target="130" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
  </root>
</mxGraphModel>
```

---

---

# DIAGRAM 3 — TSDF Volume Integration Pipeline

**Purpose:** Shows how depth frames are fused into the 3D TSDF volume atlas, including the 2D atlas packing layout.

```
  INPUT: AROcclusionManager.environmentDepthTexture (float, metres)
  INPUT: cam.worldToCameraMatrix (4×4), cam.projectionMatrix (4×4)
  INPUT: volumeOrigin (world-space corner), voxelSize = 0.15m

         ┌──────────────────────────────────────────────────┐
         │   TSDFVolumeIntegration2D.shader (GPU pass)       │
         │                                                   │
         │   For each voxel in atlas (one fragment = one voxel)│
         │   1. Map atlas UV → (x, y, z) voxel index         │
         │   2. Voxel world pos = volumeOrigin + (x,y,z)*voxelSize │
         │   3. Project world pos → depth camera UV          │
         │   4. Sample depth texture at that UV              │
         │   5. Compute SDF = measured_depth − voxel_depth   │
         │   6. Clamp: if |SDF| > truncDist → skip           │
         │   7. Weighted running average:                    │
         │      newTSDF = (oldTSDF*w + SDF) / (w+1)         │
         │      newWeight = w + 1                            │
         └────────────────────┬─────────────────────────────┘
                              │
               ┌──────────────▼──────────────┐
               │  Ping-Pong Swap             │
               │  tsdfAtlasTemp → tsdfAtlas  │
               │  weightAtlasTemp → weightAtlas │
               └──────────────┬──────────────┘
                              │
  OUTPUT: tsdfAtlas (RenderTexture RFloat)  — TSDF values per voxel
          weightAtlas (RenderTexture RFloat) — observation count

  ATLAS LAYOUT (128×128×128 packed into 2D):
  ┌────────────────────────────────────────┐
  │ slicesPerRow = ceil(√128) = 12         │
  │ atlasWidth  = 128 × 12 = 1536 px       │
  │ atlasHeight = 128 × ceil(128/12) = 1408│
  │                                        │
  │  [z=0][z=1][z=2]...[z=11]             │
  │  [z=12][z=13]...[z=23]                 │
  │  ...                                   │
  │  Each tile = 128×128 px (X,Y plane)    │
  └────────────────────────────────────────┘

  VOLUME COVERAGE:
  ┌─────────────────────────────────────────────┐
  │  Centered on camera startup position         │
  │  X: camera.x ± 9.6 m                        │
  │  Y: camera.y ± 9.6 m                        │
  │  Z: camera.z ± 9.6 m                        │
  │  Total: 19.2 m³ cube, suitable for outdoors  │
  │  maxDepth = 15 m / truncDist = 0.35 m        │
  └─────────────────────────────────────────────┘
```

### Draw.io XML — Diagram 3

```xml
<mxGraphModel>
  <root>
    <mxCell id="0"/><mxCell id="1" parent="0"/>

    <!-- TITLE -->
    <mxCell id="300" value="TSDF Volume Integration Pipeline" style="text;fontStyle=1;fontSize=16;" vertex="1" parent="1">
      <mxGeometry x="180" y="10" width="440" height="30" as="geometry"/>
    </mxCell>

    <!-- INPUT -->
    <mxCell id="301" value="INPUTS (every 10th frame)" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="40" y="60" width="700" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="302" value="Depth Texture&#xa;(float32, metres)&#xa;AROcclusionManager" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="40" y="110" width="160" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="303" value="Camera Matrices&#xa;worldToCameraMatrix&#xa;projectionMatrix" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="220" y="110" width="160" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="304" value="Volume Parameters&#xa;origin = camera − 9.6m&#xa;voxelSize = 0.15m&#xa;res = 128³" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="400" y="110" width="160" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="305" value="Previous TSDF + Weight&#xa;(ping-pong atlases)" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="580" y="110" width="160" height="70" as="geometry"/>
    </mxCell>

    <!-- GPU SHADER -->
    <mxCell id="310" value="TSDFVolumeIntegration2D.shader (GPU — one fragment per voxel)" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="40" y="220" width="700" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="311" value="1. Atlas UV → (x, y, z) voxel index" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="40" y="270" width="300" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="312" value="2. World pos = volumeOrigin + (x,y,z) × voxelSize" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="40" y="320" width="300" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="313" value="3. Project world pos → depth camera UV" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="40" y="370" width="300" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="314" value="4. Sample depth texture → measured depth" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="40" y="420" width="300" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="315" value="5. SDF = measured_depth − voxel_depth" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="360" y="270" width="300" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="316" value="6. |SDF| > truncDist (0.35m)? → discard voxel" style="rounded=1;fillColor=#f8cecc;strokeColor=#b85450;" vertex="1" parent="1">
      <mxGeometry x="360" y="320" width="300" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="317" value="7. Weighted average:&#xa;newTSDF = (oldTSDF×w + SDF) / (w+1)&#xa;newWeight = w + 1" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="360" y="370" width="300" height="60" as="geometry"/>
    </mxCell>

    <!-- PING-PONG -->
    <mxCell id="320" value="Ping-Pong Swap&#xa;tsdfAtlasTemp → tsdfAtlas&#xa;weightAtlasTemp → weightAtlas" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;" vertex="1" parent="1">
      <mxGeometry x="240" y="490" width="300" height="60" as="geometry"/>
    </mxCell>

    <!-- OUTPUTS -->
    <mxCell id="330" value="OUTPUT: tsdfAtlas (RFloat)&#xa;TSDF signed distance per voxel" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="40" y="590" width="300" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="331" value="OUTPUT: weightAtlas (RFloat)&#xa;Observation count per voxel&#xa;weight ≥ 5 = scanned area" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="360" y="590" width="300" height="50" as="geometry"/>
    </mxCell>

    <!-- ATLAS LAYOUT -->
    <mxCell id="340" value="2D Atlas Layout&#xa;slicesPerRow = 12&#xa;1536 × 1408 px&#xa;Each tile = 128×128 (one Z-slice)" style="rounded=1;fillColor=#ffe6cc;strokeColor=#d79b00;" vertex="1" parent="1">
      <mxGeometry x="760" y="270" width="200" height="100" as="geometry"/>
    </mxCell>

    <!-- EDGES -->
    <mxCell id="400" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="302" target="310" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="401" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="303" target="310" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="402" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="304" target="310" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="403" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="305" target="310" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="404" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="310" target="311" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="405" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="311" target="312" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="406" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="312" target="313" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="407" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="313" target="314" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="408" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="310" target="315" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="409" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="315" target="316" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="410" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="316" target="317" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="411" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="314" target="320" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="412" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="317" target="320" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="413" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="320" target="330" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="414" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="320" target="331" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
  </root>
</mxGraphModel>
```

---

---

# DIAGRAM 4 — YOLOv8 Detection + 3D Sphere Placement Pipeline

**Purpose:** Shows the journey from raw camera frame to a placed 3D AR sphere in world space.

```
  Camera Frame (RGB)
       │
       ▼
  ┌────────────────────────────────────────┐
  │  Frame downscaled 0.25×                │
  │  (quarter resolution = 16× speedup)    │
  └───────────────┬────────────────────────┘
                  │
                  ▼ every 7th frame
  ┌────────────────────────────────────────┐
  │  YOLOv8s ONNX (Unity Barracuda)        │
  │  Input: 640×640 RGB                    │
  │  Output: bounding boxes + class + conf │
  │  Min confidence: 60%                   │
  │  Output: ResultBox[] lastDetections    │
  └───────────────┬────────────────────────┘
                  │
                  ▼
  ┌────────────────────────────────────────┐
  │  DetectionSphereManager                │
  │                                        │
  │  For each ResultBox:                   │
  │    1. Compute detection centre (u,v)   │
  │    2. Multi-sample depth (3×3 grid,    │
  │       depthSampleRadius=1, averaged)   │
  │    3. ARRaycastManager.Raycast()       │
  │       → AR Plane hit? use hit point    │
  │    4. If raycast fails:                │
  │       use AROcclusionManager depth     │
  │    5. If depth still 0:                │
  │       use fallback depth 1.5m          │
  │    6. Camera.ScreenToWorldPoint()      │
  │       → 3D world position              │
  └───────────────┬────────────────────────┘
                  │ world position + radius
                  ▼
  ┌────────────────────────────────────────┐
  │  Sphere Instance Management            │
  │                                        │
  │  Re-detection within existing sphere?  │
  │    YES → lerp position (smooth=0.5)    │
  │    NO  → instantiate new sphere on     │
  │           layer 8 (SPHERE_LAYER)       │
  │                                        │
  │  radius = max(bbox.w, bbox.h) × factor │
  │  Persistent (usePersistentSpheres)     │
  │  CoverageSphereInfo component attached │
  └───────────────┬────────────────────────┘
                  │
                  ▼
  ┌────────────────────────────────────────┐
  │  SphereCamera (Layer 8 only)           │
  │  Renders sphere layer → RenderTexture  │
  │  CompositeSpheres() blends into frame  │
  │  GREEN → dark blue in final output     │
  └────────────────────────────────────────┘
```

### Draw.io XML — Diagram 4

```xml
<mxGraphModel>
  <root>
    <mxCell id="0"/><mxCell id="1" parent="0"/>

    <!-- TITLE -->
    <mxCell id="500" value="YOLOv8 Detection + 3D Sphere Placement" style="text;fontStyle=1;fontSize=16;" vertex="1" parent="1">
      <mxGeometry x="160" y="10" width="500" height="30" as="geometry"/>
    </mxCell>

    <!-- CAMERA FRAME -->
    <mxCell id="501" value="Camera Frame (RGB, full resolution)&#xa;ARCameraManager → ARCameraBackground" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="200" y="60" width="360" height="50" as="geometry"/>
    </mxCell>

    <!-- DOWNSCALE -->
    <mxCell id="502" value="Downscale 0.25× (quarter res)&#xa;16× speedup for mobile inference" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="200" y="140" width="360" height="50" as="geometry"/>
    </mxCell>

    <!-- YOLO -->
    <mxCell id="503" value="YOLOv8s ONNX Inference (every 7th frame)&#xa;Unity Barracuda, Input: 640×640 RGB&#xa;Output: bounding boxes, class labels, confidence" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="200" y="220" width="360" height="70" as="geometry"/>
    </mxCell>

    <!-- FILTER -->
    <mxCell id="504" value="Confidence Filter: min 60%&#xa;Non-Max Suppression&#xa;→ ResultBox[] lastDetections" style="rounded=1;fillColor=#f8cecc;strokeColor=#b85450;" vertex="1" parent="1">
      <mxGeometry x="200" y="320" width="360" height="60" as="geometry"/>
    </mxCell>

    <!-- DEPTH CHAIN -->
    <mxCell id="510" value="4-Tier Depth Resolution Chain" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="40" y="420" width="680" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="511" value="TIER 1&#xa;ARRaycastManager&#xa;.Raycast() → AR Plane" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="40" y="470" width="140" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="512" value="TIER 2&#xa;AROcclusionManager&#xa;env depth texture" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="200" y="470" width="140" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="513" value="TIER 3&#xa;3×3 multi-sample&#xa;depth averaging" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="360" y="470" width="140" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="514" value="TIER 4&#xa;Fallback depth&#xa;1.5m constant" style="rounded=1;fillColor=#ffe6cc;strokeColor=#d79b00;" vertex="1" parent="1">
      <mxGeometry x="520" y="470" width="140" height="70" as="geometry"/>
    </mxCell>

    <!-- WORLD POSITION -->
    <mxCell id="520" value="Camera.ScreenToWorldPoint() → 3D world position" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;" vertex="1" parent="1">
      <mxGeometry x="200" y="580" width="360" height="40" as="geometry"/>
    </mxCell>

    <!-- SPHERE MANAGEMENT -->
    <mxCell id="530" value="Sphere Instance Management" style="rounded=1;fillColor=#f5f5f5;strokeColor=#666;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="40" y="660" width="340" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="531" value="Re-detection match?&#xa;YES → lerp position (factor 0.5)&#xa;NO → new sphere on Layer 8&#xa;radius = max(w,h) × scaleFactor&#xa;Persistent (no time-based deletion)" style="rounded=1;fillColor=#f5f5f5;strokeColor=#666;" vertex="1" parent="1">
      <mxGeometry x="40" y="710" width="340" height="90" as="geometry"/>
    </mxCell>

    <!-- SPHERE CAMERA -->
    <mxCell id="540" value="SphereCamera (Layer 8 only)&#xa;Renders spheres → RenderTexture&#xa;Composite: GREEN → dark blue" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;" vertex="1" parent="1">
      <mxGeometry x="420" y="710" width="300" height="90" as="geometry"/>
    </mxCell>

    <!-- EDGES -->
    <mxCell id="600" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="501" target="502" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="601" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="502" target="503" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="602" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="503" target="504" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="603" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="504" target="510" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="604" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="510" target="511" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="605" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="510" target="512" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="606" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="510" target="513" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="607" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="510" target="514" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="608" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="511" target="520" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="609" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="512" target="520" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="610" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="513" target="520" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="611" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="514" target="520" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="612" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="520" target="530" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="613" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="530" target="531" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="614" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="531" target="540" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
  </root>
</mxGraphModel>
```

---

---

# DIAGRAM 5 — Spatial Coverage Mask (Stripe Overlay Logic)

**Purpose:** Shows how the TSDF weight atlas drives the stripe/camera blend decision per pixel.

```
  EVERY FRAME (GPU path, no CPU readback)

  TSDF weightAtlas (RenderTexture)
  + Camera pose matrices (_InvViewMatrix, _InvProjMatrix)
  + volumeCenter, voxelSize, resolution
       │
       ▼
  ┌────────────────────────────────────────────────────────┐
  │  TSDFWeightCoverageMask.shader                          │
  │                                                         │
  │  For each output pixel (u,v):                           │
  │  1. Reconstruct world ray: (u,v) → clip → view → world │
  │  2. March ray through TSDF volume                       │
  │  3. Look up weight at each voxel on the ray            │
  │  4. maxWeight = max over all voxels on ray             │
  │  5. Output: white (1.0) if maxWeight ≥ minTSDFWeight   │
  │             black (0.0) if maxWeight < minTSDFWeight    │
  │                                                         │
  │  Result → coverageMaskRT (128×128 R8 RenderTexture)     │
  └────────────────────────┬───────────────────────────────┘
                           │ coverage mask
                           ▼
  ┌──────────────────┐   ┌──────────────────────────────────┐
  │  stripeTexture   │   │  camera feed (src)               │
  │  (pink/white     │   │  (RGB, full resolution)          │
  │   pattern)       │   │                                  │
  └────────┬─────────┘   └──────────────┬───────────────────┘
           │                            │
           └────────────┬───────────────┘
                        ▼
  ┌────────────────────────────────────────────────────────┐
  │  PointCloudComposite.shader                             │
  │                                                         │
  │  out = lerp(stripe, camera, mask)                       │
  │                                                         │
  │  mask = 1 (white, scanned) → show camera feed          │
  │  mask = 0 (black, unscanned) → show pink/white stripes │
  └────────────────────────┬───────────────────────────────┘
                           │
                           ▼ to OnRenderImage STEP 2 output
```

### Draw.io XML — Diagram 5

```xml
<mxGraphModel>
  <root>
    <mxCell id="0"/><mxCell id="1" parent="0"/>

    <!-- TITLE -->
    <mxCell id="700" value="Spatial Coverage Mask — Stripe Overlay Logic" style="text;fontStyle=1;fontSize=16;" vertex="1" parent="1">
      <mxGeometry x="140" y="10" width="520" height="30" as="geometry"/>
    </mxCell>

    <!-- INPUTS -->
    <mxCell id="701" value="weightAtlas (RFloat RT)&#xa;TSDF observation counts" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="40" y="60" width="200" height="60" as="geometry"/>
    </mxCell>
    <mxCell id="702" value="Camera Pose (per-frame)&#xa;InvViewMatrix&#xa;InvProjMatrix" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="260" y="60" width="200" height="60" as="geometry"/>
    </mxCell>
    <mxCell id="703" value="volumeCenter, voxelSize&#xa;resolution=128³&#xa;minTSDFWeight=5.0" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="480" y="60" width="200" height="60" as="geometry"/>
    </mxCell>

    <!-- GPU MASK SHADER -->
    <mxCell id="710" value="TSDFWeightCoverageMask.shader (GPU)" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="40" y="160" width="640" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="711" value="1. Pixel (u,v) → reconstruct world-space ray&#xa;2. Step along ray through TSDF volume&#xa;3. Sample weight atlas at each voxel&#xa;4. maxWeight = max over ray&#xa;5. Output: white if maxWeight ≥ 5.0, else black" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="40" y="210" width="640" height="90" as="geometry"/>
    </mxCell>

    <!-- MASK OUTPUT -->
    <mxCell id="720" value="coverageMaskRT (128×128, R8)&#xa;white = scanned, black = unscanned" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="180" y="340" width="360" height="50" as="geometry"/>
    </mxCell>

    <!-- STRIPE + CAMERA -->
    <mxCell id="730" value="stripeTexture&#xa;(pink/white pattern)" style="rounded=1;fillColor=#f8cecc;strokeColor=#b85450;" vertex="1" parent="1">
      <mxGeometry x="40" y="440" width="200" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="731" value="Camera Feed (src)&#xa;RGB full resolution" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="480" y="440" width="200" height="50" as="geometry"/>
    </mxCell>

    <!-- COMPOSITE SHADER -->
    <mxCell id="740" value="PointCloudComposite.shader&#xa;out = lerp(stripe, camera, mask)&#xa;mask=1 → camera | mask=0 → stripes" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="180" y="540" width="360" height="70" as="geometry"/>
    </mxCell>

    <!-- FINAL OUTPUT -->
    <mxCell id="750" value="Output to OnRenderImage STEP 2&#xa;Scanned areas = Camera | Unscanned = Stripes" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;" vertex="1" parent="1">
      <mxGeometry x="180" y="660" width="360" height="50" as="geometry"/>
    </mxCell>

    <!-- EDGES -->
    <mxCell id="800" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="701" target="710" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="801" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="702" target="710" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="802" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="703" target="710" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="803" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="710" target="711" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="804" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="711" target="720" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="805" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="720" target="740" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="806" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="730" target="740" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="807" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="731" target="740" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="808" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="740" target="750" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
  </root>
</mxGraphModel>
```

---

---

# DIAGRAM 6 — Unity Scene Hierarchy / Component Attachment

**Purpose:** Shows how the Unity GameObjects, components and camera setup are organised.

```
  AR Session Origin (root)
  ├── AR Session
  │
  ├── AR Camera [Main Camera]
  │   ├── Camera
  │   ├── ARCameraManager           — provides RGB frames
  │   ├── AROcclusionManager        — provides depth + confidence
  │   ├── ARCameraBackground        — renders RGB to screen
  │   ├── TSDFVolumeAtlas           — background depth fusion
  │   │     RequireComponent(Camera)
  │   ├── ARCombinedOverlay         — OnRenderImage composite
  │   │     RequireComponent(Camera)
  │   └── ScreenshotCapture         — 📷 button UI (runtime-created)
  │
  ├── AR Plane Manager (with PlaneMaterial)
  │
  ├── AR Raycast Manager
  │
  ├── AR Anchor Manager
  │
  └── DetectionSphereManager (separate GO)
        ├── SphereCamera  [Layer 8 only]  — auto-created at runtime
        └── SphereContainer               — parent for all sphere instances
              ├── Sphere_0  (Layer 8, CoverageSphereInfo)
              ├── Sphere_1  (Layer 8, CoverageSphereInfo)
              └── ...
```

### Draw.io XML — Diagram 6

```xml
<mxGraphModel>
  <root>
    <mxCell id="0"/><mxCell id="1" parent="0"/>

    <!-- TITLE -->
    <mxCell id="900" value="Unity Scene Hierarchy &amp; Component Attachment" style="text;fontStyle=1;fontSize=16;" vertex="1" parent="1">
      <mxGeometry x="140" y="10" width="560" height="30" as="geometry"/>
    </mxCell>

    <!-- AR SESSION ORIGIN -->
    <mxCell id="901" value="AR Session Origin (root)" style="rounded=1;fillColor=#f5f5f5;strokeColor=#555;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="40" y="60" width="220" height="40" as="geometry"/>
    </mxCell>

    <!-- AR SESSION -->
    <mxCell id="902" value="AR Session" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="40" y="130" width="160" height="30" as="geometry"/>
    </mxCell>

    <!-- AR CAMERA -->
    <mxCell id="910" value="AR Camera [Main Camera]" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="300" y="60" width="220" height="40" as="geometry"/>
    </mxCell>
    <mxCell id="911" value="Camera" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="300" y="130" width="220" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="912" value="ARCameraManager" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="300" y="175" width="220" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="913" value="AROcclusionManager" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="300" y="220" width="220" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="914" value="ARCameraBackground" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="300" y="265" width="220" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="915" value="TSDFVolumeAtlas&#xa;(RequireComponent: Camera)" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="300" y="310" width="220" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="916" value="ARCombinedOverlay&#xa;(RequireComponent: Camera)" style="rounded=1;fillColor=#f8cecc;strokeColor=#b85450;" vertex="1" parent="1">
      <mxGeometry x="300" y="375" width="220" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="917" value="ScreenshotCapture&#xa;(📷 UI, runtime-created)" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;" vertex="1" parent="1">
      <mxGeometry x="300" y="440" width="220" height="50" as="geometry"/>
    </mxCell>

    <!-- OTHER AR MANAGERS -->
    <mxCell id="920" value="AR Plane Manager" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="40" y="200" width="160" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="921" value="AR Raycast Manager" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="40" y="250" width="160" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="922" value="AR Anchor Manager" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="40" y="300" width="160" height="30" as="geometry"/>
    </mxCell>

    <!-- DETECTION SPHERE MANAGER -->
    <mxCell id="930" value="DetectionSphereManager GO" style="rounded=1;fillColor=#ffe6cc;strokeColor=#d79b00;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="580" y="60" width="240" height="40" as="geometry"/>
    </mxCell>
    <mxCell id="931" value="SphereCamera [Layer 8 only]&#xa;(auto-created at runtime)" style="rounded=1;fillColor=#ffe6cc;strokeColor=#d79b00;" vertex="1" parent="1">
      <mxGeometry x="580" y="130" width="240" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="932" value="SphereContainer&#xa;Sphere_0, Sphere_1 ... (Layer 8)&#xa;CoverageSphereInfo attached" style="rounded=1;fillColor=#ffe6cc;strokeColor=#d79b00;" vertex="1" parent="1">
      <mxGeometry x="580" y="200" width="240" height="70" as="geometry"/>
    </mxCell>

    <!-- EDGES -->
    <mxCell id="1000" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="901" target="902" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1001" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="901" target="910" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1002" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="901" target="920" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1003" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="901" target="930" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1004" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="910" target="911" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1005" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="911" target="912" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1006" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="912" target="913" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1007" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="913" target="914" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1008" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="914" target="915" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1009" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="915" target="916" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1010" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="916" target="917" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1011" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="930" target="931" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1012" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="931" target="932" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
  </root>
</mxGraphModel>
```

---

---

# DIAGRAM 7 — Data Flow + Memory Layout (State Diagram)

**Purpose:** Shows all persistent GPU and CPU state, how data flows between components over time.

```
  ┌─────────────────────────────────────────────────────────────────────┐
  │                    PERSISTENT GPU STATE                              │
  │                                                                      │
  │  tsdfAtlas      [1536×1408, RFloat]  — signed distances per voxel   │
  │  tsdfAtlasTemp  [1536×1408, RFloat]  — write-buffer (ping-pong)     │
  │  weightAtlas    [1536×1408, RFloat]  — observation counts            │
  │  weightAtlasTemp[1536×1408, RFloat]  — write-buffer (ping-pong)     │
  │  coverageMaskRT [128×128, R8]        — scanned/unscanned per pixel  │
  │  sphereRT       [screen res, ARGB]   — sphere-only rendered layer    │
  └──────────────────────┬──────────────────────────────────────────────┘
                         │
          ┌──────────────┼──────────────────┐
          ▼              ▼                  ▼
  ┌───────────┐  ┌───────────────┐  ┌───────────────┐
  │ TSDFVolumeAtlas│  │ARCombinedOverlay│  │DetectionSphere│
  │ LateUpdate()  │  │OnRenderImage() │  │Manager        │
  │ every 10th f  │  │ every frame    │  │ Start/OnDetect│
  └───────────┘  └───────────────┘  └───────────────┘
          │              │                  │
          ▼              ▼                  ▼
  ┌───────────────────────────────────────────────────┐
  │              CPU STATE                             │
  │                                                    │
  │  lastDetections[]   — ResultBox list from YOLO     │
  │  activeSpheres[]    — SphereInstance list          │
  │  frameCount         — TSDF integration counter     │
  │  detectionFrameCounter — YOLO frame counter        │
  │  volumeOrigin       — world-space anchor (set once)│
  └───────────────────────────────────────────────────┘
```

### Draw.io XML — Diagram 7

```xml
<mxGraphModel>
  <root>
    <mxCell id="0"/><mxCell id="1" parent="0"/>

    <!-- TITLE -->
    <mxCell id="1100" value="Data Flow and Memory Layout" style="text;fontStyle=1;fontSize=16;" vertex="1" parent="1">
      <mxGeometry x="200" y="10" width="400" height="30" as="geometry"/>
    </mxCell>

    <!-- GPU STATE -->
    <mxCell id="1101" value="PERSISTENT GPU STATE (RenderTextures)" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="40" y="60" width="760" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="1102" value="tsdfAtlas&#xa;1536×1408, RFloat&#xa;signed distances" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="40" y="110" width="140" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="1103" value="tsdfAtlasTemp&#xa;1536×1408, RFloat&#xa;write buffer" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="200" y="110" width="140" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="1104" value="weightAtlas&#xa;1536×1408, RFloat&#xa;obs. counts" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="360" y="110" width="140" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="1105" value="weightAtlasTemp&#xa;1536×1408, RFloat&#xa;write buffer" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="520" y="110" width="140" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="1106" value="coverageMaskRT&#xa;128×128, R8&#xa;scanned pixels" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="680" y="110" width="120" height="70" as="geometry"/>
    </mxCell>

    <!-- COMPONENTS -->
    <mxCell id="1110" value="TSDFVolumeAtlas&#xa;LateUpdate()&#xa;every 10th frame" style="rounded=1;fillColor=#ffe6cc;strokeColor=#d79b00;" vertex="1" parent="1">
      <mxGeometry x="40" y="240" width="200" height="60" as="geometry"/>
    </mxCell>
    <mxCell id="1111" value="ARCombinedOverlay&#xa;OnRenderImage()&#xa;every frame" style="rounded=1;fillColor=#f8cecc;strokeColor=#b85450;" vertex="1" parent="1">
      <mxGeometry x="300" y="240" width="200" height="60" as="geometry"/>
    </mxCell>
    <mxCell id="1112" value="DetectionSphereManager&#xa;OnDetect / LateUpdate" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="560" y="240" width="200" height="60" as="geometry"/>
    </mxCell>

    <!-- CPU STATE -->
    <mxCell id="1120" value="CPU STATE" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="40" y="360" width="720" height="30" as="geometry"/>
    </mxCell>
    <mxCell id="1121" value="lastDetections[]&#xa;ResultBox list from YOLO" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;" vertex="1" parent="1">
      <mxGeometry x="40" y="410" width="200" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="1122" value="activeSpheres[]&#xa;SphereInstance list" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;" vertex="1" parent="1">
      <mxGeometry x="260" y="410" width="200" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="1123" value="frameCount (TSDF)&#xa;detectionFrameCounter (YOLO)" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;" vertex="1" parent="1">
      <mxGeometry x="480" y="410" width="200" height="50" as="geometry"/>
    </mxCell>
    <mxCell id="1124" value="volumeOrigin&#xa;world-space corner (set once at Start)" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;" vertex="1" parent="1">
      <mxGeometry x="40" y="480" width="640" height="40" as="geometry"/>
    </mxCell>

    <!-- EDGES: GPU state to components -->
    <mxCell id="1200" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1102" target="1110" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1201" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1104" target="1110" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1202" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1104" target="1111" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1203" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1106" target="1111" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1204" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1110" target="1121" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1205" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1111" target="1121" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1206" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1112" target="1122" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
  </root>
</mxGraphModel>
```

---

---

# DIAGRAM 8 — Rendering Layers and Compositing Stack

**Purpose:** Shows how the final screen image is built from multiple rendering layers in order.

```
  ┌───────────────────────────────────────────────────────┐  LAYER STACK
  │                                                        │  (bottom to top)
  │  LAYER 0: AR Camera Background                         │
  │           ARCameraBackground component                 │
  │           Renders live RGB from device camera          │
  │                                                        │
  │  LAYER 1: Sphere Render Texture (Layer 8)              │
  │           Dedicated SphereCamera, isolated layer        │
  │           Sphere meshes rendered to RT separately       │
  │           CompositeSpheres() blends onto Layer 0        │
  │                                                        │
  │  LAYER 2: Stripe / Coverage Mask Composite             │
  │           PointCloudComposite shader                   │
  │           Uses coverageMaskRT as blend factor           │
  │           Unscanned pixels → pink/white pattern        │
  │           Scanned pixels → pass through camera          │
  │                                                        │
  │  LAYER 3: Detection Bounding Boxes (optional)          │
  │           CPU texture draw, disabled by default         │
  │           (skipBoxVisualization = true)                 │
  │                                                        │
  └───────────────────────────────────────────────────────┘
              │
              ▼
  ┌───────────────────────────────────────────────────────┐
  │  Screen Output: Graphics.Blit(current, dest)           │
  │  What user sees:                                       │
  │  • Camera feed where scanned (TSDF weight ≥ 5)        │
  │  • Pink/white stripes where not yet scanned            │
  │  • Dark blue spheres at detected object positions      │
  └───────────────────────────────────────────────────────┘
```

### Draw.io XML — Diagram 8

```xml
<mxGraphModel>
  <root>
    <mxCell id="0"/><mxCell id="1" parent="0"/>

    <!-- TITLE -->
    <mxCell id="1300" value="Rendering Layers and Compositing Stack" style="text;fontStyle=1;fontSize=16;" vertex="1" parent="1">
      <mxGeometry x="180" y="10" width="480" height="30" as="geometry"/>
    </mxCell>

    <!-- LAYERS bottom to top -->
    <mxCell id="1301" value="LAYER 0 — AR Camera Background&#xa;ARCameraBackground component&#xa;Live RGB from device camera sensor&#xa;(rendered first, base layer)" style="rounded=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="40" y="400" width="760" height="70" as="geometry"/>
    </mxCell>
    <mxCell id="1302" value="LAYER 1 — Sphere Render Texture (Layer 8 isolated)&#xa;SphereCamera renders ONLY Layer 8 spheres → RenderTexture&#xa;CompositeSpheres(): GREEN sphere pixels → dark blue output&#xa;(blended on top of camera background)" style="rounded=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
      <mxGeometry x="40" y="310" width="760" height="80" as="geometry"/>
    </mxCell>
    <mxCell id="1303" value="LAYER 2 — TSDF Coverage Stripe Composite (MAIN CONTRIBUTION)&#xa;PointCloudComposite shader: lerp(stripe, camera, coverageMask)&#xa;Scanned (weight ≥ 5) → pass-through camera | Unscanned → pink/white stripes&#xa;coverageMaskRT updated every frame via GPU shader (no CPU readback)" style="rounded=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
      <mxGeometry x="40" y="210" width="760" height="90" as="geometry"/>
    </mxCell>
    <mxCell id="1304" value="LAYER 3 — Detection Bounding Box Labels (optional)&#xa;CPU texture draw — DISABLED by default (skipBoxVisualization=true)&#xa;Enabled only for debugging" style="rounded=1;fillColor=#f5f5f5;strokeColor=#666;" vertex="1" parent="1">
      <mxGeometry x="40" y="130" width="760" height="70" as="geometry"/>
    </mxCell>

    <!-- FINAL OUTPUT -->
    <mxCell id="1310" value="SCREEN OUTPUT: Graphics.Blit(current, dest)&#xa;Camera feed (scanned) + Pink stripes (unscanned) + Dark blue spheres (detected objects)" style="rounded=1;fillColor=#e1d5e7;strokeColor=#9673a6;fontStyle=1;" vertex="1" parent="1">
      <mxGeometry x="40" y="520" width="760" height="60" as="geometry"/>
    </mxCell>

    <!-- ARROW LABEL -->
    <mxCell id="1320" value="Composited&#xa;top-to-bottom" style="text;fontStyle=2;" vertex="1" parent="1">
      <mxGeometry x="820" y="270" width="100" height="80" as="geometry"/>
    </mxCell>

    <!-- EDGES -->
    <mxCell id="1400" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1301" target="1310" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1401" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1302" target="1301" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1402" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1303" target="1302" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
    <mxCell id="1403" style="edgeStyle=orthogonalEdgeStyle;" edge="1" source="1304" target="1303" parent="1"><mxGeometry relative="1" as="geometry"/></mxCell>
  </root>
</mxGraphModel>
```

---

---

# QUICK REFERENCE — All Diagrams

| # | Diagram | Best used in thesis chapter |
|---|---------|----------------------------|
| 1 | Full System Architecture | Introduction / Chapter 3 overview |
| 2 | Per-Frame Processing Pipeline | Implementation / Chapter 4 |
| 3 | TSDF Volume Integration | Implementation / TSDF section |
| 4 | YOLOv8 Detection + Sphere Placement | Implementation / Detection section |
| 5 | Spatial Coverage Mask Logic | Implementation / Coverage section |
| 6 | Unity Scene Hierarchy | Implementation / Setup section |
| 7 | Data Flow & Memory Layout | Implementation / Architecture section |
| 8 | Rendering Layers & Compositing | Implementation / Rendering section |

---

## KEY DESIGN PARAMETERS (for diagram annotation reference)

| Parameter | Value | Reason |
|-----------|-------|--------|
| Voxel size | 0.15 m | Outdoor coverage: ±9.6 m radius |
| Volume resolution | 128³ voxels | ~Memory budget: 4 × 1536×1408 × 4B ≈ 34 MB |
| TSDF truncation dist | 0.35 m | ≥ 2 × voxelSize for robust fusion |
| Max depth | 15 m | Outdoor surfaces |
| YOLO model | YOLOv8s ONNX | Best speed/accuracy for mobile |
| YOLO interval | every 7th frame | ~4 Hz at 30 FPS |
| YOLO confidence | 60% min | Reduces floor false positives |
| Camera downscale | 0.25× | 16× speedup for YOLO preprocessing |
| TSDF integration | every 10th frame | ~3 integrations/sec at 30 FPS |
| Min TSDF weight | 5.0 | ~5–6 s of direct observation to clear stripes |
| Sphere layer | Layer 8 | Isolated rendering to prevent depth bleed |
| Depth averaging | 3×3 grid | Stabilises sphere placement at edges |
| Sphere placement | Persistent | No time-based deletion |
| Screenshot button | 120×120 px, bottom-right | Runtime-created, no prefab needed |
