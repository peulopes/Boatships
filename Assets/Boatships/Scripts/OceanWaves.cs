using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// First-prototype ocean: one deterministic wave function drives both the
    /// displayed mesh and ship motion. Not a production ocean renderer.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class OceanWaves : MonoBehaviour
    {
        [Header("Ocean Mesh")]
        [SerializeField, Range(8, 100)] private int cellsPerSide = 48;
        [SerializeField, Min(20f)] private float width = 140f;
        [SerializeField] private Transform followTarget;

        [Header("Perlin Waves")]
        [SerializeField, Min(0f)] private float amplitude = 0.48f;
        [SerializeField, Min(0.001f)] private float frequency = 0.055f;
        [SerializeField, Min(0f)] private float waveSpeed = 0.23f;

        private Mesh mesh;
        private Vector3[] vertices;

        public Transform FollowTarget { set => followTarget = value; }

        /// <summary>Height in world space at a world X/Z coordinate and time.</summary>
        public float SampleHeight(float worldX, float worldZ, float time)
        {
            float a = Mathf.PerlinNoise(
                worldX * frequency + time * waveSpeed,
                worldZ * frequency + time * waveSpeed * 0.6f);

            float b = Mathf.PerlinNoise(
                worldX * frequency * 2.35f - time * waveSpeed * 0.75f + 83.1f,
                worldZ * frequency * 2.35f + time * waveSpeed * 0.5f + 127.3f);

            return amplitude * (2f * a - 1f)
                 + amplitude * 0.30f * (2f * b - 1f);
        }

        private void Awake()
        {
            BuildMesh();
        }

        private void LateUpdate()
        {
            if (followTarget != null)
            {
                Vector3 p = followTarget.position;
                transform.position = new Vector3(p.x, 0f, p.z);
            }

            // The ship and renderer sample identical absolute world coordinates.
            float step = width / cellsPerSide;
            int rowLength = cellsPerSide + 1;
            float half = width * 0.5f;
            float now = Time.time;

            for (int z = 0; z < rowLength; z++)
            {
                for (int x = 0; x < rowLength; x++)
                {
                    int index = z * rowLength + x;
                    float lx = -half + x * step;
                    float lz = -half + z * step;
                    vertices[index].y = SampleHeight(
                        lx + transform.position.x,
                        lz + transform.position.z,
                        now);
                }
            }

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            // Mesh bounds were assigned conservatively at construction time.
        }

        private void BuildMesh()
        {
            int count = cellsPerSide + 1;
            vertices = new Vector3[count * count];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[cellsPerSide * cellsPerSide * 6];
            float step = width / cellsPerSide;
            float half = width * 0.5f;

            for (int z = 0; z < count; z++)
            {
                for (int x = 0; x < count; x++)
                {
                    int index = z * count + x;
                    vertices[index] = new Vector3(-half + x * step, 0f, -half + z * step);
                    uvs[index] = new Vector2((float)x / cellsPerSide, (float)z / cellsPerSide);
                }
            }

            int t = 0;
            for (int z = 0; z < cellsPerSide; z++)
            {
                for (int x = 0; x < cellsPerSide; x++)
                {
                    int i = z * count + x;
                    // Winding faces UP on a Unity X/Z plane.
                    triangles[t++] = i;
                    triangles[t++] = i + count;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + count;
                    triangles[t++] = i + count + 1;
                }
            }

            mesh = new Mesh { name = "Boatships Dynamic Ocean" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(width + 2f, amplitude * 4f + 4f, width + 2f));
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }
}
