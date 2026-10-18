using System.Collections.Generic;
using Gomoku.App;
using Gomoku.Net;
using Gomoku.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Lobby
{
    /// <summary>
    /// Friend-room lobby: shows the big room code, both seats (nickname / host
    /// badge / ready state), the guest's one-shot ready button and the host's
    /// start button. The host is always ready and always black. There is no
    /// server "leave" message: leaving drops the socket and logs back in,
    /// which removes the player from the waiting room server-side.
    /// </summary>
    public sealed class RoomPanel : MonoBehaviour
    {
        GameClient _client;
        RoomStateMsg _state;

        Text _codeLabel;
        Text _hintLabel;
        Text _seat0;
        Text _seat1;
        Button _readyButton;
        Button _startButton;
        Button _leaveButton;

        string _myId;
        bool _readySent;
        bool _enteringGame;

        public void Init(GameClient client, RoomStateMsg state)
        {
            _client = client;
            _myId = AppController.Instance.PlayerId;
            Build();
            ApplyState(state);

            _client.RoomState += OnRoomState;
            _client.GameStart += OnGameStart;
            _client.OpponentLeft += OnOpponentLeft;
            _client.Error += OnError;
            _client.StateChanged += OnStateChanged;
        }

        void OnDestroy()
        {
            if (_client == null) return;
            _client.RoomState -= OnRoomState;
            _client.GameStart -= OnGameStart;
            _client.OpponentLeft -= OnOpponentLeft;
            _client.Error -= OnError;
            _client.StateChanged -= OnStateChanged;
        }

        bool IsHost => _state != null && _state.HostId == _myId;

        void Build()
        {
            UIFactory.Panel(transform, "RoomPanel");

            var title = UIFactory.Label(transform, "好友房间", 56, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)title.transform, 0, 780, 800, 90);

            var codeCap = UIFactory.Label(transform, "房间码（发给好友）", 30, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)codeCap.transform, 0, 640, 800, 50);
            codeCap.color = new Color(1f, 1f, 1f, 0.7f);

            var codeCard = new GameObject("CodeCard", typeof(RectTransform), typeof(Image));
            codeCard.transform.SetParent(transform, false);
            codeCard.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);
            UIFactory.Place((RectTransform)codeCard.transform, 0, 500, 620, 190);

            _codeLabel = UIFactory.Label(codeCard.transform, "", 110, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)_codeLabel.transform, 0, 0, 560, 160);
            _codeLabel.color = new Color(1f, 0.9f, 0.4f, 1f);

            BuildSeat(0, out _seat0);
            BuildSeat(1, out _seat1);

            _hintLabel = UIFactory.Label(transform, "", 32, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)_hintLabel.transform, 0, 60, 900, 70);
            _hintLabel.color = new Color(0.55f, 0.85f, 1f, 1f);

            _readyButton = UIFactory.Button(transform, "准 备", 44);
            UIFactory.Place((RectTransform)_readyButton.transform, 0, -120, 700, 110);
            _readyButton.onClick.AddListener(OnReadyClick);

            _startButton = UIFactory.Button(transform, "开始对局", 44);
            UIFactory.Place((RectTransform)_startButton.transform, 0, -120, 700, 110);
            _startButton.onClick.AddListener(OnStartClick);

            _leaveButton = UIFactory.Button(transform, "离开房间", 36);
            UIFactory.Place((RectTransform)_leaveButton.transform, 0, -280, 700, 90);
            var leaveColors = _leaveButton.colors;
            _leaveButton.targetGraphic.color = new Color(0.55f, 0.28f, 0.28f, 1f);
            _leaveButton.onClick.AddListener(OnLeaveClick);
        }

        void BuildSeat(int index, out Text label)
        {
            var y = index == 0 ? 280 : 140;
            var card = new GameObject($"Seat{index}", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(transform, false);
            card.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.32f);
            UIFactory.Place((RectTransform)card.transform, 0, y, 880, 110);

            label = UIFactory.Label(card.transform, "", 38, TextAnchor.MiddleLeft);
            var rt = (RectTransform)label.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(40, 0);
            rt.offsetMax = new Vector2(-40, 0);
        }

        RoomPlayerInfo Seat(int colorIndex)
        {
            // order[0] is host (black), order[1] is guest (white).
            if (_state.Players == null || colorIndex >= _state.Players.Count) return null;
            return _state.Players[colorIndex];
        }

        void ApplyState(RoomStateMsg state)
        {
            _state = state;
            _codeLabel.text = state.RoomCode;

            var black = Seat(0);
            var white = Seat(1);
            _seat0.text = SeatText(black, "黑方", true);
            _seat1.text = white != null ? SeatText(white, "白方", false) : "白方：虚位以待…";

            bool host = IsHost;
            _startButton.gameObject.SetActive(host);
            _readyButton.gameObject.SetActive(!host);

            if (host)
            {
                bool guestReady = white != null && white.Ready;
                _startButton.interactable = guestReady;
                _hintLabel.text = guestReady ? "好友已准备，可以开始" : "等待好友加入并准备…";
            }
            else
            {
                bool ready = _readySent || (white != null && white.Ready);
                _readyButton.interactable = !ready;
                _readyButton.GetComponentInChildren<Text>().text = ready ? "已准备，等待房主开始" : "准 备";
                _hintLabel.text = ready ? "等待房主开始对局…" : "准备后房主即可开始";
            }
        }

        static string SeatText(RoomPlayerInfo p, string side, bool host)
        {
            if (p == null) return $"{side}：—";
            var hostTag = host ? " [房主]" : "";
            var ready = p.Ready ? "✓ 已准备" : "未准备";
            return $"{side}：{p.Nickname}{hostTag}    {ready}";
        }

        void OnRoomState(RoomStateMsg msg) => ApplyState(msg);

        void OnGameStart(GameStartMsg msg)
        {
            if (_enteringGame) return;
            _enteringGame = true;

            var other = msg.YourColor == 0 ? Seat(1) : Seat(0);
            var match = new MatchFoundMsg
            {
                RoomId = msg.RoomId,
                Color = msg.YourColor,
                SkillIds = msg.YourColor == 0
                    ? new List<int>(msg.BlackSkills)
                    : new List<int>(msg.WhiteSkills),
                Opponent = new OpponentInfo
                {
                    Id = other != null ? other.Id : "",
                    Nickname = other != null ? other.Nickname : "对手"
                }
            };
            AppController.Instance.ShowBattle(match);
        }

        void OnOpponentLeft(OpponentLeftMsg msg)
        {
            // Only meaningful while waiting: the guest left.
            if (IsHost) _hintLabel.text = "好友已离开房间";
        }

        void OnError(ErrorMsg msg)
        {
            switch (msg.Code)
            {
                case "not_host": _hintLabel.text = "只有房主可以开始"; break;
                case "not_ready": _hintLabel.text = "好友还未准备"; break;
                case "not_in_room":
                    AppController.Instance.ReturnToLobbyFresh();
                    break;
            }
        }

        void OnStateChanged(ConnState state)
        {
            if (state == ConnState.Disconnected)
                AppController.Instance.ReturnToLobbyFresh();
        }

        void OnReadyClick()
        {
            _readySent = true;
            _readyButton.interactable = false;
            _readyButton.GetComponentInChildren<Text>().text = "已准备，等待房主开始";
            _hintLabel.text = "等待房主开始对局…";
            _client.Ready();
        }

        void OnStartClick()
        {
            _startButton.interactable = false;
            _hintLabel.text = "正在开始…";
            _client.Start();
        }

        void OnLeaveClick()
        {
            // No leave protocol: a manual drop removes a waiting member (or
            // destroys the host's room); reconnect + login replay lands in a
            // clean lobby with no room attachment.
            AppController.Instance.ReturnToLobbyFresh();
        }
    }
}
