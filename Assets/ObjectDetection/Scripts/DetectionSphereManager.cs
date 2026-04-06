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
    [Tooltip("Persistent spheres - no time-based deletion. Triangles disappear as areas get scanned.")]
    public bool usePersistentSpheres = true;
    public float sphereLifetime = 10.0f; // Only used if usePersistentSpheres = false
    
    [Header("Depth Confidence Filtering")]
    [Tooltip("Enable multi-sample averaging for more stable sphere placement")]
    public bool useDepthAveraging = true;
    [Tooltip("Number of samples to average (3x3 grid around detection center)")]
    public int depthSampleRadius = 1;
    
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
    private ARAnchorManager anchorManager;
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
    private const int SPHERE_LAYER = 8;  // Layer 8 for sphere-only rendering (prevents seeing through spheres)
    private const string SPHERE_LAYER_NAME = "Spheres";
    
    [Header("Sphere Tracking")]
    private List<SphereInstance> activeSpheres = new List<SphereInstance>();
    
    [Header("Sphere Position Updates")]
    [Tooltip("Enable smooth position updates when sphere is re-detected")]
    public bool enablePositionUpdates = true;
    [Tooltip("Smoothing factor for position updates (0=instant, 1=no movement)")]
    [Range(0f, 0.9f)]
    public float positionSmoothingFactor = 0.5f;
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
        public int matchCount; // Number of times this sphere has been re-detected; used for converging position
        public ARAnchor anchor; // null when placed via depth-only path (no anchorManager available or raycast missed)
        
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
            matchCount = 1; // Count the initial placement as the first observation
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

        // Find ARAnchorManager for map-relative anchoring (prevents VIO drift)
        anchorManager = FindObjectOfType<ARAnchorManager>();
        if (anchorManager == null)
            Debug.LogWarning("[ANCHOR] ARAnchorManager not found — anchoring disabled, spheres will use depth-only placement");
        else
            Debug.Log("[ANCHOR] ARAnchorManager found — anchors enabled");
        
        // CRITICAL: AR camera must NOT render sphere layer (dedicated sphereCamera handles that)
        if (arCamera != null)
        {
            arCamera.cullingMask &= ~(1 << SPHERE_LAYER); // Exclude sphere layer from AR camera
            Debug.Log($"✅ AR camera culling mask updated to exclude layer {SPHERE_LAYER}");
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
        
        // Sync sphere camera with AR camera transform
        if (sphereCamera != null && arCamera != null)
        {
            sphereCamera.transform.position = arCamera.transform.position;
            sphereCamera.transform.rotation = arCamera.transform.rotation;
            sphereCamera.fieldOfView = arCamera.fieldOfView;
        }
        
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

        // Sync world positions from ARAnchor — keeps worldPosition current as ARCore refines the map
        foreach (var s in activeSpheres)
        {
            if (s.anchor != null && s.anchor.gameObject != null)
                s.worldPosition = s.anchor.transform.position;
        }
        // MergeOverlappingSpheres() removed: spheres are pinned to initial placement positions.
        // Duplicate prevention at creation time (FindOverlappingSphere) makes merging unnecessary,
        // and the midpoint merge was causing spheres to visually jump between objects.
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
        
        // If no detections, keep spheres alive for 300 frames (~10s) before expiring.
        // 10-frame (0.33s) timeout was too aggressive — YOLO briefly misses objects as the
        // camera moves, which caused all spheres to disappear and be recreated at new positions.
        if (detections == null || detections.Count == 0)
        {
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log("[SPHERE] No detections this frame");
            }
            CleanupOldSpheres(300);
            return;
        }
        
        Debug.Log($"[SPHERE] Processing {detections.Count} detections for sphere creation");
        
        foreach (var detection in detections)
        {
            CreateSphereForDetection(detection, screenWidth, screenHeight, cropScaleRatio, cropOffsetX, cropOffsetY);
        }
        
        // Merge same-class spheres that overlap at creation time only (not per-frame).
        // This collapses two detections of e.g. "bottle+bottle" into one covering sphere.
        MergeOverlappingSpheres();

        // Cleanup: Remove spheres not matched for 300 frames (~10 seconds at 30fps).
        // 60-frame (2s) was too aggressive — user temporarily moving the camera off an object
        // caused the sphere to expire and a new one to appear at a shifted position.
        CleanupOldSpheres(300);
    }
    
    // DEPRECATED: Use IsScreenPositionCovered instead
    // This function is kept for backward compatibility but is no longer used
    private bool IsPositionCoveredBySphere(Vector2 screenPos, string classLabel, float radiusMargin = 1.8f)
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
            
            // Sphere's screen-space radius (FOV-corrected projection)
            // Formula: screenRadius = (worldRadius / depth) * (screenHeight / 2) / tan(FOV/2)
            float fovFactor = arCamera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            float screenRadius = (sphere.radius / screenPoint.z) * (Screen.height * 0.5f) / Mathf.Tan(fovFactor);
            
            // Check if detection is within sphere's screen projection (with LARGE margin to block mirror detections)
            if (screenDist < screenRadius * radiusMargin)
            {
                Debug.Log($"🚫 Position {screenPos} blocked by existing sphere at screen {screenPoint.x:F0},{screenPoint.y:F0} (dist={screenDist:F0}, radius={screenRadius:F0}, margin={radiusMargin})");
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
        Debug.Log($"\n🔵 === SPHERE CREATION START === Class:{detection.bestClassIndex} Conf:{detection.score:P0} | Active spheres: {activeSpheres.Count}");
        
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
        
        Debug.Log($"[COORDS] YOLO({centerX:F1},{centerY:F1}) -> Mapped({mappedX:F1},{mappedY:F1}) -> Screen({screenPos.x:F0},{screenPos.y:F0}) [screen size: {Screen.width}x{Screen.height}]");
        
        string className = detection.bestClassIndex.ToString();

        // ── Step 1: Get the best world position for this detection ──
        // Priority 1: ARFoundation depth-mesh raycast + ARAnchor.
        //   TrackableType.Depth hits the ARCore depth mesh (real object surfaces), NOT infinite planes.
        //   Anchor makes the sphere map-relative — survives VIO drift and ARCore map corrections.
        // Priority 2: ARCore depth texture (camera-relative fallback).
        //   ray.GetPoint(depth) is VIO-relative; sphere is pinned at creation but has no anchor.
        // Priority 3: TSDF volume depth.
        // Priority 4: ARFoundation plane raycast — LAST RESORT ONLY.
        //   TrackableType.AllTypes hits infinite ARCore planes (floor, wall, ceiling) behind the object.
        Vector3 newWorldPos = Vector3.zero;
        bool worldPosValid = false;
        float depth = -1f;
        string depthSource = "none";

        // Priority 1: ARFoundation depth-mesh raycast (TrackableType.Depth | FeaturePoint)
        //   This is the correct raycast type — hits the actual depth mesh, not background planes.
        if (raycastManager != null && anchorManager != null)
        {
            List<ARRaycastHit> anchorHits = new List<ARRaycastHit>();
            if (raycastManager.Raycast(screenPos, anchorHits,
                    TrackableType.Depth | TrackableType.FeaturePoint)
                && anchorHits.Count > 0 && anchorHits[0].distance < 8f)
            {
                newWorldPos = anchorHits[0].pose.position;
                depth = anchorHits[0].distance;
                worldPosValid = true;
                depthSource = "anchor";
                Debug.Log($"⚓ [ANCHOR] hit={newWorldPos} depth={depth:F2}m");
            }
        }

        // Priority 2: ARCore depth texture (camera-relative fallback)
        if (!worldPosValid)
        {
            depth = GetConfidenceFilteredDepth(screenPos, Screen.width, Screen.height);
            if (depth > 0f && depth < 8f && arCamera != null)
            {
                float vx = screenPos.x / Screen.width;
                float vy = screenPos.y / Screen.height;
                newWorldPos = arCamera.ViewportPointToRay(new Vector3(vx, vy, 0)).GetPoint(depth);
                worldPosValid = true;
                depthSource = "depth";
                Debug.Log($"✅ [DEPTH] depth={depth:F2}m world={newWorldPos}");
            }
        }

        // Priority 3: TSDF depth (fallback, disabled by default)
        if (!worldPosValid && useTSDFDepth && tsdfVolume != null)
        {
            depth = GetDepthFromTSDF(screenPos, Screen.width, Screen.height);
            if (depth > 0f && depth < 8f && arCamera != null)
            {
                float vx = screenPos.x / Screen.width;
                float vy = screenPos.y / Screen.height;
                newWorldPos = arCamera.ViewportPointToRay(new Vector3(vx, vy, 0)).GetPoint(depth);
                worldPosValid = true;
                depthSource = "tsdf";
                Debug.Log($"📊 [TSDF] depth={depth:F2}m world={newWorldPos}");
            }
        }

        // Priority 4 (plane raycast with TrackableType.AllTypes) — REMOVED.
        // AllTypes hits infinite ARCore floor/ceiling/wall planes, which caused spheres to appear
        // on floor ventilators and other horizontal surfaces when P1–P3 depth all failed.
        // If no reliable depth is available from P1–P3, no sphere is created. This is preferable
        // to anchoring a sphere to a floor plane that has nothing to do with the detected object.

        // ── Step 2: Check for existing sphere ──
        SphereInstance matchedSphere = FindSphereAtScreenPosition(screenPos, className, detection.rect.width, detection.rect.height);

        // Fallback world-space check — tight threshold to avoid false matches across objects
        if (matchedSphere == null && worldPosValid)
        {
            float sphereDiameterEstimate = CalculateSphereScale(detection.rect.width, detection.rect.height, depth > 0 ? depth : 1f);
            matchedSphere = FindOverlappingSphere(newWorldPos, sphereDiameterEstimate, className);
        }

        if (matchedSphere != null)
        {
            matchedSphere.lastSeenFrame = Time.frameCount;
            matchedSphere.matchCount++;
            // Position is NEVER updated after creation.
            // ray.GetPoint(depth) changes every frame as the camera moves (VIO drift + view angle),
            // so any lerp toward newWorldPos would drag the sphere off the object.
            // The sphere was placed correctly at creation using a validated depth reading — keep it there.
            Debug.Log($"[MATCH] Kept sphere #{matchedSphere.matchCount} at {matchedSphere.worldPosition} (position pinned)");
            return;
        }

        // ── Step 3: No match found — create new sphere if we have a valid world position ──
        if (!worldPosValid)
        {
            Debug.Log($"⏭️ No valid world position for {screenPos}, skipping sphere");
            return;
        }

        // Reject detections where depth < 0.4 m — these are objects at the user's feet or hands.
        // The ARCore structured-light depth sensor is unreliable below ~0.3 m, and anything this
        // close is very unlikely to be a meaningful scene object (floor vent, shoe, hand, etc.).
        if (depth < 0.4f)
        {
            Debug.Log($"⏭️ Rejected: depth {depth:F2}m < 0.4 m minimum — floor/hand artefact");
            return;
        }

        // Stability check: run for depth-texture source (verify two readings agree within 0.5m)
        if (depthSource == "depth")
        {
            float depthVerification = GetConfidenceFilteredDepth(screenPos, Screen.width, Screen.height);
            if (Mathf.Abs(depth - depthVerification) > 0.5f)
            {
                Debug.Log($"⏭️ VALIDATION FAILED: Unstable depth {depth:F2}m vs {depthVerification:F2}m, skipping sphere");
                return;
            }
        }

        float sphereDiameter = CalculateSphereScale(detection.rect.width, detection.rect.height, depth > 0 ? depth : 1f);
        Rect screenBounds = new Rect(screenPos.x - sphereDiameter * 0.5f, screenPos.y - sphereDiameter * 0.5f,
                                     sphereDiameter, sphereDiameter);

        // Create ARAnchor when placed via depth-mesh raycast — makes sphere map-relative, survives VIO drift
        ARAnchor anchor = null;
        if (depthSource == "anchor" && anchorManager != null)
        {
            anchor = anchorManager.AddAnchor(new Pose(newWorldPos, Quaternion.identity));
            if (anchor != null)
                Debug.Log($"⚓ [ANCHOR] Created anchor at {newWorldPos}");
            else
                Debug.LogWarning("[ANCHOR] AddAnchor returned null — sphere will be camera-relative");
        }

        GameObject sphere = CreateSphere(newWorldPos, sphereDiameter, detection);
        if (sphere != null)
        {
            // Parent sphere to anchor so it moves with ARCore map refinements
            if (anchor != null)
                sphere.transform.SetParent(anchor.transform, true);
            float radius = sphereDiameter / 2f;
            var sphereInstance = new SphereInstance(sphere, Time.time, sphereCounter++, newWorldPos, className, radius, screenBounds);
            sphereInstance.lastSeenFrame = Time.frameCount;
            sphereInstance.anchor = anchor;
            activeSpheres.Add(sphereInstance);
            Debug.Log($"Created sphere at {newWorldPos}, diameter {sphereDiameter:F2}m, class {detection.bestClassIndex}, anchored={anchor != null}");
        }
    }
    
    /// <summary>
    /// Check if screen position already covered by an existing sphere.
    /// Uses RAY-PROXIMITY: measures perpendicular distance from the sphere's world position to the
    /// ray cast through the detection centre. This is depth-independent — a sphere placed at the
    /// wrong depth (e.g. 0.5m when the object is 2m away) still lies on the same viewing ray, so
    /// perpendicular distance ≈ 0. Screen-space projection breaks as the camera moves, but the
    /// ray is always correct.
    /// </summary>
    private SphereInstance FindSphereAtScreenPosition(Vector2 screenPos, string className, float bboxWidth, float bboxHeight)
    {
        Ray ray = arCamera.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

        // Match threshold per sphere: radius * 6, minimum 0.25m.
        // Proportional to radius so a tiny object (r=0.1m → threshold 0.6m) doesn't accidentally
        // match a ray 1.5m away that is clearly aimed at a different object.
        // The old flat 2.0m cap was the root cause of false 1.465m matches observed in logs.
        SphereInstance bestMatch = null;
        float bestPerpDist = float.MaxValue;

        foreach (var sphere in activeSpheres)
        {
            if (sphere.gameObject == null) continue;
            if (sphere.classLabel != className) continue;

            float matchThresh = Mathf.Max(sphere.radius * 6f, 0.25f);

            // Project sphere onto ray — find closest point and perpendicular distance.
            Vector3 toSphere = sphere.worldPosition - ray.origin;
            float t = Vector3.Dot(toSphere, ray.direction);
            if (t < 0.05f) continue; // sphere is behind or at camera
            if (t > 8.0f) continue;  // ignore spheres more than 8m away

            Vector3 closestOnRay = ray.origin + ray.direction * t;
            float perpDist = Vector3.Distance(sphere.worldPosition, closestOnRay);

            if (perpDist < matchThresh && perpDist < bestPerpDist)
            {
                bestPerpDist = perpDist;
                bestMatch = sphere;
            }
        }

        if (bestMatch != null)
        {
            bestMatch.lastSeenFrame = Time.frameCount;
            float logThresh = Mathf.Max(bestMatch.radius * 6f, 0.25f);
            Debug.Log($"[MATCH] Ray-prox best={bestPerpDist:F3}m < thresh={logThresh:F2}m (r={bestMatch.radius:F3}m) — matched {className}");
            return bestMatch;
        }

        return null;
    }
    
    /// <summary>
    /// Find overlapping sphere for position update - returns matched sphere.
    /// Uses world space distance to prevent duplicates (more reliable than screen space as camera moves).
    /// </summary>
    private SphereInstance FindOverlappingSphere(Vector3 worldPos, float detectedDiameter, string className)
    {
        foreach (var sphere in activeSpheres)
        {
            if (sphere.gameObject == null) continue;
            
            // Only check spheres of same class
            if (sphere.classLabel == className)
            {
                // Calculate world space distance between detection and existing sphere
                float distance = Vector3.Distance(worldPos, sphere.worldPosition);
                
                // 0.8m threshold: close enough to catch the same object seen from a different angle
                // (ARCore depth can shift ~0.3-0.5m for the same object), but tight enough to not
                // swallow a genuinely distinct object of the same class that is nearby.
                const float WORLD_MATCH_RADIUS = 0.8f;

                if (distance < WORLD_MATCH_RADIUS)
                {
                    // CRITICAL: Update lastSeenFrame so sphere doesn't get deleted
                    sphere.lastSeenFrame = Time.frameCount;
                    Debug.Log($"[DUPLICATE PREVENTION] World distance {distance:F2}m < {WORLD_MATCH_RADIUS}m — matched existing sphere");
                    return sphere;
                }
            }
        }
        return null;
    }
    
    /// <summary>
    /// Update existing sphere position with smoothing to track object as camera moves.
    /// </summary>
    private void UpdateSpherePosition(SphereInstance sphere, Vector3 newWorldPos)
    {
        if (sphere == null || sphere.gameObject == null) return;
        
        Vector3 oldPos = sphere.worldPosition;
        float distance = Vector3.Distance(oldPos, newWorldPos);
        
        // Apply exponential smoothing to prevent jittery movement
        Vector3 smoothedPos = Vector3.Lerp(newWorldPos, oldPos, positionSmoothingFactor);
        
        sphere.worldPosition = smoothedPos;
        sphere.gameObject.transform.position = smoothedPos;
        
        Debug.Log($"[POSITION UPDATE] Sphere moved {distance:F3}m (from {oldPos} to {newWorldPos}, smoothed to {smoothedPos})");
    }
    
    /// <summary>
    /// Update existing sphere scale when larger detection found (e.g., half laptop → full laptop).
    /// </summary>
    private void UpdateSphereScale(SphereInstance sphere, float newDiameter)
    {
        if (sphere == null || sphere.gameObject == null) return;
        
        float oldRadius = sphere.radius;
        float newRadius = newDiameter * 0.5f;
        
        // Only update if new detection is significantly larger (10% threshold to avoid tiny jitter)
        if (newRadius > oldRadius * 1.1f)
        {
            sphere.radius = newRadius;
            sphere.gameObject.transform.localScale = new Vector3(newDiameter, newDiameter, newDiameter);
            Debug.Log($"[SCALE UPDATE] Sphere resized from {oldRadius * 2:F3}m to {newDiameter:F3}m diameter (object became more visible)");
        }
    }
    
    /// <summary>
    /// Get depth from TSDF volume by raycasting from camera through screen position.
    /// Uses reflection to access TSDFVolumeAtlas methods.
    /// </summary>
    private float GetDepthFromTSDF(Vector2 screenPos, int screenWidth, int screenHeight)
    {
        if (tsdfVolume == null || arCamera == null) return -1f;

        var tsdfType = tsdfVolume.GetType();
        var sampleTSDFMethod = tsdfType.GetMethod("SampleTSDFAtWorldPosition");
        var sampleWeightMethod = tsdfType.GetMethod("SampleWeightAtWorldPosition");
        if (sampleTSDFMethod == null || sampleWeightMethod == null) return -1f;

        float viewportX = screenPos.x / screenWidth;
        float viewportY = screenPos.y / screenHeight;
        Ray ray = arCamera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0));

        const float maxDist = 6f;
        const float stepSize = 0.04f;
        float prevTSDF = float.MaxValue;
        float prevT = 0f;

        for (float t = 0.2f; t < maxDist; t += stepSize)
        {
            Vector3 worldPos = ray.GetPoint(t);
            object tsdfObj   = sampleTSDFMethod.Invoke(tsdfVolume, new object[] { worldPos });
            object weightObj = sampleWeightMethod.Invoke(tsdfVolume, new object[] { worldPos });
            if (!(tsdfObj is float tsdf) || !(weightObj is float weight)) continue;
            if (weight < 2f) { prevTSDF = tsdf; prevT = t; continue; }

            if (prevTSDF > 0f && tsdf <= 0f && prevTSDF != float.MaxValue)
            {
                float crossT = prevT + stepSize * (prevTSDF / (prevTSDF - tsdf));
                return crossT;
            }
            prevTSDF = tsdf;
            prevT = t;
        }
        return -1f;
    }
    
    /// <summary>
    /// Get depth with multi-sample averaging for stability.
    /// Note: ARCore confidence texture is GPU-only and not available via CPU acquisition.
    /// We use spatial averaging of multiple depth samples instead.
    /// </summary>
    private float GetConfidenceFilteredDepth(Vector2 screenPos, int screenWidth, int screenHeight)
    {
        if (occlusionManager == null)
        {
            Debug.LogWarning("OcclusionManager is null - cannot get depth");
            return -1f;
        }
        
        // Check ARSession state
        var arSession = UnityEngine.XR.ARFoundation.ARSession.state;
        if (arSession != UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking)
        {
            if (Time.frameCount % 30 == 0)
            {
                Debug.LogWarning($"[DEPTH] ARSession state: {arSession} (need SessionTracking)");
            }
            return -1f;
        }
        
        // Try CPU image acquisition
        if (!occlusionManager.TryAcquireEnvironmentDepthCpuImage(out var depthImage))
        {
            if (Time.frameCount % 60 == 0)
            {
                Debug.LogWarning($"[DEPTH] CPU image unavailable, ensure camera is moving");
            }
            return -1f;
        }
        
        // Note: Confidence texture is GPU-only (environmentDepthConfidenceTexture)
        // CPU depth acquisition doesn't provide confidence data
        // We rely on multi-sample averaging for stability instead
        
        if (!depthEverAcquired)
        {
            Debug.Log($"[DEPTH] ✓ DEPTH ACTIVE: {depthImage.width}x{depthImage.height}");
            Debug.Log($"[DEPTH] Using multi-sample averaging for stability (confidence N/A on CPU)");
            depthEverAcquired = true;
        }
        
        // Convert screen to depth image coordinates
        // CRITICAL: ARCore depth image may have different resolution and orientation
        float normalizedX = screenPos.x / screenWidth;
        float normalizedY = screenPos.y / screenHeight;
        
        // Android ARCore often requires Y-flip for depth sampling
        #if UNITY_ANDROID
        normalizedY = 1.0f - normalizedY;
        #endif
        
        int centerX = Mathf.Clamp((int)(normalizedX * depthImage.width), 0, depthImage.width - 1);
        int centerY = Mathf.Clamp((int)(normalizedY * depthImage.height), 0, depthImage.height - 1);
        
        Debug.Log($"[DEPTH COORDS] Screen({screenPos.x:F0},{screenPos.y:F0}) -> Normalized({normalizedX:F2},{normalizedY:F2}) -> Depth({centerX},{centerY}) in {depthImage.width}x{depthImage.height}");
        
        // Read depth data
        var depthParams = new XRCpuImage.ConversionParams(depthImage, TextureFormat.RFloat);
        int depthDataLength = depthImage.width * depthImage.height * sizeof(float);
        var depthData = new Unity.Collections.NativeArray<byte>(depthDataLength, Unity.Collections.Allocator.Temp);
        depthImage.Convert(depthParams, depthData);
        
        float finalDepth = -1f;
        
        if (useDepthAveraging && depthSampleRadius > 0)
        {
            // Multi-sample averaging for stability
            List<float> validDepths = new List<float>();
            
            for (int dy = -depthSampleRadius; dy <= depthSampleRadius; dy++)
            {
                for (int dx = -depthSampleRadius; dx <= depthSampleRadius; dx++)
                {
                    int sampleX = Mathf.Clamp(centerX + dx, 0, depthImage.width - 1);
                    int sampleY = Mathf.Clamp(centerY + dy, 0, depthImage.height - 1);
                    
                    float depth = GetDepthAtPixel(depthData, sampleX, sampleY, depthImage.width);
                    
                    // Skip invalid depths
                    if (depth <= 0.01f || depth > 5.0f)
                        continue;
                    
                    validDepths.Add(depth);
                }
            }
            
            // Use MINIMUM depth (not median) — the closest valid sample is the foreground object surface.
            // Using the median on a centre-crop of the bounding box often averages in background
            // pixels (wall/floor behind the object), placing the sphere too far back. The minimum
            // picks the nearest real surface within the window, which is always the detected object.
            if (validDepths.Count > 0)
            {
                validDepths.Sort();
                finalDepth = validDepths[0]; // Minimum = closest surface = the foreground object
                
                Debug.Log($"[DEPTH] Min of {validDepths.Count} samples -> {finalDepth:F3}m (range: {validDepths[0]:F3}-{validDepths[validDepths.Count-1]:F3})");
                
                // Cleanup
                depthData.Dispose();
                depthImage.Dispose();
                return finalDepth;
            }
            else
            {
                Debug.LogWarning($"[DEPTH] No valid samples found at depth coords ({centerX},{centerY})");
            }
        }
        else
        {
            // Single-point sampling
            float depth = GetDepthAtPixel(depthData, centerX, centerY, depthImage.width);
            
            if (depth > 0.01f && depth <= 5.0f)
            {
                finalDepth = depth;
                Debug.Log($"[DEPTH] Single sample at depth coords ({centerX},{centerY}): {finalDepth:F3}m");
            }
            else
            {
                Debug.LogWarning($"[DEPTH] Invalid depth {depth:F3}m at coords ({centerX},{centerY})");
            }
        }
        
        // Cleanup
        depthData.Dispose();
        depthImage.Dispose();
        
        return finalDepth;
    }
    
    private unsafe float GetDepthAtPixel(Unity.Collections.NativeArray<byte> depthData, int x, int y, int width)
    {
        fixed (byte* ptr = depthData.ToArray())
        {
            float* depthPtr = (float*)ptr;
            int idx = y * width + x;
            return depthPtr[idx];
        }
    }
    
    /// <summary>
    /// Get depth at a specific screen position using ARCore depth map.
    /// DEPRECATED: Use GetConfidenceFilteredDepth instead.
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
        
        // Per spec: radius = max(width, height) of bbox. BBox is in YOLO 640x640 space.
        float maxDimensionYOLO = Mathf.Max(boxWidthPixels, boxHeightPixels);
        
        // Fraction of the 640px YOLO image the bbox occupies
        float bboxFraction = maxDimensionYOLO / 640f;
        
        // Use the camera's vertical FOV — this matches the YOLO preprocessing which
        // scales the image so that the vertical extent maps to the full vertical FOV.
        float cameraFOV_rad = arCamera.fieldOfView * Mathf.Deg2Rad;
        
        float angularSize    = bboxFraction * cameraFOV_rad;
        float radiusMeters   = depth * Mathf.Tan(angularSize / 2f);
        float diameterMeters = Mathf.Clamp(radiusMeters * 2f, 0.1f, 2.0f);
        
        Debug.Log($"Sphere: YOLO_bbox={maxDimensionYOLO:F0}px ({bboxFraction:P0}) angle={angularSize*Mathf.Rad2Deg:F1}deg depth={depth:F2}m diam={diameterMeters:F2}m");
        
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
            // Use provided prefab with Meta's barycentric wireframe approach
            sphere = Instantiate(spherePrefab, worldPos, Quaternion.identity, null);
            sphere.layer = SPHERE_LAYER;
            Debug.Log($"🔵 [SPHERE PREFAB] Created at {worldPos}, layer={sphere.layer}, active={sphere.activeSelf}");
            
            // IMPORTANT: Apply material FIRST before adding CoverageSphereFaceHider
            // The FaceHider component runs Awake() immediately and needs the material ready
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = null;
                
                // Try loading shader from Resources first
                Shader wireframeShader = Resources.Load<Shader>("Shaders/VertexColorWireframeTransparent");
                
                if (wireframeShader == null)
                {
                    wireframeShader = Shader.Find("Custom/VertexColorWireframeTransparent");
                }
                
                if (wireframeShader != null)
                {
                    mat = new Material(wireframeShader);
                    mat.SetColor("_WireColor", new Color(0.2f, 0.5f, 0.9f, 1.0f)); // Medium blue for wireframe
                    mat.SetFloat("_WireThickness", 1.2f);
                    mat.SetFloat("_WireAlpha", 0.8f);
                    Debug.Log($"[SPHERE PREFAB] Wireframe shader loaded (medium blue)");
                }
                else
                {
                    Debug.LogWarning("[SPHERE PREFAB] Wireframe shader not found, using Sprites/Default fallback");
                    
                    // Sprites/Default is ALWAYS included in Android builds
                    Shader fallback = Shader.Find("Sprites/Default");
                    if (fallback != null)
                    {
                        mat = new Material(fallback);
                        mat.color = new Color(0.6f, 0.85f, 1.0f, 0.65f); // Whitish-blue with 65% alpha
                    }
                    else
                    {
                        Debug.LogError("[SPHERE PREFAB] Even Sprites/Default not found! Creating default material");
                        mat = new Material(Shader.Find("Standard"));
                        mat.color = new Color(0.6f, 0.85f, 1.0f, 0.7f); // Whitish-blue with 70% alpha
                    }
                }
                
                if (mat != null)
                {
                    renderer.material = mat;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
            
            // NOW add components that process the mesh
            // Add CoverageSphereInfo to store radius
            var sphereInfo = sphere.GetComponent<CoverageSphereInfo>();
            if (sphereInfo == null)
            {
                sphereInfo = sphere.AddComponent<CoverageSphereInfo>();
            }
            sphereInfo.Radius = scale * 0.5f; // scale is diameter, radius is half
            
            // Add CoverageSphereFaceHider for look-to-hide functionality (runs Awake() when added)
            var faceHider = sphere.GetComponent<CoverageSphereFaceHider>();
            if (faceHider == null)
            {
                faceHider = sphere.AddComponent<CoverageSphereFaceHider>();
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
                Debug.Log($"🔵 [SPHERE ICOSPHERE] Created at {worldPos}, layer={sphere.layer}, active={sphere.activeSelf}");
                
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
            
            // IMPORTANT: Apply material FIRST before adding CoverageSphereFaceHider
            // The FaceHider component runs Awake() immediately and needs the material ready
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = null;
                
                // Try loading shader from Resources first
                Shader wireframeShader = Resources.Load<Shader>("Shaders/VertexColorWireframeTransparent");
                
                if (wireframeShader == null)
                {
                    wireframeShader = Shader.Find("Custom/VertexColorWireframeTransparent");
                }
                
                if (wireframeShader != null)
                {
                    mat = new Material(wireframeShader);
                    mat.SetColor("_WireColor", new Color(0.2f, 0.5f, 0.9f, 1.0f)); // Medium blue for wireframe
                    mat.SetFloat("_WireThickness", 1.2f);
                    mat.SetFloat("_WireAlpha", 0.8f);
                    Debug.Log($"[SPHERE ICOSPHERE] Wireframe shader loaded (medium blue)");
                }
                else
                {
                    Debug.LogWarning("[SPHERE ICOSPHERE] Wireframe shader not found, using Sprites/Default fallback");
                    
                    // Sprites/Default is ALWAYS included in Android builds
                    Shader fallback = Shader.Find("Sprites/Default");
                    if (fallback != null)
                    {
                        mat = new Material(fallback);
                        mat.color = new Color(0.6f, 0.85f, 1.0f, 0.65f); // Whitish-blue with 65% alpha
                    }
                    else
                    {
                        Debug.LogError("[SPHERE ICOSPHERE] Even Sprites/Default not found! Creating default material");
                        mat = new Material(Shader.Find("Standard"));
                        mat.color = new Color(0.6f, 0.85f, 1.0f, 0.7f); // Whitish-blue with 70% alpha
                    }
                }
                
                if (mat != null)
                {
                    renderer.material = mat;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
            else
            {
                Debug.LogError("[SPHERE ICOSPHERE] No renderer found on sphere!");
            }
            
            // NOW add components that process the mesh
            // Add CoverageSphereInfo to store radius
            var sphereInfo = sphere.AddComponent<CoverageSphereInfo>();
            sphereInfo.Radius = scale * 0.5f; // scale is diameter, radius is half
            
            // Add CoverageSphereFaceHider for look-to-hide functionality (runs Awake() when added)
            var faceHider = sphere.AddComponent<CoverageSphereFaceHider>();
        }
        
        sphere.name = $"Sphere_{sphereCounter:D3}_{detection.bestClassIndex}";
        sphere.transform.localScale = Vector3.one * scale;
        Debug.Log($"✅ SPHERE CREATED: {sphere.name} at world {worldPos}, scale {scale:F2}, layer {sphere.layer}");
        
        
        return sphere;
    }
    
    /// <summary>
    /// Remove spheres that haven't been seen (matched to detections) for N frames.
    /// Ensures only currently detected objects have spheres.
    /// </summary>
    private void CleanupOldSpheres(int maxFramesNotSeen)
    {
        // Skip cleanup if using persistent spheres (they stay until fully scanned)
        if (usePersistentSpheres)
        {
            return;
        }
        
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
                if (sphere.anchor != null)
                {
                    Destroy(sphere.anchor.gameObject);
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
    /// Setup sphere rendering camera and compositor shader.
    /// Renders spheres to separate layer, then composites with GREEN→blue conversion.
    /// </summary>
    private void SetupSphereCamera()
    {
        // Create sphere camera that renders ONLY sphere layer
        sphereCameraObj = new GameObject("SphereCamera");
        sphereCamera = sphereCameraObj.AddComponent<Camera>();
        
        // Match AR camera settings
        sphereCamera.CopyFrom(arCamera);
        sphereCamera.cullingMask = 1 << SPHERE_LAYER; // Only render sphere layer
        sphereCamera.clearFlags = CameraClearFlags.SolidColor;
        sphereCamera.backgroundColor = new Color(0, 0, 0, 0); // Transparent background
        sphereCamera.depth = arCamera.depth - 1; // Render before AR camera
        
        // Create RenderTexture for sphere-only rendering
        sphereRenderTexture = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
        sphereRenderTexture.name = "SphereRenderTexture";
        sphereCamera.targetTexture = sphereRenderTexture;
        
        // Load compositor shader that converts GREEN→blue        // Try multiple paths
        Shader compositorShader = Shader.Find("Custom/SphereRendering");
        if (compositorShader == null)
        {
            compositorShader = Resources.Load<Shader>("Shaders/SphereRendering");
        }
        
        if (compositorShader != null)
        {
            sphereCompositeMaterial = new Material(compositorShader);
            Debug.Log("✅ Sphere compositor shader loaded (will convert GREEN→blue)");
        }
        else
        {
            Debug.LogError("❌ SphereRendering compositor shader not found! Tried: Shader.Find('Custom/SphereRendering') and Resources.Load<Shader>('Shaders/SphereRendering')");
        }
        
        // Sync camera position/rotation with AR camera in Update
        Debug.Log($"✅ Sphere camera setup complete - Layer {SPHERE_LAYER}, RenderTexture {sphereRenderTexture.width}x{sphereRenderTexture.height}");
    }
    
    /// <summary>
    /// Composite sphere rendering onto AR camera view.
    /// Called BY ARCombinedOverlay during its OnRenderImage.
    /// Spheres are already BLUE - just alpha blend over background.
    /// </summary>
    public void CompositeSpheres(RenderTexture src, RenderTexture dest)
    {
        if (sphereRenderTexture == null)
        {
            // No sphere rendering yet, just pass through
            Graphics.Blit(src, dest);
            return;
        }
        
        // Use compositor material for proper alpha blending
        if (sphereCompositeMaterial != null)
        {
            sphereCompositeMaterial.SetTexture("_SphereTex", sphereRenderTexture);
            Graphics.Blit(src, dest, sphereCompositeMaterial);
        }
        else
        {
            // Fallback: just pass through
            Graphics.Blit(src, dest);
        }
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
                
                // Check if spheres intersect or are very close
                float distance = Vector3.Distance(sphere1.worldPosition, sphere2.worldPosition);
                float radiusSum = sphere1.radius + sphere2.radius;
                
                // AGGRESSIVE: Merge if within 1.5x radius sum (not just touching)
                if (distance < radiusSum * 1.5f) // More aggressive merging
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
                    
                    // Case 2: Partial overlap — IntelliCap merge formula (paper §3.3):
                    //   cnew = (c1 + c2) / 2
                    //   rnew = (r1 + r2 + ||c1−c2||) / 2,  capped at MAX_MERGED_RADIUS
                    // This runs once at creation time only (not per-frame), so the
                    // one-time position move is intentional multi-view consolidation.
                    Vector3 newCenter = (sphere1.worldPosition + sphere2.worldPosition) / 2f;
                    float newRadius = Mathf.Min((sphere1.radius + sphere2.radius + distance) / 2f, MAX_MERGED_RADIUS);
                    float newDiameter = newRadius * 2f;

                    Debug.Log($"🔵 Merging: ({sphere1.worldPosition}, r={sphere1.radius:F2}) + ({sphere2.worldPosition}, r={sphere2.radius:F2}) → cnew={newCenter}, rnew={newRadius:F2}m");

                    ResultBox dummyDetection = new ResultBox(
                        new UnityEngine.Rect(0, 0, 0, 0), 1.0f, int.Parse(sphere1.classLabel));
                    Color mergedColor = new Color(0.7f, 0.3f, 1.0f, 0.2f);
                    GameObject mergedSphere = CreateSphere(newCenter, newDiameter, dummyDetection, mergedColor);

                    if (mergedSphere != null)
                    {
                        activeSpheres.Add(new SphereInstance(
                            mergedSphere, Time.time, sphereCounter++,
                            newCenter, sphere1.classLabel, newRadius,
                            new Rect(0, 0, 0, 0)));

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
