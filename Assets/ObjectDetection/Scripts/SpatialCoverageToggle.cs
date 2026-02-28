using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toggle spatial coverage visualization and object detection on/off via UI button.
/// Attach to AR Camera.
/// Uses MonoBehaviour references to avoid assembly definition issues.
/// </summary>
public class SpatialCoverageToggle : MonoBehaviour
{
    [Header("UI Reference")]
    [Tooltip("Assign the UI Button from the scene")]
    public Button toggleButton;
    
    [Header("Components to Toggle")]
    [Tooltip("Auto-found if not assigned - TSDFVolumeAtlas")]
    public MonoBehaviour tsdfVolume;
    
    [Tooltip("Auto-found if not assigned - TSDFReliableRenderer")]
    public MonoBehaviour tsdfRenderer;
    
    [Tooltip("Auto-found if not assigned - TSDFRayMarching")]
    public MonoBehaviour tsdfRayMarching;
    
    [Tooltip("Auto-found if not assigned - stripe overlay + object detection")]
    public ARCombinedOverlay arOverlay;
    
    [Header("Button Text")]
    public Text buttonText;
    
    [Header("State")]
    public bool spatialCoverageActive = false; // Start with spatial coverage OFF
    
    private string enableText = "Enable Spatial Coverage";
    private string disableText = "Disable Spatial Coverage";

    void Start()
    {
        Debug.Log("=== Spatial Coverage Toggle Starting ===");
        
        // Auto-find components if not assigned (use string-based search to avoid assembly issues)
        if (tsdfVolume == null)
        {
            tsdfVolume = FindComponentByTypeName("TSDFVolumeAtlas");
            Debug.Log($"[TOGGLE] Auto-found TSDFVolumeAtlas: {tsdfVolume != null}");
        }
        
        if (tsdfRenderer == null)
        {
            tsdfRenderer = FindComponentByTypeName("TSDFReliableRenderer");
            Debug.Log($"[TOGGLE] Auto-found TSDFReliableRenderer: {tsdfRenderer != null}");
        }
        
        if (tsdfRayMarching == null)
        {
            tsdfRayMarching = FindComponentByTypeName("TSDFRayMarching");
            Debug.Log($"[TOGGLE] Auto-found TSDFRayMarching: {tsdfRayMarching != null}");
        }
        
        if (arOverlay == null)
        {
            arOverlay = GetComponent<ARCombinedOverlay>();
            if (arOverlay == null)
            {
                arOverlay = FindObjectOfType<ARCombinedOverlay>();
            }
            Debug.Log($"[TOGGLE] Auto-found ARCombinedOverlay: {arOverlay != null}");
        }
        
        // Setup button
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleSpatialCoverage);
            Debug.Log("[TOGGLE] Button listener registered");
        }
        else
        {
            Debug.LogError("[TOGGLE] ❌ Toggle button not assigned! Assign in Inspector");
        }
        
        // Auto-find button text if not assigned
        if (buttonText == null && toggleButton != null)
        {
            buttonText = toggleButton.GetComponentInChildren<Text>();
        }
        
        // Initialize state - START WITH EVERYTHING DISABLED
        SetSpatialCoverageActive(false);
        UpdateButtonText();
        
        Debug.Log($"[TOGGLE] ✅ Initialization complete - spatial coverage starts DISABLED");
    }

    /// <summary>
    /// Toggle spatial coverage on/off
    /// </summary>
    public void ToggleSpatialCoverage()
    {
        spatialCoverageActive = !spatialCoverageActive;
        SetSpatialCoverageActive(spatialCoverageActive);
        UpdateButtonText();
        
        Debug.Log($"[TOGGLE] 🔘 Spatial coverage toggled: {(spatialCoverageActive ? "ON" : "OFF")}");
    }
    
    /// <summary>
    /// Enable or disable all spatial coverage components
    /// </summary>
    private void SetSpatialCoverageActive(bool active)
    {
        // Toggle TSDF volume integration
        if (tsdfVolume != null)
        {
            tsdfVolume.enabled = active;
            Debug.Log($"[TOGGLE] TSDFVolumeAtlas: {(active ? "ENABLED" : "DISABLED")}");
        }
        
        // Toggle TSDF rendering (whichever renderer is present)
        if (tsdfRenderer != null)
        {
            tsdfRenderer.enabled = active;
            
            // Try to set enableRendering property via reflection (TSDFReliableRenderer has this)
            var enableRenderingProp = tsdfRenderer.GetType().GetProperty("enableRendering");
            if (enableRenderingProp != null)
            {
                enableRenderingProp.SetValue(tsdfRenderer, active);
            }
            
            Debug.Log($"[TOGGLE] TSDFReliableRenderer: {(active ? "ENABLED" : "DISABLED")}");
        }
        
        if (tsdfRayMarching != null)
        {
            tsdfRayMarching.enabled = active;
            Debug.Log($"[TOGGLE] TSDFRayMarching: {(active ? "ENABLED" : "DISABLED")}");
        }
        
        // Toggle ARCombinedOverlay (stripes + object detection)
        if (arOverlay != null)
        {
            arOverlay.enabled = active;
            
            // Also control sub-features
            arOverlay.enableStripes = active;
            arOverlay.enableObjectDetection = active;
            
            Debug.Log($"[TOGGLE] ARCombinedOverlay: {(active ? "ENABLED" : "DISABLED")} (stripes + detection)");
        }
    }
    
    /// <summary>
    /// Update button text to show current state
    /// </summary>
    private void UpdateButtonText()
    {
        if (buttonText != null)
        {
            buttonText.text = spatialCoverageActive ? disableText : enableText;
        }
    }
    
    /// <summary>
    /// Public method to enable spatial coverage (for external scripts)
    /// </summary>
    public void EnableSpatialCoverage()
    {
        if (!spatialCoverageActive)
        {
            ToggleSpatialCoverage();
        }
    }
    
    /// <summary>
    /// Public method to disable spatial coverage (for external scripts)
    /// </summary>
    
    /// <summary>
    /// Find a component by type name (avoids assembly reference issues)
    /// </summary>
    private MonoBehaviour FindComponentByTypeName(string typeName)
    {
        MonoBehaviour[] allComponents = FindObjectsOfType<MonoBehaviour>();
        foreach (var mb in allComponents)
        {
            if (mb.GetType().Name == typeName)
            {
                return mb;
            }
        }
        return null;
    }
    public void DisableSpatialCoverage()
    {
        if (spatialCoverageActive)
        {
            ToggleSpatialCoverage();
        }
    }
}
