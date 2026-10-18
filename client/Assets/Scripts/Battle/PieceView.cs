using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Battle
{
    /// <summary>
    /// One visual intersection piece (normal stone or semi-transparent phantom).
    /// Sprites are generated procedurally and cached on the BoardView.
    /// </summary>
    public sealed class PieceView : MonoBehaviour
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public bool Phantom { get; private set; }

        Image _image;

        public static Sprite BuildStoneSprite(int color)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var clear = new Color(0f, 0f, 0f, 0f);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            float cx = size * 0.5f, cy = size * 0.5f;
            float radius = size * 0.46f;
            var baseColor = color == 0
                ? new Color(0.06f, 0.06f, 0.08f)
                : new Color(0.93f, 0.92f, 0.90f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;

                    Color c = baseColor;
                    // Light from the upper-left gives the disc a subtle 3D feel.
                    float shade = 1f - Mathf.Clamp01((dx + dy) / (radius * 2.6f)) * 0.28f;
                    c *= shade;
                    // Specular highlight.
                    float hx = x + 0.5f - (cx - radius * 0.32f);
                    float hy = y + 0.5f - (cy + radius * 0.34f);
                    if (Mathf.Sqrt(hx * hx + hy * hy) < radius * 0.22f)
                        c = Color.Lerp(c, Color.white, color == 0 ? 0.45f : 0.35f);
                    // Soft anti-aliased edge.
                    float edge = Mathf.Clamp01((radius - d) / 2.5f);
                    c.a = edge;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
        }

        public static Sprite BuildDotSprite()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = size * 0.5f, radius = size * 0.42f;
            var dot = new Color(0.9f, 0.2f, 0.18f, 1f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                px[y * size + x] = d <= radius
                    ? new Color(dot.r, dot.g, dot.b, Mathf.Clamp01((radius - d) / 2f))
                    : new Color(0, 0, 0, 0);
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
        }

        public void Init(int x, int y, int color, bool phantom, Sprite stoneSprite)
        {
            X = x;
            Y = y;
            Phantom = phantom;
            if (_image == null) _image = gameObject.AddComponent<Image>();
            _image.sprite = stoneSprite;
            _image.color = phantom
                ? new Color(1f, 1f, 1f, color == 0 ? 0.55f : 0.7f)
                : Color.white;
            _image.raycastTarget = false;
            _image.SetNativeSize();
        }

        /// <summary>Short scale pop used when a freshly placed stone appears.</summary>
        public void PlayPop()
        {
            StopAllCoroutines();
            StartCoroutine(PopRoutine());
        }

        System.Collections.IEnumerator PopRoutine()
        {
            const float duration = 0.16f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float s = Mathf.Lerp(0.5f, 1f, t / duration);
                transform.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            transform.localScale = Vector3.one;
        }
    }
}
