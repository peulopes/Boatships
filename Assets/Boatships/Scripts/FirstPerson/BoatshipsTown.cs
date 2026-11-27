using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    public enum TownService { Tavern, Store, Shipwright, Harbor }

    /// <summary>Builds a walkable old harbour town on top of a port island's procedural terrain: a planked pier with
    /// bollards and a cargo crane, a cobbled square with a well, market stalls and lamps, four service buildings
    /// (tavern, general store, shipwright, harbour master), half-timbered back-street houses and townsfolk.
    /// Everything is destroyed by Clear().</summary>
    public sealed class BoatshipsTown : MonoBehaviour
    {
        private static readonly string[] Signs = { "TAVERN", "GENERAL STORE", "SHIPWRIGHT", "HARBOR MASTER" };
        private static readonly string[] Prompts =
            { "Enter the tavern", "Enter the general store", "Visit the shipwright", "Speak to the harbor master" };

        private static readonly Color[] Plaster =
        {
            new Color(0.9f, 0.87f, 0.8f), new Color(0.85f, 0.7f, 0.46f), new Color(0.86f, 0.72f, 0.66f),
            new Color(0.68f, 0.77f, 0.8f), new Color(0.8f, 0.73f, 0.6f), new Color(0.73f, 0.78f, 0.64f)
        };
        private static readonly Color[] RoofTiles =
        {
            new Color(0.62f, 0.27f, 0.17f), new Color(0.5f, 0.2f, 0.14f), new Color(0.32f, 0.34f, 0.38f), new Color(0.45f, 0.34f, 0.22f)
        };
        private static readonly Color[] ShutterPaint =
        {
            new Color(0.2f, 0.42f, 0.3f), new Color(0.2f, 0.32f, 0.52f), new Color(0.55f, 0.18f, 0.15f), new Color(0.38f, 0.26f, 0.15f)
        };
        private static readonly Color[] Cobbles = { new Color(0.56f, 0.54f, 0.5f), new Color(0.48f, 0.46f, 0.43f), new Color(0.62f, 0.58f, 0.52f) };
        private static readonly Color[] Produce =
        {
            new Color(0.75f, 0.12f, 0.1f), new Color(0.95f, 0.55f, 0.12f), new Color(0.92f, 0.8f, 0.25f),
            new Color(0.4f, 0.6f, 0.2f), new Color(0.42f, 0.18f, 0.4f)
        };
        private static readonly Color[] Blooms =
        {
            new Color(0.85f, 0.15f, 0.2f), new Color(0.95f, 0.6f, 0.75f), new Color(0.95f, 0.85f, 0.3f), new Color(0.95f, 0.95f, 0.92f)
        };
        private static readonly Color Timber = new Color(0.27f, 0.17f, 0.1f);
        private static readonly Color Stone = new Color(0.58f, 0.56f, 0.52f);
        private static readonly Color DarkStone = new Color(0.4f, 0.39f, 0.37f);
        private static readonly Color Plank = new Color(0.47f, 0.33f, 0.19f);
        private static readonly Color DarkWood = new Color(0.32f, 0.22f, 0.12f);
        private static readonly Color Iron = new Color(0.14f, 0.14f, 0.15f);
        private static readonly Color Brass = new Color(0.72f, 0.56f, 0.24f);
        private static readonly Color Glass = new Color(0.95f, 0.78f, 0.45f);
        private static readonly Color LanternLight = new Color(1f, 0.82f, 0.45f);
        private static readonly Color Cream = new Color(0.93f, 0.9f, 0.8f);
        private static readonly Color Leaves = new Color(0.25f, 0.45f, 0.2f);
        private static readonly Color Rope = new Color(0.7f, 0.6f, 0.42f);
        private static readonly Color Canvas = new Color(0.82f, 0.76f, 0.6f);
        private static readonly Quaternion Id = Quaternion.identity;

        private struct HouseStyle
        {
            public float Width, Depth, GroundStorey, UpperStorey;
            public Color Wall, Roof, Shutter;
            public bool Timbered, Chimney, Quoins, FlowerBoxes, WideDoor, PlankWalls;
        }

        private GameObject root;
        private ProceduralIslandChunk islandChunk;
        private bool permanent;
        private readonly List<GameObject> hiddenClutter = new List<GameObject>();
        private static readonly List<BoatshipsTown> Harbours = new List<BoatshipsTown>();
        /// <summary>Named parts the harbour checks look up; merging keeps their (empty) objects.</summary>
        private static readonly HashSet<string> KeptParts = new HashSet<string> { "Walls", "Roof", "Pier" };
        public Transform Root => root != null ? root.transform : null;
        private static readonly HashSet<string> Clutter = new HashSet<string> { "Tree Trunk", "Low Poly Canopy", "Palm Tree", "Low Poly Rock" };
        // Shared by every town (all harbours use the same palette), so building a town creates no materials.
        private static readonly Dictionary<long, Material> materials = new Dictionary<long, Material>();
        private readonly List<Vector3> occupied = new List<Vector3>(); // (x, radius, z)
        private ArchipelagoWorld.IslandSite site;
        private int seed;
        private System.Random rng;
        private Vector3? fortCover; // harbour towns only: where the fort's battery protects ships

        public bool Built => root != null;

        // A port island's harbour town is built with the island and stays while the island is loaded.
        public Vector2Int Cell { get; private set; }
        public Vector2 Mooring { get; private set; }
        public Vector3 Spawn { get; private set; }
        public float Facing { get; private set; }
        public IReadOnlyList<FpsInteractable> Doors => doors;
        /// <summary>Receives door use (tavern, store…) while the player walks this town.</summary>
        public Action<TownService> Handler { get; set; }
        private readonly List<FpsInteractable> doors = new List<FpsInteractable>();

        /// <summary>The loaded harbour town of a port island, or null if that island is not streamed in.</summary>
        public static BoatshipsTown For(Vector2Int cell)
        {
            foreach (BoatshipsTown harbour in Harbours)
                if (harbour != null && harbour.Built && harbour.isActiveAndEnabled && harbour.Cell == cell) return harbour;
            return null;
        }

        /// <summary>Where ships moor beside the town pier: on the harbour side, just outside the island's navigation circle.</summary>
        public static Vector2 MooringPoint(ArchipelagoWorld.IslandSite site)
        {
            float yaw = ProceduralIslandChunk.HarbourYaw(site) * Mathf.Deg2Rad;
            return site.Center + new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * (site.Radius * 0.78f + 5.5f);
        }

        /// <summary>Builds this port island's permanent harbour town as part of the island chunk.</summary>
        /// <param name="defendedWaters">Centre of the harbour waters the town's fort guards.</param>
        public void BuildHarbour(ArchipelagoWorld.IslandSite island, int worldSeed, ProceduralIslandChunk owner, Vector3 defendedWaters)
        {
            Vector2 mooring = MooringPoint(island);
            permanent = true;
            islandChunk = owner;
            fortCover = defendedWaters;
            List<FpsInteractable> built = Build(island, worldSeed, new Vector3(mooring.x, 0f, mooring.y),
                service => Handler?.Invoke(service), out Vector3 spawn, out float facing);
            root.transform.SetParent(owner.transform, true);
            doors.Clear(); doors.AddRange(built);
            Spawn = spawn; Facing = facing; Mooring = mooring; Cell = island.Cell;
            if (!Harbours.Contains(this)) Harbours.Add(this);
        }

        /// <summary>Builds the town. dock is where the ship is moored. Returns the doors to register with the
        /// first-person player, plus the spawn point and facing.</summary>
        public List<FpsInteractable> Build(ArchipelagoWorld.IslandSite island, int worldSeed, Vector3 dock,
            Action<TownService> use, out Vector3 spawn, out float facing)
        {
            Clear();
            site = island; seed = worldSeed;
            rng = new System.Random(island.DetailSeed);
            var doors = new List<FpsInteractable>();
            root = new GameObject("Port Town " + island.Cell);
            if (islandChunk == null) islandChunk = FindChunk(island);
            Vector2 center = island.Center;
            Vector2 dock2 = new Vector2(dock.x, dock.z);
            Vector2 inland = (center - dock2).sqrMagnitude > 0.01f ? (center - dock2).normalized : Vector2.up;
            Vector2 sideways = new Vector2(inland.y, -inland.x);

            // Pier: starts beside the moored ship and runs up onto the beach.
            Vector2 pierStart = dock2 + sideways * 1.5f;
            Vector2 landing = pierStart;
            for (int i = 0; i < 200; i++)
            {
                landing = pierStart + inland * (i * 0.25f);
                if (H(landing) > 0.55f) break;
            }
            Vector2 pierEnd = landing + inland * 0.6f;
            Vector3 pierA = new Vector3(pierStart.x, 0.42f, pierStart.y);
            Vector3 pierB = new Vector3(pierEnd.x, Mathf.Max(0.42f, H(pierEnd) + 0.02f), pierEnd.y);
            BuildPier(pierA, pierB);

            Vector2 plaza = landing + inland * 3f;
            HideIslandClutter(plaza, 10.5f, landing);
            BuildStreet(landing, plaza, sideways);
            BuildSquare(plaza, inland);

            float[] angles = { -95f, -38f, 38f, 95f };
            for (int i = 0; i < 4; i++)
            {
                TownService service = (TownService)i;
                Vector2 spot = plaza + Rotate(inland, angles[i]) * 3.1f;
                for (int step = 0; step < 12 && !Buildable(site, seed, spot); step++) spot += inland * 0.4f;
                Vector2 toPlaza = (plaza - spot).normalized;
                Vector3 door = ServiceBuilding(spot, new Vector3(toPlaza.x, 0f, toPlaza.y), service);
                occupied.Add(new Vector3(spot.x, service == TownService.Shipwright ? 2.1f : 1.5f, spot.y));
                doors.Add(new FpsInteractable { Position = () => door, Prompt = Prompts[i], Radius = 0.9f, Use = () => use(service) });
            }
            if (fortCover.HasValue) BuildFort(plaza, inland, fortCover.Value);

            // The moored ship itself: step back aboard to set sail.
            Vector3 boarding = pierA;
            doors.Add(new FpsInteractable
            {
                Position = () => boarding, Prompt = "The docks — leave town", Radius = 0.9f, Use = () => use(TownService.Harbor)
            });

            BuildBackStreets(plaza, inland, landing);
            BuildWaterfront(landing, inland, sideways);
            BuildPeople(plaza, inland, landing, sideways);
            // Thousands of cobbles, beams and trims become one renderer per material; townsfolk stay animated.
            BoatshipsMeshBatcher.Combine(root.transform, null, KeptParts);

            Vector2 spawn2 = Vector2.Lerp(pierStart, landing, 0.5f);
            spawn = new Vector3(spawn2.x, 0.6f, spawn2.y);
            facing = Mathf.Atan2(inland.x, inland.y) * Mathf.Rad2Deg;
            return doors;
        }

        public void Clear()
        {
            if (root != null) Destroy(root);
            root = null;
            foreach (GameObject item in hiddenClutter) if (item != null) item.SetActive(true);
            hiddenClutter.Clear();
            if (!permanent) islandChunk = null;
            occupied.Clear();
        }

        private static ProceduralIslandChunk FindChunk(ArchipelagoWorld.IslandSite island)
        {
            foreach (ProceduralIslandChunk chunk in FindObjectsByType<ProceduralIslandChunk>(FindObjectsInactive.Exclude))
            {
                Vector3 p = chunk.transform.position;
                if ((new Vector2(p.x, p.z) - island.Center).sqrMagnitude <= 1f) return chunk;
            }
            return null;
        }

        /// <summary>Island trees and rocks have no colliders; hide those standing in the town or on the path up from the pier.</summary>
        private void HideIslandClutter(Vector2 plaza, float radius, Vector2 landing)
        {
            if (islandChunk == null) return;
            foreach (Transform child in islandChunk.transform)
            {
                if (!Clutter.Contains(child.name) || !child.gameObject.activeSelf) continue;
                Vector2 p = new Vector2(child.position.x, child.position.z);
                if ((p - plaza).magnitude > radius && SegmentDistance(p, landing, plaza) > 2f) continue;
                // A harbour town replaces them for good; a temporary town only hides them.
                child.gameObject.SetActive(false);
                if (permanent) Destroy(child.gameObject);
                else hiddenClutter.Add(child.gameObject);
            }
        }

        internal static float Ground(ArchipelagoWorld.IslandSite site, int seed, Vector2 point) =>
            ProceduralIslandChunk.Height(site, seed, point.x - site.Center.x, point.y - site.Center.y);

        private static bool Buildable(ArchipelagoWorld.IslandSite site, int seed, Vector2 spot)
        {
            float h = Ground(site, seed, spot);
            if (h < 0.7f || h > 4.6f) return false;
            float spread = 0f;
            for (int i = 0; i < 4; i++)
                spread = Mathf.Max(spread, Mathf.Abs(Ground(site, seed, spot + Rotate(Vector2.up, i * 90f) * 1.1f) - h));
            return spread < 1.4f;
        }

        // ───────────────────────────── Waterfront ─────────────────────────────

        private void BuildPier(Vector3 a, Vector3 b)
        {
            Transform r = root.transform;
            Quaternion rotation = Quaternion.LookRotation(b - a);
            Vector3 up = rotation * Vector3.up, along = rotation * Vector3.forward, side = rotation * Vector3.right;
            float length = Vector3.Distance(a, b) + 0.4f;
            Vector3 mid = (a + b) * 0.5f;
            Prim(PrimitiveType.Cube, "Pier", r, mid, rotation, new Vector3(0.9f, 0.1f, length), Plank, true);
            float spacing = Mathf.Max(0.22f, length / 60f);
            for (float d = -length * 0.5f + 0.15f; d < length * 0.5f; d += spacing)
                Prim(PrimitiveType.Cube, "Plank Seam", r, mid + along * d + up * 0.051f, rotation, new Vector3(0.9f, 0.01f, 0.02f), DarkWood);
            foreach (int s in new[] { -1, 1 })
                Prim(PrimitiveType.Cube, "Stringer", r, mid + side * (0.46f * s) - up * 0.03f, rotation, new Vector3(0.06f, 0.14f, length), DarkWood);
            for (float t = 0f; t <= 1.001f; t += 0.2f)
            {
                Vector3 post = Vector3.Lerp(a, b, t);
                foreach (int s in new[] { -1, 1 })
                    Prim(PrimitiveType.Cylinder, "Pier Post", r, post + side * (0.42f * s) - Vector3.up * 0.35f, Id,
                        new Vector3(0.09f, 0.45f, 0.09f), DarkWood);
            }
            float[] bollards = { 0.12f, 0.45f, 0.8f };
            for (int i = 0; i < bollards.Length; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, bollards[i]) + side * (0.34f * (i % 2 == 0 ? 1f : -1f)) + up * 0.05f;
                Prim(PrimitiveType.Cylinder, "Bollard", r, p + Vector3.up * 0.07f, Id, new Vector3(0.1f, 0.07f, 0.1f), Iron, true);
                Prim(PrimitiveType.Sphere, "Bollard Cap", r, p + Vector3.up * 0.14f, Id, new Vector3(0.13f, 0.05f, 0.13f), Iron);
                if (i == 0) Prim(PrimitiveType.Cylinder, "Rope Coil", r, p + along * 0.22f + Vector3.up * 0.02f, Id, new Vector3(0.18f, 0.02f, 0.18f), Rope);
            }
        }

        private void BuildWaterfront(Vector2 landing, Vector2 inland, Vector2 sideways)
        {
            Transform r = root.transform;
            Lamp(landing + sideways * 0.9f + inland * 0.8f);
            Lamp(landing - sideways * 0.9f + inland * 0.8f);

            // Cargo waiting to be loaded.
            Vector2 cargo = landing + sideways * 1.6f + inland * 0.4f;
            if (H(cargo) > 0.45f)
            {
                float yaw = Mathf.Atan2(inland.x, inland.y) * Mathf.Rad2Deg;
                Crate(r, Ground3(cargo + sideways * 0.15f), 0.28f, yaw);
                Crate(r, Ground3(cargo - sideways * 0.18f + inland * 0.05f), 0.28f, yaw + 8f);
                Crate(r, Ground3(cargo) + Vector3.up * 0.28f, 0.26f, yaw + 20f);
                Barrel(r, Ground3(cargo + inland * 0.4f), true);
                Barrel(r, Ground3(cargo + inland * 0.42f + sideways * 0.26f), true);
                Barrel(r, Ground3(cargo - inland * 0.35f), false, yaw);
                Sack(r, Ground3(cargo + sideways * 0.45f + inland * 0.2f));
            }

            // Wooden cargo crane leaning out over the water.
            Vector2 cranePoint = landing - sideways * 1.5f + inland * 0.3f;
            float ch = H(cranePoint);
            if (ch > 0.45f)
            {
                Vector3 baseP = new Vector3(cranePoint.x, ch, cranePoint.y);
                Prim(PrimitiveType.Cube, "Crane Base", r, baseP + Vector3.up * 0.06f, Id, new Vector3(0.42f, 0.14f, 0.42f), Stone, true);
                Prim(PrimitiveType.Cylinder, "Crane Mast", r, baseP + Vector3.up * 0.95f, Id, new Vector3(0.1f, 0.9f, 0.1f), DarkWood, true);
                Vector3 seaward = new Vector3(-inland.x, 0f, -inland.y);
                Vector3 top = baseP + Vector3.up * 1.7f;
                Vector3 boomDir = (seaward * Mathf.Cos(20f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(20f * Mathf.Deg2Rad)).normalized;
                Vector3 tip = top + boomDir * 1.3f;
                Beam(r, "Crane Boom", top - boomDir * 0.15f, tip, 0.07f, DarkWood);
                Beam(r, "Crane Brace", baseP + Vector3.up * 1.05f, top + boomDir * 0.65f, 0.05f, DarkWood);
                Beam(r, "Crane Rope", tip, tip - Vector3.up * 0.75f, 0.012f, Rope);
                Prim(PrimitiveType.Sphere, "Cargo Net", r, tip - Vector3.up * 0.9f, Id, new Vector3(0.3f, 0.26f, 0.3f), Canvas);
            }

            // Fishing nets hung up to dry.
            Vector2 rack = landing - sideways * 2.5f + inland * 1.1f;
            float rh = H(rack);
            if (rh > 0.45f)
            {
                Vector3 c = new Vector3(rack.x, rh, rack.y);
                Vector3 across = new Vector3(inland.x, 0f, inland.y) * 0.5f;
                Prim(PrimitiveType.Cube, "Net Post", r, c + across + Vector3.up * 0.35f, Id, new Vector3(0.05f, 0.7f, 0.05f), DarkWood, true);
                Prim(PrimitiveType.Cube, "Net Post", r, c - across + Vector3.up * 0.35f, Id, new Vector3(0.05f, 0.7f, 0.05f), DarkWood, true);
                Beam(r, "Net Bar", c + across + Vector3.up * 0.68f, c - across + Vector3.up * 0.68f, 0.035f, DarkWood);
                Quaternion face = Quaternion.LookRotation(new Vector3(sideways.x, 0f, sideways.y));
                Prim(PrimitiveType.Cube, "Fishing Net", r, c + Vector3.up * 0.44f, face, new Vector3(0.95f, 0.46f, 0.012f), new Color(0.3f, 0.36f, 0.3f));
                Prim(PrimitiveType.Cube, "Net Float Line", r, c + Vector3.up * 0.22f, face, new Vector3(0.95f, 0.025f, 0.03f), new Color(0.85f, 0.5f, 0.2f));
            }

            // A rowboat pulled up on the beach.
            Vector2 boat = landing + sideways * 2.4f - inland * 0.5f;
            float bh = H(boat);
            if (bh > 0.4f)
            {
                Quaternion yaw = Quaternion.Euler(0f, Mathf.Atan2(inland.x, inland.y) * Mathf.Rad2Deg + 25f, 6f);
                Prim(PrimitiveType.Sphere, "Rowboat", r, new Vector3(boat.x, bh + 0.04f, boat.y), yaw, new Vector3(0.5f, 0.24f, 1.25f), Plank);
                Prim(PrimitiveType.Sphere, "Rowboat Keel", r, new Vector3(boat.x, bh + 0.15f, boat.y), yaw, new Vector3(0.06f, 0.04f, 1.2f), DarkWood);
                Prim(PrimitiveType.Cube, "Oar", r, new Vector3(boat.x, bh + 0.03f, boat.y) + new Vector3(sideways.x, 0f, sideways.y) * 0.42f,
                    yaw * Quaternion.Euler(0f, 12f, 0f), new Vector3(0.04f, 0.02f, 0.9f), DarkWood);
            }
        }

        // ───────────────────────────── Streets and square ─────────────────────────────

        private void BuildStreet(Vector2 landing, Vector2 plaza, Vector2 sideways)
        {
            Vector2 delta = plaza - landing;
            float length = delta.magnitude;
            Vector2 along = delta / Mathf.Max(0.001f, length);
            for (float d = 0.2f; d < length - 2.3f; d += 0.3f)
                for (float w = -0.45f; w <= 0.46f; w += 0.3f)
                    Cobble(landing + along * d + sideways * (w + ((int)(d / 0.3f) % 2) * 0.15f - 0.075f));
        }

        private void BuildSquare(Vector2 plaza, Vector2 inland)
        {
            const float radius = 2.35f;
            for (float x = -radius; x <= radius; x += 0.3f)
                for (float z = -radius; z <= radius; z += 0.3f)
                {
                    Vector2 offset = new Vector2(x + (Mathf.RoundToInt(z / 0.3f) % 2 == 0 ? 0f : 0.15f), z);
                    if (offset.sqrMagnitude <= radius * radius) Cobble(plaza + offset);
                }
            // Kerb ring around the cobbles.
            for (int i = 0; i < 40; i++)
            {
                float a = i * 9f;
                if (Mathf.Abs(a - 180f) < 14f) continue; // the street from the pier comes in here
                Vector2 p = plaza + Rotate(inland, a) * (radius + 0.15f);
                float h = H(p);
                if (h < 0.45f) continue;
                Prim(PrimitiveType.Cube, "Kerb", root.transform, new Vector3(p.x, h + 0.02f, p.y), Quaternion.Euler(0f, Yaw(Rotate(inland, a)), 0f),
                    new Vector3(0.36f, 0.06f, 0.12f), DarkStone);
            }

            Well(plaza);
            occupied.Add(new Vector3(plaza.x, 0.6f, plaza.y));
            Color[] canopies = { new Color(0.65f, 0.15f, 0.12f), new Color(0.2f, 0.45f, 0.3f) };
            for (int i = 0; i < 2; i++)
            {
                Vector2 p = plaza + Rotate(inland, i == 0 ? 145f : -145f) * 2.55f;
                Stall(p, plaza, canopies[i]);
                occupied.Add(new Vector3(p.x, 0.7f, p.y));
            }
            Vector2 cartSpot = plaza + inland * 3.6f;
            Cart(cartSpot, Yaw(Rotate(inland, 90f)));
            occupied.Add(new Vector3(cartSpot.x, 0.9f, cartSpot.y));
            foreach (float a in new[] { 70f, -70f }) Lamp(plaza + Rotate(inland, a) * 1.75f);
            foreach (float a in new[] { 165f, -165f }) Lamp(plaza + Rotate(inland, a) * 1.95f);
        }

        private void Cobble(Vector2 p)
        {
            float h = H(p);
            if (h < 0.45f) return;
            const float e = 0.15f;
            Vector3 normal = new Vector3(H(p - Vector2.right * e) - H(p + Vector2.right * e), 2f * e,
                H(p - Vector2.up * e) - H(p + Vector2.up * e)).normalized;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Range(-8f, 8f), 0f);
            Prim(PrimitiveType.Cube, "Cobble", root.transform, new Vector3(p.x, h + 0.01f, p.y), rotation,
                new Vector3(Range(0.24f, 0.28f), 0.045f, Range(0.22f, 0.28f)), Cobbles[rng.Next(Cobbles.Length)]);
        }

        private void Well(Vector2 p)
        {
            Transform well = Group("Well", Ground3(p) - Vector3.up * 0.05f, 0f);
            Prim(PrimitiveType.Cylinder, "Well Wall", well, new Vector3(0f, 0.17f, 0f), Id, new Vector3(0.72f, 0.17f, 0.72f), Stone, true);
            Prim(PrimitiveType.Cylinder, "Well Rim", well, new Vector3(0f, 0.35f, 0f), Id, new Vector3(0.78f, 0.025f, 0.78f), DarkStone);
            Prim(PrimitiveType.Cylinder, "Water", well, new Vector3(0f, 0.34f, 0f), Id, new Vector3(0.58f, 0.02f, 0.58f), new Color(0.12f, 0.22f, 0.28f));
            foreach (int s in new[] { -1, 1 })
            {
                Prim(PrimitiveType.Cube, "Well Post", well, new Vector3(s * 0.3f, 0.62f, 0f), Id, new Vector3(0.05f, 0.56f, 0.05f), DarkWood);
                Prim(PrimitiveType.Cube, "Well Roof", well, new Vector3(s * 0.17f, 0.97f, 0f), Quaternion.Euler(0f, 0f, -s * 35f),
                    new Vector3(0.46f, 0.03f, 0.52f), RoofTiles[0]);
            }
            Prim(PrimitiveType.Cylinder, "Winch", well, new Vector3(0f, 0.8f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.05f, 0.31f, 0.05f), DarkWood);
            Prim(PrimitiveType.Cube, "Well Rope", well, new Vector3(0.06f, 0.68f, 0f), Id, new Vector3(0.01f, 0.22f, 0.01f), Rope);
            Prim(PrimitiveType.Cylinder, "Bucket", well, new Vector3(0.06f, 0.55f, 0f), Id, new Vector3(0.09f, 0.05f, 0.09f), DarkWood);
        }

        private void Stall(Vector2 p, Vector2 plaza, Color canopy)
        {
            Vector2 toPlaza = (plaza - p).normalized;
            Transform stall = Group("Market Stall", Ground3(p), Yaw(toPlaza));
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    Prim(PrimitiveType.Cube, "Stall Post", stall, new Vector3(sx * 0.48f, sz > 0 ? 0.45f : 0.52f, sz * 0.27f), Id,
                        new Vector3(0.05f, sz > 0 ? 0.9f : 1.04f, 0.05f), DarkWood);
            Prim(PrimitiveType.Cube, "Counter", stall, new Vector3(0f, 0.42f, 0.08f), Id, new Vector3(1.02f, 0.05f, 0.42f), DarkWood, true);
            Prim(PrimitiveType.Cube, "Counter Front", stall, new Vector3(0f, 0.22f, 0.28f), Id, new Vector3(1.0f, 0.36f, 0.03f), Plank);
            for (int i = 0; i < 6; i++)
                Prim(PrimitiveType.Cube, "Canopy", stall, new Vector3(-0.5f + 0.0833f + i * 0.1667f, 0.98f, 0f), Quaternion.Euler(14f, 0f, 0f),
                    new Vector3(0.168f, 0.025f, 0.72f), i % 2 == 0 ? canopy : Cream);
            for (int b = 0; b < 3; b++)
            {
                Vector3 basket = new Vector3(-0.32f + b * 0.32f, 0.46f, 0.1f);
                Prim(PrimitiveType.Cylinder, "Basket", stall, basket, Id, new Vector3(0.22f, 0.035f, 0.22f), new Color(0.62f, 0.48f, 0.28f));
                Color fruit = Produce[rng.Next(Produce.Length)];
                for (int f = 0; f < 5; f++)
                    Prim(PrimitiveType.Sphere, "Produce", stall, basket + new Vector3(Range(-0.06f, 0.06f), 0.05f + f * 0.008f, Range(-0.06f, 0.06f)),
                        Id, Vector3.one * 0.065f, fruit);
            }
            // The vendor stands behind the counter.
            Transform vendor = new GameObject("Market Vendor").transform;
            vendor.SetParent(stall, false);
            vendor.localPosition = new Vector3(0f, 0f, -0.42f);
            BoatshipsHumanoid.Build(vendor, Color.HSVToRGB((float)rng.NextDouble(), 0.4f, 0.7f), BoatshipsFirstPerson.HumanHeight, false, rng.Next());
        }

        private void Cart(Vector2 p, float yaw)
        {
            float h = H(p);
            if (h < 0.5f) return;
            Transform cart = Group("Hand Cart", new Vector3(p.x, h, p.y), yaw);
            Prim(PrimitiveType.Cube, "Cart Bed", cart, new Vector3(0f, 0.3f, 0f), Id, new Vector3(0.5f, 0.05f, 0.8f), Plank, true);
            foreach (int s in new[] { -1, 1 })
            {
                Prim(PrimitiveType.Cube, "Cart Side", cart, new Vector3(s * 0.25f, 0.4f, 0f), Id, new Vector3(0.03f, 0.16f, 0.8f), DarkWood);
                Prim(PrimitiveType.Cylinder, "Wheel", cart, new Vector3(s * 0.3f, 0.18f, -0.1f), Quaternion.Euler(0f, 0f, 90f),
                    new Vector3(0.36f, 0.025f, 0.36f), DarkWood);
                Prim(PrimitiveType.Cylinder, "Hub", cart, new Vector3(s * 0.32f, 0.18f, -0.1f), Quaternion.Euler(0f, 0f, 90f),
                    new Vector3(0.08f, 0.02f, 0.08f), Iron);
                Prim(PrimitiveType.Cube, "Handle", cart, new Vector3(s * 0.18f, 0.22f, 0.62f), Quaternion.Euler(-14f, 0f, 0f),
                    new Vector3(0.035f, 0.035f, 0.5f), DarkWood);
            }
            Sack(cart, new Vector3(-0.1f, 0.33f, 0.15f));
            Sack(cart, new Vector3(0.1f, 0.33f, -0.12f));
            Barrel(cart, new Vector3(0.08f, 0.33f, 0.2f), true);
        }

        private void Lamp(Vector2 p)
        {
            float h = H(p);
            if (h < 0.45f) return;
            Transform r = root.transform;
            Prim(PrimitiveType.Cylinder, "Lamp Base", r, new Vector3(p.x, h + 0.03f, p.y), Id, new Vector3(0.13f, 0.04f, 0.13f), DarkStone);
            Prim(PrimitiveType.Cylinder, "Lamp Post", r, new Vector3(p.x, h + 0.45f, p.y), Id, new Vector3(0.045f, 0.45f, 0.045f), Iron, true);
            Prim(PrimitiveType.Cube, "Lantern Base", r, new Vector3(p.x, h + 0.9f, p.y), Id, new Vector3(0.14f, 0.025f, 0.14f), Iron);
            Prim(PrimitiveType.Cube, "Lantern", r, new Vector3(p.x, h + 0.98f, p.y), Id, new Vector3(0.11f, 0.14f, 0.11f), LanternLight, false, 2);
            Prim(PrimitiveType.Cube, "Lantern Cap", r, new Vector3(p.x, h + 1.07f, p.y), Quaternion.Euler(0f, 45f, 0f), new Vector3(0.15f, 0.04f, 0.15f), Iron);
            Prim(PrimitiveType.Sphere, "Lantern Finial", r, new Vector3(p.x, h + 1.11f, p.y), Id, Vector3.one * 0.04f, Iron);
        }

        // ───────────────────────────── Buildings ─────────────────────────────

        private Vector3 ServiceBuilding(Vector2 spot, Vector3 front, TownService service)
        {
            HouseStyle style;
            Transform t;
            float g;
            Vector3 door;
            switch (service)
            {
                case TownService.Tavern:
                    style = new HouseStyle
                    {
                        Width = 2.2f, Depth = 1.8f, GroundStorey = 1.15f, UpperStorey = 0.9f, Wall = new Color(0.84f, 0.68f, 0.45f),
                        Roof = RoofTiles[0], Shutter = ShutterPaint[0], Timbered = true, Chimney = true, FlowerBoxes = true
                    };
                    t = House(spot, front, style, "Tavern", out g, out door);
                    Sign(t, Signs[0], g + 1.02f, style.Depth * 0.5f + 0.04f, 1.1f);
                    HangingSign(t, style.Width * 0.5f - 0.14f, g + style.GroundStorey - 0.06f, style.Depth * 0.5f + 0.08f);
                    WallLantern(t, new Vector3(0.42f, g + 0.72f, style.Depth * 0.5f));
                    Bench(t, new Vector3(-0.68f, g, style.Depth * 0.5f + 0.24f), 0f);
                    Barrel(t, new Vector3(0.72f, g, style.Depth * 0.5f + 0.5f), true);
                    Prim(PrimitiveType.Cylinder, "Tankard", t, new Vector3(0.68f, g + 0.33f, style.Depth * 0.5f + 0.48f), Id, new Vector3(0.05f, 0.035f, 0.05f), Brass);
                    float side = -style.Width * 0.5f - 0.24f;
                    Barrel(t, new Vector3(side, DecorY(t, side, 0.4f), 0.4f), true);
                    Barrel(t, new Vector3(side, DecorY(t, side, 0.05f), 0.05f), false, 0f);
                    return door;

                case TownService.Store:
                    style = new HouseStyle
                    {
                        Width = 2.2f, Depth = 1.7f, GroundStorey = 1.3f, Wall = Plaster[0], Roof = RoofTiles[1],
                        Shutter = ShutterPaint[1], FlowerBoxes = false
                    };
                    t = House(spot, front, style, "General Store", out g, out door);
                    Sign(t, Signs[1], g + 1.18f, style.Depth * 0.5f + 0.04f, 1.3f);
                    Awning(t, style.Width + 0.1f, g + 0.95f, style.Depth * 0.5f, new Color(0.62f, 0.16f, 0.14f));
                    foreach (int s in new[] { -1, 1 })
                    {
                        Crate(t, new Vector3(s * 0.72f, g, style.Depth * 0.5f + 0.2f), 0.24f, 0f);
                        Color fruit = Produce[rng.Next(Produce.Length)];
                        for (int f = 0; f < 6; f++)
                            Prim(PrimitiveType.Sphere, "Produce", t, new Vector3(s * 0.72f + Range(-0.07f, 0.07f), g + 0.27f, style.Depth * 0.5f + 0.2f + Range(-0.07f, 0.07f)),
                                Id, Vector3.one * 0.07f, fruit);
                        Sack(t, new Vector3(s * 1.0f, g, style.Depth * 0.5f + 0.16f));
                    }
                    return door;

                case TownService.Shipwright:
                    style = new HouseStyle
                    {
                        Width = 2.6f, Depth = 1.9f, GroundStorey = 1.35f, Wall = new Color(0.55f, 0.4f, 0.26f), Roof = RoofTiles[3],
                        Shutter = ShutterPaint[3], PlankWalls = true, WideDoor = true
                    };
                    t = House(spot, front, style, "Shipwright", out g, out door);
                    Sign(t, Signs[2], g + 1.2f, style.Depth * 0.5f + 0.04f, 1.2f);
                    BoatFrame(t, 0f, -style.Depth * 0.5f - 0.55f);
                    float pz = style.Depth * 0.5f + 0.22f;
                    for (int i = 0; i < 4; i++)
                        Prim(PrimitiveType.Cube, "Timber Plank", t, new Vector3(-0.95f + (i % 2) * 0.04f, DecorY(t, -0.95f, pz) + 0.03f + i * 0.045f, pz),
                            Quaternion.Euler(0f, 90f + i * 3f, 0f), new Vector3(0.2f, 0.04f, 0.8f), i % 2 == 0 ? Plank : new Color(0.58f, 0.42f, 0.24f), i == 0);
                    Sawhorse(t, new Vector3(0.95f, DecorY(t, 0.95f, style.Depth * 0.5f + 0.35f), style.Depth * 0.5f + 0.35f));
                    return door;

                default:
                    style = new HouseStyle
                    {
                        Width = 2.2f, Depth = 1.8f, GroundStorey = 1.15f, UpperStorey = 0.95f, Wall = new Color(0.86f, 0.86f, 0.82f),
                        Roof = RoofTiles[2], Shutter = ShutterPaint[1], Quoins = true, Chimney = true
                    };
                    t = House(spot, front, style, "Harbor Master", out g, out door);
                    Sign(t, Signs[3], g + 1.02f, style.Depth * 0.5f + 0.04f, 1.3f);
                    Balcony(t, g + style.GroundStorey, style.Depth * 0.5f + 0.08f);
                    Flagpole(t, style.Width * 0.5f + 0.35f, style.Depth * 0.5f - 0.1f);
                    Anchor(t, -0.75f, style.Depth * 0.5f + 0.35f);
                    return door;
            }
        }

        private void BuildBackStreets(Vector2 plaza, Vector2 inland, Vector2 landing)
        {
            int built = 0;
            float[] radii = { 5.5f, 7.8f };
            float[] steps = { 27f, 21f };
            for (int ring = 0; ring < radii.Length; ring++)
                for (float a = -150f; a <= 150.1f; a += steps[ring])
                {
                    Vector2 spot = plaza + Rotate(inland, a + Range(-5f, 5f)) * (radii[ring] + Range(-0.3f, 0.3f));
                    if (!Buildable(site, seed, spot) || Blocked(spot, 1.3f) || SegmentDistance(spot, landing, plaza) < 1.9f) continue;
                    Vector2 toPlaza = (plaza - spot).normalized;
                    Vector2 facing = Rotate(toPlaza, Range(-12f, 12f));
                    bool tall = rng.NextDouble() < 0.6;
                    var style = new HouseStyle
                    {
                        Width = Range(1.6f, 2.2f), Depth = Range(1.4f, 1.8f), GroundStorey = 1.1f, UpperStorey = tall ? 0.85f : 0f,
                        Wall = Plaster[rng.Next(Plaster.Length)], Roof = RoofTiles[rng.Next(RoofTiles.Length)],
                        Shutter = ShutterPaint[rng.Next(ShutterPaint.Length)], Timbered = rng.NextDouble() < 0.5,
                        Chimney = rng.NextDouble() < 0.65, FlowerBoxes = rng.NextDouble() < 0.4
                    };
                    House(spot, new Vector3(facing.x, 0f, facing.y), style, "Town House", out _, out _);
                    occupied.Add(new Vector3(spot.x, 1.3f, spot.y));
                    if (++built >= 12) return;
                }
        }

        // ───────────────────────────── Harbour fort ─────────────────────────────

        /// <summary>The town's stone keep on the waterfront beside the square: door onto the town, a cannon
        /// battery on its roof covering the harbour. Built at village scale, in the same stone as the houses.</summary>
        private void BuildFort(Vector2 plaza, Vector2 inland, Vector3 defendedWaters)
        {
            const float w = 2.6f, d = 2.6f, wallHeight = 2.5f;
            // Seaward of the tavern or the harbour master, whichever flank stands on dry ground.
            Vector2 spot = plaza + Rotate(inland, -108f) * 6.6f;
            foreach (Vector2 candidate in new[]
                { spot, plaza + Rotate(inland, 108f) * 6.6f, plaza + Rotate(inland, -95f) * 6.8f, plaza + Rotate(inland, 95f) * 6.8f })
            {
                if (H(candidate) < 0.6f || Blocked(candidate, 1.9f)) continue;
                spot = candidate;
                break;
            }
            Vector2 toPlaza = (plaza - spot).normalized;
            Vector2 seaward = -inland;

            // Footprint: rest on the lowest corner so the masonry reaches the ground all round.
            float lowest = H(spot);
            for (int i = 0; i < 8; i++) lowest = Mathf.Min(lowest, H(spot + Rotate(Vector2.up, i * 45f) * (w * 0.72f)));
            float floor = lowest - 0.15f;
            float g = Mathf.Clamp(H(spot + toPlaza * (d * 0.5f + 0.35f)) - floor, 0.15f, 1.2f);
            float top = g + wallHeight;

            Transform t = new GameObject("Harbour Fort").transform;
            t.SetParent(root.transform, false);
            t.SetPositionAndRotation(new Vector3(spot.x, floor, spot.y), Quaternion.LookRotation(new Vector3(toPlaza.x, 0f, toPlaza.y)));
            occupied.Add(new Vector3(spot.x, 2.1f, spot.y));

            // Masonry: battered plinth, keep walls, string course and corner turrets.
            Prim(PrimitiveType.Cube, "Fort Plinth", t, new Vector3(0f, (g + 0.35f) * 0.5f, 0f), Id, new Vector3(w + 0.3f, g + 0.35f, d + 0.3f), DarkStone, true);
            Prim(PrimitiveType.Cube, "Walls", t, new Vector3(0f, top * 0.5f, 0f), Id, new Vector3(w, top, d), Stone, true);
            Prim(PrimitiveType.Cube, "String Course", t, new Vector3(0f, g + 1.45f, 0f), Id, new Vector3(w + 0.06f, 0.07f, d + 0.06f), DarkStone);
            Prim(PrimitiveType.Cube, "Roof Deck", t, new Vector3(0f, top + 0.03f, 0f), Id, new Vector3(w + 0.04f, 0.06f, d + 0.04f), DarkStone);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    Vector3 corner = new Vector3(sx * w * 0.5f, 0f, sz * d * 0.5f);
                    float turret = top + 0.45f;
                    Prim(PrimitiveType.Cylinder, "Corner Turret", t, corner + Vector3.up * (turret * 0.5f), Id, new Vector3(0.62f, turret * 0.5f, 0.62f), Stone);
                    Prim(PrimitiveType.Cylinder, "Turret Corbel", t, corner + Vector3.up * (top + 0.02f), Id, new Vector3(0.74f, 0.06f, 0.74f), DarkStone);
                    for (int m = 0; m < 4; m++)
                    {
                        float a = m * 90f + 45f;
                        Vector3 merlon = corner + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.27f) + Vector3.up * (turret + 0.1f);
                        Prim(PrimitiveType.Cube, "Turret Merlon", t, merlon, Quaternion.Euler(0f, a, 0f), new Vector3(0.18f, 0.2f, 0.1f), Stone);
                    }
                }
            // Parapet and merlons, with embrasures left open on the seaward side for the guns.
            Vector3 seaLocal = t.InverseTransformDirection(new Vector3(seaward.x, 0f, seaward.y));
            for (int side = 0; side < 4; side++)
            {
                Quaternion face = Quaternion.Euler(0f, side * 90f, 0f);
                Vector3 outward = face * Vector3.forward;
                bool battery = Vector3.Dot(outward, seaLocal) > 0.5f;
                Prim(PrimitiveType.Cube, "Parapet", t, outward * (d * 0.5f - 0.06f) + Vector3.up * (top + 0.2f), face,
                    new Vector3(w - 0.5f, 0.34f, 0.12f), Stone);
                for (float x = -0.8f; x <= 0.81f; x += 0.4f)
                {
                    if (battery && Mathf.Abs(Mathf.Abs(x) - 0.6f) < 0.25f) continue;
                    Prim(PrimitiveType.Cube, "Merlon", t, face * new Vector3(x, top + 0.5f, d * 0.5f - 0.06f), face, new Vector3(0.22f, 0.26f, 0.14f), Stone);
                }
                // Arrow slits on every face, two storeys.
                foreach (float y in new[] { g + 0.95f, g + 1.95f })
                    foreach (float x in side == 0 && y < g + 1.4f ? new[] { -0.85f, 0.85f } : new[] { -0.6f, 0.6f })
                    {
                        Prim(PrimitiveType.Cube, "Slit Surround", t, face * new Vector3(x, y, d * 0.5f + 0.008f), face, new Vector3(0.16f, 0.44f, 0.02f), DarkStone);
                        Prim(PrimitiveType.Cube, "Arrow Slit", t, face * new Vector3(x, y, d * 0.5f + 0.02f), face, new Vector3(0.05f, 0.34f, 0.01f), new Color(0.05f, 0.05f, 0.06f));
                    }
            }

            // Gate onto the town square.
            float fz = d * 0.5f;
            Prim(PrimitiveType.Cube, "Gate Arch", t, new Vector3(0f, g + 0.56f, fz + 0.015f), Id, new Vector3(0.82f, 1.12f, 0.04f), DarkStone);
            Prim(PrimitiveType.Cube, "Keystone", t, new Vector3(0f, g + 1.14f, fz + 0.03f), Id, new Vector3(0.18f, 0.16f, 0.05f), Stone);
            Prim(PrimitiveType.Cube, "Gate", t, new Vector3(0f, g + 0.48f, fz + 0.035f), Id, new Vector3(0.62f, 0.96f, 0.025f), new Color(0.3f, 0.18f, 0.09f));
            foreach (float y in new[] { 0.2f, 0.5f, 0.8f })
                Prim(PrimitiveType.Cube, "Gate Strap", t, new Vector3(0f, g + y, fz + 0.05f), Id, new Vector3(0.62f, 0.04f, 0.008f), Iron);
            Prim(PrimitiveType.Sphere, "Gate Ring", t, new Vector3(0.18f, g + 0.46f, fz + 0.06f), Id, Vector3.one * 0.05f, Iron);
            Prim(PrimitiveType.Cube, "Gate Step", t, new Vector3(0f, g - 0.04f, fz + 0.2f), Id, new Vector3(0.9f, 0.08f, 0.4f), Stone);
            Sign(t, "HARBOUR FORT", g + 1.32f, fz + 0.04f, 1.25f);
            foreach (int sx in new[] { -1, 1 }) WallLantern(t, new Vector3(sx * 0.62f, g + 0.82f, fz));

            // Roof battery: two guns trained on the harbour. HarbourFort swings each whole pivot to aim.
            Transform[] muzzles = new Transform[2];
            Quaternion toSea = Quaternion.LookRotation(new Vector3(seaward.x, 0f, seaward.y));
            Vector3 seaRight = Vector3.Cross(Vector3.up, new Vector3(seaward.x, 0f, seaward.y));
            for (int i = 0; i < muzzles.Length; i++)
            {
                Vector3 world = t.TransformPoint(Vector3.up * (top + 0.48f)) + new Vector3(seaward.x, 0f, seaward.y) * 0.6f +
                    seaRight * (i == 0 ? -0.6f : 0.6f);
                Transform pivot = new GameObject("Fort Cannon " + (i + 1)).transform;
                pivot.SetParent(t, false);
                pivot.SetPositionAndRotation(world, toSea);
                Transform carriage = new GameObject("Cannon Carriage").transform;
                carriage.SetParent(t, false);
                carriage.SetPositionAndRotation(world, toSea);
                Prim(PrimitiveType.Cube, "Carriage", carriage, new Vector3(0f, -0.28f, -0.05f), Id, new Vector3(0.34f, 0.3f, 0.5f), DarkWood);
                foreach (int s in new[] { -1, 1 })
                    Prim(PrimitiveType.Cylinder, "Truck Wheel", carriage, new Vector3(s * 0.19f, -0.36f, 0.05f), Quaternion.Euler(0f, 0f, 90f),
                        new Vector3(0.16f, 0.02f, 0.16f), Timber);
                Prim(PrimitiveType.Cylinder, "Iron Barrel", pivot, new Vector3(0f, 0f, 0.3f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.17f, 0.38f, 0.17f), Iron);
                Prim(PrimitiveType.Cylinder, "Muzzle Ring", pivot, new Vector3(0f, 0f, 0.66f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.21f, 0.03f, 0.21f), Iron);
                Prim(PrimitiveType.Sphere, "Cascabel", pivot, new Vector3(0f, 0f, -0.1f), Id, Vector3.one * 0.12f, Iron);
                muzzles[i] = new GameObject("Muzzle").transform;
                muzzles[i].SetParent(pivot, false);
                muzzles[i].localPosition = Vector3.forward * 0.8f;
            }
            // Shot locker and colours.
            Vector3 deck = new Vector3(0f, top + 0.06f, 0f) - seaLocal * 0.65f;
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Sphere, "Roundshot", t, deck + new Vector3((i % 3 - 1) * 0.1f, 0.05f + (i == 3 ? 0.08f : 0f), i == 3 ? 0f : 0.02f), Id,
                    Vector3.one * 0.1f, Iron);
            Vector3 pole = new Vector3(-w * 0.5f, top + 0.45f, -d * 0.5f);
            Prim(PrimitiveType.Cylinder, "Flag Pole", t, pole + Vector3.up * 0.9f, Id, new Vector3(0.04f, 0.9f, 0.04f), Cream);
            Prim(PrimitiveType.Sphere, "Pole Finial", t, pole + Vector3.up * 1.82f, Id, Vector3.one * 0.06f, Brass);
            Prim(PrimitiveType.Cube, "Flag", t, pole + new Vector3(0.33f, 1.58f, 0f), Id, new Vector3(0.62f, 0.4f, 0.015f), new Color(0.15f, 0.25f, 0.55f));
            Prim(PrimitiveType.Cube, "Flag Cross", t, pole + new Vector3(0.33f, 1.58f, 0f), Id, new Vector3(0.62f, 0.07f, 0.02f), Cream);
            Prim(PrimitiveType.Cube, "Flag Cross", t, pole + new Vector3(0.24f, 1.58f, 0f), Id, new Vector3(0.07f, 0.4f, 0.02f), Cream);

            // A sentry at the gate and powder kegs against the wall.
            Transform sentry = new GameObject("Fort Sentry").transform;
            sentry.SetParent(t, false);
            sentry.localPosition = new Vector3(0.75f, DecorY(t, 0.75f, fz + 0.45f), fz + 0.45f);
            BoatshipsHumanoid.Build(sentry, new Color(0.55f, 0.14f, 0.12f), BoatshipsFirstPerson.HumanHeight, false, rng.Next(), HumanoidHat.Tricorn);
            float kx = -w * 0.5f - 0.3f;
            Barrel(t, new Vector3(kx, DecorY(t, kx, 0.35f), 0.35f), true);
            Barrel(t, new Vector3(kx, DecorY(t, kx, 0.05f), 0.05f), true);

            HarbourFort fort = t.gameObject.AddComponent<HarbourFort>();
            fort.Configure(defendedWaters, muzzles, FindAnyObjectByType<OceanWaves>());
        }

        /// <summary>Builds a house whose front (+Z) faces <paramref name="front"/>. g is the local height of the door sill.</summary>
        private Transform House(Vector2 spot, Vector3 front, HouseStyle s, string name, out float g, out Vector3 doorWorld)
        {
            float w = s.Width, d = s.Depth, gs = s.GroundStorey, us = s.UpperStorey;
            float reach = Mathf.Max(w, d) * 0.6f;
            float lowest = H(spot);
            for (int i = 0; i < 4; i++) lowest = Mathf.Min(lowest, H(spot + Rotate(Vector2.up, 45f + i * 90f) * reach));
            float floor = lowest - 0.15f;
            Vector2 front2 = new Vector2(front.x, front.z).normalized;
            g = Mathf.Clamp(H(spot + front2 * (d * 0.5f + 0.3f)) - floor, 0.15f, 0.6f);

            Transform t = new GameObject(name).transform;
            t.SetParent(root.transform, false);
            t.SetPositionAndRotation(new Vector3(spot.x, floor, spot.y), Quaternion.LookRotation(front));
            float top = g + gs + us;
            float uw = us > 0f ? w + 0.06f : w, ud = us > 0f ? d + 0.16f : d;

            Prim(PrimitiveType.Cube, "Plinth", t, new Vector3(0f, (g + 0.1f) * 0.5f, 0f), Id, new Vector3(w + 0.08f, g + 0.1f, d + 0.08f), Stone);
            Prim(PrimitiveType.Cube, "Walls", t, new Vector3(0f, (g + gs) * 0.5f, 0f), Id, new Vector3(w, g + gs, d), s.Wall, true);
            if (us > 0f)
                Prim(PrimitiveType.Cube, "Upper Walls", t, new Vector3(0f, g + gs + us * 0.5f, 0f), Id, new Vector3(uw, us, ud), s.Wall, true);

            if (s.PlankWalls)
                for (float x = -w * 0.5f + 0.2f; x < w * 0.5f - 0.1f; x += 0.22f)
                    foreach (int sz in new[] { -1, 1 })
                        Prim(PrimitiveType.Cube, "Plank Seam", t, new Vector3(x, (g + gs) * 0.5f, sz * (d * 0.5f + 0.003f)), Id,
                            new Vector3(0.015f, g + gs, 0.01f), DarkWood);

            if (s.Timbered) TimberFrame(t, w, d, uw, ud, g, gs, us);
            if (s.Quoins) Quoins(t, w, d, uw, ud, us > 0f ? g + gs : top, top);

            // Door.
            float fz = d * 0.5f;
            if (s.WideDoor)
            {
                Prim(PrimitiveType.Cube, "Workshop Opening", t, new Vector3(0f, g + 0.5f, fz + 0.004f), Id, new Vector3(0.92f, 1.0f, 0.01f), new Color(0.09f, 0.07f, 0.05f));
                Prim(PrimitiveType.Cube, "Lintel", t, new Vector3(0f, g + 1.04f, fz + 0.02f), Id, new Vector3(1.08f, 0.08f, 0.05f), Timber);
                foreach (int sx in new[] { -1, 1 })
                {
                    Prim(PrimitiveType.Cube, "Jamb", t, new Vector3(sx * 0.5f, g + 0.5f, fz + 0.02f), Id, new Vector3(0.07f, 1.0f, 0.05f), Timber);
                    Prim(PrimitiveType.Cube, "Barn Door", t, new Vector3(sx * 0.78f, g + 0.49f, fz + 0.02f), Id, new Vector3(0.46f, 0.96f, 0.03f), Plank);
                    Prim(PrimitiveType.Cube, "Door Brace", t, new Vector3(sx * 0.78f, g + 0.49f, fz + 0.037f), Quaternion.Euler(0f, 0f, sx * 25f),
                        new Vector3(0.05f, 1.0f, 0.01f), DarkWood);
                }
            }
            else
            {
                Prim(PrimitiveType.Cube, "Door Frame", t, new Vector3(0f, g + 0.46f, fz + 0.012f), Id, new Vector3(0.54f, 0.94f, 0.025f), Timber);
                Prim(PrimitiveType.Cube, "Door", t, new Vector3(0f, g + 0.42f, fz + 0.026f), Id, new Vector3(0.42f, 0.84f, 0.02f), new Color(0.36f, 0.2f, 0.1f));
                for (int i = 0; i < 2; i++)
                    Prim(PrimitiveType.Cube, "Door Strap", t, new Vector3(0f, g + 0.2f + i * 0.45f, fz + 0.038f), Id, new Vector3(0.42f, 0.03f, 0.006f), Iron);
                Prim(PrimitiveType.Sphere, "Door Handle", t, new Vector3(0.14f, g + 0.42f, fz + 0.045f), Id, Vector3.one * 0.035f, Brass);
                Prim(PrimitiveType.Cube, "Door Step", t, new Vector3(0f, g - 0.03f, fz + 0.15f), Id, new Vector3(0.66f, 0.06f, 0.3f), Stone);
            }

            // Windows.
            // Shutters (±0.345 wide) only where they clear the door frame and the wall corners.
            float groundY = g + 0.55f, sideZ = s.Timbered ? d * 0.18f : 0f;
            float groundX = Mathf.Max(w * 0.31f, 0.56f);
            bool groundShutters = groundX - 0.345f > 0.31f && groundX + 0.345f < w * 0.5f - 0.02f;
            if (!s.WideDoor)
                foreach (int sx in new[] { -1, 1 })
                    Window(t, new Vector3(sx * groundX, groundY, fz), 0f, s, true, groundShutters);
            if (d >= 1.4f)
                foreach (int sx in new[] { -1, 1 })
                    Window(t, new Vector3(sx * w * 0.5f, s.WideDoor ? g + 0.75f : groundY, sideZ), sx * 90f, s, false);
            Window(t, new Vector3(0f, s.WideDoor ? g + 0.75f : groundY, -d * 0.5f), 180f, s, false);
            if (us > 0f)
            {
                float upperY = g + gs + us * 0.5f, upperX = w * 0.3f;
                bool upperShutters = upperX - 0.345f > (s.Timbered ? BracePanel(w) + 0.02f : 0.05f) && upperX + 0.345f < uw * 0.5f - 0.02f;
                foreach (int sx in new[] { -1, 1 })
                {
                    Window(t, new Vector3(sx * upperX, upperY, ud * 0.5f), 0f, s, true, upperShutters);
                    Window(t, new Vector3(sx * uw * 0.5f, upperY, sideZ), sx * 90f, s, false);
                }
                Window(t, new Vector3(0f, upperY, -ud * 0.5f), 180f, s, false);
            }

            Roof(t, s, uw, ud, top);
            doorWorld = t.TransformPoint(new Vector3(0f, g, d * 0.5f + 0.3f));
            return t;
        }

        private void TimberFrame(Transform t, float w, float d, float uw, float ud, float g, float gs, float us)
        {
            float postHeight = gs - 0.1f;
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    Prim(PrimitiveType.Cube, "Corner Post", t, new Vector3(sx * w * 0.5f, g + 0.1f + postHeight * 0.5f, sz * d * 0.5f), Id,
                        new Vector3(0.075f, postHeight, 0.075f), Timber);
                    if (us > 0f)
                        Prim(PrimitiveType.Cube, "Corner Post", t, new Vector3(sx * uw * 0.5f, g + gs + us * 0.5f, sz * ud * 0.5f), Id,
                            new Vector3(0.07f, us, 0.07f), Timber);
                }
            Prim(PrimitiveType.Cube, "Floor Beam", t, new Vector3(0f, g + gs, 0f), Id, new Vector3(uw + 0.05f, 0.075f, ud + 0.05f), Timber);
            Prim(PrimitiveType.Cube, "Sill Beam", t, new Vector3(0f, g + 0.12f, 0f), Id, new Vector3(w + 0.03f, 0.05f, d + 0.03f), Timber);
            // Studs flanking the door and diagonal braces on the side walls.
            foreach (int sx in new[] { -1, 1 })
            {
                Prim(PrimitiveType.Cube, "Stud", t, new Vector3(sx * 0.3f, g + 0.1f + postHeight * 0.5f, d * 0.5f + 0.006f), Id,
                    new Vector3(0.05f, postHeight, 0.02f), Timber);
                float braceLength = Mathf.Sqrt(d * d * 0.25f + postHeight * postHeight);
                Prim(PrimitiveType.Cube, "Brace", t, new Vector3(sx * (w * 0.5f + 0.006f), g + 0.1f + postHeight * 0.5f, -d * 0.25f),
                    Quaternion.Euler(Mathf.Atan2(d * 0.5f, postHeight) * Mathf.Rad2Deg * sx, 0f, 0f), new Vector3(0.02f, braceLength, 0.05f), Timber);
            }
            if (us <= 0f) return;
            Prim(PrimitiveType.Cube, "Wall Plate", t, new Vector3(0f, g + gs + us, 0f), Id, new Vector3(uw + 0.04f, 0.06f, ud + 0.04f), Timber);
            float panel = BracePanel(w);
            float diagonal = Mathf.Sqrt(panel * panel * 4f + us * us);
            float angle = Mathf.Atan2(panel * 2f, us) * Mathf.Rad2Deg;
            foreach (int sz in new[] { -1, 1 })
                foreach (int lean in new[] { -1, 1 })
                    Prim(PrimitiveType.Cube, "Cross Brace", t, new Vector3(0f, g + gs + us * 0.5f, sz * (ud * 0.5f + 0.006f)),
                        Quaternion.Euler(0f, 0f, lean * angle), new Vector3(0.045f, diagonal - 0.04f, 0.02f), Timber);
        }

        /// <summary>Half-width of the X-brace panel between the upper front windows.</summary>
        private static float BracePanel(float w) => Mathf.Clamp(w * 0.3f - 0.36f, 0.1f, 0.22f);

        private void Quoins(Transform t, float w, float d, float uw, float ud, float upperFloor, float top)
        {
            bool wide = false;
            for (float y = 0.22f; y < top - 0.05f; y += 0.2f, wide = !wide)
            {
                bool upper = y > upperFloor;
                float hw = (upper ? uw : w) * 0.5f, hd = (upper ? ud : d) * 0.5f;
                foreach (int sx in new[] { -1, 1 })
                    foreach (int sz in new[] { -1, 1 })
                        Prim(PrimitiveType.Cube, "Quoin", t, new Vector3(sx * hw, y, sz * hd), Id,
                            wide ? new Vector3(0.24f, 0.13f, 0.14f) : new Vector3(0.14f, 0.13f, 0.24f), Stone);
            }
        }

        private void Window(Transform t, Vector3 position, float yaw, HouseStyle s, bool front, bool shutters = true)
        {
            Transform win = new GameObject("Window").transform;
            win.SetParent(t, false);
            win.localPosition = position;
            win.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, "Window Frame", win, new Vector3(0f, 0f, 0.012f), Id, new Vector3(0.38f, 0.34f, 0.025f), Timber);
            Prim(PrimitiveType.Cube, "Glass", win, new Vector3(0f, 0f, 0.024f), Id, new Vector3(0.3f, 0.26f, 0.01f), Glass, false, 1);
            Prim(PrimitiveType.Cube, "Mullion", win, new Vector3(0f, 0f, 0.031f), Id, new Vector3(0.025f, 0.26f, 0.008f), Timber);
            Prim(PrimitiveType.Cube, "Transom", win, new Vector3(0f, 0.02f, 0.031f), Id, new Vector3(0.3f, 0.022f, 0.008f), Timber);
            Prim(PrimitiveType.Cube, "Sill", win, new Vector3(0f, -0.185f, 0.04f), Id, new Vector3(0.44f, 0.035f, 0.08f), Stone);
            if (shutters)
                foreach (int sx in new[] { -1, 1 })
                    Prim(PrimitiveType.Cube, "Shutter", win, new Vector3(sx * 0.27f, 0f, 0.022f), Id, new Vector3(0.15f, 0.32f, 0.02f), s.Shutter);
            if (!front || !s.FlowerBoxes) return;
            Prim(PrimitiveType.Cube, "Flower Box", win, new Vector3(0f, -0.23f, 0.08f), Id, new Vector3(0.38f, 0.08f, 0.1f), DarkWood);
            Prim(PrimitiveType.Cube, "Foliage", win, new Vector3(0f, -0.18f, 0.08f), Id, new Vector3(0.36f, 0.05f, 0.08f), Leaves);
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Sphere, "Bloom", win, new Vector3(-0.13f + i * 0.087f, -0.155f, 0.08f + Range(-0.02f, 0.02f)), Id,
                    Vector3.one * 0.065f, Blooms[rng.Next(Blooms.Length)]);
        }

        private void Roof(Transform t, HouseStyle s, float wr, float ud, float top)
        {
            float half = wr * 0.5f, hw = half + 0.14f, dr = ud + 0.3f;
            float ridgeY = top + half;
            Color dark = new Color(s.Roof.r * 0.75f, s.Roof.g * 0.75f, s.Roof.b * 0.75f);
            Prim(PrimitiveType.Cube, "Gable", t, new Vector3(0f, top, 0f), Quaternion.Euler(0f, 0f, 45f), new Vector3(wr / 1.4142f, wr / 1.4142f, ud), s.Wall);
            if (s.Timbered)
                foreach (int sz in new[] { -1, 1 })
                    Prim(PrimitiveType.Cube, "King Post", t, new Vector3(0f, top + half * 0.5f, sz * (ud * 0.5f + 0.006f)), Id,
                        new Vector3(0.05f, half, 0.02f), Timber);
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 eave = new Vector3(side * hw, top - 0.14f, 0f), ridge = new Vector3(0f, ridgeY, 0f);
                Vector3 normal = new Vector3(side * 0.7071f, 0.7071f, 0f);
                Quaternion slope = Quaternion.Euler(0f, 0f, -side * 45f);
                float length = Vector3.Distance(eave, ridge) + 0.05f;
                Prim(PrimitiveType.Cube, "Roof", t, (eave + ridge) * 0.5f + normal * 0.035f, slope, new Vector3(length, 0.07f, dr), s.Roof);
                foreach (float f in new[] { 0.15f, 0.38f, 0.61f, 0.84f })
                    Prim(PrimitiveType.Cube, "Tile Row", t, Vector3.Lerp(eave, ridge, f) + normal * 0.075f, slope, new Vector3(0.035f, 0.025f, dr), dark);
                foreach (int sz in new[] { -1, 1 })
                    Prim(PrimitiveType.Cube, "Bargeboard", t, (eave + ridge) * 0.5f + normal * 0.02f + Vector3.forward * (sz * (dr * 0.5f - 0.015f)), slope,
                        new Vector3(length, 0.1f, 0.035f), Timber);
            }
            Prim(PrimitiveType.Cube, "Ridge", t, new Vector3(0f, ridgeY + 0.045f, 0f), Quaternion.Euler(0f, 0f, 45f), new Vector3(0.1f, 0.1f, dr + 0.02f), dark);
            if (!s.Chimney) return;
            float cx = wr * 0.22f, cz = -ud * 0.2f;
            float bottom = top + wr * 0.12f, crown = top + wr * 0.28f + 0.4f;
            Prim(PrimitiveType.Cube, "Chimney", t, new Vector3(cx, (bottom + crown) * 0.5f, cz), Id, new Vector3(0.24f, crown - bottom, 0.24f), Stone);
            Prim(PrimitiveType.Cube, "Chimney Cap", t, new Vector3(cx, crown, cz), Id, new Vector3(0.3f, 0.05f, 0.3f), DarkStone);
            Prim(PrimitiveType.Cylinder, "Chimney Pot", t, new Vector3(cx, crown + 0.07f, cz), Id, new Vector3(0.09f, 0.05f, 0.09f), RoofTiles[0]);
        }

        // ───────────────────────────── Building details ─────────────────────────────

        private void Sign(Transform t, string text, float y, float z, float width)
        {
            Prim(PrimitiveType.Cube, "Sign Trim", t, new Vector3(0f, y, z - 0.004f), Id, new Vector3(width + 0.06f, 0.22f, 0.03f), Brass);
            Prim(PrimitiveType.Cube, "Sign Board", t, new Vector3(0f, y, z + 0.008f), Id, new Vector3(width, 0.18f, 0.03f), DarkWood);
            GameObject label = new GameObject("Sign Text");
            label.transform.SetParent(t, false);
            label.transform.localPosition = new Vector3(0f, y, z + 0.03f);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh mesh = label.AddComponent<TextMesh>();
            mesh.text = text; mesh.font = BoatshipsUI.Font; mesh.fontSize = 64; mesh.characterSize = 0.02f;
            mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center; mesh.color = new Color(1f, 0.9f, 0.62f);
            label.GetComponent<MeshRenderer>().sharedMaterial = BoatshipsUI.Font.material;
        }

        private void HangingSign(Transform t, float x, float y, float z)
        {
            Prim(PrimitiveType.Cube, "Sign Bracket", t, new Vector3(x, y, z + 0.24f), Id, new Vector3(0.025f, 0.025f, 0.5f), Iron);
            Prim(PrimitiveType.Cube, "Bracket Stay", t, new Vector3(x, y - 0.12f, z + 0.1f), Quaternion.Euler(-50f, 0f, 0f), new Vector3(0.02f, 0.02f, 0.3f), Iron);
            foreach (float cz in new[] { 0.28f, 0.48f })
                Prim(PrimitiveType.Cube, "Sign Chain", t, new Vector3(x, y - 0.06f, z + cz), Id, new Vector3(0.01f, 0.1f, 0.01f), Iron);
            Prim(PrimitiveType.Cube, "Hanging Sign", t, new Vector3(x, y - 0.26f, z + 0.38f), Id, new Vector3(0.03f, 0.3f, 0.34f), DarkWood);
            Prim(PrimitiveType.Cube, "Sign Rim", t, new Vector3(x, y - 0.26f, z + 0.38f), Id, new Vector3(0.026f, 0.33f, 0.37f), Brass);
            Prim(PrimitiveType.Cylinder, "Tankard Emblem", t, new Vector3(x, y - 0.27f, z + 0.36f), Quaternion.Euler(0f, 0f, 90f),
                new Vector3(0.12f, 0.03f, 0.12f), Brass);
            Prim(PrimitiveType.Cube, "Tankard Handle", t, new Vector3(x, y - 0.27f, z + 0.45f), Id, new Vector3(0.06f, 0.08f, 0.025f), Brass);
        }

        private void WallLantern(Transform t, Vector3 at)
        {
            Prim(PrimitiveType.Cube, "Lantern Arm", t, at + new Vector3(0f, 0.08f, 0.07f), Id, new Vector3(0.02f, 0.02f, 0.14f), Iron);
            Prim(PrimitiveType.Cube, "Wall Lantern", t, at + new Vector3(0f, 0f, 0.14f), Id, new Vector3(0.08f, 0.11f, 0.08f), LanternLight, false, 2);
            Prim(PrimitiveType.Cube, "Lantern Cap", t, at + new Vector3(0f, 0.07f, 0.14f), Quaternion.Euler(0f, 45f, 0f), new Vector3(0.1f, 0.03f, 0.1f), Iron);
        }

        private void Awning(Transform t, float width, float y, float z, Color stripe)
        {
            const int stripes = 8;
            float sw = width / stripes;
            for (int i = 0; i < stripes; i++)
                Prim(PrimitiveType.Cube, "Awning", t, new Vector3(-width * 0.5f + sw * (i + 0.5f), y, z + 0.3f), Quaternion.Euler(18f, 0f, 0f),
                    new Vector3(sw + 0.002f, 0.025f, 0.64f), i % 2 == 0 ? stripe : Cream);
            for (int i = 0; i < stripes; i++)
                Prim(PrimitiveType.Cube, "Valance", t, new Vector3(-width * 0.5f + sw * (i + 0.5f), y - 0.14f, z + 0.605f), Id,
                    new Vector3(sw + 0.002f, 0.09f, 0.015f), i % 2 == 0 ? stripe : Cream);
            foreach (int sx in new[] { -1, 1 })
                Prim(PrimitiveType.Cube, "Awning Rod", t, new Vector3(sx * width * 0.5f, y - 0.02f, z + 0.3f), Quaternion.Euler(18f, 0f, 0f),
                    new Vector3(0.02f, 0.02f, 0.64f), Iron);
        }

        private void Balcony(Transform t, float floorY, float z)
        {
            Prim(PrimitiveType.Cube, "Balcony", t, new Vector3(0f, floorY + 0.02f, z + 0.17f), Id, new Vector3(0.9f, 0.05f, 0.34f), DarkWood);
            Prim(PrimitiveType.Cube, "Balcony Rail", t, new Vector3(0f, floorY + 0.32f, z + 0.33f), Id, new Vector3(0.9f, 0.03f, 0.03f), DarkWood);
            foreach (int sx in new[] { -1, 1 })
                Prim(PrimitiveType.Cube, "Balcony Rail", t, new Vector3(sx * 0.44f, floorY + 0.32f, z + 0.17f), Id, new Vector3(0.03f, 0.03f, 0.34f), DarkWood);
            for (int i = 0; i < 7; i++)
                Prim(PrimitiveType.Cube, "Baluster", t, new Vector3(-0.39f + i * 0.13f, floorY + 0.18f, z + 0.33f), Id, new Vector3(0.025f, 0.28f, 0.025f), DarkWood);
            Prim(PrimitiveType.Cube, "Balcony Door Frame", t, new Vector3(0f, floorY + 0.36f, z + 0.004f), Id, new Vector3(0.42f, 0.7f, 0.02f), Timber);
            Prim(PrimitiveType.Cube, "Balcony Door", t, new Vector3(0f, floorY + 0.34f, z + 0.014f), Id, new Vector3(0.32f, 0.62f, 0.02f), Glass, false, 1);
            Prim(PrimitiveType.Cube, "Balcony Mullion", t, new Vector3(0f, floorY + 0.34f, z + 0.026f), Id, new Vector3(0.025f, 0.62f, 0.008f), Timber);
        }

        private void Flagpole(Transform t, float x, float z)
        {
            float y = DecorY(t, x, z);
            Prim(PrimitiveType.Cylinder, "Flag Pole", t, new Vector3(x, y + 1.4f, z), Id, new Vector3(0.04f, 1.4f, 0.04f), Cream, true);
            Prim(PrimitiveType.Sphere, "Pole Finial", t, new Vector3(x, y + 2.82f, z), Id, Vector3.one * 0.06f, Brass);
            Prim(PrimitiveType.Cube, "Flag", t, new Vector3(x + 0.31f, y + 2.56f, z), Id, new Vector3(0.6f, 0.38f, 0.015f), new Color(0.15f, 0.25f, 0.55f));
            Prim(PrimitiveType.Cube, "Flag Cross", t, new Vector3(x + 0.31f, y + 2.56f, z), Id, new Vector3(0.6f, 0.07f, 0.02f), Cream);
            Prim(PrimitiveType.Cube, "Flag Cross", t, new Vector3(x + 0.22f, y + 2.56f, z), Id, new Vector3(0.07f, 0.38f, 0.02f), Cream);
        }

        private void Anchor(Transform t, float x, float z)
        {
            float y = DecorY(t, x, z);
            Transform anchor = new GameObject("Anchor").transform;
            anchor.SetParent(t, false);
            anchor.localPosition = new Vector3(x, y, z);
            anchor.localRotation = Quaternion.Euler(-12f, 20f, 0f);
            Prim(PrimitiveType.Cube, "Shank", anchor, new Vector3(0f, 0.32f, 0f), Id, new Vector3(0.05f, 0.56f, 0.05f), Iron, true);
            Prim(PrimitiveType.Cube, "Stock", anchor, new Vector3(0f, 0.54f, 0f), Id, new Vector3(0.36f, 0.04f, 0.04f), DarkWood);
            Prim(PrimitiveType.Cylinder, "Ring", anchor, new Vector3(0f, 0.63f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.08f, 0.01f, 0.08f), Iron);
            foreach (int s in new[] { -1, 1 })
            {
                Prim(PrimitiveType.Cube, "Arm", anchor, new Vector3(s * 0.11f, 0.1f, 0f), Quaternion.Euler(0f, 0f, s * 55f), new Vector3(0.05f, 0.26f, 0.05f), Iron);
                Prim(PrimitiveType.Cube, "Fluke", anchor, new Vector3(s * 0.2f, 0.2f, 0f), Quaternion.Euler(0f, 0f, s * 20f), new Vector3(0.09f, 0.1f, 0.03f), Iron);
            }
        }

        private void BoatFrame(Transform t, float x, float z)
        {
            float y = DecorY(t, x, z);
            Transform boat = new GameObject("Boat Under Construction").transform;
            boat.SetParent(t, false);
            boat.localPosition = new Vector3(x, y, z);
            boat.localRotation = Quaternion.Euler(0f, 90f, 0f);
            foreach (float bz in new[] { -0.45f, 0.45f })
                Prim(PrimitiveType.Cube, "Keel Block", boat, new Vector3(0f, 0.06f, bz), Id, new Vector3(0.3f, 0.12f, 0.12f), DarkWood, true);
            Prim(PrimitiveType.Cube, "Keel", boat, new Vector3(0f, 0.15f, 0f), Id, new Vector3(0.06f, 0.06f, 1.4f), Plank);
            Prim(PrimitiveType.Cube, "Stem", boat, new Vector3(0f, 0.38f, 0.72f), Quaternion.Euler(-20f, 0f, 0f), new Vector3(0.06f, 0.5f, 0.06f), Plank);
            Prim(PrimitiveType.Cube, "Sternpost", boat, new Vector3(0f, 0.34f, -0.7f), Quaternion.Euler(10f, 0f, 0f), new Vector3(0.06f, 0.4f, 0.06f), Plank);
            for (int i = 0; i < 5; i++)
            {
                float rz = -0.5f + i * 0.25f;
                float spread = 0.26f - Mathf.Abs(rz) * 0.18f;
                foreach (int s in new[] { -1, 1 })
                    Prim(PrimitiveType.Cube, "Rib", boat, new Vector3(s * spread, 0.35f, rz), Quaternion.Euler(0f, 0f, s * 18f), new Vector3(0.035f, 0.42f, 0.035f), Plank);
                Prim(PrimitiveType.Cube, "Floor Timber", boat, new Vector3(0f, 0.18f, rz), Id, new Vector3(spread * 2f, 0.035f, 0.035f), Plank);
            }
            foreach (int s in new[] { -1, 1 })
                Prim(PrimitiveType.Cube, "Strake", boat, new Vector3(s * 0.26f, 0.24f, 0f), Id, new Vector3(0.025f, 0.08f, 1.1f), new Color(0.6f, 0.44f, 0.26f));
        }

        private void Sawhorse(Transform t, Vector3 at)
        {
            Prim(PrimitiveType.Cube, "Sawhorse Beam", t, at + new Vector3(0f, 0.32f, 0f), Id, new Vector3(0.6f, 0.06f, 0.06f), Plank, true);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    Prim(PrimitiveType.Cube, "Sawhorse Leg", t, at + new Vector3(sx * 0.24f, 0.16f, sz * 0.07f), Quaternion.Euler(sz * 15f, 0f, 0f),
                        new Vector3(0.035f, 0.34f, 0.035f), DarkWood);
            Prim(PrimitiveType.Cube, "Saw", t, at + new Vector3(0.05f, 0.38f, 0f), Quaternion.Euler(0f, 30f, 0f), new Vector3(0.28f, 0.01f, 0.07f), Steel);
        }

        private static readonly Color Steel = new Color(0.7f, 0.72f, 0.75f);

        private void Bench(Transform t, Vector3 at, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, "Bench Seat", t, at + r * new Vector3(0f, 0.22f, 0f), r, new Vector3(0.62f, 0.04f, 0.17f), Plank, true);
            foreach (int s in new[] { -1, 1 })
                Prim(PrimitiveType.Cube, "Bench Leg", t, at + r * new Vector3(s * 0.25f, 0.1f, 0f), r, new Vector3(0.04f, 0.2f, 0.15f), DarkWood);
        }

        private void Barrel(Transform parent, Vector3 bottom, bool upright, float yaw = 0f)
        {
            Quaternion r = upright ? Id : Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, 90f);
            Vector3 center = bottom + Vector3.up * (upright ? 0.15f : 0.12f);
            Prim(PrimitiveType.Cylinder, "Barrel", parent, center, r, new Vector3(0.24f, 0.15f, 0.24f), new Color(0.48f, 0.32f, 0.16f), true);
            foreach (float off in new[] { -0.09f, 0.09f })
                Prim(PrimitiveType.Cylinder, "Barrel Hoop", parent, center + r * new Vector3(0f, off, 0f), r, new Vector3(0.255f, 0.012f, 0.255f), Iron);
        }

        private void Crate(Transform parent, Vector3 bottom, float size, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, "Crate", parent, bottom + Vector3.up * (size * 0.5f), r, Vector3.one * size, new Color(0.62f, 0.47f, 0.27f), true);
            foreach (float y in new[] { 0.03f, size - 0.03f })
                Prim(PrimitiveType.Cube, "Crate Band", parent, bottom + Vector3.up * y, r, new Vector3(size + 0.01f, 0.035f, size + 0.01f), DarkWood);
        }

        private void Sack(Transform parent, Vector3 bottom)
        {
            Prim(PrimitiveType.Sphere, "Sack", parent, bottom + Vector3.up * 0.1f, Quaternion.Euler(0f, Range(0f, 90f), Range(-8f, 8f)),
                new Vector3(0.2f, 0.22f, 0.18f), Canvas);
            Prim(PrimitiveType.Sphere, "Sack Tie", parent, bottom + Vector3.up * 0.21f, Id, new Vector3(0.06f, 0.05f, 0.06f), Rope);
        }

        // ───────────────────────────── People ─────────────────────────────

        private void BuildPeople(Vector2 plaza, Vector2 inland, Vector2 landing, Vector2 sideways)
        {
            for (int i = 0; i < 6; i++)
            {
                GameObject walker = new GameObject("Townsperson");
                walker.transform.SetParent(root.transform, false);
                Color shirt = Color.HSVToRGB((float)rng.NextDouble(), 0.45f, 0.75f);
                BoatshipsHumanoid.Build(walker.transform, shirt, BoatshipsFirstPerson.HumanHeight * Range(0.92f, 1.06f), false, rng.Next());
                walker.AddComponent<TownWalker>().Configure(site, seed, plaza, 0.85f, 1.55f, rng.Next());
            }
            // Dock hands by the cargo and the drying nets.
            Stand(landing + sideways * 1.15f + inland * 0.1f, Yaw(sideways), new Color(0.75f, 0.72f, 0.62f), HumanoidHat.Bandana);
            Stand(landing - sideways * 2.0f + inland * 1.1f, Yaw(-sideways), new Color(0.35f, 0.42f, 0.5f), HumanoidHat.StrawHat);
        }

        private void Stand(Vector2 p, float yaw, Color shirt, HumanoidHat hat)
        {
            float h = H(p);
            if (h < 0.45f) return;
            Transform person = Group("Dock Hand", new Vector3(p.x, h, p.y), yaw);
            BoatshipsHumanoid.Build(person, shirt, BoatshipsFirstPerson.HumanHeight, false, rng.Next(), hat);
        }

        // ───────────────────────────── Helpers ─────────────────────────────

        private float H(Vector2 p) => Ground(site, seed, p);
        private Vector3 Ground3(Vector2 p) => new Vector3(p.x, H(p), p.y);
        private float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        private static float Yaw(Vector2 direction) => Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;

        /// <summary>Terrain height under a house-local point, in the house's local space.</summary>
        private float DecorY(Transform t, float x, float z)
        {
            Vector3 world = t.TransformPoint(new Vector3(x, 0f, z));
            return H(new Vector2(world.x, world.z)) - t.position.y;
        }

        private bool Blocked(Vector2 spot, float radius)
        {
            foreach (Vector3 o in occupied)
                if ((new Vector2(o.x, o.z) - spot).sqrMagnitude < (o.y + radius) * (o.y + radius)) return true;
            return false;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private Transform Group(string name, Vector3 position, float yaw)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(root.transform, false);
            group.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return group;
        }

        private void Beam(Transform parent, string name, Vector3 from, Vector3 to, float thickness, Color color)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.0001f) return;
            Prim(PrimitiveType.Cube, name, parent, (from + to) * 0.5f, Quaternion.LookRotation(delta),
                new Vector3(thickness, thickness, delta.magnitude), color);
        }

        private GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 position, Quaternion rotation,
            Vector3 scale, Color color, bool collider = false, int glow = 0)
        {
            GameObject part = BoatshipsPrimitives.Create(type, collider);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = Material(color, glow);
            return part;
        }

        /// <summary>One material per colour (and glow level), shared by all towns, so the many small parts share materials.</summary>
        private Material Material(Color color, int glow)
        {
            Color32 c = color;
            long key = ((long)c.r << 24) | ((long)c.g << 16) | ((long)c.b << 8) | c.a | ((long)glow << 32);
            if (materials.TryGetValue(key, out Material cached) && cached != null) return cached;
            Material material = new Material(BoatshipsHumanoid.MaterialFor(color)) { name = "Town" };
            if (glow > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * (glow == 1 ? 0.35f : 1.4f));
            }
            materials[key] = material;
            return material;
        }

        private void OnDestroy()
        {
            Harbours.Remove(this);
            Clear();
        }
    }

    /// <summary>Ambient townsperson strolling around the square. Walks between points on a ring, so the
    /// path never crosses the well in the middle.</summary>
    public sealed class TownWalker : MonoBehaviour
    {
        private ArchipelagoWorld.IslandSite site;
        private int seed;
        private Vector2 home;
        private float innerRadius, outerRadius;
        private float angle;
        private Vector2 position;
        private Vector2 target;
        private float waitUntil;
        private System.Random random;

        public void Configure(ArchipelagoWorld.IslandSite island, int worldSeed, Vector2 plaza, float inner, float outer, int randomSeed)
        {
            site = island; seed = worldSeed; home = plaza; innerRadius = inner; outerRadius = outer;
            random = new System.Random(randomSeed);
            angle = (float)random.NextDouble() * 360f;
            position = PointAt(angle);
            target = PickPoint();
            Place();
        }

        private Vector2 PointAt(float degrees)
        {
            float radius = Mathf.Lerp(innerRadius, outerRadius, (float)random.NextDouble());
            float r = degrees * Mathf.Deg2Rad;
            return home + new Vector2(Mathf.Sin(r), Mathf.Cos(r)) * radius;
        }

        private Vector2 PickPoint()
        {
            for (int i = 0; i < 10; i++)
            {
                float next = angle + (random.Next(2) == 0 ? -1f : 1f) * (35f + (float)random.NextDouble() * 55f);
                Vector2 point = PointAt(next);
                if (BoatshipsTown.Ground(site, seed, point) <= 0.6f) continue;
                angle = next;
                return point;
            }
            return position;
        }

        private void Update()
        {
            if (random == null || Time.time < waitUntil) return;
            Vector2 toTarget = target - position;
            if (toTarget.magnitude < 0.05f)
            {
                waitUntil = Time.time + 1f + (float)random.NextDouble() * 3f;
                target = PickPoint();
                return;
            }
            position += toTarget.normalized * Mathf.Min(toTarget.magnitude, 0.55f * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(toTarget.x, toTarget.y) * Mathf.Rad2Deg, 0f);
            Place();
        }

        private void Place() =>
            transform.position = new Vector3(position.x, BoatshipsTown.Ground(site, seed, position), position.y);
    }
}
