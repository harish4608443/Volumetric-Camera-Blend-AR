using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(Camera))]
public class TSDFVolumetricFusion : MonoBehaviour
{
    // TSDF Volume Settings
    [Header("TSDF Volume Configuration")]
    [SerializeField] private int volumeResolution = 64;
    [SerializeField] private float voxelSize = 0.05f;
    [SerializeField] private int atlasSize = 512;
    [SerializeField] private float truncationDistance = 0.2f;
    [SerializeField] private float maxDepth = 5.0f;
    
    [Header("Integration Settings")]
    [SerializeField] private int integrationInterval = 5;
    
    [Header("Visualization")]
    [SerializeField] private float volumeOpacity = 0.5f;
    
    // TSDF Atlas Texture (2D representation of 3D volume)
    private RenderTexture tsdfAtlas;
    
    // Integration shader
    private Material integrationMaterial;
    private Shader integrationShader;
    
    // Visualization
    private CommandBuffer commandBuffer;
    private Material visualizeMaterial;
    
    // AR Components
    private ARCameraManager arCameraManager;
    private AROcclusionManager occlusionManager;
    private Camera mainCamera;
    
    // Frame tracking
    private int frameCount = 0;
    private Matrix4x4 previousViewMatrix;
    private bool isInitialized = false;
    
    void Start()
    {
        Debug.Log("═══════════════════════════════════════════════════════");
        Debug.Log("         TSDFVolumetricFusion DISABLED");
        Debug.Log("         (CommandBuffer blits don't work - use ARVolumetricBlend)");
        Debug.Log("═══════════════════════════════════════════════════════");
        enabled = false;
        return;
        
        // Get AR components
        arCameraManager = FindObjectOfType<ARCameraManager>();
        occlusionManager = FindObjectOfType<AROcclusionManager>();
        mainCamera = GetComponent<Camera>();
        
        if (arCameraManager == null || occlusionManager == null)
        {
            Debug.LogError("[TSDF] Missing ARCameraManager or AROcclusionManager!");
            enabled = false;
            return;
        }
        
        // Ensure depth is enabled
        if (occlusionManager.requestedEnvironmentDepthMode == EnvironmentDepthMode.Disabled)
        {
            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
        }
        
        // Initialize TSDF atlas
        InitializeTSDFAtlas();
        
        // Use DepthColorize shader (proven to work)
        Shader visualizeShader = Shader.Find("Custom/DepthColorize");
        
        if (visualizeShader == null)
        {
            Debug.LogError($"[TSDF] DepthColorize shader not found!");
            enabled = false;
            return;
        }
        
        visualizeMaterial = new Material(visualizeShader);
        Debug.Log($"[TSDF] Using DepthColorize visualization");
        
        // Setup CommandBuffer (proven pattern from ARVolumetricBlend)
        if (mainCamera.clearFlags == CameraClearFlags.SolidColor)
        {
            mainCamera.clearFlags = CameraClearFlags.Depth;
            Debug.Log("[TSDF] Changed clearFlags to Depth");
        }
        
        commandBuffer = new CommandBuffer { name = "TSDF Visualization" };
        mainCamera.AddCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
        
        previousViewMatrix = mainCamera.worldToCameraMatrix;
        isInitialized = true;
        
        Debug.Log($"[TSDF] Initialized - Volume: {volumeResolution}³, Voxel: {voxelSize}m, Atlas: {atlasSize}");
    }
    
    void InitializeTSDFAtlas()
    {
        tsdfAtlas = new RenderTexture(atlasSize, atlasSize, 0, RenderTextureFormat.ARGBFloat);
        tsdfAtlas.enableRandomWrite = false;
        tsdfAtlas.filterMode = FilterMode.Point;
        tsdfAtlas.wrapMode = TextureWrapMode.Clamp;
        tsdfAtlas.Create();
        
        // Clear to default TSDF values (distance = truncation, weight = 0)
        RenderTexture.active = tsdfAtlas;
        GL.Clear(true, true, new Color(truncationDistance, 0, 0, 0));
        RenderTexture.active = null;
        
        Debug.Log($"[TSDF] Atlas created: {atlasSize}x{atlasSize}");
    }
    
    void LateUpdate()
    {
        if (!isInitialized) return;
        
        frameCount++;
        
        if (frameCount == 1)
        {
            Debug.Log("[TSDF] ▶ First frame - visualization active");
        }
        
        // Update visualization (using proven CommandBuffer pattern)
        UpdateVisualization();
    }
    
    void IntegrateDepth()
    {
        var depthTexture = occlusionManager.environmentDepthTexture;
        if (depthTexture == null) return;
        
        // Get camera intrinsics
        if (!arCameraManager.TryGetIntrinsics(out XRCameraIntrinsics intrinsics))
        {
            return;
        }
        
        // Calculate FOV from intrinsics
        float focalLengthX = intrinsics.focalLength.x;
        float focalLengthY = intrinsics.focalLength.y;
        float principalPointX = intrinsics.principalPoint.x;
        float principalPointY = intrinsics.principalPoint.y;
        
        float fovX = 2.0f * Mathf.Atan(intrinsics.resolution.x / (2.0f * focalLengthX)) * Mathf.Rad2Deg;
        float fovY = 2.0f * Mathf.Atan(intrinsics.resolution.y / (2.0f * focalLengthY)) * Mathf.Rad2Deg;
        
        // Setup integration material
        integrationMaterial.SetTexture("_DepthTex", depthTexture);
        integrationMaterial.SetTexture("_TSDFAtlas", tsdfAtlas);
        integrationMaterial.SetMatrix("_WorldToCameraMatrix", mainCamera.worldToCameraMatrix);
        integrationMaterial.SetMatrix("_CameraToWorldMatrix", mainCamera.cameraToWorldMatrix);
        integrationMaterial.SetInt("_VolumeResolution", volumeResolution);
        integrationMaterial.SetFloat("_VoxelSize", voxelSize);
        integrationMaterial.SetFloat("_TruncationDistance", truncationDistance);
        integrationMaterial.SetFloat("_MaxDepth", maxDepth);
        integrationMaterial.SetVector("_DepthResolution", new Vector4(depthTexture.width, depthTexture.height, 0, 0));
        integrationMaterial.SetVector("_CameraIntrinsics", new Vector4(focalLengthX, focalLengthY, principalPointX, principalPointY));
        integrationMaterial.SetFloat("_FovX", fovX);
        integrationMaterial.SetFloat("_FovY", fovY);
        
        // Integrate into TSDF atlas
        RenderTexture temp = RenderTexture.GetTemporary(atlasSize, atlasSize, 0, RenderTextureFormat.ARGBFloat);
        Graphics.Blit(tsdfAtlas, temp, integrationMaterial);
        Graphics.Blit(temp, tsdfAtlas);
        RenderTexture.ReleaseTemporary(temp);
        
        if (frameCount % 30 == 0)
        {
            Debug.Log($"[TSDF] Frame {frameCount} integrated | Depth: {depthTexture.width}x{depthTexture.height} | FOV: {fovX:F1}°×{fovY:F1}°");
        }
    }
    
    void UpdateVisualization()
    {
        // Use proven CommandBuffer pattern from ARVolumetricBlend
        var depthTexture = occlusionManager.environmentDepthTexture;
        if (depthTexture == null) return;
        
        commandBuffer.Clear();
        
        // Setup visualization material (DepthColorize shader)
        visualizeMaterial.SetTexture("_DepthTex", depthTexture);
        visualizeMaterial.SetFloat("_DepthScale", 2.0f);
        
        // Blit to screen (proven working approach)
        commandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, visualizeMaterial);
    }
    
    void OnDestroy()
    {
        if (commandBuffer != null && mainCamera != null)
        {
            mainCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
        }
        
        if (tsdfAtlas != null)
        {
            tsdfAtlas.Release();
            Destroy(tsdfAtlas);
        }
        
        if (integrationMaterial != null) Destroy(integrationMaterial);
        if (visualizeMaterial != null) Destroy(visualizeMaterial);
    }
}
