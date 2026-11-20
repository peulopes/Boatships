using System;
using UnityEngine;
using UnityEngine.UI;

namespace Boatships
{
    /// <summary>Shared runtime UI factory for the voyage layer (world map, menus, character creation).</summary>
    internal static class BoatshipsUI
    {
        private static Font font;
        private static Sprite circle;
        private static Sprite triangle;

        public static readonly Color Background = new Color(0.02f, 0.07f, 0.1f, 1f);
        public static readonly Color Panel = new Color(0.04f, 0.12f, 0.16f, 0.97f);
        public static readonly Color ButtonColor = new Color(0.12f, 0.32f, 0.39f);
        public static readonly Color Selected = new Color(0.2f, 0.55f, 0.6f);
        public static readonly Color Accent = new Color(0.75f, 0.5f, 0.2f);
        public static readonly Color Ink = new Color(0.03f, 0.07f, 0.1f);
        public static readonly Color Title = new Color(0.65f, 0.9f, 0.88f);

        /// <summary>Optional click feedback (wired to the mobile presentation's click sound).</summary>
        public static Action ClickSound;

        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null) go.transform.SetParent(parent, false);
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static RectTransform SafeArea(Canvas canvas)
        {
            RectTransform safe = Rect("Safe Area", canvas.transform);
            safe.gameObject.AddComponent<BoatshipsSafeArea>();
            Stretch(safe);
            return safe;
        }

        public static RectTransform Rect(string name, Transform parent, params Type[] components)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            foreach (Type component in components) go.AddComponent(component);
            return go.GetComponent<RectTransform>();
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public static void Anchor(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
        {
            rect.anchorMin = new Vector2(xMin, yMin); rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public static Image Image(string name, Transform parent, Color color)
        {
            Image image = Rect(name, parent, typeof(Image)).GetComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text Label(string text, Transform parent, int size, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            Text label = Rect("Label", parent, typeof(Text)).GetComponent<Text>();
            label.font = Font; label.fontSize = size; label.color = Color.white;
            label.text = text; label.alignment = alignment; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        public static Text FixedLabel(string text, Transform parent, int size, float height, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            Text label = Label(text, parent, size, alignment);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return label;
        }

        public static Button Button(string title, Transform parent, Action action, float height = 58f, int size = 22)
        {
            RectTransform rect = Rect(title, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().preferredHeight = height;
            Image image = rect.GetComponent<Image>();
            image.color = ButtonColor;
            Button button = rect.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { ClickSound?.Invoke(); action?.Invoke(); });
            Text label = Label(title, rect, size, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(8f, 2f);
            label.rectTransform.offsetMax = new Vector2(-8f, -2f);
            return button;
        }

        public static void SetButton(Button button, string title, bool interactable)
        {
            if (button == null) return;
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.text = title;
            button.interactable = interactable;
        }

        public static VerticalLayoutGroup Column(RectTransform rect, float spacing, int padding)
        {
            VerticalLayoutGroup column = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = spacing;
            column.padding = new RectOffset(padding, padding, padding, padding);
            column.childControlHeight = true; column.childControlWidth = true;
            column.childForceExpandHeight = false; column.childForceExpandWidth = true;
            return column;
        }

        public static HorizontalLayoutGroup Row(Transform parent, float height, float spacing)
        {
            RectTransform rect = Rect("Row", parent, typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().preferredHeight = height;
            HorizontalLayoutGroup row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = spacing;
            row.childControlHeight = true; row.childControlWidth = true;
            row.childForceExpandHeight = true; row.childForceExpandWidth = true;
            return row;
        }

        public static Sprite Circle => circle != null ? circle : (circle = MakeSprite("Map Circle", (x, y) =>
            Mathf.Clamp01((0.5f - new Vector2(x - 0.5f, y - 0.5f).magnitude) * 32f)));

        /// <summary>Upward-pointing ship arrow.</summary>
        public static Sprite Triangle => triangle != null ? triangle : (triangle = MakeSprite("Map Ship", (x, y) =>
        {
            float halfWidth = (1f - y) * 0.42f;
            float inside = Mathf.Min(halfWidth - Mathf.Abs(x - 0.5f), y - 0.06f, 0.96f - y);
            float notch = y < 0.3f ? Mathf.Abs(x - 0.5f) - (0.3f - y) * 0.9f : 1f;
            return Mathf.Clamp01(Mathf.Min(inside, notch) * 40f);
        }));

        private static Sprite MakeSprite(string name, Func<float, float, float> alpha)
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color32(255, 255, 255,
                    (byte)(alpha((x + 0.5f) / size, (y + 0.5f) / size) * 255f));
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }

    /// <summary>Keeps a full-screen RectTransform inside the device safe area.</summary>
    public sealed class BoatshipsSafeArea : MonoBehaviour
    {
        private Rect applied;
        private Vector2 screen;

        private void Update()
        {
            if (applied == Screen.safeArea && screen == new Vector2(Screen.width, Screen.height)) return;
            applied = Screen.safeArea;
            screen = new Vector2(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(applied.xMin / Screen.width, applied.yMin / Screen.height);
            rect.anchorMax = new Vector2(applied.xMax / Screen.width, applied.yMax / Screen.height);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
