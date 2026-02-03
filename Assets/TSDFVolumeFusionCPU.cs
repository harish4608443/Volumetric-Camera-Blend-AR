using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Rendering;
using System.Threading.Tasks;

/// <summary>
/// CPU-based TSDF Volume Fusion (works on OpenGLES3 without compute shaders)
/// Slower than GPU but compatible with ARCore on Android
/// </summary>
[RequireComponent(typeof(Camera))]
public class TSDFVolumeFusionCPU : MonoBehaviour
{
    [Header("AR Components")]
    public AROcclusionManager occlusionManager;
    public ARCameraManager cameraManager;
    
    [Header("Volume Settings")]
    [Tooltip("Volume dimensions in voxels - Keep small for CPU performance")]
    public Vector3Int volumeResolution = new Vector3Int(128, 64, 128);
    
    [Tooltip("Size of each voxel in meters")]
    public float voxelSize = 0.03f;
    
    [Tooltip("TSDF truncation distance in meters")]
    public float truncationDistance = 0.15f;
    
    [Tooltip("Maximum valid depth value")]
    public float maxDepth = 5.0f;
    
    [Header("Rendering")]
    public Shader rayMarchShader;
    public float volumeOpacity = 0.3f;
    public float rayMarchStepSize = 0.03f;
    public int maxRayMarchSteps = 100;
    public float surfaceThreshold = 0.02f;
    
    [Header("Integration")]
    [Tooltip("Integrate every N frames")]
    public int integrationInterval = 10;
    
    [Tooltip("Use multithreading for CPU integration")]
    public bool useMultithreading = true;
    
    // Volume data (CPU)
    private float[] tsdfData;
    private float[] weightData;
    private Texture3D tsdfTexture;
    
    // Rendering
    private Material rayMarchMaterial;
    private CommandBuffer commandBuffer;
    private int frameCount = 0;
    private Vector3 volumeOrigin;
    
    // Depth cache
    private Texture2D depthCPU;
    
    void Start()
    {
        Debug.Log("[CPU TSDF] Starting initialization...");
        
        if (occlusionManager == null)
        {
            Debug.LogError("[CPU TSDF] OcclusionManager not assigned!");
            enabled = false;
            return;
        }
        
        if (rayMarchShader == null)
        {
            Debug.LogError("[CPU TSDF] RayMarchShader not assigned!");
            enabled = false;
            return;
        }
        
        // Initialize volume data arrays
        int totalVoxels = volumeResolution.x * volumeResolution.y * volumeResolution.z;
        tsdfData = new float[totalVoxels];
        weightData = new float[totalVoxels];
        
        // Initialize to empty space
        for (int i = 0; i < totalVoxels; i++)
        {
            tsdfData[i] = 1.0f; // Far from surface
            weightData[i] = 0.0f; // No observations
        }
        
        // Create 3D texture for GPU ray marching
        tsdfTexture = new Texture3D(volumeResolution.x, volumeResolution.y, volumeResolution.z, TextureFormat.RFloat, false);
        tsdfTexture.wrapMode = TextureWrapMode.Clamp;
        tsdfTexture.filterMode = FilterMode.Trilinear;
        UpdateVolumeTexture();
        
        Debug.Log($"✓ CPU-based TSDF Volume created: {volumeResolution} voxels @ {voxelSize}m, {totalVoxels} total");
        
        // Create ray march material
        rayMarchMaterial = new Material(rayMarchShader);
        
        // Setup command buffer for rendering
        Camera cam = GetComponent<Camera>();
        commandBuffer = new CommandBuffer();
        commandBuffer.name = "TSDF Ray March CPU";
        cam.AddCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
        
        // Volume origin (centered in front of camera)
        volumeOrigin = transform.position + transform.forward * (volumeResolution.z * voxelSize * 0.5f);
        
        Debug.Log($"✓ CPU TSDF Volume Fusion initialized. Origin: {volumeOrigin}, Camera: {cam.name}");
        Debug.Log($"   Volume extent: {volumeResolution.x * voxelSize}m x {volumeResolution.y * voxelSize}m x {volumeResolution.z * voxelSize}m");
    }
    
    void LateUpdate()
    {
        frameCount++;
        
        // Integrate depth every N frames
        if (frameCount % integrationInterval == 0)
        {
            IntegrateDepthFrameCPU();
        }
        
        // Update ray march rendering every frame
        UpdateRayMarchRendering();
    }
    
    void IntegrateDepthFrameCPU()
    {
        if (occlusionManager == null)
        {
            Debug.LogWarning("[CPU TSDF] OcclusionManager is null");
            return;
        }
        
        Texture depthTexture = occlusionManager.environmentDepthTexture;
        if (depthTexture == null)
        {
            Debug.LogWarning("[CPU TSDF] No depth texture available");
            return;
        }
        
        // Get camera matrices
        Camera cam = GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogError("[CPU TSDF] Camera is null");
            return;
        }
        
        Matrix4x4 viewMatrix = cam.worldToCameraMatrix;
        Matrix4x4 projectionMatrix = cam.projectionMatrix;
        
        int depthWidth = depthTexture.width;
        int depthHeight = depthTexture.height;
        
        // Read depth texture to CPU (expensive but necessary)
        if (depthCPU == null || depthCPU.width != depthWidth || depthCPU.height != depthHeight)
        {
            depthCPU = new Texture2D(depthWidth, depthHeight, TextureFormat.RFloat, false);
        }
        
        // Copy depth texture to readable format
        RenderTexture rt = null;
        try
        {
            rt = RenderTexture.GetTemporary(depthWidth, depthHeight, 0, RenderTextureFormat.RFloat);
            Graphics.Blit(depthTexture, rt);
            RenderTexture.active = rt;
            depthCPU.ReadPixels(new Rect(0, 0, depthWidth, depthHeight), 0, 0);
            depthCPU.Apply();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CPU TSDF] Error reading depth texture: {e.Message}");
            return;
        }
        finally
        {
            RenderTexture.active = null;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
        }
        
        Color[] depthPixels = depthCPU.GetPixels();
        
        // Debug: Sample some depth values to verify
        if (frameCount == 30)
        {
            float centerDepth = depthPixels[(depthHeight/2) * depthWidth + (depthWidth/2)].r;
            float minDepth = 999f, maxDepth = 0f;
            for (int i = 0; i < depthPixels.Length; i += 100)
            {
                float d = depthPixels[i].r;
                if (d > 0.01f) { minDepth = Mathf.Min(minDepth, d); maxDepth = Mathf.Max(maxDepth, d); }
            }
            Debug.Log($"[CPU TSDF] Depth values - center: {centerDepth:F3}, min: {minDepth:F3}, max: {maxDepth:F3}");
        }
        
        // Integrate volume (CPU or multithreaded)
        if (useMultithreading)
        {
            IntegrateVolumeParallel(depthPixels, depthWidth, depthHeight, viewMatrix, projectionMatrix);
        }
        else
        {
            IntegrateVolumeSingleThread(depthPixels, depthWidth, depthHeight, viewMatrix, projectionMatrix);
        }
        
        // Update GPU texture
        UpdateVolumeTexture();
        
        if (frameCount % 300 == 0)
        {
            // Sample TSDF values at volume center to verify integration
            int centerIdx = (volumeResolution.z / 2) * volumeResolution.y * volumeResolution.x +
                            (volumeResolution.y / 2) * volumeResolution.x +
                            (volumeResolution.x / 2);
            float centerSDF = tsdfData[centerIdx];
            float centerWeight = weightData[centerIdx];
            
            Debug.Log($"[CPU TSDF] Frame {frameCount / integrationInterval}, depth: {depthWidth}x{depthHeight}, center SDF: {centerSDF:F3}, weight: {centerWeight:F3}");
        }
    }
    
    void IntegrateVolumeSingleThread(Color[] depthPixels, int depthWidth, int depthHeight, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
    {
        for (int z = 0; z < volumeResolution.z; z++)
        {
            for (int y = 0; y < volumeResolution.y; y++)
            {
                for (int x = 0; x < volumeResolution.x; x++)
                {
                    IntegrateVoxel(x, y, z, depthPixels, depthWidth, depthHeight, viewMatrix, projectionMatrix);
                }
            }
        }
    }
    
    void IntegrateVolumeParallel(Color[] depthPixels, int depthWidth, int depthHeight, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
    {
        Parallel.For(0, volumeResolution.z, z =>
        {
            for (int y = 0; y < volumeResolution.y; y++)
            {
                for (int x = 0; x < volumeResolution.x; x++)
                {
                    IntegrateVoxel(x, y, z, depthPixels, depthWidth, depthHeight, viewMatrix, projectionMatrix);
                }
            }
        });
    }
    
    void IntegrateVoxel(int x, int y, int z, Color[] depthPixels, int depthWidth, int depthHeight, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
    {
        // Convert voxel to world position
        Vector3 voxelWorld = volumeOrigin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * voxelSize;
        
        // Transform to camera space
        Vector4 voxelCam = viewMatrix * new Vector4(voxelWorld.x, voxelWorld.y, voxelWorld.z, 1.0f);
        
        // Behind camera check
        if (voxelCam.z <= 0) return;
        
        // Project to screen space
        Vector4 voxelClip = projectionMatrix * voxelCam;
        Vector3 voxelNDC = new Vector3(voxelClip.x / voxelClip.w, voxelClip.y / voxelClip.w, voxelClip.z / voxelClip.w);
        
        // Convert to UV coordinates [0,1]
        Vector2 uv = new Vector2(voxelNDC.x * 0.5f + 0.5f, voxelNDC.y * 0.5f + 0.5f);
        
        // Check if within depth image bounds
        if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return;
        
        // Sample depth (flip Y for Android)
        #if UNITY_ANDROID
        uv.y = 1.0f - uv.y;
        #endif
        
        int pixelX = Mathf.Clamp((int)(uv.x * depthWidth), 0, depthWidth - 1);
        int pixelY = Mathf.Clamp((int)(uv.y * depthHeight), 0, depthHeight - 1);
        float measuredDepth = depthPixels[pixelY * depthWidth + pixelX].r;
        
        // Debug first valid integration
        if (frameCount == 30 && x == volumeResolution.x/2 && y == volumeResolution.y/2 && z == volumeResolution.z/2)
        {
            Debug.Log($"[CPU TSDF] Center voxel - world: {voxelWorld}, cam: {voxelCam.z:F3}, measured: {measuredDepth:F3}, uv: {uv}");
        }
        
        // Validate depth
        if (measuredDepth < 0.01f || measuredDepth > maxDepth) return;
        
        // Compute signed distance
        float sdf = measuredDepth - voxelCam.z;
        
        // Apply truncation
        if (sdf < -truncationDistance) return;
        
        // Normalize to [-1, 1]
        sdf = Mathf.Clamp(sdf / truncationDistance, -1.0f, 1.0f);
        
        // Get voxel index
        int idx = x + y * volumeResolution.x + z * volumeResolution.x * volumeResolution.y;
        
        // Read current values
        float currentSDF = tsdfData[idx];
        float currentWeight = weightData[idx];
        
        // Weighted average update
        float newWeight = 1.0f;
        float totalWeight = currentWeight + newWeight;
        float updatedSDF = (currentSDF * currentWeight + sdf * newWeight) / totalWeight;
        
        // Cap weight to prevent overflow
        totalWeight = Mathf.Min(totalWeight, 100.0f);
        
        // Write back (thread-safe for parallel access by different z slices)
        tsdfData[idx] = updatedSDF;
        weightData[idx] = totalWeight;
    }
    
    void UpdateVolumeTexture()
    {
        // Convert float array to Color array for SetPixels
        Color[] colors = new Color[tsdfData.Length];
        int nonEmptyVoxels = 0;
        for (int i = 0; i < tsdfData.Length; i++)
        {
            colors[i] = new Color(tsdfData[i], 0, 0, 0);
            if (Mathf.Abs(tsdfData[i]) < 0.5f) nonEmptyVoxels++;
        }
        
        tsdfTexture.SetPixels(colors);
        tsdfTexture.Apply();
        
        if (frameCount % 300 == 0)
        {
            Debug.Log($"[CPU TSDF] Texture updated, {nonEmptyVoxels} voxels near surface (|SDF| < 0.5)");
        }
    }
    
    void UpdateRayMarchRendering()
    {
        if (rayMarchMaterial == null)
        {
            Debug.LogWarning("[CPU TSDF] RayMarchMaterial is null");
            return;
        }
        
        if (commandBuffer == null)
        {
            Debug.LogWarning("[CPU TSDF] CommandBuffer is null");
            return;
        }
        
        // Update material properties
        rayMarchMaterial.SetTexture("TSDFVolume", tsdfTexture);
        rayMarchMaterial.SetVector("VolumeSize", new Vector3(volumeResolution.x, volumeResolution.y, volumeResolution.z));
        rayMarchMaterial.SetVector("VolumeOrigin", volumeOrigin);
        rayMarchMaterial.SetFloat("VoxelSize", voxelSize);
        rayMarchMaterial.SetFloat("_VolumeOpacity", volumeOpacity);
        rayMarchMaterial.SetFloat("_StepSize", rayMarchStepSize);
        rayMarchMaterial.SetInt("_MaxSteps", maxRayMarchSteps);
        rayMarchMaterial.SetFloat("_SurfaceThreshold", surfaceThreshold);
        
        if (frameCount % 300 == 0)
        {
            Debug.Log($"[CPU TSDF] Shader params - Origin: {volumeOrigin}, Resolution: {volumeResolution}, Opacity: {volumeOpacity}, StepSize: {rayMarchStepSize}");
        }
        
        // Rebuild command buffer
        commandBuffer.Clear();
        
        int tempRT = Shader.PropertyToID("_TempRT");
        commandBuffer.GetTemporaryRT(tempRT, -1, -1, 0, FilterMode.Bilinear);
        commandBuffer.Blit(BuiltinRenderTextureType.CameraTarget, tempRT);
        commandBuffer.Blit(tempRT, BuiltinRenderTextureType.CameraTarget, rayMarchMaterial);
        commandBuffer.ReleaseTemporaryRT(tempRT);
    }
    
    void OnDestroy()
    {
        if (tsdfTexture != null)
        {
            Destroy(tsdfTexture);
        }
        
        if (commandBuffer != null)
        {
            Camera cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
            }
            commandBuffer.Release();
        }
        
        if (rayMarchMaterial != null)
        {
            Destroy(rayMarchMaterial);
        }
        
        if (depthCPU != null)
        {
            Destroy(depthCPU);
        }
    }
    
    public void ResetVolume()
    {
        for (int i = 0; i < tsdfData.Length; i++)
        {
            tsdfData[i] = 1.0f;
            weightData[i] = 0.0f;
        }
        UpdateVolumeTexture();
        Debug.Log("✓ CPU TSDF Volume reset");
    }
}
