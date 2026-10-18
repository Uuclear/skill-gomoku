using System;
using System.Collections.Generic;
using Gomoku.App;
using Gomoku.Core;
using Gomoku.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Lobby
{
    /// <summary>
    /// Full-screen overlay for picking exactly 4 of the 30 skills. Shared by
    /// quick match and friend rooms. The picker does not persist anything
    /// until "confirm"; on confirm the loadout is saved to PlayerProfile and
    /// reported through the callback, then the overlay destroys itself.
    /// </summary>
    public sealed class SkillSelectPanel : MonoBehaviour
    {
        const int MaxPick = 4;

        readonly List<int> _picked = new List<int>(MaxPick);
        readonly List<CellView> _cells = new List<CellView>(SkillCatalog.All.Count);

        Action<int[]> _onConfirm;
        Text _countLabel;
        Button _confirmButton;
        Text _toast;
        Image _toastBg;

        sealed class CellView
        {
            public SkillSpec Spec;
            public Image Border;
            public Image Inner;
            public Text Order;
        }

        public void Init(IReadOnlyList<int> current, Action<int[]> onConfirm)
        {
            _onConfirm = onConfirm;
            if (current != null)
            {
                foreach (var id in current)
                {
                    if (_picked.Count >= MaxPick) break;
                    if (SkillCatalog.ById(id) != null && !_picked.Contains(id))
                        _picked.Add(id);
                }
            }
            Build();
            Refresh();
        }

        void Build()
        {
            UIFactory.Panel(transform, "SkillSelectPanel");

            var title = UIFactory.Label(transform, "选择技能（30选4）", 54, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)title.transform, -60, 845, 800, 80);

            var back = UIFactory.Button(transform, "返回", 32);
            UIFactory.Place((RectTransform)back.transform, 400, 845, 170, 80);
            back.onClick.AddListener(Close);

            _countLabel = UIFactory.Label(transform, "", 32, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)_countLabel.transform, 0, 760, 900, 50);
            _countLabel.color = new Color(0.55f, 0.85f, 1f, 1f);

            BuildGrid();

            _confirmButton = UIFactory.Button(transform, "确认选择", 42);
            UIFactory.Place((RectTransform)_confirmButton.transform, 0, -870, 760, 110);
            _confirmButton.onClick.AddListener(OnConfirmClick);

            _toast = UIFactory.Toast(transform, out _toastBg, 34);
            var toastRt = (RectTransform)_toast.transform.parent;
            toastRt.SetAsLastSibling();
            UIFactory.Place(toastRt, 0, 660, 800, 80);
            _toast.color = new Color(1f, 0.85f, 0.3f, 1f);
            toastRt.gameObject.SetActive(false);
        }

        void BuildGrid()
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(transform, false);
            UIFactory.Place((RectTransform)scrollGo.transform, 0, -90, 1000, 1360);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 220f;
            scroll.inertia = true;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewport = UIFactory.Stretch((RectTransform)viewportGo.transform);
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            scroll.viewport = viewport;

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewport, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 2000f);

            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.spacing = new Vector2(16f, 16f);
            grid.cellSize = new Vector2(482f, 216f);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperCenter;

            contentGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;

            foreach (var spec in SkillCatalog.All)
                BuildCell(content, spec);
        }

        void BuildCell(Transform parent, SkillSpec spec)
        {
            var cell = new CellView { Spec = spec };

            var go = new GameObject($"Skill_{spec.Id}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            cell.Border = go.GetComponent<Image>();
            cell.Border.color = new Color(0.08f, 0.09f, 0.11f, 1f);

            var innerGo = new GameObject("Inner", typeof(RectTransform), typeof(Image));
            innerGo.transform.SetParent(go.transform, false);
            var innerRt = (RectTransform)innerGo.transform;
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(7, 7);
            innerRt.offsetMax = new Vector2(-7, -7);
            cell.Inner = innerGo.GetComponent<Image>();
            cell.Inner.raycastTarget = false;
            cell.Inner.color = CategoryColor(spec.Category);

            var name = UIFactory.Label(innerGo.transform, spec.Name, 32, TextAnchor.MiddleLeft);
            AnchorFill(name.rectTransform, 16, -8, -150, -52);
            name.color = Color.white;
            name.raycastTarget = false;

            cell.Order = UIFactory.Label(innerGo.transform, "", 28, TextAnchor.MiddleRight);
            AnchorFill(cell.Order.rectTransform, -140, -10, -14, -50);
            cell.Order.raycastTarget = false;
            cell.Order.color = new Color(1f, 0.9f, 0.35f, 1f);

            var stats = UIFactory.Label(innerGo.transform, StatsText(spec), 24, TextAnchor.MiddleLeft);
            AnchorFill(stats.rectTransform, 16, -54, -14, -86);
            stats.raycastTarget = false;
            stats.color = new Color(1f, 1f, 1f, 0.85f);

            var desc = UIFactory.Label(innerGo.transform, spec.Desc, 23, TextAnchor.UpperLeft);
            AnchorFill(desc.rectTransform, 16, -90, -14, -12);
            desc.raycastTarget = false;
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.color = new Color(1f, 1f, 1f, 0.8f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = cell.Border;
            button.onClick.AddListener(() => OnCellClick(cell));

            _cells.Add(cell);
        }

        static void AnchorFill(RectTransform rt, float left, float top, float right, float bottom)
        {
            // Offcuts relative to a stretched rect: positive values grow inward.
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(right, top);
        }

        static string StatsText(SkillSpec spec)
        {
            var uses = spec.MaxUses > 0 ? $" · 限{spec.MaxUses}次" : "";
            var cd = spec.Cooldown > 0 ? $"冷却{spec.Cooldown}" : "无冷却";
            return $"费{spec.Cost} · {cd}{uses}";
        }

        static Color CategoryColor(string cat)
        {
            switch (cat)
            {
                case SkillCat.Info: return new Color(0.35f, 0.60f, 0.95f);
                case SkillCat.Buff: return new Color(0.45f, 0.75f, 0.35f);
                case SkillCat.Debuff: return new Color(0.85f, 0.40f, 0.35f);
                case SkillCat.Board: return new Color(0.70f, 0.55f, 0.35f);
                case SkillCat.Special: return new Color(0.65f, 0.45f, 0.85f);
                default: return Color.gray;
            }
        }

        void OnCellClick(CellView cell)
        {
            var idx = _picked.IndexOf(cell.Spec.Id);
            if (idx >= 0)
            {
                _picked.RemoveAt(idx);
                Refresh();
                return;
            }
            if (_picked.Count >= MaxPick)
            {
                ShowToast("最多选择4个技能");
                return;
            }
            _picked.Add(cell.Spec.Id);
            Refresh();
        }

        void Refresh()
        {
            foreach (var cell in _cells)
            {
                var idx = _picked.IndexOf(cell.Spec.Id);
                var selected = idx >= 0;
                var col = CategoryColor(cell.Spec.Category);
                col.a = selected ? 0.92f : 0.30f;
                cell.Inner.color = col;
                cell.Border.color = selected
                    ? new Color(1f, 0.85f, 0.2f, 1f)
                    : new Color(0.08f, 0.09f, 0.11f, 1f);
                cell.Order.text = selected ? $"已选 {idx + 1}" : "";
            }
            _countLabel.text = $"已选 {_picked.Count}/{MaxPick} · 点击卡片选择，再点取消";
            _confirmButton.interactable = _picked.Count == MaxPick;
        }

        void OnConfirmClick()
        {
            if (_picked.Count != MaxPick)
            {
                ShowToast($"还需选择 {MaxPick - _picked.Count} 个技能");
                return;
            }
            var loadout = _picked.ToArray();
            PlayerProfile.LastSkillIds = loadout;
            PlayerProfile.Save();
            _onConfirm?.Invoke(loadout);
            Close();
        }

        void ShowToast(string text)
        {
            _toast.text = text;
            _toast.transform.parent.gameObject.SetActive(true);
            _toastBg.color = new Color(0.1f, 0.08f, 0.02f, 0.85f);
            StopAllCoroutines();
            StartCoroutine(HideToast());
        }

        System.Collections.IEnumerator HideToast()
        {
            yield return new WaitForSeconds(1.3f);
            _toast.transform.parent.gameObject.SetActive(false);
        }

        void Close() => Destroy(gameObject);
    }
}
