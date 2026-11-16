using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>One opt-in home for prototype readouts and testing actions.</summary>
    public sealed class BoatshipsDebugMenu : MonoBehaviour
    {
        private GameObject panel;
        private RectTransform content;
        public static bool IsOpen { get; private set; }

        public static void Register(Component owner, params Component[] controls)
        {
            Canvas canvas = owner.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            BoatshipsDebugMenu menu = canvas.GetComponent<BoatshipsDebugMenu>();
            if (menu == null)
            {
                menu = canvas.gameObject.AddComponent<BoatshipsDebugMenu>();
                menu.Build(canvas.transform);
            }
            foreach (Component control in controls)
            {
                if (control == null) continue;
                RectTransform rect = control.GetComponent<RectTransform>();
                rect.SetParent(menu.content, false);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                LayoutElement layout = control.GetComponent<LayoutElement>();
                if (layout == null) layout = control.gameObject.AddComponent<LayoutElement>();
                layout.minHeight = control is Text ? 160f : 52f;
                layout.preferredHeight = layout.minHeight;
                layout.flexibleHeight = 0f;
                if (control is Text text)
                {
                    text.alignment = TextAnchor.MiddleLeft;
                    text.horizontalOverflow = HorizontalWrapMode.Wrap;
                    text.verticalOverflow = VerticalWrapMode.Truncate;
                }
            }
        }

        private void Build(Transform parent)
        {
            IsOpen = false;
            RectTransform root = Make("Debug Menu (F5)", parent, typeof(Image));
            panel = root.gameObject;
            root.anchorMin = new Vector2(0.12f, 0.1f);
            root.anchorMax = new Vector2(0.88f, 0.9f);
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0.025f, 0.06f, 0.08f, 0.98f);
            RectTransform title = Make("Debug — F5 to close", root, typeof(Text));
            title.anchorMin = new Vector2(0.03f, 0.91f);
            title.anchorMax = new Vector2(0.97f, 0.99f);
            title.offsetMin = title.offsetMax = Vector2.zero;
            Text label = title.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 24;
            label.color = Color.white;
            label.text = "DEBUG / TEST TOOLS — F5 to close";
            label.raycastTarget = false;
            RectTransform viewport = Make("Viewport", root, typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.anchorMin = new Vector2(0.03f, 0.03f);
            viewport.anchorMax = new Vector2(0.97f, 0.90f);
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0.06f, 0.12f, 0.15f);
            content = Make("Debug Controls", viewport, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            VerticalLayoutGroup column = content.GetComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(12, 12, 12, 12);
            column.spacing = 12f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            panel.SetActive(false);
        }

        private static RectTransform Make(string name, Transform parent, params System.Type[] types)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            foreach (System.Type type in types) go.AddComponent(type);
            return go.GetComponent<RectTransform>();
        }

        private void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.f5Key.wasPressedThisFrame) return;
            IsOpen = !IsOpen;
            panel.SetActive(IsOpen);
            if (IsOpen) panel.transform.SetAsLastSibling();
        }

        private void OnDestroy() { IsOpen = false; }
    }
}
