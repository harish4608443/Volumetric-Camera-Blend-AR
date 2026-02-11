using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Rendering;

/// <summary>
/// Renders TSDF volume with ray marching, skipping unreliable voxels
/// Works with ARCombinedOverlay - stripes show through unreliable regions
/// Attach to AR Camera (after TSDFVolumeAtlas)
/// </summary>
[RequireComponent(typeof(Camera))]
public class TSDFReliableRenderer : MonoBehaviour
{
    [Header("TSDF Source")]
    public TSDFVolumeAtlas tsdfAtlas;
    
    [Header("Shader Settings")]
    public Shader tsdfRayMarchShader;
    [Tooltip("Minimum weight to consider a voxel reliable")]
    [Range(0.1f, 10f)]
    public float minWeight = 1.0f;
    [Range(0.01f, 0.1f)]
    public float stepSize = 0.03f;
    [Range(64, 512)]
    public int maxSteps = 256;
    
    [Header("Debug")]
    public bool enableRendering = true;
    
    private Material rayMarchMaterial;
    private CommandBuffer commandBuffer;
    private Camera cam;

    void Start()
    {
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("         TSDF RELIABLE RENDERER");
        Debug.Log("         Shows only reliable voxels");
        Debug.Log("         Stripes show through unreliable regions");
        Debug.Log("═══════════════════════════════════════════════════");
        
        cam = GetComponent<Camera>();
        
        // Auto-find TSDF atlas if not assigned
        if (tsdfAtlas == null)
        {
            tsdfAtlas = FindObjectOfType<TSDFVolumeAtlas>();
            Debug.Log($"[TSDF Renderer] Auto-found TSDFVolumeAtlas: {tsdfAtlas != null}");
        }
        
        // Auto-load shader if not assigned
        if (tsdfRayMarchShader == null)
        {
            tsdfRayMarchShader = Shader.Find("Custom/TSDFRayMarchReliable");
            Debug.Log($"[TSDF Renderer] Auto-loaded shader: {tsdfRayMarchShader != null}");
        }
        
        if (tsdfAtlas == null || tsdfRayMarchShader == null)
        {
            Debug.LogError("[TSDF Renderer] Missing TSDF atlas or shader!");
            enabled = false;
            return;
        }
        
        // Create material
        rayMarchMaterial = new Material(tsdfRayMarchShader);
        Debug.Log($"[TSDF Renderer] Material created: {rayMarchMaterial != null}");
        
        // Create command buffer
        commandBuffer = new CommandBuffer();
        commandBuffer.name = "TSDF Reliable Rendering";
        
        // Add to camera - render after stripes but before UI
        cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, commandBuffer);
        
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("     TSDF RELIABLE RENDERER INITIALIZED");
        Debug.Log("═══════════════════════════════════════════════════");
    }

    void LateUpdate()
    {
        if (!enableRendering || tsdfAtlas == null || rayMarchMaterial == null)
            return;
        
        UpdateRendering();
    }

    void UpdateRendering()
    {
        // Get TSDF data
        RenderTexture tsdfTex = tsdfAtlas.GetTSDFAtlas();
        RenderTexture weightTex = tsdfAtlas.GetWeightAtlas();
        
        if (tsdfTex == null || weightTex == null)
            return;
        
        // Set shader parameters
        rayMarchMaterial.SetTexture("_TSDFAtlas", tsdfTex);
        rayMarchMaterial.SetTexture("_WeightAtlas", weightTex);
        rayMarchMaterial.SetVector("_VolumeOrigin", tsdfAtlas.volumeCenter);
        rayMarchMaterial.SetVector("_VolumeResolution", new Vector4(
            tsdfAtlas.GetVolumeResolution().x,
            tsdfAtlas.GetVolumeResolution().y,
            tsdfAtlas.GetVolumeResolution().z, 0));
        rayMarchMaterial.SetFloat("_VoxelSize", tsdfAtlas.GetVoxelSize());
        rayMarchMaterial.SetInt("_SlicesPerRow", tsdfAtlas.GetSlicesPerRow());
        rayMarchMaterial.SetFloat("_MinWeight", minWeight);
        rayMarchMaterial.SetFloat("_StepSize", stepSize);
        rayMarchMaterial.SetInt("_MaxSteps", maxSteps);
        rayMarchMaterial.SetMatrix("_InvView", cam.cameraToWorldMatrix);
        rayMarchMaterial.SetMatrix("_InvProj", cam.projectionMatrix.inverse);
        
        // Rebuild command buffer
        commandBuffer.Clear();
        commandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, rayMarchMaterial);
    }

    void OnDestroy()
    {
        if (commandBuffer != null)
        {
            cam?.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, commandBuffer);
            commandBuffer.Release();
        }
        
        if (rayMarchMaterial != null)
            Destroy(rayMarchMaterial);
    }
}
