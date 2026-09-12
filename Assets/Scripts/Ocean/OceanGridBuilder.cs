using UnityEngine;
using UnityEngine.Rendering;

namespace TFOU.Ocean
{
    /// <summary>
    /// Builds the flat grid meshes the ocean shader displaces on the GPU.
    /// Three rings are used:
    ///   NEAR     - fine cells around the ship (full wave detail + micro chop)
    ///   MID      - coarse cells out to ~2 km  (swell only, chop faded by vertex alpha)
    ///   HORIZON  - a 2-quad skirt out to 30 km (flat, fog + sky reflection only)
    /// Vertex color alpha carries a radial "detail" weight (1 at center -> 0 at edge)
    /// so wave amplitude dies smoothly at ring borders: no seams, no popping, and
    /// high-frequency waves only render where cell density can resolve them.
    /// </summary>
    public static class OceanGridBuilder
    {
        public static Mesh BuildRing(int resolution, float size, bool radialFade, float maxAlpha, string meshName)
        {
            resolution = Mathf.Clamp(resolution, 8, 512);
            size = Mathf.Max(10f, size);

            int vertCount = (resolution + 1) * (resolution + 1);
            var verts = new Vector3[vertCount];
            var norms = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var cols = new Color[vertCount];
            var tris = new int[resolution * resolution * 6];

            int vi = 0, ti = 0;
            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    float fx = x / (float)resolution;
                    float fz = z / (float)resolution;

                    verts[vi] = new Vector3((fx - 0.5f) * size, 0f, (fz - 0.5f) * size);
                    norms[vi] = Vector3.up;
                    uvs[vi] = new Vector2(fx, fz);

                    float alpha = maxAlpha;
                    if (radialFade)
                    {
                        float radial = Mathf.Sqrt((fx - 0.5f) * (fx - 0.5f) + (fz - 0.5f) * (fz - 0.5f)) * 2f;
                        alpha = maxAlpha * (1f - Mathf.SmoothStep(0.55f, 0.98f, radial));
                    }
                    cols[vi] = new Color(0f, 0f, 0f, alpha);

                    if (x < resolution && z < resolution)
                    {
                        int a = z * (resolution + 1) + x;
                        int b = a + 1;
                        int c = a + (resolution + 1);
                        int d = c + 1;

                        // Upward-facing winding (matches original grid).
                        tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                        tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                    }
                    vi++;
                }
            }

            var mesh = new Mesh { name = meshName };
            if (vertCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            // Vertices move in the vertex shader; pad bounds so the ring never gets
            // frustum-culled while its center is behind the camera.
            var b = mesh.bounds;
            b.size = new Vector3(b.size.x, 128f, b.size.z);
            mesh.bounds = b;
            return mesh;
        }

        public static float CellSize(int resolution, float size) => Mathf.Max(10f, size) / Mathf.Clamp(resolution, 8, 512);
    }
}
