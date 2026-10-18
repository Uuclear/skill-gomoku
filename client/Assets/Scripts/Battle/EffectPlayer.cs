using System.Collections.Generic;
using Gomoku.Core;
using Gomoku.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Battle
{
    /// <summary>
    /// Lightweight battle juice: floating energy deltas over the local
    /// player's card, expanding ring pulses on skill-affected board points,
    /// category-colored card rings, a per-cast banner and the bomb burst
    /// with board shake. Everything is pooled-ish and self-expiring in Update.
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
            public float Life;
            public float FromScale;
            public float ToScale;
        }

        sealed class BannerFx
        {
            public GameObject Go;
            public Image Bg;
            public Text Text;
            public float Born;
            public Color BaseColor;
        }

        readonly List<FloatText> _texts = new List<FloatText>();
        readonly List<PulseFx> _pulses = new List<PulseFx>();
        BannerFx _banner;

        RectTransform _boardRoot;
        float _step;

        // Board shake state; _boardHome is restored exactly when it ends.
        bool _shaking;
        float _shakeUntil;
        Vector2 _boardHome;

        Sprite _ringSprite;

        static readonly Color OwnColor = new Color(1f, 0.86f, 0.3f, 0.95f);
        static readonly Color FoeColor = new Color(1f, 0.45f, 0.35f, 0.95f);

        /// <summary>Category palette, mirrors the SkillBarView slot colors.</summary>
        public static Color ColorForCategory(string cat)
        {
            switch (cat)
            {
                case SkillCat.Info: return new Color(0.24f, 0.5f, 0.72f, 1f);
                case SkillCat.Buff: return new Color(0.27f, 0.6f, 0.4f, 1f);
                case SkillCat.Debuff: return new Color(0.66f, 0.3f, 0.34f, 1f);
                case SkillCat.Board: return new Color(0.55f, 0.46f, 0.28f, 1f);
                default: return new Color(0.5f, 0.36f, 0.66f, 1f);
            }
        }

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

        /// <summary>Expanding ring at a board point (own gold / foe red).</summary>
        public void Pulse(int x, int y, bool own) => PulseAt(x, y, own ? OwnColor : FoeColor);

        /// <summary>Expanding ring at a board point with an explicit color.</summary>
        public void PulseAt(int x, int y, Color color)
        {
            if (_boardRoot == null) return;
            SpawnRing(_boardRoot, BoardLocalPos(x, y), _step, color, 0.7f, 0.6f, 2f);
        }

        /// <summary>Category-colored ring over a player card (own bottom, foe top).</summary>
        public void CardPulse(bool own, Color color)
        {
            SpawnRing(transform, new Vector2(0, own ? -805f : 805f), 130f, color, 0.7f, 1f, 2f);
        }

        /// <summary>Bomb detonation: two staggered rings at a board point.</summary>
        public void Burst(int x, int y)
        {
            if (_boardRoot == null) return;
            var pos = BoardLocalPos(x, y);
            SpawnRing(_boardRoot, pos, _step * 0.8f,
                new Color(1f, 0.62f, 0.2f, 1f), 0.5f, 0.5f, 2.8f);
            SpawnRing(_boardRoot, pos, _step * 1.2f,
                new Color(1f, 0.35f, 0.25f, 1f), 0.8f, 0.6f, 2.2f);
        }

        /// <summary>
        /// Brief random board jitter (~0.2s). Re-entrant: a running shake is
        /// snapped home first so offsets never accumulate.
        /// </summary>
        public void ShakeBoard()
        {
            if (_boardRoot == null) return;
            if (_shaking) _boardRoot.anchoredPosition = _boardHome;
            else _boardHome = _boardRoot.anchoredPosition;
            _shaking = true;
            _shakeUntil = Time.time + 0.2f;
        }

        /// <summary>
        /// Category-colored cast banner at y=560 (the gap between the emote
        /// bar and the opponent card). A new cast replaces the old banner.
        /// </summary>
        public void SkillBanner(string skillName, bool own, string category)
        {
            if (_banner != null)
            {
                Destroy(_banner.Go);
                _banner = null;
            }
            var text = UIFactory.Toast(transform, out var bg, 36);
            var go = bg.gameObject;
            go.name = "SkillBanner";
            UIFactory.Place((RectTransform)go.transform, 0, 560, 520, 70);
            var c = ColorForCategory(category);
            bg.color = new Color(c.r, c.g, c.b, 0.9f);
            text.text = $"{(own ? "我方" : "对方")} · {skillName}";
            text.color = Color.white;
            go.transform.SetAsLastSibling();
            _banner = new BannerFx
            {
                Go = go, Bg = bg, Text = text, Born = Time.time, BaseColor = bg.color
            };
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
                float t = (Time.time - p.Born) / p.Life;
                if (t >= 1f)
                {
                    Destroy(p.Go);
                    _pulses.RemoveAt(i);
                    continue;
                }
                float s = p.FromScale + t * (p.ToScale - p.FromScale);
                p.Rt.localScale = new Vector3(s, s, 1f);
                var c = p.Img.color;
                c.a = (1f - t) * 0.9f;
                p.Img.color = c;
            }

            if (_banner != null)
            {
                float t = (Time.time - _banner.Born) / 1.6f;
                if (t >= 1f)
                {
                    Destroy(_banner.Go);
                    _banner = null;
                }
                else
                {
                    // Quick fade-in, hold, then fade out.
                    float a = t < 0.1f ? t / 0.1f : t > 0.7f ? (1f - t) / 0.3f : 1f;
                    var bc = _banner.BaseColor;
                    bc.a = _banner.BaseColor.a * a;
                    _banner.Bg.color = bc;
                    var tc = _banner.Text.color;
                    tc.a = a;
                    _banner.Text.color = tc;
                }
            }

            if (_shaking)
            {
                if (Time.time >= _shakeUntil)
                {
                    _boardRoot.anchoredPosition = _boardHome;
                    _shaking = false;
                }
                else
                {
                    _boardRoot.anchoredPosition = _boardHome +
                        new Vector2(Random.Range(-8f, 8f), Random.Range(-8f, 8f));
                }
            }
        }

        Vector2 BoardLocalPos(int x, int y) =>
            new Vector2(
                BoardGeometry.ToLocalX(x, 15, _step),
                BoardGeometry.ToLocalY(y, 15, _step));

        PulseFx SpawnRing(Transform parent, Vector2 pos, float size, Color color,
            float life, float fromScale, float toScale)
        {
            var go = new GameObject("Pulse", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = _ringSprite;
            img.color = color;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = pos;
            rt.SetAsLastSibling();
            var fx = new PulseFx
            {
                Go = go, Img = img, Rt = rt, Born = Time.time,
                Life = life, FromScale = fromScale, ToScale = toScale
            };
            _pulses.Add(fx);
            return fx;
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
