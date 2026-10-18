using System.Collections.Generic;
using Gomoku.Core;
using Gomoku.Net;
using Gomoku.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Battle
{
    /// <summary>
    /// Procedural 15x15 board: wood background with grid, stone pool, phantom
    /// stones, revealed mine markers, last-move dot and tap-to-move input.
    /// Rebuilt fully from every authoritative snapshot.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public System.Action<int, int> CellTapped;

        public const float BoardPx = 1000f;

        public RectTransform Root => _root;

        int _size = 15;
        float _step;

        RectTransform _root;
        RectTransform _pieceLayer;
        Image _touch;

        readonly List<PieceView> _pieces = new List<PieceView>();
        PieceView _lastDot;
        Sprite _blackSprite;
        Sprite _whiteSprite;
        Sprite _dotSprite;
        Sprite _mineSprite;

        bool _inputEnabled;

        public void Build(Transform parent, int boardSize)
        {
            _size = boardSize;
            _step = BoardGeometry.Step(BoardPx, _size);

            _root = UIFactoryStretch(parent, "Board");
            UIFactory.Place(_root, 0, -150, BoardPx, BoardPx);

            var bgGo = new GameObject("BoardBg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(_root, false);
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgGo.GetComponent<Image>().sprite = BuildBoardSprite();
            bgGo.GetComponent<Image>().type = Image.Type.Simple;

            _pieceLayer = new GameObject("Pieces", typeof(RectTransform)).GetComponent<RectTransform>();
            _pieceLayer.SetParent(_root, false);
            _pieceLayer.anchorMin = Vector2.zero;
            _pieceLayer.anchorMax = Vector2.one;
            _pieceLayer.offsetMin = _pieceLayer.offsetMax = Vector2.zero;

            // Full-size transparent catcher (sits above pieces visually but
            // stones set raycastTarget=false so taps always reach it).
            var touchGo = new GameObject("Touch", typeof(RectTransform), typeof(Image));
            touchGo.transform.SetParent(_root, false);
            _touch = touchGo.GetComponent<Image>();
            _touch.color = new Color(0, 0, 0, 0);
            var touchRt = (RectTransform)touchGo.transform;
            touchRt.anchorMin = Vector2.zero;
            touchRt.anchorMax = Vector2.one;
            touchRt.offsetMin = touchRt.offsetMax = Vector2.zero;
            var button = touchGo.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnTouchClick);

            _blackSprite = PieceView.BuildStoneSprite(0);
            _whiteSprite = PieceView.BuildStoneSprite(1);
            _dotSprite = PieceView.BuildDotSprite();
            _mineSprite = BuildMineSprite();
        }

        public void SetInputEnabled(bool enabled) => _inputEnabled = enabled;

        /// <summary>Rebuild all pieces from an authoritative snapshot.</summary>
        public void Apply(Snapshot snap, Point? animatedPoint, Point? lastMove)
        {
            int existing = _pieces.Count;
            int i = 0;

            void AddStone(int x, int y, int color, bool phantom)
            {
                PieceView piece;
                if (i < existing)
                {
                    piece = _pieces[i];
                    piece.gameObject.SetActive(true);
                    piece.Init(x, y, color, phantom, color == 0 ? _blackSprite : _whiteSprite);
                    PositionPiece(piece, x, y, phantom);
                }
                else
                {
                    piece = CreatePiece(x, y, color, phantom);
                }
                i++;

                if (animatedPoint.HasValue && animatedPoint.Value.X == x && animatedPoint.Value.Y == y)
                    piece.PlayPop();
            }

            if (snap.Stones != null)
                foreach (var s in snap.Stones)
                    AddStone(s.X, s.Y, s.C, false);
            if (snap.Phantoms != null)
                foreach (var p in snap.Phantoms)
                    AddStone(p.X, p.Y, p.C, true);
            if (snap.MinePositions != null)
                foreach (var m in snap.MinePositions)
                    AddStone(m.X, m.Y, 0, false); // mines are re-skinned below

            // Re-skin the mine entries (they were appended after stones/phantoms).
            int stonePhantomCount =
                (snap.Stones != null ? snap.Stones.Count : 0) +
                (snap.Phantoms != null ? snap.Phantoms.Count : 0);
            for (int k = stonePhantomCount; k < i; k++)
            {
                var img = _pieces[k].GetComponent<Image>();
                img.sprite = _mineSprite;
                img.color = new Color(1f, 0.85f, 0.2f, 0.85f);
            }

            for (; i < _pieces.Count; i++)
                _pieces[i].gameObject.SetActive(false);

            SetLastMove(lastMove);
        }

        PieceView CreatePiece(int x, int y, int color, bool phantom)
        {
            var go = new GameObject(phantom ? "Phantom" : "Stone", typeof(RectTransform));
            go.transform.SetParent(_pieceLayer, false);
            var piece = go.AddComponent<PieceView>();
            piece.Init(x, y, color, phantom, color == 0 ? _blackSprite : _whiteSprite);
            PositionPiece(piece, x, y, phantom);
            _pieces.Add(piece);
            return piece;
        }

        void PositionPiece(PieceView piece, int x, int y, bool phantom)
        {
            piece.name = phantom ? "Phantom" : "Stone";
            var rt = (RectTransform)piece.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float stoneSize = _step * 0.92f;
            rt.sizeDelta = new Vector2(stoneSize, stoneSize);
            rt.anchoredPosition = new Vector2(
                BoardGeometry.ToLocalX(x, _size, _step),
                BoardGeometry.ToLocalY(y, _size, _step));
            piece.transform.localScale = Vector3.one;
        }

        void SetLastMove(Point? lastMove)
        {
            if (!lastMove.HasValue)
            {
                if (_lastDot != null) _lastDot.gameObject.SetActive(false);
                return;
            }
            if (_lastDot == null)
            {
                var go = new GameObject("LastDot", typeof(RectTransform));
                go.transform.SetParent(_pieceLayer, false);
                _lastDot = go.AddComponent<PieceView>();
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.sprite = _dotSprite;
                img.SetNativeSize();
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(_step * 0.34f, _step * 0.34f);
            }
            _lastDot.gameObject.SetActive(true);
            ((RectTransform)_lastDot.transform).anchoredPosition = new Vector2(
                BoardGeometry.ToLocalX(lastMove.Value.X, _size, _step),
                BoardGeometry.ToLocalY(lastMove.Value.Y, _size, _step));
            _lastDot.transform.SetAsLastSibling();
        }

        void OnTouchClick()
        {
            if (!_inputEnabled) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _root, Input.mousePosition, null, out var local))
                return;
            var grid = BoardGeometry.ToGrid(local.x, local.y, _size, _step);
            if (grid != null)
                CellTapped?.Invoke(grid.Item1, grid.Item2);
        }

        // ---------- procedural textures ----------

        Sprite BuildBoardSprite()
        {
            const int size = 1024;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear
            };
            var wood = new Color(0.86f, 0.72f, 0.48f);
            var px = new Color[size * size];
            // Cheap deterministic wood-ish noise.
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float grain = Mathf.PerlinNoise(x * 0.03f, y * 0.12f) - 0.5f;
                float noise = (Mathf.PerlinNoise(x * 0.4f, y * 0.4f) - 0.5f) * 0.06f;
                px[y * size + x] = wood + new Color(grain * 0.05f + noise,
                    grain * 0.035f + noise, grain * 0.01f);
            }
            tex.SetPixels(px);

            float margin = size / 15f * 0.5f;
            float step = (size - margin * 2f) / (_size - 1);
            float GX(int i) => margin + i * step;

            var lineColor = new Color(0.18f, 0.11f, 0.05f, 0.9f);
            for (int i = 0; i < _size; i++)
            {
                DrawLine(tex, GX(0), GX(i), GX(_size - 1), GX(i), 2.2f, lineColor);
                DrawLine(tex, GX(i), GX(0), GX(i), GX(_size - 1), 2.2f, lineColor);
            }

            // Standard star points for 15x15.
            int[] stars = { 3, 7, 11 };
            foreach (var sx in stars)
            foreach (var sy in stars)
                DrawDisc(tex, GX(sx), GX(sy), 6f, lineColor);

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
        }

        static Sprite BuildMineSprite()
        {
            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = size * 0.5f, r = size * 0.32f;
            var mine = new Color(0.25f, 0.18f, 0.12f, 1f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                px[y * size + x] = d <= r
                    ? new Color(mine.r, mine.g, mine.b, Mathf.Clamp01((r - d) / 2f))
                    : new Color(0, 0, 0, 0);
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
        }

        static void DrawDisc(Texture2D tex, float cx, float cy, float radius, Color color)
        {
            int x0 = Mathf.Max(0, (int)(cx - radius) - 1);
            int x1 = Mathf.Min(tex.width - 1, (int)(cx + radius) + 1);
            int y0 = Mathf.Max(0, (int)(cy - radius) - 1);
            int y1 = Mathf.Min(tex.height - 1, (int)(cy + radius) + 1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                if (d <= radius)
                    tex.SetPixel(x, y, color);
            }
        }

        static void DrawLine(Texture2D tex, float x0, float y0, float x1, float y1,
            float width, Color color)
        {
            float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            int steps = Mathf.CeilToInt(len) + 1;
            float dx = (x1 - x0) / steps;
            float dy = (y1 - y0) / steps;
            float r = width * 0.5f;
            for (int i = 0; i <= steps; i++)
                DrawDisc(tex, x0 + dx * i, y0 + dy * i, r, color);
        }

        static RectTransform UIFactoryStretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UIFactory.Stretch((RectTransform)go.transform);
        }
    }
}
