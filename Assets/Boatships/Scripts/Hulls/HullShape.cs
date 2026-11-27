using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// The smooth outline a voxel hull is sculpted to when rendered: a pointed, raked bow, a broad
    /// transom stern, a rounded bilge and a rocker rising towards the ends. Damage still works per voxel;
    /// this only decides where each voxel's corners are drawn.
    /// Stations: t = 0 at the keel to 1 at the deck, zc = -1 at the stern to +1 at the bow.
    /// </summary>
    public static class HullShape
    {
        public static float HalfLength(HullDefinition d) => d.Length * 0.5f * d.BlockSize;
        public static float DeckY(HullDefinition d) => d.Height * 0.5f * d.BlockSize;
        public static float Depth(HullDefinition d) => d.Height * d.BlockSize;

        /// <summary>Hull station of the corner row k (0..Length).</summary>
        public static float Station(HullDefinition d, float k) => (k - d.Length * 0.5f) / (d.Length * 0.5f);

        /// <summary>Half-beam, in blocks, of the outside of the hull.</summary>
        public static float HalfWidthBlocks(HullDefinition d, float t, float zc)
        {
            float l = Mathf.Clamp01(Mathf.Abs(zc));
            float beam = zc >= 0f ? 1f - 0.9f * Mathf.Pow(l, 2.4f) : 1f - 0.5f * Mathf.Pow(l, 2.8f);
            float u = 1f - Mathf.Clamp01(t);
            return d.Width * 0.45f * beam * (0.42f + 0.58f * (1f - u * u));
        }

        /// <summary>How far the ends lean out (the bow forward, the transom aft) at height t.</summary>
        public static float Rake(HullDefinition d, float t, float zc)
        {
            float depth = Depth(d);
            t = Mathf.Clamp01(t);
            return zc >= 0f
                ? 0.32f * depth * Mathf.Pow(t, 1.4f) * Smooth(0.7f, 1f, zc)
                : -0.1f * depth * t * Smooth(0.8f, 1f, -zc);
        }

        /// <summary>How far the bottom sweeps up towards the bow and stern.</summary>
        public static float Rocker(HullDefinition d, float t, float zc)
        {
            float below = 1f - Mathf.Clamp01(t);
            return zc >= 0f
                ? 0.22f * Depth(d) * Mathf.Pow(below, 1.2f) * Smooth(0.55f, 1f, zc)
                : 0.12f * Depth(d) * below * Smooth(0.6f, 1f, -zc);
        }

        /// <summary>A point on the outside of the hull; side is -1 (port), 0 (centreline) or 1 (starboard).</summary>
        public static Vector3 Surface(HullDefinition d, float side, float t, float zc)
        {
            return new Vector3(
                side * HalfWidthBlocks(d, t, zc) * d.BlockSize,
                (t - 0.5f) * Depth(d) + Rocker(d, t, zc),
                zc * HalfLength(d) + Rake(d, t, zc));
        }

        /// <summary>Outward normal of the hull side at a station.</summary>
        public static Vector3 SurfaceNormal(HullDefinition d, float side, float t, float zc)
        {
            const float e = 0.03f;
            Vector3 along = Surface(d, side, t, zc + e) - Surface(d, side, t, zc - e);
            Vector3 up = Surface(d, side, Mathf.Min(1f, t + e), zc) - Surface(d, side, Mathf.Max(0f, t - e), zc);
            Vector3 normal = Vector3.Cross(up, along).normalized;
            return normal.x * side < 0f ? -normal : normal;
        }

        /// <summary>Top of the curled stem post at the bow, where the forestay is made fast.</summary>
        public static Vector3 StemHead(HullDefinition d) =>
            Surface(d, 0f, 1f, 1f) + new Vector3(0f, 0.26f, 0.08f) * Depth(d);

        /// <summary>Bulwark height above the deck: low amidships, sweeping up into the bow.</summary>
        public static float BulwarkHeight(HullDefinition d, float zc) =>
            (0.13f + 0.25f * Mathf.Pow(Smooth(0.5f, 1f, zc), 1.5f)) * Depth(d);

        /// <summary>Stations aft of this get an open quarterdeck railing instead of a solid bulwark.</summary>
        public const float RailingStation = -0.42f;

        private static float Smooth(float from, float to, float value)
        {
            float x = Mathf.Clamp01((value - from) / (to - from));
            return x * x * (3f - 2f * x);
        }
    }
}
