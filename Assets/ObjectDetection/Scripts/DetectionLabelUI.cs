using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using NN;

/// <summary>
/// Displays YOLO detection labels as UI text overlays on the camera view.
/// Add this to a Canvas in your scene.
/// </summary>
public class DetectionLabelUI : MonoBehaviour
{
    public ARCombinedOverlay combinedOverlay;
    public GameObject labelPrefab; // Assign a Text UI prefab
    public int maxLabels = 20;
    
    private List<GameObject> labelPool = new List<GameObject>();
    private Camera arCamera;
    
    void Start()
    {
        arCamera = Camera.main;
        
        // Create label pool
        for (int i = 0; i < maxLabels; i++)
        {
            GameObject label = Instantiate(labelPrefab, transform);
            label.SetActive(false);
            labelPool.Add(label);
        }
    }
    
    void LateUpdate()
    {
        // Deactivate all labels
        foreach (var label in labelPool)
            label.SetActive(false);
        
        if (combinedOverlay == null)
            return;
            
        var detections = combinedOverlay.GetLastDetections();
        int labelIndex = 0;
        
        foreach (var box in detections)
        {
            if (labelIndex >= maxLabels)
                break;
                
            GameObject labelObj = labelPool[labelIndex++];
            labelObj.SetActive(true);
            
            Text text = labelObj.GetComponent<Text>();
            if (text != null)
            {
                string className = GetClassName(box.bestClassIndex);
                text.text = $"{className} {(box.score * 100):F0}%";
                
                // Position label at top-left of bounding box
                RectTransform rectTransform = labelObj.GetComponent<RectTransform>();
                Vector2 screenPos = new Vector2(
                    box.rect.x * Screen.width / 640f,
                    Screen.height - (box.rect.y * Screen.height / 640f)
                );
                rectTransform.anchoredPosition = screenPos;
            }
        }
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
}
