using System.Collections.Generic;
using Gomoku.Core;
using Gomoku.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Battle
{
    /// <summary>
    /// Overlays board-effect markers from an authoritative snapshot: 坚守
    /// (permanent gold ring), 护盾 (temporary blue ring), 单点封锁 (red X),
    /// 禁区 (3x3 red-tinted zone) and 困局 (yellow 5x5 allowed cage). The
    /// server sends every active marker (no owner hiding), so each kind uses a
    /// fixed colour. Built inside the board root so it shares board-local
    /// coordinates with the stones.
    /// </summary>
    public sealed class MarkerView : MonoBehaviour
    {
        int _size = 15;
        float _step;
        RectTransform _layer;
        RectTransform _fxLayer;

        Sprite _ringSprite;
        Sprite _crossSprite;
        Sprite _glowSprite;

        readonly List<GameObject> _pool = new List<GameObject>();
        int _used;

        readonly List<GameObject> _hintPool = new List<GameObject>();
        int _hintUsed;

        readonly List<GameObject> _selPool = new List<GameObject>();
        int _selUsed;

        readonly List<GameObject> _winPool = new List<GameObject>();
        int _winUsed;

        static readonly Color FortifyColor = new Color(1f, 0.84f, 0.25f, 0.95f);
        static readonly Color ShieldColor = new Color(0.35f, 0.7f, 1f, 0.9f);
        static readonly Color SealColor = new Color(0.95f, 0.3f, 0.28f, 0.85f);
        static readonly Color CageColor = new Color(1f, 0.9f, 0.3f, 0.14f);
        static readonly Color ZoneColor = new Color(0.95f, 0.3f, 0.28f, 0.22f);
        static readonly Color OracleColor = new Color(0.3f, 0.95f, 0.5f, 0.55f);
        static readonly Color ThreatColor = new Color(1f, 0.32f, 0.24f, 0.6f);
        static readonly Color SelectColor = new Color(1f, 1f, 1f, 0.95f);

        public void Build(RectTransform boardRoot, int boardSize)
        {
            _size = boardSize;
            _step = BoardGeometry.Step(BoardView.BoardPx, _size);

            var go = new GameObject("Markers", typeof(RectTransform));
            go.transform.SetParent(boardRoot, false);
            _layer = (RectTransform)go.transform;
            _layer.anchorMin = Vector2.zero;
            _layer.anchorMax = Vector2.one;
            _layer.offsetMin = _layer.offsetMax = Vector2.zero;

            // FX layer sits above markers (and above the transparent touch
            // catcher); every FX graphic has raycastTarget=false so board taps
            // still reach the catcher beneath.
            var fxGo = new GameObject("SkillFx", typeof(RectTransform));
            fxGo.transform.SetParent(boardRoot, false);
            _fxLayer = (RectTransform)fxGo.transform;
            _fxLayer.anchorMin = Vector2.zero;
            _fxLayer.anchorMax = Vector2.one;
            _fxLayer.offsetMin = _fxLayer.offsetMax = Vector2.zero;
            _fxLayer.SetAsLastSibling();

            _ringSprite = BuildRingSprite();
            _crossSprite = BuildCrossSprite();
            _glowSprite = BuildGlowSprite();
        }

        /// <summary>Rebuild all markers for a snapshot.</summary>
        public void Apply(Snapshot snap, int moveCount)
        {
            _used = 0;

            if (snap.Cage != null && snap.Cage.ExpireAtMove > moveCount)
                AddZone(snap.Cage.Center.X, snap.Cage.Center.Y, 2, CageColor); // 5x5
            if (snap.Zones != null)
                foreach (var z in snap.Zones)
                    if (z.ExpireAtMove > moveCount)
                        AddZone(z.Cx, z.Cy, 1, ZoneColor); // 3x3

            if (snap.Fortifies != null)
                foreach (var m in snap.Fortifies)
                    AddMark(m.X, m.Y, _ringSprite, FortifyColor);
            if (snap.Shields != null)
                foreach (var m in snap.Shields)
                    if (m.ExpireAtMove > moveCount)
                        AddMark(m.X, m.Y, _ringSprite, ShieldColor);
            if (snap.Seals != null)
                foreach (var m in snap.Seals)
                    if (m.ExpireAtMove > moveCount)
                        AddMark(m.X, m.Y, _crossSprite, SealColor);

            for (int i = _used; i < _pool.Count; i++) _pool[i].SetActive(false);
        }

        // ---------- oracle / radar hints (private to this client) ----------

        /// <summary>threat=true paints red radar points, false green oracle hints.</summary>
        public void ShowHints(List<Point> points, bool threat)
        {
            ClearHints();
            if (points != null)
                foreach (var p in points)
                {
                    var go = GetFx(_hintPool, ref _hintUsed, _glowSprite,
                        threat ? ThreatColor : OracleColor, _step * 0.78f);
                    PlaceAt(go, p.X, p.Y);
                }
        }

        public void ClearHints()
        {
            for (int i = 0; i < _hintPool.Count; i++) _hintPool[i].SetActive(false);
            _hintUsed = 0;
        }

        // ---------- target-selection markers ----------

        /// <summary>White rings on the points already picked for a pending skill.</summary>
        public void ShowSelection(List<Point> points)
        {
            ClearSelection();
            if (points != null)
                foreach (var p in points)
                {
                    var go = GetFx(_selPool, ref _selUsed, _ringSprite, SelectColor, _step * 0.9f);
                    PlaceAt(go, p.X, p.Y);
                }
        }

        public void ClearSelection()
        {
            for (int i = 0; i < _selPool.Count; i++) _selPool[i].SetActive(false);
            _selUsed = 0;
        }

        // ---------- winning five line (game over) ----------

        static readonly Color WinLineColor = new Color(1f, 0.84f, 0.2f, 0.95f);
        static readonly Color WinGlowColor = new Color(1f, 0.75f, 0.15f, 0.4f);
        Sprite _capsuleSprite;

        /// <summary>Highlight the five winning stones and draw a line through them.</summary>
        public void ShowWinLine(List<Point> points)
        {
            ClearWinLine();
            if (points == null || points.Count == 0) return;
            if (_capsuleSprite == null) _capsuleSprite = BuildCapsuleSprite();

            foreach (var p in points)
            {
                var glow = GetFx(_winPool, ref _winUsed, _glowSprite, WinGlowColor, _step * 1.05f);
                glow.transform.localRotation = Quaternion.identity;
                glow.transform.localScale = Vector3.one;
                PlaceAt(glow, p.X, p.Y);
            }

            // One capsule segment per adjacent pair.
            for (int i = 0; i + 1 < points.Count; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                float ax = BoardGeometry.ToLocalX(a.X, _size, _step);
                float ay = BoardGeometry.ToLocalY(a.Y, _size, _step);
                float bx = BoardGeometry.ToLocalX(b.X, _size, _step);
                float by = BoardGeometry.ToLocalY(b.Y, _size, _step);
                float dx = bx - ax;
                float dy = by - ay;
                float len = Mathf.Sqrt(dx * dx + dy * dy) + _step * 0.5f;

                var go = GetWinLine();
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(len, _step * 0.22f);
                rt.anchoredPosition = new Vector2((ax + bx) * 0.5f, (ay + by) * 0.5f);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
            }
        }

        public void ClearWinLine()
        {
            for (int i = 0; i < _winPool.Count; i++) _winPool[i].SetActive(false);
            _winUsed = 0;
        }

        GameObject GetWinLine()
        {
            GameObject go;
            if (_winUsed < _winPool.Count)
            {
                go = _winPool[_winUsed];
                go.SetActive(true);
            }
            else
            {
                go = new GameObject("WinLine", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_fxLayer, false);
                go.GetComponent<Image>().raycastTarget = false;
                _winPool.Add(go);
            }
            go.GetComponent<Image>().sprite = _capsuleSprite;
            go.GetComponent<Image>().color = WinLineColor;
            _winUsed++;
            return go;
        }

        void Update()
        {
            if (_hintUsed > 0)
            {
                float s = 1f + Mathf.Sin(Time.time * 5f) * 0.12f;
                for (int i = 0; i < _hintUsed; i++)
                    _hintPool[i].transform.localScale = new Vector3(s, s, 1f);
            }
        }

        GameObject GetFx(List<GameObject> pool, ref int used, Sprite sprite, Color color, float size)
        {
            GameObject go;
            if (used < pool.Count)
            {
                go = pool[used];
                go.SetActive(true);
            }
            else
            {
                go = new GameObject("Fx", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_fxLayer, false);
                go.GetComponent<Image>().raycastTarget = false;
                pool.Add(go);
            }
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            used++;
            return go;
        }

        void PlaceAt(GameObject go, int x, int y)
        {
            ((RectTransform)go.transform).anchoredPosition = new Vector2(
                BoardGeometry.ToLocalX(x, _size, _step),
                BoardGeometry.ToLocalY(y, _size, _step));
        }

        void AddZone(int cx, int cy, int radius, Color color)
        {
            var go = Get();
            var img = go.GetComponent<Image>();
            img.sprite = null;
            img.color = color;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            float span = _step * (radius * 2 + 1);
            rt.sizeDelta = new Vector2(span, span);
            rt.anchoredPosition = new Vector2(
                BoardGeometry.ToLocalX(cx, _size, _step),
                BoardGeometry.ToLocalY(cy, _size, _step));
        }

        void AddMark(int x, int y, Sprite sprite, Color color)
        {
            var go = Get();
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(_step * 0.82f, _step * 0.82f);
            rt.anchoredPosition = new Vector2(
                BoardGeometry.ToLocalX(x, _size, _step),
                BoardGeometry.ToLocalY(y, _size, _step));
        }

        GameObject Get()
        {
            GameObject go;
            if (_used < _pool.Count)
            {
                go = _pool[_used];
                go.SetActive(true);
            }
            else
            {
                go = new GameObject("Marker", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_layer, false);
                go.GetComponent<Image>().raycastTarget = false;
                _pool.Add(go);
            }
            _used++;
            return go;
        }

        // ---------- procedural sprites ----------

        static Sprite BuildRingSprite()
        {
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
            float c = size * 0.5f;
            const float outer = 42f, inner = 32f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                if (d <= outer && d >= inner)
                    px[y * size + x] = new Color(1f, 1f, 1f,
                        Mathf.Clamp01(Mathf.Min(outer - d, d - inner) / 2f + 0.5f));
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static Sprite BuildCrossSprite()
        {
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
            float c = size * 0.5f, r = size * 0.34f, thick = 6f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float ux = x + 0.5f - c, uy = y + 0.5f - c;
                float d1 = Mathf.Abs(ux - uy) / Mathf.Sqrt(2f);
                float d2 = Mathf.Abs(ux + uy) / Mathf.Sqrt(2f);
                bool inside = Mathf.Abs(ux) <= r && Mathf.Abs(uy) <= r;
                if (inside && (d1 <= thick * 0.5f || d2 <= thick * 0.5f))
                {
                    float a = Mathf.Clamp01(thick * 0.5f - Mathf.Min(d1, d2) + 1f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static Sprite BuildGlowSprite()
        {
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
            float c = size * 0.5f, r = size * 0.44f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                if (d <= r)
                {
                    float t = d / r;
                    // soft filled disc with a brighter rim
                    float a = Mathf.Clamp01(0.28f + (1f - t) * 0.5f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // Horizontal capsule: 256x32, round ends. Used rotated for the win line.
        static Sprite BuildCapsuleSprite()
        {
            const int w = 256, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
            float r = h * 0.5f;
            float cy = h * 0.5f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx;
                if (x < r) fx = r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, cy));
                else if (x > w - r) fx = r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(w - r, cy));
                else fx = r - Mathf.Abs(y + 0.5f - cy);
                if (fx > 0f) px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(fx + 0.4f));
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
