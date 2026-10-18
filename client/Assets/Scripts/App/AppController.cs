using System.Collections.Generic;
using Gomoku.Battle;
using Gomoku.Lobby;
using Gomoku.Net;
using Gomoku.UI;
using UnityEngine;

namespace Gomoku.App
{
    /// <summary>
    /// Application entry point and global context. Builds the UI canvas in
    /// code (no scene assets to maintain), owns the single GameClient, pumps
    /// network events on the main thread and swaps top-level panels.
    /// </summary>
    public sealed class AppController : MonoBehaviour
    {
        public static AppController Instance { get; private set; }

        public GameClient Net { get; private set; }
        public string PlayerId { get; private set; }

        Canvas _canvas;
        RectTransform _panelRoot;
        LoginPanel _login;
        LobbyPanel _lobby;

        // Last known friend-room seat list, used to resolve the opponent
        // nickname when a game_start reaches the lobby via an Attach replay
        // (no room_state was seen in this process).
        RoomStateMsg _lastRoom;
        string _activePanel;
        bool _resetting;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;

            Net = new GameClient();
            Net.LoginOk += OnLoginOk;
            // Global routing for Attach replays after (re)login: room_state
            // opens/caches the friend room; a game_start seen from the lobby
            // (or login panel) jumps straight into battle. Room and battle
            // panels subscribe on their own and win over these routes simply
            // by being shown before these events arrive in normal flows.
            Net.RoomState += OnGlobalRoomState;
            Net.GameStart += OnGlobalGameStart;

            BuildShell();
            ShowLogin();
        }

        void Update() => Net?.Pump();

        void OnDestroy()
        {
            if (Net != null)
            {
                Net.LoginOk -= OnLoginOk;
                Net.RoomState -= OnGlobalRoomState;
                Net.GameStart -= OnGlobalGameStart;
                Net.Dispose();
            }
        }

        void OnApplicationQuit() => Net?.Dispose();

        void BuildShell()
        {
            var canvasGo = UIFactory.CanvasObject("RootCanvas");
            DontDestroyOnLoad(canvasGo);
            _canvas = canvasGo.GetComponent<Canvas>();

            var root = new GameObject("PanelRoot", typeof(RectTransform));
            root.transform.SetParent(canvasGo.transform, false);
            _panelRoot = UIFactory.Stretch(root.GetComponent<RectTransform>());
        }

        GameObject ShowPanel(string name)
        {
            for (int i = 0; i < _panelRoot.childCount; i++)
                Destroy(_panelRoot.GetChild(i).gameObject);

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_panelRoot, false);
            UIFactory.Stretch((RectTransform)go.transform);
            _activePanel = name;
            return go;
        }

        void ShowLogin()
        {
            var go = ShowPanel("LoginPanel");
            _login = go.AddComponent<LoginPanel>();
            _login.Init(Net);
        }

        public void ShowLobby()
        {
            var go = ShowPanel("LobbyPanel");
            _lobby = go.AddComponent<LobbyPanel>();
            _lobby.Init(Net);
        }

        public void ShowBattle(MatchFoundMsg match)
        {
            var go = ShowPanel("BattlePanel");
            var battle = go.AddComponent<BattleController>();
            battle.Init(Net, match);
        }

        public void ShowRoom(RoomStateMsg state)
        {
            _lastRoom = state;
            var go = ShowPanel("RoomPanel");
            var room = go.AddComponent<RoomPanel>();
            room.Init(Net, state);
        }

        // Global Attach-replay routing. RoomPanel itself subscribes to
        // room_state, but gate anyway: when a room panel is live it owns the
        // update and must not be replaced by a rebuilt instance.
        void OnGlobalRoomState(RoomStateMsg msg)
        {
            if (_activePanel == "LobbyPanel" || _activePanel == "LoginPanel")
                ShowRoom(msg);
            else
                _lastRoom = msg;
        }

        /// <summary>
        /// game_start arriving while a room panel is live is handled by the
        /// room panel itself; in battle the BattleController handles rematch
        /// and Attach replays. Only lobby/login need an autonomous jump.
        /// </summary>
        void OnGlobalGameStart(GameStartMsg msg)
        {
            if (_activePanel != "LobbyPanel" && _activePanel != "LoginPanel") return;
            string oppId = null;
            string oppName = "对手";
            if (_lastRoom != null && _lastRoom.Players != null)
            {
                foreach (var p in _lastRoom.Players)
                {
                    bool me = p.Id == PlayerId;
                    if (!me)
                    {
                        oppId = p.Id;
                        oppName = p.Nickname;
                    }
                }
            }
            var match = new MatchFoundMsg
            {
                RoomId = msg.RoomId,
                Color = msg.YourColor,
                Opponent = new OpponentInfo { Id = oppId ?? "", Nickname = oppName },
                SkillIds = msg.YourColor == 0
                    ? new List<int>(msg.BlackSkills)
                    : new List<int>(msg.WhiteSkills)
            };
            ShowBattle(match);
        }

        /// <summary>
        /// Leave a room/over-game that has no server-side leave protocol:
        /// manually drop the socket (no auto-reconnect), then log back in so
        /// the server de-rosters the player and the lobby is attachment-free.
        /// </summary>
        public void ReturnToLobbyFresh()
        {
            if (_resetting) return;
            _resetting = true;
            _lastRoom = null;

            var url = string.IsNullOrEmpty(PlayerProfile.ServerUrl)
                ? ServerConfig.DefaultUrl
                : PlayerProfile.ServerUrl;

            ShowLobby();
            Net.Disconnect();
            Net.Connect(url, PlayerProfile.Nickname, PlayerProfile.DeviceId);
            _resetting = false;
        }

        void OnLoginOk(LoginOkMsg msg) => PlayerId = msg.PlayerId;
    }
}
