using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Rendering;

/// <summary>
/// TSDF Volume Fusion using 2D texture atlas (OpenGLES3 compatible)
/// Stores 3D volume as 2D atlas to enable GPU writes without compute shaders
/// Uses native depth resolution with camera intrinsics
/// </summary>
[RequireComponent(typeof(Camera))]
public class TSDFVolumeAtlas : MonoBehaviour
{
    [Header("AR Components")]
    public AROcclusionManager occlusionManager;
    public ARCameraManager cameraManager;
    public ARCameraBackground cameraBackground;
    
    [Header("Volume Settings")]
    public Vector3Int volumeResolution = new Vector3Int(128, 128, 128);  // Increased for testing
    public float voxelSize = 0.05f;
    public float truncationDistance = 0.2f;
    public float maxDepth = 5.0f;
    
    [Header("Depth Confidence Filtering")]
    [Tooltip("Minimum confidence (0-1) to accept depth pixels. Lower = more samples, higher = more reliable")]
    public float minDepthConfidence = 0.2f;  // 20% confidence - accept more samples while filtering very unreliable pixels
    [Tooltip("Enable to filter unreliable depth pixels before TSDF integration")]
    public bool useConfidenceFiltering = true;
    
    [Header("Shaders")]
    public Shader volumeIntegrationShader;
    public Shader rayMarchShader;
    
    [Header("TEST: Use DepthColorize shader instead")]
    public Shader depthColorizeShader;  // Assign DepthColorize shader in Inspector
    
    [Header("Rendering")]
    public float volumeOpacity = 0.5f;
    public float stepSize = 0.03f;
    public float surfaceThreshold = 0.03f;
    
    [Header("Integration")]
    [Tooltip("Integrate every N frames (30 = ~1 second at 30 FPS, matches IntelliCap demo)")]
    public int integrationInterval = 30;
    
    // Volume stored as 2D atlas (ping-pong)
    private RenderTexture tsdfAtlas;
    private RenderTexture tsdfAtlasTemp;
    private RenderTexture weightAtlas;
    private RenderTexture weightAtlasTemp;
    
    // Public API for ray marching
    public RenderTexture GetTSDFAtlas() { return tsdfAtlas; }
    public RenderTexture GetWeightAtlas() { return weightAtlas; }
    // volumeCenter is used by TSDFWeightCoverageMask.shader which centers its lookup at this point.
    // The shader computes: voxelPos = (worldPos - volumeCenter) / voxelSize + res/2
    // So volumeCenter must be the GEOMETRIC center of the volume, not the corner.
    public Vector3 volumeCenter
    {
        get
        {
            return volumeOrigin + new Vector3(
                volumeResolution.x * voxelSize * 0.5f,
                volumeResolution.y * voxelSize * 0.5f,
                volumeResolution.z * voxelSize * 0.5f);
        }
    }
    public Vector3Int GetVolumeResolution() { return volumeResolution; }
    public float GetVoxelSize() { return voxelSize; }
    public int GetSlicesPerRow() { return Mathf.CeilToInt(Mathf.Sqrt(volumeResolution.z)); }
    
    /// <summary>
    /// Sample TSDF value at a world position. Returns distance to nearest surface (negative = inside object).
    /// </summary>
    public float SampleTSDFAtWorldPosition(Vector3 worldPos)
    {
        // Transform world position to volume local space (volumeOrigin is the corner, not center)
        Vector3 localPos = worldPos - volumeOrigin;
        
        // Convert to voxel coordinates (corner-based: voxel 0 = origin corner)
        Vector3 voxelPos = localPos / voxelSize;
        
        // Check bounds
        if (voxelPos.x < 0 || voxelPos.x >= volumeResolution.x ||
            voxelPos.y < 0 || voxelPos.y >= volumeResolution.y ||
            voxelPos.z < 0 || voxelPos.z >= volumeResolution.z)
        {
            return 1.0f; // Outside volume = far from surface
        }
        
        // Sample via CPU readback (slow but works for occasional queries)
        // For real-time, would use compute shader
        return SampleTSDFViaCPU(voxelPos);
    }
    
    /// <summary>
    /// Sample weight at a world position. Higher weight = more reliable data.
    /// </summary>
    public float SampleWeightAtWorldPosition(Vector3 worldPos)
    {
        // Transform world position to volume local space (volumeOrigin is the corner, not center)
        Vector3 localPos = worldPos - volumeOrigin;
        
        // Convert to voxel coordinates (corner-based: voxel 0 = origin corner)
        Vector3 voxelPos = localPos / voxelSize;
        
        // Check bounds
        if (voxelPos.x < 0 || voxelPos.x >= volumeResolution.x ||
            voxelPos.y < 0 || voxelPos.y >= volumeResolution.y ||
            voxelPos.z < 0 || voxelPos.z >= volumeResolution.z)
        {
            return 0f; // Outside volume = no data
        }
        
        // Sample via CPU readback
        return SampleWeightViaCPU(voxelPos);
    }
    
    private float SampleTSDFViaCPU(Vector3 voxelPos)
    {
        // TODO: Implement CPU readback - requires RenderTexture.active approach
        // For now return placeholder (requires GPU->CPU copy which is slow)
        // In production, use compute shader for GPU-side sampling
        return 1.0f; // Placeholder - assume far from surface
    }
    
    private float SampleWeightViaCPU(Vector3 voxelPos)
    {
        // TODO: Implement CPU readback
        // For now return low weight (unreliable)
        return 0f;
    }
    
    private Material integrationMaterial;
    private Material rayMarchMaterial;
    private CommandBuffer commandBuffer;
    
    private int frameCount = 0;
    private Vector3 volumeOrigin;
    private int atlasWidth, atlasHeight;
    
    // Performance monitoring
    private float lastMemoryCheckTime = 0f;
    private float lastFPSCheckTime = 0f;
    private int fpsFrameCount = 0;
    
    void Start()
    {
        // IMPORTANT: Force runtime values to override whatever Inspector has saved
        // Inspector values from old serialized builds will always be wrong
        minDepthConfidence = 0.1f;   // Very low - accept almost all depth pixels
        useConfidenceFiltering = false;  // DISABLE filtering entirely - accept ALL depth pixels for max coverage
        maxDepth = 8.0f;             // Accept depth up to 8 meters
        truncationDistance = 0.15f;  // 15cm truncation band - slightly wider for better coverage

        // Temporary origin placeholder - overridden below in Start() with camera-relative centered origin.
        // Covers ±3.2m around camera startup position in all axes (X, Y, Z).
        volumeOrigin = new Vector3(-3.2f, -3.2f, -3.2f); // placeholder; overridden below
        
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("         TSDF VOLUME FUSION (BACKGROUND)");
        Debug.Log("         Integration active, no rendering");
        Debug.Log($"         Confidence filtering: DISABLED (accepting all depth)");
        Debug.Log($"         Volume origin (placeholder): {volumeOrigin} - real origin set after Start()");
        Debug.Log("═══════════════════════════════════════════════════");
        
        // AUTO-FIND all components (no Inspector setup needed!)
        if (occlusionManager == null)
        {
            occlusionManager = FindObjectOfType<AROcclusionManager>();
            Debug.Log($"[TSDF] Auto-found AROcclusionManager: {occlusionManager != null}");
        }
        
        if (cameraManager == null)
        {
            cameraManager = FindObjectOfType<ARCameraManager>();
            Debug.Log($"[TSDF] Auto-found ARCameraManager: {cameraManager != null}");
        }
        
        if (cameraBackground == null)
        {
            cameraBackground = FindObjectOfType<ARCameraBackground>();
            Debug.Log($"[TSDF] Auto-found ARCameraBackground: {cameraBackground != null}");
        }
        
        // AUTO-LOAD shaders by name
        if (volumeIntegrationShader == null)
        {
            volumeIntegrationShader = Shader.Find("Custom/TSDFVolumeIntegration2D");
            Debug.Log($"[TSDF] Auto-loaded integration shader: {volumeIntegrationShader != null}");
        }
        
        if (rayMarchShader == null)
        {
            rayMarchShader = Shader.Find("Custom/TSDFRayMarchAtlas");
            Debug.Log($"[TSDF] Auto-loaded ray march shader: {rayMarchShader != null}");
        }
        
        if (depthColorizeShader == null)
        {
            depthColorizeShader = Shader.Find("Custom/DepthColorize");
            Debug.Log($"[TSDF] Auto-loaded DepthColorize shader: {depthColorizeShader != null}");
        }
        
        Debug.Log($"[TSDF] Validating components...");
        if (!ValidateComponents()) return;
        
        // Calculate atlas dimensions (pack Z slices into 2D grid)
        int slicesPerRow = Mathf.CeilToInt(Mathf.Sqrt(volumeResolution.z));
        atlasWidth = volumeResolution.x * slicesPerRow;
        atlasHeight = volumeResolution.y * Mathf.CeilToInt((float)volumeResolution.z / slicesPerRow);
        
        Debug.Log($"   Volume: {volumeResolution}, Voxel: {voxelSize}m");
        Debug.Log($"   Atlas: {atlasWidth}x{atlasHeight} (packing {volumeResolution.z} slices)");
        
        // Calculate memory requirements
        float atlasMemoryMB = (atlasWidth * atlasHeight * 4f) / (1024f * 1024f);  // 4 bytes per pixel for RFloat
        float totalMemoryMB = atlasMemoryMB * 4f;  // 4 atlases (TSDF + Weight, ping-pong)
        Debug.Log($"   Estimated Memory: {totalMemoryMB:F2} MB ({atlasMemoryMB:F2} MB per atlas)");
        Debug.Log($"   Total Voxels: {volumeResolution.x * volumeResolution.y * volumeResolution.z:N0}");
        
        // Check available memory
        long totalRAM = SystemInfo.systemMemorySize;
        Debug.Log($"   System RAM: {totalRAM} MB");
        
        // Create atlas textures
        tsdfAtlas = CreateAtlas("TSDF Atlas", new Color(1, 0, 0, 1));
        tsdfAtlasTemp = CreateAtlas("TSDF Atlas Temp", new Color(1, 0, 0, 1));
        weightAtlas = CreateAtlas("Weight Atlas", Color.black);
        weightAtlasTemp = CreateAtlas("Weight Atlas Temp", Color.black);
        
        // Create materials
        integrationMaterial = new Material(volumeIntegrationShader);
        
        // TEST: Use DepthColorize shader instead of ray march shader
        if (depthColorizeShader != null)
        {
            rayMarchMaterial = new Material(depthColorizeShader);
            Debug.Log($"[TSDF] Using DepthColorize shader for testing!");
        }
        else
        {
            rayMarchMaterial = new Material(rayMarchShader);
        }
        
        Debug.Log($"[TSDF] ✓ Integration material created: {integrationMaterial != null}");
        Debug.Log($"[TSDF] ✓ Ray march material created: {rayMarchMaterial != null}");
        Debug.Log($"[TSDF] Integration shader: {volumeIntegrationShader.name}");
        Debug.Log($"[TSDF] Ray march shader: {rayMarchMaterial.shader.name}");
        
        // No CommandBuffer - ARVolumetricBlend handles rendering
        Debug.Log("[TSDF] Integration-only mode (no visualization)");
        Debug.Log("[TSDF] ARVolumetricBlend provides depth visualization");
        
        // Volume origin: center the volume on the camera startup position in ALL dimensions.
        // With + forward*0.1 the volume only covered z=0..6.4m (forward hemisphere).
        // Any wall behind the camera at startup was outside the volume and could never be scanned.
        // Centering with - forward*halfXY covers z = -(3.2m) .. +(3.2m) from startup in all directions.
        float halfXY = volumeResolution.x * voxelSize * 0.5f;   // 3.2m
        volumeOrigin = transform.position
            - transform.right   * halfXY   // center X around camera
            - transform.up      * halfXY   // center Y around camera
            - transform.forward * halfXY;  // center Z around camera (covers full room, not just forward)
        Debug.Log($"[TSDF] Volume CENTERED on camera: origin={volumeOrigin}, covers ±{halfXY:F1}m in all directions (full room 360°)");
        
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("     TSDF VOLUME ATLAS INITIALIZED SUCCESSFULLY");
        Debug.Log("═══════════════════════════════════════════════════");
    }
    
    bool ValidateComponents()
    {
        if (occlusionManager == null || cameraManager == null)
        {
            Debug.LogError("[TSDF] Missing AR components (OcclusionManager or CameraManager)!");
            enabled = false;
            return false;
        }
        
        if (cameraBackground == null)
        {
            Debug.LogWarning("[TSDF] ARCameraBackground not found - will use black background");
        }
        
        if (volumeIntegrationShader == null || rayMarchShader == null)
        {
            Debug.LogError("[TSDF] Missing shaders!");
            enabled = false;
            return false;
        }
        
        return true;
    }
    
    RenderTexture CreateAtlas(string name, Color clearColor)
    {
        RenderTexture atlas = new RenderTexture(atlasWidth, atlasHeight, 0, RenderTextureFormat.RFloat);
        atlas.filterMode = FilterMode.Point;
        atlas.wrapMode = TextureWrapMode.Clamp;
        atlas.name = name;
        atlas.Create();
        
        // Clear to initial value
        RenderTexture.active = atlas;
        GL.Clear(false, true, clearColor);
        RenderTexture.active = null;
        
        return atlas;
    }
    
    void LateUpdate()
    {
        frameCount++;
        
        if (frameCount == 1)
        {
            Debug.Log("[TSDF] ▶ Integration active (silent background mode)");
            lastMemoryCheckTime = Time.realtimeSinceStartup;
            lastFPSCheckTime = Time.realtimeSinceStartup;
        }
        
        if (frameCount % integrationInterval == 0)
        {
            IntegrateDepth();
        }
        
        // FPS monitoring every second
        fpsFrameCount++;
        float currentTime = Time.realtimeSinceStartup;
        if (currentTime - lastFPSCheckTime >= 1.0f)
        {
            float fps = fpsFrameCount / (currentTime - lastFPSCheckTime);
            Debug.Log($"[TSDF] FPS: {fps:F1}, Frame: {frameCount}");
            fpsFrameCount = 0;
            lastFPSCheckTime = currentTime;
        }
        
        // Memory monitoring every 5 seconds
        if (currentTime - lastMemoryCheckTime >= 5.0f)
        {
            LogMemoryUsage();
            lastMemoryCheckTime = currentTime;
        }
        
        if (frameCount == 30 || frameCount == 60)
        {
            Debug.Log($"[TSDF] Update() frame {frameCount}");
        }
    }
    
    void IntegrateDepth()
    {
        Texture depthTex = occlusionManager?.environmentDepthTexture;
        if (depthTex == null) return;
        
        // Get confidence texture for filtering unreliable pixels
        Texture confidenceTex = null;
        if (useConfidenceFiltering)
        {
            confidenceTex = occlusionManager?.environmentDepthConfidenceTexture;
            if (confidenceTex == null)
            {
                if (frameCount == 30)
                {
                    Debug.LogWarning("[TSDF] Confidence texture not available, using all depth pixels");
                }
                useConfidenceFiltering = false;
            }
        }
        
        Camera cam = GetComponent<Camera>();
        
        // Log configuration once
        if (frameCount == 30)
        {
            Debug.Log($"═══════════════════════════════════════");
            Debug.Log($"   TSDF VOLUME FUSION ACTIVE");
            Debug.Log($"═══════════════════════════════════════");
            Debug.Log($"Native Depth: {depthTex.width}x{depthTex.height}");
            Debug.Log($"Camera FOV: {cam.fieldOfView:F1}°");
            Debug.Log($"Volume: {volumeResolution} @ {voxelSize}m/voxel");
            Debug.Log($"Truncation: {truncationDistance}m");
            if (useConfidenceFiltering && confidenceTex != null)
            {
                Debug.Log($"✓ Confidence Filtering: ENABLED (threshold: {minDepthConfidence:F2})");
                Debug.Log($"  Only reliable depth pixels integrated");
            }
            else
            {
                Debug.Log($"⚠ Confidence Filtering: DISABLED (using all raw depth)");
            }
            Debug.Log($"═══════════════════════════════════════");
        }
        
        // Setup integration shader
        integrationMaterial.SetTexture("_DepthTex", depthTex);
        integrationMaterial.SetTexture("_PrevTSDF", tsdfAtlas);
        integrationMaterial.SetTexture("_PrevWeight", weightAtlas);
        
        // Setup confidence filtering
        if (useConfidenceFiltering && confidenceTex != null)
        {
            integrationMaterial.SetTexture("_ConfidenceTex", confidenceTex);
            integrationMaterial.SetFloat("_MinConfidence", minDepthConfidence);
            integrationMaterial.SetInt("_UseConfidence", 1);
        }
        else
        {
            integrationMaterial.SetInt("_UseConfidence", 0);
        }
        
        integrationMaterial.SetMatrix("_ViewMatrix", cam.worldToCameraMatrix);
        integrationMaterial.SetMatrix("_ProjMatrix", cam.projectionMatrix);
        integrationMaterial.SetVector("_VolumeOrigin", volumeOrigin);
        integrationMaterial.SetVector("_VolumeResolution", new Vector4(volumeResolution.x, volumeResolution.y, volumeResolution.z, 0));
        integrationMaterial.SetFloat("_VoxelSize", voxelSize);
        integrationMaterial.SetFloat("_TruncDist", truncationDistance);
        integrationMaterial.SetFloat("_MaxDepth", maxDepth);
        integrationMaterial.SetInt("_SlicesPerRow", Mathf.CeilToInt(Mathf.Sqrt(volumeResolution.z)));
        
        // Integrate into temp atlases
        Graphics.Blit(null, tsdfAtlasTemp, integrationMaterial, 0);
        Graphics.Blit(null, weightAtlasTemp, integrationMaterial, 1);
        
        // Swap
        (tsdfAtlas, tsdfAtlasTemp) = (tsdfAtlasTemp, tsdfAtlas);
        (weightAtlas, weightAtlasTemp) = (weightAtlasTemp, weightAtlas);
        
        // DIAGNOSTIC: Sample weight atlas to verify integration (every 60 frames)
        // Sample Z-slice corresponding to ~1m depth (typical indoor range), NOT atlas center.
        // Atlas center maps to Z-slice 66 = world depth ~3.4m (outside camera's typical 0.3-2.5m range).
        if (frameCount % 60 == 0 && frameCount >= 60)
        {
            // Z-voxel for 1m depth
            int slicesPerRowDiag = Mathf.CeilToInt(Mathf.Sqrt(volumeResolution.z));
            int zVoxel1m = Mathf.Clamp((int)((1.0f - volumeOrigin.z) / voxelSize), 0, volumeResolution.z - 1);
            int sliceX1m = zVoxel1m % slicesPerRowDiag;
            int sliceY1m = zVoxel1m / slicesPerRowDiag;
            int sampleX = sliceX1m * volumeResolution.x + volumeResolution.x / 2 - 16;
            int sampleY = sliceY1m * volumeResolution.y + volumeResolution.y / 2 - 16;
            sampleX = Mathf.Clamp(sampleX, 0, weightAtlas.width - 32);
            sampleY = Mathf.Clamp(sampleY, 0, weightAtlas.height - 32);

            RenderTexture.active = weightAtlas;
            Texture2D weightSample = new Texture2D(32, 32, TextureFormat.RFloat, false);
            weightSample.ReadPixels(new Rect(sampleX, sampleY, 32, 32), 0, 0);
            weightSample.Apply();
            RenderTexture.active = null;
            
            float maxWeight = 0f;
            float avgWeight = 0f;
            int nonZeroCount = 0;
            Color[] pixels = weightSample.GetPixels();
            foreach (var p in pixels)
            {
                avgWeight += p.r;
                if (p.r > 0.001f)
                {
                    nonZeroCount++;
                    if (p.r > maxWeight) maxWeight = p.r;
                }
            }
            avgWeight /= pixels.Length;
            
            Debug.Log($"[TSDF] F{frameCount} Weight@1m-slice: {nonZeroCount}/{pixels.Length} non-zero, max={maxWeight:F2}, avg={avgWeight:F3} (Z-voxel={zVoxel1m} = world depth {volumeOrigin.z + (zVoxel1m+0.5f)*voxelSize:F2}m)");
            if (nonZeroCount == 0 && frameCount > 300)
            {
                // Only warn after 300 frames (~15s) — the GPU mask may still be working even if this slice is zero
                Debug.LogWarning($"[TSDF] ⚠️ No data at 1m-depth slice after {frameCount} frames. " +
                    $"This may be OK if camera has not pointed at ~1m - check TSDF GPU % in STRIPE MASK logs.");
            }
            
            Destroy(weightSample);
        }
        
        if (frameCount == 5)
        {
            Debug.Log($"[TSDF] ▶ First depth integration (frame {frameCount})");
            Debug.Log($"[TSDF] Depth texture: {depthTex.width}x{depthTex.height}");
        }
        
        if (frameCount % 150 == 0)
        {
            Debug.Log($"[TSDF] Frame {frameCount / integrationInterval} integrated");
            Debug.Log($"[TSDF] Integration time: {Time.realtimeSinceStartup:F2}s");
        }
    }
    
    void LogMemoryUsage()
    {
        // Get current memory usage
        long totalMemory = System.GC.GetTotalMemory(false);
        float totalMemoryMB = totalMemory / (1024f * 1024f);
        
        // Get Unity memory usage
        long monoHeap = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
        long monoUsed = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        float monoHeapMB = monoHeap / (1024f * 1024f);
        float monoUsedMB = monoUsed / (1024f * 1024f);
        
        // Get texture memory
        long totalAllocatedMemory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        long totalReservedMemory = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
        float allocatedMB = totalAllocatedMemory / (1024f * 1024f);
        float reservedMB = totalReservedMemory / (1024f * 1024f);
        
        Debug.Log($"═══════════════════════════════════════════════════");
        Debug.Log($"   MEMORY USAGE (Frame {frameCount})");
        Debug.Log($"═══════════════════════════════════════════════════");
        Debug.Log($"GC Total: {totalMemoryMB:F2} MB");
        Debug.Log($"Mono Heap: {monoUsedMB:F2} / {monoHeapMB:F2} MB");
        Debug.Log($"Unity Allocated: {allocatedMB:F2} MB");
        Debug.Log($"Unity Reserved: {reservedMB:F2} MB");
        Debug.Log($"Atlas Size: {atlasWidth}x{atlasHeight}");
        Debug.Log($"Voxel Count: {volumeResolution.x * volumeResolution.y * volumeResolution.z:N0}");
        Debug.Log($"═══════════════════════════════════════════════════");
    }
    
    void UpdateRayMarching()
    {
        if (frameCount == 1)
        {
            Debug.Log("[TSDF] ▶ First UpdateRayMarching() called");
        }
        
        if (rayMarchMaterial == null)
        {
            Debug.LogError("[TSDF] ✗ Ray march material is NULL!");
            return;
        }
        
        if (commandBuffer == null)
        {
            Debug.LogError("[TSDF] ✗ CommandBuffer is NULL!");
            return;
        }
        
        Camera cam = GetComponent<Camera>();
        
        // CRITICAL: Update depth texture EVERY FRAME (it's NULL for first few frames!)
        Texture2D currentDepthTex = occlusionManager.environmentDepthTexture;
        
        if (depthColorizeShader != null)
        {
            // Using DepthColorize shader - set depth texture EVERY FRAME
            if (currentDepthTex != null)
            {
                rayMarchMaterial.SetTexture("_EnvironmentDepth", currentDepthTex);
                rayMarchMaterial.SetFloat("_ColorIntensity", 0.7f);
                rayMarchMaterial.SetFloat("_NormalStrength", 1.0f);
            }
            else if (frameCount == 30)
            {
                Debug.LogWarning("[TSDF] ⚠ Depth texture still NULL at frame 30!");
            }
        }
        else
        {
            // Using TSDF ray march shader - set TSDF parameters
            rayMarchMaterial.SetTexture("_TSDFAtlas", tsdfAtlas);
            rayMarchMaterial.SetVector("_VolumeOrigin", volumeOrigin);
            rayMarchMaterial.SetVector("_VolumeResolution", new Vector4(volumeResolution.x, volumeResolution.y, volumeResolution.z, 0));
            rayMarchMaterial.SetFloat("_VoxelSize", voxelSize);
            rayMarchMaterial.SetFloat("_VolumeOpacity", volumeOpacity);
            rayMarchMaterial.SetFloat("_StepSize", stepSize);
            rayMarchMaterial.SetFloat("_SurfaceThreshold", surfaceThreshold);
            rayMarchMaterial.SetInt("_SlicesPerRow", Mathf.CeilToInt(Mathf.Sqrt(volumeResolution.z)));
            rayMarchMaterial.SetMatrix("_InvView", cam.cameraToWorldMatrix);
            rayMarchMaterial.SetMatrix("_InvProj", cam.projectionMatrix.inverse);
        }
        
        if (frameCount == 30)
        {
            Debug.Log($"[TSDF] Ray march material: {rayMarchMaterial.shader.name}");
            Debug.Log($"[TSDF] Depth texture: {currentDepthTex != null} ({currentDepthTex?.width}x{currentDepthTex?.height})");
            Debug.Log($"[TSDF] TSDF Atlas: {tsdfAtlas != null}, Size: {tsdfAtlas.width}x{tsdfAtlas.height}");
            Debug.Log($"[TSDF] CommandBuffer executing...");
            
            // CRITICAL DEBUG: Check if ARCameraBackground is rendering
            if (cameraBackground != null)
            {
                Debug.Log($"[TSDF] ARCameraBackground enabled: {cameraBackground.enabled}");
                Debug.Log($"[TSDF] ARCameraBackground customMaterial: {cameraBackground.customMaterial}");
                Debug.Log($"[TSDF] ARCameraBackground material: {cameraBackground.material?.name}");
                Debug.Log($"[TSDF] ARCameraBackground useCustomMaterial: {cameraBackground.useCustomMaterial}");
            }
            else
            {
                Debug.LogError("[TSDF] ARCameraBackground is NULL at frame 30!");
            }
        }
        
        // Rebuild command buffer every frame (CRITICAL - same as ARVolumetricBlend)
        commandBuffer.Clear();
        
        // DIRECT BLIT: Just apply shader fullscreen (ARCameraBackground already rendered)
        commandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, rayMarchMaterial);
        
        if (frameCount == 30)
        {
            Debug.Log($"[TSDF] ✓ CommandBuffer rebuilt: {commandBuffer.sizeInBytes} bytes");
            Debug.Log($"[TSDF] Pattern: Direct fullscreen shader blit");
        }
        
        if (frameCount == 60)
        {
            Debug.Log($"[TSDF] Ray marching active, volume opacity: {volumeOpacity}");
            Debug.Log($"[TSDF] Step size: {stepSize}, Surface threshold: {surfaceThreshold}");
        }
    }
    
    void OnDestroy()
    {
        if (tsdfAtlas) tsdfAtlas.Release();
        if (tsdfAtlasTemp) tsdfAtlasTemp.Release();
        if (weightAtlas) weightAtlas.Release();
        if (weightAtlasTemp) weightAtlasTemp.Release();
        
        if (commandBuffer != null)
        {
            GetComponent<Camera>()?.RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
            commandBuffer.Release();
        }
        
        if (integrationMaterial) Destroy(integrationMaterial);
        if (rayMarchMaterial) Destroy(rayMarchMaterial);
    }
}
