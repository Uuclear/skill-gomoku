using System;
using System.Collections.Generic;
using Gomoku.Core;
using Gomoku.Net;
using Gomoku.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Battle
{
    /// <summary>
    /// Drives skill target picking by <see cref="SkillSpec.Target"/>. While
    /// active it owns board taps (normal placement is suspended). Local checks
    /// mirror server/internal/skill/executor.go validateTargets closely enough
    /// to reject obviously bad picks; the server stays authoritative.
    /// </summary>
    public sealed class TargetSelector : MonoBehaviour
    {
        // (skillId, targets, dir) ready to send.
        public Action<int, List<Point>, int> Resolved;

        Snapshot _snap;
        int _myColor;
        SkillSpec _spec;
        readonly List<Point> _picked = new List<Point>(2);
        bool _stonePhase = true; // OWN_TO_POINT: true=picking own stone

        GameObject _bar;
        Text _instruction;
        GameObject _pad;
        Button[] _dirButtons = new Button[4];

        public Action StateChanged;

        public bool Active => _spec != null;
        public SkillSpec Current => _spec;

        public void Build(Transform parent)
        {
            var layer = UIFactory.Panel(transform, "TargetSelector");
            var img = layer.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = false;

            _bar = new GameObject("TargetBar", typeof(RectTransform), typeof(Image));
            _bar.transform.SetParent(parent, false);
            _bar.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.92f);
            UIFactory.Place((RectTransform)_bar.transform, 0, -696, 860, 56);

            _instruction = UIFactory.Label(_bar.transform, "", 30, TextAnchor.MiddleLeft);
            var instrRt = (RectTransform)_instruction.transform;
            instrRt.anchorMin = Vector2.zero;
            instrRt.anchorMax = Vector2.one;
            instrRt.offsetMin = new Vector2(24, 0);
            instrRt.offsetMax = new Vector2(-180, 0);

            var cancel = UIFactory.Button(_bar.transform, "取消", 28);
            var cancelRt = (RectTransform)cancel.transform;
            cancelRt.anchorMin = new Vector2(1f, 0.5f);
            cancelRt.anchorMax = new Vector2(1f, 0.5f);
            cancelRt.pivot = new Vector2(1f, 0.5f);
            cancelRt.anchoredPosition = new Vector2(-16, 0);
            cancelRt.sizeDelta = new Vector2(140, 46);
            cancel.onClick.AddListener(Cancel);

            BuildPad(parent);
            _bar.SetActive(false);
        }

        void BuildPad(Transform parent)
        {
            _pad = new GameObject("DirPad", typeof(RectTransform));
            _pad.transform.SetParent(parent, false);
            UIFactory.Place((RectTransform)_pad.transform, 0, -150, 360, 360);
            _pad.SetActive(false);

            // dir: 0:+x(right) 1:+y(up) 2:-x(left) 3:-y(down)
            AddDirButton(0, "→", 72, 0);
            AddDirButton(1, "↑", 0, 72);
            AddDirButton(2, "←", -72, 0);
            AddDirButton(3, "↓", 0, -72);
        }

        void AddDirButton(int dir, string glyph, float dx, float dy)
        {
            var btn = UIFactory.Button(_pad.transform, glyph, 56);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(dx * 1.7f, dy * 1.7f);
            rt.sizeDelta = new Vector2(118, 118);
            int captured = dir;
            btn.onClick.AddListener(() => SubmitDir(captured));
            _dirButtons[dir] = btn;
        }

        public void Begin(SkillSpec spec, Snapshot snap, int myColor)
        {
            _spec = spec;
            _snap = snap;
            _myColor = myColor;
            _picked.Clear();
            _stonePhase = true;

            if (spec.Target == SkillTarget.None)
            {
                Resolved?.Invoke(spec.Id, null, 0);
                _spec = null;
                return;
            }

            _bar.SetActive(true);
            _instruction.text = InstructionText();
            RefreshPad();
            StateChanged?.Invoke();
        }

        public void Cancel()
        {
            if (_spec == null) return;
            _spec = null;
            _picked.Clear();
            _bar.SetActive(false);
            _pad.SetActive(false);
            StateChanged?.Invoke();
        }

        /// <summary>Board tap routed here while a skill is being targeted.</summary>
        public void OnTap(int x, int y)
        {
            if (_spec == null) return;
            var p = new Point { X = x, Y = y };

            switch (_spec.Target)
            {
                case SkillTarget.Point:
                    if (IsEmpty(x, y)) FinishOne(p);
                    break;
                case SkillTarget.ZoneCenter:
                    FinishOne(p); // any in-bounds point; tap is always in board
                    break;
                case SkillTarget.OwnStone:
                    if (IsOwnStone(x, y) && !(_spec.Id == 8 && IsFortified(x, y)))
                        FinishOne(p);
                    break;
                case SkillTarget.AnyStone:
                    if (IsAnyStone(x, y) && !IsProtected(x, y)) FinishOne(p);
                    break;
                case SkillTarget.OwnToPoint:
                    if (_stonePhase)
                    {
                        if (IsOwnStone(x, y) && !IsFortified(x, y))
                        {
                            _picked.Add(p);
                            _stonePhase = false;
                            _instruction.text = "选择要移动到的空位";
                            StateChanged?.Invoke();
                        }
                    }
                    else if (IsEmpty(x, y))
                    {
                        FinishOne(p);
                    }
                    break;
                case SkillTarget.AnyTwo:
                    if (IsAnyStone(x, y) && !IsFortified(x, y) && !IsProtected(x, y) &&
                        !ContainsPoint(_picked, p))
                    {
                        _picked.Add(p);
                        if (_picked.Count == 2) FinishPicked();
                        else { _instruction.text = "选择第 2 枚棋子"; StateChanged?.Invoke(); }
                    }
                    break;
                case SkillTarget.OwnDir:
                    if (IsOwnStone(x, y) && !IsFortified(x, y))
                    {
                        _picked.Clear();
                        _picked.Add(p);
                        _pad.SetActive(true);
                        _instruction.text = "选择推进方向";
                        RefreshPad();
                        StateChanged?.Invoke();
                    }
                    break;
            }
        }

        void FinishOne(Point p)
        {
            _picked.Add(p);
            FinishPicked();
        }

        void FinishPicked()
        {
            int id = _spec.Id;
            var targets = new List<Point>(_picked);
            _spec = null;
            _bar.SetActive(false);
            _pad.SetActive(false);
            StateChanged?.Invoke();
            Resolved?.Invoke(id, targets, 0);
        }

        void SubmitDir(int dir)
        {
            if (_spec == null || _picked.Count != 1) return;
            var p = _picked[0];
            int tx = p.X + (dir == 0 ? 1 : dir == 2 ? -1 : 0);
            int ty = p.Y + (dir == 1 ? 1 : dir == 3 ? -1 : 0);
            if (!InBoard(tx, ty) || !IsEmpty(tx, ty)) return;

            int id = _spec.Id;
            var targets = new List<Point>(_picked);
            _spec = null;
            _bar.SetActive(false);
            _pad.SetActive(false);
            StateChanged?.Invoke();
            Resolved?.Invoke(id, targets, dir);
        }

        void RefreshPad()
        {
            if (_spec == null || _spec.Target != SkillTarget.OwnDir || _picked.Count != 1)
                return;
            var p = _picked[0];
            for (int dir = 0; dir < 4; dir++)
            {
                int tx = p.X + (dir == 0 ? 1 : dir == 2 ? -1 : 0);
                int ty = p.Y + (dir == 1 ? 1 : dir == 3 ? -1 : 0);
                _dirButtons[dir].interactable = InBoard(tx, ty) && IsEmpty(tx, ty);
            }
        }

        string InstructionText()
        {
            switch (_spec.Target)
            {
                case SkillTarget.Point: return "选择一个空位";
                case SkillTarget.ZoneCenter: return "选择禁区中心";
                case SkillTarget.OwnStone: return "选择一枚己方棋子";
                case SkillTarget.AnyStone: return "选择一枚棋子";
                case SkillTarget.OwnToPoint: return "选择要移动的己方棋子";
                case SkillTarget.AnyTwo: return "选择第 1 枚棋子";
                case SkillTarget.OwnDir: return "选择要推进的己方棋子";
                default: return _spec.Name;
            }
        }

        public IReadOnlyList<Point> Picked => _picked;

        // ---------- board predicates (mirror server checks) ----------

        static bool InBoard(int x, int y) => x >= 0 && y >= 0 && x < 15 && y < 15;

        bool IsEmpty(int x, int y)
        {
            if (_snap.Stones != null)
                foreach (var s in _snap.Stones) if (s.X == x && s.Y == y) return false;
            if (_snap.Phantoms != null)
                foreach (var p in _snap.Phantoms) if (p.X == x && p.Y == y) return false;
            if (_snap.MinePositions != null)
                foreach (var m in _snap.MinePositions) if (m.X == x && m.Y == y) return false;
            return true;
        }

        bool IsOwnStone(int x, int y) => StoneColorAt(x, y) == _myColor;
        bool IsAnyStone(int x, int y) => StoneColorAt(x, y) != -1;

        int StoneColorAt(int x, int y)
        {
            if (_snap.Stones != null)
                foreach (var s in _snap.Stones)
                    if (s.X == x && s.Y == y) return s.C;
            return -1;
        }

        bool IsFortified(int x, int y) => HasMark(_snap.Fortifies, x, y, false);

        // 护盾 only protects against enemy casters; own stones are never
        // blocked by the own shield (mirrors executor.protected).
        bool IsProtected(int x, int y)
        {
            if (IsFortified(x, y)) return true;
            if (StoneColorAt(x, y) == _myColor) return false;
            return HasMark(_snap.Shields, x, y, true);
        }

        bool HasMark(List<MarkPointDto> marks, int x, int y, bool checkExpire)
        {
            if (marks == null) return false;
            foreach (var m in marks)
                if (m.X == x && m.Y == y &&
                    (!checkExpire || m.ExpireAtMove > _snap.MoveCount))
                    return true;
            return false;
        }

        static bool ContainsPoint(List<Point> list, Point p)
        {
            foreach (var q in list) if (q.X == p.X && q.Y == p.Y) return true;
            return false;
        }
    }
}
