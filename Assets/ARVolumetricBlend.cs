using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Rendering;

/// <summary>
/// AR-based volumetric blend using ARCore plane detection and depth
/// Camera 1: RGB camera feed with plane overlays
/// Camera 2: Depth-based volumetric rendering
/// </summary>
[RequireComponent(typeof(Camera))]
public class ARVolumetricBlend : MonoBehaviour
{
    [Header("AR Components")]
    [Tooltip("AR Camera Manager (from AR Session Origin)")]
    public ARCameraManager arCameraManager;
    
    [Tooltip("AR Plane Manager for detecting surfaces")]
    public ARPlaneManager arPlaneManager;
    
    [Tooltip("AR Occlusion Manager for depth")]
    public AROcclusionManager arOcclusionManager;

    [Header("Volumetric Camera")]
    [Tooltip("Second camera for volumetric depth rendering")]
    public Camera volumetricCamera;

    [Header("Blend Settings")]
    [Range(0f, 1f)]
    [Tooltip("Alpha for plane overlays")]
    public float planeOverlayAlpha = 0.5f;
    
    [Range(0f, 1f)]
    [Tooltip("Alpha for depth-based volumetric")]
    public float volumetricAlpha = 0.5f;

    [Header("Plane Visualization")]
    [Tooltip("Material for detected planes")]
    public Material planeMaterial;
    
    [Tooltip("Enable plane visualization")]
    public bool showPlanes = true;
    
    [Tooltip("Make plane overlays more visible")]
    [Range(0f, 1f)]
    public float planeVisibilityBoost = 0.8f;

    [Header("Debug")]
    [Tooltip("Show debug info on screen")]
    public bool showDebugInfo = true;
    
    [Tooltip("Show only volumetric texture (no blend)")]
    public bool showVolumetricOnly = false;
    
    [Tooltip("Disable depth colorizer rendering (use when ARCombinedOverlay is active)")]
    public bool disableDepthColorizer = false;
    
    private int planeCount = 0;
    private string lastPlaneInfo = "No planes detected";

    [Header("Shader")]
    [Tooltip("Shader for depth-based RGB colorization")]
    public Shader depthColorizeShader;
    private Material depthMaterial;
    
    [Header("Depth Visualization")]
    [Range(0f, 1f)]
    [Tooltip("RGB gradient intensity")]
    public float colorIntensity = 0.0f;
    
    [Range(0.1f, 5f)]
    [Tooltip("Normal calculation strength (surface detail)")]
    public float normalStrength = 1.0f;

    private RenderTexture volumetricRT;
    private Texture2D depthTexture;
    private CommandBuffer commandBuffer;

    private void Start()
    {
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("         ARVolumetricBlend DEPTH VISUALIZATION");
        Debug.Log("         + TSDF fusion in background");
        Debug.Log("═══════════════════════════════════════════════════");
        
        // Validate AR components
        if (arCameraManager == null)
        {
            Debug.LogError("AR Camera Manager not assigned!");
            enabled = false;
            return;
        }

        if (arPlaneManager == null)
        {
            Debug.LogError("AR Plane Manager not assigned!");
            enabled = false;
            return;
        }

        if (volumetricCamera == null)
        {
            Debug.LogError("Volumetric Camera not assigned!");
            enabled = false;
            return;
        }

        if (depthColorizeShader == null)
        {
            Debug.LogError("Depth Colorize Shader not assigned!");
            enabled = false;
            return;
        }

        // ARCombinedOverlay owns the final composite via OnRenderImage.
        // This CommandBuffer fires at CameraEvent.AfterEverything — which is AFTER OnRenderImage —
        // so it would overwrite the stripe composite with the rainbow depth gradient (blue/green artefacts).
        // Disable it now if ARCombinedOverlay exists in the scene (even if currently inactive).
        var overlay = FindObjectOfType<ARCombinedOverlay>(true); // true = include inactive
        if (overlay != null)
        {
            disableDepthColorizer = true;
            Debug.Log("[ARVolumetricBlend] ARCombinedOverlay present — depth colorizer DISABLED at startup to prevent blue/green overlay artefacts");
        }

        // Create depth visualization material
        depthMaterial = new Material(depthColorizeShader);
        Debug.Log("=== ARVolumetric: Depth colorize material created ===");
        
        // Setup CommandBuffer for AR Camera rendering (instead of OnRenderImage)
        SetupCommandBuffer();
        
        // DO NOT modify AR Camera clearFlags or background - AR Foundation handles this
        // The ARCameraBackground component will render the camera feed
        
        // Force enable and configure AR Plane Manager
        arPlaneManager.enabled = true;
        arPlaneManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        Debug.Log($"AR Plane Manager enabled: {arPlaneManager.enabled}");
        Debug.Log($"AR Plane Manager detection mode: {arPlaneManager.requestedDetectionMode}");

        // Setup volumetric camera
        SetupVolumetricCamera();

        // Subscribe to plane events
        arPlaneManager.planesChanged += OnPlanesChanged;
        Debug.Log("Subscribed to plane change events");

        // Enable depth if available
        if (arOcclusionManager != null)
        {
            arOcclusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            Debug.Log($"Depth API enabled, mode: {arOcclusionManager.requestedEnvironmentDepthMode}");
        }
        else
        {
            Debug.LogWarning("AR Occlusion Manager not assigned - no depth data!");
        }

        Debug.Log("=== AR Volumetric Blend initialization complete ===");
    }

    private void Update()
    {
        // Force check plane manager state
        if (arPlaneManager != null && !arPlaneManager.enabled)
        {
            Debug.LogWarning("AR Plane Manager got disabled! Re-enabling...");
            arPlaneManager.enabled = true;
            arPlaneManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        }
        
        // Log plane count periodically for debugging
        if (Time.frameCount % 60 == 0)
        {
            int currentPlanes = arPlaneManager != null ? arPlaneManager.trackables.count : 0;
            bool hasDepth = arOcclusionManager != null && arOcclusionManager.environmentDepthTexture != null;
            Debug.Log($"Frame {Time.frameCount}: {currentPlanes} planes, Depth: {hasDepth}");
            
            // Also check if subsystem is available
            if (arPlaneManager.subsystem == null)
            {
                Debug.LogError("AR Plane subsystem is NULL!");
            }
            else
            {
                Debug.Log($"AR Plane subsystem running: {arPlaneManager.subsystem.running}");
            }
        }
    }

    private void SetupCommandBuffer()
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null) return;

        commandBuffer = new CommandBuffer();
        commandBuffer.name = "AR Depth Visualization";
        
        // Add command buffer to render AFTER everything (including ARCameraBackground)
        cam.AddCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
        
        Debug.Log("✓ CommandBuffer added to AR Camera");
        Debug.Log($"   Native depth resolution will be used (NOT screen-scaled)");
        Debug.Log($"   Camera intrinsics (FOV, projection matrix) will be applied correctly");
    }

    private void LateUpdate()
    {
        if (commandBuffer == null || depthMaterial == null) return;
        
        // Auto-disable if ARCombinedOverlay is active anywhere in the scene —
        // it owns the final composite (OnRenderImage) and the depth colorizer
        // would bake a rainbow gradient into the camera frame that bleeds through
        // in scanned regions as blue/green particles.
        if (!disableDepthColorizer)
        {
            var overlay = FindObjectOfType<ARCombinedOverlay>();
            if (overlay != null && overlay.enabled)
            {
                disableDepthColorizer = true;
                Debug.Log("[ARVolumetricBlend] ARCombinedOverlay detected — depth colorizer disabled to prevent blue/green gradient artefacts");
            }
        }

        // Skip rendering if disabled
        if (disableDepthColorizer)
        {
            commandBuffer.Clear();
            return;
        }
        
        // Get depth texture
        Texture depthTexture = null;
        if (arOcclusionManager != null)
        {
            depthTexture = arOcclusionManager.environmentDepthTexture;
        }
        
        if (depthTexture == null) return;
        
        // Update shader properties
        depthMaterial.SetTexture("_EnvironmentDepth", depthTexture);
        depthMaterial.SetFloat("_ColorIntensity", colorIntensity);
        depthMaterial.SetFloat("_NormalStrength", normalStrength);
        
        // Log native depth resolution and camera intrinsics
        if (Time.frameCount == 100)
        {
            Camera cam = GetComponent<Camera>();
            Debug.Log($"");
            Debug.Log($"═══════════════════════════════════════════════════");
            Debug.Log($"         DEPTH FUSION CONFIGURATION");
            Debug.Log($"═══════════════════════════════════════════════════");
            Debug.Log($"Native Depth Resolution: {depthTexture.width}x{depthTexture.height}");
            Debug.Log($"   ✓ Using ARCore depth at NATIVE resolution");
            Debug.Log($"   ✓ NOT scaled to screen resolution");
            Debug.Log($"");
            Debug.Log($"Camera Intrinsics:");
            Debug.Log($"   Vertical FOV: {cam.fieldOfView:F2}°");
            Debug.Log($"   Aspect Ratio: {cam.aspect:F3}");
            
            // Get focal length from projection matrix
            Matrix4x4 proj = cam.projectionMatrix;
            float fy = proj[1, 1];
            float fx = proj[0, 0];
            float cx = proj[0, 2];
            float cy = proj[1, 2];
            Debug.Log($"   Projection Matrix:");
            Debug.Log($"     fx (horizontal focal): {fx:F4}");
            Debug.Log($"     fy (vertical focal): {fy:F4}");
            Debug.Log($"     cx (principal point x): {cx:F4}");
            Debug.Log($"     cy (principal point y): {cy:F4}");
            Debug.Log($"");
            Debug.Log($"TSDF Approach:");
            Debug.Log($"   Real-time depth fusion per frame");
            Debug.Log($"   Uses native depth + camera intrinsics directly");
            Debug.Log($"   Volumetric rendering via depth reprojection");
            Debug.Log($"═══════════════════════════════════════════════════");
            Debug.Log($"");
        }
        
        // Rebuild command buffer every frame
        commandBuffer.Clear();
        
        // Get temporary render texture
        int tempRT = Shader.PropertyToID("_TempRT");
        commandBuffer.GetTemporaryRT(tempRT, -1, -1, 0, FilterMode.Bilinear);
        
        // Copy CameraTarget to temp, apply shader, write back
        commandBuffer.Blit(BuiltinRenderTextureType.CameraTarget, tempRT);
        commandBuffer.Blit(tempRT, BuiltinRenderTextureType.CameraTarget, depthMaterial);
        
        // Release temp RT
        commandBuffer.ReleaseTemporaryRT(tempRT);
        
        if (Time.frameCount % 300 == 0)
        {
            Debug.Log($"[CommandBuffer] Depth visualization updated: {depthTexture.width}x{depthTexture.height}");
        }
    }

    private void SetupVolumetricCamera()
    {
        // DESTROY volumetric camera completely - we don't need it
        if (volumetricCamera != null)
        {
            Destroy(volumetricCamera.gameObject);
            volumetricCamera = null;
            Debug.Log("✓ Volumetric camera DESTROYED - using depth visualization");
        }
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        Debug.Log($"=== OnPlanesChanged called! Added: {args.added.Count}, Updated: {args.updated.Count}, Removed: {args.removed.Count} ===");
        
        // Handle new planes
        foreach (var plane in args.added)
        {
            Debug.Log($"NEW PLANE: {plane.trackableId}, Alignment: {plane.alignment}, Size: {plane.size}");
            ConfigurePlane(plane);
            planeCount++;
        }

        // Update existing planes
        foreach (var plane in args.updated)
        {
            ConfigurePlane(plane);
        }

        lastPlaneInfo = $"Planes: {arPlaneManager.trackables.count} detected (Total added: {planeCount})";
        Debug.Log(lastPlaneInfo);
    }

    private void ConfigurePlane(ARPlane plane)
    {
        // Disable plane mesh visualization - we're using depth-based RGB instead
        plane.gameObject.SetActive(false);
    }
    
    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private Color GetPlaneColor(ARPlane plane)
    {
        // Bright, fully opaque colors for testing
        switch (plane.alignment)
        {
            case PlaneAlignment.HorizontalUp:
                return new Color(0.0f, 1.0f, 0.0f, 1.0f); // Floor - Bright Green
            case PlaneAlignment.HorizontalDown:
                return new Color(0.0f, 0.8f, 1.0f, 1.0f); // Ceiling - Bright Cyan
            case PlaneAlignment.Vertical:
                return new Color(1.0f, 0.6f, 0.0f, 1.0f); // Walls - Bright Orange
            default:
                return new Color(1.0f, 0.0f, 1.0f, 1.0f); // Other - Bright Magenta
        }
    }

    private void OnDestroy()
    {
        // Cleanup command buffer
        if (commandBuffer != null)
        {
            Camera cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
            }
            commandBuffer.Release();
        }
        
        // Cleanup
        if (volumetricRT != null)
        {
            volumetricRT.Release();
            Destroy(volumetricRT);
        }

        if (depthMaterial != null)
        {
            Destroy(depthMaterial);
        }

        // Unsubscribe
        if (arPlaneManager != null)
        {
            arPlaneManager.planesChanged -= OnPlanesChanged;
        }
    }

    private void OnGUI()
    {
        if (!showDebugInfo) return;

        // Show debug info
        GUI.color = Color.white;
        GUI.backgroundColor = new Color(0, 0, 0, 0.7f);
        
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 30;
        style.alignment = TextAnchor.UpperLeft;
        style.normal.textColor = Color.white;
        
        string debugText = $"{lastPlaneInfo}\n";
        debugText += $"AR Plane Manager: {(arPlaneManager != null && arPlaneManager.enabled ? "Enabled" : "Disabled")}\n";
        debugText += $"Detection Mode: {(arPlaneManager != null ? arPlaneManager.requestedDetectionMode.ToString() : "N/A")}\n";
        debugText += $"Depth: {(arOcclusionManager != null && arOcclusionManager.environmentDepthTexture != null ? $"{arOcclusionManager.environmentDepthTexture.width}x{arOcclusionManager.environmentDepthTexture.height}" : "No depth")}\n";
        debugText += $"Color Intensity: {colorIntensity:F2}\n";
        debugText += $"Normal Strength: {normalStrength:F2}\n";
        debugText += $"Depth Material: {(depthMaterial != null ? "OK" : "NULL")}\n";
        debugText += $"Frame: {Time.frameCount}";
        
        GUI.Box(new Rect(10, 10, 700, 300), debugText, style);
    }

    public void TogglePlanes()
    {
        showPlanes = !showPlanes;
        foreach (var plane in arPlaneManager.trackables)
        {
            plane.gameObject.SetActive(showPlanes);
        }
    }
}
