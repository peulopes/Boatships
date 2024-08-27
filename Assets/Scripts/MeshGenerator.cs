using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class MeshGenerator : MonoBehaviour
{
    public int width = 10;        // Width of the grid
    public int height = 10;       // Height of the grid
    public float spacing = 1f;    // Distance between vertices

    void Start()
    {
        GenerateMesh();
    }

    void GenerateMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        Mesh mesh = meshFilter.mesh;

        // Clear existing mesh data
        if (mesh != null)
        {
            mesh.Clear();
        }
        else
        {
            mesh = new Mesh();
        }

        meshFilter.mesh = mesh;

        int numVertices = (width + 1) * (height + 1);
        Vector3[] vertices = new Vector3[numVertices];
        int[] triangles = new int[width * height * 6];  // Each square is made of 2 triangles
        Vector2[] uv = new Vector2[numVertices];

        // Generate vertices
        for (int y = 0; y <= height; y++)
        {
            for (int x = 0; x <= width; x++)
            {
                int index = x + y * (width + 1);
                vertices[index] = new Vector3(x * spacing, 0, y * spacing);
                uv[index] = new Vector2((float)x / width, (float)y / height);
            }
        }

        // Generate triangles
        int triIndex = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int start = x + y * (width + 1);
                int a = start;
                int b = start + width + 1;
                int c = start + 1;
                int d = start + width + 2;

                // Two triangles per square
                triangles[triIndex] = a;
                triangles[triIndex + 1] = b;
                triangles[triIndex + 2] = c;
                triangles[triIndex + 3] = c;
                triangles[triIndex + 4] = b;
                triangles[triIndex + 5] = d;

                triIndex += 6; // Move to the next set of triangle indices
            }
        }

        // Assign data to the mesh
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Debugging output
        Debug.Log($"Vertices count: {vertices.Length}");
        Debug.Log($"Triangles count: {triangles.Length}");
    }
}
