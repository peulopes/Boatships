using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Boatships.Editor
{
    public static class HarbourFortPlayChecks
    {
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>("Assets/Boatships/Materials/" + name + ".mat");

        public static async Task Run()
        {
            float previousScale = Time.timeScale;
            Time.timeScale = 4f;
            GameObject sea = new GameObject("Fort test ocean");
            OceanWaves waves = sea.AddComponent<OceanWaves>();
            sea.GetComponent<MeshRenderer>().enabled = false;
            var oceanSettings = new SerializedObject(waves);
            oceanSettings.FindProperty("amplitude").floatValue = 0f;
            oceanSettings.ApplyModifiedPropertiesWithoutUndo();
            var site = new ArchipelagoWorld.IslandSite {
                Center = new Vector2(5000f, 5000f), Radius = 26f,
                Kind = IslandKind.PortSite, DetailSeed = 847291, Biome = IslandBiome.Tropical
            };
            GameObject island = new GameObject("Defended port test");
            island.transform.position = new Vector3(site.Center.x, 0f, site.Center.y);
            island.AddComponent<ProceduralIslandChunk>().Build(site, 28, 847291,
                Material("Island_Sand"), Material("Island_Grass"), Material("Island_Rock"),
                Material("Island_Bark"), Material("Island_Leaves"), Material("Island_Stone"),
                Material("Island_PortMarker"), Material("Island_PirateMarker"), Material("Island_TreasureMarker"));
            HarbourFort fort = island.GetComponentInChildren<HarbourFort>();
            Check(fort != null, "Generated port has no working fort.");
            Transform harbour = island.transform.Find("Port Village and Pier");
            Vector3 forward = harbour.forward, right = harbour.right;
            Vector3 centre = fort.ProtectedCentre;
            ShipCombatTarget bystander = CreateShip(centre + forward * 24f + right * 14f, waves);
            ShipCombatTarget player = CreateShip(centre + forward * 16f - right * 6f, waves);
            ShipCombatTarget npc = CreateShip(centre + forward * 20f + right * 6f, waves);
            npc.gameObject.AddComponent<EnemyShipAI>().enabled = false;
            int innocentHull = bystander.RemainingVoxels;
            Vector3 npcPosition = npc.transform.position;
            ShipCombatTarget outsider = CreateShip(centre + forward * (fort.ProtectedRadius + 3f), waves);
            await Task.Delay(150);
            try
            {
                Check(fort.AggressorCount == 0 && fort.ShotsFired == 0, "Fort attacked peaceful ships.");
                Shoot(outsider, outsider.transform.position + Vector3.up * 6f, forward * 22f);
                Shoot(null, centre + Vector3.up * 6f, Vector3.up * 22f);
                await Task.Delay(150);
                Check(fort.AggressorCount == 0, "Distant gunfire or fort fire provoked the battery.");

                // A shot from outside the protected area must still provoke the fort if it lands inside.
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "Shot impact near harbour";
                marker.transform.position = centre + forward * 10f + Vector3.up * 6f;
                marker.transform.localScale = Vector3.one * 2f;
                Physics.SyncTransforms();
                Vector3 start = outsider.transform.position;
                start.y = marker.transform.position.y;
                Shoot(outsider, start, (marker.transform.position - start).normalized * 60f);
                await Until(() => fort.CurrentTarget == outsider, 3f, "A shot landing beside the harbour did not provoke its fort.");
                Check(fort.CurrentTarget == outsider, "Fort did not identify the distant shooter.");
                Object.Destroy(marker);
                Object.Destroy(outsider.gameObject);
                await Task.Delay(100);
                Check(fort.AggressorCount == 0, "Fort retained a destroyed target.");

                int playerHull = player.RemainingVoxels;
                Shoot(player, player.transform.position + Vector3.up * 2f, forward * 22f + Vector3.up * 12f);
                await Until(() => player.RemainingVoxels < playerHull, 8f, "Fort cannonballs did not hit the player aggressor.");
                Check(fort.ShotsFired > 0 && fort.CurrentTarget == player, "Fort did not retaliate against the player.");
                Shoot(npc, npc.transform.position + Vector3.up * 2f, forward * 22f + Vector3.up * 12f);
                await Until(() => fort.AggressorCount == 2, 2f, "Fort did not remember multiple aggressors.");
                npc.transform.position = fort.transform.position + forward * (fort.EngagementRange + 20f);
                Physics.SyncTransforms();
                await Until(() => fort.AggressorCount == 1, 2f, "Fort did not release the NPC outside its range.");
                await Until(() => player.GetComponent<ShipFlooding>().IsSinking, 20f,
                    "Fort hits did not create enough hull damage and flooding to sink the player.");
                await Until(() => fort.AggressorCount == 0, 2f, "Fort kept shooting a sinking ship.");

                npc.transform.position = npcPosition;
                Physics.SyncTransforms();
                int npcHull = npc.RemainingVoxels;
                Shoot(npc, npc.transform.position + Vector3.up * 2f, forward * 22f + Vector3.up * 12f);
                await Until(() => npc.RemainingVoxels < npcHull, 8f, "Fort cannonballs did not hit the NPC aggressor.");
                await Until(() => npc.GetComponent<ShipFlooding>().IsSinking, 20f, "Fort could not sink the NPC aggressor.");
                await Until(() => fort.AggressorCount == 0, 2f, "Fort retained a sinking NPC.");
                int shots = fort.ShotsFired;
                await Task.Delay(700);
                Check(fort.ShotsFired == shots && bystander.RemainingVoxels == innocentHull,
                    "Fort attacked an innocent boat or continued firing without an active aggressor.");
                Debug.Log("BOATSHIPS_FORT_DEFENCE_CHECKS_PASSED: neutral ships, distant shots, nearby impacts, " +
                    "player and NPC retaliation, multiple aggressors, range escape, sinking and ceasefire.");
            }
            finally
            {
                Time.timeScale = previousScale;
                Object.Destroy(island);
                Object.Destroy(sea);
                Object.Destroy(player.gameObject);
                Object.Destroy(npc.gameObject);
                Object.Destroy(bystander.gameObject);
                if (outsider != null) Object.Destroy(outsider.gameObject);
            }
        }

        private static async Task Until(Func<bool> condition, float timeout, string message)
        {
            // Editor compilation can suspend physics for many wall-clock seconds.
            float simulationDeadline = Time.time + timeout * Time.timeScale;
            float safetyDeadline = Time.realtimeSinceStartup + 120f;
            while (!condition() && Time.time < simulationDeadline && Time.realtimeSinceStartup < safetyDeadline)
                await Task.Delay(25);
            Check(condition(), message);
        }

        private static ShipCombatTarget CreateShip(Vector3 position, OceanWaves waves)
        {
            GameObject ship = new GameObject("Fort test boat");
            position.y = 0.15f;
            ship.transform.position = position;
            HullDefinition definition = AssetDatabase.LoadAssetAtPath<HullDefinition>("Assets/Boatships/Hulls/StarterSloop.asset");
            VoxelHull hull = ship.AddComponent<VoxelHull>();
            hull.Configure(definition, Material("Wood"));
            BoxCollider collider = ship.AddComponent<BoxCollider>();
            collider.size = new Vector3(definition.Width, definition.Height, definition.Length) * definition.BlockSize;
            ShipCombatTarget target = ship.AddComponent<ShipCombatTarget>();
            target.Configure(hull, false);
            ship.AddComponent<ShipFlooding>().Configure(hull, waves, null, null, null, null, null);
            return target;
        }

        private static void Shoot(ShipCombatTarget shooter, Vector3 start, Vector3 velocity)
        {
            BoatshipsProjectile projectile = BoatshipsProjectile.Spawn(start, Material("CannonballIron"));
            projectile.Launch(velocity, shooter, null, 90f, 0.45f, 0f);
        }
    }
}
