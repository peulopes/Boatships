using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// Low-poly models for shipyard equipment, in the same style as the sculpted hull.
    /// Each item is built under its mount transform (+z = the way the mount faces).
    /// Names other systems rely on are kept: cannons have a "Barrel" pivot whose local +Y is the bore,
    /// and the main square sail is the renderer named "Sail" (captain colours and flag attach to it).
    /// </summary>
    public static class ShipEquipmentModels
    {
        private const int Main = 0, Trim = 1, Iron = 2, Dark = 3, Lamp = 4, Count = 5;

        private static Material[] Palette(Material main) => new[]
        {
            main != null ? main : ShipPalette.Solid(ShipPalette.DarkWood), ShipPalette.Solid(ShipPalette.Spar),
            ShipPalette.Solid(ShipPalette.Iron), ShipPalette.Solid(ShipPalette.DarkWood), ShipPalette.Glow(ShipPalette.Lamp)
        };

        public static void Build(Transform item, ShipEquipmentDefinition definition, HullDefinition hull)
        {
            Material material = definition.Material;
            switch (definition.Kind)
            {
                case ShipEquipmentKind.Cannon: Cannon(item, material); break;
                case ShipEquipmentKind.Storage: Storage(item, material); break;
                case ShipEquipmentKind.Armor: Armor(item, material); break;
                case ShipEquipmentKind.Cabin: Cabin(item, material); break;
                case ShipEquipmentKind.Mast: Mast(item, material, hull); break;
                case ShipEquipmentKind.Rudder: Rudder(item, material); break;
            }
        }

        private static void Cannon(Transform item, Material metal)
        {
            var carriage = new LowPolyMesh(Count);
            carriage.Box(new Vector3(0f, 0.07f, -0.02f), new Vector3(0.15f, 0.035f, 0.28f), Dark);
            for (int side = -1; side <= 1; side += 2)
            {
                carriage.Box(new Vector3(side * 0.075f, 0.135f, -0.02f), new Vector3(0.03f, 0.17f, 0.3f), Dark);
                carriage.Box(new Vector3(side * 0.075f, 0.225f, 0f), new Vector3(0.036f, 0.02f, 0.05f), Iron);
                foreach (float z in new[] { 0.09f, -0.12f })
                {
                    carriage.Rod(new Vector3(side * 0.095f, 0.05f, z), new Vector3(side * 0.13f, 0.05f, z), 0.05f, 0.05f, 8, Dark);
                    carriage.Rod(new Vector3(side * 0.12f, 0.05f, z), new Vector3(side * 0.14f, 0.05f, z), 0.018f, 0.018f, 6, Iron);
                }
            }
            foreach (float z in new[] { 0.09f, -0.12f })
                carriage.Box(new Vector3(0f, 0.05f, z), new Vector3(0.22f, 0.03f, 0.03f), Dark);
            carriage.Create("Carriage", item, Vector3.zero, Quaternion.identity, Palette(metal));

            // The bore runs along the pivot's +Y; it is laid forward (+z) and aimed by WeaponGroupCombat.
            var barrel = new LowPolyMesh(Count);
            barrel.Prism(new Vector3(0f, -0.15f, 0f), 0.052f, 0.043f, 0.52f, 8, Main);
            barrel.Prism(new Vector3(0f, 0.33f, 0f), 0.058f, 0.058f, 0.045f, 8, Main);
            barrel.Prism(new Vector3(0f, -0.08f, 0f), 0.06f, 0.06f, 0.05f, 8, Main);
            barrel.Prism(new Vector3(0f, -0.2f, 0f), 0.02f, 0.032f, 0.05f, 6, Main);
            barrel.Box(Vector3.zero, new Vector3(0.18f, 0.03f, 0.03f), Main);
            barrel.Create("Barrel", item, new Vector3(0f, 0.23f, 0f), Quaternion.Euler(90f, 0f, 0f), Palette(metal));
        }

        /// <summary>Mast, yards, square sails and rigging stepped at the mount's position on a hull.</summary>
        public static void Rig(Transform mount, Material sailCloth, HullDefinition hull) => Mast(mount, sailCloth, hull);

        private static void Mast(Transform item, Material sailCloth, HullDefinition hull)
        {
            Vector3 step = item.localPosition;
            Vector3 up = Vector3.up;
            var rig = new LowPolyMesh(Count);

            // Lower mast with iron bands, crow's nest, topmast and truck.
            rig.Prism(Vector3.zero, 0.06f, 0.045f, 2.25f, 8, Trim);
            foreach (float y in new[] { 0.95f, 1.7f }) rig.Prism(new Vector3(0f, y, 0f), 0.064f, 0.064f, 0.035f, 8, Iron);
            rig.Prism(new Vector3(0f, 2.1f, 0f), 0.13f, 0.17f, 0.15f, 8, Dark);
            rig.Prism(new Vector3(0f, 2.235f, 0f), 0.178f, 0.178f, 0.025f, 8, Trim);
            rig.Prism(new Vector3(0f, 2.2f, 0f), 0.038f, 0.026f, 1.2f, 6, Trim);
            rig.Prism(new Vector3(0f, 3.4f, 0f), 0.045f, 0.045f, 0.045f, 6, Iron);

            Yard(rig, new Vector3(0f, 1.97f, 0.07f), 1.78f, 0.032f);
            Yard(rig, new Vector3(0f, 3.0f, 0.06f), 1.2f, 0.025f);

            // Shrouds from the bulwarks to the masthead, with ratlines to climb them.
            float rail = HullShape.BulwarkHeight(hull, step.z / HullShape.HalfLength(hull)) + 0.03f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 head = new Vector3(side * 0.05f, 2.08f, -0.02f);
                var feet = new Vector3[3];
                for (int i = 0; i < feet.Length; i++)
                {
                    float z = step.z - 0.4f + i * 0.19f;
                    feet[i] = HullShape.Surface(hull, side, 1f, z / HullShape.HalfLength(hull)) + up * rail - step -
                        new Vector3(side * 0.02f, 0f, 0f);
                    rig.Beam(feet[i], head, 0.014f, 0.014f, Main);
                    rig.Box(feet[i], new Vector3(0.04f, 0.05f, 0.04f), Dark);
                }
                for (float f = 0.1f; f < 0.86f; f += 0.105f)
                    rig.Beam(Vector3.Lerp(feet[0], head, f), Vector3.Lerp(feet[2], head, f), 0.009f, 0.009f, Main);
                rig.Beam(new Vector3(side * 0.16f, 2.25f, 0f), new Vector3(side * 0.03f, 2.95f, 0f), 0.01f, 0.01f, Main);
            }
            rig.Beam(new Vector3(0f, 2.15f, 0.05f), HullShape.StemHead(hull) - step, 0.014f, 0.014f, Main);
            rig.Create("Rigging", item, Vector3.zero, Quaternion.identity, Palette(ShipPalette.Solid(ShipPalette.Rope)));

            // Square sails: the course below the crow's nest and the topsail above it.
            Sail(item, "Course Sail", new Vector3(0f, 1.47f, 0.09f), new Vector3(1.62f, 0.94f, 0.16f), sailCloth);
            Sail(item, "Sail", new Vector3(0f, 2.67f, 0.08f), new Vector3(1.08f, 0.62f, 0.12f), sailCloth);
        }

        private static void Yard(LowPolyMesh rig, Vector3 centre, float length, float radius)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 end = centre + Vector3.right * (side * length * 0.5f);
                rig.Rod(centre, end, radius, radius * 0.65f, 6, Trim);
                rig.Box(end, new Vector3(0.035f, 0.035f, 0.035f), Iron);
            }
            rig.Box(centre + new Vector3(0f, 0f, -0.04f), new Vector3(0.1f, 0.05f, 0.05f), Iron);
        }

        private static void Sail(Transform item, string name, Vector3 position, Vector3 size, Material cloth)
        {
            var sail = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            sail.transform.SetParent(item, false);
            sail.transform.localPosition = position;
            sail.transform.localScale = size;
            sail.GetComponent<MeshFilter>().sharedMesh = LowPolyMesh.SailMesh;
            sail.GetComponent<MeshRenderer>().sharedMaterial = cloth != null ? cloth : ShipPalette.Solid(new Color(0.93f, 0.88f, 0.72f));
        }

        private static void Rudder(Transform item, Material wood)
        {
            var m = new LowPolyMesh(Count);
            var blade = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                bool top = (i & 2) != 0, front = (i & 4) != 0;
                blade[i] = new Vector3((i & 1) != 0 ? 0.022f : -0.022f, top ? 0.3f : -0.32f,
                    front ? -0.12f : (top ? -0.27f : -0.37f));
            }
            m.Hexa(blade, Main);
            foreach (float y in new[] { -0.2f, 0f, 0.2f })
                m.Box(new Vector3(0f, y, -0.13f), new Vector3(0.055f, 0.025f, 0.05f), Iron);
            m.Prism(new Vector3(0f, 0.3f, -0.13f), 0.025f, 0.025f, 0.3f, 6, Main);
            m.Create("Rudder", item, Vector3.zero, Quaternion.identity, Palette(wood));
        }

        private static void Storage(Transform item, Material crateWood)
        {
            var m = new LowPolyMesh(Count);
            Crate(m, new Vector3(-0.11f, 0f, -0.1f), 0.21f, 0f);
            Crate(m, new Vector3(0.13f, 0f, -0.12f), 0.17f, 8f);
            Crate(m, new Vector3(-0.1f, 0.21f, -0.09f), 0.15f, -12f);
            Cask(m, new Vector3(0.11f, 0f, 0.13f));
            Cask(m, new Vector3(-0.12f, 0f, 0.14f));
            m.Create("Cargo", item, Vector3.zero, Quaternion.identity, Palette(crateWood));
        }

        private static void Crate(LowPolyMesh m, Vector3 bottom, float size, float yaw)
        {
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            Vector3 up = Vector3.up;
            m.Box(bottom + up * (size * 0.5f), turn, Vector3.one * size, Main);
            m.Box(bottom + up * 0.025f, turn, new Vector3(size + 0.01f, 0.03f, size + 0.01f), Trim);
            m.Box(bottom + up * (size - 0.025f), turn, new Vector3(size + 0.01f, 0.03f, size + 0.01f), Trim);
            m.Box(bottom + up * (size * 0.5f), turn, new Vector3(0.03f, size + 0.008f, size + 0.01f), Trim);
        }

        private static void Cask(LowPolyMesh m, Vector3 bottom)
        {
            Vector3 up = Vector3.up;
            m.Prism(bottom, 0.072f, 0.088f, 0.11f, 8, Dark);
            m.Prism(bottom + up * 0.11f, 0.088f, 0.072f, 0.11f, 8, Dark);
            m.Prism(bottom + up * 0.03f, 0.082f, 0.085f, 0.018f, 8, Iron);
            m.Prism(bottom + up * 0.172f, 0.085f, 0.082f, 0.018f, 8, Iron);
        }

        private static void Armor(Transform item, Material plate)
        {
            var m = new LowPolyMesh(Count);
            m.Box(new Vector3(0f, 0.03f, 0f), new Vector3(0.24f, 0.05f, 0.24f), Main);
            m.Box(new Vector3(0f, 0.065f, 0f), new Vector3(0.18f, 0.02f, 0.18f), Main);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                m.Box(new Vector3(sx * 0.095f, 0.06f, sz * 0.095f), new Vector3(0.025f, 0.02f, 0.025f), Iron);
            m.Create("Plate", item, Vector3.zero, Quaternion.identity, Palette(plate));
        }

        private static void Cabin(Transform item, Material walls)
        {
            var m = new LowPolyMesh(Count);
            m.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.74f, 0.04f, 0.74f), Dark);
            m.Box(new Vector3(0f, 0.27f, 0f), new Vector3(0.68f, 0.46f, 0.68f), Main);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                m.Box(new Vector3(sx * 0.34f, 0.27f, sz * 0.34f), new Vector3(0.05f, 0.5f, 0.05f), Trim);
            m.Box(new Vector3(0f, 0.51f, 0f), new Vector3(0.72f, 0.04f, 0.72f), Trim);

            // Gable ends and a pitched roof with a ridge running fore and aft.
            var gable = new Vector3[8];
            for (int sz = -1; sz <= 1; sz += 2)
            {
                for (int i = 0; i < 8; i++)
                {
                    bool top = (i & 2) != 0;
                    gable[i] = new Vector3(top ? 0f : ((i & 1) != 0 ? 0.34f : -0.34f), top ? 0.74f : 0.53f,
                        sz * 0.34f + ((i & 4) != 0 ? 0.015f : -0.015f));
                }
                m.Hexa(gable, Main);
            }
            for (int sx = -1; sx <= 1; sx += 2)
                m.Beam(new Vector3(sx * 0.42f, 0.5f, 0f), new Vector3(0f, 0.76f, 0f), 0.84f, 0.035f, Dark);
            m.Box(new Vector3(0f, 0.775f, 0f), new Vector3(0.05f, 0.04f, 0.86f), Trim);

            // Door facing aft towards the helm, lit windows on the other three walls, and a stove pipe.
            m.Box(new Vector3(0f, 0.2f, -0.345f), new Vector3(0.18f, 0.34f, 0.02f), Dark);
            m.Box(new Vector3(0.05f, 0.2f, -0.358f), new Vector3(0.02f, 0.02f, 0.012f), Iron);
            for (int sx = -1; sx <= 1; sx += 2)
                Window(m, new Vector3(sx * 0.345f, 0.3f, 0f), Quaternion.LookRotation(new Vector3(sx, 0f, 0f)), 0.16f, 0.13f);
            Window(m, new Vector3(0f, 0.3f, 0.345f), Quaternion.identity, 0.16f, 0.13f);
            m.Prism(new Vector3(0.17f, 0.6f, 0.17f), 0.03f, 0.03f, 0.26f, 6, Iron);
            m.Create("Cabin", item, Vector3.zero, Quaternion.identity, Palette(walls));
        }

        private static void Window(LowPolyMesh m, Vector3 at, Quaternion facing, float width, float height)
        {
            Vector3 normal = facing * Vector3.forward;
            m.Box(at, facing, new Vector3(width + 0.035f, height + 0.035f, 0.02f), Trim);
            m.Box(at + normal * 0.008f, facing, new Vector3(width, height, 0.02f), Lamp);
            m.Box(at + normal * 0.018f, facing, new Vector3(0.012f, height, 0.008f), Trim);
        }
    }
}
