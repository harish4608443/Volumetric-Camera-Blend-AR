using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Generates spatial coverage mask based on AR mesh reconstruction (IntelliCap method).
/// Areas in reconstructed mesh → show camera feed (scanned).
/// Areas not in mesh → show pink-white stripes (unscanned).
/// </summary>
public class MeshCoverageMask
{
    private ARMeshManager meshManager;
    private Camera camera;
    private float maxRaycastDistance;
    
    public MeshCoverageMask(ARMeshManager meshMgr, Camera cam, float maxDist)
    {
        meshManager = meshMgr;
        camera = cam;
        maxRaycastDistance = maxDist;
    }
    
    /// <summary>
    /// Generate mask by raycasting from camera to check AR mesh coverage.
    /// Returns texture where white=reconstructed (show camera), black=unscanned (show stripes).
    /// </summary>
    public Texture2D GenerateMask(int width, int height)
    {
        Texture2D maskTex = new Texture2D(width, height, TextureFormat.R8, false);
        Color32[] pixels = new Color32[width * height];
        
        int reconstructedCount = 0;
        int unscannedCount = 0;
        
        // For each mask pixel, raycast to see if it hits the AR mesh
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                
                // Convert mask pixel to viewport coordinates (0-1)
                float u = (float)x / width;
                float v = (float)y / height;
                
                // Create ray from camera through this pixel
                Ray ray = camera.ViewportPointToRay(new Vector3(u, v, 0));
                
                // Check if ray hits AR mesh
                RaycastHit hit;
                bool hitMesh = Physics.Raycast(ray, out hit, maxRaycastDistance);
                
                if (hitMesh && hit.collider != null)
                {
                    // Check if hit object is AR mesh (has MeshFilter/MeshCollider from ARMeshManager)
                    MeshFilter meshFilter = hit.collider.GetComponent<MeshFilter>();
                    if (meshFilter != null)
                    {
                        // Hit reconstructed mesh - show camera feed
                        pixels[idx] = new Color32(255, 255, 255, 255);
                        reconstructedCount++;
                    }
                    else
                    {
                        // Hit non-mesh object - show stripes
                        pixels[idx] = new Color32(0, 0, 0, 255);
                        unscannedCount++;
                    }
                }
                else
                {
                    // No mesh hit - show stripes (unscanned)
                    pixels[idx] = new Color32(0, 0, 0, 255);
                    unscannedCount++;
                }
            }
        }
        
        int totalPixels = width * height;
        float meshCoverage = 100f * reconstructedCount / totalPixels;
        
        Debug.Log($"[MESH COVERAGE] Frame {Time.frameCount}: " +
                 $"Reconstructed mesh: {reconstructedCount}/{totalPixels} ({meshCoverage:F1}%), " +
                 $"Unscanned: {unscannedCount} | AR Mesh chunks: {(meshManager != null ? meshManager.meshes.Count : 0)}");
        
        maskTex.SetPixels32(pixels);
        maskTex.Apply();
        return maskTex;
    }
}
