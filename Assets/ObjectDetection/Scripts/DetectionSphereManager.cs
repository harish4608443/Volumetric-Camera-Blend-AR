using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using NN;

/// <summary>
/// Manages 3D sphere generation for detected objects.
/// Places spheres in AR world space based on YOLO detections and ARCore depth data.
/// </summary>
public class DetectionSphereManager : MonoBehaviour
{
    [Header("Sphere Settings")]
    [Tooltip("Sphere prefab to instantiate. Leave empty for default Unity sphere primitive. Use IntelliCap's LODManager prefab here.")]
    public GameObject spherePrefab;
    public bool enableSpheres = true;
    public float sphereLifetime = 2.0f; // How long spheres stay visible
    public float minSphereScale = 0.1f;
    public float maxSphereScale = 2.0f;
    [Tooltip("Scaling factor for sphere size. Supervisor spec: radius = max(width, height). Default 1.0 means radius equals max dimension.")]
    [Range(0.5f, 3.0f)]
    public float sphereScaleFactor = 1.0f; // Supervisor spec: radius = max_dimension
    [Tooltip("Optional material for spheres. Leave empty for default transparent material.")]
    public Material sphereMaterial;
    public float fallbackDepth = 1.5f; // Default depth (meters) when ARCore depth unavailable
    public bool useDepthFallback = false; // DISABLED - only create spheres with valid raycast/ARCore depth
    [Tooltip("Use TSDF volume for sphere depth instead of ARCore raycast")]
    public bool useTSDFDepth = false;
    private UnityEngine.Component tsdfVolume; // Use Component to avoid assembly reference
    
    [Header("AR Components")]
    private AROcclusionManager occlusionManager;
    private ARRaycastManager raycastManager;
    private Camera arCamera;
    private bool depthEverAcquired = false; // Track if we've ever successfully got depth data
    private Vector3 lastCameraPosition = Vector3.zero; // Track camera movement to reject unreliable fallback spheres
    private Material depthRenderingMaterial; // RED rendering (compositor converts to dark blue)
    private Material smallSphereMaterial; // GREEN rendering (compositor converts to bright blue)
    // Note: DepthSource not accessible due to assembly definition separation
    // Will use direct TryAcquire and raycasting instead
    
    [Header("Sphere Rendering (Layer-based like IntelliCap)")]
    private Camera sphereCamera; // Dedicated camera for sphere-only rendering
    private RenderTexture sphereRenderTexture;
    private Material sphereCompositeMaterial;
    private GameObject sphereCameraObj;
    private const int SPHERE_LAYER = 0;  // Use default layer for direct AR camera rendering // Layer for sphere objects
    private const string SPHERE_LAYER_NAME = "Spheres";
    
    [Header("Sphere Tracking")]
    private List<SphereInstance> activeSpheres = new List<SphereInstance>();
    private GameObject sphereContainer;
    private int sphereCounter = 0;
    
    private class SphereInstance
    {
        public GameObject gameObject;
        public float timestamp;
        public int detectionId;
        public Vector3 worldPosition;
        public string classLabel;
        public float radius; // Sphere radius in meters (scale / 2)
        public int lastSeenFrame; // Track when this sphere was last matched to a detection
        public Rect screenBounds; // Screen-space bounds for overlap checking
        
        public SphereInstance(GameObject go, float time, int id, Vector3 pos, string label, float rad, Rect bounds)
        {
            gameObject = go;
            timestamp = time;
            detectionId = id;
            worldPosition = pos;
            classLabel = label;
            radius = rad;
            lastSeenFrame = Time.frameCount;
            screenBounds = bounds;
        }
    }
    
    void Start()
    {
        arCamera = Camera.main;
        if (arCamera == null)
        {
            arCamera = FindObjectOfType<Camera>();
        }
        
        // CRITICAL: Check all required AR components for depth
        var arCameraBackground = arCamera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();
        if (arCameraBackground == null)
        {
            Debug.LogError("[DEPTH] ARCameraBackground NOT found on AR Camera - REQUIRED for depth!");
            arCameraBackground = arCamera.gameObject.AddComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();
            Debug.Log("[DEPTH] Added ARCameraBackground to AR Camera");
        }
        
        var arCameraManager = FindObjectOfType<UnityEngine.XR.ARFoundation.ARCameraManager>();
        if (arCameraManager == null)
        {
            Debug.LogError("[DEPTH] ARCameraManager NOT found - REQUIRED for depth!");
        }
        else
        {
            Debug.Log("[DEPTH] ARCameraManager found and enabled");
        }
        
        var arSession = FindObjectOfType<UnityEngine.XR.ARFoundation.ARSession>();
        if (arSession == null)
        {
            Debug.LogError("[DEPTH] ARSession NOT found - REQUIRED for depth!");
        }
        else
        {
            Debug.Log($"[DEPTH] ARSession found, state: {UnityEngine.XR.ARFoundation.ARSession.state}");
        }
        
        occlusionManager = FindObjectOfType<AROcclusionManager>();
        if (occlusionManager == null)
        {
            Debug.LogError("DetectionSphereManager: AROcclusionManager not found!");
            enableSpheres = false;
            return;
        }
        
        // Find ARRaycastManager for surface-based depth estimation (fallback)
        raycastManager = FindObjectOfType<ARRaycastManager>();
        if (raycastManager == null)
        {
            Debug.LogWarning("[RAYCAST] ARRaycastManager not found - will use fallback depth only");
        }
        else
        {
            Debug.Log("[RAYCAST] ARRaycastManager found - available as fallback");
        }
        
        // Log occlusion manager state
        Debug.Log($"[DEPTH] AROcclusionManager found: enabled={occlusionManager.enabled}");
        Debug.Log($"[DEPTH] Environment depth mode: {occlusionManager.requestedEnvironmentDepthMode}");
        Debug.Log($"[DEPTH] Current environment depth mode: {occlusionManager.currentEnvironmentDepthMode}");
        Debug.Log($"[DEPTH] Occlusion preference mode: {occlusionManager.requestedOcclusionPreferenceMode}");
        
        // Check if device supports depth - check subsystem directly
        var subsystem = occlusionManager.subsystem;
        if (subsystem != null)
        {
            Debug.Log($"[DEPTH] Occlusion subsystem running: {subsystem.running}");
            var descriptor = subsystem.subsystemDescriptor;
            if (descriptor != null)
            {
                Debug.Log($"[DEPTH] Subsystem descriptor: {descriptor.id}");
                // Note: supportsEnvironmentDepth not available in Unity 2020.3, check by trying to acquire
            }
        }
        else
        {
            Debug.LogWarning("[DEPTH] Occlusion subsystem is null - may not be initialized yet");
        }
        
        // Try to enable depth if not already enabled
        if (occlusionManager.currentEnvironmentDepthMode == UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Disabled)
        {
            Debug.LogWarning("[DEPTH] Depth mode is disabled, attempting to enable...");
            occlusionManager.requestedEnvironmentDepthMode = UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Fastest;
        }
        
        // Force enable occlusion manager
        occlusionManager.enabled = true;
        
        // Find TSDF volume if using TSDF depth (use reflection to avoid assembly reference)
        if (useTSDFDepth)
        {
            var tsdfVolumeType = System.Type.GetType("TSDFVolumeAtlas");
            if (tsdfVolumeType != null)
            {
                tsdfVolume = FindObjectOfType(tsdfVolumeType) as UnityEngine.Component;
                if (tsdfVolume == null)
                {
                    Debug.LogWarning("[SPHERE] useTSDFDepth=true but TSDFVolumeAtlas not found - falling back to ARCore depth");
                    useTSDFDepth = false;
                }
                else
                {
                    Debug.Log("[SPHERE] Using TSDF volume for sphere depth placement");
                }
            }
            else
            {
                Debug.LogWarning("[SPHERE] TSDFVolumeAtlas type not found - falling back to ARCore depth");
                useTSDFDepth = false;
            }
        }
        
        // Create container for all spheres (NO PARENT - keep in world space so spheres stay fixed)
        sphereContainer = new GameObject("DetectionSpheres");
        // DO NOT parent to transform - spheres must remain fixed in world space when camera moves
        // sphereContainer.transform.parent = transform;
        sphereContainer.layer = SPHERE_LAYER; // Sphere layer for separate rendering
        
        // Setup sphere camera and rendering system (IntelliCap approach)
        SetupSphereCamera();
        
        Debug.Log("DetectionSphereManager initialized");
    }
    
    void Update()
    {
        if (!enableSpheres) return;
        
        // Spheres render directly on Layer 0 - no camera sync needed
        
        // Remove oldest spheres if we have too many (keep recent ones)
        if (activeSpheres.Count > 30)
        {
            int toRemove = activeSpheres.Count - 30;
            Debug.Log($"🧹 Cleaning up {toRemove} oldest spheres (keeping newest 30)");
            
            for (int i = 0; i < toRemove; i++)
            {
                if (activeSpheres[0].gameObject != null)
                {
                    Destroy(activeSpheres[0].gameObject);
                }
                activeSpheres.RemoveAt(0);
            }
        }
        
        // Clean up null references
        activeSpheres.RemoveAll(s => s.gameObject == null);
        
        // Merge overlapping spheres multiple times per frame (more aggressive)
        // Original IntelliCap keeps merging nearby spheres continuously
        for (int i = 0; i < 3; i++)
        {
            MergeOverlappingSpheres();
        }
    }
    
    /// <summary>
    /// Create spheres for a list of YOLO detections.
    /// </summary>
    public void CreateSpheresForDetections(List<ResultBox> detections, int screenWidth, int screenHeight, float cropScaleRatio, float cropOffsetX, float cropOffsetY)
    {
        if (!enableSpheres)
        {
            Debug.Log("[SPHERE] Sphere generation disabled (enableSpheres=false)");
            return;
        }
        
        // If no detections, remove spheres not seen for 10 frames (camera might be held still with no objects)
        if (detections == null || detections.Count == 0)
        {
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log("[SPHERE] No detections this frame");
            }
            CleanupOldSpheres(10);
            return;
        }
        
        Debug.Log($"[SPHERE] Processing {detections.Count} detections for sphere creation");
        
        foreach (var detection in detections)
        {
            CreateSphereForDetection(detection, screenWidth, screenHeight, cropScaleRatio, cropOffsetX, cropOffsetY);
        }
        
        // Immediately merge overlapping spheres after creating new ones
        MergeOverlappingSpheres();
        
        // Remove spheres that haven't been matched to any detection for 30 frames (~1 second at 30fps)
        // This ensures only currently detected objects have spheres
        CleanupOldSpheres(30);
    }
    
    /// <summary>
    /// Check if a screen position is inside or near an existing sphere (screen space check).
    /// </summary>
    private bool IsPositionCoveredBySphere(Vector2 screenPos, string classLabel, float radiusMargin = 1.2f)
    {
        foreach (var sphere in activeSpheres)
        {
            if (sphere.gameObject == null) continue;
            
            // Only check spheres of same class (prevents detecting mugs through mug sphere)
            if (sphere.classLabel != classLabel) continue;
            
            // Project sphere world position to screen
            Vector3 screenPoint = arCamera.WorldToScreenPoint(sphere.worldPosition);
            
            // Check if in front of camera
            if (screenPoint.z < 0) continue;
            
            // Calculate screen-space distance
            float screenDist = Vector2.Distance(new Vector2(screenPoint.x, screenPoint.y), screenPos);
            
            // Sphere's screen-space radius (approximate)
            float screenRadius = (sphere.radius / screenPoint.z) * arCamera.pixelHeight * 0.5f;
            
            // Check if detection is within sphere's screen projection (with margin)
            if (screenDist < screenRadius * radiusMargin)
            {
                return true; // Position covered by existing sphere
            }
        }
        return false;
    }
    
    /// <summary>
    /// Create a single sphere for a detection.
    /// </summary>
    private void CreateSphereForDetection(ResultBox detection, int screenWidth, int screenHeight, float cropScaleRatio, float cropOffsetX, float cropOffsetY)
    {
        Debug.Log($"\n🔵 === SPHERE CREATION START === Class:{detection.bestClassIndex} Conf:{detection.score:P0}");
        
        // Calculate center of bounding box in YOLO 640x640 space
        float centerX = detection.rect.x + detection.rect.width / 2f;
        float centerY = detection.rect.y + detection.rect.height / 2f;
        
        // Map from 640x640 crop space back to original screen space
        float scaledX = centerX + cropOffsetX;
        float scaledY = centerY + cropOffsetY;
        float mappedX = scaledX / cropScaleRatio;
        float mappedY = scaledY / cropScaleRatio;
        
        // Apply Y-flip for Unity screen space (Y is inverted)
        Vector2 screenPos = new Vector2(mappedX, screenHeight - mappedY);
        
        Debug.Log($"Sphere coords: YOLO({centerX:F1},{centerY:F1}) -> Scaled({scaledX:F1},{scaledY:F1}) -> Screen({mappedX:F1},{mappedY:F1})");
        
        // CRITICAL: Check if this position is already covered by an existing sphere
        // Prevents detecting mirrored/refracted objects through transparent spheres
        if (IsPositionCoveredBySphere(screenPos, detection.bestClassIndex.ToString()))
        {
            Debug.Log($"⚠️ Skipping detection at {screenPos} - position already covered by existing sphere (prevents mirroring detection)");
            return;
        }
        
        // Get depth at this position - priority: Direct TryAcquire > Raycast > Fallback
        float depth = -1f;
        
        // Priority 1: Try TSDF depth (if enabled)
        if (useTSDFDepth && tsdfVolume != null)
        {
            depth = GetDepthFromTSDF(screenPos, screenWidth, screenHeight);
            if (depth > 0f && depth < 10f)
            {
                Debug.Log($"📊 Using TSDF depth {depth:F2}m at screen {screenPos}");
            }
        }
        
        // Priority 2: Try direct ARCore depth acquisition
        if (depth <= 0f || depth > 10f)
        {
            depth = GetDepthAtScreenPosition(screenPos, screenWidth, screenHeight);
            bool usedARCoreDepth = (depth > 0f && depth < 10f);
            if (usedARCoreDepth)
            {
                Debug.Log($"✅ ARCore depth: {depth:F2}m at screen {screenPos}");
            }
        }
        
        // Priority 3: Try AR raycasting to find actual surface
        if (depth <= 0f || depth > 10f)
        {
            depth = GetDepthViaRaycast(screenPos);
            if (depth > 0f && depth < 10f)
            {
                Debug.Log($"🎯 Using raycast depth {depth:F2}m at screen {screenPos} (ARCore unavailable)");
            }
        }
        
        // Priority 4: Skip sphere creation if no valid depth found
        if (depth <= 0f || depth > 10f)
        {
            if (useDepthFallback)
            {
                depth = fallbackDepth;
                Debug.LogWarning($"⚠️ Using fallback depth {depth}m at {screenPos} - may be inaccurate!");
            }
            else
            {
                Debug.Log($"⏭️ No valid depth ({depth:F2}m) at {screenPos}, skipping sphere (prevents random placement)");
                return;
            }
        }
        
        // Convert screen position + depth to world position
        // CRITICAL: Use viewport-to-world method for stable AR positioning
        Vector3 worldPos = Vector3.zero;
        
        if (arCamera != null)
        {
            // Convert screen pixels to viewport coordinates (0-1 range)
            float viewportX = screenPos.x / Screen.width;
            float viewportY = screenPos.y / Screen.height;
            
            // Cast ray from viewport position
            Ray ray = arCamera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0));
            
            // Place sphere at depth distance along ray
            worldPos = ray.GetPoint(depth);
            
            Debug.Log($"✅ Sphere placement: screen=({screenPos.x:F0},{screenPos.y:F0}) viewport=({viewportX:F2},{viewportY:F2}) depth={depth:F2}m world={worldPos}");
        }
        else
        {
            Debug.LogError("AR Camera is null!");
            return;
        }
        
        // Calculate sphere scale - use bbox dimensions directly
        float sphereDiameter = CalculateSphereScale(detection.rect.width, detection.rect.height, depth);
        
        // Create detection bounding box for overlap checking
        Rect screenBounds = new Rect(detection.rect.x, detection.rect.y, detection.rect.width, detection.rect.height);
        
        // CRITICAL: Check if this detection's screen position overlaps with existing sphere (prevents duplicates)
        if (IsDetectionOverlapping(screenBounds, detection.bestClassIndex.ToString()))
        {
            Debug.Log($"⏭️ Skipping duplicate sphere - bbox {screenBounds} overlaps with existing sphere of class {detection.bestClassIndex}");
            return;
        }
        
        // Create sphere primitive at detected object
        GameObject sphere = CreateSphere(worldPos, sphereDiameter, detection);
        
        if (sphere != null)
        {
            float radius = sphereDiameter / 2f;
            var sphereInstance = new SphereInstance(sphere, Time.time, sphereCounter++, worldPos, detection.bestClassIndex.ToString(), radius, screenBounds);
            sphereInstance.lastSeenFrame = Time.frameCount;
            activeSpheres.Add(sphereInstance);
            Debug.Log($"Created sphere at {worldPos}, diameter {sphereDiameter:F2}m, class {detection.bestClassIndex}");
        }
    }
    
    /// <summary>
    /// Check if a detection bounding box overlaps with existing spheres of the same class.
    /// More accurate than point-based checking for preventing duplicates.
    /// </summary>
    private bool IsDetectionOverlapping(Rect detectionBounds, string className)
    {
        foreach (var sphere in activeSpheres)
        {
            if (sphere.classLabel == className)
            {
                // Check IOU (intersection over union) of bounding boxes
                if (sphere.screenBounds.Overlaps(detectionBounds))
                {
                    float intersectArea = Mathf.Max(0, Mathf.Min(sphere.screenBounds.xMax, detectionBounds.xMax) - Mathf.Max(sphere.screenBounds.xMin, detectionBounds.xMin)) *
                                         Mathf.Max(0, Mathf.Min(sphere.screenBounds.yMax, detectionBounds.yMax) - Mathf.Max(sphere.screenBounds.yMin, detectionBounds.yMin));
                    float unionArea = sphere.screenBounds.width * sphere.screenBounds.height + detectionBounds.width * detectionBounds.height - intersectArea;
                    float iou = intersectArea / unionArea;
                    
                    if (iou > 0.3f) // 30% overlap threshold
                    {
                        Debug.Log($"Overlap detected: IOU={iou:F2} between new detection and existing sphere");
                        return true;
                    }
                }
            }
        }
        return false;
    }
    
    /// <summary>
    /// Get depth from TSDF volume by raycasting from camera through screen position.
    /// Uses reflection to access TSDFVolumeAtlas methods.
    /// </summary>
    private float GetDepthFromTSDF(Vector2 screenPos, int screenWidth, int screenHeight)
    {
        if (tsdfVolume == null || arCamera == null) return -1f;
        
        // Get methods via reflection
        var tsdfType = tsdfVolume.GetType();
        var sampleTSDFMethod = tsdfType.GetMethod("SampleTSDFAtWorldPosition");
        var sampleWeightMethod = tsdfType.GetMethod("SampleWeightAtWorldPosition");
        
        if (sampleTSDFMethod == null || sampleWeightMethod == null)
        {
            Debug.LogWarning("[SPHERE] TSDF sampling methods not found");
            return -1f;
        }
        
        // Convert screen to viewport
        float viewportX = screenPos.x / screenWidth;
        float viewportY = screenPos.y / screenHeight;
        
        // Cast ray from camera
        Ray ray = arCamera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0));
        
        // Sample TSDF along ray to find surface
        float maxDistance = 5f;
        float stepSize = 0.05f; // 5cm steps
        
        for (float t = 0.1f; t < maxDistance; t += stepSize)
        {
            Vector3 worldPos = ray.GetPoint(t);
            
            // Sample TSDF value at this position via reflection
            object tsdfValue = sampleTSDFMethod.Invoke(tsdfVolume, new object[] { worldPos });
            object weightValue = sampleWeightMethod.Invoke(tsdfVolume, new object[] { worldPos });
            
            if (tsdfValue is float tsdf && weightValue is float weight)
            {
                // Surface found when TSDF crosses zero (positive to negative)
                if (tsdf < 0.01f && tsdf > -0.05f)
                {
                    // Check weight to ensure this is a reliable observation
                    if (weight > 1.0f)
                    {
                        return t; // Distance along ray
                    }
                }
            }
        }
        
        return -1f; // No surface found
    }
    
    /// <summary>
    /// Get depth at a specific screen position using ARCore depth map.
    /// Software-based depth (S24 Ultra) needs tracking to be fully initialized first.
    /// </summary>
    private float GetDepthAtScreenPosition(Vector2 screenPos, int screenWidth, int screenHeight)
    {
        if (occlusionManager == null)
        {
            Debug.LogWarning("OcclusionManager is null - cannot get depth");
            return -1f;
        }
        
        // Check ARSession state - depth won't be available until tracking is established
        var arSession = UnityEngine.XR.ARFoundation.ARSession.state;
        if (arSession != UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking)
        {
            // Log every 30 frames (~0.5 seconds) to make sure we see this
            if (Time.frameCount % 30 == 0)
            {
                Debug.LogWarning($"[DEPTH CHECK] ARSession state: {arSession} (need SessionTracking for depth)");
            }
            return -1f;
        }
        
        if (!occlusionManager.TryAcquireEnvironmentDepthCpuImage(out var depthImage))
        {
            if (Time.frameCount % 60 == 0) // Reduce log spam - log once per second
            {
                Debug.LogWarning($"[DEPTH FAIL] TryAcquireEnvironmentDepthCpuImage returned FALSE");
                Debug.LogWarning($"[DEPTH FAIL] Current mode: {occlusionManager.currentEnvironmentDepthMode}");
                Debug.LogWarning($"[DEPTH FAIL] Requested mode: {occlusionManager.requestedEnvironmentDepthMode}");
                Debug.LogWarning($"[DEPTH FAIL] Occlusion enabled: {occlusionManager.enabled}");
                Debug.LogWarning($"[DEPTH FAIL] Subsystem running: {occlusionManager.subsystem?.running}");
                Debug.LogWarning($"[DEPTH FAIL] HINT: Move camera, point at textured surfaces, ensure good lighting");
            }
            return -1f;
        }
        
        // Log first successful depth acquisition
        if (!depthEverAcquired)
        {
            Debug.Log($"[DEPTH] ✓✓✓ DEPTH DATA NOW AVAILABLE! First depth image: {depthImage.width}x{depthImage.height}");
            depthEverAcquired = true;
        }
        
        // Convert screen position to depth image coordinates
        float normalizedX = screenPos.x / screenWidth;
        float normalizedY = screenPos.y / screenHeight;
        
        int depthX = Mathf.Clamp((int)(normalizedX * depthImage.width), 0, depthImage.width - 1);
        int depthY = Mathf.Clamp((int)(normalizedY * depthImage.height), 0, depthImage.height - 1);
        
        // Read depth data
        var conversionParams = new XRCpuImage.ConversionParams(depthImage, TextureFormat.RFloat);
        int dataLength = depthImage.width * depthImage.height * sizeof(float);
        var depthData = new Unity.Collections.NativeArray<byte>(dataLength, Unity.Collections.Allocator.Temp);
        depthImage.Convert(conversionParams, depthData);
        
        float depth = -1f;
        unsafe
        {
            fixed (byte* ptr = depthData.ToArray())
            {
                float* depthPtr = (float*)ptr;
                int idx = depthY * depthImage.width + depthX;
                depth = depthPtr[idx];
            }
        }
        
        depthData.Dispose();
        depthImage.Dispose();
        
        return depth;
    }
    
    /// <summary>
    /// Get depth by raycasting against AR planes and feature points.
    /// This provides real-world depth when ARCore depth is unavailable.
    /// </summary>
    private float GetDepthViaRaycast(Vector2 screenPos)
    {
        if (raycastManager == null || arCamera == null)
            return -1f;
        
        List<ARRaycastHit> hits = new List<ARRaycastHit>();
        
        // Raycast from screen position - will hit AR planes and feature points
        if (raycastManager.Raycast(screenPos, hits, TrackableType.AllTypes))
        {
            if (hits.Count > 0)
            {
                // Use the closest hit
                ARRaycastHit hit = hits[0];
                Vector3 hitPos = hit.pose.position;
                float distance = Vector3.Distance(arCamera.transform.position, hitPos);
                
                if (Time.frameCount % 60 == 0) // Log occasionally
                {
                    Debug.Log($"[RAYCAST] Hit at {hitPos}, distance: {distance:F2}m, trackable: {hit.trackableId}");
                }
                
                return distance;
            }
        }
        
        return -1f; // No hit
    }
    
    /// <summary>
    /// Convert screen position with depth to 3D world position.
    /// </summary>
    private Vector3 ScreenToWorldPoint(Vector2 screenPos, float depth)
    {
        if (arCamera == null) return Vector3.zero;
        
        // Create a point at the screen position with depth
        Vector3 screenPoint = new Vector3(screenPos.x, screenPos.y, depth);
        
        // Convert to world space
        Vector3 worldPos = arCamera.ScreenToWorldPoint(screenPoint);
        
        return worldPos;
    }
    
    /// <summary>
    /// Calculate sphere diameter per supervisor specification.
    /// Supervisor: radius = max(width, height) of bounding box (not half)
    /// Returns sphere DIAMETER (not radius). Unity sphere scale is diameter.
    /// </summary>
    private float CalculateSphereScale(float boxWidthPixels, float boxHeightPixels, float depth)
    {
        if (arCamera == null) return 0.5f;
        
        // FIXED: Proper angular size calculation
        // Per spec: radius = max(width, height) of bbox
        // BBox is in YOLO 640x640 space
        
        float maxDimensionYOLO = Mathf.Max(boxWidthPixels, boxHeightPixels);
        
        // What fraction of the 640px YOLO image does this bbox occupy?
        float bboxFraction = maxDimensionYOLO / 640f;
        
        // Camera's field of view (vertical)
        float cameraFOV_rad = arCamera.fieldOfView * Mathf.Deg2Rad;
        
        // Angular size that this bbox subtends
        float angularSize = bboxFraction * cameraFOV_rad;
        
        // At the given depth, calculate the ACTUAL world size
        // Formula: world_radius = depth * tan(angular_size / 2)
        float radiusMeters = depth * Mathf.Tan(angularSize / 2f);
        
        // Unity sphere scale is DIAMETER
        float diameterMeters = radiusMeters * 2f;
        
        // Clamp to reasonable physical object sizes
        diameterMeters = Mathf.Clamp(diameterMeters, 0.1f, 2.0f);
        
        Debug.Log($"🎯 Sphere: YOLO_bbox={maxDimensionYOLO:F0}px ({bboxFraction:P0}) -> angle={angularSize*Mathf.Rad2Deg:F1}° -> at depth={depth:F2}m -> diam={diameterMeters:F2}m");
        
        return diameterMeters;
    }
    
    /// <summary>
    /// Instantiate a sphere GameObject at the specified position and scale.
    /// </summary>
    private GameObject CreateSphere(Vector3 worldPos, float scale, ResultBox detection, Color? customColor = null)
    {
        GameObject sphere;
        
        // CRITICAL: Sphere must be created at world position with NO parent
        // This ensures spheres stay fixed in world space when camera moves
        
        if (spherePrefab != null)
        {
            // Use provided prefab - NO PARENT to keep sphere fixed in world space
            sphere = Instantiate(spherePrefab, worldPos, Quaternion.identity, null);
            // DO NOT parent to sphereContainer - spheres must stay at world coordinates
            
            sphere.layer = SPHERE_LAYER;
            
            // Semi-transparent cyan fill + cyan wireframe (like old ARCombinedOverlay)
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Use Sprites/Default shader - always included and supports alpha
                Material mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = new Color(0.0f, 0.71f, 0.78f, 0.4f); // IntelliCap cyan, 40% alpha
                mat.renderQueue = 3000; // Render before wireframe (which is at 4000)
                renderer.material = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Debug.Log($"[SPHERE PREFAB] Sprites/Default shader, Cyan R=0.0, G=0.71, B=0.78, A=0.4");
            }
            else
            {
                Debug.LogError("[SPHERE PREFAB] No renderer found on sphere!");
            }
            
            // Add wireframe with IntelliCap cyan color
            var wireframe = sphere.AddComponent<WireframeDrawer>();
            if (wireframe != null)
            {
                wireframe.SetColor(new Color(0.0f, 0.71f, 0.78f, 1.0f));  // IntelliCap cyan #00B5C7, full opacity
                Debug.Log($"[SPHERE] Wireframe added with IntelliCap cyan: R=0.0, G=0.71, B=0.78, A=1.0");
            }
        }
        else
        {
            // Create low-poly icosphere - IntelliCap approach
            try
            {
                sphere = IcosphereMesh.CreateIcosphere(subdivisions: 1, radius: 0.5f);
                if (sphere == null)
                {
                    Debug.LogWarning("⚠️ Icosphere creation returned null, falling back to primitive");
                    sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                }
                
                sphere.transform.position = worldPos;
                sphere.layer = SPHERE_LAYER;
                
                // Verify mesh is valid
                var meshFilter = sphere.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.mesh != null)
                {
                    var mesh = meshFilter.mesh;
                    if (mesh.vertexCount == 0 || mesh.triangles.Length == 0)
                    {
                        Debug.LogWarning($"⚠️ Invalid icosphere mesh (verts={mesh.vertexCount}, tris={mesh.triangles.Length}), using primitive");
                        DestroyImmediate(sphere);
                        sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        sphere.transform.position = worldPos;
                        sphere.layer = SPHERE_LAYER;
                    }
                    else
                    {
                        Debug.Log($"✅ Icosphere mesh valid: {mesh.vertexCount} vertices, {mesh.triangles.Length/3} triangles");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"❌ Icosphere creation failed: {ex.Message}, using primitive sphere");
                sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.position = worldPos;
                sphere.layer = SPHERE_LAYER;
            }
            
            // Disable sphere collider to prevent physics interactions
            var collider = sphere.GetComponent<SphereCollider>();
            if (collider != null)
            {
                Destroy(collider);
            }
            
            // Semi-transparent cyan fill + cyan wireframe (like old ARCombinedOverlay)
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Use Sprites/Default shader - always included and supports alpha
                Material mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = new Color(0.0f, 0.71f, 0.78f, 0.4f); // IntelliCap cyan, 40% alpha
                mat.renderQueue = 3000; // Render before wireframe (which is at 4000)
                renderer.material = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Debug.Log($"[SPHERE ICOSPHERE] Sprites/Default shader, Cyan R=0.0, G=0.71, B=0.78, A=0.4");
            }
            else
            {
                Debug.LogError("[SPHERE ICOSPHERE] No renderer found on sphere!");
            }
            
            // Add wireframe with IntelliCap cyan color
            var wireframe = sphere.AddComponent<WireframeDrawer>();
            if (wireframe != null)
            {
                wireframe.SetColor(new Color(0.0f, 0.71f, 0.78f, 1.0f));  // IntelliCap cyan #00B5C7, full opacity
                Debug.Log($"[SPHERE] Wireframe added with IntelliCap cyan: R=0.0, G=0.71, B=0.78, A=1.0");
            }
        }
        
        sphere.name = $"Sphere_{sphereCounter:D3}_{detection.bestClassIndex}";
        sphere.transform.localScale = Vector3.one * scale;
        
        return sphere;
    }
    
    /// <summary>
    /// Remove spheres that haven't been seen (matched to detections) for N frames.
    /// Ensures only currently detected objects have spheres.
    /// </summary>
    private void CleanupOldSpheres(int maxFramesNotSeen)
    {
        int currentFrame = Time.frameCount;
        int removedCount = 0;
        
        for (int i = activeSpheres.Count - 1; i >= 0; i--)
        {
            var sphere = activeSpheres[i];
            int framesSinceLastSeen = currentFrame - sphere.lastSeenFrame;
            
            if (framesSinceLastSeen > maxFramesNotSeen)
            {
                Debug.Log($"🗑️ Removing old sphere (class={sphere.classLabel}) not seen for {framesSinceLastSeen} frames");
                if (sphere.gameObject != null)
                {
                    DestroyImmediate(sphere.gameObject);
                }
                activeSpheres.RemoveAt(i);
                removedCount++;
            }
        }
        
        if (removedCount > 0)
        {
            Debug.Log($"🗑️ Cleanup: Removed {removedCount} old spheres, {activeSpheres.Count} remaining");
        }
    }
    
    /// <summary>
    /// Setup sphere rendering - using default layer for direct visibility.
    /// </summary>
    private void SetupSphereCamera()
    {
        // Spheres on Layer 0 are directly visible to AR camera - no setup needed
        Debug.Log($"Spheres using default layer (0) - directly visible to AR camera");
    }
    
    /// <summary>
    /// Composite sphere rendering onto AR camera view.
    /// Called automatically by Unity after camera renders.
    /// </summary>
    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        // Spheres on Layer 0 render directly - just pass through
        Graphics.Blit(src, dest);
    }
    
    /// <summary>
    /// Clear all active spheres.
    /// </summary>
    public void ClearAllSpheres()
    {
        foreach (var sphere in activeSpheres)
        {
            if (sphere.gameObject != null)
            {
                Destroy(sphere.gameObject);
            }
        }
        activeSpheres.Clear();
    }
    
    /// <summary>
    /// Merge overlapping spheres to reduce total count.
    /// Based on paper: check collision, handle containment, merge intersecting spheres.
    /// </summary>
    private void MergeOverlappingSpheres()
    {
        if (activeSpheres.Count < 2) return;
        
        const float MAX_MERGED_RADIUS = 1.5f; // Cap merged sphere radius at 1.5m
        bool anyMerged = false;
        
        for (int i = activeSpheres.Count - 1; i >= 0; i--)
        {
            if (activeSpheres[i].gameObject == null) continue;
            
            for (int j = i - 1; j >= 0; j--)
            {
                if (activeSpheres[j].gameObject == null) continue;
                
                var sphere1 = activeSpheres[i];
                var sphere2 = activeSpheres[j];
                
                // ONLY merge spheres of the SAME class (e.g., 2 bottles, 2 phones)
                // Different objects (TV, laptop, desk) should remain separate
                if (sphere1.classLabel != sphere2.classLabel)
                {
                    continue; // Skip - different object types don't merge
                }
                
                // Check if spheres intersect
                float distance = Vector3.Distance(sphere1.worldPosition, sphere2.worldPosition);
                float radiusSum = sphere1.radius + sphere2.radius;
                
                if (distance < radiusSum) // Spheres overlap
                {
                    // Case 1: Complete containment - discard smaller sphere
                    if (distance + sphere1.radius <= sphere2.radius)
                    {
                        // Sphere1 completely inside sphere2
                        Debug.Log($"🔵 Merging: Sphere1 (r={sphere1.radius:F2}m) contained in Sphere2 (r={sphere2.radius:F2}m), discarding smaller");
                        DestroyImmediate(sphere1.gameObject);
                        activeSpheres.RemoveAt(i);
                        anyMerged = true;
                        break;
                    }
                    else if (distance + sphere2.radius <= sphere1.radius)
                    {
                        // Sphere2 completely inside sphere1
                        Debug.Log($"🔵 Merging: Sphere2 (r={sphere2.radius:F2}m) contained in Sphere1 (r={sphere1.radius:F2}m), discarding smaller");
                        DestroyImmediate(sphere2.gameObject);
                        activeSpheres.RemoveAt(j);
                        anyMerged = true;
                        i--; // Adjust index since we removed from earlier position
                        continue;
                    }
                    
                    // Case 2: Partial overlap - merge into new sphere
                    // New center: midpoint between centers
                    Vector3 newCenter = (sphere1.worldPosition + sphere2.worldPosition) / 2f;
                    
                    // New radius: (r1 + r2 + distance) / 2
                    float newRadius = (sphere1.radius + sphere2.radius + distance) / 2f;
                    
                    // Cap at maximum radius
                    newRadius = Mathf.Min(newRadius, MAX_MERGED_RADIUS);
                    float newDiameter = newRadius * 2f;
                    
                    Debug.Log($"🔵 Merging: Sphere1 (r={sphere1.radius:F2}m) + Sphere2 (r={sphere2.radius:F2}m) at dist={distance:F2}m -> New (r={newRadius:F2}m) at {newCenter}");
                    
                    // Create merged sphere (use class label from first sphere)
                    ResultBox dummyDetection = new ResultBox(
                        new UnityEngine.Rect(0, 0, 0, 0),
                        1.0f,
                        int.Parse(sphere1.classLabel)
                    );
                    
                    // Use purple/magenta color for merged spheres (0.7, 0.3, 1.0 with 0.2 alpha)
                    Color mergedColor = new Color(0.7f, 0.3f, 1.0f, 0.2f);
                    GameObject mergedSphere = CreateSphere(newCenter, newDiameter, dummyDetection, mergedColor);
                    
                    if (mergedSphere != null)
                    {
                        // Create empty bounds for merged sphere (not used for overlap detection)
                        Rect mergedBounds = new Rect(0, 0, 0, 0);
                        
                        // Add merged sphere to list
                        activeSpheres.Add(new SphereInstance(
                            mergedSphere, 
                            Time.time, 
                            sphereCounter++, 
                            newCenter, 
                            sphere1.classLabel, 
                            newRadius,
                            mergedBounds
                        ));
                        
                        // Remove old spheres immediately
                        DestroyImmediate(sphere1.gameObject);
                        DestroyImmediate(sphere2.gameObject);
                        activeSpheres.RemoveAt(i);
                        activeSpheres.RemoveAt(j);
                        
                        anyMerged = true;
                        break;
                    }
                }
            }
        }
        
        if (anyMerged)
        {
            Debug.Log($"🔵 Merge complete: {activeSpheres.Count} spheres remaining");
        }
    }
    
    void OnDestroy()
    {
        ClearAllSpheres();
        
        // Cleanup sphere camera and render texture
        if (sphereRenderTexture != null)
        {
            sphereRenderTexture.Release();
            Destroy(sphereRenderTexture);
        }
        
        if (sphereCameraObj != null)
        {
            Destroy(sphereCameraObj);
        }
        
        if (sphereCompositeMaterial != null)
        {
            Destroy(sphereCompositeMaterial);
        }
    }
}
