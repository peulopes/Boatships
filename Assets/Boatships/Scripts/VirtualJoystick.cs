using UnityEngine;
using UnityEngine.EventSystems;

namespace Boatships
{
    /// <summary>Simple uGUI joystick for multitouch (each pointer has its own ID).</summary>
    public sealed class VirtualJoystick : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform background;
        [SerializeField] private RectTransform handle;

        private int activePointerId = int.MinValue;
        public Vector2 Direction { get; private set; }
        public RectTransform Background { set => background = value; }
        public RectTransform Handle { set => handle = value; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (activePointerId != int.MinValue) return;
            activePointerId = eventData.pointerId;
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (activePointerId != eventData.pointerId || background == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    background, eventData.position, eventData.pressEventCamera, out Vector2 local))
                return;

            float radius = Mathf.Min(background.rect.width, background.rect.height) * 0.5f;
            if (radius <= 0.001f) return;

            Direction = Vector2.ClampMagnitude(local / radius, 1f);
            if (handle != null)
                handle.anchoredPosition = Direction * radius * 0.65f;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (activePointerId != eventData.pointerId) return;
            activePointerId = int.MinValue;
            Direction = Vector2.zero;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
        }

        private void OnDisable()
        {
            activePointerId = int.MinValue;
            Direction = Vector2.zero;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
        }
    }
}
