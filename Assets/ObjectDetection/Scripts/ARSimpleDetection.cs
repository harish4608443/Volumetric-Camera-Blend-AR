using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.Barracuda;
using NN;

/// <summary>
/// Simple AR object detection component without point cloud visualization.
/// Runs YOLO detection on camera frames and draws bounding boxes directly on screen.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ARSimpleDetection : MonoBehaviour
{
    [Header("AR Components")]
    public ARCameraManager cameraManager;
    
    [Header("Detection Settings")]
    public NNModel modelAsset;
    public int detectionInterval = 1; // Every frame (1 = every frame, 5 = every 5th frame)
    public float minConfidence = 0.2f;
    
    private NNHandler nn;
    private YOLOv8 yolo;
    
    [Header("Visualization Settings")]
    public int boxThickness = 12;
    public float fadeAfterSeconds = 0.5f;
    public float fadeDuration = 0.5f;
    public int minBoxSize = 15;
    
    private int frameCounter = 0;
    private Texture2D detectionTexture;
    private List<DetectionWithTime> timedDetections = new List<DetectionWithTime>();
    private float cropScaleRatio = 1f;
    private float cropOffsetX = 0f;
    private float cropOffsetY = 0f;
    
    private class DetectionWithTime
    {
        public ResultBox box;
        public float timestamp;
        
        public DetectionWithTime(ResultBox box, float timestamp)
        {
            this.box = box;
            this.timestamp = timestamp;
        }
    }
    
    void Start()
    {
        if (cameraManager == null)
        {
            cameraManager = FindObjectOfType<ARCameraManager>();
        }
        
        if (modelAsset == null)
        {
            Debug.LogError("Model Asset is missing! Please assign yolov8s-seg.onnx");
            return;
        }
        
        // Initialize YOLO with the model
        nn = new NNHandler(modelAsset);
        yolo = new YOLOv8(nn);
        YOLOv8OutputReader.DiscardThreshold = minConfidence;
    }
    
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // Copy source to destination
        Graphics.Blit(source, destination);
        
        // Run detection at specified interval
        frameCounter++;
        if (frameCounter >= detectionInterval)
        {
            frameCounter = 0;
            RunDetection(source);
        }
        
        // Draw detections with fade effect
        DrawDetections(destination);
    }
    
    private void RunDetection(RenderTexture source)
    {
        if (yolo == null) return;
        
        try
        {
            Debug.Log($"RunDetection: Input size {source.width}x{source.height}, format {source.format}");
            
            // Calculate crop parameters for coordinate mapping
            float widthRatio = 640f / source.width;
            float heightRatio = 640f / source.height;
            cropScaleRatio = Mathf.Max(widthRatio, heightRatio);
            
            int scaledWidth = Mathf.CeilToInt(source.width * cropScaleRatio);
            int scaledHeight = Mathf.CeilToInt(source.height * cropScaleRatio);
            
            cropOffsetX = (scaledWidth - 640) / 2f;
            cropOffsetY = (scaledHeight - 640) / 2f;
            
            Debug.Log($"Crop params: scale={cropScaleRatio:F3}, offset=({cropOffsetX:F1}, {cropOffsetY:F1})");
            
            // Use the EXACT same workflow as static image detection (DNN project)
            // 1. Resize and center-crop using TextureTools
            if (detectionTexture == null || detectionTexture.width != 640)
            {
                detectionTexture = new Texture2D(640, 640, TextureFormat.RGB24, false);
            }
            TextureTools.ResizeAndCropToCenter(source, ref detectionTexture, 640, 640);
            
            Debug.Log($"Tensor input texture: {detectionTexture.width}x{detectionTexture.height}, format {detectionTexture.format}");
            
            // 2. Run YOLO detection - Barracuda's Tensor(Texture2D) handles preprocessing
            var allDetections = yolo.Run(detectionTexture);
            
            // Log ALL raw detections
            foreach (var box in allDetections)
            {
                Debug.Log($"RAW: {GetClassName(box.bestClassIndex)} {(box.score * 100):F1}% at {box.rect} size:{box.rect.width}x{box.rect.height}");
            }
            
            // Filter out unreasonable detections
            List<ResultBox> validDetections = new List<ResultBox>();
            foreach (var box in allDetections)
            {
                // Skip boxes that are extremely large (full screen, likely errors)
                if (box.rect.width > 600 || box.rect.height > 600)
                {
                    Debug.Log($"FILTERED: Oversized {GetClassName(box.bestClassIndex)} {box.rect.width}x{box.rect.height}");
                    continue;
                }
                
                // Skip boxes at edges (likely partial detections cut off by crop)
                if (box.rect.x < 5 || box.rect.y < 5)
                {
                    Debug.Log($"FILTERED: Edge box {GetClassName(box.bestClassIndex)} at x:{box.rect.x} y:{box.rect.y}");
                    continue;
                }
                
                Debug.Log($"VALID: {GetClassName(box.bestClassIndex)}: {(box.score * 100):F1}% at {box.rect}");
                validDetections.Add(box);
            }
            
            // Update timed detections
            float currentTime = Time.time;
            if (validDetections.Count == 0)
            {
                if (timedDetections.Count > 0)
                {
                    Debug.Log("No detections - cleared all boxes instantly");
                }
                timedDetections.Clear();
            }
            else
            {
                timedDetections.Clear();
                foreach (var box in validDetections)
                {
                    timedDetections.Add(new DetectionWithTime(box, currentTime));
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Detection error: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private void DrawDetections(RenderTexture destination)
    {
        if (timedDetections.Count == 0) return;
        
        // Convert RenderTexture to Texture2D for drawing
        RenderTexture.active = destination;
        Texture2D displayTexture = new Texture2D(destination.width, destination.height, TextureFormat.RGB24, false);
        displayTexture.ReadPixels(new Rect(0, 0, destination.width, destination.height), 0, 0);
        displayTexture.Apply();
        RenderTexture.active = null;
        
        float currentTime = Time.time;
        
        foreach (var detection in timedDetections)
        {
            float age = currentTime - detection.timestamp;
            
            // Calculate alpha based on fade settings
            float alpha = 1.0f;
            if (age > fadeAfterSeconds)
            {
                float fadeProgress = (age - fadeAfterSeconds) / fadeDuration;
                alpha = Mathf.Clamp01(1.0f - fadeProgress);
            }
            
            if (alpha <= 0.01f) continue;
            
            ResultBox box = detection.box;
            
            // Transform coordinates from 640x640 crop space back to original resolution
            float scaledX = (box.rect.x - cropOffsetX) / cropScaleRatio;
            float scaledY = (box.rect.y - cropOffsetY) / cropScaleRatio;
            float scaledW = box.rect.width / cropScaleRatio;
            float scaledH = box.rect.height / cropScaleRatio;
            
            Rect scaledRect = new Rect(scaledX, scaledY, scaledW, scaledH);
            
            // Skip tiny boxes
            if (scaledRect.width < minBoxSize || scaledRect.height < minBoxSize) continue;
            
            // Draw bounding box with alpha blending
            Color boxColor = Color.green;
            boxColor.a = alpha;
            
            Debug.Log($"Drawing box: {GetClassName(box.bestClassIndex)} {(box.score*100):F1}% alpha:{alpha:F2} age:{age:F1}s at scaled rect: {scaledRect}");
            
            TextureTools.DrawRectOutline(displayTexture, scaledRect, boxColor, boxThickness, 
                                        rectIsNormalized: false, revertY: true);
            
            // Draw label with confidence
            string label = $"{GetClassName(box.bestClassIndex)} {(box.score * 100):F0}%";
            int labelX = (int)scaledRect.x + 5;
            int labelY = destination.height - (int)scaledRect.y - 15;
            
            Color textColor = new Color(1f, 1f, 1f, alpha);
            TextureTextDrawer.DrawText(displayTexture, label, labelX, labelY, textColor, 4);
        }
        
        // Blit the modified texture back to destination
        Graphics.Blit(displayTexture, destination);
        Destroy(displayTexture);
    }
    
    string GetClassName(int classIndex)
    {
        string[] cocoClasses = new string[] {
            "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
            "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog",
            "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella",
            "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball", "kite",
            "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket", "bottle",
            "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple", "sandwich",
            "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
            "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote",
            "keyboard", "cell phone", "microwave", "oven", "toaster", "sink", "refrigerator", "book",
            "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush"
        };
        
        if (classIndex >= 0 && classIndex < cocoClasses.Length)
            return cocoClasses[classIndex];
        return $"Class{classIndex}";
    }
    
    void OnDestroy()
    {
        nn?.Dispose();
        if (detectionTexture != null)
        {
            Destroy(detectionTexture);
        }
    }
}
