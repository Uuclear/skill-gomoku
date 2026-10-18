using System.Collections.Generic;
using Gomoku.Core;
using Gomoku.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Battle
{
    /// <summary>
    /// Lightweight battle juice: floating energy deltas over the local
    /// player's card and expanding ring pulses on skill-affected board
    /// points. Everything is pooled-ish and self-expiring in Update.
    /// </summary>
    public sealed class EffectPlayer : MonoBehaviour
    {
        sealed class FloatText
        {
            public Text Text;
            public RectTransform Rt;
            public float Born;
            public float FromY;
        }

        sealed class PulseFx
        {
            public GameObject Go;
            public Image Img;
            public RectTransform Rt;
            public float Born;
        }

        readonly List<FloatText> _texts = new List<FloatText>();
        readonly List<PulseFx> _pulses = new List<PulseFx>();

        RectTransform _boardRoot;
        float _step;

        Sprite _ringSprite;

        static readonly Color OwnColor = new Color(1f, 0.86f, 0.3f, 0.95f);
        static readonly Color FoeColor = new Color(1f, 0.45f, 0.35f, 0.95f);

        public void Build(Transform parent)
        {
            var layer = UIFactory.Panel(transform, "EffectPlayer");
            var img = layer.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = false;
            _ringSprite = BuildPulseSprite();
        }

        public void AttachBoard(RectTransform boardRoot, int boardSize)
        {
            _boardRoot = boardRoot;
            _step = BoardGeometry.Step(BoardView.BoardPx, boardSize);
        }

        /// <summary>Spawn a +n / -n energy delta over the self card.</summary>
        public void FloatingEnergy(int delta)
        {
            if (delta == 0) return;
            var go = new GameObject("EnergyFloat", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<Text>();
            text.font = UIFactory.Font;
            text.fontSize = 44;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.text = delta > 0 ? $"+{delta}" : delta.ToString();
            text.color = delta > 0
                ? new Color(0.45f, 1f, 0.55f, 1f)
                : new Color(1f, 0.78f, 0.3f, 1f);
            var rt = (RectTransform)go.transform;
            UIFactory.Place(rt, 330, -740, 220, 80);
            _texts.Add(new FloatText
            {
                Text = text, Rt = rt, Born = Time.time, FromY = rt.anchoredPosition.y
            });
        }

        /// <summary>Expanding ring at a board point (board-local coordinates).</summary>
        public void Pulse(int x, int y, bool own)
        {
            if (_boardRoot == null) return;
            var go = new GameObject("Pulse", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_boardRoot, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = _ringSprite;
            img.color = own ? OwnColor : FoeColor;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(_step, _step);
            rt.anchoredPosition = new Vector2(
                BoardGeometry.ToLocalX(x, 15, _step),
                BoardGeometry.ToLocalY(y, 15, _step));
            rt.SetAsLastSibling();
            _pulses.Add(new PulseFx { Go = go, Img = img, Rt = rt, Born = Time.time });
        }

        void Update()
        {
            const float life = 0.9f;
            for (int i = _texts.Count - 1; i >= 0; i--)
            {
                var f = _texts[i];
                float t = (Time.time - f.Born) / life;
                if (t >= 1f)
                {
                    Destroy(f.Text.gameObject);
                    _texts.RemoveAt(i);
                    continue;
                }
                f.Rt.anchoredPosition = new Vector2(f.Rt.anchoredPosition.x, f.FromY + t * 110f);
                var c = f.Text.color;
                c.a = 1f - t;
                f.Text.color = c;
            }

            for (int i = _pulses.Count - 1; i >= 0; i--)
            {
                var p = _pulses[i];
                float t = (Time.time - p.Born) / 0.7f;
                if (t >= 1f)
                {
                    Destroy(p.Go);
                    _pulses.RemoveAt(i);
                    continue;
                }
                float s = 0.6f + t * 1.4f;
                p.Rt.localScale = new Vector3(s, s, 1f);
                var c = p.Img.color;
                c.a = (1f - t) * 0.9f;
                p.Img.color = c;
            }
        }

        static Sprite BuildPulseSprite()
        {
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
            float c = size * 0.5f;
            const float outer = 44f, inner = 36f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                if (d <= outer && d >= inner)
                    px[y * size + x] = new Color(1f, 1f, 1f, 1f);
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
