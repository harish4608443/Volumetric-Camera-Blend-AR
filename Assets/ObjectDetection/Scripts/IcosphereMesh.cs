using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates a low-poly icosphere mesh (geodesic sphere) matching IntelliCap's hexagonal appearance.
/// Creates a sphere from triangular faces that create hexagonal/pentagonal patterns.
/// </summary>
public class IcosphereMesh : MonoBehaviour
{
    /// <summary>
    /// Create a low-poly icosphere mesh GameObject.
    /// subdivisions = 0: 20 faces (very faceted, hexagonal look)
    /// subdivisions = 1: 80 faces (still faceted but smoother)
    /// subdivisions = 2: 320 faces (balanced)
    /// </summary>
    public static GameObject CreateIcosphere(int subdivisions = 1, float radius = 0.5f)
    {
        GameObject sphere = new GameObject("Icosphere");
        MeshFilter meshFilter = sphere.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = sphere.AddComponent<MeshRenderer>();
        
        meshFilter.mesh = GenerateIcosphereMesh(subdivisions, radius);
        
        return sphere;
    }
    
    private static Mesh GenerateIcosphereMesh(int subdivisions, float radius)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Icosphere";
        
        // Golden ratio for icosahedron
        float t = (1.0f + Mathf.Sqrt(5.0f)) / 2.0f;
        
        // Initial 12 vertices of icosahedron
        List<Vector3> vertices = new List<Vector3>
        {
            new Vector3(-1,  t,  0).normalized * radius,
            new Vector3( 1,  t,  0).normalized * radius,
            new Vector3(-1, -t,  0).normalized * radius,
            new Vector3( 1, -t,  0).normalized * radius,
            
            new Vector3( 0, -1,  t).normalized * radius,
            new Vector3( 0,  1,  t).normalized * radius,
            new Vector3( 0, -1, -t).normalized * radius,
            new Vector3( 0,  1, -t).normalized * radius,
            
            new Vector3( t,  0, -1).normalized * radius,
            new Vector3( t,  0,  1).normalized * radius,
            new Vector3(-t,  0, -1).normalized * radius,
            new Vector3(-t,  0,  1).normalized * radius
        };
        
        // Initial 20 triangular faces
        List<int> triangles = new List<int>
        {
            0, 11, 5,   0, 5, 1,    0, 1, 7,    0, 7, 10,   0, 10, 11,
            1, 5, 9,    5, 11, 4,   11, 10, 2,  10, 7, 6,   7, 1, 8,
            3, 9, 4,    3, 4, 2,    3, 2, 6,    3, 6, 8,    3, 8, 9,
            4, 9, 5,    2, 4, 11,   6, 2, 10,   8, 6, 7,    9, 8, 1
        };
        
        // Subdivide to increase polygon count
        for (int i = 0; i < subdivisions; i++)
        {
            List<int> newTriangles = new List<int>();
            Dictionary<long, int> midpointCache = new Dictionary<long, int>();
            
            for (int j = 0; j < triangles.Count; j += 3)
            {
                int v0 = triangles[j];
                int v1 = triangles[j + 1];
                int v2 = triangles[j + 2];
                
                // Get midpoint vertices
                int m01 = GetMidpoint(v0, v1, vertices, midpointCache, radius);
                int m12 = GetMidpoint(v1, v2, vertices, midpointCache, radius);
                int m20 = GetMidpoint(v2, v0, vertices, midpointCache, radius);
                
                // Create 4 new triangles from the original
                newTriangles.Add(v0);  newTriangles.Add(m01); newTriangles.Add(m20);
                newTriangles.Add(v1);  newTriangles.Add(m12); newTriangles.Add(m01);
                newTriangles.Add(v2);  newTriangles.Add(m20); newTriangles.Add(m12);
                newTriangles.Add(m01); newTriangles.Add(m12); newTriangles.Add(m20);
            }
            
            triangles = newTriangles;
        }
        
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    private static int GetMidpoint(int v0, int v1, List<Vector3> vertices, Dictionary<long, int> cache, float radius)
    {
        // Create unique key for vertex pair
        long key = ((long)Mathf.Min(v0, v1) << 32) | (uint)Mathf.Max(v0, v1);
        
        if (cache.ContainsKey(key))
        {
            return cache[key];
        }
        
        // Calculate midpoint and project to sphere surface
        Vector3 midpoint = ((vertices[v0] + vertices[v1]) / 2.0f).normalized * radius;
        int index = vertices.Count;
        vertices.Add(midpoint);
        cache[key] = index;
        
        return index;
    }
}
