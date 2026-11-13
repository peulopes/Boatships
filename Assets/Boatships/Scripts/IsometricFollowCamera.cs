using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Boatships
{
    /// <summary>Orthographic follow camera with optional mouse and touch orbit/zoom.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class IsometricFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float azimuth = 45f;
        [SerializeField, Range(20f, 75f)] private float elevation = 45f;
        [SerializeField] private float distance = 22f;
        [SerializeField] private float followSmoothness = 6f;
        [SerializeField] private float zoomSensitivity = 0.02f;
        [SerializeField] private float dragSensitivity = 0.14f;
        [SerializeField] private Vector3 focusOffset = new Vector3(0f, 0.7f, 0f);

        private Camera cameraComponent;
        public Transform Target { set => target = value; }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        private void Awake()
        {
            cameraComponent = GetComponent<Camera>();
            cameraComponent.orthographic = true;
            cameraComponent.orthographicSize = 12f;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                    azimuth += mouse.delta.ReadValue().x * dragSensitivity;

                cameraComponent.orthographicSize = Mathf.Clamp(
                    cameraComponent.orthographicSize - mouse.scroll.ReadValue().y * zoomSensitivity,
                    5f, 25f);
            }

            // Mobile: drag on RIGHT half to orbit; two fingers there to zoom.
            var touches = Touch.activeTouches;
            if (touches.Count == 1)
            {
                var t = touches[0];
                if (t.screenPosition.x > Screen.width * 0.5f)
                    azimuth += t.delta.x * dragSensitivity;
            }
            else if (touches.Count == 2)
            {
                var a = touches[0];
                var b = touches[1];
                if (a.screenPosition.x > Screen.width * 0.45f &&
                    b.screenPosition.x > Screen.width * 0.45f)
                {
                    float currentDistance = Vector2.Distance(a.screenPosition, b.screenPosition);
                    float previousDistance = Vector2.Distance(
                        a.screenPosition - a.delta, b.screenPosition - b.delta);
                    cameraComponent.orthographicSize = Mathf.Clamp(
                        cameraComponent.orthographicSize -
                        (currentDistance - previousDistance) * 0.014f,
                        5f, 25f);
                }
            }

            Quaternion rotation = Quaternion.Euler(elevation, azimuth, 0f);
            Vector3 focus = target.position + focusOffset;
            Vector3 destination = focus - (rotation * Vector3.forward) * distance;
            float blend = 1f - Mathf.Exp(-followSmoothness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, destination, blend);
            transform.rotation = rotation;
        }

        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }
    }
}
