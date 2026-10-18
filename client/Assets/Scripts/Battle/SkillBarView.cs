using System.Collections.Generic;
using Gomoku.Core;
using Gomoku.Net;
using Gomoku.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Battle
{
    /// <summary>
    /// The four equipped-skill slots. Rebuilt state comes from the owner's
    /// SideStateDto each snapshot (looked up by id: the server map has no
    /// stable order). Slots are greyed while silenced, unaffordable, on
    /// cooldown, exhausted, or not on the player's turn; a tap raises
    /// SkillClicked for the TargetSelector flow.
    /// </summary>
    public sealed class SkillBarView : MonoBehaviour
    {
        public System.Action<SkillSpec> SkillClicked;

        sealed class Slot
        {
            public int SkillId;
            public Button Button;
            public Image Bg;
            public Image CdOverlay;
            public Text Name;
            public Text Cost;
            public Text CdText;
            public Image SelectFrame;
            public Color BaseColor;
        }

        readonly List<Slot> _slots = new List<Slot>(4);

        static readonly Color InfoColor = new Color(0.24f, 0.5f, 0.72f, 1f);
        static readonly Color BuffColor = new Color(0.27f, 0.6f, 0.4f, 1f);
        static readonly Color DebuffColor = new Color(0.66f, 0.3f, 0.34f, 1f);
        static readonly Color BoardColor = new Color(0.55f, 0.46f, 0.28f, 1f);
        static readonly Color SpecialColor = new Color(0.5f, 0.36f, 0.66f, 1f);
        static readonly Color GreyColor = new Color(0.2f, 0.2f, 0.22f, 1f);

        public void Build(Transform parent, IList<int> equippedIds)
        {
            var layer = UIFactory.Panel(transform, "SkillBar");
            var img = layer.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;

            // Band -960..-860: self card ends at -860, screen bottom is -960.
            const float slotW = 240f, slotH = 100f, gap = 16f;
            float x0 = -(3f * slotW + 3f * gap) / 2f + slotW / 2f;
            for (int i = 0; i < 4; i++)
            {
                int id = equippedIds != null && i < equippedIds.Count ? equippedIds[i] : 0;
                var spec = SkillCatalog.ById(id);
                var slot = BuildSlot(parent, spec, x0 + i * (slotW + gap), slotW, slotH);
                _slots.Add(slot);
            }
        }

        Slot BuildSlot(Transform parent, SkillSpec spec, float x, float w, float h)
        {
            var slot = new Slot { SkillId = spec != null ? spec.Id : 0 };

            var go = new GameObject("SkillSlot", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            slot.Bg = go.GetComponent<Image>();
            slot.BaseColor = CategoryColor(spec != null ? spec.Category : SkillCat.Special);
            slot.Bg.color = slot.BaseColor;
            UIFactory.Place((RectTransform)go.transform, x, -910, w, h);

            slot.Button = go.AddComponent<Button>();
            var colors = slot.Button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.32f);
            slot.Button.colors = colors;
            int capturedId = slot.SkillId;
            slot.Button.onClick.AddListener(() =>
            {
                var s = SkillCatalog.ById(capturedId);
                if (s != null) SkillClicked?.Invoke(s);
            });

            slot.Cost = UIFactory.Label(go.transform, "", 26, TextAnchor.UpperLeft);
            var costRt = (RectTransform)slot.Cost.transform;
            costRt.anchorMin = Vector2.zero;
            costRt.anchorMax = Vector2.one;
            costRt.offsetMin = new Vector2(10, 2);
            costRt.offsetMax = new Vector2(-10, -2);
            slot.Cost.color = new Color(1f, 0.86f, 0.35f, 1f);

            slot.Name = UIFactory.Label(go.transform, spec != null ? spec.Name : "—",
                27, TextAnchor.MiddleCenter);
            var nameRt = (RectTransform)slot.Name.transform;
            nameRt.anchorMin = new Vector2(0f, 0.15f);
            nameRt.anchorMax = new Vector2(1f, 0.75f);
            nameRt.offsetMin = new Vector2(6, 0);
            nameRt.offsetMax = new Vector2(-6, 0);

            // Cooldown dim layer.
            var cdGo = new GameObject("Cd", typeof(RectTransform), typeof(Image));
            cdGo.transform.SetParent(go.transform, false);
            slot.CdOverlay = cdGo.GetComponent<Image>();
            slot.CdOverlay.raycastTarget = true; // eats taps through the slot button
            slot.CdOverlay.color = new Color(0f, 0f, 0f, 0.66f);
            var cdRt = (RectTransform)cdGo.transform;
            cdRt.anchorMin = Vector2.zero;
            cdRt.anchorMax = Vector2.one;
            cdRt.offsetMin = cdRt.offsetMax = Vector2.zero;
            slot.CdText = UIFactory.Label(cdGo.transform, "", 40, TextAnchor.MiddleCenter);
            var cdTextRt = (RectTransform)slot.CdText.transform;
            cdTextRt.anchorMin = Vector2.zero;
            cdTextRt.anchorMax = Vector2.one;
            cdTextRt.offsetMin = cdTextRt.offsetMax = Vector2.zero;

            // Selected frame.
            var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            frameGo.transform.SetParent(go.transform, false);
            slot.SelectFrame = frameGo.GetComponent<Image>();
            slot.SelectFrame.raycastTarget = false;
            slot.SelectFrame.sprite = BuildFrameSprite();
            slot.SelectFrame.type = Image.Type.Sliced;
            slot.SelectFrame.color = new Color(1f, 0.92f, 0.35f, 1f);
            var frameRt = (RectTransform)frameGo.transform;
            frameRt.anchorMin = Vector2.zero;
            frameRt.anchorMax = Vector2.one;
            frameRt.offsetMin = new Vector2(-5, -5);
            frameRt.offsetMax = new Vector2(5, 5);
            slot.SelectFrame.gameObject.SetActive(false);

            return slot;
        }

        /// <summary>
        /// Refresh slot visuals. myTurn=false greys every slot. Opponent-side
        /// states are masked (cd=-1) but this bar only ever shows my own side.
        /// </summary>
        public void Apply(SideStateDto side, bool myTurn, int selectedSkillId)
        {
            if (side == null) return;
            foreach (var slot in _slots)
            {
                if (slot.SkillId == 0) continue;
                var spec = SkillCatalog.ById(slot.SkillId);
                var st = FindState(side, slot.SkillId);

                int cost = side.DiscountNext ? Mathf.Max(0, spec.Cost - 3) : spec.Cost;
                slot.Cost.text = cost == 0 ? "免费" : cost.ToString();

                slot.Name.text = spec.MaxUses > 0 && st != null && st.UsesLeft >= 0
                    ? $"{spec.Name}\n<size=20>剩{st.UsesLeft}次</size>"
                    : spec.Name;

                int cd = st != null ? st.Cd : 0;
                bool noUses = spec.MaxUses > 0 && st != null && st.UsesLeft == 0;
                bool silenced = side.SilenceTurns > 0 && slot.SkillId != SkillCatalog.Purify;
                bool afford = side.Energy >= cost;

                bool blocked = cd > 0 || noUses || silenced || !afford;
                slot.Bg.color = (!myTurn || blocked) ? GreyColor : slot.BaseColor;
                slot.Button.interactable = myTurn && !blocked;

                if (cd > 0)
                {
                    slot.CdOverlay.gameObject.SetActive(true);
                    slot.CdText.text = cd.ToString();
                }
                else if (silenced)
                {
                    slot.CdOverlay.gameObject.SetActive(true);
                    slot.CdText.text = "沉默";
                }
                else if (noUses)
                {
                    slot.CdOverlay.gameObject.SetActive(true);
                    slot.CdText.text = "用尽";
                }
                else if (!afford || !myTurn)
                {
                    slot.CdOverlay.gameObject.SetActive(true);
                    slot.CdText.text = "";
                }
                else
                {
                    slot.CdOverlay.gameObject.SetActive(false);
                }

                slot.SelectFrame.gameObject.SetActive(selectedSkillId == slot.SkillId);
            }
        }

        public void ClearSelection()
        {
            foreach (var s in _slots) s.SelectFrame.gameObject.SetActive(false);
        }

        static SkillStateDto FindState(SideStateDto side, int id)
        {
            if (side == null || side.Skills == null) return null;
            foreach (var s in side.Skills) if (s.Id == id) return s;
            return null;
        }

        static Color CategoryColor(string cat)
        {
            switch (cat)
            {
                case SkillCat.Info: return InfoColor;
                case SkillCat.Buff: return BuffColor;
                case SkillCat.Debuff: return DebuffColor;
                case SkillCat.Board: return BoardColor;
                default: return SpecialColor;
            }
        }

        static Sprite BuildFrameSprite()
        {
            const int size = 64, border = 10;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
            void Put(int x, int y)
            {
                if (x >= 0 && y >= 0 && x < size && y < size) px[y * size + x] = Color.white;
            }
            for (int i = 0; i < size; i++)
            {
                Put(i, 0); Put(i, 1); Put(i, 2);
                Put(i, size - 1); Put(i, size - 2); Put(i, size - 3);
                Put(0, i); Put(1, i); Put(2, i);
                Put(size - 1, i); Put(size - 2, i); Put(size - 3, i);
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }
    }
}
