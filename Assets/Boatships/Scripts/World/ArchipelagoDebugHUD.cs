using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Editor/mobile exploration controls. Not part of final game UX.</summary>
    public sealed class ArchipelagoDebugHUD : MonoBehaviour
    {
        [SerializeField] private ArchipelagoWorld world;
        [SerializeField] private Transform ship;
        [SerializeField] private Text label;
        [SerializeField] private Button seedButton;
        [SerializeField] private Button jumpButton;
        [SerializeField] private Button homeButton;
        private float nextRefresh;

        public void Configure(ArchipelagoWorld archipelago, Transform controlledShip,
            Text readout, Button newSeed, Button jump, Button home)
        {
            world = archipelago;
            ship = controlledShip;
            label = readout;
            seedButton = newSeed;
            jumpButton = jump;
            homeButton = home;
        }

        private void Start()
        {
            if (seedButton != null) seedButton.onClick.AddListener(ChangeSeed);
            if (jumpButton != null) jumpButton.onClick.AddListener(JumpToNearestIsland);
            if (homeButton != null) homeButton.onClick.AddListener(ReturnToStart);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.gKey.wasPressedThisFrame) ChangeSeed();
                if (keyboard.tKey.wasPressedThisFrame) JumpToNearestIsland();
                if (keyboard.homeKey.wasPressedThisFrame) ReturnToStart();
            }
            if (world == null || ship == null || label == null || Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.25f;
            string nearest = "None";
            if (world.TryNearestIsland(ship.position, out ArchipelagoWorld.IslandSite site))
            {
                Vector2 delta = site.Center - new Vector2(ship.position.x, ship.position.z);
                float distance = Mathf.Max(0f, delta.magnitude - site.Radius * 0.75f);
                int bearing = Mathf.RoundToInt((Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg + 360f) % 360f);
                nearest = site.Kind + " | shore ~" + distance.ToString("0") + " m | " + bearing + "°";
            }
            label.text = "ARCHIPELAGO / SEED " + world.Seed +
                "\nWorld " + world.SideLength.ToString("0") + "m square | Islands " + world.IslandCount +
                "\nActive island chunks: " + world.LoadedChunkCount +
                "\nShip: X " + ship.position.x.ToString("0") + " Z " + ship.position.z.ToString("0") +
                "\nNearest: " + nearest +
                "\nG new seed | T jump to shore | Home return";
        }

        public void ChangeSeed()
        {
            if (world == null) return;
            ReturnToStart();
            world.Regenerate(unchecked(world.Seed + 1));
        }

        public void JumpToNearestIsland()
        {
            if (world == null || ship == null || !world.TryNearestIsland(ship.position, out var site)) return;
            Vector2 direction = new Vector2(ship.position.x, ship.position.z) - site.Center;
            if (direction.sqrMagnitude < 0.001f) direction = Vector2.down;
            direction.Normalize();
            Vector2 target = site.Center + direction * (site.Radius * 0.78f + 6f);
            MoveShip(new Vector3(target.x, 0.8f, target.y));
        }

        public void ReturnToStart() { MoveShip(new Vector3(0f, 0.8f, 0f)); }

        private void MoveShip(Vector3 destination)
        {
            if (ship == null) return;
            // Prototype debug teleport only; legitimate travel uses the joystick.
            Rigidbody rigidbody = ship.GetComponent<Rigidbody>();
            if (rigidbody != null) rigidbody.position = destination;
            else ship.position = destination;
        }
    }
}
