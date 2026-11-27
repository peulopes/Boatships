using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// Low-poly trim around a sculpted voxel hull: bulwarks with a cap rail, an open quarterdeck railing,
    /// keel, curled stem post, anchors, lit stern windows, a stern lantern and a deck hatch.
    /// Each piece is fixed to one hull block and disappears when that block is shot away.
    /// The whole dressing is a single mesh, rebuilt when the hull changes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class HullDressing : MonoBehaviour
    {
        private const int Trim = 0, Planks = 1, Iron = 2, Lamp = 3, Keel = 4, Submeshes = 5;

        private VoxelHull hull;
        private HullDefinition builtFor;
        private Mesh mesh;
        private readonly List<(Vector3Int anchor, LowPolyMesh part)> pieces = new List<(Vector3Int, LowPolyMesh)>();
        private readonly LowPolyMesh combined = new LowPolyMesh(Submeshes);

        public static HullDressing Attach(VoxelHull hull)
        {
            Transform existing = hull.transform.Find("Hull Dressing");
            GameObject go = existing != null ? existing.gameObject :
                new GameObject("Hull Dressing", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(hull.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            HullDressing dressing = go.GetComponent<HullDressing>();
            if (dressing == null) dressing = go.AddComponent<HullDressing>();
            dressing.hull = hull;
            return dressing;
        }

        public void Refresh()
        {
            if (hull == null || hull.Definition == null) return;
            if (builtFor != hull.Definition) Design(hull.Definition);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Boatships Hull Dressing" };
                mesh.MarkDynamic();
                GetComponent<MeshFilter>().sharedMesh = mesh;
            }
            Material hullMaterial = hull.GetComponent<MeshRenderer>().sharedMaterial;
            GetComponent<MeshRenderer>().sharedMaterials = new[]
            {
                ShipPalette.Solid(ShipPalette.Trim), ShipPalette.Lighter(hullMaterial) ?? hullMaterial,
                ShipPalette.Solid(ShipPalette.Iron), ShipPalette.Glow(ShipPalette.Lamp), hullMaterial
            };
            combined.Clear();
            foreach ((Vector3Int anchor, LowPolyMesh part) in pieces)
                if (hull.HasBlock(anchor.x, anchor.y, anchor.z)) part.AppendTo(combined);
            combined.WriteTo(mesh);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        private void Design(HullDefinition d)
        {
            builtFor = d;
            pieces.Clear();
            float depth = HullShape.Depth(d);
            int top = d.Height - 1, centre = d.Width / 2;
            Vector3 up = Vector3.up;

            // Bulwarks with a tan cap rail along each side; an open railing around the quarterdeck.
            for (int side = -1; side <= 1; side += 2)
            for (int z = 0; z < d.Length; z++)
            {
                if (!OuterDeckCell(d, side, z, out Vector3Int anchor)) continue;
                float zcA = HullShape.Station(d, z), zcB = HullShape.Station(d, z + 1);
                Vector3 a = HullShape.Surface(d, side, 1f, zcA), b = HullShape.Surface(d, side, 1f, zcB);
                var part = new LowPolyMesh(Submeshes);
                if ((zcA + zcB) * 0.5f > HullShape.RailingStation)
                {
                    float ha = HullShape.BulwarkHeight(d, zcA), hb = HullShape.BulwarkHeight(d, zcB);
                    part.Board(a, b, ha, hb, 0.035f, Planks);
                    part.Board(a + up * ha, b + up * hb, 0.032f, 0.032f, 0.075f, Trim);
                }
                else Railing(part, a, b, depth);
                pieces.Add((anchor, part));
            }

            // Transom railing and stern lantern.
            if (Designed(d, centre, top, 0))
            {
                var part = new LowPolyMesh(Submeshes);
                Vector3 port = HullShape.Surface(d, -1f, 1f, -1f), starboard = HullShape.Surface(d, 1f, 1f, -1f);
                for (int i = 0; i < 3; i++)
                    Railing(part, Vector3.Lerp(port, starboard, i / 3f), Vector3.Lerp(port, starboard, (i + 1) / 3f), depth);
                Vector3 post = HullShape.Surface(d, 0f, 1f, -1f) + new Vector3(0f, 0f, 0.07f);
                float lanternHeight = 0.5f * depth;
                part.Box(post + up * lanternHeight * 0.5f, new Vector3(0.035f, lanternHeight, 0.035f), Trim);
                Vector3 lantern = post + up * (lanternHeight + 0.05f);
                part.Box(lantern + up * -0.045f, new Vector3(0.09f, 0.02f, 0.09f), Iron);
                part.Box(lantern, new Vector3(0.065f, 0.08f, 0.065f), Lamp);
                part.Prism(lantern + up * 0.04f, 0.055f, 0.012f, 0.05f, 4, Iron);
                pieces.Add((new Vector3Int(centre, top, 0), part));
            }

            // Keel along the bottom centreline.
            for (int z = 0; z < d.Length; z++)
            {
                if (!Designed(d, centre, 0, z)) continue;
                var part = new LowPolyMesh(Submeshes);
                part.Board(HullShape.Surface(d, 0f, 0f, HullShape.Station(d, z)),
                    HullShape.Surface(d, 0f, 0f, HullShape.Station(d, z + 1)), 0.075f, 0.075f, 0.06f, Keel, Vector3.down);
                pieces.Add((new Vector3Int(centre, 0, z), part));
            }

            // Stem post up the bow, finished with a curled scroll head.
            int bow = d.Length - 1;
            for (int y = 0; y < d.Height; y++)
            {
                if (!Designed(d, centre, y, bow)) continue;
                var part = new LowPolyMesh(Submeshes);
                part.Beam(HullShape.Surface(d, 0f, (float)y / d.Height, 1f) + new Vector3(0f, 0f, 0.02f),
                    HullShape.Surface(d, 0f, (float)(y + 1) / d.Height, 1f) + new Vector3(0f, 0f, 0.02f),
                    0.07f, 0.055f, Iron, Vector3.right);
                pieces.Add((new Vector3Int(centre, y, bow), part));
            }
            if (Designed(d, centre, top, bow))
            {
                var part = new LowPolyMesh(Submeshes);
                Vector3 previous = HullShape.Surface(d, 0f, 1f, 1f) + new Vector3(0f, 0f, 0.02f);
                Vector3 head = HullShape.StemHead(d);
                part.Beam(previous, head, 0.07f, 0.055f, Iron, Vector3.right);
                float r0 = 0.12f * depth;
                Vector3 curl = head - new Vector3(0f, 0f, r0);
                previous = head;
                for (int i = 1; i <= 9; i++)
                {
                    float angle = i / 9f * Mathf.PI * 1.75f, r = r0 * (1f - 0.55f * angle / (Mathf.PI * 2f));
                    Vector3 next = curl + new Vector3(0f, Mathf.Sin(angle), Mathf.Cos(angle)) * r;
                    part.Beam(previous, next, 0.06f - i * 0.003f, 0.05f, Iron, Vector3.right);
                    previous = next;
                }
                pieces.Add((new Vector3Int(centre, top, bow), part));
            }

            // Anchors hung on each bow.
            for (int side = -1; side <= 1; side += 2)
            {
                var part = new LowPolyMesh(Submeshes);
                Vector3 normal = HullShape.SurfaceNormal(d, side, 0.62f, 0.7f);
                Vector3 at = HullShape.Surface(d, side, 0.62f, 0.7f) + normal * 0.025f;
                Anchor(part, at, Quaternion.LookRotation(normal, Vector3.ProjectOnPlane(up, normal)), depth);
                pieces.Add((NearestCell(d, at), part));
            }

            // Lit cabin windows in the transom and on each quarter.
            {
                Vector3 low = HullShape.Surface(d, 0f, 0.5f, -1f), high = HullShape.Surface(d, 0f, 0.75f, -1f);
                Vector3 normal = Vector3.Cross(high - low, Vector3.right).normalized;
                Vector3 middle = HullShape.Surface(d, 0f, 0.62f, -1f);
                float spread = HullShape.HalfWidthBlocks(d, 0.62f, -1f) * d.BlockSize * 0.42f;
                for (int side = -1; side <= 1; side += 2)
                {
                    var part = new LowPolyMesh(Submeshes);
                    Vector3 at = middle + Vector3.right * (side * spread);
                    Window(part, at, Quaternion.LookRotation(normal, high - low), 0.13f, 0.16f);
                    pieces.Add((NearestCell(d, at), part));
                }
            }
            for (int side = -1; side <= 1; side += 2)
            foreach (float zc in new[] { -0.84f, -0.7f })
            {
                var part = new LowPolyMesh(Submeshes);
                Vector3 normal = HullShape.SurfaceNormal(d, side, 0.62f, zc);
                Vector3 at = HullShape.Surface(d, side, 0.62f, zc);
                Window(part, at, Quaternion.LookRotation(normal, Vector3.ProjectOnPlane(up, normal)), 0.11f, 0.14f);
                pieces.Add((NearestCell(d, at), part));
            }

            // Cargo hatch grating amidships, between the mast and the after guns.
            {
                var part = new LowPolyMesh(Submeshes);
                Vector3 deck = new Vector3(0f, HullShape.DeckY(d), 0f);
                part.Box(deck + up * 0.015f, new Vector3(0.36f, 0.03f, 0.3f), Keel);
                for (int i = -2; i <= 2; i++)
                    part.Box(deck + new Vector3(0f, 0.034f, i * 0.05f), new Vector3(0.3f, 0.012f, 0.025f), Trim);
                pieces.Add((NearestCell(d, deck - up * 0.05f), part));
            }
        }

        private static void Railing(LowPolyMesh part, Vector3 a, Vector3 b, float depth)
        {
            Vector3 up = Vector3.up;
            float rail = 0.3f * depth;
            part.Board(a, b, 0.05f, 0.05f, 0.04f, Planks);
            part.Box(a + up * (rail * 0.5f), new Vector3(0.035f, rail, 0.035f), Trim);
            part.Board(a + up * rail, b + up * rail, 0.03f, 0.03f, 0.055f, Trim);
            part.Board(a + up * (rail * 0.5f), b + up * (rail * 0.5f), 0.02f, 0.02f, 0.03f, Trim);
        }

        private static void Anchor(LowPolyMesh part, Vector3 at, Quaternion frame, float depth)
        {
            float s = depth;
            Vector3 P(float x, float y, float z) => at + frame * new Vector3(x, y, z) * s;
            part.Box(P(0f, 0f, 0f), frame, new Vector3(0.025f, 0.3f, 0.02f) * s, Iron);
            part.Box(P(0f, 0.13f, 0.01f), frame, new Vector3(0.2f, 0.025f, 0.025f) * s, Iron);
            part.Box(P(0f, 0.17f, 0f), frame, new Vector3(0.05f, 0.04f, 0.012f) * s, Iron);
            part.Box(P(0f, -0.15f, 0f), frame, new Vector3(0.05f, 0.03f, 0.02f) * s, Iron);
            for (int side = -1; side <= 1; side += 2)
            {
                part.Beam(P(0f, -0.15f, 0f), P(side * 0.1f, -0.07f, 0f), 0.022f * s, 0.018f * s, Iron, frame * Vector3.forward);
                part.Box(P(side * 0.1f, -0.06f, 0f), frame * Quaternion.Euler(0f, 0f, side * 45f),
                    new Vector3(0.05f, 0.05f, 0.015f) * s, Iron);
            }
        }

        private static void Window(LowPolyMesh part, Vector3 at, Quaternion frame, float width, float height)
        {
            Vector3 normal = frame * Vector3.forward;
            part.Box(at + normal * 0.008f, frame, new Vector3(width + 0.04f, height + 0.04f, 0.02f), Trim);
            part.Box(at + normal * 0.016f, frame, new Vector3(width, height, 0.02f), Lamp);
            part.Box(at + normal * 0.028f, frame, new Vector3(0.014f, height, 0.008f), Trim);
            part.Box(at + normal * 0.028f, frame, new Vector3(width, 0.014f, 0.008f), Trim);
        }

        private static bool Designed(HullDefinition d, int x, int y, int z) => d.ContainsBlock(x, y, z);

        /// <summary>The outermost deck block of a row on one side, which carries that row's bulwark.</summary>
        private static bool OuterDeckCell(HullDefinition d, int side, int z, out Vector3Int cell)
        {
            int y = d.Height - 1;
            for (int i = 0; i < d.Width; i++)
            {
                int x = side > 0 ? d.Width - 1 - i : i;
                if (!d.ContainsBlock(x, y, z)) continue;
                cell = new Vector3Int(x, y, z);
                return true;
            }
            cell = default;
            return false;
        }

        private static Vector3Int NearestCell(HullDefinition d, Vector3 local)
        {
            Vector3Int best = new Vector3Int(d.Width / 2, d.Height - 1, d.Length / 2);
            float bestDistance = float.PositiveInfinity;
            for (int x = 0; x < d.Width; x++)
            for (int y = 0; y < d.Height; y++)
            for (int z = 0; z < d.Length; z++)
            {
                if (!d.ContainsBlock(x, y, z)) continue;
                float distance = (d.BlockCenter(x, y, z) - local).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = new Vector3Int(x, y, z);
            }
            return best;
        }
    }
}
