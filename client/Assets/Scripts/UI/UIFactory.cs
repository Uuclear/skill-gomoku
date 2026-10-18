using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Gomoku.UI
{
    /// <summary>
    /// Helpers for building uGUI entirely in code (no prefabs). Uses the legacy
    /// built-in font so it works in Unity 6 without importing an asset.
    /// </summary>
    public static class UIFactory
    {
        static Font _font;
        public static Font Font => _font != null ? _font : (_font = Font.CreateDynamicFontFromOSFont("Arial", 32));

        public static GameObject CanvasObject(string name, Transform parent = null)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            if (parent != null) go.transform.SetParent(parent, false);
            EnsureEventSystem();
            return go;
        }

        /// <summary>
        /// uGUI needs an EventSystem in the scene or no Button/InputField ever
        /// receives pointer/keyboard events. The whole UI is built in code with no
        /// scene assets, so nothing creates one by default — create it on demand.
        /// </summary>
        static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        static GameObject Create(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static RectTransform Panel(Transform parent, string name = "Panel")
        {
            var go = Create(name, parent);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.13f, 0.14f, 0.17f, 1f);
            return Stretch(go.GetComponent<RectTransform>());
        }

        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Text Label(Transform parent, string text, int fontSize = 36,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var go = Create("Label", parent);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = Color.white;
            return t;
        }

        public static InputField Input(Transform parent, string placeholder, int fontSize = 36)
        {
            var go = Create("Input", parent);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.92f);

            var textGo = Create("Text", go.transform);
            var text = textGo.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.black;
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(16, 0);
            textRt.offsetMax = new Vector2(-16, 0);

            var phGo = Create("Placeholder", go.transform);
            var ph = phGo.AddComponent<Text>();
            ph.font = Font;
            ph.fontSize = fontSize;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.text = placeholder;
            ph.color = new Color(0.4f, 0.4f, 0.4f, 0.7f);
            var phRt = (RectTransform)phGo.transform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(16, 0);
            phRt.offsetMax = new Vector2(-16, 0);

            var input = go.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            return input;
        }

        public static Button Button(Transform parent, string caption, int fontSize = 36)
        {
            var go = Create("Button", parent);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.45f, 0.82f, 1f);

            var text = Label(go.transform, caption, fontSize, TextAnchor.MiddleCenter);
            var textRt = (RectTransform)text.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.15f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.3f);
            btn.colors = colors;
            return btn;
        }

        /// <summary>
        /// Toast bubble: an Image background with a centered Text child. A
        /// GameObject can only hold one Graphic, so the background must be its
        /// own GO with the text as a child — adding Image onto a Text GO fails.
        /// </summary>
        public static Text Toast(Transform parent, out Image bg, int fontSize = 32)
        {
            var go = Create("Toast", parent);
            bg = go.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.08f, 0.02f, 0f);
            bg.raycastTarget = false;
            var text = Label(go.transform, "", fontSize, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Place a rect by normalized center + size (design space 1080x1920).</summary>
        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
