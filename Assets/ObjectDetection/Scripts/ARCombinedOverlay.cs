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
    public bool useTSDFWeights = true;  // ENABLED - Using TSDF with lowered confidence threshold (0.2) for better integration
    
    [Header("Depth Threshold Method (Simple - DEPRECATED, only for fallback)")]
    [Tooltip("Depth threshold in meters - areas closer than this show camera feed")]  
    [Range(0.3f, 50.0f)]
    public float depthThreshold = 50.0f;  // 50 meters - VERY permissive, shows camera for almost everything (fallback only)
    [Tooltip("Use accumulative scanning - once scanned, area stays scanned")]
    public bool useAccumulativeScanning = true;  // ACCUMULATIVE mode: once scanned, stays scanned (persistent memory)
    
    [Header("TSDF Weight Method (IntelliCap)")]
    [Tooltip("Minimum TSDF weight to consider area scanned")]
    [Range(0.01f, 10.0f)]
    public float minTSDFWeight = 5.0f;   // Requires ~17 integration frames (~5-6 s of direct observation) before area clears — suitable for outdoor scanning
    [Tooltip("TSDFVolumeAtlas component for weight-based spatial coverage")]
    public MonoBehaviour tsdfAtlas;  // Auto-found if not assigned (uses MonoBehaviour to avoid assembly reference issues)
    [Tooltip("Shader for generating coverage mask from TSDF weights (GPU-accelerated)")]
    public Shader tsdfWeightMaskShader;  // Assign TSDFWeightCoverageMask shader in Inspector
    
    private int textureWidth = 128;
    private int textureHeight = 128;
#pragma warning disable 0414 // legacy fields retained for potential reuse
    private float depthModeEnabledTime = -1f;
    private bool tsdfAutoFallbackTriggered = false;
    private Color32[] tsdfLastValidMaskPixels;
#pragma warning restore 0414
    private Color32[] accumulativeMask;  // Persistent spatial coverage memory
    private int tsdfAtlasRetryCount = 0;  // Retry finding TSDF atlas in first few frames
    private Material tsdfWeightMaskMaterial;  // GPU material for TSDF weight coverage masking
    private int tsdfZeroWeightCount = 0;  // Track consecutive frames with zero TSDF weights (for auto-fallback)
    private System.IntPtr tsdfLastDepthNativePtr = System.IntPtr.Zero;  // Detects when ARCore depth texture actually updates
    
    [Header("YOLO Detection Settings")]
    public bool enableObjectDetection = true;
    [Tooltip("YOLOv8 ONNX model file")]
    public NNModel yoloModel;
    [Range(0.0f, 1f)]
    public float minConfidence = 0.60f;  // 60% confidence - reduces floor/background false positives
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
    [Tooltip("Show TSDF weight gradient instead of binary mask. Well-scanned areas (high weight) show full camera feed; barely-scanned areas blend in stripes. Reveals geometry alignment mismatches.")]
    public bool debugGeometryAlignment = false;
    
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
    private RenderTexture coverageMaskRT; // Persistent GPU RT updated every frame — no CPU readback
    private Material blitMaterial;
    private AROcclusionManager occlusionManager;
#pragma warning disable 0414
    private int stripeFrameCounter = 0; // retained; was used by old CPU readback path
#pragma warning restore 0414
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

        // ARVolumetricBlend self-disables its depth colorizer when it detects ARCombinedOverlay is active.
        // (handled in ARVolumetricBlend.LateUpdate — no action needed here)

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
            // FORCE all values at runtime - Inspector serialized values are always wrong after rebuilds
            useTSDFWeights = true;
            minTSDFWeight = 5.0f;       // ~17 integrations (~5-6s continuous scan) before stripes clear — genuine outdoor look-around required
            
            Debug.Log("[STRIPE] Runtime override: useTSDFWeights=true, minTSDFWeight=2.0 (requires real scanning before stripes clear)");
            
            // Auto-find TSDF atlas
            if (tsdfAtlas == null)
            {
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
                    Debug.Log($"[STRIPE] ✅ Auto-found TSDFVolumeAtlas for spatial coverage");
                else
                    Debug.LogWarning($"[STRIPE] ⚠️ TSDFVolumeAtlas not found - stripes will show everywhere until found");
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
                
                // All values already forced above
                Debug.Log($"[STRIPE] ✅ METHOD: TSDF WEIGHTS ONLY | minWeight={minTSDFWeight} | atlas={(tsdfAtlas != null ? tsdfAtlas.GetType().Name : "NOT FOUND YET - stripes until ready")}");
                Debug.Log($"[STRIPE] ✅ SPATIAL COVERAGE: new angle = STRIPES, seen area = CAMERA FEED");
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
            
            // Enforce minimum confidence at 0.60 — values below this let through enough
            // floor-texture false positives (vent covers, floor patterns, shoes) to place
            // spurious spheres on the ground. 60% eliminates most of these without
            // losing legitimate high-confidence object detections.
            if (minConfidence < 0.60f)
            {
                Debug.LogWarning($"[YOLO] Confidence threshold {minConfidence} below 0.60, forcing up to reduce floor false positives");
                minConfidence = 0.60f;
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
            // Include inactive/disabled components (true parameter) since SpatialCoverageToggle disables them by default
            MonoBehaviour[] allMonoBehaviours = GameObject.FindObjectsOfType<MonoBehaviour>(true);
            foreach (MonoBehaviour mb in allMonoBehaviours)
            {
                if (mb.GetType().Name == "TSDFVolumeAtlas")
                {
                    tsdfAtlas = mb;
                    Debug.Log($"[STRIPE] ✅✅✅ FOUND TSDFVolumeAtlas on frame {Time.frameCount} (retry #{tsdfAtlasRetryCount})");
                    Debug.Log($"[STRIPE] 📊 TSDF WEIGHTS MODE ACTIVATED (IntelliCap method)");
                    Debug.Log($"[STRIPE] Atlas object: {mb.gameObject.name}, Component: {mb.GetType().FullName}, Enabled: {mb.enabled}");
                    break;
                }
            }
            
            // Keep retrying indefinitely - show stripes until atlas is found (correct: unscanned)
            if (tsdfAtlasRetryCount >= 60 && tsdfAtlas == null && tsdfAtlasRetryCount % 60 == 0)
            {
                Debug.LogWarning($"[STRIPE] ⚠️ TSDFVolumeAtlas not found yet after {tsdfAtlasRetryCount} frames - still showing stripes.");
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
        if (Time.frameCount % 90 == 0)
        {
            Debug.Log($"[STRIPE CHECK] F{Time.frameCount} About to check: enableStripes={enableStripes}, blitMat={blitMaterial != null}, stripeTex={stripeTexture != null}");
        }
        
        if (enableStripes && blitMaterial != null && stripeTexture != null)
        {
            temp2 = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
            ApplyStripeOverlay(current, temp2);
            current = temp2;
        }
        else if (Time.frameCount % 90 == 0)
        {
            Debug.LogWarning($"[STRIPE CHECK] F{Time.frameCount} SKIPPED stripe overlay: enableStripes={enableStripes}, blitMat={blitMaterial != null}, stripeTex={stripeTexture != null}");
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
        // SAFETY: If shader or stripe texture not ready, show camera feed
        if (blitMaterial == null || stripeTexture == null)
        {
            Graphics.Blit(src, dest);
            return;
        }

        // Ensure persistent GPU coverage mask RT exists (avoids per-frame GC allocation)
        if (coverageMaskRT == null || !coverageMaskRT.IsCreated())
        {
            coverageMaskRT = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.R8);
            coverageMaskRT.name = "CoverageMaskRT";
            coverageMaskRT.Create();
            Debug.Log($"[STRIPE MASK] Created persistent coverageMaskRT {textureWidth}x{textureHeight}");
        }

        // Render TSDF coverage mask every frame directly to a GPU RT (no CPU readback).
        // Camera matrices (_InvViewMatrix, _InvProjMatrix) change every frame as the device
        // moves — re-rendering each frame keeps the mask correctly aligned with the view.
        if (useTSDFWeights && tsdfAtlas != null && tsdfAtlas.enabled)
        {
            if (RenderCoverageMaskToRT())
                hasDepthData = true;
        }

        // Show plain camera feed while waiting for first depth frame
        if (showCameraUntilScanned && !hasDepthData)
        {
            Graphics.Blit(src, dest);
            return;
        }

        // No depth data yet → full stripes (correct: scene is entirely unscanned)
        if (!hasDepthData)
        {
            blitMaterial.SetTexture("_CameraTex", src);
            blitMaterial.SetTexture("_MaskTex", Texture2D.blackTexture);
            blitMaterial.SetTexture("_StripeTex", stripeTexture);
            Graphics.Blit(src, dest, blitMaterial);
            return;
        }

        if (Time.frameCount % 90 == 0)
            Debug.Log($"[STRIPE OVERLAY] F{Time.frameCount} GPU mask {coverageMaskRT?.width}x{coverageMaskRT?.height} debugGeom={debugGeometryAlignment}");

        // Composite: camera feed where TSDF weight >= threshold, stripes where unscanned.
        // When debugGeometryAlignment=true the shader outputs a weight gradient (not binary)
        // so well-scanned areas show full camera and barely-scanned areas blend in stripes,
        // making geometry alignment mismatches visually obvious.
        blitMaterial.SetTexture("_CameraTex", src);
        blitMaterial.SetTexture("_MaskTex", coverageMaskRT);
        blitMaterial.SetTexture("_StripeTex", stripeTexture);
        Graphics.Blit(src, dest, blitMaterial);
    }

    /// <summary>
    /// Renders the TSDF weight coverage mask every frame to a persistent GPU RenderTexture.
    /// No CPU ReadPixels — the RenderTexture is passed directly to the composite shader.
    /// Returns true when the GPU blit succeeded with valid TSDF data.
    /// </summary>
    bool RenderCoverageMaskToRT()
    {
        try
        {
            if (tsdfAtlas == null) return false;

            Texture depthTex = occlusionManager?.environmentDepthTexture;
            if (depthTex == null) return false;

            // Get TSDF weight atlas via reflection
            var getWeightMethod = tsdfAtlas.GetType().GetMethod("GetWeightAtlas");
            if (getWeightMethod == null) return false;
            RenderTexture weightAtlas = getWeightMethod.Invoke(tsdfAtlas, null) as RenderTexture;
            if (weightAtlas == null) return false;

            // Initialize coverage mask material once
            if (tsdfWeightMaskMaterial == null)
            {
                if (tsdfWeightMaskShader == null)
                    tsdfWeightMaskShader = Shader.Find("TSDFWeightCoverageMask");
                if (tsdfWeightMaskShader == null)
                {
                    Debug.LogError("[STRIPE MASK] TSDFWeightCoverageMask shader not found!");
                    return false;
                }
                tsdfWeightMaskMaterial = new Material(tsdfWeightMaskShader);
                Debug.Log("[STRIPE MASK] Created TSDFWeightCoverageMask material");
            }

            // Get TSDF volume parameters via reflection
            var volumeOrigin  = (Vector3)tsdfAtlas.GetType().GetProperty("volumeCenter").GetValue(tsdfAtlas);
            var volumeRes     = (Vector3Int)tsdfAtlas.GetType().GetMethod("GetVolumeResolution").Invoke(tsdfAtlas, null);
            float voxelSize   = (float)tsdfAtlas.GetType().GetMethod("GetVoxelSize").Invoke(tsdfAtlas, null);
            int slicesPerRow  = (int)tsdfAtlas.GetType().GetMethod("GetSlicesPerRow").Invoke(tsdfAtlas, null);
            int sliceRowCount = Mathf.CeilToInt((float)volumeRes.z / slicesPerRow);

            // Per-frame shader parameters (camera matrices change each frame with device movement)
            Camera cam = GetComponent<Camera>();
            tsdfWeightMaskMaterial.SetTexture("_WeightAtlas", weightAtlas);
            tsdfWeightMaskMaterial.SetTexture("_DepthTex", depthTex);
            tsdfWeightMaskMaterial.SetFloat("_WeightThreshold", minTSDFWeight);
            tsdfWeightMaskMaterial.SetVector("_VolumeOrigin", volumeOrigin);
            tsdfWeightMaskMaterial.SetVector("_VolumeResolution", new Vector3(volumeRes.x, volumeRes.y, volumeRes.z));
            tsdfWeightMaskMaterial.SetFloat("_VoxelSize", voxelSize);
            tsdfWeightMaskMaterial.SetInt("_SlicesPerRow", slicesPerRow);
            tsdfWeightMaskMaterial.SetInt("_SliceRowCount", sliceRowCount);
            tsdfWeightMaskMaterial.SetMatrix("_InvViewMatrix", cam.cameraToWorldMatrix);
            tsdfWeightMaskMaterial.SetMatrix("_InvProjMatrix", cam.projectionMatrix.inverse);
            tsdfWeightMaskMaterial.SetFloat("_DebugWeights", debugGeometryAlignment ? 1f : 0f);

            // GPU blit to persistent RT — no CPU ReadPixels, no GC pressure
            Graphics.Blit(null, coverageMaskRT, tsdfWeightMaskMaterial);

            if (Time.frameCount % 90 == 0)
                Debug.Log($"[STRIPE MASK] F{Time.frameCount} TSDF GPU mask rendered, debugGeom={debugGeometryAlignment}");

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[STRIPE MASK] RenderCoverageMaskToRT failed: {e.Message}");
            return false;
        }
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
        // DEBUG: Always log when this is called
        if (Time.frameCount % 90 == 0)
        {
            Debug.Log($"[MASK GEN] F{Time.frameCount} GenerateMaskTexture() CALLED - useTSDFWeights={useTSDFWeights}, tsdfAtlas={(tsdfAtlas != null)}, atlas.enabled={(tsdfAtlas != null ? tsdfAtlas.enabled.ToString() : "N/A")}");
        }
        
        // AUTO-FALLBACK: Disabled - showing stripes (black mask) IS the correct behavior for unseen areas.
        // TSDF takes time to warm up; while empty, we WANT to show stripes everywhere to indicate not-yet-scanned.
        // Only fall back if TSDF atlas is completely gone (nulled/destroyed), handled below in routing.
        // Recovery: if TSDF previously failed but now has data, re-enable it.
        if (!useTSDFWeights && tsdfAtlas != null && tsdfAtlas.enabled && tsdfZeroWeightCount == 0)
        {
            Debug.Log($"[MASK GEN] ✅ TSDF RECOVERY: Re-enabling TSDF weights (was in fallback but now has data)");
            useTSDFWeights = true;
            tsdfAutoFallbackTriggered = false;
        }
        
        // ROUTE: Choose spatial coverage method
        // Check both that atlas exists AND is enabled (SpatialCoverageToggle controls this)
        if (useTSDFWeights && tsdfAtlas != null && tsdfAtlas.enabled)
        {
            // IntelliCap method: Use TSDF weight volume
            if (Time.frameCount == 20 || Time.frameCount == 50 || Time.frameCount == 100 || Time.frameCount % 300 == 0)
            {
                Debug.Log($"[STRIPE MASK] F{Time.frameCount} ✅✅✅ Using TSDF WEIGHTS path (useTSDFWeights={useTSDFWeights}, atlas={tsdfAtlas != null}, atlas.enabled={tsdfAtlas.enabled})");
            }
            
            Texture2D tsdfMask = GenerateMaskFromTSDFWeights();
            if (tsdfMask != null)
            {
                return tsdfMask;
            }
            
            // TSDF failed (shader error etc.) - return null so stripes show everywhere (correct: area is unscanned)
            if (Time.frameCount % 90 == 0)
                Debug.LogWarning($"[STRIPE MASK] F{Time.frameCount} TSDF returned null - showing stripes (unscanned)");
            return null;
        }
        
        // TSDF atlas not yet found - return null so stripes show everywhere while waiting
        if (Time.frameCount % 90 == 0)
            Debug.Log($"[STRIPE MASK] F{Time.frameCount} Waiting for TSDF atlas... showing stripes until ready.");
        return null;
    }
    
    // GenerateMaskFromGPUTexture and GenerateMaskFromCPUImage removed - TSDF is the only spatial coverage method.
    // Returning null from GenerateMaskTexture = stripes everywhere (correct for unscanned areas).

    Texture2D _REMOVED_GenerateMaskFromGPUTexture(Texture depthTex)
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
        
        // SAFETY: If entire mask is black, create a DEFAULT WHITE MASK (show camera everywhere)
        // This prevents pink-white stripes from covering the entire screen
        if (whiteCount == 0)
        {
            Debug.LogError($"[STRIPE MASK] ❌❌❌ ENTIRE MASK IS BLACK! Creating default WHITE mask (show camera everywhere)");
            Debug.LogError($"[STRIPE MASK] Reason: All depth values > {depthThreshold}m or invalid. Range was: {minDepth:F2}-{maxDepth:F2}m");
            
            // Create all-white mask = show camera everywhere (no stripes)
            for (int i = 0; i < maskPixels.Length; i++)
            {
                maskPixels[i] = new Color32(255, 255, 255, 255);
            }
            whiteCount = maskPixels.Length;
            blackCount = 0;
        }
        
        if (useAccumulativeScanning)
        {
            accumulativeMask = maskPixels;
        }
        
        Texture2D maskTex = new Texture2D(targetWidth, targetHeight, TextureFormat.R8, false);
        maskTex.SetPixels32(maskPixels);
        maskTex.Apply();
        return maskTex;
    }
    
    Texture2D _REMOVED_GenerateMaskFromCPUImage(XRCpuImage image)
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
    /// Generate spatial coverage mask from TSDF weight volume (IntelliCap method) - GPU accelerated.
    /// Areas with high TSDF weight = well-reconstructed = show camera feed.
    /// Areas with low TSDF weight = unreliable/unscanned = show stripes.
    /// </summary>
    Texture2D GenerateMaskFromTSDFWeights()
    {
        try
        {
            // Log entry on first few frames
            if (Time.frameCount <= 120 && Time.frameCount % 30 == 0)
            {
                Debug.Log($"[STRIPE MASK TSDF] F{Time.frameCount} GenerateMaskFromTSDFWeights() CALLED");
            }
            
            if (tsdfAtlas == null)
            {
                Debug.LogError("[STRIPE MASK TSDF] ❌ TSDF atlas is null!");
                return null;
            }
            
            if (occlusionManager == null)
            {
                Debug.LogError("[STRIPE MASK TSDF] ❌ OcclusionManager is null!");
                return null;
            }
            
            Texture depthTex = occlusionManager.environmentDepthTexture;
            if (depthTex == null)
            {
                if (Time.frameCount % 90 == 0)
                {
                    Debug.LogWarning("[STRIPE MASK TSDF] ⚠️ Depth texture is null (ARCore depth not ready)!");
                }
                return null;
            }
            
            // Get TSDF weight atlas via reflection
            var getWeightMethod = tsdfAtlas.GetType().GetMethod("GetWeightAtlas");
            if (getWeightMethod == null)
            {
                Debug.LogError("[STRIPE MASK TSDF] ❌ GetWeightAtlas() method not found on TSDF atlas!");
                return null;
            }
            
            RenderTexture weightAtlas = getWeightMethod.Invoke(tsdfAtlas, null) as RenderTexture;
            if (weightAtlas == null)
            {
                if (Time.frameCount % 90 == 0)
                {
                    Debug.LogWarning("[STRIPE MASK TSDF] ⚠️ Weight atlas is null (TSDF may not be initialized yet). Waiting...");
                }
                return null;
            }
            
            // First successful call
            if (Time.frameCount <= 120 && Time.frameCount % 30 == 0)
            {
                Debug.Log($"[STRIPE MASK TSDF] ✅ Weight atlas found: {weightAtlas.width}x{weightAtlas.height}");
            }
            
            // Initialize material if needed
            if (tsdfWeightMaskMaterial == null)
            {
                if (tsdfWeightMaskShader == null)
                {
                    tsdfWeightMaskShader = Shader.Find("TSDFWeightCoverageMask");
                    if (tsdfWeightMaskShader == null)
                    {
                        Debug.LogError("[STRIPE MASK TSDF] ❌❌❌ TSDFWeightCoverageMask shader NOT FOUND!");
                        Debug.LogError("[STRIPE MASK TSDF] ❌ Looking for: 'TSDFWeightCoverageMask'");
                        Debug.LogError("[STRIPE MASK TSDF] ❌ Make sure shader exists at: Assets/ObjectDetection/Shaders/TSDFWeightCoverageMask.shader");
                        return null;
                    }
                    Debug.Log("[STRIPE MASK TSDF] ✅ Found TSDFWeightCoverageMask shader");
                }
                
                tsdfWeightMaskMaterial = new Material(tsdfWeightMaskShader);
                Debug.Log($"[STRIPE MASK TSDF] ✅✅✅ Created TSDF weight coverage material (GPU-accelerated)");
                Debug.Log($"[STRIPE MASK TSDF] Shader name: {tsdfWeightMaskShader.name}");
            }
        
        // Get TSDF volume parameters via reflection
        var getVolumeOriginMethod = tsdfAtlas.GetType().GetProperty("volumeCenter");
        var getVolumeResMethod = tsdfAtlas.GetType().GetMethod("GetVolumeResolution");
        var getVoxelSizeMethod = tsdfAtlas.GetType().GetMethod("GetVoxelSize");
        var getSlicesPerRowMethod = tsdfAtlas.GetType().GetMethod("GetSlicesPerRow");
        
        Vector3 volumeOrigin = (Vector3)getVolumeOriginMethod.GetValue(tsdfAtlas);
        Vector3Int volumeRes = (Vector3Int)getVolumeResMethod.Invoke(tsdfAtlas, null);
        float voxelSize = (float)getVoxelSizeMethod.Invoke(tsdfAtlas, null);
        int slicesPerRow = (int)getSlicesPerRowMethod.Invoke(tsdfAtlas, null);
        int sliceRowCount = Mathf.CeilToInt((float)volumeRes.z / slicesPerRow);
        
        // Log volume parameters on first few frames
        if (Time.frameCount <= 120 && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[STRIPE MASK TSDF] Volume params: origin={volumeOrigin}, res={volumeRes}, voxelSize={voxelSize:F4}m, slices={slicesPerRow}, sliceRows={sliceRowCount}");
            Debug.Log($"[STRIPE MASK TSDF] Weight atlas: {weightAtlas.width}x{weightAtlas.height} (expected {volumeRes.x*slicesPerRow}x{volumeRes.y*sliceRowCount}), depth: {depthTex.width}x{depthTex.height}");
        }
        
        // Setup shader parameters
        Camera cam = GetComponent<Camera>();
        tsdfWeightMaskMaterial.SetTexture("_WeightAtlas", weightAtlas);
        tsdfWeightMaskMaterial.SetTexture("_DepthTex", depthTex);
        tsdfWeightMaskMaterial.SetFloat("_WeightThreshold", minTSDFWeight);
        tsdfWeightMaskMaterial.SetVector("_VolumeOrigin", volumeOrigin);
        tsdfWeightMaskMaterial.SetVector("_VolumeResolution", new Vector3(volumeRes.x, volumeRes.y, volumeRes.z));
        tsdfWeightMaskMaterial.SetFloat("_VoxelSize", voxelSize);
        tsdfWeightMaskMaterial.SetInt("_SlicesPerRow", slicesPerRow);
        tsdfWeightMaskMaterial.SetInt("_SliceRowCount", sliceRowCount);
        tsdfWeightMaskMaterial.SetMatrix("_InvViewMatrix", cam.cameraToWorldMatrix);
        tsdfWeightMaskMaterial.SetMatrix("_InvProjMatrix", cam.projectionMatrix.inverse);
        
        // Render coverage mask using GPU shader
        int targetWidth = textureWidth;
        int targetHeight = textureHeight;
        
        RenderTexture maskRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.R8);
        Graphics.Blit(null, maskRT, tsdfWeightMaskMaterial);
        
        // Read back to CPU
        RenderTexture.active = maskRT;
        Texture2D maskTex = new Texture2D(targetWidth, targetHeight, TextureFormat.R8, false);
        maskTex.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        maskTex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(maskRT);

        // Return the raw GPU mask directly.
        // Stale-depth pixels (ARCore ~5Hz) are handled inside the shader via ray-marching:
        // the shader checks the TSDF volume along the view ray so already-scanned areas
        // show camera feed even when live depth is unavailable.
        // Log statistics
        if (Time.frameCount % 60 == 0)
        {
            Color32[] statsPixels = maskTex.GetPixels32();
            int whiteCount = 0;
            foreach (var p in statsPixels) if (p.r > 127) whiteCount++;
            float cameraPct = 100f * whiteCount / statsPixels.Length;
            Debug.Log($"[STRIPE MASK] F{Time.frameCount} TSDF GPU: Cam={cameraPct:F1}% Threshold={minTSDFWeight}");
        }

        return maskTex;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[STRIPE MASK TSDF] ❌❌❌ EXCEPTION in GenerateMaskFromTSDFWeights: {e.GetType().Name}");
            Debug.LogError($"[STRIPE MASK TSDF] Message: {e.Message}");
            Debug.LogError($"[STRIPE MASK TSDF] Stack: {e.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// Reset spatial coverage - all areas become unscanned again (show stripes).
    /// Useful for testing or demo purposes.
    /// </summary>
    public void ResetSpatialCoverage()
    {
        // Reset held-mask and depth pointer so next real depth frame starts fresh
        tsdfLastValidMaskPixels = null;
        tsdfLastDepthNativePtr = System.IntPtr.Zero;

        // Reset legacy mask too
        if (accumulativeMask != null)
        {
            for (int i = 0; i < accumulativeMask.Length; i++)
                accumulativeMask[i] = new Color32(0, 0, 0, 255);
        }
        
        if (maskTexture != null)
        {
            Destroy(maskTexture);
            maskTexture = null;
        }
        
        Debug.Log("[STRIPE MASK] Spatial coverage RESET - all areas now UNSCANNED (stripes everywhere)");
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
        if (coverageMaskRT != null) { coverageMaskRT.Release(); coverageMaskRT = null; }
    }
    
    public List<ResultBox> GetLastDetections() => lastDetections;
    public void SetConfidenceThreshold(float threshold)
    {
        minConfidence = Mathf.Clamp01(threshold);
        YOLOv8OutputReader.DiscardThreshold = minConfidence;
    }
}
