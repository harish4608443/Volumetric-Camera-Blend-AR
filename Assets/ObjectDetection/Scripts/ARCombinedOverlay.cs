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
    
    [Header("Spatial Coverage Method")]
    [Tooltip("Use TSDF weight volume for spatial coverage (IntelliCap method) instead of simple depth threshold")]
    public bool useTSDFWeights = false;  // DISABLED: TSDFVolumeAtlas.SampleWeightViaCPU() not implemented yet (returns 0)
    
    [Header("Depth Threshold Method (Simple)")]
    [Tooltip("Depth threshold in meters - areas closer than this show camera feed")]
    [Range(0.3f, 3.0f)]
    public float depthThreshold = 2.0f;  // 2.0 meters - comfortable distance to see camera feed
    [Tooltip("Use accumulative scanning - once scanned, area stays scanned")]
    public bool useAccumulativeScanning = false;  // REAL-TIME mode: stripes show immediately on unscanned areas
    
    [Header("TSDF Weight Method (IntelliCap)")]
    [Tooltip("Minimum TSDF weight to consider area scanned (NOT USED - weight sampling unimplemented)")]
    [Range(0.01f, 10.0f)]
    public float minTSDFWeight = 1.0f;  // Areas with weight >= 1.0 would show camera feed (if implemented)
    [Tooltip("TSDFVolumeAtlas component for weight-based spatial coverage")]
    public MonoBehaviour tsdfAtlas;  // Auto-found if not assigned (uses MonoBehaviour to avoid assembly reference issues)
    
    private int textureWidth = 128;
    private int textureHeight = 128;
    private float depthModeEnabledTime = -1f;  // Track when depth mode becomes enabled
    private Color32[] accumulativeMask;  // Persistent spatial coverage memory
    private int tsdfAtlasRetryCount = 0;  // Retry finding TSDF atlas in first few frames
    
    [Header("YOLO Detection Settings")]
    public bool enableObjectDetection = true;
    [Tooltip("YOLOv8 ONNX model file")]
    public NNModel yoloModel;
    [Range(0.0f, 1f)]
    public float minConfidence = 0.40f;  // 40% confidence - filters out most false positives
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
            // Auto-find TSDF atlas if using TSDF weight method
            if (useTSDFWeights && tsdfAtlas == null)
            {
                // Search all MonoBehaviours for one with class name "TSDFVolumeAtlas"
                // Type.GetType() doesn't work across assemblies without full assembly qualification
                MonoBehaviour[] allMonoBehaviours = GameObject.FindObjectsOfType<MonoBehaviour>();
                foreach (MonoBehaviour mb in allMonoBehaviours)
                {
                    if (mb.GetType().Name == "TSDFVolumeAtlas")
                    {
                        tsdfAtlas = mb;
                        break;
                    }
                }
                
                if (tsdfAtlas != null)
                {
                    Debug.Log($"[STRIPE] ✅ Auto-found TSDFVolumeAtlas for spatial coverage");
                    Debug.LogWarning($"[STRIPE] ⚠️ TSDF weight sampling not implemented yet (SampleWeightViaCPU returns 0), staying with depth threshold");
                    useTSDFWeights = false;  // Force fallback to depth threshold
                }
                else
                {
                    Debug.LogWarning($"[STRIPE] ⚠️ TSDFVolumeAtlas not found yet (may be execution order issue, will retry)");
                    // Don't set useTSDFWeights=false yet - retry in Update() for first few frames
                }
            }
            
            occlusionManager = FindObjectOfType<AROcclusionManager>();
            if (occlusionManager != null)
            {
                occlusionManager.enabled = true;
                occlusionManager.requestedEnvironmentDepthMode = UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Fastest;
                Debug.Log($"[STRIPE] Occlusion manager found, depth mode: {occlusionManager.currentEnvironmentDepthMode}");
            }
            else
            {
                Debug.LogWarning("[STRIPE] AROcclusionManager not found - depth-based masking unavailable");
            }
            
            stripeTexture = GenerateStripeTexture(256, 256, 4);
            Debug.Log($"[STRIPE] Generated stripe texture");
            
            Shader blitShader = Shader.Find("ARRealism/PointCloudComposite");
            if (blitShader == null)
            {
                Debug.LogError("[STRIPE] PointCloudComposite shader not found!");
                enableStripes = false;
            }
            else
            {
                blitMaterial = new Material(blitShader);
                
                // ALWAYS force real-time mode (accumulative causes stripes to disappear)
                if (useAccumulativeScanning)
                {
                    Debug.LogWarning($"[STRIPE] Accumulative mode causes stripes to disappear, forcing REAL-TIME mode");
                    useAccumulativeScanning = false;
                }
                
                // Log active spatial coverage method
                string method = useTSDFWeights ? "TSDF WEIGHTS (IntelliCap)" : "DEPTH THRESHOLD (Simple)";
                Debug.Log($"[STRIPE] 📊 SPATIAL COVERAGE METHOD: {method}");
                
                if (useTSDFWeights)
                {
                    Debug.Log($"[STRIPE] TSDF Settings: minWeight={minTSDFWeight}, atlas={tsdfAtlas != null}");
                }
                else
                {
                    Debug.Log($"[STRIPE] Depth Settings: Threshold={depthThreshold}m, Accumulative={useAccumulativeScanning}");
                }
                
                Debug.Log($"[STRIPE] ✅ FINAL SETTINGS: Method={method}");
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
            
            // Ensure minimum confidence threshold (don't override Inspector value unless too low)
            if (minConfidence < 0.25f)
            {
                Debug.LogWarning($"[YOLO] Confidence threshold {minConfidence} too low, forcing to 0.35f");
                minConfidence = 0.35f;
            }
            
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
                Debug.Log($"  Min Confidence: {minConfidence:P0} ({minConfidence})");
                Debug.Log($"  Detection Interval: {detectionInterval} frames");
                Debug.Log($"  Objects below {minConfidence:P0} confidence will be ignored");
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
        // Retry finding TSDF atlas if it wasn't found in Start() (execution order issue)
        // TSDFVolumeAtlas initializes around frame 15-20, so retry for 60 frames to be safe
        if (useTSDFWeights && tsdfAtlas == null && tsdfAtlasRetryCount < 60)
        {
            tsdfAtlasRetryCount++;
            
            // Log retry attempts periodically
            if (tsdfAtlasRetryCount % 10 == 0 || tsdfAtlasRetryCount <= 3)
            {
                Debug.Log($"[STRIPE] Retrying TSDFVolumeAtlas search... attempt #{tsdfAtlasRetryCount}, frame {Time.frameCount}");
            }
            
            // Search all MonoBehaviours for one with class name "TSDFVolumeAtlas"
            // Type.GetType() doesn't work across assemblies, so we search manually
            MonoBehaviour[] allMonoBehaviours = GameObject.FindObjectsOfType<MonoBehaviour>();
            foreach (MonoBehaviour mb in allMonoBehaviours)
            {
                if (mb.GetType().Name == "TSDFVolumeAtlas")
                {
                    tsdfAtlas = mb;
                    Debug.Log($"[STRIPE] ✅ Found TSDFVolumeAtlas on frame {Time.frameCount} (retry #{tsdfAtlasRetryCount})");
                    Debug.Log($"[STRIPE] 📊 SWITCHING TO TSDF WEIGHTS MODE (IntelliCap)");
                    break;
                }
            }
            
            // After 60 retries (~3 seconds), give up and fall back to depth threshold
            if (tsdfAtlasRetryCount >= 60 && tsdfAtlas == null)
            {
                Debug.LogWarning($"[STRIPE] ⚠️ TSDFVolumeAtlas not found after 60 frames! Falling back to depth threshold method");
                useTSDFWeights = false;
            }
        }
        
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
        // SAFETY: If shader failed to load, just show camera feed
        if (blitMaterial == null || stripeTexture == null)
        {
            Graphics.Blit(src, dest);
            return;
        }
        
        // Generate mask VERY frequently to show stripes in real-time
        // Every 3 frames for first 60 seconds, then every 10 frames
        int currentUpdateInterval = (Time.time < 60f) ? 3 : 10;
        
        // Generate mask every N frames OR if maskTexture is null (keep trying until depth available)
        stripeFrameCounter++;
        
        if (stripeFrameCounter >= currentUpdateInterval || maskTexture == null)
        {
            stripeFrameCounter = 0;
            
            Texture2D newMask = GenerateMaskTexture();
            if (newMask != null)
            {
                maskTexture = newMask;
                hasDepthData = true;
                
                // Track when depth first becomes available
                if (depthModeEnabledTime < 0)
                {
                    depthModeEnabledTime = Time.time;
                    Debug.Log($"[STRIPE] Depth data first acquired at {depthModeEnabledTime:F1}s (frame {Time.frameCount})");
                }
            }
            else
            {
                if (Time.frameCount % 90 == 0)
                {
                    Debug.LogWarning($"[STRIPE] Frame {Time.frameCount}: Failed to generate mask - depth not available yet");
                }
            }
        }
        else if (Time.frameCount % 90 == 0)
        {
            Debug.Log($"[STRIPE] Frame {Time.frameCount}: Using existing mask (counter={stripeFrameCounter}/{currentUpdateInterval}, time={Time.time:F1}s)");
        }
       
        // Show camera feed if no depth data yet and showCameraUntilScanned is enabled
        if (showCameraUntilScanned && !hasDepthData)
        {
            if (Time.frameCount % 90 == 0)
            {
                Debug.Log("[STRIPE] Showing camera feed - waiting for depth data");
            }
            Graphics.Blit(src, dest);
            return;
        }
        
        // If still no mask texture, just show camera
        if (maskTexture == null)
        {
            if (Time.frameCount % 90 == 0)
            {
                Debug.LogWarning("[STRIPE] No mask texture - showing camera feed");
            }
            Graphics.Blit(src, dest);
            return;
        }
        
        // Apply stripes overlay on camera feed
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
                // 1. Confidence threshold filter
                if (box.score < minConfidence)
                {
                    if (Time.frameCount % 300 == 0)  // Log occasionally to show filtering is working
                    {
                        Debug.Log($"[YOLO] Filtered low confidence: {GetClassName(box.bestClassIndex)} {box.score:P0} < {minConfidence:P0}");
                    }
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
                        // BALANCED: Only create spheres for recent detections with good confidence
                        if (age < 0.5f && timedBox.box.score >= 0.70f) // BALANCED at 70% confidence (was 75%, then 65%)
                        {
                            string className = GetClassName(timedBox.box.bestClassIndex);
                            float intelliCapScore = ObjectDetectionThresholds.GetObjectScore(className, timedBox.box.score);
                            
                            if (intelliCapScore >= 0f) // Passed threshold check
                            {
                                candidates.Add((timedBox.box, intelliCapScore, className));
                                Debug.Log($"[CANDIDATE] {className}: IntelliCap={intelliCapScore:F2}, rawConf={timedBox.box.score:P0}, age={age:F2}s");
                            }
                            else
                            {
                                Debug.Log($"[FILTER] {className} rejected by IntelliCap thresholds (conf:{timedBox.box.score:F2}, score:{intelliCapScore:F2})");
                            }
                        }
                    }
                    
                    // Step 2: COMPETITIVE FILTERING - Winner-Takes-All
                    // Only the SINGLE highest-scoring object gets a sphere
                    List<ResultBox> competitiveWinners = new List<ResultBox>();
                    
                    if (candidates.Count > 0)
                    {
                        Debug.Log($"[COMPETITIVE] ===== {candidates.Count} candidates detected, applying winner-takes-all filtering =====");
                        
                        // Sort by IntelliCap score (highest first)
                        candidates.Sort((a, b) => b.score.CompareTo(a.score));
                        
                        Debug.Log($"[COMPETITIVE] Candidates ranked by IntelliCap score:");
                        for (int i = 0; i < candidates.Count; i++)
                        {
                            string rank = i == 0 ? "🏆 #1 (WINNER)" : $"#{i+1}";
                            Debug.Log($"[COMPETITIVE]   {rank} {candidates[i].className}: IntelliCap={candidates[i].score:F2}, confidence={candidates[i].box.score:P0}");
                        }
                        
                        // Winner-takes-all: ONLY the top-scoring detection gets a sphere
                        var winner = candidates[0];
                        competitiveWinners.Add(winner.box);
                        Debug.Log($"[COMPETITIVE] 🎯 SPHERE CREATED FOR: {winner.className} (IntelliCap score: {winner.score:F2})");
                        
                        // Log rejected candidates
                        if (candidates.Count > 1)
                        {
                            Debug.Log($"[COMPETITIVE] ❌ REJECTED {candidates.Count - 1} lower-scoring objects:");
                            for (int i = 1; i < candidates.Count; i++)
                            {
                                Debug.Log($"[COMPETITIVE]   ❌ {candidates[i].className} (score:{candidates[i].score:F2}) - lower than winner");
                            }
                        }
                        
                        Debug.Log($"[COMPETITIVE] ===== Result: 1 sphere from {candidates.Count} candidates =====");
                    }
                    
                    if (competitiveWinners.Count > 0)
                    {
                        Debug.Log($"🎯 Creating sphere for THE WINNER: {GetClassName(competitiveWinners[0].bestClassIndex)} (1 winner from {candidates.Count} candidates)");
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
    
    /// <summary>
    /// Generate spatial coverage mask based on TSDF reconstruction or AR mesh (IntelliCap method).
    /// Priority: TSDF weight volume > AR mesh > accumulative depth.
    /// </summary>
    
    Texture2D GenerateMaskTexture()
    {
        // ROUTE: Choose spatial coverage method
        if (useTSDFWeights && tsdfAtlas != null)
        {
            // IntelliCap method: Use TSDF weight volume
            if (Time.frameCount == 20 || Time.frameCount == 50 || Time.frameCount == 100)
            {
                Debug.Log($"[STRIPE MASK] F{Time.frameCount} Using TSDF WEIGHTS path (useTSDFWeights={useTSDFWeights}, atlas={tsdfAtlas != null})");
            }
            return GenerateMaskFromTSDFWeights();
        }
        
        // Fallback/Simple method: Use raw depth threshold
        if (Time.frameCount == 20 || Time.frameCount == 50 || Time.frameCount == 100)
        {
            Debug.Log($"[STRIPE MASK] F{Time.frameCount} Using DEPTH THRESHOLD path (useTSDFWeights={useTSDFWeights}, atlas={tsdfAtlas != null})");
        }
        
        if (occlusionManager == null)
        {
            if (Time.frameCount % 90 == 0)
            {
                Debug.LogWarning("[STRIPE MASK] OcclusionManager is NULL");
            }
            return null;
        }
        
        // Try GPU depth texture first (more widely supported)
        Texture depthTex = occlusionManager.environmentDepthTexture;
        if (depthTex != null)
        {
            return GenerateMaskFromGPUTexture(depthTex);
        }
        
        // Fall back to CPU depth image (may not be supported on all devices)
        if (!occlusionManager.TryAcquireEnvironmentDepthCpuImage(out var image))
        {
            if (Time.frameCount % 90 == 0)
            {
                Debug.LogWarning("[STRIPE MASK] Failed to acquire depth image (CPU path), and GPU texture is null");
                Debug.LogWarning($"[STRIPE MASK] Depth mode requested: {occlusionManager.requestedEnvironmentDepthMode}, current: {occlusionManager.currentEnvironmentDepthMode}");
            }
            return null;
        }
        
        return GenerateMaskFromCPUImage(image);
    }
    
    Texture2D GenerateMaskFromGPUTexture(Texture depthTex)
    {
        int targetWidth = textureWidth;
        int targetHeight = textureHeight;
        
        // Initialize accumulative mask on first run
        // SHADER LOGIC: White (255) = scanned (camera), Black (0) = unscanned (stripes)
        if (useAccumulativeScanning && (accumulativeMask == null || accumulativeMask.Length != targetWidth * targetHeight))
        {
            accumulativeMask = new Color32[targetWidth * targetHeight];
            for (int i = 0; i < accumulativeMask.Length; i++)
            {
                accumulativeMask[i] = new Color32(0, 0, 0, 255);  // Start ALL UNSCANNED (black = stripes)
            }
            Debug.Log($"[STRIPE MASK] Initialized {targetWidth}x{targetHeight} accumulative mask (GPU path) - all BLACK (unscanned/stripes)");
        }
        
        // Read depth texture from GPU
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.RFloat);
        Graphics.Blit(depthTex, rt);
        
        RenderTexture.active = rt;
        Texture2D tempDepthTex = new Texture2D(targetWidth, targetHeight, TextureFormat.RFloat, false);
        tempDepthTex.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        tempDepthTex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        
        // Process depth data
        Color[] depthPixels = tempDepthTex.GetPixels();
        Destroy(tempDepthTex);
        
        Color32[] maskPixels;
        if (useAccumulativeScanning)
        {
            maskPixels = accumulativeMask;
        }
        else
        {
            maskPixels = new Color32[targetWidth * targetHeight];
        }
        
        int blackCount = 0;
        int whiteCount = 0;
        int newlyScannedCount = 0;
        float minDepth = float.MaxValue;
        float maxDepth = 0f;
        
        for (int i = 0; i < depthPixels.Length; i++)
        {
            float depth = depthPixels[i].r;  // Depth stored in red channel
            
            if (depth > 0.01f)
            {
                if (depth < minDepth) minDepth = depth;
                if (depth > maxDepth) maxDepth = depth;
            }
            
            if (useAccumulativeScanning)
            {
                // ACCUMULATIVE: Once scanned (depth within threshold), mark as WHITE (camera), stays forever
                if (depth > 0.01f && depth <= depthThreshold)
                {
                    if (maskPixels[i].r == 0)  // Was unscanned (black)
                    {
                        newlyScannedCount++;
                    }
                    maskPixels[i] = new Color32(255, 255, 255, 255);  // Scanned = WHITE (camera)
                }
                // DON'T reset to black - preserve spatial coverage memory
            }
            else
            {
                // REAL-TIME: Mask changes every frame based on current depth
                // Show camera ONLY for areas within threshold, stripes everywhere else
                if (depth > 0.01f && depth <= depthThreshold)
                {
                    maskPixels[i] = new Color32(255, 255, 255, 255);  // Close = WHITE (camera)
                }
                else
                {
                    maskPixels[i] = new Color32(0, 0, 0, 255);  // Far/unknown = BLACK (stripes)
                }
            }
            
            // Count pixels for statistics
            if (maskPixels[i].r == 255)  // White = camera
                whiteCount++;
            else  // Black = stripes
                blackCount++;
        }
        
        // ALWAYS log mask stats to track scanning progress
        float cameraPct = (blackCount + whiteCount > 0) ? 100f * whiteCount / (blackCount + whiteCount) : 0f;
        string mode = useAccumulativeScanning ? "ACCUM" : "REALTIME";
        Debug.Log($"[STRIPE MASK] F{Time.frameCount} {mode}: Cam={whiteCount}({cameraPct:F1}%) Strip={blackCount} New={newlyScannedCount} Depth:{minDepth:F2}-{maxDepth:F2}m T:{depthThreshold}m");
        
        if (useAccumulativeScanning)
        {
            accumulativeMask = maskPixels;
        }
        
        Texture2D maskTex = new Texture2D(targetWidth, targetHeight, TextureFormat.R8, false);
        maskTex.SetPixels32(maskPixels);
        maskTex.Apply();
        return maskTex;
    }
    
    Texture2D GenerateMaskFromCPUImage(XRCpuImage image)
    {
            
        int width = image.width;
        int height = image.height;
        int targetWidth = Mathf.Min(width, textureWidth);
        int targetHeight = Mathf.Min(height, textureHeight);
        
        // Initialize accumulative mask on first run
        // SHADER LOGIC: White (255) = scanned (camera), Black (0) = unscanned (stripes)
        if (useAccumulativeScanning && (accumulativeMask == null || accumulativeMask.Length != targetWidth * targetHeight))
        {
            accumulativeMask = new Color32[targetWidth * targetHeight];
            for (int i = 0; i < accumulativeMask.Length; i++)
            {
                accumulativeMask[i] = new Color32(0, 0, 0, 255);  // Start ALL UNSCANNED (black = stripes)
            }
            Debug.Log($"[STRIPE MASK] Initialized {targetWidth}x{targetHeight} accumulative mask (CPU path) - all BLACK (unscanned/stripes)");
        }
        
        Texture2D maskTex = new Texture2D(targetWidth, targetHeight, TextureFormat.R8, false);
        var conversionParams = new XRCpuImage.ConversionParams(image, TextureFormat.RFloat);
        int dataLength = width * height * sizeof(float);
        var rawDepthData = new Unity.Collections.NativeArray<byte>(dataLength, Unity.Collections.Allocator.Temp);
        image.Convert(conversionParams, rawDepthData);
        image.Dispose();
        
        Color32[] maskPixels;
        if (useAccumulativeScanning)
        {
            maskPixels = accumulativeMask;  // Work on persistent mask
        }
        else
        {
            maskPixels = new Color32[targetWidth * targetHeight];  // Fresh mask each frame
        }
        
        int blackCount = 0;  // Camera pixels
        int whiteCount = 0;  // Stripe pixels
        int newlyScannedCount = 0;  // Pixels scanned this frame
        float minDepth = float.MaxValue;
        float maxDepth = 0f;
        
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
                        int maskIdx = y * targetWidth + x;
                        
                        if (depth > 0.01f)
                        {
                            if (depth < minDepth) minDepth = depth;
                            if (depth > maxDepth) maxDepth = depth;
                        }
                        
                        if (useAccumulativeScanning)
                        {
                            // ACCUMULATIVE: Once scanned (depth within threshold), mark as WHITE (camera), stays forever
                            if (depth > 0.01f && depth <= depthThreshold)
                            {
                                if (maskPixels[maskIdx].r == 0)  // Was unscanned (black)
                                {
                                    newlyScannedCount++;
                                }
                                maskPixels[maskIdx] = new Color32(255, 255, 255, 255);  // Scanned = WHITE (camera)
                            }
                            // DON'T reset to black - preserve spatial coverage memory
                        }
                        else
                        {
                            // REAL-TIME: Mask changes every frame based on current depth
                            if (depth > 0.01f && depth <= depthThreshold)
                            {
                                maskPixels[maskIdx] = new Color32(255, 255, 255, 255);  // Scanned = WHITE (camera)
                            }
                            else
                            {
                                maskPixels[maskIdx] = new Color32(0, 0, 0, 255);  // Unscanned = BLACK (stripes)
                            }
                        }
                        
                        // Count pixels for statistics
                        if (maskPixels[maskIdx].r == 255)  // White = camera
                            whiteCount++;
                        else  // Black = stripes
                            blackCount++;
                    }
                }
            }
        }
        
        if (Time.frameCount % 60 == 0)
        {
            float cameraPct = 100f * whiteCount / (blackCount + whiteCount);
            string mode = useAccumulativeScanning ? "ACCUMULATIVE CPU" : "REAL-TIME CPU";
            Debug.Log($"[STRIPE MASK] Frame {Time.frameCount} {mode}: Camera={whiteCount} ({cameraPct:F1}%), Stripes={blackCount}, " +
                     $"Newly scanned: {newlyScannedCount}, Depth: {minDepth:F2}-{maxDepth:F2}m, Threshold: {depthThreshold}m");
        }
        
        if (useAccumulativeScanning)
        {
            accumulativeMask = maskPixels;  // Save back to persistent storage
        }
        
        maskTex.SetPixels32(maskPixels);
        maskTex.Apply();
        rawDepthData.Dispose();
        return maskTex;
    }
    
    /// <summary>
    /// Generate spatial coverage mask from TSDF weight volume (IntelliCap method).
    /// Areas with high TSDF weight = well-reconstructed = show camera feed.
    /// Areas with low TSDF weight = unreliable/unscanned = show stripes.
    /// </summary>
    Texture2D GenerateMaskFromTSDFWeights()
    {
        int targetWidth = textureWidth;
        int targetHeight = textureHeight;
        
        Texture2D maskTex = new Texture2D(targetWidth, targetHeight, TextureFormat.R8, false);
        Color32[] maskPixels = new Color32[targetWidth * targetHeight];
        
        Camera cam = GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogError("[STRIPE MASK] Camera component not found!");
            return null;
        }
        
        int blackCount = 0;  // Stripes (unscanned/low weight)
        int whiteCount = 0;  // Camera (well-scanned/high weight)
        float minWeight = float.MaxValue;
        float maxWeight = float.MinValue;
        
        // Sample TSDF weight for each pixel
        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                int idx = y * targetWidth + x;
                
                // Convert pixel to normalized viewport coordinates (0-1)
                float u = (float)x / targetWidth;
                float v = (float)y / targetHeight;
                
                // Cast ray from camera through this pixel
                Ray ray = cam.ViewportPointToRay(new Vector3(u, v, 0));
                
                // Sample TSDF weight at a point along the ray (e.g., 1.5m from camera)
                float sampleDistance = 1.5f;  // Sample at mid-range
                Vector3 samplePoint = ray.origin + ray.direction * sampleDistance;
                
                // Get TSDF weight at this world position (using reflection to avoid assembly reference)
                float weight = 0f;
                if (tsdfAtlas != null)
                {
                    var method = tsdfAtlas.GetType().GetMethod("SampleWeightAtWorldPosition");
                    if (method != null)
                    {
                        weight = (float)method.Invoke(tsdfAtlas, new object[] { samplePoint });
                    }
                    else if (Time.frameCount % 60 == 0 && idx == 0)
                    {
                        Debug.LogWarning("[STRIPE MASK] SampleWeightAtWorldPosition method not found!");
                    }
                }
                
                // Debug log first few samples
                if (Time.frameCount % 200 == 0 && idx < 5)
                {
                    Debug.Log($"[STRIPE MASK] Sample [{x},{y}]: samplePoint={samplePoint}, weight={weight}");
                }
                
                // Track weight range (including 0)
                if (weight < minWeight) minWeight = weight;
                if (weight > maxWeight) maxWeight = weight;
                
                // Determine if area is scanned based on TSDF weight
                if (weight >= minTSDFWeight)
                {
                    maskPixels[idx] = new Color32(255, 255, 255, 255);  // WHITE = scanned (camera)
                    whiteCount++;
                }
                else
                {
                    maskPixels[idx] = new Color32(0, 0, 0, 255);  // BLACK = unscanned (stripes)
                    blackCount++;
                }
            }
        }
        
        // Log statistics
        float cameraPct = (blackCount + whiteCount > 0) ? 100f * whiteCount / (blackCount + whiteCount) : 0f;
        Debug.Log($"[STRIPE MASK] F{Time.frameCount} TSDF: Cam={whiteCount}({cameraPct:F1}%) Strip={blackCount} Weight:{minWeight:F2}-{maxWeight:F2} T:{minTSDFWeight}");
        
        maskTex.SetPixels32(maskPixels);
        maskTex.Apply();
        return maskTex;
    }
    
    /// <summary>
    /// Reset spatial coverage - all areas become unscanned again (show stripes).
    /// Useful for testing or demo purposes.
    /// </summary>
    public void ResetSpatialCoverage()
    {
        if (accumulativeMask != null)
        {
            for (int i = 0; i < accumulativeMask.Length; i++)
            {
                accumulativeMask[i] = new Color32(0, 0, 0, 255);  // Reset to BLACK = unscanned (stripes)
            }
            Debug.Log("[STRIPE MASK] Spatial coverage RESET - all areas now UNSCANNED (black = stripes)");
        }
        
        if (maskTexture != null)
        {
            Destroy(maskTexture);
            maskTexture = null;
        }
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
