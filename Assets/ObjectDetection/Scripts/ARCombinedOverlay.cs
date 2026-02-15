using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Barracuda;
using NN;
using System.Collections.Generic;

/// <summary>
/// Combined AR visualization system:
/// 1. Pink-white stripes on unscanned areas
/// 2. YOLO object detection bounding boxes
/// Attach this to your AR Camera (replaces SimpleIncompleteOverlay + ARYOLODetector).
/// </summary>
[RequireComponent(typeof(Camera))]
public class ARCombinedOverlay : MonoBehaviour
{
    [Header("Stripe Overlay Settings")]
    public bool enableStripes = true;  // Show pink-white stripes on unscanned areas
    public bool showCameraUntilScanned = false;  // Show stripes immediately, not camera feed
    public bool enablePointCloudRendering = false;  // Disable red dots
    [Tooltip("Force full screen stripes at startup - disable this to show camera feed with depth-based stripes")]
    public bool forceFullOverlay = false;  // FALSE = depth-based scanning mode
    [Tooltip("Depth threshold in meters - show STRIPES on objects FARTHER than this (far/uncertain), show CAMERA on objects closer. Default 2.0m.")]
    public float incompleteThreshold = 2.0f;
    private int textureWidth = 128;
    private int textureHeight = 128;
    private int maskUpdateInterval = 3;  // Update every 3 frames for better performance
    
    [Header("YOLO Detection Settings")]
    public bool enableObjectDetection = true;
    [Tooltip("YOLOv8 ONNX model file")]
    public NNModel yoloModel;
    [Range(0.0f, 1f)]
    public float minConfidence = 0.15f;  // Working threshold - 15%
    public int detectionInterval = 7;  // Every 7th frame - WORKING SETTING
    public float minBoxSize = 15f;
     
    [Header("Performance Optimization")]
    [Tooltip("Downscale camera feed before processing. Lower = faster. 1.0=full, 0.5=half, 0.25=quarter")]
    [Range(0.25f, 1.0f)]
    public float cameraDownscale = 0.25f;  // Process quarter-resolution for 16x speedup
    
    [Tooltip("Skip bounding box visualization for better performance")]
    public bool skipBoxVisualization = true;  // Spheres only, no boxes
    
    [Header("Visualization")]
    public Color[] boxColors = new Color[] { 
        new Color(1f, 0f, 0f, 0.5f),      // Transparent red overlay
        new Color(0f, 1f, 0f, 0.5f),      // Transparent green overlay
        new Color(0f, 0f, 1f, 0.5f),      // Transparent blue overlay
        new Color(0f, 1f, 1f, 0.5f),      // Transparent cyan overlay
        new Color(1f, 0f, 1f, 0.5f),      // Transparent magenta overlay
        new Color(1f, 1f, 0f, 0.5f)       // Transparent yellow overlay
    };
    [Range(1, 20)]
    public int boxThickness = 12;

    [Header("Display & Labels")]
    [Tooltip("Flip overlays horizontally to correct mirrored camera feeds (e.g., front camera).")]
    public bool flipHorizontal = false;
    [Range(2, 8)]
    public int labelScale = 4;
    public bool showLabelBackground = true;
    [Range(0f, 1f)]
    public float labelBgOpacity = 0.6f;

    [Header("Debug")]
    [Tooltip("Enable detailed per-frame logs for debugging.")]
    public bool verboseLogs = true;  // ENABLED for debugging
    
    [Header("3D Sphere Visualization")]
    public bool enableSpheres = true;
    public GameObject spherePrefab;
    public float sphereLifetime = 2.0f;
    public float fallbackDepth = 1.5f; // Default depth when ARCore depth unavailable
    public bool useDepthFallback = true; // ENABLED - allow spheres with fallback depth (WORKING SETTING)
    private DetectionSphereManager sphereManager;
    
    // Stripe overlay components
    private Texture2D stripeTexture;
    private Texture2D maskTexture;
    private Material blitMaterial;
    private AROcclusionManager occlusionManager;
    private int stripeFrameCounter = 0;
    private bool hasDepthData = false;  // Track if we've received any depth data yet
    
    // YOLO detection components
    private NNHandler nn;
    private YOLOv8 yolo;
    private List<ResultBox> lastDetections = new List<ResultBox>();
    private int detectionFrameCounter = 0;
    private Texture2D detectionTexture;
    
    // Fade-away tracking
    private class DetectionWithTime
    {
        public ResultBox box;
        public float timestamp;
        public DetectionWithTime(ResultBox b, float t) { box = b; timestamp = t; }
    }
    private List<DetectionWithTime> timedDetections = new List<DetectionWithTime>();
    [Header("Fade Settings")]
    public float fadeAfterSeconds = 0.5f; // How long boxes stay visible after detection lost
    public float fadeDuration = 0.5f; // How long the fade animation takes

    void VLog(string msg)
    {
        if (verboseLogs) Debug.Log(msg);
    }
    
    void Start()
    {
        Debug.Log("=== ARCombinedOverlay Starting ===");
        Debug.Log($"Stripes: {enableStripes}, Detection: {enableObjectDetection}, Spheres: {enableSpheres}");
        
        // Disable point cloud rendering (red dots)
        if (!enablePointCloudRendering)
        {
            GameObject pointCloudObj = GameObject.Find("RawPointCloudBlender");
            if (pointCloudObj != null)
            {
                pointCloudObj.SetActive(false);
                Debug.Log("✓ Point cloud red dots DISABLED");
            }
        }
        
        // Disable point cloud UI toggle - search more aggressively
        Canvas[] allCanvases = GameObject.FindObjectsOfType<Canvas>(true);
        foreach (Canvas canvas in allCanvases)
        {
            string canvasName = canvas.gameObject.name.ToLower();
            if (canvasName.Contains("point") || canvasName.Contains("cloud") || canvasName.Contains("collider"))
            {
                canvas.gameObject.SetActive(false);
                Debug.Log($"✓ DISABLED Canvas: {canvas.gameObject.name}");
            }
        }
        
        // Also disable any Toggle UI components for point clouds
        UnityEngine.UI.Toggle[] toggles = GameObject.FindObjectsOfType<UnityEngine.UI.Toggle>(true);
        foreach (var toggle in toggles)
        {
            string toggleName = toggle.gameObject.name.ToLower();
            if (toggleName.Contains("point") || toggleName.Contains("cloud") || toggleName.Contains("collider"))
            {
                // Disable the toggle AND its parent container
                if (toggle.transform.parent != null)
                {
                    toggle.transform.parent.gameObject.SetActive(false);
                    Debug.Log($"✓ DISABLED Toggle parent: {toggle.transform.parent.name}");
                }
                else
                {
                    toggle.gameObject.SetActive(false);
                    Debug.Log($"✓ DISABLED Toggle: {toggle.gameObject.name}");
                }
            }
        }
        
        // Disable any UI Text labels containing "Point Cloud"
        UnityEngine.UI.Text[] allTexts = GameObject.FindObjectsOfType<UnityEngine.UI.Text>(true);
        foreach (var textComp in allTexts)
        {
            if (textComp.text.ToLower().Contains("point") || textComp.text.ToLower().Contains("cloud"))
            {
                // Disable the entire parent hierarchy
                Transform parent = textComp.transform.parent;
                if (parent != null)
                {
                    parent.gameObject.SetActive(false);
                    Debug.Log($"✓ DISABLED UI Text parent: {parent.name} (text: '{textComp.text}')");
                }
                else
                {
                    textComp.gameObject.SetActive(false);
                    Debug.Log($"✓ DISABLED UI Text: {textComp.gameObject.name} (text: '{textComp.text}')");
                }
            }
        }
        
        // Initialize stripe overlay
        if (enableStripes)
        {
            // Ensure ARCameraManager exists (CRITICAL for depth API to work)
            ARCameraManager cameraManager = FindObjectOfType<ARCameraManager>();
            if (cameraManager == null)
            {
                cameraManager = gameObject.AddComponent<ARCameraManager>();
                Debug.Log("[DEPTH INIT] Added ARCameraManager to AR Camera");
            }
            else
            {
                Debug.Log("[DEPTH INIT] ARCameraManager already present");
            }
            
            occlusionManager = FindObjectOfType<AROcclusionManager>();
            if (occlusionManager != null)
            {
                occlusionManager.enabled = true;
                // Explicitly request depth data for sphere placement
                occlusionManager.requestedEnvironmentDepthMode = UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Fastest;
                Debug.Log($"[DEPTH INIT] Set depth mode to Fastest, current mode: {occlusionManager.currentEnvironmentDepthMode}");
            }
            else
            {
                Debug.LogWarning("[DEPTH INIT] AROcclusionManager not found in scene!");
            }
            
            stripeTexture = GenerateStripeTexture(256, 256, 4);
            Debug.Log($"[STRIPE] Generated stripe texture: {stripeTexture != null}, size: {(stripeTexture != null ? stripeTexture.width + "x" + stripeTexture.height : "null")}");
            
            Shader blitShader = Shader.Find("ARRealism/PointCloudComposite");
            Debug.Log($"[STRIPE] Shader.Find('ARRealism/PointCloudComposite') result: {blitShader != null}");
            if (blitShader == null)
            {
                Debug.LogError("[STRIPE] ❌ PointCloudComposite shader not found! Check GraphicsSettings!");
                Debug.LogError("[STRIPE] ❌ Stripes DISABLED - shader missing from build");
                enableStripes = false;
            }
            else
            {
                blitMaterial = new Material(blitShader);
                Debug.Log($"[STRIPE] ✅ Stripe overlay initialized! Material: {blitMaterial != null}, Shader: {blitShader.name}");
                Debug.Log($"[STRIPE] ✅ Mode: {(forceFullOverlay ? "FULL OVERLAY (stripes everywhere)" : "DEPTH-BASED (stripes on unscanned areas)")}");
            }
        }
        
        // Initialize sphere manager for 3D visualization
        // CRITICAL: Attach to AR Camera so OnRenderImage compositing works!
        if (enableSpheres)
        {
            sphereManager = gameObject.AddComponent<DetectionSphereManager>();
            sphereManager.spherePrefab = spherePrefab;
            sphereManager.enableSpheres = enableSpheres;
            sphereManager.sphereLifetime = sphereLifetime;
            sphereManager.fallbackDepth = fallbackDepth;
            sphereManager.useDepthFallback = useDepthFallback;
            Debug.Log($"✅ Sphere manager attached to AR Camera for compositing - fallback depth: {fallbackDepth}m");
        }
        
        // Initialize YOLO detection
        if (enableObjectDetection)
        {
            Debug.Log("Initializing object detection...");
            
            if (yoloModel == null)
            {
                Debug.LogError("❌❌❌ YOLO MODEL NOT ASSIGNED! ❌❌❌");
                Debug.LogError("Fix: Select 'Main Camera' → Inspector → ARCombinedOverlay component → Assign yolov8s-seg.onnx to 'Yolo Model' field");
                enableObjectDetection = false;
                return;
            }
            
            // Force confidence threshold
            minConfidence = 0.15f;  // WORKING VALUE - don't change
            
            Debug.Log($"Loading YOLO model: {yoloModel.name}");
            
            try
            {
                Debug.Log($"Creating NNHandler with model: {yoloModel.name}");
                nn = new NNHandler(yoloModel);
                Debug.Log("✅ NNHandler created");
                
                Debug.Log("Creating YOLOv8 instance...");
                yolo = new YOLOv8(nn);
                Debug.Log("✅ YOLOv8 created");
                
                YOLOv8OutputReader.DiscardThreshold = 0.01f;  // Very low (1%), let our code filter later
                detectionTexture = new Texture2D(640, 640, TextureFormat.RGB24, false);
                
                Debug.Log($"✅✅✅ YOLO INITIALIZED SUCCESSFULLY! ✅✅✅");
                Debug.Log($"  Model: {yoloModel.name}");
                Debug.Log($"  Min Confidence: {minConfidence}");
                Debug.Log($"  Detection Interval: {detectionInterval} frames");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ YOLO initialization FAILED: {e.Message}");
                Debug.LogError($"Stack trace: {e.StackTrace}");
                enableObjectDetection = false;
            }
        }
        else
        {
            Debug.LogWarning("Object detection is disabled in ARCombinedOverlay settings");
        }
    }
    
    void OnEnable()
    {
        // Ensure camera is enabled when component is enabled
        Camera cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.enabled = true;
            // Don't set clearFlags - let ARCameraBackground handle it
        }
        
        // Force AR components to initialize
        var arCameraManager = FindObjectOfType<UnityEngine.XR.ARFoundation.ARCameraManager>();
        if (arCameraManager != null)
        {
            arCameraManager.enabled = true;
        }
        
        var arSession = FindObjectOfType<UnityEngine.XR.ARFoundation.ARSession>();
        if (arSession != null)
        {
            arSession.enabled = true;
            Debug.Log($"[DEPTH INIT] ARSession found and enabled");
        }
        else
        {
            Debug.LogError("[DEPTH INIT] ARSession NOT found in scene! This is required for depth.");
        }
        
        // Ensure ARCameraBackground is enabled
        var arCameraBackground = GetComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();
        if (arCameraBackground != null)
        {
            arCameraBackground.enabled = true;
        }
    }
    
    void Update()
    {
        // Continuously ensure camera stays enabled
        Camera cam = GetComponent<Camera>();
        if (cam != null && !cam.enabled)
        {
            cam.enabled = true;
            Debug.LogWarning("Camera was disabled, re-enabling!");
        }
    }
    
    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        // Log first few frames for debugging
        if (Time.frameCount <= 5)
        {
            Debug.Log($"[RENDER] OnRenderImage CALLED! Frame {Time.frameCount}, enableStripes={enableStripes}, material={blitMaterial != null}, texture={stripeTexture != null}");
        }
        
        // CRITICAL FIX: If stripes are disabled, just pass through the camera feed
        if (!enableStripes)
        {
            Graphics.Blit(src, dest);
            return;
        }
        
        // SUPERVISOR'S WORKFLOW:
        // 1. Grab frame from camera (src)
        // 2. Run YOLO on clean frame (buffer it)
        // 3. Add overlays / UI / visualization
        
        // DEBUG: Check if src is valid and not black
        if (Time.frameCount % 60 == 0)  // Log once per second at 60fps
        {
            var arCamBg = GetComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();
            Debug.Log($"[RENDER] Frame {Time.frameCount}: src={src?.width}x{src?.height}, ARCameraBackground={arCamBg?.enabled}, material={arCamBg?.material?.name}");
        }
        
        VLog($"[RENDER] OnRenderImage called - enableStripes: {enableStripes}, blitMaterial: {blitMaterial != null}, stripeTexture: {stripeTexture != null}");
        
        RenderTexture current = src;
        RenderTexture temp1 = null;
        RenderTexture temp2 = null;
        
        // STEP 1: Run YOLO on the ORIGINAL clean frame (no overlays)
        if (enableObjectDetection && yolo != null)
        {
            detectionFrameCounter++;
            if (detectionFrameCounter >= detectionInterval)
            {
                detectionFrameCounter = 0;
                // Create a clean copy of the camera frame for YOLO
                RenderTexture cleanFrameBuffer = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
                Graphics.Blit(src, cleanFrameBuffer);
                RunDetection(cleanFrameBuffer);  // YOLO processes the clean buffered frame
                RenderTexture.ReleaseTemporary(cleanFrameBuffer);
            }
        }
        
        // STEP 1.5: Composite detection spheres (GREEN → blue conversion)
        if (sphereManager != null)
        {
            temp1 = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
            sphereManager.CompositeSpheres(current, temp1);
            current = temp1;
        }
        
        // STEP 2: Apply stripe overlay on unscanned areas (camera visible through scanned areas)
        if (enableStripes && blitMaterial != null && stripeTexture != null)
        {
            temp2 = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
            ApplyStripeOverlay(current, temp2);
            current = temp2;
        }
        
        // STEP 3: Add detection visualization (boxes and labels) on TOP
        if (enableObjectDetection && !skipBoxVisualization && timedDetections.Count > 0)
        {
            RenderTexture temp3 = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
            DrawDetections(current, temp3);  // Draw on the composite with overlays
            current = temp3;
            if (temp2 != null) RenderTexture.ReleaseTemporary(temp2);
            temp2 = temp3;
        }
        
        // Final output
        Graphics.Blit(current, dest);
        
        // Cleanup temporary textures
        if (temp1 != null) RenderTexture.ReleaseTemporary(temp1);
        if (temp2 != null) RenderTexture.ReleaseTemporary(temp2);
    }
    
    void ApplyStripeOverlay(RenderTexture src, RenderTexture dest)
    {
        // Log first few frames for debugging
        if (Time.frameCount <= 5)
        {
            Debug.Log($"[STRIPE] ApplyStripeOverlay CALLED! forceFullOverlay={forceFullOverlay}, material={blitMaterial != null}, texture={stripeTexture != null}");
        }
        
        VLog($"[STRIPE] ApplyStripeOverlay - forceFullOverlay: {forceFullOverlay}, material: {blitMaterial != null}, texture: {stripeTexture != null}");
        
        // SAFETY: If shader failed to load, just show camera feed
        if (blitMaterial == null || stripeTexture == null)
        {
            Debug.LogWarning($"[STRIPE] Shader not ready - passing through camera. Material: {blitMaterial != null}, Texture: {stripeTexture != null}");
            Graphics.Blit(src, dest);
            return;
        }
        
        // FULL OVERLAY MODE: Show pink-white stripes everywhere (app startup)
        if (forceFullOverlay)
        {
            if (Time.frameCount % 60 == 0) // Log every second
            {
                Debug.Log($"[STRIPE] FULL OVERLAY MODE ACTIVE - forceFullOverlay=true, showing stripes everywhere");
            }
            VLog("[STRIPE] Applying FULL stripe overlay with material");
            blitMaterial.SetTexture("_CameraTex", src);
            blitMaterial.SetTexture("_MaskTex", Texture2D.blackTexture);  // BLACK (mask=0) = stripes everywhere in old shader
            blitMaterial.SetTexture("_StripeTex", stripeTexture);
            Graphics.Blit(src, dest, blitMaterial);
            return;
        }
        
        // NORMAL MODE: Update mask every N frames and show camera feed with depth-based stripes
        stripeFrameCounter++;
        if (stripeFrameCounter >= maskUpdateInterval || maskTexture == null)
        {
            stripeFrameCounter = 0;
            Texture2D newMask = GenerateMaskTexture();
            if (newMask != null)
            {
                if (Time.frameCount % 60 == 0)  // Log once per second
                {
                    Debug.Log($"[STRIPE] Depth-based mask updated at frame {Time.frameCount}");
                }
                maskTexture = newMask;
                hasDepthData = true;  // Mark that we've received depth data
            }
            else if (Time.frameCount % 60 == 0)
            {
                Debug.LogWarning($"[STRIPE] Failed to generate mask - no depth data at frame {Time.frameCount}");
            }
        }
        
        // Show camera feed if no depth data yet and showCameraUntilScanned is enabled
        if (showCameraUntilScanned && !hasDepthData)
        {
            Graphics.Blit(src, dest);
            return;
        }
        
        // If still no mask texture, just show camera
        if (maskTexture == null)
        {
            if (Time.frameCount % 60 == 0)
            {
                Debug.LogWarning($"[STRIPE] No mask texture - showing camera feed only (depth not acquired yet?)");
            }
            Graphics.Blit(src, dest);
            return;
        }
        
        // Apply stripes overlay on camera feed (camera visible underneath)
        if (Time.frameCount % 60 == 0) // Log every second 
        {
            Debug.Log($"[STRIPE] DEPTH-BASED MODE - Applying mask-based stripes, threshold={incompleteThreshold:F2}m, hasDepth={hasDepthData}");
        }
        blitMaterial.SetTexture("_CameraTex", src);
        blitMaterial.SetTexture("_MaskTex", maskTexture);
        blitMaterial.SetTexture("_StripeTex", stripeTexture);
        Graphics.Blit(src, dest, blitMaterial);
    }
    
    // Store crop info for coordinate mapping
    private float cropScaleRatio = 1f;
    private float cropOffsetX = 0f;
    private float cropOffsetY = 0f;
    
    void RunDetection(RenderTexture src)
    {
        try
        {
            // PERFORMANCE: Downscale camera input before YOLO (keeps YOLO at 640x640)
            int processWidth = Mathf.RoundToInt(src.width * cameraDownscale);
            int processHeight = Mathf.RoundToInt(src.height * cameraDownscale);
            
            RenderTexture downscaledInput = null;
            RenderTexture inputToProcess = src;
            
            if (cameraDownscale < 0.99f)
            {
                downscaledInput = RenderTexture.GetTemporary(processWidth, processHeight, 0, src.format);
                Graphics.Blit(src, downscaledInput);
                inputToProcess = downscaledInput;
            }
            
            // Calculate coordinate mapping (YOLO is always 640x640)
            float originalWidth = src.width;
            float originalHeight = src.height;
            
            float widthRatio = 640f / originalWidth;
            float heightRatio = 640f / originalHeight;
            cropScaleRatio = Mathf.Max(widthRatio, heightRatio);
            
            int scaledWidth = Mathf.CeilToInt(originalWidth * cropScaleRatio);
            int scaledHeight = Mathf.CeilToInt(originalHeight * cropScaleRatio);
            
            cropOffsetX = (scaledWidth - 640) / 2f;
            cropOffsetY = (scaledHeight - 640) / 2f;
            
            // Resize to 640x640 for YOLO
            if (detectionTexture == null || detectionTexture.width != 640)
            {
                detectionTexture = new Texture2D(640, 640, TextureFormat.RGB24, false);
            }
            TextureTools.ResizeAndCropToCenter(inputToProcess, ref detectionTexture, 640, 640);
            
            if (downscaledInput != null)
            {
                RenderTexture.ReleaseTemporary(downscaledInput);
            }
            
            VLog($"Tensor input texture: {detectionTexture.width}x{detectionTexture.height}, format {detectionTexture.format}");
            VLog($"Source RenderTexture: {src.width}x{src.height}, format {src.format}, sRGB:{src.sRGB}");
            
            // 2. Let YOLO.Run() create tensor with Barracuda's built-in Tensor(Texture2D) constructor
            //    This applies the same preprocessing as the static image workflow
            var allDetections = yolo.Run(detectionTexture);
            Debug.Log($"🔍 YOLO returned {allDetections.Count} raw detections");
            VLog($"YOLO raw output: {allDetections.Count} detections before filtering");
            
            // Log ALL raw detections
            foreach (var box in allDetections)
            {
                VLog($"RAW: {GetClassName(box.bestClassIndex)} {(box.score * 100):F1}% at {box.rect} size:{box.rect.width}x{box.rect.height}");
            }
            
            // Advanced filtering for better accuracy
            List<ResultBox> validDetections = new List<ResultBox>();
            
            foreach (var box in allDetections)
            {
                // 1. Confidence threshold (20% minimum)
                if (box.score < minConfidence)
                {
                    continue;
                }
                
                // 2. Size validation - boxes in 640x640 space
                if (box.rect.width > 620 || box.rect.height > 620)
                {
                    VLog($"FILTERED: Oversized {GetClassName(box.bestClassIndex)} {box.rect.width}x{box.rect.height}");
                    continue;
                }
                
                if (box.rect.width < 20 || box.rect.height < 20)
                {
                    VLog($"FILTERED: Too small {GetClassName(box.bestClassIndex)} size:{box.rect.width}x{box.rect.height}");
                    continue;
                }
                
                // 3. Aspect ratio validation - filter unrealistic shapes
                float aspectRatio = box.rect.width / box.rect.height;
                if (aspectRatio > 5f || aspectRatio < 0.2f)  // Too wide or too tall
                {
                    VLog($"FILTERED: Bad aspect ratio {GetClassName(box.bestClassIndex)} {aspectRatio:F2}");
                    continue;
                }
                
                // 4. Overlap check - remove heavily overlapping detections (improved NMS)
                bool overlapsExisting = false;
                foreach (var existingBox in validDetections)
                {
                    float intersectionArea = GetIntersectionArea(box.rect, existingBox.rect);
                    float box1Area = box.rect.width * box.rect.height;
                    float box2Area = existingBox.rect.width * existingBox.rect.height;
                    float unionArea = box1Area + box2Area - intersectionArea;
                    float iou = intersectionArea / unionArea;
                    
                    // If IoU > 50% and existing box has higher confidence, skip this box
                    if (iou > 0.5f && existingBox.score >= box.score)
                    {
                        overlapsExisting = true;
                        VLog($"FILTERED: Overlaps {GetClassName(box.bestClassIndex)} IoU:{iou:F2}");
                        break;
                    }
                }
                if (overlapsExisting) continue;
                
                validDetections.Add(box);
                VLog($"VALID: {GetClassName(box.bestClassIndex)}: {(box.score * 100):F1}% at {box.rect}");
            }
            
            // Temporal consistency - require detection in multiple consecutive frames
            if (validDetections.Count > 0)
            {
                lastDetections = validDetections;
                
                float currentTime = Time.time;
                List<DetectionWithTime> updatedDetections = new List<DetectionWithTime>();
                
                foreach (var newBox in validDetections)
                {
                    // Find matching existing detection (same class, similar position)
                    DetectionWithTime existing = null;
                    foreach (var timedBox in timedDetections)
                    {
                        if (timedBox.box.bestClassIndex == newBox.bestClassIndex)
                        {
                            // Check if boxes overlap (similar position using IoU)
                            float intersectionArea = GetIntersectionArea(newBox.rect, timedBox.box.rect);
                            float box1Area = newBox.rect.width * newBox.rect.height;
                            float box2Area = timedBox.box.rect.width * timedBox.box.rect.height;
                            float unionArea = box1Area + box2Area - intersectionArea;
                            float iou = intersectionArea / unionArea;
                            
                            if (iou > 0.3f) // 30% overlap = same object
                            {
                                existing = timedBox;
                                break;
                            }
                        }
                    }
                    
                    if (existing != null)
                    {
                        // Keep existing detection with original timestamp (persistent)
                        updatedDetections.Add(new DetectionWithTime(newBox, existing.timestamp));
                    }
                    else
                    {
                        // New detection - add with current timestamp
                        updatedDetections.Add(new DetectionWithTime(newBox, currentTime));
                    }
                }
                
                timedDetections = updatedDetections;
                Debug.Log($"✓ Updated with {lastDetections.Count} valid detections, total timed: {timedDetections.Count}");
                
                // Create 3D spheres ONLY for NEW high-confidence detections
                // Strict filtering: only create spheres for detections that just appeared (age < 0.5s)
                if (enableSpheres && sphereManager != null && validDetections.Count > 0)
                {
                    // Step 1: Collect fresh detections with their IntelliCap scores
                    List<(ResultBox box, float score, string className)> candidates = new List<(ResultBox, float, string)>();
                    
                    foreach (var timedBox in updatedDetections)
                    {
                        float age = currentTime - timedBox.timestamp;
                        // Only create spheres for recent detections with high confidence
                        if (age < 0.5f && timedBox.box.score >= 0.6f) // 60% confidence minimum
                        {
                            string className = GetClassName(timedBox.box.bestClassIndex);
                            float intelliCapScore = ObjectDetectionThresholds.GetObjectScore(className, timedBox.box.score);
                            
                            if (intelliCapScore >= 0f) // Passed threshold check
                            {
                                candidates.Add((timedBox.box, intelliCapScore, className));
                            }
                            else
                            {
                                Debug.Log($"[FILTER] {className} rejected by IntelliCap thresholds (conf:{timedBox.box.score:F2})");
                            }
                        }
                    }
                    
                    // Step 2: Competitive filtering - keep only highest-scoring object in overlapping groups
                    List<ResultBox> competitiveWinners = new List<ResultBox>();
                    
                    if (candidates.Count > 0)
                    {
                        // Sort by score (highest first)
                        candidates.Sort((a, b) => b.score.CompareTo(a.score));
                        
                        Debug.Log($"[COMPETITIVE] {candidates.Count} candidates, sorted by score:");
                        foreach (var c in candidates)
                        {
                            Debug.Log($"  - {c.className}: score={c.score:F2}, conf={c.box.score:F2}");
                        }
                        
                        // Greedy selection: process highest-scoring first
                        foreach (var candidate in candidates)
                        {
                            // Check if this candidate overlaps with any already-selected winner
                            bool overlapsWinner = false;
                            
                            foreach (var winner in competitiveWinners)
                            {
                                // Calculate IoU (Intersection over Union)
                                float intersectionArea = GetIntersectionArea(candidate.box.rect, winner.rect);
                                float box1Area = candidate.box.rect.width * candidate.box.rect.height;
                                float box2Area = winner.rect.width * winner.rect.height;
                                float unionArea = box1Area + box2Area - intersectionArea;
                                float iou = unionArea > 0 ? intersectionArea / unionArea : 0f;
                                
                                // If IoU > 30%, consider them overlapping (competing)
                                if (iou > 0.3f)
                                {
                                    string winnerClass = GetClassName(winner.bestClassIndex);
                                    Debug.Log($"[COMPETITIVE] ❌ {candidate.className} (score:{candidate.score:F2}) LOSES to {winnerClass} (IoU:{iou:F2})");
                                    overlapsWinner = true;
                                    break;
                                }
                            }
                            
                            if (!overlapsWinner)
                            {
                                competitiveWinners.Add(candidate.box);
                                Debug.Log($"[COMPETITIVE] ✅ {candidate.className} WINS (score:{candidate.score:F2})");
                            }
                        }
                    }
                    
                    if (competitiveWinners.Count > 0)
                    {
                        Debug.Log($"🎯 Creating spheres for {competitiveWinners.Count} winners after competitive filtering ({candidates.Count} candidates)");
                        sphereManager.CreateSpheresForDetections(competitiveWinners, src.width, src.height, cropScaleRatio, cropOffsetX, cropOffsetY);
                    }
                    else
                    {
                        Debug.Log($"⏭️ No detections passed competitive filtering ({validDetections.Count} valid, {candidates.Count} candidates)");
                    }
                }
                else if (!enableSpheres)
                {
                    Debug.LogWarning("⚠️ Spheres disabled in settings!");
                }
                else if (sphereManager == null)
                {
                    Debug.LogError("❌ SphereManager is NULL!");
                }
            }
            else
            {
                // No detections this frame; keep existing timed detections
                // so they can persist briefly and fade instead of disappearing instantly.
                Debug.Log($"No new detections - keeping {timedDetections.Count} existing boxes for fade");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Detection failed: {e.Message}\n{e.StackTrace}");
            lastDetections.Clear();
        }
    }
    
    void DrawDetections(RenderTexture src, RenderTexture dest)
    {
        Debug.Log($"🎨 DRAWING {timedDetections.Count} boxes on ORIGINAL camera (size: {src.width}x{src.height})");
        
        RenderTexture.active = src;
        Texture2D displayTexture = new Texture2D(src.width, src.height, TextureFormat.RGB24, false);
        displayTexture.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        displayTexture.Apply();
        RenderTexture.active = null;
        
        float currentTime = Time.time;
        
        foreach (var timedBox in timedDetections)
        {
            ResultBox box = timedBox.box;
            float age = currentTime - timedBox.timestamp;
            
            // Calculate fade alpha
            float alpha = 1f;
            if (age > fadeAfterSeconds)
            {
                // Start fading
                float fadeProgress = (age - fadeAfterSeconds) / fadeDuration;
                alpha = 1f - Mathf.Clamp01(fadeProgress);
            }
            
            // Skip if completely faded
            if (alpha <= 0f) continue;
            
            // Filter out tiny boxes (likely false positives)
            if (box.rect.width < minBoxSize || box.rect.height < minBoxSize)
            {
                continue;
            }
            
            // Base transparency from palette (e.g., 0.3) multiplied by fade alpha
            Color baseColor = boxColors[box.bestClassIndex % boxColors.Length];
            float finalAlpha = Mathf.Clamp01(baseColor.a * alpha);
            Color boxColor = new Color(baseColor.r, baseColor.g, baseColor.b, finalAlpha);
            
            // Map coordinates from 640x640 cropped space back to original image
            // 1. Add crop offset to get coordinates in scaled image space
            float scaledX = box.rect.x + cropOffsetX;
            float scaledY = box.rect.y + cropOffsetY;
            float scaledW = box.rect.width;
            float scaledH = box.rect.height;
            
            // 2. Scale back to original image dimensions
            float mappedX = scaledX / cropScaleRatio;
            float mappedY = scaledY / cropScaleRatio;
            float mappedW = scaledW / cropScaleRatio;
            float mappedH = scaledH / cropScaleRatio;
            
            // 3. Expand boxes by 10% to ensure full object coverage (fix for 80% coverage issue)
            float expandFactor = 1.10f;  // 10% expansion
            float expandW = mappedW * (expandFactor - 1f) / 2f;
            float expandH = mappedH * (expandFactor - 1f) / 2f;
            
            Rect scaledRect = new Rect(
                mappedX - expandW,
                mappedY - expandH,
                mappedW + (expandW * 2f),
                mappedH + (expandH * 2f)
            );
            
            VLog($"Drawing box: {GetClassName(box.bestClassIndex)} {(box.score*100):F1}% alpha:{alpha:F2} age:{age:F1}s at scaled rect: {scaledRect}");
            
            // Draw filled semi-transparent colored rectangle covering the entire object
            // Apply Y-flip for correct screen coordinates
            Rect flippedRect = new Rect(
                scaledRect.x,
                src.height - scaledRect.y - scaledRect.height,  // Flip Y coordinate
                scaledRect.width,
                scaledRect.height
            );

            // Optional horizontal flip (e.g., for mirrored camera feeds)
            Rect finalRect = flippedRect;
            if (flipHorizontal)
            {
                float newX = src.width - flippedRect.x - flippedRect.width;
                finalRect = new Rect(newX, flippedRect.y, flippedRect.width, flippedRect.height);
            }
            
            TextureTools.DrawFilledRect(displayTexture, finalRect, boxColor,
                                        rectIsNormalized: false, revertY: false);  // Already flipped above
            
            // Labels removed as per user request - only showing colored bounding boxes
        }
        
        Graphics.Blit(displayTexture, dest);
        Destroy(displayTexture);

        // Prune fully faded boxes after drawing
        timedDetections.RemoveAll(tb => (Time.time - tb.timestamp) > (fadeAfterSeconds + fadeDuration));
    }
    
    // Helper function to calculate intersection area between two rectangles
    float GetIntersectionArea(Rect rect1, Rect rect2)
    {
        float x1 = Mathf.Max(rect1.x, rect2.x);
        float y1 = Mathf.Max(rect1.y, rect2.y);
        float x2 = Mathf.Min(rect1.x + rect1.width, rect2.x + rect2.width);
        float y2 = Mathf.Min(rect1.y + rect1.height, rect2.y + rect2.height);
        
        float width = Mathf.Max(0, x2 - x1);
        float height = Mathf.Max(0, y2 - y1);
        
        return width * height;
    }
    
    string GetClassName(int classIndex)
    {
        string[] cocoClasses = new string[] {
            "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
            "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog",
            "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella",
            "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball", "kite",
            "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket", "bottle",
            "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple", "sandwich",
            "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
            "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote",
            "keyboard", "cell phone", "microwave", "oven", "toaster", "sink", "refrigerator", "book",
            "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush"
        };
        
        if (classIndex >= 0 && classIndex < cocoClasses.Length)
            return cocoClasses[classIndex];
        return $"Class{classIndex}";
    }
    
    Texture2D GenerateMaskTexture()
    {
        if (occlusionManager == null || !occlusionManager.TryAcquireEnvironmentDepthCpuImage(out var image))
            return null;
            
        int width = image.width;
        int height = image.height;
        int targetWidth = Mathf.Min(width, textureWidth);
        int targetHeight = Mathf.Min(height, textureHeight);
        
        Texture2D maskTex = new Texture2D(targetWidth, targetHeight, TextureFormat.R8, false);
        var conversionParams = new XRCpuImage.ConversionParams(image, TextureFormat.RFloat);
        int dataLength = width * height * sizeof(float);
        var rawDepthData = new Unity.Collections.NativeArray<byte>(dataLength, Unity.Collections.Allocator.Temp);
        image.Convert(conversionParams, rawDepthData);
        image.Dispose();
        
        Color32[] pixels = new Color32[targetWidth * targetHeight];
        
        // DEBUG: Track depth statistics
        int validDepthCount = 0;
        int noDepthCount = 0;
        float minDepth = float.MaxValue;
        float maxDepth = float.MinValue;
        float avgDepth = 0;
        
        unsafe
        {
            fixed (byte* ptr = rawDepthData.ToArray())
            {
                float* depthPtr = (float*)ptr;
                int skipX = width / targetWidth;
                int skipY = height / targetHeight;
                
                for (int y = 0; y < targetHeight; y++)
                {
                    for (int x = 0; x < targetWidth; x++)
                    {
                        int srcX = x * skipX;
                        int srcY = y * skipY;
                        int srcIdx = srcY * width + srcX;
                        float depth = depthPtr[srcIdx];
                        
                        // Shader blend: result = camera * mask + stripes * (1-mask)
                        // mask=255 (white) → camera * 1 + stripes * 0 = CAMERA (scanned)
                        // mask=0 (black) → camera * 0 + stripes * 1 = STRIPES (unscanned)
                        // FINAL LOGIC: Show CAMERA only if depth is valid AND close (0.01m < depth ≤ threshold)
                        // Show STRIPES if: depth invalid (≤0.01m = unscanned/new) OR depth far (>threshold)
                        byte mask = (depth > 0.01f && depth <= incompleteThreshold) ? (byte)255 : (byte)0;
                        pixels[y * targetWidth + x] = new Color32(mask, mask, mask, 255);
                        
                        // Debug statistics
                        if (depth > 0.01f)
                        {
                            validDepthCount++;
                            minDepth = Mathf.Min(minDepth, depth);
                            maxDepth = Mathf.Max(maxDepth, depth);
                            avgDepth += depth;
                        }
                        else
                        {
                            noDepthCount++;
                        }
                    }
                }
            }
        }
        
        // Log depth statistics every 2 seconds
        if (Time.frameCount % 120 == 0)
        {
            avgDepth = validDepthCount > 0 ? avgDepth / validDepthCount : 0;
            Debug.Log($"[STRIPE DEPTH] Valid: {validDepthCount}/{targetWidth*targetHeight} ({100f*validDepthCount/(targetWidth*targetHeight):F1}%), " +
                     $"Range: {minDepth:F2}m - {maxDepth:F2}m, Avg: {avgDepth:F2}m, Threshold: {incompleteThreshold:F2}m");
        }
        
        maskTex.SetPixels32(pixels);
        maskTex.Apply();
        rawDepthData.Dispose();
        return maskTex;
    }
    
    Texture2D GenerateStripeTexture(int width, int height, int stripeWidth)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color32 pink = new Color32(255, 178, 229, 255);
        Color32 white = new Color32(255, 255, 255, 255);
        
        Color32[] pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Diagonal stripes: alternating based on (x + y) creates diagonal pattern
                bool isPink = (((x + y) / stripeWidth) % 2 == 0);
                pixels[y * width + x] = isPink ? pink : white;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
    
    /// <summary>
    /// Call this to start showing camera feed and depth-based scanning (called when recording starts)
    /// </summary>
    public void StartScanning()
    {
        forceFullOverlay = false;
        Debug.Log("✓ Started scanning - camera feed now visible with depth-based stripes");
    }
    
    void OnDestroy()
    {
        nn?.Dispose();
        if (detectionTexture != null) Destroy(detectionTexture);
    }
    
    public List<ResultBox> GetLastDetections() => lastDetections;
    public void SetConfidenceThreshold(float threshold)
    {
        minConfidence = Mathf.Clamp01(threshold);
        YOLOv8OutputReader.DiscardThreshold = minConfidence;
    }
}
