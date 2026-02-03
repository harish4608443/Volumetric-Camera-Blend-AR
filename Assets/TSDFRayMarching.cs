using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// TSDF Ray Marching Visualization
/// Copies ARVolumetricBlend's proven pattern but renders ray marched TSDF volume
/// </summary>
[RequireComponent(typeof(Camera))]
public class TSDFRayMarching : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TSDFVolumeAtlas tsdfVolume;
    
    [Header("Ray Marching Settings")]
    [SerializeField] private float stepSize = 0.02f;
    [SerializeField] private int maxSteps = 128;
    [SerializeField] private float surfaceThreshold = 0.03f;
    [SerializeField] private float volumeOpacity = 0.6f;
    
    private Camera mainCamera;
    private CommandBuffer commandBuffer;
    private Material rayMarchMaterial;
    private Shader rayMarchShader;
    
    void Start()
    {
        Debug.Log("═══════════════════════════════════════════════════════");
        Debug.Log("         TSDF RAY MARCHING VISUALIZATION");
        Debug.Log("         Camera feed + volumetric TSDF overlay");
        Debug.Log("═══════════════════════════════════════════════════════");
        
        mainCamera = GetComponent<Camera>();
        
        // Find TSDF volume
        if (tsdfVolume == null)
        {
            tsdfVolume = FindObjectOfType<TSDFVolumeAtlas>();
        }
        
        if (tsdfVolume == null)
        {
            Debug.LogError("[RayMarch] TSDFVolumeAtlas not found!");
            enabled = false;
            return;
        }
        
        // Load shader
        rayMarchShader = Shader.Find("Custom/TSDFRayMarch");
        if (rayMarchShader == null)
        {
            Debug.LogError("[RayMarch] Custom/TSDFRayMarch shader not found!");
            enabled = false;
            return;
        }
        
        rayMarchMaterial = new Material(rayMarchShader);
        
        // Setup CommandBuffer (EXACT pattern from ARVolumetricBlend)
        if (mainCamera.clearFlags == CameraClearFlags.SolidColor)
        {
            mainCamera.clearFlags = CameraClearFlags.Depth;
            Debug.Log("[RayMarch] Changed clearFlags to Depth");
        }
        
        commandBuffer = new CommandBuffer { name = "TSDF Ray March" };
        mainCamera.AddCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
        
        Debug.Log("[RayMarch] Initialized successfully");
    }
    
    void LateUpdate()
    {
        if (rayMarchMaterial == null || tsdfVolume == null) return;
        
        // Get TSDF atlas from volume
        RenderTexture tsdfAtlas = tsdfVolume.GetTSDFAtlas();
        if (tsdfAtlas == null) return;
        
        commandBuffer.Clear();
        
        // Setup material properties
        rayMarchMaterial.SetTexture("_TSDFAtlas", tsdfAtlas);
        rayMarchMaterial.SetMatrix("_CameraToWorldMatrix", mainCamera.cameraToWorldMatrix);
        rayMarchMaterial.SetMatrix("_WorldToCameraMatrix", mainCamera.worldToCameraMatrix);
        rayMarchMaterial.SetMatrix("_ProjectionMatrix", mainCamera.projectionMatrix);
        rayMarchMaterial.SetInt("_VolumeResolution", tsdfVolume.volumeResolution.x);
        rayMarchMaterial.SetFloat("_VoxelSize", tsdfVolume.voxelSize);
        rayMarchMaterial.SetFloat("_StepSize", stepSize);
        rayMarchMaterial.SetInt("_MaxSteps", maxSteps);
        rayMarchMaterial.SetFloat("_SurfaceThreshold", surfaceThreshold);
        rayMarchMaterial.SetFloat("_VolumeOpacity", volumeOpacity);
        rayMarchMaterial.SetVector("_VolumeCenter", tsdfVolume.volumeCenter);
        rayMarchMaterial.SetFloat("_TruncationDistance", tsdfVolume.truncationDistance);
        
        // Blit to screen (EXACT pattern from ARVolumetricBlend)
        commandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, rayMarchMaterial);
    }
    
    void OnDestroy()
    {
        if (commandBuffer != null && mainCamera != null)
        {
            mainCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
        }
        
        if (rayMarchMaterial != null)
        {
            Destroy(rayMarchMaterial);
        }
    }
}
