using UnityEngine;
using UnityEngine.UI;
using Gomoku.UI;

namespace Gomoku.Battle
{
    /// <summary>
    /// Shows emotes as transient bubbles near the two player cards. The six
    /// emote symbols are client-defined; the wire protocol only carries ids 0..5.
    /// The layer has no graphics that block input: all raycastTargets are false.
    /// </summary>
    public sealed class EmoteView : MonoBehaviour
    {
        public const int Count = 6;
        const float HoldSeconds = 2.2f;

        static readonly string[] Labels =
        {
            "好棋 👍", "失误 😅", "赞 👏", "加油 💪", "等等 ✋", "哈哈 😄"
        };

        GameObject _mineBubble;
        GameObject _oppBubble;
        Text _mineText;
        Text _oppText;
        int _myColor;
        float _mineUntil;
        float _oppUntil;

        public static string Label(int id)
        {
            if (id < 0 || id >= Labels.Length) return "";
            return Labels[id];
        }

        public void Build(Transform parent, int myColor)
        {
            _myColor = myColor;

            var layer = new GameObject("EmoteLayer", typeof(RectTransform));
            layer.transform.SetParent(parent, false);
            UIFactory.Stretch((RectTransform)layer.transform);

            _mineBubble = CreateBubble(layer.transform, new Color(0.22f, 0.45f, 0.82f, 0.92f), out _mineText);
            _oppBubble = CreateBubble(layer.transform, new Color(0.72f, 0.38f, 0.3f, 0.92f), out _oppText);
            _mineBubble.SetActive(false);
            _oppBubble.SetActive(false);
        }

        public void Show(int fromColor, int id)
        {
            string label = Label(id);
            if (label.Length == 0) return;
            bool mine = fromColor == _myColor;
            var go = mine ? _mineBubble : _oppBubble;
            var text = mine ? _mineText : _oppText;
            text.text = label;
            // Own bubble sits above the TargetSelector bar (-724..-668) and
            // the skill bar (-960..-860).
            UIFactory.Place((RectTransform)go.transform, 0, mine ? -790 : 620, 300, 76);
            go.SetActive(true);
            if (mine) _mineUntil = Time.time + HoldSeconds;
            else _oppUntil = Time.time + HoldSeconds;
        }

        void Update()
        {
            if (_mineBubble.activeSelf && Time.time > _mineUntil) _mineBubble.SetActive(false);
            if (_oppBubble.activeSelf && Time.time > _oppUntil) _oppBubble.SetActive(false);
        }

        static GameObject CreateBubble(Transform parent, Color color, out Text text)
        {
            var go = new GameObject("Bubble", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            var label = UIFactory.Label(go.transform, "", 36, TextAnchor.MiddleCenter);
            label.raycastTarget = false;
            var rt = (RectTransform)label.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            text = label;
            return go;
        }
    }
}
