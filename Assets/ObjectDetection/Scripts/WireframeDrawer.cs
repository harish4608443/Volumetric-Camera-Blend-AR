using UnityEngine;

/// <summary>
/// Draws wireframe edges of sphere mesh using GL lines.
/// Based on IntelliCap WireframeDrawer - only renders lines, no surfaces (prevents mirroring).
/// </summary>
public class WireframeDrawer : MonoBehaviour
{
    public Color wireframeColor = new Color(0.0f, 0.71f, 0.78f, 1.0f);  // IntelliCap cyan/turquoise #00B5C7, full opacity for GL lines
    public float lineWidth = 2.0f;  // Line thickness (may not work on all platforms)
    
    private Mesh mesh;
    private Material wireframeMaterial;
    private Camera arCam;

    void Start()
    {
        mesh = GetComponent<MeshFilter>()?.mesh;
        if (mesh == null)
        {
            Debug.LogWarning("WireframeDrawer: No MeshFilter found!");
            enabled = false;
            return;
        }

        arCam = Camera.main;
        if (arCam == null)
        {
            arCam = FindObjectOfType<Camera>();
        }

        // Create material for drawing wireframe lines - render on top of sphere
        wireframeMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
        wireframeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        wireframeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        wireframeMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        wireframeMaterial.SetInt("_ZWrite", 0);
        wireframeMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); // Always draw wireframe on top
        wireframeMaterial.renderQueue = 4000; // Render after transparent objects
        
        Debug.Log($"[WIREFRAME] Initialized with IntelliCap cyan: R={wireframeColor.r:F2}, G={wireframeColor.g:F2}, B={wireframeColor.b:F2}, A={wireframeColor.a:F2}");
    }

    /// <summary>
    /// Set wireframe color (cyan for individual, magenta for merged)
    /// </summary>
    public void SetColor(Color color)
    {
        wireframeColor = color;
        if (wireframeMaterial != null)
        {
            wireframeMaterial.SetColor("_Color", color);
            Debug.Log($"[WIREFRAME] Color updated: R={color.r:F2}, G={color.g:F2}, B={color.b:F2}, A={color.a:F2}");
        }
    }

    // Draw the wireframe during rendering
    void OnRenderObject()
    {
        if (mesh == null || wireframeMaterial == null || arCam == null)
            return;

        // Ensure material has current color before rendering
        wireframeMaterial.SetColor("_Color", wireframeColor);
        
        // Debug log every 3 seconds to verify color
        if (Time.frameCount % 180 == 0)
        {
            Debug.Log($"[WIREFRAME RENDER] Drawing with color R={wireframeColor.r:F3}, G={wireframeColor.g:F3}, B={wireframeColor.b:F3}, A={wireframeColor.a:F3}");
        }
        
        GL.PushMatrix();
        GL.LoadProjectionMatrix(arCam.projectionMatrix);
        GL.modelview = arCam.worldToCameraMatrix * transform.localToWorldMatrix;
        
        // Activate material and begin line drawing with IntelliCap cyan
        wireframeMaterial.SetPass(0);
        GL.Begin(GL.LINES);
        GL.Color(wireframeColor); // Set vertex color for all lines

        Vector3 cameraPosition = arCam.transform.position;

        // Draw each triangle's edges
        for (int i = 0; i < mesh.triangles.Length; i += 3)
        {
            int index0 = mesh.triangles[i];
            int index1 = mesh.triangles[i + 1];
            int index2 = mesh.triangles[i + 2];

            // Use raw mesh vertices - GL.modelview handles the transformation to world space
            Vector3 v0 = mesh.vertices[index0];
            Vector3 v1 = mesh.vertices[index1];
            Vector3 v2 = mesh.vertices[index2];

            // Calculate the normal vector in local space
            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            
            // Transform vertices to world space for camera facing check
            Vector3 v0World = transform.TransformPoint(v0);
            Vector3 cameraToV0 = (v0World - arCam.transform.position).normalized;
            Vector3 normalWorld = transform.TransformDirection(normal);
            float dotProduct = Vector3.Dot(cameraToV0, normalWorld);

            if (dotProduct < 0)  // Draw only faces facing the camera
            {
                // Draw triangle edges - use local space vertices, GL matrix handles transformation
                GL.Vertex(v0);
                GL.Vertex(v1);
                GL.Vertex(v1);
                GL.Vertex(v2);
                GL.Vertex(v2);
                GL.Vertex(v0);
            }
        }

        GL.End();
        GL.PopMatrix();
    }

    void OnDestroy()
    {
        if (wireframeMaterial != null)
        {
            Destroy(wireframeMaterial);
        }
    }
}
