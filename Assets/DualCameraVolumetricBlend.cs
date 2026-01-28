using UnityEngine;

/// <summary>
/// Manages two cameras: Main RGB camera and Volumetric camera
/// Blends them together using alpha blending
/// </summary>
[RequireComponent(typeof(Camera))]
public class DualCameraVolumetricBlend : MonoBehaviour
{
    [Header("Camera References")]
    [Tooltip("The main RGB camera (usually this component's camera)")]
    public Camera mainCamera;
    
    [Tooltip("The volumetric rendering camera")]
    public Camera volumetricCamera;

    [Header("Blend Settings")]
    [Range(0f, 1f)]
    [Tooltip("Alpha blend factor for volumetric layer (0 = invisible, 1 = fully visible)")]
    public float volumetricAlpha = 0.5f;

    [Header("Debug")]
    [Tooltip("Show only main camera (no volumetric)")]
    public bool debugShowMainOnly = false;
    
    [Tooltip("Show only volumetric camera")]
    public bool debugShowVolumetricOnly = false;

    [Header("Render Textures")]
    private RenderTexture volumetricCameraRT;
    
    [Header("Shader")]
    [Tooltip("Shader for alpha blending the two cameras")]
    public Shader blendShader;
    private Material blendMaterial;

    private void Start()
    {
        // Validate setup
        if (mainCamera == null)
        {
            mainCamera = GetComponent<Camera>();
        }

        if (volumetricCamera == null)
        {
            Debug.LogError("Volumetric Camera not assigned!");
            enabled = false;
            return;
        }

        if (blendShader == null)
        {
            Debug.LogError("Blend Shader not assigned!");
            enabled = false;
            return;
        }

        // Create blend material
        blendMaterial = new Material(blendShader);
        Debug.Log("DualCameraBlend: Blend material created successfully");

        // Setup render textures
        SetupRenderTextures();

        // Configure volumetric camera
        volumetricCamera.enabled = true;
        volumetricCamera.targetTexture = volumetricCameraRT;
        volumetricCamera.clearFlags = CameraClearFlags.SolidColor;
        volumetricCamera.backgroundColor = new Color(0, 0, 0, 0); // Force transparent background
        
        Debug.Log($"DualCameraBlend: Setup complete. RT size: {volumetricCameraRT.width}x{volumetricCameraRT.height}");
        Debug.Log($"Main Camera targetTexture: {mainCamera.targetTexture}");
        Debug.Log($"Main Camera enabled: {mainCamera.enabled}");
        Debug.Log($"Volumetric Camera background: {volumetricCamera.backgroundColor}");
        
        // Main camera renders to screen (no targetTexture needed)
    }

    private void SetupRenderTextures()
    {
        int width = Screen.width;
        int height = Screen.height;

        // Volumetric camera render texture (RGBA for alpha channel)
        volumetricCameraRT = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
        volumetricCameraRT.name = "VolumetricCameraRT";
        volumetricCameraRT.Create();
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (blendMaterial == null || volumetricCamera == null)
        {
            Debug.LogWarning("DualCameraBlend: Missing material or camera reference!");
            Graphics.Blit(source, destination);
            return;
        }

        // Debug modes
        if (debugShowMainOnly)
        {
            // Show only main camera RGB
            Graphics.Blit(source, destination);
            return;
        }
        
        if (debugShowVolumetricOnly)
        {
            // Show only volumetric camera
            Graphics.Blit(volumetricCameraRT, destination);
            return;
        }

        // Set shader properties
        // source = main camera's RGB output
        blendMaterial.SetTexture("_MainTex", source);
        blendMaterial.SetTexture("_VolumetricTex", volumetricCameraRT);
        blendMaterial.SetFloat("_VolumetricAlpha", volumetricAlpha);

        // Perform the blend: Blit(RGB, V, RGB+V)
        Graphics.Blit(source, destination, blendMaterial);
    }

    private void OnDestroy()
    {
        // Cleanup
        if (volumetricCameraRT != null)
        {
            volumetricCameraRT.Release();
            Destroy(volumetricCameraRT);
        }

        if (blendMaterial != null)
        {
            Destroy(blendMaterial);
        }
    }

    private void OnValidate()
    {
        // Update blend alpha in real-time while adjusting in inspector
        if (blendMaterial != null)
        {
            blendMaterial.SetFloat("_VolumetricAlpha", volumetricAlpha);
        }
    }

    // Handle screen resolution changes
    private void Update()
    {
        if (volumetricCameraRT != null && (volumetricCameraRT.width != Screen.width || volumetricCameraRT.height != Screen.height))
        {
            // Recreate render textures if screen size changed
            if (volumetricCameraRT != null)
            {
                volumetricCameraRT.Release();
                Destroy(volumetricCameraRT);
            }

            SetupRenderTextures();
            volumetricCamera.targetTexture = volumetricCameraRT;
        }
    }
}
