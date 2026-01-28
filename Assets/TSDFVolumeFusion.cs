using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Rendering;

/// <summary>
/// TSDF Volume Fusion for true volumetric reconstruction
/// Integrates depth frames over time to build 3D voxel representation
/// </summary>
public class TSDFVolumeFusion : MonoBehaviour
{
    [Header("AR Components")]
    public AROcclusionManager occlusionManager;
    public ARCameraManager cameraManager;
    
    [Header("Volume Settings")]
    [Tooltip("Volume dimensions in voxels")]
    public Vector3Int volumeResolution = new Vector3Int(128, 128, 128);
    
    [Tooltip("Size of each voxel in meters")]
    public float voxelSize = 0.05f;
    
    [Tooltip("TSDF truncation distance in meters")]
    public float truncationDistance = 0.15f;
    
    [Header("Rendering")]
    public Shader rayMarchShader;
    public float volumeOpacity = 0.5f;
    public float rayMarchStepSize = 0.01f;
    public int maxRayMarchSteps = 200;
    
    [Header("Integration")]
    [Tooltip("Integrate every N frames")]
    public int integrationInterval = 5;
    
    // Compute shader and resources
    public ComputeShader volumeIntegrationCS;
    private RenderTexture tsdfVolume;
    private Material rayMarchMaterial;
    private CommandBuffer commandBuffer;
    
    private int integrateKernel;
    private int clearKernel;
    private int frameCount = 0;
    private Vector3 volumeOrigin;
    
    void Start()
    {
        if (volumeIntegrationCS == null)
        {
            Debug.LogError("TSDF Volume Integration Compute Shader not assigned!");
            enabled = false;
            return;
        }
        
        if (rayMarchShader == null)
        {
            Debug.LogError("Ray March Shader not assigned!");
            enabled = false;
            return;
        }
        
        // Initialize TSDF volume (3D texture)
        tsdfVolume = new RenderTexture(volumeResolution.x, volumeResolution.y, 0, RenderTextureFormat.RGFloat);
        tsdfVolume.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        tsdfVolume.volumeDepth = volumeResolution.z;
        tsdfVolume.enableRandomWrite = true;
        tsdfVolume.Create();
        
        Debug.Log($"✓ TSDF Volume created: {volumeResolution} voxels, {voxelSize}m voxel size");
        
        // Get compute kernels
        integrateKernel = volumeIntegrationCS.FindKernel("IntegrateDepth");
        clearKernel = volumeIntegrationCS.FindKernel("ClearVolume");
        
        // Clear volume
        ClearVolume();
        
        // Create ray march material
        rayMarchMaterial = new Material(rayMarchShader);
        
        // Setup command buffer for rendering
        Camera cam = GetComponent<Camera>();
        commandBuffer = new CommandBuffer();
        commandBuffer.name = "TSDF Ray March";
        cam.AddCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
        
        // Volume origin (centered in front of camera)
        volumeOrigin = transform.position + transform.forward * (volumeResolution.z * voxelSize * 0.5f);
        
        Debug.Log("✓ TSDF Volume Fusion initialized");
    }
    
    void ClearVolume()
    {
        volumeIntegrationCS.SetTexture(clearKernel, "TSDFVolume", tsdfVolume);
        volumeIntegrationCS.SetVector("VolumeSize", new Vector3(volumeResolution.x, volumeResolution.y, volumeResolution.z));
        
        int threadGroupsX = Mathf.CeilToInt(volumeResolution.x / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(volumeResolution.y / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(volumeResolution.z / 8.0f);
        
        volumeIntegrationCS.Dispatch(clearKernel, threadGroupsX, threadGroupsY, threadGroupsZ);
        
        Debug.Log("✓ TSDF Volume cleared");
    }
    
    void LateUpdate()
    {
        frameCount++;
        
        // Integrate depth every N frames
        if (frameCount % integrationInterval == 0)
        {
            IntegrateDepthFrame();
        }
        
        // Update ray march rendering
        UpdateRayMarchRendering();
    }
    
    void IntegrateDepthFrame()
    {
        if (occlusionManager == null) return;
        
        Texture depthTexture = occlusionManager.environmentDepthTexture;
        if (depthTexture == null) return;
        
        // Set compute shader parameters
        volumeIntegrationCS.SetTexture(integrateKernel, "TSDFVolume", tsdfVolume);
        volumeIntegrationCS.SetTexture(integrateKernel, "DepthTexture", depthTexture);
        volumeIntegrationCS.SetVector("VolumeSize", new Vector3(volumeResolution.x, volumeResolution.y, volumeResolution.z));
        volumeIntegrationCS.SetVector("VolumeOrigin", volumeOrigin);
        volumeIntegrationCS.SetFloat("VoxelSize", voxelSize);
        volumeIntegrationCS.SetFloat("TruncationDistance", truncationDistance);
        volumeIntegrationCS.SetVector("DepthResolution", new Vector2(depthTexture.width, depthTexture.height));
        
        // Camera transforms
        Matrix4x4 cameraToWorld = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
        volumeIntegrationCS.SetMatrix("CameraToWorld", cameraToWorld);
        
        // Dispatch compute shader
        int threadGroupsX = Mathf.CeilToInt(volumeResolution.x / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(volumeResolution.y / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(volumeResolution.z / 8.0f);
        
        volumeIntegrationCS.Dispatch(integrateKernel, threadGroupsX, threadGroupsY, threadGroupsZ);
        
        if (frameCount % 300 == 0)
        {
            Debug.Log($"[TSDF] Integrated depth frame {frameCount / integrationInterval}");
        }
    }
    
    void UpdateRayMarchRendering()
    {
        if (rayMarchMaterial == null || commandBuffer == null) return;
        
        // Update material properties
        rayMarchMaterial.SetTexture("TSDFVolume", tsdfVolume);
        rayMarchMaterial.SetVector("VolumeSize", new Vector3(volumeResolution.x, volumeResolution.y, volumeResolution.z));
        rayMarchMaterial.SetVector("VolumeOrigin", volumeOrigin);
        rayMarchMaterial.SetFloat("VoxelSize", voxelSize);
        rayMarchMaterial.SetFloat("_VolumeOpacity", volumeOpacity);
        rayMarchMaterial.SetFloat("_StepSize", rayMarchStepSize);
        rayMarchMaterial.SetInt("_MaxSteps", maxRayMarchSteps);
        
        Matrix4x4 viewToWorld = Camera.main.cameraToWorldMatrix;
        Matrix4x4 invProjection = Camera.main.projectionMatrix.inverse;
        rayMarchMaterial.SetMatrix("_CameraToWorld", viewToWorld);
        rayMarchMaterial.SetMatrix("_CameraInvProjection", invProjection);
        
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
        if (tsdfVolume != null)
        {
            tsdfVolume.Release();
            Destroy(tsdfVolume);
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
    }
    
    // Public API
    public void ResetVolume()
    {
        ClearVolume();
        Debug.Log("✓ TSDF Volume reset");
    }
}
