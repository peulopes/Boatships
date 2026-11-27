using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Something the first-person player can use by walking up to it (a door, a cannon).</summary>
    public sealed class FpsInteractable
    {
        public Func<Vector3> Position;
        public string Prompt;
        public Action Use;
        public float Radius = 1f;
        /// <summary>When above zero, E must be held this long (patching a hole, bailing a bucket).</summary>
        public float HoldSeconds;
        /// <summary>A held action that repeats for as long as E stays down (bailing).</summary>
        public bool Repeat;
        /// <summary>Optional: the interaction is hidden while this returns false.</summary>
        public Func<bool> Available;
    }

    /// <summary>A gun the first-person player can take command of: aim with the mouse, fire with a click.</summary>
    public interface IMannedGun
    {
        float AimYaw { get; set; }
        float AimPitch { get; set; }
        bool Ready { get; }
        Vector3 WorldPosition { get; }
        Vector3 ViewPosition { get; }
        Quaternion ViewRotation { get; }
        bool Fire();
        /// <summary>Hand-loaded guns: a ball is being rammed home. Guns loaded by their crew never report loading.</summary>
        bool Loading => false;
        float LoadProgress => Ready ? 1f : 0f;
        /// <summary>Starts loading by hand ([R]). Returns false where the crew does the loading.</summary>
        bool StartLoading() => false;
    }

    public enum HandWeapon { Cutlass, Pistol }

    /// <summary>First-person view: walking on land (CharacterController) or on a moving ship deck
    /// (hull-local coordinates), cutlass and block, flintlock pistol, deck cannons, interactions and a keyboard/mouse HUD.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class BoatshipsFirstPerson : MonoBehaviour
    {
        public const float HumanHeight = 0.8f;
        public const float Reach = 0.75f;
        private const float EyeHeight = 0.7f;
        private const float WalkSpeed = 1.7f;
        private const float Gravity = 6f;
        // Jump apex of about 0.3 m on an 0.8 m figure (Space), with a short grace window after leaving a ledge.
        private const float JumpSpeed = 1.95f;
        private const float CoyoteTime = 0.12f;
        private const float JumpBuffer = 0.12f;
        private const float MouseLook = 0.12f;
        private enum Mode { None, Ground, Deck }

        private BoatshipsWorldMapView view;
        private BoatshipsCampaignHUD hud;
        private PortDocking docking;
        private Canvas gameplayCanvas;
        private BoatshipsCharacter character;

        private GameObject rig;
        private CharacterController controller;
        private Camera cam;
        private AudioListener listener;
        private Transform viewSword;
        private Mode mode;
        private DeckSpace deck;
        private Vector3 deckLocal;
        private float yaw;
        private float pitch;
        private float verticalSpeed;
        private float hop;           // height above the deck while jumping on a ship
        private float lastGrounded;  // Time.time the player last stood on something
        private float jumpQueuedAt = float.NegativeInfinity;
        private float swing;
        private float nextAttack;
        private IMannedGun mounted;
        private Transform viewPistol;
        private GameObject muzzleFlash;
        private float flashUntil;
        private float recoil;
        private bool pistolLoaded = true;
        private float reloadDoneAt = float.NegativeInfinity;
        private const float PistolReloadSeconds = 2.4f;
        private const float PistolRange = 18f;
        private FpsInteractable focused;
        private float holdProgress;
        private bool holdNeedsRelease;
        private bool atHelm;
        private float knockedUntil;
        private Action revived;
        private Vector3 crossFrom;
        private float crossStarted = float.NegativeInfinity;
        private const float CrossSeconds = 0.7f;
        private readonly List<FpsInteractable> interactables = new List<FpsInteractable>();
        private readonly List<Camera> disabledCameras = new List<Camera>();
        private readonly List<AudioListener> disabledListeners = new List<AudioListener>();
        private bool gameplayCanvasWasEnabled;
        private Action exitAction;
        private string status = "";
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        private Canvas hudCanvas;
        private Button exitButton;
        private Text promptText;
        private Text healthText;
        private Text statusText;
        private Image flash;
        private float flashAlpha;

        public bool Active => mode != Mode.None;
        /// <summary>True while any first-person session runs, so ship controls leave the keyboard alone.</summary>
        public static bool OnFoot { get; private set; }
        public bool Airborne => mode == Mode.Deck ? hop > 0f : mode == Mode.Ground && controller != null && !controller.isGrounded;
        public float Health { get; private set; } = 100f;

        // Equipment (set by the player's inventory): health from armour, weapon damage/reach/speed, blocking.
        public float MaxHealth { get; set; } = 100f;
        public float DamageMultiplier { get; set; } = 1f;
        public float ReachMultiplier { get; set; } = 1f;
        public float SwingSpeedMultiplier { get; set; } = 1f;
        public float BlockBonus { get; set; }
        /// <summary>[H]: asks the inventory for a bandage or grog; returns the health it restores (0: none left).</summary>
        public Func<float> HealRequested { get; set; }

        /// <summary>Applies the equipped weapon's look: longer reach, longer blade.</summary>
        public void SetWeaponLook(float reach, Color blade)
        {
            if (viewSword == null) return;
            viewSword.localScale = new Vector3(1f, 1f, Mathf.Clamp(reach, 0.6f, 1.6f));
            Renderer renderer = viewSword.GetComponentInChildren<Renderer>();
            if (renderer != null) renderer.sharedMaterial = BoatshipsHumanoid.MaterialFor(blade);
        }
        public bool Alive => Health > 0f;
        public bool Blocking { get; private set; }
        public int Kills { get; private set; }
        public bool Contributed { get; private set; }
        public Vector3 DeckLocal => deckLocal;
        /// <summary>World position of the player's eyes (where enemy musket fire aims).</summary>
        public Vector3 EyePosition => cam != null ? cam.transform.position : Vector3.zero;
        public IMannedGun Mounted => mounted;
        public HandWeapon Weapon { get; private set; } = HandWeapon.Cutlass;
        public bool Reloading => Time.time < reloadDoneAt;
        public event Action Downed;
        /// <summary>Standing at the ship's wheel: WASD steers the ship (read <see cref="HelmInput"/>).</summary>
        public bool AtHelm => atHelm;
        public Vector2 HelmInput { get; private set; }
        public bool KnockedOut => knockedUntil > 0f;
        public DeckSpace CurrentDeck => mode == Mode.Deck ? deck : null;
        public bool Crossing => Time.time - crossStarted < CrossSeconds;

        /// <summary>Credit for helping in a fight without landing a blow (e.g. plugging leaks).</summary>
        public void MarkContributed() => Contributed = true;

        public bool OnDeck(VoxelHull hull) => mode == Mode.Deck && deck != null && deck.Hull == hull;
        public void SetStatus(string text) => status = text ?? "";
        public void SetExit(string label, Action action)
        {
            exitAction = action;
            BoatshipsUI.SetButton(exitButton, (label ?? "Leave") + "  [Tab]", action != null);
            exitButton.gameObject.SetActive(action != null);
        }

        public void Configure(BoatshipsWorldMapView mapView, BoatshipsCampaignHUD campaignHud, PortDocking port,
            Canvas gameplay, BoatshipsCharacter captain)
        { view = mapView; hud = campaignHud; docking = port; gameplayCanvas = gameplay; character = captain; }

        private void Awake()
        {
            BoatshipsDeckFighter.Player = this;
            rig = new GameObject("First Person Rig");
            rig.transform.SetParent(transform, false);
            controller = rig.AddComponent<CharacterController>();
            controller.height = HumanHeight; controller.radius = 0.14f; controller.center = new Vector3(0f, HumanHeight * 0.5f, 0f);
            controller.stepOffset = 0.14f; controller.slopeLimit = 55f; controller.skinWidth = 0.02f; controller.minMoveDistance = 0f;
            GameObject eye = new GameObject("First Person Camera");
            eye.transform.SetParent(transform, false);
            cam = eye.AddComponent<Camera>();
            cam.fieldOfView = 70f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 700f;
            cam.enabled = false;
            listener = eye.AddComponent<AudioListener>();
            listener.enabled = false;
            GameObject arm = new GameObject("View Sword");
            arm.transform.SetParent(eye.transform, false);
            arm.transform.localPosition = new Vector3(0.11f, -0.11f, 0.16f);
            BoatshipsHumanoid.Part(PrimitiveType.Cube, "Blade", arm.transform, new Vector3(0f, 0f, 0.1f),
                new Vector3(0.012f, 0.02f, 0.2f), new Color(0.78f, 0.8f, 0.84f));
            BoatshipsHumanoid.Part(PrimitiveType.Cube, "Hilt", arm.transform, Vector3.zero,
                new Vector3(0.05f, 0.012f, 0.012f), new Color(0.6f, 0.45f, 0.15f));
            viewSword = arm.transform;

            // Flintlock pistol: walnut stock, iron barrel and lock, brass butt cap.
            GameObject pistol = new GameObject("View Pistol");
            pistol.transform.SetParent(eye.transform, false);
            pistol.transform.localPosition = new Vector3(0.1f, -0.1f, 0.2f);
            Color walnut = new Color(0.36f, 0.2f, 0.1f), iron = new Color(0.2f, 0.2f, 0.22f), brass = new Color(0.75f, 0.58f, 0.25f);
            BoatshipsHumanoid.Part(PrimitiveType.Cube, "Stock", pistol.transform, new Vector3(0f, 0f, 0.05f),
                new Vector3(0.022f, 0.026f, 0.13f), walnut);
            GameObject grip = BoatshipsHumanoid.Part(PrimitiveType.Cube, "Grip", pistol.transform, new Vector3(0f, -0.035f, -0.025f),
                new Vector3(0.024f, 0.075f, 0.032f), walnut);
            grip.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            BoatshipsHumanoid.Part(PrimitiveType.Cube, "Butt Cap", pistol.transform, new Vector3(0f, -0.07f, -0.045f),
                new Vector3(0.03f, 0.018f, 0.036f), brass).transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            GameObject tube = BoatshipsHumanoid.Part(PrimitiveType.Cylinder, "Barrel", pistol.transform, new Vector3(0f, 0.012f, 0.13f),
                new Vector3(0.014f, 0.08f, 0.014f), iron);
            tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            BoatshipsHumanoid.Part(PrimitiveType.Cube, "Lock", pistol.transform, new Vector3(0.013f, 0.012f, 0.02f),
                new Vector3(0.006f, 0.02f, 0.04f), iron);
            BoatshipsHumanoid.Part(PrimitiveType.Cube, "Hammer", pistol.transform, new Vector3(0.008f, 0.03f, 0.005f),
                new Vector3(0.006f, 0.022f, 0.008f), iron);
            muzzleFlash = BoatshipsHumanoid.Part(PrimitiveType.Sphere, "Muzzle Flash", pistol.transform, new Vector3(0f, 0.012f, 0.23f),
                new Vector3(0.05f, 0.05f, 0.08f), new Color(1f, 0.85f, 0.4f));
            muzzleFlash.SetActive(false);
            viewPistol = pistol.transform;
            pistol.SetActive(false);
            rig.SetActive(false);
            BuildHud();
            hudCanvas.gameObject.SetActive(false);
        }

        // ───────────────────────────── Sessions ─────────────────────────────

        public void BeginGround(Vector3 position, float facing)
        {
            End();
            mode = Mode.Ground;
            deck = null;
            yaw = facing; pitch = 5f; verticalSpeed = 0f; hop = 0f;
            rig.SetActive(true);
            controller.enabled = false;
            rig.transform.position = position;
            controller.enabled = true;
            Activate();
        }

        public void BeginDeck(VoxelHull hull, Vector3 local, float facing)
        {
            End();
            deck = DeckSpace.Create(hull);
            if (deck == null) return;
            mode = Mode.Deck;
            deckLocal = deck.Constrain(local, local);
            yaw = facing; pitch = 0f; verticalSpeed = 0f; hop = 0f;
            Activate();
        }

        /// <summary>Climbs (or swings) across to another ship's deck without ending the session.</summary>
        public void TransferToDeck(VoxelHull hull, Vector3 local)
        {
            if (mode != Mode.Deck) return;
            DeckSpace target = DeckSpace.Create(hull);
            if (target == null) return;
            crossFrom = cam.transform.position;
            crossStarted = Time.time;
            float worldYaw = deck != null && deck.Valid ? deck.FrameYaw + yaw : yaw;
            deck = target;
            deckLocal = deck.Constrain(local, local);
            yaw = worldYaw - deck.FrameYaw;
            mounted = null; atHelm = false; hop = 0f; verticalSpeed = 0f; holdProgress = 0f;
        }

        /// <summary>Takes the wheel standing at <paramref name="standLocal"/> (deck space), facing the bow.</summary>
        public void MountHelm(Vector3 standLocal)
        {
            if (mode != Mode.Deck || deck == null) return;
            mounted = null;
            atHelm = true;
            deckLocal = deck.Constrain(standLocal, standLocal);
            yaw = 0f;
        }

        /// <summary>Downed in a ship battle: out of action for a few seconds, then revived where onRevive puts us.</summary>
        public void KnockOut(float seconds, Action onRevive)
        {
            if (!Active) return;
            knockedUntil = Time.time + Mathf.Max(0.5f, seconds);
            revived = onRevive;
            mounted = null; atHelm = false; Blocking = false;
            flashAlpha = 0.8f;
        }

        public void End()
        {
            if (mode == Mode.None) return;
            mode = Mode.None;
            OnFoot = false;
            hop = 0f;
            atHelm = false; knockedUntil = 0f; revived = null; holdProgress = 0f; HelmInput = Vector2.zero;
            mounted = null; focused = null; deck = null;
            interactables.Clear();
            exitAction = null;
            status = "";
            foreach (Camera other in disabledCameras) if (other != null) other.enabled = true;
            foreach (AudioListener other in disabledListeners) if (other != null) other.enabled = true;
            disabledCameras.Clear(); disabledListeners.Clear();
            cam.enabled = false; listener.enabled = false;
            rig.SetActive(false);
            hudCanvas.gameObject.SetActive(false);
            if (gameplayCanvas != null) gameplayCanvas.enabled = gameplayCanvasWasEnabled;
            if (docking != null) docking.KeyboardShortcuts = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        private void Activate()
        {
            disabledCameras.Clear();
            foreach (Camera other in Camera.allCameras)
                if (other != cam && other.enabled) { other.enabled = false; disabledCameras.Add(other); }
            disabledListeners.Clear();
            foreach (AudioListener other in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (other != listener && other.enabled) { other.enabled = false; disabledListeners.Add(other); }
            cam.enabled = true; listener.enabled = true;
            if (gameplayCanvas != null) gameplayCanvasWasEnabled = gameplayCanvas.enabled;
            if (docking != null) docking.KeyboardShortcuts = false;
            Health = MaxHealth; Kills = 0; Contributed = false; Blocking = false; flashAlpha = 0f;
            interactables.Clear();
            hudCanvas.gameObject.SetActive(true);
            SetExit(null, null);
            LateUpdate();
        }

        public void AddInteractable(FpsInteractable interactable) { if (interactable != null) interactables.Add(interactable); }
        public void RemoveInteractable(FpsInteractable interactable) => interactables.Remove(interactable);

        public void Mount(IMannedGun cannon)
        {
            if (mode != Mode.Deck || cannon == null) return;
            mounted = cannon;
        }

        public void SelectWeapon(HandWeapon weapon)
        {
            if (Weapon == weapon) return;
            Weapon = weapon;
            nextAttack = Time.time + 0.25f;
        }

        private void StartReload()
        {
            if (pistolLoaded || Reloading) return;
            reloadDoneAt = Time.time + PistolReloadSeconds;
        }

        public void TakeDamage(float amount, Vector3 from)
        {
            if (!Active || !Alive) return;
            float taken = Blocking ? amount * Mathf.Max(0.05f, 0.25f - BlockBonus) : amount * (1f - Mathf.Clamp(BlockBonus, 0f, 0.5f));
            Health = Mathf.Max(0f, Health - taken);
            flashAlpha = Blocking ? 0.15f : 0.45f;
            if (!Alive) Downed?.Invoke();
        }

        // ───────────────────────────── Frame ─────────────────────────────

        private void Update()
        {
            if (!Active) return;
            // A gun shot off the deck while manned: step back from it.
            if (mounted is UnityEngine.Object gun && gun == null) mounted = null;
            bool blocked = hud != null && hud.BlockingOverlayOpen;
            bool menu = view != null && view.MenuOpen;
            if (gameplayCanvas != null) gameplayCanvas.enabled = blocked;
            hudCanvas.enabled = !blocked;
            if (knockedUntil > 0f && Time.time >= knockedUntil)
            {
                knockedUntil = 0f;
                Health = MaxHealth;
                Action onRevive = revived;
                revived = null;
                onRevive?.Invoke();
            }
            bool input = !blocked && !menu && Time.timeScale > 0f && Alive && !KnockedOut && !Crossing;
            UpdateCursor(input);
            if (mode == Mode.Deck && (deck == null || !deck.Valid)) { End(); return; }

            OnFoot = true;
            Vector2 move = input ? ReadMove() : Vector2.zero;
            Keyboard jumpKeys = Keyboard.current;
            if (input && mounted == null && jumpKeys != null && jumpKeys.spaceKey.wasPressedThisFrame) jumpQueuedAt = Time.time;
            Vector2 look = input ? ReadLook() : Vector2.zero;
            Blocking = input && mounted == null && !atHelm && Weapon == HandWeapon.Cutlass && ReadBlock();
            if (!pistolLoaded && reloadDoneAt > 0f && Time.time >= reloadDoneAt) { pistolLoaded = true; reloadDoneAt = float.NegativeInfinity; }
            HelmInput = atHelm ? move : Vector2.zero;
            if (atHelm)
            {
                // Hands on the wheel: look around freely, the ship (not the player) moves.
                yaw = Mathf.Clamp(yaw + look.x, -120f, 120f);
                pitch = Mathf.Clamp(pitch - look.y, -60f, 60f);
            }
            else if (mounted != null)
            {
                mounted.AimYaw = Mathf.Clamp(mounted.AimYaw + look.x, -55f, 55f);
                mounted.AimPitch = Mathf.Clamp(mounted.AimPitch + look.y, -6f, 40f);
            }
            else
            {
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -80f, 80f);
                Move(move, Time.deltaTime);
            }
            UpdateFocus();
            if (input)
            {
                Keyboard keyboard = Keyboard.current;
                bool usePressed = keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.fKey.wasPressedThisFrame);
                bool useHeld = keyboard != null && (keyboard.eKey.isPressed || keyboard.fKey.isPressed);
                if (mounted != null || atHelm || focused == null || focused.HoldSeconds <= 0f)
                {
                    holdProgress = 0f;
                    if (usePressed) Interact();
                }
                else if (!useHeld) { holdProgress = 0f; holdNeedsRelease = false; }
                else if (!holdNeedsRelease)
                {
                    holdProgress += Time.deltaTime / focused.HoldSeconds;
                    if (holdProgress >= 1f)
                    {
                        holdProgress = 0f;
                        if (!focused.Repeat) holdNeedsRelease = true;
                        focused.Use?.Invoke();
                        Contributed = true;
                    }
                }
                // [H]: bandages or grog from the bag.
                if (keyboard != null && keyboard.hKey.wasPressedThisFrame && Health < MaxHealth && HealRequested != null)
                {
                    float heal = HealRequested();
                    if (heal > 0f) { Health = Mathf.Min(MaxHealth, Health + heal); status = "You patch yourself up (+" + Mathf.RoundToInt(heal) + ")."; }
                    else status = "No bandages or grog in your bag.";
                }
                // Cannons are loaded by hand, one ball at a time.
                if (mounted != null && keyboard != null && keyboard.rKey.wasPressedThisFrame && mounted.StartLoading()) Contributed = true;
                // Tab leaves the scene, as in Bannerlord.
                if (keyboard != null && keyboard.tabKey.wasPressedThisFrame && exitAction != null) exitAction.Invoke();
                if (keyboard != null && mode == Mode.Deck && mounted == null && !atHelm)
                {
                    if (keyboard.digit1Key.wasPressedThisFrame) SelectWeapon(HandWeapon.Cutlass);
                    if (keyboard.digit2Key.wasPressedThisFrame) SelectWeapon(HandWeapon.Pistol);
                    if (keyboard.rKey.wasPressedThisFrame && Weapon == HandWeapon.Pistol) StartReload();
                }
                Mouse mouse = Mouse.current;
                if (mode == Mode.Deck && mounted == null && !atHelm && mouse != null && Cursor.lockState == CursorLockMode.Locked)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                        SelectWeapon(Weapon == HandWeapon.Cutlass ? HandWeapon.Pistol : HandWeapon.Cutlass);
                }
                bool click = mouse != null && Cursor.lockState == CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame;
                if (click) Attack();
            }
            UpdateHud();
        }

        private void LateUpdate()
        {
            if (!Active) return;
            if (mounted is UnityEngine.Object gun && gun == null) mounted = null;
            if (mounted != null)
            {
                cam.transform.SetPositionAndRotation(mounted.ViewPosition, mounted.ViewRotation);
            }
            else if (mode == Mode.Deck && deck != null && deck.Valid)
            {
                Vector3 eye = deck.ToWorld(deckLocal) + Vector3.up * (EyeHeight + hop);
                float crossing = (Time.time - crossStarted) / CrossSeconds;
                // Climbing across between ships: an arc from where we were to the other deck.
                if (crossing < 1f) eye = Vector3.Lerp(crossFrom, eye, Mathf.SmoothStep(0f, 1f, crossing)) + Vector3.up * (Mathf.Sin(crossing * Mathf.PI) * 0.6f);
                if (KnockedOut) eye += Vector3.down * (EyeHeight * 0.75f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(KnockedOut ? -30f : pitch, deck.FrameYaw + yaw, KnockedOut ? 25f : 0f));
            }
            else if (mode == Mode.Ground)
            {
                cam.transform.SetPositionAndRotation(rig.transform.position + Vector3.up * EyeHeight, Quaternion.Euler(pitch, yaw, 0f));
            }
            swing = Mathf.Max(0f, swing - Time.deltaTime * 3.2f);
            bool armed = mode == Mode.Deck && mounted == null && !atHelm && !KnockedOut;
            viewSword.gameObject.SetActive(armed && Weapon == HandWeapon.Cutlass);
            viewSword.localRotation = Blocking ? Quaternion.Euler(-10f, -60f, 80f) :
                Quaternion.Euler(-25f + Mathf.Sin(swing * Mathf.PI) * 85f, -10f + Mathf.Sin(swing * Mathf.PI) * 40f, 0f);
            viewPistol.gameObject.SetActive(armed && Weapon == HandWeapon.Pistol);
            recoil = Mathf.Max(0f, recoil - Time.deltaTime * 5f);
            // Reloading tips the pistol down to ram powder and ball; firing kicks it up.
            float reload = Reloading ? Mathf.Sin(Mathf.Clamp01(1f - (reloadDoneAt - Time.time) / PistolReloadSeconds) * Mathf.PI) : 0f;
            viewPistol.localRotation = Quaternion.Euler(reload * 55f - recoil * 22f, reload * -20f, reload * 25f);
            viewPistol.localPosition = new Vector3(0.1f, -0.1f - reload * 0.05f, 0.2f - recoil * 0.04f);
            muzzleFlash.SetActive(Time.time < flashUntil);
        }

        private void Move(Vector2 input, float dt)
        {
            if (dt <= 0f || atHelm) return;
            Vector3 planar = new Vector3(input.x, 0f, input.y);
            bool wantsJump = Time.time - jumpQueuedAt <= JumpBuffer;
            if (mode == Mode.Deck)
            {
                Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * planar;
                deckLocal = deck.Constrain(deckLocal, deckLocal + direction * (WalkSpeed / deck.Scale) * dt);
                // The deck is a hull-local plane, so a jump is a height offset riding along with the ship.
                if (hop <= 0f && wantsJump) { verticalSpeed = JumpSpeed; hop = 0.0001f; jumpQueuedAt = float.NegativeInfinity; }
                if (hop > 0f)
                {
                    verticalSpeed -= Gravity * dt;
                    hop += verticalSpeed * dt;
                    if (hop <= 0f) { hop = 0f; verticalSpeed = 0f; }
                }
                return;
            }
            Vector3 velocity = Quaternion.Euler(0f, yaw, 0f) * planar * WalkSpeed;
            if (controller.isGrounded)
            {
                lastGrounded = Time.time;
                if (verticalSpeed < 0f) verticalSpeed = -0.5f;
            }
            if (wantsJump && verticalSpeed <= 0f && Time.time - lastGrounded <= CoyoteTime)
            {
                verticalSpeed = JumpSpeed;
                lastGrounded = float.NegativeInfinity;
                jumpQueuedAt = float.NegativeInfinity;
            }
            verticalSpeed -= Gravity * dt;
            Vector3 before = rig.transform.position;
            CollisionFlags hit = controller.Move((velocity + Vector3.up * verticalSpeed) * dt);
            if ((hit & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
            if (Walkable(rig.transform.position)) return;
            // Never walk off into the sea (piers and decks are fine).
            controller.enabled = false;
            rig.transform.position = before;
            controller.enabled = true;
            verticalSpeed = 0f;
        }

        private static bool Walkable(Vector3 position) =>
            Physics.Raycast(position + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 3f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.point.y > 0.05f;

        private Vector3 FeetPosition => mode == Mode.Deck && deck != null && deck.Valid ? deck.ToWorld(deckLocal) : rig.transform.position;

        private void UpdateFocus()
        {
            FpsInteractable previous = focused;
            focused = null;
            if (mounted != null || atHelm || KnockedOut) { holdProgress = 0f; return; }
            Vector3 feet = FeetPosition;
            Vector3 forward = cam.transform.forward; forward.y = 0f;
            float best = float.MaxValue;
            foreach (FpsInteractable item in interactables)
            {
                if (item.Available != null && !item.Available()) continue;
                Vector3 offset = item.Position() - feet;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > item.Radius || distance >= best) continue;
                if (distance > 0.35f && Vector3.Dot(forward.normalized, offset / distance) < 0.25f) continue;
                best = distance; focused = item;
            }
            if (focused != previous) { holdProgress = 0f; holdNeedsRelease = false; }
        }

        private void Interact()
        {
            if (atHelm) { atHelm = false; return; }
            if (mounted != null) { mounted = null; return; }
            focused?.Use?.Invoke();
        }

        private void Attack()
        {
            if (mounted != null)
            {
                if (mounted.Fire()) Contributed = true;
                return;
            }
            if (mode != Mode.Deck || atHelm) return;
            if (Weapon == HandWeapon.Pistol) { FirePistol(); return; }
            if (Blocking || Time.time < nextAttack) return;
            nextAttack = Time.time + 0.55f / Mathf.Max(0.3f, SwingSpeedMultiplier);
            swing = 1f;
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            BoatshipsDeckFighter best = null;
            float bestDistance = Reach * ReachMultiplier;
            foreach (BoatshipsDeckFighter fighter in BoatshipsDeckFighter.All)
            {
                if (fighter.Team != 1 || !fighter.Alive || fighter.Deck == null || fighter.Deck.Hull != deck.Hull) continue;
                Vector3 offset = (fighter.Local - deckLocal) * deck.Scale;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > bestDistance) continue;
                if (distance > 0.15f && Vector3.Angle(forward, offset) > 65f) continue;
                bestDistance = distance; best = fighter;
            }
            if (best == null) return;
            int fighting = character != null ? character.Skill(CharacterSkill.Fighting) : 0;
            best.TakeHit(16f * DamageMultiplier * (1f + fighting * 0.12f));
            Contributed = true;
            if (!best.Alive) Kills++;
        }

        /// <summary>One ball, then a reload. Hits the first foe along the sight line, on any deck within range.</summary>
        private void FirePistol()
        {
            if (Reloading || Time.time < nextAttack) return;
            if (!pistolLoaded) { StartReload(); return; }
            pistolLoaded = false;
            nextAttack = Time.time + 0.3f;
            flashUntil = Time.time + 0.06f;
            recoil = 1f;
            int gunnery = character != null ? character.Skill(CharacterSkill.Gunnery) : 0;
            float spread = Mathf.Lerp(2.2f, 0.6f, Mathf.Clamp01(gunnery / 10f));
            Vector3 direction = Quaternion.Euler(UnityEngine.Random.Range(-spread, spread), UnityEngine.Random.Range(-spread, spread), 0f) *
                Vector3.forward;
            var ray = new Ray(cam.transform.position, cam.transform.rotation * direction);
            BoatshipsDeckFighter best = null;
            float bestDistance = PistolRange;
            foreach (BoatshipsDeckFighter fighter in BoatshipsDeckFighter.All)
            {
                if (fighter == null || fighter.Team != 1 || !fighter.Alive) continue;
                Vector3 chest = fighter.transform.position + fighter.transform.up * (HumanHeight * 0.55f);
                if (!RayHitsSphere(ray, chest, HumanHeight * 0.3f, out float distance) || distance >= bestDistance) continue;
                bestDistance = distance; best = fighter;
            }
            Contributed = true;
            if (best == null) return;
            best.TakeHit(48f * (1f + gunnery * 0.06f), Incapacitation.Firearm);
            if (!best.Alive) Kills++;
        }

        private static bool RayHitsSphere(Ray ray, Vector3 centre, float radius, out float distance)
        {
            Vector3 toCentre = centre - ray.origin;
            float along = Vector3.Dot(toCentre, ray.direction);
            distance = along;
            if (along < 0f) return false;
            return (toCentre - ray.direction * along).sqrMagnitude <= radius * radius;
        }

        // ───────────────────────────── Input ─────────────────────────────

        private void UpdateCursor(bool input)
        {
            if (Application.isMobilePlatform) return;
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (!input || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
            {
                if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                return;
            }
            if (Cursor.lockState != CursorLockMode.Locked && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                !OverUI(mouse.position.ReadValue()))
            {
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            }
        }

        private Vector2 ReadMove()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return Vector2.zero;
            float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
                (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private Vector2 ReadLook()
        {
            Vector2 look = Vector2.zero;
            Mouse mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked) look += mouse.delta.ReadValue() * MouseLook;
            return look;
        }

        private bool ReadBlock()
        {
            Mouse mouse = Mouse.current;
            return mouse != null && Cursor.lockState == CursorLockMode.Locked && mouse.rightButton.isPressed;
        }

        private bool OverUI(Vector2 position)
        {
            if (EventSystem.current == null) return false;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            return uiHits.Count > 0;
        }

        // ───────────────────────────── HUD ─────────────────────────────

        private void BuildHud()
        {
            hudCanvas = BoatshipsUI.CreateCanvas("Boatships First Person HUD", 8, transform);
            RectTransform safe = BoatshipsUI.SafeArea(hudCanvas);
            flash = BoatshipsUI.Image("Damage Flash", hudCanvas.transform, new Color(0.8f, 0f, 0f, 0f));
            BoatshipsUI.Stretch(flash.rectTransform);
            flash.raycastTarget = false;
            flash.transform.SetAsFirstSibling();

            Image cross = BoatshipsUI.Image("Crosshair", safe, new Color(1f, 1f, 1f, 0.8f));
            cross.sprite = BoatshipsUI.Circle; cross.raycastTarget = false;
            cross.rectTransform.anchorMin = cross.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            cross.rectTransform.sizeDelta = new Vector2(10f, 10f);

            promptText = BoatshipsUI.Label("", safe, 28, TextAnchor.MiddleCenter);
            BoatshipsUI.Anchor(promptText.rectTransform, 0.2f, 0.28f, 0.8f, 0.38f);
            promptText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
            statusText = BoatshipsUI.Label("", safe, 22, TextAnchor.UpperCenter);
            BoatshipsUI.Anchor(statusText.rectTransform, 0.25f, 0.82f, 0.75f, 0.985f);
            statusText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
            healthText = BoatshipsUI.Label("", safe, 24, TextAnchor.UpperRight);
            BoatshipsUI.Anchor(healthText.rectTransform, 0.75f, 0.88f, 0.985f, 0.985f);
            healthText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

            exitButton = BoatshipsUI.Button("Leave  [Tab]", safe, () => exitAction?.Invoke(), 52f, 20);
            Place((RectTransform)exitButton.transform, new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(300f, 52f));
            BoatshipsUI.StyleButton(exitButton, BoatshipsUI.Accent);
            exitButton.GetComponentInChildren<Text>().color = BoatshipsUI.Ink;
            Button menu = BoatshipsUI.Button("Menu  [Esc]", safe,
                () => { if (hud != null) hud.OpenCampaignPage(BoatshipsCampaignHUD.SettingsPage); }, 52f, 20);
            Place((RectTransform)menu.transform, new Vector2(0f, 1f), new Vector2(324f, -16f), new Vector2(170f, 52f));
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }

        private static string Bar(float progress)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(progress * 10f), 0, 10);
            return new string('■', filled) + new string('□', 10 - filled);
        }

        private int shownPromptState = -1, shownHealth = int.MinValue + 1, shownKills = -1, shownWeaponState = -1;
        private FpsInteractable shownFocus;

        private void UpdateHud()
        {
            bool combat = mode == Mode.Deck;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            // The prompt and health lines only change on events; rebuild their strings only then (not every frame).
            bool pistol = combat && Weapon == HandWeapon.Pistol;
            int pistolState = !pistol ? 0 : Reloading ? 1 : pistolLoaded ? 2 : 3;
            int kind, bucket = 0;
            if (KnockedOut) { kind = 7; bucket = Mathf.CeilToInt(knockedUntil - Time.time); }
            else if (atHelm) kind = 6;
            else if (mounted != null)
            {
                kind = mounted.Ready ? 3 : mounted.Loading ? 5 : 4;
                if (mounted.Loading) bucket = Mathf.FloorToInt(mounted.LoadProgress * 10f);
            }
            else if (focused != null) { kind = 2; bucket = Mathf.FloorToInt(holdProgress * 10f); }
            else if (pistol) { kind = 8; bucket = pistolState; }
            else kind = combat ? 1 : 0;
            int promptState = ((kind * 32 + bucket) * 2 + (locked ? 1 : 0));
            if (promptState != shownPromptState || focused != shownFocus)
            {
                shownPromptState = promptState; shownFocus = focused;
                string keys = locked ? "" : "\n<color=#B8A27A>Click to look around  ·  Esc opens the menu</color>";
                if (KnockedOut) promptText.text = "You are knocked out…  back on your feet in " + bucket + "s";
                else if (atHelm) promptText.text = "At the wheel  —  [A/D] steer   [W/S] raise or lower sail   [E] let go" + keys;
                else if (mounted != null)
                    promptText.text = (mounted.Ready ? "Loaded — aim and [Left click] fire" :
                        mounted.Loading ? "Loading…  " + Bar(mounted.LoadProgress) : "Empty — [R] load a cannonball") +
                        "   [E] leave the cannon" + keys;
                else if (focused != null)
                    promptText.text = focused.Prompt + (focused.HoldSeconds > 0f ? "  [hold E]" + (holdProgress > 0f ? "  " + Bar(holdProgress) : "") : "  [E]") + keys;
                else if (pistol)
                    promptText.text = (Reloading ? "Reloading the pistol…" : pistolLoaded ? "[Left click] fire" : "[R] reload") +
                        "   [1] cutlass   [Space] jump" + keys;
                else promptText.text = combat ? "[Left click] slash   [Right mouse] block   [2] pistol   [Space] jump" + keys : keys.TrimStart('\n');
            }
            int health = combat ? Mathf.CeilToInt(Health) : int.MinValue;
            if (health != shownHealth || Kills != shownKills || pistolState != shownWeaponState)
            {
                shownHealth = health; shownKills = Kills; shownWeaponState = pistolState;
                string weapon = pistol ? "Flintlock — " + (Reloading ? "reloading" : pistolLoaded ? "loaded" : "empty") : "Cutlass";
                healthText.text = combat ? "Health " + health + "\n" + weapon + (Kills > 0 ? "\nFoes downed " + Kills : "") : "";
            }
            statusText.text = status;
            flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.deltaTime * 1.5f);
            flash.color = new Color(0.8f, 0f, 0f, flashAlpha);
        }

        private void OnDestroy()
        {
            End();
            if (BoatshipsDeckFighter.Player == this) BoatshipsDeckFighter.Player = null;
        }
    }

    /// <summary>A deck gun the first-person player can man: aim with the look controls, fire, reload.</summary>
    public sealed class BoatshipsDeckCannon : MonoBehaviour, IMannedGun
    {
        public const float MuzzleSpeed = 22f;
        public const float ShotGravity = 18f;
        public const float MaxYaw = 55f, MinPitch = -6f, MaxPitch = 40f;
        private static Material ballMaterial;
        private static Material arcMaterial;

        private DeckSpace deck;
        private float baseYaw;
        private Vector3 local;
        private Transform pivot;
        private Transform muzzle;
        private ShipCombatTarget owner;
        private OceanWaves ocean;
        private float loadSeconds;
        private float damage;
        private float loadedAt;
        private LineRenderer arc;
        private readonly Vector3[] arcPoints = new Vector3[24];

        public float AimYaw { get; set; }
        public float AimPitch { get; set; } = 12f;
        /// <summary>A ball is in the barrel.</summary>
        public bool Ready { get; private set; } = true;
        public bool Loading => loadedAt > 0f;
        public float LoadProgress => Loading ? Mathf.Clamp01(1f - (loadedAt - Time.time) / loadSeconds) : Ready ? 1f : 0f;
        /// <summary>The deck sailor working this gun, if any (the player takes precedence).</summary>
        public BoatshipsDeckFighter Operator { get; set; }
        public ShipCombatTarget Owner => owner;
        public DeckSpace Deck => deck;
        /// <summary>+1 starboard, -1 port, 0 bow/stern gun.</summary>
        public int Side => Mathf.Abs(Mathf.DeltaAngle(baseYaw, 90f)) < 46f ? 1 : Mathf.Abs(Mathf.DeltaAngle(baseYaw, -90f)) < 46f ? -1 : 0;
        public Vector3 WorldPosition => transform.position;
        public Vector3 Local => local;
        /// <summary>Where a gunner stands: just inboard of the carriage.</summary>
        public Vector3 StandLocal => deck.Constrain(local, local - Quaternion.Euler(0f, baseYaw, 0f) * Vector3.forward * (0.32f / deck.Scale));
        public Vector3 ViewPosition => pivot.position - pivot.forward * 0.42f + Vector3.up * 0.2f;
        public Quaternion ViewRotation => Quaternion.LookRotation(pivot.forward, Vector3.up) * Quaternion.Euler(4f, 0f, 0f);

        /// <summary>A gun at the rail on one side (+1 starboard, -1 port), amidships.</summary>
        public static BoatshipsDeckCannon Create(DeckSpace deck, int side, ShipCombatTarget owner, OceanWaves ocean,
            float reloadSeconds, float damage)
        {
            Vector3 rail = deck.Rail(side, 0f);
            Vector3 local = deck.Constrain(rail, rail - Vector3.right * side * (0.25f / deck.Scale));
            return CreateAt(deck, local, side * 90f, owner, ocean, reloadSeconds, damage);
        }

        /// <summary>A gun at a deck point (hull-local), pointing <paramref name="yaw"/> degrees from the bow.</summary>
        public static BoatshipsDeckCannon CreateAt(DeckSpace deck, Vector3 local, float yaw, ShipCombatTarget owner, OceanWaves ocean,
            float loadSeconds, float damage)
        {
            GameObject root = new GameObject(yaw > 0f ? "Starboard Deck Gun" : "Port Deck Gun");
            root.transform.SetParent(deck.Frame, false);
            root.transform.localScale = Vector3.one / deck.Scale;
            BoatshipsDeckCannon cannon = root.AddComponent<BoatshipsDeckCannon>();
            cannon.deck = deck; cannon.baseYaw = yaw; cannon.owner = owner; cannon.ocean = ocean;
            cannon.loadSeconds = Mathf.Max(0.5f, loadSeconds); cannon.damage = damage;
            cannon.local = deck.Constrain(local, local);
            root.transform.localPosition = cannon.local;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            BoatshipsHumanoid.Part(PrimitiveType.Cube, "Carriage", root.transform, new Vector3(0f, 0.06f, 0f),
                new Vector3(0.22f, 0.12f, 0.26f), new Color(0.35f, 0.22f, 0.1f));
            cannon.pivot = new GameObject("Barrel Pivot").transform;
            cannon.pivot.SetParent(root.transform, false);
            cannon.pivot.localPosition = new Vector3(0f, 0.16f, 0f);
            GameObject barrel = BoatshipsHumanoid.Part(PrimitiveType.Cylinder, "Barrel", cannon.pivot, new Vector3(0f, 0f, 0.14f),
                new Vector3(0.09f, 0.2f, 0.09f), new Color(0.15f, 0.15f, 0.16f));
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cannon.muzzle = new GameObject("Muzzle").transform;
            cannon.muzzle.SetParent(cannon.pivot, false);
            cannon.muzzle.localPosition = new Vector3(0f, 0f, 0.38f);
            if (arcMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                if (shader != null) arcMaterial = new Material(shader) { color = new Color(0.4f, 1f, 1f, 0.8f) };
            }
            if (arcMaterial != null)
            {
                cannon.arc = root.AddComponent<LineRenderer>();
                cannon.arc.sharedMaterial = arcMaterial;
                cannon.arc.useWorldSpace = true;
                cannon.arc.positionCount = cannon.arcPoints.Length;
                cannon.arc.widthMultiplier = 0.06f;
                cannon.arc.enabled = false;
            }
            return cannon;
        }

        /// <summary>Starts ramming a ball home. Returns false if already loaded or loading.</summary>
        public bool StartLoading()
        {
            if (Ready || Loading) return false;
            loadedAt = Time.time + loadSeconds;
            return true;
        }

        /// <summary>Solves the gun's yaw (relative to its mount) and pitch to land a ball at a world point.</summary>
        public bool SolveAim(Vector3 target, out float yaw, out float pitch)
        {
            yaw = 0f; pitch = 0f;
            if (muzzle == null) return false;
            Vector3 from = pivot.position;
            Vector3 delta = target - from;
            Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
            float d = flat.magnitude;
            if (d < 0.5f) return false;
            float v2 = MuzzleSpeed * MuzzleSpeed, g = ShotGravity;
            float discriminant = v2 * v2 - g * (g * d * d + 2f * delta.y * v2);
            if (discriminant < 0f) return false;
            pitch = Mathf.Atan((v2 - Mathf.Sqrt(discriminant)) / (g * d)) * Mathf.Rad2Deg;
            Vector3 localDirection = deck.Frame.InverseTransformDirection(flat / d);
            yaw = Mathf.DeltaAngle(baseYaw, Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg);
            return Mathf.Abs(yaw) <= MaxYaw && pitch >= MinPitch && pitch <= MaxPitch;
        }

        private void LateUpdate()
        {
            if (pivot == null) return;
            if (Loading && Time.time >= loadedAt) { loadedAt = 0f; Ready = true; }
            AimYaw = Mathf.Clamp(AimYaw, -MaxYaw, MaxYaw);
            AimPitch = Mathf.Clamp(AimPitch, MinPitch, MaxPitch);
            pivot.localRotation = Quaternion.Euler(-AimPitch, AimYaw, 0f);
            bool aiming = BoatshipsDeckFighter.Player != null && ReferenceEquals(BoatshipsDeckFighter.Player.Mounted, this);
            if (arc == null) return;
            arc.enabled = aiming && Ready;
            if (!arc.enabled) return;
            Vector3 position = muzzle.position;
            Vector3 velocity = pivot.forward * MuzzleSpeed;
            for (int i = 0; i < arcPoints.Length; i++)
            {
                arcPoints[i] = position;
                position += velocity * 0.07f;
                velocity += Vector3.down * ShotGravity * 0.07f;
            }
            arc.SetPositions(arcPoints);
        }

        public bool Fire()
        {
            if (!Ready || muzzle == null) return false;
            if (ballMaterial == null) ballMaterial = BoatshipsHumanoid.MaterialFor(new Color(0.12f, 0.12f, 0.13f));
            BoatshipsProjectile projectile = BoatshipsProjectile.Spawn(muzzle.position, ballMaterial);
            projectile.Launch(pivot.forward * MuzzleSpeed, owner, ocean, damage, 0.45f, ShotGravity);
            Ready = false;
            return true;
        }
    }
}
