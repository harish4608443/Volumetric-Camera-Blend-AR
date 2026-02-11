using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.Barracuda;
using NN;
using System.Collections.Generic;

/// <summary>
/// Real-time YOLO object detection integrated with AR Foundation.
/// Attach this to AR Camera alongside SimpleIncompleteOverlay.
/// Processes AR camera frames and draws bounding boxes on detected objects.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ARYOLODetector : MonoBehaviour
{
    [Header("YOLO Model")]
    [Tooltip("YOLOv8 ONNX model file")]
    public NNModel modelFile;
    
    [Header("Detection Settings")]
    [Range(0.0f, 1f)]
    [Tooltip("Minimum confidence threshold for detections")]
    public float minConfidence = 0.5f;
    
    [Tooltip("Detection interval in frames (higher = better performance)")]
    public int detectionInterval = 10;
    
    [Tooltip("Enable to draw bounding boxes on detected objects")]
    public bool drawBoundingBoxes = true;
    
    [Header("Visualization")]
    public Color[] boxColors = new Color[] { 
        Color.red, Color.green, Color.blue, 
        Color.cyan, Color.magenta, Color.yellow 
    };
    
    [Range(1, 10)]
    public int boxThickness = 2;
    
    private NNHandler nn;
    private YOLOv8 yolo;
    private ARCameraManager cameraManager;
    private List<ResultBox> lastDetections = new List<ResultBox>();
    private int frameCounter = 0;
    private Texture2D detectionTexture;
    private RenderTexture overlayTexture;
    
    void Start()
    {
        if (modelFile == null)
        {
            Debug.LogError("ARYOLODetector: YOLO model not assigned!");
            enabled = false;
            return;
        }
        
        // Initialize YOLO
        nn = new NNHandler(modelFile);
        yolo = new YOLOv8(nn);
        YOLOv8OutputReader.DiscardThreshold = minConfidence;
        
        // Get AR camera manager
        cameraManager = FindObjectOfType<ARCameraManager>();
        if (cameraManager == null)
        {
            Debug.LogError("ARYOLODetector: ARCameraManager not found!");
            enabled = false;
            return;
        }
        
        // Create detection texture (640x640 for YOLO input)
        detectionTexture = new Texture2D(640, 640, TextureFormat.RGB24, false);
        
        Debug.Log("ARYOLODetector initialized successfully!");
    }
    
    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        // Run detection every N frames
        frameCounter++;
        if (frameCounter >= detectionInterval)
        {
            frameCounter = 0;
            RunDetection(src);
        }
        
        // Draw bounding boxes on output
        if (drawBoundingBoxes && lastDetections.Count > 0)
        {
            RenderTexture temp = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
            Graphics.Blit(src, temp);
            
            // Convert to Texture2D to draw boxes
            RenderTexture.active = temp;
            Texture2D displayTexture = new Texture2D(src.width, src.height, TextureFormat.RGB24, false);
            displayTexture.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            displayTexture.Apply();
            RenderTexture.active = null;
            
            // Draw boxes
            DrawBoundingBoxes(displayTexture);
            
            // Blit back to destination
            Graphics.Blit(displayTexture, dest);
            
            RenderTexture.ReleaseTemporary(temp);
            Destroy(displayTexture);
        }
        else
        {
            Graphics.Blit(src, dest);
        }
    }
    
    void RunDetection(RenderTexture src)
    {
        // Resize AR camera frame to 640x640 for YOLO
        TextureTools.ResizeAndCropToCenter(src, ref detectionTexture, 640, 640);
        
        // Run YOLO inference
        lastDetections = yolo.Run(detectionTexture);
        
        if (lastDetections.Count > 0)
        {
            Debug.Log($"ARYOLODetector: Detected {lastDetections.Count} objects");
        }
    }
    
    void DrawBoundingBoxes(Texture2D texture)
    {
        foreach (var box in lastDetections)
        {
            Color boxColor = boxColors[box.bestClassIndex % boxColors.Length];
            
            // Scale box coordinates from 640x640 to actual texture size
            Rect scaledRect = new Rect(
                box.rect.x * texture.width / 640f,
                box.rect.y * texture.height / 640f,
                box.rect.width * texture.width / 640f,
                box.rect.height * texture.height / 640f
            );
            
            TextureTools.DrawRectOutline(texture, scaledRect, boxColor, boxThickness, 
                                        rectIsNormalized: false, revertY: true);
        }
    }
    
    void OnDestroy()
    {
        nn?.Dispose();
        if (detectionTexture != null)
            Destroy(detectionTexture);
        if (overlayTexture != null)
            overlayTexture.Release();
    }
    
    /// <summary>
    /// Get the most recent detection results
    /// </summary>
    public List<ResultBox> GetLastDetections()
    {
        return lastDetections;
    }
    
    /// <summary>
    /// Update confidence threshold at runtime
    /// </summary>
    public void SetConfidenceThreshold(float threshold)
    {
        minConfidence = Mathf.Clamp01(threshold);
        YOLOv8OutputReader.DiscardThreshold = minConfidence;
    }
}
