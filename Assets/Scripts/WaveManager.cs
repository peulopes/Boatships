using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveManager : MonoBehaviour
{
    public static WaveManager instance;

    [System.Serializable]
    public struct Wave
    {
        public float amplitude;     // Height of the wave
        public float wavelength;    // Wavelength (distance between peaks)
        public float speed;         // Speed of wave propagation
        public Vector2 direction;   // Direction of the wave (x, z)
    }

    public Wave[] waves;            // Array of wave structures
    public MeshFilter waterMeshFilter; // Reference to the MeshFilter component of the water plane

    private Vector3[] baseVertices;  // Original vertices of the water plane

    private void Awake()
    {
        // Singleton pattern
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Debug.Log("Instance already exists, destroying object!");
            Destroy(this);
        }

        // Get the original vertices of the mesh
        baseVertices = waterMeshFilter.mesh.vertices;
    }

    private void Update()
    {
        // Update the water mesh to reflect wave changes
        UpdateWaterMesh();
    }

    private void UpdateWaterMesh()
    {
        Mesh mesh = waterMeshFilter.mesh;
        Vector3[] vertices = new Vector3[baseVertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = waterMeshFilter.transform.TransformPoint(baseVertices[i]);
            Vector3 displacement;
            float waveHeight = GetWaveHeight(worldPos.x, worldPos.z, out displacement);
            vertices[i] = baseVertices[i] + new Vector3(displacement.x, waveHeight, displacement.z);
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals(); // Update the normals for correct lighting
        mesh.RecalculateBounds();  // Recalculate bounds for culling
    }

    public float GetWaveHeight(float x, float z, out Vector3 displacement)
    {
        float totalHeight = 0f;
        displacement = Vector3.zero;

        foreach (Wave wave in waves)
        {
            float k = 2 * Mathf.PI / wave.wavelength;
            float phase = wave.speed * Time.time;
            Vector2 direction = wave.direction.normalized;

            float dotProduct = Vector2.Dot(direction, new Vector2(x, z));
            float waveHeight = wave.amplitude * Mathf.Sin(k * dotProduct + phase);

            totalHeight += waveHeight;

            float dx = direction.x * (wave.amplitude * Mathf.Cos(k * dotProduct + phase));
            float dz = direction.y * (wave.amplitude * Mathf.Cos(k * dotProduct + phase));
            displacement += new Vector3(dx, 0f, dz);
        }

        return totalHeight;
    }
}