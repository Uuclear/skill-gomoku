using Gomoku.App;
using Gomoku.Net;
using Gomoku.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Lobby
{
    /// <summary>
    /// Nickname + server URL entry. Connecting (and the implicit login replay)
    /// is triggered here; lobby appears on the first login_ok.
    /// </summary>
    public sealed class LoginPanel : MonoBehaviour
    {
        GameClient _client;
        InputField _nickInput;
        InputField _urlInput;
        Button _loginButton;
        Text _status;

        public void Init(GameClient client)
        {
            _client = client;
            Build();

            _client.StateChanged += OnStateChanged;
            _client.LoginOk += OnLoginOk;
            _client.Error += OnError;
            _client.ReconnectFailed += OnReconnectFailed;
        }

        void OnDestroy()
        {
            if (_client == null) return;
            _client.StateChanged -= OnStateChanged;
            _client.LoginOk -= OnLoginOk;
            _client.Error -= OnError;
            _client.ReconnectFailed -= OnReconnectFailed;
        }

        void Build()
        {
            UIFactory.Panel(transform, "LoginPanel");

            var title = UIFactory.Label(transform, "技能五子棋", 72, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)title.transform, 0, 620, 800, 120);

            var nickLabel = UIFactory.Label(transform, "昵称", 32);
            UIFactory.Place((RectTransform)nickLabel.transform, 0, 430, 760, 50);

            _nickInput = UIFactory.Input(transform, "输入昵称（最多12字）", 36);
            UIFactory.Place((RectTransform)_nickInput.transform, 0, 350, 760, 90);
            _nickInput.characterLimit = 12;
            _nickInput.text = PlayerProfile.Nickname;

            var urlLabel = UIFactory.Label(transform, "服务器地址", 32);
            UIFactory.Place((RectTransform)urlLabel.transform, 0, 230, 760, 50);

            _urlInput = UIFactory.Input(transform, "ws://host:8080/ws", 28);
            UIFactory.Place((RectTransform)_urlInput.transform, 0, 150, 760, 90);
            var savedUrl = PlayerProfile.ServerUrl;
            _urlInput.text = string.IsNullOrEmpty(savedUrl) ? ServerConfig.DefaultUrl : savedUrl;

            _loginButton = UIFactory.Button(transform, "登录", 44);
            UIFactory.Place((RectTransform)_loginButton.transform, 0, 0, 760, 110);
            _loginButton.onClick.AddListener(OnLoginClick);

            _status = UIFactory.Label(transform, "", 30, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)_status.transform, 0, -140, 760, 60);
            _status.color = new Color(1f, 0.82f, 0.35f, 1f);
        }

        void OnLoginClick()
        {
            var nick = string.IsNullOrWhiteSpace(_nickInput.text) ? "" : _nickInput.text.Trim();
            var url = _urlInput.text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                _status.text = "请填写服务器地址";
                return;
            }

            PlayerProfile.Nickname = nick;
            PlayerProfile.ServerUrl = url;
            PlayerProfile.Save();

            _loginButton.interactable = false;
            _status.text = "连接中…";
            _client.Connect(url, nick, PlayerProfile.DeviceId);
        }

        void OnStateChanged(ConnState state)
        {
            switch (state)
            {
                case ConnState.Connected:
                    _status.text = "登录中…";
                    break;
                case ConnState.Reconnecting:
                    _status.text = "连接断开，重连中…";
                    break;
                case ConnState.Disconnected:
                    _status.text = "连接失败，请检查地址";
                    _loginButton.interactable = true;
                    break;
            }
        }

        void OnLoginOk(LoginOkMsg msg)
        {
            _status.text = "";
            AppController.Instance.ShowLobby();
        }

        void OnError(ErrorMsg msg)
        {
            if (msg.Code == "bad_request")
            {
                _status.text = msg.Message;
                _loginButton.interactable = true;
            }
        }

        void OnReconnectFailed(string reason)
        {
            _status.text = reason;
            _loginButton.interactable = true;
        }
    }
}
