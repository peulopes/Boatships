using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Boatships
{
    /// <summary>
    /// The captain aboard their own ship. At the helm the ship is sailed from the sky view and the gun crews
    /// fire on their own. Leaving the helm (Q) puts the captain on deck in first person, where they can take
    /// command of any cannon, lead a boarding party across to an enemy alongside, or take the wheel again.
    /// The ship holds her course while nobody is steering.
    /// </summary>
    public sealed class BoatshipsShipCommand : MonoBehaviour
    {
        private BoatshipsTravelController travel;
        private BoatshipsFirstPerson fps;
        private BoatshipsCampaignHUD hud;
        private BoatshipsWorldMapView view;
        private PortDocking docking;
        private BoatshipsBoarding boarding;
        private BoatshipsEncounterDirector director;
        private ShipCombatTarget flagship;
        private WeaponGroupCombat weapons;
        private ShipFlooding flooding;
        private ShipArcadeController movement;

        private bool session;
        private float nextGunScan;
        private string notice;
        private float noticeUntil;
        private readonly HashSet<ShipGunStation> listedGuns = new HashSet<ShipGunStation>();

        private VoxelHull Hull => flagship != null ? flagship.Hull : null;
        public bool AwayFromHelm => session;

        public void Configure(BoatshipsTravelController controller, BoatshipsFirstPerson firstPerson, BoatshipsCampaignHUD campaignHud,
            BoatshipsWorldMapView mapView, PortDocking port, BoatshipsBoarding boardingAction, BoatshipsEncounterDirector encounters,
            GameObject ship)
        {
            travel = controller; fps = firstPerson; hud = campaignHud; view = mapView; docking = port;
            boarding = boardingAction; director = encounters;
            flagship = ship.GetComponent<ShipCombatTarget>();
            weapons = ship.GetComponent<WeaponGroupCombat>();
            flooding = ship.GetComponent<ShipFlooding>();
            movement = ship.GetComponent<ShipArcadeController>();
        }

        /// <summary>At sea in your own ship, with the wheel in your hands.</summary>
        public bool AtHelm =>
            travel != null && travel.Live && travel.Captain && Hull != null && !session && fps != null && !fps.Active &&
            (travel.Mode == TravelMode.Battle || travel.Mode == TravelMode.Island) &&
            (docking == null || !docking.IsDocked) && (flooding == null || !flooding.IsSinking) &&
            (boarding == null || !boarding.InProgress) && movement != null && movement.enabled;

        private void Update()
        {
            if (fps == null || travel == null) return;
            if (!session)
            {
                Keyboard keyboard = Keyboard.current;
                bool free = !BoatshipsUI.Typing && (hud == null || !hud.BlockingOverlayOpen) && (view == null || !view.MenuOpen);
                if (AtHelm && free && keyboard != null && keyboard.qKey.wasPressedThisFrame) LeaveHelm();
                return;
            }
            // The session ends when first person was taken over (boarding, a town) or closed elsewhere.
            if (!fps.Active || !fps.OnDeck(Hull)) { session = false; listedGuns.Clear(); return; }
            if (!fps.Alive)
            {
                TakeWheel();
                travel.Notify("A musket ball found you on the open deck. The bosun hauls you back to the wheel, bloodied but alive.");
                return;
            }
            bool atSea = travel.Mode == TravelMode.Battle || travel.Mode == TravelMode.Island;
            if (!atSea || (docking != null && docking.IsDocked)) { TakeWheel(); return; }
            if (flooding != null && flooding.IsSinking) { TakeWheel(); travel.Notify("Your ship is sinking!"); return; }
            if (Time.time >= nextGunScan) { nextGunScan = Time.time + 0.5f; ListNewGuns(); }
            fps.SetStatus(StatusText());
        }

        private void LeaveHelm()
        {
            VoxelHull hull = Hull;
            Vector3 helm = new Vector3(0f, HullShape.DeckY(hull.Definition), -HullShape.HalfLength(hull.Definition) * 0.7f);
            foreach (HullHardpoint slot in hull.Definition.Hardpoints)
                if (slot.type == HullHardpointType.Helm) helm = slot.localPosition;
            fps.BeginDeck(hull, helm + new Vector3(0f, 0f, 0.3f), 0f);
            if (!fps.Active) return;
            session = true;
            listedGuns.Clear();
            nextGunScan = 0f;
            fps.SetExit("Take the wheel", TakeWheel);
            fps.AddInteractable(new FpsInteractable
            {
                Position = () => Hull != null ? Hull.transform.TransformPoint(helm) : Vector3.zero,
                Prompt = "Take the wheel (steer from above)",
                Radius = 0.75f,
                Use = TakeWheel
            });
            fps.AddInteractable(new FpsInteractable
            {
                Position = BoardingPoint,
                Prompt = "Board the enemy ship!",
                Radius = 0.9f,
                Use = Board
            });
            Say("You left the helm — she holds her course. [1] cutlass, [2] flintlock: pick off the enemy crew within 18 m, but mind their muskets.");
        }

        private void TakeWheel()
        {
            if (!session) return;
            session = false;
            listedGuns.Clear();
            fps.End();
        }

        /// <summary>Each installed cannon becomes a post the captain can take over (guns can be added or lost mid-fight).</summary>
        private void ListNewGuns()
        {
            VoxelHull hull = Hull;
            if (hull == null) return;
            foreach (ShipGunStation station in hull.GetComponentsInChildren<ShipGunStation>())
            {
                if (station == null || !listedGuns.Add(station)) continue;
                ShipGunStation gun = station;
                fps.AddInteractable(new FpsInteractable
                {
                    Position = () => gun != null && gun.isActiveAndEnabled ? gun.WorldPosition : Vector3.one * 1e6f,
                    Prompt = "Take command of this gun",
                    Radius = 0.65f,
                    Use = () => { if (gun != null) { fps.Mount(gun); Say("You have this gun. Aim with the mouse, click to fire — the crew reloads it."); } }
                });
            }
        }

        /// <summary>The rail nearest an enemy close enough to board; far away (never offered) otherwise.</summary>
        private Vector3 BoardingPoint()
        {
            if (director == null || Hull == null || (boarding != null && boarding.InProgress)) return Vector3.one * 1e6f;
            ShipCombatTarget enemy = director.NearestEnemy(out _, out float distance);
            if (enemy == null || distance > 8f) return Vector3.one * 1e6f;
            DeckSpace deck = DeckSpace.Create(Hull);
            Vector3 local = deck.ToLocal(enemy.transform.position);
            float along = Mathf.Clamp(local.z / Mathf.Max(0.1f, deck.HalfLength), -0.8f, 0.8f);
            return deck.ToWorld(deck.Rail(local.x >= 0f ? 1 : -1, along));
        }

        private void Board()
        {
            if (boarding == null) { Say("There is no way across."); return; }
            boarding.Begin(true);
            if (!boarding.InProgress) Say(boarding.Status);
        }

        private void Say(string text)
        {
            notice = text;
            noticeUntil = Time.time + 4f;
        }

        private string StatusText()
        {
            string crews = weapons != null && weapons.CrewGunnery ? weapons.CrewStatus : "";
            string enemy = "";
            if (director != null)
            {
                ShipCombatTarget nearest = director.NearestEnemy(out EncounterRecord record, out float distance);
                if (nearest != null)
                {
                    float hull = nearest.TotalVoxels > 0 ? (float)nearest.RemainingVoxels / nearest.TotalVoxels : 0f;
                    bool boardable = BoatshipsEncounterDirector.CanBoard(nearest, record, out string reason, true);
                    enemy = "Enemy " + Mathf.RoundToInt(distance) + " m, hull " + Mathf.RoundToInt(hull * 100f) + "%" +
                        (record != null && record.crewMax > 0 ? ", crew " + record.crew + "/" + record.crewMax : "") +
                        (distance <= 8f ? (boardable ? " — close enough to board! Go to the rail and lead the charge." : " — " + reason) :
                         distance <= 18f ? " — in pistol range [2]: pick off her crew" : "");
                }
            }
            return "Aboard your ship — she holds her course while you are away from the wheel" +
                (crews.Length > 0 ? "\n" + crews : "") + (enemy.Length > 0 ? "\n" + enemy : "") +
                (Time.time < noticeUntil ? "\n" + notice : "");
        }
    }
}
