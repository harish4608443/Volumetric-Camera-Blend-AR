using UnityEngine;
using System.Text;

/// <summary>
/// Diagnostic tool to check if ARCombinedOverlay and sphere detection is working.
/// Attach this to AR Camera alongside ARCombinedOverlay.
/// </summary>
public class DetectionDiagnostics : MonoBehaviour
{
    private ARCombinedOverlay overlay;
    private DetectionSphereManager sphereManager;
    private float lastCheckTime = 0f;
    private int frameCount = 0;
    
    void Start()
    {
        overlay = GetComponent<ARCombinedOverlay>();
        sphereManager = FindObjectOfType<DetectionSphereManager>();
        
        Debug.Log("========== DETECTION DIAGNOSTICS START ==========");
        
        // Check ARCombinedOverlay
        if (overlay == null)
        {
            Debug.LogError("❌ ARCombinedOverlay NOT FOUND on this GameObject!");
            return;
        }
        
        Debug.Log("✅ ARCombinedOverlay found");
        
        // Check settings
        Debug.Log($"Settings Check:");
        Debug.Log($"  - enableObjectDetection: {overlay.enableObjectDetection}");
        Debug.Log($"  - enableSpheres: {overlay.enableSpheres}");
        Debug.Log($"  - enableStripes: {overlay.enableStripes}");
        Debug.Log($"  - yoloModel assigned: {overlay.yoloModel != null}");
        Debug.Log($"  - minConfidence: {overlay.minConfidence}");
        Debug.Log($"  - detectionInterval: {overlay.detectionInterval}");
        
        if (overlay.yoloModel == null)
        {
            Debug.LogError("❌ YOLO MODEL NOT ASSIGNED! Select AR Camera, find ARCombinedOverlay component, assign yolov8s-seg.onnx to 'Yolo Model' field");
        }
        
        // Check SphereManager
        if (sphereManager == null)
        {
            Debug.LogError("❌ DetectionSphereManager NOT FOUND in scene!");
        }
        else
        {
            Debug.Log($"✅ DetectionSphereManager found");
            Debug.Log($"  - enableSpheres: {sphereManager.enableSpheres}");
            Debug.Log($"  - useDepthFallback: {sphereManager.useDepthFallback}");
            Debug.Log($"  - spherePrefab: {(sphereManager.spherePrefab != null ? "assigned" : "null (will use primitive)")}");
        }
        
        // Check Camera
        Camera cam = GetComponent<Camera>();
        if (cam != null)
        {
            Debug.Log($"✅ Camera: {cam.pixelWidth}x{cam.pixelHeight}, FOV: {cam.fieldOfView}");
        }
        
        // Check AR Foundation
        var arCameraManager = FindObjectOfType<UnityEngine.XR.ARFoundation.ARCameraManager>();
        if (arCameraManager == null)
        {
            Debug.LogError("❌ ARCameraManager NOT FOUND!");
        }
        else
        {
            Debug.Log($"✅ ARCameraManager found");
        }
        
        var occlusionManager = FindObjectOfType<UnityEngine.XR.ARFoundation.AROcclusionManager>();
        if (occlusionManager == null)
        {
            Debug.LogWarning("⚠️ AROcclusionManager NOT FOUND - depth may not work");
        }
        else
        {
            Debug.Log($"✅ AROcclusionManager found");
            Debug.Log($"  - requestedEnvironmentDepthMode: {occlusionManager.requestedEnvironmentDepthMode}");
        }
        
        Debug.Log("========== DIAGNOSTICS COMPLETE ==========");
        Debug.Log("Watch for these logs during runtime:");
        Debug.Log("  - '🔍 YOLO returned X raw detections' - detection running");
        Debug.Log("  - '📊 After filtering: X/Y valid detections' - filter results");
        Debug.Log("  - '🎯 Creating spheres for X fresh detections' - sphere creation");
    }
    
    void Update()
    {
        frameCount++;
        
        // Status update every 3 seconds
        if (Time.time - lastCheckTime > 3f)
        {
            lastCheckTime = Time.time;
            
            StringBuilder status = new StringBuilder();
            status.AppendLine($"\n========== STATUS UPDATE (Frame {frameCount}) ==========");
            
            if (overlay != null)
            {
                status.AppendLine($"ARCombinedOverlay: Active");
                status.AppendLine($"  Detection enabled: {overlay.enableObjectDetection}");
                status.AppendLine($"  Spheres enabled: {overlay.enableSpheres}");
            }
            
            if (sphereManager != null)
            {
                // Use reflection to access private activeSpheres count
                var activeSpheresField = sphereManager.GetType().GetField("activeSpheres", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (activeSpheresField != null)
                {
                    var activeSpheres = activeSpheresField.GetValue(sphereManager) as System.Collections.IList;
                    if (activeSpheres != null)
                    {
                        status.AppendLine($"Active spheres in scene: {activeSpheres.Count}");
                    }
                }
            }
            
            status.AppendLine("=================================================");
            Debug.Log(status.ToString());
        }
    }
}
