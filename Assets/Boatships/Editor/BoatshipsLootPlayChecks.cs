using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Boatships.Editor
{
    public static class BoatshipsLootPlayChecks
    {
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        public static async Task Run()
        {
            const int seed = 19573861;
            string directory = Path.GetFullPath("Temp/LootChecks/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            BoatshipsSaveStore.Initialize(0, directory);
            GameObject host = new GameObject("Loot check systems");
            GameObject sea = new GameObject("Loot check sea");
            OceanWaves waves = sea.AddComponent<OceanWaves>();
            sea.GetComponent<MeshRenderer>().enabled = false;
            ArchipelagoWorld world = host.AddComponent<ArchipelagoWorld>();
            world.enabled = false;
            world.UseSeed(seed);
            PortEconomy account = host.AddComponent<PortEconomy>();
            account.UseWorldSeed(seed);
            ShipCombatTarget player = Ship("Loot check player", new Vector3(2000f, 0.8f, 2000f));
            ShipCombatTarget pirate = Ship("Loot check original pirate", new Vector3(2040f, 0.8f, 2000f));
            PortDocking docking = host.AddComponent<PortDocking>();
            docking.Configure(world, player.transform, null, null, null, null, player.Hull, account);
            Vector3 location = new Vector3(2000f, 0.8f, 2000f);
            Check(account.TryRegisterLoot(Cache("prototype_pirate_01", location + Vector3.right * 5f, 31, 2)), "Could not register original pirate salvage.");
            Check(account.TryRegisterLoot(Cache("campaign_loot_check", location + Vector3.right * 7f, 57, 4)), "Could not register campaign cargo.");
            Check(account.TryRegisterLoot(Cache("campaign_far_check", location + Vector3.right * 45f, 99, 9)), "Could not register distant cargo.");
            BattleLootSystem loot = host.AddComponent<BattleLootSystem>();
            loot.Configure(world, account, docking, player, pirate, null, null, waves);
            BoatshipsEncounterDirector director = host.AddComponent<BoatshipsEncounterDirector>();
            director.Configure(world, account, player, null, waves);
            GameObject canvas = new GameObject("Loot check canvas", typeof(RectTransform), typeof(Canvas));
            GameObject hudRoot = new GameObject("Gameplay loot", typeof(RectTransform));
            hudRoot.transform.SetParent(canvas.transform, false);
            GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(hudRoot.transform, false);
            Text status = Label("LootInfo", panel.transform);
            GameObject buttonObject = new GameObject("CollectButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(panel.transform, false);
            Button collect = buttonObject.GetComponent<Button>();
            collect.targetGraphic = buttonObject.GetComponent<Image>();
            Label("Caption", buttonObject.transform);
            hudRoot.AddComponent<BattleLootHUD>().Configure(loot, docking, account, status, collect, null);
            try
            {
                await Frames(15);
                // Exercise the real docking gate without changing any player save.
                world.Regenerate(seed);
                Check(world.TryNearestIslandOfKind(Vector3.zero, IslandKind.PortSite, out var port), "No test port.");
                player.transform.position = new Vector3(port.Center.x, 0.8f, port.Center.y);
                Check(account.TryRegisterLoot(Cache("dock_check", player.transform.position, 12, 1)), "Could not register dock test loot.");
                docking.Dock();
                await Frames(3);
                loot.CollectNearby();
                Check(docking.IsDocked && !loot.CanCollect && !collect.gameObject.activeInHierarchy && account.GetPendingLoot("dock_check") != null,
                    "Docked ships could collect loot, or collection HUD was not hidden.");
                docking.Undock();
                await Frames(3);
                Check(collect.interactable, "Collection did not become available after departing.");
                collect.onClick.Invoke();
                Check(account.GetPendingLoot("dock_check") == null, "Screen button could not collect after departing.");

                player.transform.position = location - Vector3.right * 30f;
                await Frames(3);
                int gold = account.Gold, wood = account.Wood;
                director.CollectNearbyCargo(); // Synchronizes visible cargo while the player is outside collection range.
                Check(!collect.interactable && !loot.CanCollect, "Collection enabled outside the 9m range.");
                loot.CollectNearby();
                Check(account.Gold == gold && account.GetPendingLoot("campaign_loot_check") != null, "Distant loot was collected.");
                GameObject pirateMarker = GameObject.Find("Floating Pirate Salvage (M9)");
                GameObject nearCargo = null, farCargo = null;
                foreach (Renderer renderer in Object.FindObjectsByType<Renderer>())
                {
                    if (renderer.name != "Campaign cargo") continue;
                    if (Mathf.Abs(renderer.transform.position.x - (location.x + 7f)) < 0.5f) nearCargo = renderer.gameObject;
                    if (Mathf.Abs(renderer.transform.position.x - (location.x + 45f)) < 0.5f) farCargo = renderer.gameObject;
                }
                Check(pirateMarker != null && nearCargo != null && farCargo != null, "Loot markers were not visible before collection.");
                player.transform.position = location;
                await Frames(3);
                Check(collect.gameObject.activeInHierarchy && collect.interactable &&
                    collect.GetComponentInParent<BoatshipsDebugMenu>() == null,
                    "Loot collection was not available on the gameplay HUD.");
                collect.onClick.Invoke();
                Check(account.Gold == gold + 88 && account.Wood == wood + 6,
                    "Screen collection did not grant both original-pirate and campaign loot exactly once.");
                Check(account.GetPendingLoot("prototype_pirate_01") == null && account.GetPendingLoot("campaign_loot_check") == null &&
                    account.GetPendingLoot("campaign_far_check") != null, "Collection removed the wrong loot caches.");
                Check((pirateMarker == null || !pirateMarker.activeSelf) && (nearCargo == null || !nearCargo.activeSelf) && farCargo.activeSelf,
                    "Collected loot did not disappear immediately, or distant loot disappeared.");
                Check(status.text.Contains("Collected 2 loot crates") && status.text.Contains("88 gold"),
                    "The screen did not confirm the collected rewards.");
                collect.onClick.Invoke();
                Check(account.Gold == gold + 88, "Repeated collection duplicated rewards.");
                await Frames(3);
                Check(pirateMarker == null && nearCargo == null && farCargo != null, "Collected marker objects were not destroyed.");
                Check(BoatshipsSaveStore.Commit(), "Could not save the loot collection checkpoint.");
                BoatshipsSaveStore.Shutdown();
                BoatshipsSaveStore.Initialize(0, directory);
                PortEconomy reloaded = new GameObject("Reloaded loot account").AddComponent<PortEconomy>();
                reloaded.UseWorldSeed(seed);
                Check(reloaded.Gold == gold + 88 && reloaded.Wood == wood + 6 && reloaded.PendingLoot.Count == 1 &&
                    reloaded.GetPendingLoot("campaign_far_check") != null, "Reload restored collected loot or lost its rewards.");
                Check(!reloaded.TryCollectLoot("prototype_pirate_01", out _) && !reloaded.TryCollectLoot("campaign_loot_check", out _),
                    "Collected loot could be claimed again after reload.");
                Object.Destroy(reloaded.gameObject);
                Debug.Log("BOATSHIPS_LOOT_COLLECTION_CHECKS_PASSED: gameplay button, both loot sources, range and docking gates, " +
                    "reward confirmation, immediate marker removal, repeat-click protection and persistence.");
            }
            finally
            {
                Object.Destroy(canvas);
                Object.Destroy(host);
                Object.Destroy(player.gameObject);
                Object.Destroy(pirate.gameObject);
                Object.Destroy(sea);
            }
        }

        private static async Task Frames(int count)
        {
            int target = Time.frameCount + count;
            float hudTime = Time.unscaledTime + 0.2f;
            float deadline = Time.realtimeSinceStartup + 45f;
            while ((Time.frameCount < target || Time.unscaledTime < hudTime) && Time.realtimeSinceStartup < deadline) await Task.Delay(30);
            Check(Time.frameCount >= target, "Unity stopped advancing frames during the loot checks.");
        }

        private static PortEconomy.LootCache Cache(string id, Vector3 position, int gold, int wood) =>
            new PortEconomy.LootCache { id = id, x = position.x, z = position.z, gold = gold, wood = wood };

        private static ShipCombatTarget Ship(string name, Vector3 position)
        {
            GameObject ship = new GameObject(name);
            ship.transform.position = position;
            VoxelHull hull = ship.AddComponent<VoxelHull>();
            hull.Configure(AssetDatabase.LoadAssetAtPath<HullDefinition>("Assets/Boatships/Hulls/StarterSloop.asset"),
                AssetDatabase.LoadAssetAtPath<Material>("Assets/Boatships/Materials/Wood.mat"));
            ShipCombatTarget target = ship.AddComponent<ShipCombatTarget>();
            target.Configure(hull, false);
            return target;
        }

        private static Text Label(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return label;
        }
    }
}
