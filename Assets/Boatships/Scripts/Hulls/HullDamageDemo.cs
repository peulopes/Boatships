using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Boatships
{
    /// <summary>
    /// Temporary prototype damage harness. H damages the starboard midship area.
    /// Desktop left-click / mobile tap on the ship's coarse collider damages the
    /// point touched. Ignores touches on Unity UI (e.g., the move joystick).
    /// </summary>
    public sealed class HullDamageDemo : MonoBehaviour
    {
        [SerializeField] private VoxelHull hull;
        [SerializeField, Min(1f)] private float impactDamage = 85f;
        [SerializeField, Min(0.05f)] private float impactRadius = 0.43f;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        public VoxelHull Hull { set => hull = value; }

        private void Awake()
        {
            if (hull == null) hull = GetComponentInChildren<VoxelHull>();
        }

        private void Update()
        {
            if (hull == null || hull.Definition == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
                DamageTestSide();

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                TryDamageAtScreen(mouse.position.ReadValue());

            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                TryDamageAtScreen(touch.primaryTouch.position.ReadValue());
        }

        public void DamageTestSide()
        {
            HullDefinition d = hull.Definition;
            Vector3 local = new Vector3((d.Width - 1) * d.BlockSize * 0.5f,
                -d.BlockSize * 0.5f, 0f);
            hull.ApplyImpactWorld(hull.transform.TransformPoint(local), impactDamage, impactRadius);
        }

        private void TryDamageAtScreen(Vector2 screenPosition)
        {
            if (Camera.main == null || IsOnUI(screenPosition)) return;
            Ray ray = Camera.main.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f)) return;
            // The old prototype uses a single simplified BoxCollider on ship root.
            VoxelHull target = hit.collider.GetComponentInChildren<VoxelHull>();
            if (target == null) return;
            target.ApplyImpactWorld(hit.point, impactDamage, impactRadius);
        }

        private bool IsOnUI(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;
            uiHits.Clear();
            PointerEventData eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };
            EventSystem.current.RaycastAll(eventData, uiHits);
            return uiHits.Count != 0;
        }

        private void OnGUI()
        {
            if (hull == null || hull.Definition == null) return;
            GUILayout.BeginArea(new Rect(12f, 12f, 440f, 100f), GUI.skin.box);
            GUILayout.Label("Boatships | Hull damage test (prototype)");
            GUILayout.Label($"Hull voxels: {hull.RemainingBlocks} / {hull.TotalBlocks}");
            GUILayout.Label("Press H or tap/click the hull to destroy blocks");
            GUILayout.EndArea();
        }
    }
}
