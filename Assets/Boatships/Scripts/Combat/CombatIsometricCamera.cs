using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Boatships
{
    /// <summary>Like the starter follow camera, but ignores UI controls during mobile orbit.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CombatIsometricCamera : MonoBehaviour
    {
        [SerializeField] private Transform followTarget;
        [SerializeField] private float azimuth = 45f;
        [SerializeField, Range(20f, 75f)] private float elevation = 45f;
        [SerializeField] private float followDistance = 22f;
        [SerializeField] private float smoothing = 6f;
        [SerializeField] private float dragSensitivity = 0.14f;
        [SerializeField] private float zoomSensitivity = 0.02f;
        private Camera cam;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        public Transform Target { set => followTarget = value; }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 12f;
        }

        private void OnEnable() => EnhancedTouchSupport.Enable();
        private void OnDisable() => EnhancedTouchSupport.Disable();

        private bool OverUI(Vector2 position)
        {
            if (EventSystem.current == null) return false;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            return uiHits.Count > 0;
        }

        private void LateUpdate()
        {
            if (followTarget == null) return;
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                    azimuth += mouse.delta.ReadValue().x * dragSensitivity;
                cam.orthographicSize = Mathf.Clamp(cam.orthographicSize -
                    mouse.scroll.ReadValue().y * zoomSensitivity, 5f, 25f);
            }

            var touches = Touch.activeTouches;
            if (touches.Count == 1)
            {
                var t = touches[0];
                if (t.screenPosition.x > Screen.width * 0.5f && !OverUI(t.screenPosition))
                    azimuth += t.delta.x * dragSensitivity;
            }
            else if (touches.Count == 2)
            {
                var a = touches[0];
                var b = touches[1];
                if (!OverUI(a.screenPosition) && !OverUI(b.screenPosition))
                {
                    float current = Vector2.Distance(a.screenPosition, b.screenPosition);
                    float previous = Vector2.Distance(a.screenPosition - a.delta, b.screenPosition - b.delta);
                    cam.orthographicSize = Mathf.Clamp(cam.orthographicSize -
                        (current - previous) * 0.014f, 5f, 25f);
                }
            }

            Quaternion rotation = Quaternion.Euler(elevation, azimuth, 0f);
            Vector3 focus = followTarget.position + new Vector3(0f, 0.7f, 0f);
            Vector3 position = focus - (rotation * Vector3.forward) * followDistance;
            transform.position = Vector3.Lerp(transform.position, position,
                1f - Mathf.Exp(-smoothing * Time.deltaTime));
            transform.rotation = rotation;
        }
    }
}
