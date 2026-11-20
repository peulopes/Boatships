using System;
using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    public enum TownService { Tavern, Store, Shipwright, Harbor }

    /// <summary>Builds a small walkable port town (pier, plaza, four service buildings, props and townsfolk)
    /// on top of a port island's procedural terrain. Everything is destroyed by Clear().</summary>
    public sealed class BoatshipsTown : MonoBehaviour
    {
        private static readonly string[] Signs = { "TAVERN", "GENERAL STORE", "SHIPWRIGHT", "HARBOR MASTER" };
        private static readonly string[] Prompts =
            { "Enter the tavern", "Enter the general store", "Visit the shipwright", "Speak to the harbor master" };
        private static readonly Color[] Walls =
        {
            new Color(0.78f, 0.62f, 0.42f), new Color(0.86f, 0.8f, 0.64f), new Color(0.6f, 0.45f, 0.3f), new Color(0.82f, 0.82f, 0.78f)
        };

        private GameObject root;
        private readonly List<Material> materials = new List<Material>();

        public bool Built => root != null;

        /// <summary>Builds the town. dock is where the ship is moored. Returns the doors to register with the
        /// first-person player, plus the spawn point and facing.</summary>
        public List<FpsInteractable> Build(ArchipelagoWorld.IslandSite site, int seed, Vector3 dock,
            Action<TownService> use, out Vector3 spawn, out float facing)
        {
            Clear();
            var doors = new List<FpsInteractable>();
            root = new GameObject("Port Town " + site.Cell);
            Vector2 center = site.Center;
            Vector2 dock2 = new Vector2(dock.x, dock.z);
            Vector2 inland = (center - dock2).sqrMagnitude > 0.01f ? (center - dock2).normalized : Vector2.up;
            Vector2 sideways = new Vector2(inland.y, -inland.x);

            // Pier: starts beside the moored ship and runs up onto the beach.
            Vector2 pierStart = dock2 + sideways * 1.5f;
            Vector2 landing = pierStart;
            for (int i = 0; i < 200; i++)
            {
                landing = pierStart + inland * (i * 0.25f);
                if (Ground(site, seed, landing) > 0.55f) break;
            }
            Vector2 pierEnd = landing + inland * 0.6f;
            Vector3 pierA = new Vector3(pierStart.x, 0.42f, pierStart.y);
            Vector3 pierB = new Vector3(pierEnd.x, Mathf.Max(0.42f, Ground(site, seed, pierEnd) + 0.02f), pierEnd.y);
            Box("Pier", (pierA + pierB) * 0.5f, Quaternion.LookRotation(pierB - pierA),
                new Vector3(0.9f, 0.1f, Vector3.Distance(pierA, pierB) + 0.4f), new Color(0.45f, 0.32f, 0.18f), true);
            for (float t = 0f; t <= 1f; t += 0.2f)
            {
                Vector3 post = Vector3.Lerp(pierA, pierB, t);
                foreach (int s in new[] { -1, 1 })
                {
                    Vector3 offset = new Vector3(sideways.x, 0f, sideways.y) * 0.42f * s;
                    Box("Pier Post", post + offset - Vector3.up * 0.35f, Quaternion.identity, new Vector3(0.08f, 0.9f, 0.08f),
                        new Color(0.32f, 0.22f, 0.12f), false);
                }
            }

            Vector2 plaza = landing + inland * 3f;
            Box("Plaza", new Vector3(plaza.x, Ground(site, seed, plaza) + 0.01f, plaza.y), Quaternion.identity,
                new Vector3(3.4f, 0.04f, 3.4f), new Color(0.62f, 0.58f, 0.52f), false);

            float[] angles = { -95f, -38f, 38f, 95f };
            for (int i = 0; i < 4; i++)
            {
                TownService service = i == 0 ? TownService.Tavern : i == 1 ? TownService.Store :
                    i == 2 ? TownService.Shipwright : TownService.Harbor;
                Vector2 direction = Rotate(inland, angles[i]);
                Vector2 spot = plaza + direction * 3.1f;
                for (int step = 0; step < 12 && !Buildable(site, seed, spot); step++) spot += inland * 0.4f;
                Vector2 toPlaza = (plaza - spot).normalized;
                Vector3 front = new Vector3(toPlaza.x, 0f, toPlaza.y);
                Vector3 door = Building(site, seed, spot, front, Walls[i], Signs[i], i);
                TownService chosen = service;
                doors.Add(new FpsInteractable
                {
                    Position = () => door, Prompt = Prompts[i], Radius = 0.9f, Use = () => use(chosen)
                });
            }

            // The moored ship itself: step back aboard to set sail.
            Vector3 boarding = pierA;
            doors.Add(new FpsInteractable
            {
                Position = () => boarding, Prompt = "The docks — leave town", Radius = 0.9f, Use = () => use(TownService.Harbor)
            });

            var random = new System.Random(site.DetailSeed);
            for (int i = 0; i < 6; i++)
            {
                Vector2 spot = plaza + Rotate(inland, (float)random.NextDouble() * 360f) * (1.9f + (float)random.NextDouble() * 0.5f);
                bool barrel = random.Next(2) == 0;
                float h = Ground(site, seed, spot);
                if (h < 0.4f) continue;
                if (barrel) Cylinder("Barrel", new Vector3(spot.x, h + 0.14f, spot.y), new Vector3(0.22f, 0.14f, 0.22f), new Color(0.45f, 0.3f, 0.15f));
                else Box("Crate", new Vector3(spot.x, h + 0.13f, spot.y), Quaternion.Euler(0f, (float)random.NextDouble() * 90f, 0f),
                    Vector3.one * 0.26f, new Color(0.6f, 0.45f, 0.25f), true);
            }
            for (int i = 0; i < 2; i++)
            {
                Vector2 spot = landing + sideways * (i == 0 ? 0.9f : -0.9f) + inland * 0.8f;
                float h = Ground(site, seed, spot);
                Cylinder("Lamp Post", new Vector3(spot.x, h + 0.4f, spot.y), new Vector3(0.05f, 0.4f, 0.05f), new Color(0.15f, 0.15f, 0.15f));
                Box("Lamp", new Vector3(spot.x, h + 0.85f, spot.y), Quaternion.identity, Vector3.one * 0.1f, new Color(1f, 0.85f, 0.45f), false);
            }
            for (int i = 0; i < 4; i++)
            {
                GameObject walker = new GameObject("Townsperson");
                walker.transform.SetParent(root.transform, false);
                Color shirt = Color.HSVToRGB((float)random.NextDouble(), 0.45f, 0.75f);
                BoatshipsHumanoid.Build(walker.transform, shirt, BoatshipsFirstPerson.HumanHeight, false);
                walker.AddComponent<TownWalker>().Configure(site, seed, plaza, 2.6f, random.Next());
            }

            Vector2 spawn2 = Vector2.Lerp(pierStart, landing, 0.5f);
            spawn = new Vector3(spawn2.x, 0.6f, spawn2.y);
            facing = Mathf.Atan2(inland.x, inland.y) * Mathf.Rad2Deg;
            return doors;
        }

        public void Clear()
        {
            if (root != null) Destroy(root);
            root = null;
            foreach (Material material in materials) if (material != null) Destroy(material);
            materials.Clear();
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

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private Vector3 Building(ArchipelagoWorld.IslandSite site, int seed, Vector2 spot, Vector3 front, Color wall, string sign, int index)
        {
            const float width = 2f, height = 1.35f, depth = 1.7f;
            float lowest = float.MaxValue;
            for (int i = 0; i < 4; i++)
                lowest = Mathf.Min(lowest, Ground(site, seed, spot + Rotate(Vector2.up, 45f + i * 90f) * 1.2f));
            lowest = Mathf.Min(lowest, Ground(site, seed, spot));
            float floor = lowest - 0.15f;
            Quaternion rotation = Quaternion.LookRotation(front);
            Vector3 basePoint = new Vector3(spot.x, floor, spot.y);
            GameObject house = new GameObject(sign);
            house.transform.SetParent(root.transform, false);
            house.transform.SetPositionAndRotation(basePoint, rotation);
            Transform t = house.transform;
            Local(t, PrimitiveType.Cube, "Walls", new Vector3(0f, height * 0.5f + 0.15f, 0f), Quaternion.identity,
                new Vector3(width, height + 0.3f, depth), wall, true);
            float roofSize = width / 1.414f * 1.08f;
            Local(t, PrimitiveType.Cube, "Roof", new Vector3(0f, height + 0.3f, 0f), Quaternion.Euler(0f, 0f, 45f),
                new Vector3(roofSize, roofSize, depth + 0.25f), new Color(0.55f, 0.2f, 0.14f), false);
            Local(t, PrimitiveType.Cube, "Door", new Vector3(0f, 0.15f + 0.42f, depth * 0.5f + 0.01f), Quaternion.identity,
                new Vector3(0.42f, 0.84f, 0.04f), new Color(0.3f, 0.18f, 0.09f), false);
            Local(t, PrimitiveType.Cube, "Window", new Vector3(-0.62f, 0.15f + 0.75f, depth * 0.5f + 0.01f), Quaternion.identity,
                new Vector3(0.32f, 0.28f, 0.03f), new Color(1f, 0.85f, 0.5f), false);
            Local(t, PrimitiveType.Cube, "Window", new Vector3(0.62f, 0.15f + 0.75f, depth * 0.5f + 0.01f), Quaternion.identity,
                new Vector3(0.32f, 0.28f, 0.03f), new Color(1f, 0.85f, 0.5f), false);
            Local(t, PrimitiveType.Cube, "Sign Board", new Vector3(0f, 0.15f + 1.12f, depth * 0.5f + 0.04f), Quaternion.identity,
                new Vector3(1.3f, 0.26f, 0.04f), new Color(0.25f, 0.16f, 0.08f), false);
            GameObject label = new GameObject("Sign Text");
            label.transform.SetParent(t, false);
            label.transform.localPosition = new Vector3(0f, 0.15f + 1.12f, depth * 0.5f + 0.07f);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = sign; text.font = BoatshipsUI.Font; text.fontSize = 64; text.characterSize = 0.022f;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = new Color(1f, 0.92f, 0.7f);
            label.GetComponent<MeshRenderer>().sharedMaterial = BoatshipsUI.Font.material;
            return house.transform.TransformPoint(new Vector3(0f, 0.15f, depth * 0.5f + 0.3f));
        }

        private void Local(Transform parent, PrimitiveType type, string name, Vector3 position, Quaternion rotation,
            Vector3 scale, Color color, bool collider)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            if (!collider) Destroy(part.GetComponent<Collider>());
            part.GetComponent<Renderer>().sharedMaterial = Material(color);
        }

        private void Box(string name, Vector3 position, Quaternion rotation, Vector3 scale, Color color, bool collider)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(root.transform, false);
            part.transform.SetPositionAndRotation(position, rotation);
            part.transform.localScale = scale;
            if (!collider) Destroy(part.GetComponent<Collider>());
            part.GetComponent<Renderer>().sharedMaterial = Material(color);
        }

        private void Cylinder(string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            part.name = name;
            part.transform.SetParent(root.transform, false);
            part.transform.position = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = Material(color);
        }

        private Material Material(Color color)
        {
            Material material = new Material(BoatshipsHumanoid.MaterialFor(color)) { name = "Town" };
            materials.Add(material);
            return material;
        }

        private void OnDestroy() => Clear();
    }

    /// <summary>Ambient townsperson strolling between points around the plaza.</summary>
    public sealed class TownWalker : MonoBehaviour
    {
        private ArchipelagoWorld.IslandSite site;
        private int seed;
        private Vector2 home;
        private float radius;
        private Vector2 position;
        private Vector2 target;
        private float waitUntil;
        private System.Random random;

        public void Configure(ArchipelagoWorld.IslandSite island, int worldSeed, Vector2 plaza, float range, int randomSeed)
        {
            site = island; seed = worldSeed; home = plaza; radius = range;
            random = new System.Random(randomSeed);
            position = PickPoint();
            target = PickPoint();
            Place();
        }

        private Vector2 PickPoint()
        {
            for (int i = 0; i < 10; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector2 point = home + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * (float)random.NextDouble() * radius;
                if (BoatshipsTown.Ground(site, seed, point) > 0.6f) return point;
            }
            return home;
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
