using UnityEngine;
using UnityEngine.SceneManagement;

namespace Boatships
{
    /// <summary>Installs the world-map voyage layer (character, travel controller, map UI, character creation)
    /// into any campaign scene when it loads, so existing scenes need no rebuild.
    /// Scenes without a BoatshipsSaveCoordinator (the milestone prototypes) are left untouched.</summary>
    public static class BoatshipsVoyageBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Object.FindAnyObjectByType<BoatshipsTravelController>() != null) return;
            BoatshipsSaveCoordinator saves = Object.FindAnyObjectByType<BoatshipsSaveCoordinator>();
            if (saves == null) return;
            Install(saves);
        }

        public static void Install(BoatshipsSaveCoordinator saves)
        {
            PortEconomy economy = Object.FindAnyObjectByType<PortEconomy>();
            PortDocking docking = Object.FindAnyObjectByType<PortDocking>();
            BoatshipsEncounterDirector director = Object.FindAnyObjectByType<BoatshipsEncounterDirector>();
            BoatshipsCrewProgression progression = Object.FindAnyObjectByType<BoatshipsCrewProgression>();
            BoatshipsCampaignHUD hud = Object.FindAnyObjectByType<BoatshipsCampaignHUD>();
            OceanWaves ocean = Object.FindAnyObjectByType<OceanWaves>();
            BoatshipsMobilePresentation presentation = Object.FindAnyObjectByType<BoatshipsMobilePresentation>();
            if (economy == null || docking == null || director == null || director.World == null || progression == null)
            {
                Debug.LogWarning("Boatships voyage layer not installed: the campaign scene is missing core systems.");
                return;
            }
            GameObject flagship = progression.gameObject;
            director.AutoWaves = false;
            if (presentation != null) BoatshipsUI.ClickSound = presentation.Click;

            GameObject host = new GameObject("Boatships Voyage");
            BoatshipsCharacter character = host.AddComponent<BoatshipsCharacter>();
            character.Configure(director.World, economy, saves, flagship.transform);
            BoatshipsTravelController travel = host.AddComponent<BoatshipsTravelController>();
            travel.Configure(director.World, economy, docking, director, saves, character, hud, flagship.transform,
                flagship.GetComponent<ShipArcadeController>(), flagship.GetComponent<WeaponGroupCombat>(),
                flagship.GetComponent<ShipFlooding>(), ocean);
            BoatshipsWorldMapView view = host.AddComponent<BoatshipsWorldMapView>();
            view.Configure(travel, director.World, hud);
            BoatshipsCharacterCreation creation = host.AddComponent<BoatshipsCharacterCreation>();
            if (hud != null) hud.BindVoyage(character, creation);

            // First-person layer: port towns, battles aboard AI ships, and boarding actions.
            BoatshipsFirstPerson firstPerson = host.AddComponent<BoatshipsFirstPerson>();
            firstPerson.Configure(view, hud, docking, presentation != null ? presentation.Canvas : null, character);
            BoatshipsTown town = host.AddComponent<BoatshipsTown>();
            BoatshipsCrewBattle crewBattle = host.AddComponent<BoatshipsCrewBattle>();
            crewBattle.Configure(director, firstPerson, character, ocean);
            travel.BindFirstPerson(firstPerson, town, crewBattle);
            BoatshipsBoarding boarding = Object.FindAnyObjectByType<BoatshipsBoarding>();
            if (boarding != null)
                host.AddComponent<BoatshipsBoardingFirstPerson>().Configure(boarding, firstPerson, travel, flagship.transform);
        }
    }
}
