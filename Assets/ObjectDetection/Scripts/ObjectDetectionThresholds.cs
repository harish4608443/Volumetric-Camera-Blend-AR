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

    // SELECTIVE whitelist - focus on good AR targets (balanced: not too strict, not too loose)
    private static readonly Dictionary<string, ObjectThreshold> thresholds = new Dictionary<string, ObjectThreshold>()
    {
        // TOP PRIORITY: Small handheld objects (most common AR targets)
        { "bottle",      new ObjectThreshold(0.8f, 0.7f, 0.85f, 0.75f, 0.6f) },  // Primary target
        { "wine glass",  new ObjectThreshold(0.85f, 0.5f, 0.8f, 0.85f, 0.85f) }, // Very distinctive
        { "cup",         new ObjectThreshold(0.8f, 0.7f, 0.8f, 0.7f, 0.4f) },     // Common household
        { "bowl",        new ObjectThreshold(0.75f, 0.6f, 0.75f, 0.6f, 0.3f) },   // Added back - common
        
        // HIGH PRIORITY: Electronics (clear shapes, high-value targets)
        { "cell phone",  new ObjectThreshold(0.85f, 0.8f, 0.85f, 0.7f, 0.0f) },  // Very distinctive
        { "laptop",      new ObjectThreshold(0.85f, 0.8f, 0.9f, 0.7f, 0.0f) },   // Large, clear
        { "mouse",       new ObjectThreshold(0.75f, 0.65f, 0.75f, 0.6f, 0.0f) },  // Added back - common
        { "keyboard",    new ObjectThreshold(0.85f, 0.8f, 0.85f, 0.5f, 0.0f) },  // Added back - distinctive keys
        { "remote",      new ObjectThreshold(0.85f, 0.75f, 0.8f, 0.5f, 0.0f) },  // Buttons pattern
        
        // MEDIUM PRIORITY: Utensils (very distinctive shapes)
        { "fork",        new ObjectThreshold(0.95f, 0.4f, 0.7f, 0.8f, 0.0f) },   // Unique prongs
        { "knife",       new ObjectThreshold(0.9f, 0.4f, 0.75f, 0.85f, 0.0f) },  // Blade
        { "spoon",       new ObjectThreshold(0.85f, 0.4f, 0.7f, 0.8f, 0.0f) },   // Clear shape
        
        // MEDIUM PRIORITY: Personal items
        { "backpack",    new ObjectThreshold(0.75f, 0.75f, 0.8f, 0.4f, 0.0f) },  // Added back - texture
        { "handbag",     new ObjectThreshold(0.75f, 0.8f, 0.75f, 0.5f, 0.0f) },  // Added back - texture
        
        // MEDIUM PRIORITY: Other clear objects
        { "vase",        new ObjectThreshold(0.9f, 0.6f, 0.8f, 0.7f, 0.4f) },    // Unique shapes
        { "book",        new ObjectThreshold(0.85f, 0.75f, 0.8f, 0.4f, 0.0f) },  // Rectangular
        { "clock",       new ObjectThreshold(0.85f, 0.75f, 0.75f, 0.6f, 0.0f) }, // Added back - face pattern
        
        // MEDIUM PRIORITY: Sports (distinctive shapes)
        { "sports ball", new ObjectThreshold(0.95f, 0.65f, 0.8f, 0.5f, 0.0f) },  // Added back - spherical
        { "frisbee",     new ObjectThreshold(0.9f, 0.6f, 0.75f, 0.5f, 0.0f) },   // Added back - disc
        
        // NOTE: Still excluded furniture (too large), large appliances (not handheld)
    };

    // BALANCED strict minimum scores - require quality but not perfection
    private static readonly ObjectThreshold minimumThresholds = new ObjectThreshold(
        geometric: 0.75f,   // 75% geometric complexity (balanced from 80%)
        texture: 0.7f,      // 70% texture variance (balanced from 75%)
        size: 0.7f,         // 70% size match (balanced from 75%)
        specular: 0.7f,     // 70% specularity (balanced from 75%)
        transparent: 0.5f   // 50% transparency (balanced from 60%)
    );
    
    // BALANCED: Require 2 out of 5 criteria (reduced from 3 - was too strict)
    private const int MIN_CRITERIA_REQUIRED = 2;
    
    // BALANCED: Minimum total score 2.5 (reduced from 3.0) - good quality objects
    private const float MIN_TOTAL_SCORE = 2.5f;

    /// <summary>
    /// Calculate total IntelliCap score for an object (higher = better for detection).
    /// Used for competitive selection when multiple objects are nearby.
    /// </summary>
    /// <param name="className">The detected object class name</param>
    /// <param name="confidence">Detection confidence score (0-1)</param>
    /// <returns>Total score (0-5), or -1 if below minimum thresholds</returns>
    public static float GetObjectScore(string className, float confidence)
    {
        // STRICT: Reject ALL unknown objects (not in whitelist)
        if (!thresholds.ContainsKey(className))
        {
            Debug.Log($"[THRESHOLD] ❌ {className} REJECTED - not in whitelist (unknown object)");
            return -1f; // Unknown objects automatically fail
        }

        ObjectThreshold objThreshold = thresholds[className];

        // STRICT: Count how many criteria pass (requires MULTIPLE, not just one)
        bool geometric = objThreshold.Geometric >= minimumThresholds.Geometric;
        bool texture = objThreshold.Texture >= minimumThresholds.Texture;
        bool size = objThreshold.Size >= minimumThresholds.Size;
        bool specular = objThreshold.Specular >= minimumThresholds.Specular;
        bool transparent = objThreshold.Transparent >= minimumThresholds.Transparent;

        int passedCriteria = (geometric ? 1 : 0) + (texture ? 1 : 0) + (size ? 1 : 0) + 
                             (specular ? 1 : 0) + (transparent ? 1 : 0);

        if (passedCriteria < MIN_CRITERIA_REQUIRED)
        {
            Debug.Log($"[THRESHOLD] ❌ {className} REJECTED - only {passedCriteria}/{MIN_CRITERIA_REQUIRED} criteria passed " +
                      $"(geo:{objThreshold.Geometric:F2}{(geometric ? "✓" : "✗")}, " +
                      $"tex:{objThreshold.Texture:F2}{(texture ? "✓" : "✗")}, " +
                      $"size:{objThreshold.Size:F2}{(size ? "✓" : "✗")}, " +
                      $"spec:{objThreshold.Specular:F2}{(specular ? "✓" : "✗")}, " +
                      $"trans:{objThreshold.Transparent:F2}{(transparent ? "✓" : "✗")})");
            return -1f; // Requires at least 2 criteria
        }

        // Calculate total score (sum of all properties, weighted by confidence)
        float totalScore = (objThreshold.Geometric + objThreshold.Texture + objThreshold.Size + 
                           objThreshold.Specular + objThreshold.Transparent) * confidence;

        // STRICT: Require minimum total score
        if (totalScore < MIN_TOTAL_SCORE)
        {
            Debug.Log($"[THRESHOLD] ❌ {className} REJECTED - total score {totalScore:F2} < minimum {MIN_TOTAL_SCORE:F2}");
            return -1f;
        }

        Debug.Log($"[THRESHOLD] ✅ {className} PASSED: score={totalScore:F2} ({passedCriteria}/5 criteria) " +
                  $"(geo:{objThreshold.Geometric:F2}{(geometric ? "✓" : "✗")}, " +
                  $"tex:{objThreshold.Texture:F2}{(texture ? "✓" : "✗")}, " +
                  $"size:{objThreshold.Size:F2}{(size ? "✓" : "✗")}, " +
                  $"spec:{objThreshold.Specular:F2}{(specular ? "✓" : "✗")}, " +
                  $"trans:{objThreshold.Transparent:F2}{(transparent ? "✓" : "✗")}) × conf:{confidence:F2}");

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
