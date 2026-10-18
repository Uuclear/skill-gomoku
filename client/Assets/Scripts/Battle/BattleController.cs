using System;
using System.Collections.Generic;
using Gomoku.App;
using Gomoku.Core;
using Gomoku.Net;
using Gomoku.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Battle
{
    /// <summary>
    /// Drives the battle scene: applies authoritative snapshots to the board,
    /// sends move intents, runs the turn countdown locally, handles emotes,
    /// surrender (two-tap confirm), disconnect/reconnect, the game-over
    /// dialog and the rematch vote.
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        GameClient _client;
        MatchFoundMsg _match;

        BoardView _board;
        HudView _hud;
        MarkerView _markers;
        SkillBarView _skillBar;
        TargetSelector _selector;
        EffectPlayer _fx;
        EmoteView _emotes;

        Text _waiting;
        Text _toast;
        GameObject _resultOverlay;
        Text _resultText;
        Button _rematchButton;
        GameObject _votePair;
        Text _hintLabel;
        Button _surrenderButton;
        Text _surrenderText;
        List<Button> _emoteButtons;

        GameObject _reconnectOverlay;
        Button _reconnectBackButton;

        int _myColor;
        int _turnSeconds = 60;
        bool _inGame;
        bool _gameOver;
        Snapshot _snap;
        Point? _lastMove;
        float _toastUntil;
        List<int> _mySkills;

        // two-tap surrender
        bool _surrenderArmed;
        float _surrenderArmedUntil;

        public void Init(GameClient client, MatchFoundMsg match)
        {
            _client = client;
            _match = match;
            _myColor = match.Color;

            UIFactory.Panel(transform, "BattlePanel");
            _hud = gameObject.AddComponent<HudView>();
            _hud.Build(transform, _myColor, PlayerProfile.Nickname, match.Opponent.Nickname);

            _board = gameObject.AddComponent<BoardView>();
            _board.Build(transform, 15);
            _board.SetInputEnabled(false);
            _board.CellTapped += OnCellTapped;

            _markers = gameObject.AddComponent<MarkerView>();
            _markers.Build(_board.Root, 15);

            _fx = gameObject.AddComponent<EffectPlayer>();
            _fx.Build(transform);
            _fx.AttachBoard(_board.Root, 15);

            _emotes = gameObject.AddComponent<EmoteView>();
            _emotes.Build(transform, _myColor);

            _mySkills = match.SkillIds != null
                ? new List<int>(match.SkillIds)
                : new List<int>();
            _skillBar = gameObject.AddComponent<SkillBarView>();
            _skillBar.Build(transform, _mySkills);
            _skillBar.SkillClicked += OnSkillClicked;

            _selector = gameObject.AddComponent<TargetSelector>();
            _selector.Build(transform);
            _selector.Resolved += OnSkillResolved;
            _selector.StateChanged += OnSelectorChanged;

            _waiting = UIFactory.Label(transform, "等待开局…", 40, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)_waiting.transform, 0, -150, 800, 100);

            BuildEmoteBar();
            BuildSurrenderButton();
            BuildToast();
            BuildResultOverlay();
            BuildReconnectOverlay();

            SetInGameControls(false);

            _client.GameStart += OnGameStart;
            _client.GameEvent += OnGameEvent;
            _client.GameOver += OnGameOver;
            _client.TurnExpireWarn += OnTurnExpireWarn;
            _client.OpponentLeft += OnOpponentLeft;
            _client.Error += OnError;
            _client.StateChanged += OnStateChanged;
            _client.SkillPrivate += OnSkillPrivate;
            _client.Emote += OnEmote;
            _client.RematchRequested += OnRematchRequested;
            _client.ReconnectFailed += OnReconnectFailed;
        }

        void OnDestroy()
        {
            if (_client == null) return;
            _client.GameStart -= OnGameStart;
            _client.GameEvent -= OnGameEvent;
            _client.GameOver -= OnGameOver;
            _client.TurnExpireWarn -= OnTurnExpireWarn;
            _client.OpponentLeft -= OnOpponentLeft;
            _client.Error -= OnError;
            _client.StateChanged -= OnStateChanged;
            _client.SkillPrivate -= OnSkillPrivate;
            _client.Emote -= OnEmote;
            _client.RematchRequested -= OnRematchRequested;
            _client.ReconnectFailed -= OnReconnectFailed;
        }

        // ---------- in-game controls ----------

        void BuildEmoteBar()
        {
            _emoteButtons = new List<Button>(EmoteView.Count);
            float w = 150f, h = 80f, gap = 10f;
            float total = EmoteView.Count * w + (EmoteView.Count - 1) * gap;
            float x0 = -total * 0.5f + w * 0.5f;
            for (int i = 0; i < EmoteView.Count; i++)
            {
                var btn = UIFactory.Button(transform, EmoteView.Label(i), 30);
                UIFactory.Place((RectTransform)btn.transform, x0 + i * (w + gap), 470, w, h);
                btn.GetComponent<Image>().color = new Color(0.3f, 0.32f, 0.38f, 0.95f);
                int id = i;
                btn.onClick.AddListener(() => OnEmoteClicked(id));
                _emoteButtons.Add(btn);
            }
        }

        void BuildSurrenderButton()
        {
            _surrenderButton = UIFactory.Button(transform, "投降", 32);
            UIFactory.Place((RectTransform)_surrenderButton.transform, -430, 560, 160, 84);
            _surrenderButton.GetComponent<Image>().color = new Color(0.55f, 0.3f, 0.28f, 1f);
            _surrenderText = _surrenderButton.GetComponentInChildren<Text>();
            _surrenderButton.onClick.AddListener(OnSurrenderClick);
        }

        void BuildToast()
        {
            _toast = UIFactory.Label(transform, "", 34, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)_toast.transform, 0, 420, 960, 80);
            _toast.color = new Color(1f, 0.85f, 0.35f, 1f);
            _toast.raycastTarget = false;
            _toast.gameObject.SetActive(false);
        }

        void BuildResultOverlay()
        {
            _resultOverlay = new GameObject("ResultOverlay", typeof(RectTransform), typeof(Image));
            _resultOverlay.transform.SetParent(transform, false);
            UIFactory.Stretch((RectTransform)_resultOverlay.transform);
            _resultOverlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            _resultText = UIFactory.Label(_resultOverlay.transform, "", 64, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)_resultText.transform, 0, 240, 900, 220);

            _hintLabel = UIFactory.Label(_resultOverlay.transform, "", 32, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)_hintLabel.transform, 0, 60, 900, 80);
            _hintLabel.color = new Color(1f, 0.9f, 0.5f, 1f);

            _rematchButton = UIFactory.Button(_resultOverlay.transform, "再来一局", 40);
            UIFactory.Place((RectTransform)_rematchButton.transform, 0, -60, 560, 110);
            _rematchButton.onClick.AddListener(OnRematchClick);

            _votePair = new GameObject("VotePair", typeof(RectTransform));
            _votePair.transform.SetParent(_resultOverlay.transform, false);
            UIFactory.Stretch((RectTransform)_votePair.transform);

            var accept = UIFactory.Button(_votePair.transform, "接受", 40);
            UIFactory.Place((RectTransform)accept.transform, -160, -60, 280, 110);
            accept.onClick.AddListener(() => _client.Rematch(true));

            var refuse = UIFactory.Button(_votePair.transform, "拒绝", 36);
            UIFactory.Place((RectTransform)refuse.transform, 160, -60, 280, 110);
            refuse.GetComponent<Image>().color = new Color(0.55f, 0.3f, 0.28f, 1f);
            refuse.onClick.AddListener(() => _client.Rematch(false));

            var back = UIFactory.Button(_resultOverlay.transform, "返回大厅", 38);
            UIFactory.Place((RectTransform)back.transform, 0, -220, 560, 100);
            back.GetComponent<Image>().color = new Color(0.3f, 0.32f, 0.38f, 1f);
            back.onClick.AddListener(() => AppController.Instance.ReturnToLobbyFresh());

            _resultOverlay.SetActive(false);
        }

        void BuildReconnectOverlay()
        {
            _reconnectOverlay = new GameObject("ReconnectOverlay", typeof(RectTransform), typeof(Image));
            _reconnectOverlay.transform.SetParent(transform, false);
            UIFactory.Stretch((RectTransform)_reconnectOverlay.transform);
            _reconnectOverlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            var label = UIFactory.Label(_reconnectOverlay.transform, "断线重连中…", 44, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)label.transform, 0, 60, 800, 100);

            _reconnectBackButton = UIFactory.Button(_reconnectOverlay.transform, "返回大厅", 38);
            UIFactory.Place((RectTransform)_reconnectBackButton.transform, 0, -120, 560, 100);
            _reconnectBackButton.onClick.AddListener(
                () => AppController.Instance.ReturnToLobbyFresh());
            _reconnectBackButton.gameObject.SetActive(false);

            _reconnectOverlay.SetActive(false);
        }

        void SetInGameControls(bool on)
        {
            if (_surrenderButton != null) _surrenderButton.gameObject.SetActive(on);
            if (_emoteButtons != null)
                foreach (var b in _emoteButtons) b.interactable = on;
        }

        void Update()
        {
            if (_toast != null && _toast.gameObject.activeSelf && Time.time > _toastUntil)
                _toast.gameObject.SetActive(false);

            if (_surrenderArmed && Time.time > _surrenderArmedUntil)
            {
                _surrenderArmed = false;
                _surrenderText.text = "投降";
            }

            if (!_inGame || _gameOver || _snap == null) return;
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            float remainSec = (_snap.TurnEndsAtMs - nowMs) / 1000f;
            _hud.SetTimer(remainSec / _turnSeconds, Mathf.CeilToInt(remainSec));
        }

        // ---------- server events ----------

        void OnGameStart(GameStartMsg msg)
        {
            _inGame = true;
            _gameOver = false;
            _myColor = msg.YourColor;
            _turnSeconds = msg.Config.TurnSeconds;
            _lastMove = null;
            _mySkills = msg.YourColor == 0
                ? new List<int>(msg.BlackSkills)
                : new List<int>(msg.WhiteSkills);
            _surrenderArmed = false;
            _selector?.Cancel();
            _markers?.ClearHints();
            _markers?.ClearWinLine();
            _resultOverlay.SetActive(false);
            if (_waiting != null) _waiting.gameObject.SetActive(false);
            SetInGameControls(true);
        }

        void OnGameEvent(GameEventMsg msg)
        {
            if (!_inGame) _inGame = true;
            int prevEnergy = MySide(_snap)?.Energy ?? -1;

            _snap = msg.State;

            var ev = msg.Event;
            Point? animate = null;
            bool placement = ev.Kind == "move" || ev.Kind == "double_first" ||
                              ev.Kind == "double_second" || ev.Kind == "phantom";
            if (placement && ev.X.HasValue && ev.Y.HasValue)
            {
                var p = new Point { X = ev.X.Value, Y = ev.Y.Value };
                animate = p;
                if (ev.Kind != "phantom") _lastMove = p;
            }
            if (ev.Kind == "undo" || ev.Kind == "bomb" || ev.Kind == "disarm" ||
                ev.Kind == "rewind" || ev.Kind == "mine_trigger")
                _lastMove = null;

            // Hints (神谕/雷达) describe the position right after my own info
            // skill; every subsequent state change invalidates them.
            if (ev.Kind != "turn_start")
                _markers.ClearHints();

            _board.Apply(_snap, animate, _lastMove);
            _markers.Apply(_snap, _snap.MoveCount);
            ApplyHud();

            if (prevEnergy >= 0 && MySide(_snap).Energy != prevEnergy)
                _fx.FloatingEnergy(MySide(_snap).Energy - prevEnergy);

            // Pulse on concrete board-affecting public events.
            bool own = ev.Actor == _myColor;
            if (ev.Kind == "skill" && ev.SkillId.HasValue && ev.X.HasValue && ev.Y.HasValue &&
                BoardAffecting(ev.SkillId.Value))
                _fx.Pulse(ev.X.Value, ev.Y.Value, own);
            if ((ev.Kind == "bomb" || ev.Kind == "undo" || ev.Kind == "mine_trigger")
                && ev.X.HasValue && ev.Y.HasValue)
                _fx.Pulse(ev.X.Value, ev.Y.Value, own);
            if (ev.Kind == "disarm" && ev.Points != null && ev.Points.Count > 0)
                _fx.Pulse(ev.Points[0].X, ev.Points[0].Y, own);

            RefreshInput();
        }

        static bool BoardAffecting(int skillId)
        {
            // Info / pure economy skills have no board point to pulse at;
            // note: 5 mine is already suppressed for the opponent by the server.
            switch (skillId)
            {
                case 11: case 12: case 13: case 14: case 15:
                case 18: case 19: case 20: case 21: case 22:
                case 23: case 25: case 26: case 27: case 28:
                case 29: case 30:
                    return false;
                default:
                    return true;
            }
        }

        void OnGameOver(GameOverMsg msg)
        {
            _gameOver = true;
            SetInGameControls(false);
            RefreshInput();

            _markers.ShowWinLine(msg.WinLine);

            string title;
            if (!msg.Winner.HasValue) title = "平 局";
            else title = msg.Winner.Value == _myColor ? "你赢了！" : "你输了";
            _resultText.text = $"{title}\n<size=36>{ReasonText(msg.Reason)}</size>";

            // A game lost via disconnect grace cannot be rematched server-side.
            bool canRematch = msg.Reason != "left";
            StartRematchDialog(canRematch);
            _resultOverlay.SetActive(true);
        }

        void StartRematchDialog(bool canRematch)
        {
            _rematchButton.gameObject.SetActive(canRematch);
            _votePair.SetActive(false);
            _hintLabel.text = canRematch ? "" : "对手已离开，无法再来一局";
            _rematchButton.interactable = true;
            _rematchButton.GetComponentInChildren<Text>().text = "再来一局";
        }

        void OnTurnExpireWarn(TurnExpireWarnMsg msg) => Toast($"剩余 {msg.RemainSeconds} 秒");

        void OnOpponentLeft(OpponentLeftMsg msg)
        {
            if (_gameOver && msg.Reason == "left")
            {
                // Opponent declined the rematch or left after the game ended.
                StartRematchDialog(false);
                return;
            }
            Toast(msg.Reason == "disconnect"
                ? "对手断线，30秒内可返回"
                : "对手已离开");
        }

        void OnError(ErrorMsg msg)
        {
            if (_gameOver)
            {
                // Only surface liveness problems on the result dialog; stale
                // rejections of post-game votes are silent.
                if (msg.Code == "not_in_room") StartRematchDialog(false);
                return;
            }
            Toast(ErrorText(msg.Code, msg.Message));
            RefreshInput();
        }

        void OnStateChanged(ConnState state)
        {
            if (_reconnectOverlay == null) return;
            bool reconnecting = state == ConnState.Reconnecting;
            _reconnectOverlay.SetActive(reconnecting);
            _reconnectBackButton.gameObject.SetActive(false);
        }

        void OnReconnectFailed(string reason)
        {
            if (_reconnectOverlay == null) return;
            _reconnectOverlay.SetActive(true);
            _reconnectBackButton.gameObject.SetActive(true);
            Toast(reason);
        }

        void OnSkillPrivate(PrivateSkillMsg msg)
        {
            if (msg.Payload == null) return;
            switch (msg.SkillId)
            {
                case SkillCatalog.Oracle:
                case SkillCatalog.Radar:
                    var oracle = msg.Payload.ToObject<OraclePayload>();
                    _markers.ShowHints(oracle.Points, msg.SkillId == SkillCatalog.Radar);
                    break;
                case SkillCatalog.Analyze:
                    var a = msg.Payload.ToObject<AnalyzePayload>();
                    var me = _myColor == 0 ? a.Black : a.White;
                    var fo = _myColor == 0 ? a.White : a.Black;
                    Toast($"我方 最长{me.Longest} 活三{me.Open3} 活四{me.Open4}\n" +
                          $"对手 最长{fo.Longest} 活三{fo.Open3} 活四{fo.Open4}");
                    break;
            }
        }

        // ---------- emotes / rematch ----------

        void OnEmote(EmoteMsg msg) => _emotes.Show(msg.FromColor, msg.Id);

        void OnEmoteClicked(int id)
        {
            if (!_inGame || _gameOver) return;
            _client.SendEmote(id);
        }

        void OnRematchRequested(RematchRequestedMsg msg)
        {
            if (!_gameOver) return;
            _rematchButton.gameObject.SetActive(false);
            _votePair.SetActive(true);
            _hintLabel.text = "对手想再来一局";
        }

        void OnRematchClick()
        {
            _client.Rematch(true);
            _rematchButton.interactable = false;
            _rematchButton.GetComponentInChildren<Text>().text = "已邀请，等待回应…";
            _hintLabel.text = "";
        }

        // ---------- surrender ----------

        void OnSurrenderClick()
        {
            if (!_inGame || _gameOver) return;
            if (!_surrenderArmed)
            {
                _surrenderArmed = true;
                _surrenderArmedUntil = Time.time + 3f;
                _surrenderText.text = "再点确认";
                return;
            }
            _surrenderArmed = false;
            _surrenderText.text = "投降";
            _client.Surrender();
            _surrenderButton.interactable = false;
        }

        // ---------- input ----------

        void OnCellTapped(int x, int y)
        {
            if (_selector.Active)
            {
                _selector.OnTap(x, y);
                return;
            }
            if (!CanMove()) return;
            _client.Move(x, y);
            _board.SetInputEnabled(false);
        }

        void OnSkillClicked(SkillSpec spec)
        {
            if (!CanMove()) return;
            // Tapping the active skill again cancels targeting.
            if (_selector.Active && _selector.Current.Id == spec.Id)
            {
                _selector.Cancel();
                return;
            }
            _selector.Begin(spec, _snap, _myColor);
        }

        void OnSkillResolved(int skillId, List<Point> targets, int dir)
        {
            _client.UseSkill(skillId, targets, dir);
            RefreshInput();
        }

        void OnSelectorChanged()
        {
            if (_selector.Active)
                _markers.ShowSelection(new List<Point>(_selector.Picked));
            else
                _markers.ClearSelection();
            RefreshInput();
        }

        bool CanMove() =>
            _inGame && !_gameOver && _snap != null &&
            _snap.Status == "playing" && _snap.Turn == _myColor;

        void RefreshInput()
        {
            bool selecting = _selector.Active;
            _board.SetInputEnabled(CanMove()); // taps route via OnCellTapped either way
            var mine = MySide(_snap);
            _skillBar.Apply(mine, CanMove(), selecting ? _selector.Current.Id : 0);
        }

        SideStateDto MySide(Snapshot snap)
        {
            if (snap == null) return null;
            return _myColor == 0 ? snap.Black : snap.White;
        }

        SideStateDto OppSide(Snapshot snap)
        {
            if (snap == null) return null;
            return _myColor == 0 ? snap.White : snap.Black;
        }

        void ApplyHud()
        {
            var mine = MySide(_snap);
            var opp = OppSide(_snap);
            _hud.Apply(_snap.Turn, _myColor, mine.Energy, opp.Energy);
        }

        void Toast(string text)
        {
            _toast.text = text;
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.time + 2.5f;
        }

        static string ReasonText(string reason)
        {
            switch (reason)
            {
                case "five": return "五子连珠";
                case "surrender": return "对手投降";
                case "timeout": return "超时判负";
                case "full": return "棋盘下满，平局";
                case "left": return "对手离开";
                default: return "";
            }
        }

        static string ErrorText(string code, string fallback)
        {
            switch (code)
            {
                case "illegal_point": return "该位置不能落子";
                case "point_blocked": return "此处已被封锁";
                case "not_your_turn": return "还没轮到你";
                case "game_over": return "对局已结束";
                case "bad_state": return "当前状态无法操作";
                default: return string.IsNullOrEmpty(fallback) ? code : fallback;
            }
        }
    }
}
