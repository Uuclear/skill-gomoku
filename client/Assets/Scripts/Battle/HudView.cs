using Gomoku.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Battle
{
    /// <summary>
    /// Top/bottom player cards (nickname, colour badge, energy) and the turn
    /// countdown bar between them. Active side is highlighted.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        Text _oppName;
        Text _oppEnergy;
        Image _oppCard;
        Text _selfName;
        Text _selfEnergy;
        Image _selfCard;
        Image _timerFill;
        Text _timerText;

        Color _idleColor = new Color(0.18f, 0.19f, 0.23f, 1f);
        Color _activeColor = new Color(0.27f, 0.36f, 0.55f, 1f);

        public void Build(Transform parent, int myColor, string myName, string oppName)
        {
            var layer = UIFactory.Panel(transform, "Hud");
            var img = layer.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;

            _oppCard = BuildCard(parent, "OppCard", 805, out _oppName, out _oppEnergy);
            _selfCard = BuildCard(parent, "SelfCard", -805, out _selfName, out _selfEnergy);

            _oppName.text = $"{ColorName(1 - myColor)} · {oppName}";
            _selfName.text = $"{ColorName(myColor)} · {myName}";

            BuildTimer(parent);
        }

        Image BuildCard(Transform parent, string name, float y, out Text title, out Text energy)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var card = go.GetComponent<Image>();
            card.color = _idleColor;
            UIFactory.Place((RectTransform)go.transform, 0, y, 1000, 110);

            title = UIFactory.Label(go.transform, "", 34, TextAnchor.MiddleLeft);
            var titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(0.65f, 1f);
            titleRt.offsetMin = new Vector2(28, 0);
            titleRt.offsetMax = new Vector2(-10, 0);

            energy = UIFactory.Label(go.transform, "", 40, TextAnchor.MiddleRight);
            var energyRt = (RectTransform)energy.transform;
            energyRt.anchorMin = new Vector2(0.62f, 0f);
            energyRt.anchorMax = new Vector2(1f, 1f);
            energyRt.offsetMin = new Vector2(0, 0);
            energyRt.offsetMax = new Vector2(-30, 0);
            energy.color = new Color(1f, 0.86f, 0.35f, 1f);
            return card;
        }

        void BuildTimer(Transform parent)
        {
            var barGo = new GameObject("TimerBar", typeof(RectTransform), typeof(Image));
            barGo.transform.SetParent(parent, false);
            var barBg = barGo.GetComponent<Image>();
            barBg.color = new Color(0f, 0f, 0f, 0.45f);
            UIFactory.Place((RectTransform)barGo.transform, 0, 700, 720, 34);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(barGo.transform, false);
            _timerFill = fillGo.GetComponent<Image>();
            _timerFill.color = new Color(0.3f, 0.78f, 0.45f, 1f);
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            _timerText = UIFactory.Label(parent, "", 28, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)_timerText.transform, 0, 700, 720, 34);
        }

        public void Apply(int turn, int myColor, int myEnergy, int oppEnergy)
        {
            _selfEnergy.text = $"能量 {myEnergy}";
            // -1 = masked by fog of war until 侦查 is used.
            _oppEnergy.text = oppEnergy < 0 ? "能量 ?" : $"能量 {oppEnergy}";
            _selfCard.color = turn == myColor ? _activeColor : _idleColor;
            _oppCard.color = turn == 1 - myColor ? _activeColor : _idleColor;
        }

        /// <summary>fraction 0..1 of the current turn left.</summary>
        public void SetTimer(float fraction, int remainSeconds)
        {
            fraction = Mathf.Clamp01(fraction);
            var rt = (RectTransform)_timerFill.transform;
            // Left-anchored fill: shrink from the right edge.
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(fraction, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _timerFill.color = fraction > 0.25f
                ? new Color(0.3f, 0.78f, 0.45f, 1f)
                : new Color(0.9f, 0.28f, 0.22f, 1f);
            _timerText.text = $"{Mathf.Max(0, remainSeconds)}s";
        }

        static string ColorName(int color) => color == 0 ? "黑方" : "白方";
    }
}
