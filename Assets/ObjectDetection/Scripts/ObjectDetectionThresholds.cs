using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// IntelliCap-style thresholds for object detection filtering.
/// Each object class has predefined scores for geometric, texture, size, specular, and transparent properties.
/// Objects must meet at least one threshold criterion to pass filtering.
/// </summary>
public class ObjectDetectionThresholds
{
    // Threshold class to hold criteria scores
    public class ObjectThreshold
    {
        public float Geometric;    // Shape complexity, edges (0-1)
        public float Texture;      // Surface texture variance (0-1)
        public float Size;         // Expected size range (0-1)
        public float Specular;     // Reflectivity, highlights (0-1)
        public float Transparent;  // Transparency level (0-1)

        public ObjectThreshold(float geometric, float texture, float size, float specular, float transparent)
        {
            Geometric = geometric;
            Texture = texture;
            Size = size;
            Specular = specular;
            Transparent = transparent;
        }
    }

    // Minimum threshold values - objects must meet at least one to pass
    private static readonly Dictionary<string, ObjectThreshold> thresholds = new Dictionary<string, ObjectThreshold>()
    {
        // Kitchen & Dining Objects (high detection priority)
        { "bottle",      new ObjectThreshold(0.7f, 0.6f, 0.8f, 0.7f, 0.5f) },  // High geometric (cylindrical), medium specular, some transparency
        { "wine glass",  new ObjectThreshold(0.8f, 0.4f, 0.7f, 0.8f, 0.8f) },  // Very high transparent, high geometric (stem)
        { "cup",         new ObjectThreshold(0.7f, 0.6f, 0.75f, 0.6f, 0.3f) }, // High geometric (cylindrical), medium texture
        { "bowl",        new ObjectThreshold(0.75f, 0.5f, 0.7f, 0.5f, 0.2f) }, // High geometric (circular), medium size
        { "fork",        new ObjectThreshold(0.9f, 0.3f, 0.6f, 0.7f, 0.0f) },  // Very high geometric (distinct prongs), high specular
        { "knife",       new ObjectThreshold(0.85f, 0.3f, 0.65f, 0.8f, 0.0f) }, // High geometric (blade), very high specular
        { "spoon",       new ObjectThreshold(0.8f, 0.3f, 0.6f, 0.75f, 0.0f) }, // High geometric, high specular
        
        // Note: "mug" is not a COCO class - YOLO detects it as "cup". Left here for reference.
        // If using custom YOLO with mug class, use these scores (lower than bottle):
        // { "mug",      new ObjectThreshold(0.6f, 0.5f, 0.65f, 0.5f, 0.0f) }, // Medium scores - loses to bottle
        
        // Electronics (medium-high priority)
        { "cell phone",  new ObjectThreshold(0.8f, 0.7f, 0.8f, 0.6f, 0.0f) },  // High geometric (rectangular), high texture (screen)
        { "laptop",      new ObjectThreshold(0.8f, 0.75f, 0.85f, 0.6f, 0.0f) }, // High geometric, high texture
        { "mouse",       new ObjectThreshold(0.75f, 0.6f, 0.7f, 0.5f, 0.0f) }, // Medium-high geometric
        { "keyboard",    new ObjectThreshold(0.85f, 0.8f, 0.8f, 0.4f, 0.0f) }, // High geometric (keys pattern), high texture
        { "remote",      new ObjectThreshold(0.8f, 0.7f, 0.75f, 0.4f, 0.0f) }, // High geometric, high texture (buttons)
        
        // Furniture & Large Objects (medium priority - may not want spheres for these)
        { "chair",       new ObjectThreshold(0.5f, 0.4f, 0.6f, 0.2f, 0.0f) },  // Lower priority - too large
        { "couch",       new ObjectThreshold(0.4f, 0.5f, 0.5f, 0.2f, 0.0f) },  // Lower priority - too large
        { "dining table", new ObjectThreshold(0.5f, 0.4f, 0.6f, 0.3f, 0.0f) }, // Lower priority - too large
        { "bed",         new ObjectThreshold(0.4f, 0.5f, 0.5f, 0.2f, 0.0f) },  // Lower priority - too large
        
        // Personal Items (high priority)
        { "backpack",    new ObjectThreshold(0.7f, 0.7f, 0.75f, 0.3f, 0.0f) }, // High texture variety
        { "handbag",     new ObjectThreshold(0.7f, 0.75f, 0.7f, 0.4f, 0.0f) }, // High texture, medium geometric
        { "tie",         new ObjectThreshold(0.8f, 0.7f, 0.6f, 0.5f, 0.0f) },  // High geometric (elongated), high texture
        { "suitcase",    new ObjectThreshold(0.75f, 0.6f, 0.8f, 0.4f, 0.0f) }, // High geometric, good size
        
        // Sports & Recreation (medium priority)
        { "sports ball", new ObjectThreshold(0.9f, 0.6f, 0.75f, 0.4f, 0.0f) }, // Very high geometric (spherical)
        { "frisbee",     new ObjectThreshold(0.85f, 0.5f, 0.7f, 0.4f, 0.0f) },  // High geometric (circular disc)
        { "skateboard",  new ObjectThreshold(0.8f, 0.6f, 0.75f, 0.3f, 0.0f) },  // High geometric, medium texture
        
        // Books & Paper (medium priority)
        { "book",        new ObjectThreshold(0.8f, 0.7f, 0.7f, 0.3f, 0.0f) },  // High geometric (rectangular), high texture
        
        // Home Items (medium priority)
        { "vase",        new ObjectThreshold(0.85f, 0.5f, 0.75f, 0.6f, 0.3f) }, // High geometric (unique shapes), some transparency
        { "clock",       new ObjectThreshold(0.8f, 0.7f, 0.7f, 0.5f, 0.0f) },  // High geometric (circular/rectangular), high texture (face)
        { "potted plant", new ObjectThreshold(0.6f, 0.8f, 0.7f, 0.2f, 0.0f) }, // High texture variety (leaves)
        
        // Appliances (medium-high priority)
        { "microwave",   new ObjectThreshold(0.75f, 0.5f, 0.8f, 0.5f, 0.0f) }, // High geometric, good size
        { "oven",        new ObjectThreshold(0.7f, 0.5f, 0.8f, 0.5f, 0.0f) },  // Good geometric and size
        { "toaster",     new ObjectThreshold(0.8f, 0.5f, 0.75f, 0.6f, 0.0f) }, // High geometric, medium specular
    };

    // Minimum scores required to pass (objects must exceed at least ONE of these)
    private static readonly ObjectThreshold minimumThresholds = new ObjectThreshold(
        geometric: 0.6f,    // 60% geometric complexity
        texture: 0.6f,      // 60% texture variance
        size: 0.65f,        // 65% size match
        specular: 0.6f,     // 60% specularity
        transparent: 0.4f   // 40% transparency (lower because fewer objects are transparent)
    );

    /// <summary>
    /// Calculate total IntelliCap score for an object (higher = better for detection).
    /// Used for competitive selection when multiple objects are nearby.
    /// </summary>
    /// <param name="className">The detected object class name</param>
    /// <param name="confidence">Detection confidence score (0-1)</param>
    /// <returns>Total score (0-5), or -1 if below minimum thresholds</returns>
    public static float GetObjectScore(string className, float confidence)
    {
        // If object class not in threshold dictionary, use confidence-only
        if (!thresholds.ContainsKey(className))
        {
            Debug.Log($"[THRESHOLD] {className} not in config, score = confidence ({confidence:F2})");
            return confidence >= 0.6f ? confidence : -1f; // Return confidence as score if passes
        }

        ObjectThreshold objThreshold = thresholds[className];

        // Check if ANY threshold criterion is met (IntelliCap OR logic)
        bool geometric = objThreshold.Geometric >= minimumThresholds.Geometric;
        bool texture = objThreshold.Texture >= minimumThresholds.Texture;
        bool size = objThreshold.Size >= minimumThresholds.Size;
        bool specular = objThreshold.Specular >= minimumThresholds.Specular;
        bool transparent = objThreshold.Transparent >= minimumThresholds.Transparent;

        bool passes = geometric || texture || size || specular || transparent;

        if (!passes)
        {
            Debug.Log($"[THRESHOLD] ❌ {className} REJECTED - all scores below minimum");
            return -1f; // Failed threshold check
        }

        // Calculate total score (sum of all properties, weighted by confidence)
        float totalScore = (objThreshold.Geometric + objThreshold.Texture + objThreshold.Size + 
                           objThreshold.Specular + objThreshold.Transparent) * confidence;

        Debug.Log($"[THRESHOLD] {className} score={totalScore:F2} (geo:{objThreshold.Geometric:F2}, " +
                  $"tex:{objThreshold.Texture:F2}, size:{objThreshold.Size:F2}, " +
                  $"spec:{objThreshold.Specular:F2}, trans:{objThreshold.Transparent:F2}) × conf:{confidence:F2}");

        return totalScore;
    }

    /// <summary>
    /// Check if a detected object meets the IntelliCap thresholds.
    /// Returns true if the object class has high enough scores in at least one category.
    /// </summary>
    /// <param name="className">The detected object class name</param>
    /// <param name="confidence">Detection confidence score (0-1)</param>
    /// <returns>True if object passes threshold filtering</returns>
    public static bool PassesThreshold(string className, float confidence)
    {
        return GetObjectScore(className, confidence) >= 0f;
    }

    /// <summary>
    /// Get the threshold configuration for a specific object class.
    /// </summary>
    public static ObjectThreshold GetThreshold(string className)
    {
        if (thresholds.ContainsKey(className))
            return thresholds[className];
        return null;
    }

    /// <summary>
    /// Check if an object class is configured for detection.
    /// </summary>
    public static bool HasThreshold(string className)
    {
        return thresholds.ContainsKey(className);
    }
}
