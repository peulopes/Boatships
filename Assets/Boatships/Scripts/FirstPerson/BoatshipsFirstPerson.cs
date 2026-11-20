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
    }

    /// <summary>First-person view: walking on land (CharacterController) or on a moving ship deck
    /// (hull-local coordinates), cutlass and block, deck cannons, interactions and a keyboard/mouse HUD.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class BoatshipsFirstPerson : MonoBehaviour
    {
        public const float HumanHeight = 0.8f;
        public const float Reach = 0.75f;
        private const float EyeHeight = 0.7f;
        private const float WalkSpeed = 1.7f;
        private const float Gravity = 6f;
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
        private float swing;
        private float nextAttack;
        private BoatshipsDeckCannon mounted;
        private FpsInteractable focused;
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
        public float Health { get; private set; } = 100f;
        public bool Alive => Health > 0f;
        public bool Blocking { get; private set; }
        public int Kills { get; private set; }
        public bool Contributed { get; private set; }
        public Vector3 DeckLocal => deckLocal;
        public BoatshipsDeckCannon Mounted => mounted;
        public event Action Downed;

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
            yaw = facing; pitch = 5f; verticalSpeed = 0f;
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
            yaw = facing; pitch = 0f;
            Activate();
        }

        public void End()
        {
            if (mode == Mode.None) return;
            mode = Mode.None;
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
            Health = 100f; Kills = 0; Contributed = false; Blocking = false; flashAlpha = 0f;
            interactables.Clear();
            hudCanvas.gameObject.SetActive(true);
            SetExit(null, null);
            LateUpdate();
        }

        public void AddInteractable(FpsInteractable interactable) { if (interactable != null) interactables.Add(interactable); }
        public void RemoveInteractable(FpsInteractable interactable) => interactables.Remove(interactable);

        public void Mount(BoatshipsDeckCannon cannon)
        {
            if (mode != Mode.Deck || cannon == null) return;
            mounted = cannon;
        }

        public void TakeDamage(float amount, Vector3 from)
        {
            if (!Active || !Alive) return;
            Health = Mathf.Max(0f, Health - (Blocking ? amount * 0.25f : amount));
            flashAlpha = Blocking ? 0.15f : 0.45f;
            if (!Alive) Downed?.Invoke();
        }

        // ───────────────────────────── Frame ─────────────────────────────

        private void Update()
        {
            if (!Active) return;
            bool blocked = hud != null && hud.BlockingOverlayOpen;
            bool menu = view != null && view.MenuOpen;
            if (gameplayCanvas != null) gameplayCanvas.enabled = blocked;
            hudCanvas.enabled = !blocked;
            bool input = !blocked && !menu && Time.timeScale > 0f && Alive;
            UpdateCursor(input);
            if (mode == Mode.Deck && (deck == null || !deck.Valid)) { End(); return; }

            Vector2 move = input ? ReadMove() : Vector2.zero;
            Vector2 look = input ? ReadLook() : Vector2.zero;
            Blocking = input && mounted == null && ReadBlock();
            if (mounted != null)
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
                if (keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.fKey.wasPressedThisFrame)) Interact();
                // Tab leaves the scene, as in Bannerlord.
                if (keyboard != null && keyboard.tabKey.wasPressedThisFrame && exitAction != null) exitAction.Invoke();
                Mouse mouse = Mouse.current;
                bool click = mouse != null && Cursor.lockState == CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame;
                if (click) Attack();
            }
            UpdateHud();
        }

        private void LateUpdate()
        {
            if (!Active) return;
            if (mounted != null)
            {
                cam.transform.SetPositionAndRotation(mounted.ViewPosition, mounted.ViewRotation);
            }
            else if (mode == Mode.Deck && deck != null && deck.Valid)
            {
                Vector3 eye = deck.ToWorld(deckLocal) + Vector3.up * EyeHeight;
                cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(pitch, deck.FrameYaw + yaw, 0f));
            }
            else if (mode == Mode.Ground)
            {
                cam.transform.SetPositionAndRotation(rig.transform.position + Vector3.up * EyeHeight, Quaternion.Euler(pitch, yaw, 0f));
            }
            swing = Mathf.Max(0f, swing - Time.deltaTime * 3.2f);
            viewSword.gameObject.SetActive(mode == Mode.Deck && mounted == null);
            viewSword.localRotation = Blocking ? Quaternion.Euler(-10f, -60f, 80f) :
                Quaternion.Euler(-25f + Mathf.Sin(swing * Mathf.PI) * 85f, -10f + Mathf.Sin(swing * Mathf.PI) * 40f, 0f);
        }

        private void Move(Vector2 input, float dt)
        {
            if (dt <= 0f) return;
            Vector3 planar = new Vector3(input.x, 0f, input.y);
            if (mode == Mode.Deck)
            {
                Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * planar;
                deckLocal = deck.Constrain(deckLocal, deckLocal + direction * (WalkSpeed / deck.Scale) * dt);
                return;
            }
            Vector3 velocity = Quaternion.Euler(0f, yaw, 0f) * planar * WalkSpeed;
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -0.5f;
            verticalSpeed -= Gravity * dt;
            Vector3 before = rig.transform.position;
            controller.Move((velocity + Vector3.up * verticalSpeed) * dt);
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
            focused = null;
            if (mounted != null) return;
            Vector3 feet = FeetPosition;
            Vector3 forward = cam.transform.forward; forward.y = 0f;
            float best = float.MaxValue;
            foreach (FpsInteractable item in interactables)
            {
                Vector3 offset = item.Position() - feet;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > item.Radius || distance >= best) continue;
                if (distance > 0.35f && Vector3.Dot(forward.normalized, offset / distance) < 0.25f) continue;
                best = distance; focused = item;
            }
        }

        private void Interact()
        {
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
            if (mode != Mode.Deck || Blocking || Time.time < nextAttack) return;
            nextAttack = Time.time + 0.55f;
            swing = 1f;
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            BoatshipsDeckFighter best = null;
            float bestDistance = Reach;
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
            best.TakeHit(16f * (1f + fighting * 0.12f));
            Contributed = true;
            if (!best.Alive) Kills++;
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

        private void UpdateHud()
        {
            bool combat = mode == Mode.Deck;
            string keys = Cursor.lockState == CursorLockMode.Locked ? "" : "\n<color=#B8A27A>Click to look around  ·  Esc opens the menu</color>";
            if (mounted != null)
                promptText.text = (mounted.Ready ? "Aim and fire" : "Reloading…") + "  —  [Left click] fire   [E] leave the cannon" + keys;
            else if (focused != null) promptText.text = focused.Prompt + "  [E]" + keys;
            else promptText.text = combat ? "[Left click] attack   [Right mouse] block" + keys : keys.TrimStart('\n');
            healthText.text = combat ? "Health " + Mathf.CeilToInt(Health) + (Kills > 0 ? "\nFoes downed " + Kills : "") : "";
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
    public sealed class BoatshipsDeckCannon : MonoBehaviour
    {
        private const float MuzzleSpeed = 22f;
        private const float ShotGravity = 18f;
        private static Material ballMaterial;
        private static Material arcMaterial;

        private DeckSpace deck;
        private int side;
        private Transform pivot;
        private Transform muzzle;
        private ShipCombatTarget owner;
        private OceanWaves ocean;
        private float reloadSeconds;
        private float damage;
        private float readyAt;
        private LineRenderer arc;
        private readonly Vector3[] arcPoints = new Vector3[24];

        public float AimYaw { get; set; }
        public float AimPitch { get; set; } = 12f;
        public bool Ready => Time.time >= readyAt;
        public int Side => side;
        public Vector3 WorldPosition => transform.position;
        public Vector3 ViewPosition => pivot.position - pivot.forward * 0.42f + Vector3.up * 0.2f;
        public Quaternion ViewRotation => Quaternion.LookRotation(pivot.forward, Vector3.up) * Quaternion.Euler(4f, 0f, 0f);

        public static BoatshipsDeckCannon Create(DeckSpace deck, int side, ShipCombatTarget owner, OceanWaves ocean,
            float reloadSeconds, float damage)
        {
            GameObject root = new GameObject(side > 0 ? "Starboard Deck Gun" : "Port Deck Gun");
            root.transform.SetParent(deck.Frame, false);
            root.transform.localScale = Vector3.one / deck.Scale;
            Vector3 rail = deck.Rail(side, 0f);
            root.transform.localPosition = deck.Constrain(rail, rail - Vector3.right * side * (0.25f / deck.Scale));
            BoatshipsDeckCannon cannon = root.AddComponent<BoatshipsDeckCannon>();
            cannon.deck = deck; cannon.side = side; cannon.owner = owner; cannon.ocean = ocean;
            cannon.reloadSeconds = reloadSeconds; cannon.damage = damage;
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

        private void LateUpdate()
        {
            if (pivot == null) return;
            pivot.localRotation = Quaternion.Euler(-AimPitch, side * 90f + AimYaw, 0f);
            bool aiming = BoatshipsDeckFighter.Player != null && BoatshipsDeckFighter.Player.Mounted == this;
            if (arc == null) return;
            arc.enabled = aiming;
            if (!aiming) return;
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
            readyAt = Time.time + reloadSeconds;
            return true;
        }
    }
}
