using System.Collections;
using System.Collections.Generic;
using System.Text;
using Gomoku.App;
using Gomoku.Core;
using Gomoku.Net;
using Gomoku.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gomoku.Lobby
{
    /// <summary>
    /// Post-login hall: loadout picker (30 choose 4), quick match, create /
    /// join a friend room by 5-char code. A successful room_create or
    /// room_join is answered with room_state, which opens the room panel.
    /// </summary>
    public sealed class LobbyPanel : MonoBehaviour
    {
        static readonly int[] DefaultLoadout = { 3, 4, 5, 18 };

        GameClient _client;
        Text _status;
        Text _loadoutLabel;
        Button _skillsButton;
        Button _matchButton;
        Button _cancelButton;
        Button _createButton;
        Button _joinButton;
        InputField _codeInput;
        Text _toast;
        Image _toastBg;

        bool _matching;

        public void Init(GameClient client)
        {
            _client = client;
            Build();
            RefreshLoadout();

            // Friend-room room_state / game_start are routed globally by
            // AppController (also needed for Attach replays after reconnect).
            _client.StateChanged += OnStateChanged;
            _client.Error += OnError;
            _client.MatchWaiting += OnMatchWaiting;
            _client.MatchFound += OnMatchFound;
        }

        void OnDestroy()
        {
            if (_client == null) return;
            _client.StateChanged -= OnStateChanged;
            _client.Error -= OnError;
            _client.MatchWaiting -= OnMatchWaiting;
            _client.MatchFound -= OnMatchFound;
        }

        void Build()
        {
            UIFactory.Panel(transform, "LobbyPanel");

            var title = UIFactory.Label(transform, "大 厅", 60, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)title.transform, 0, 800, 800, 100);

            _status = UIFactory.Label(transform, "", 32, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)_status.transform, 0, 710, 900, 56);
            _status.color = new Color(0.55f, 0.85f, 1f, 1f);

            // Loadout summary card.
            var cardGo = new GameObject("LoadoutCard", typeof(RectTransform), typeof(Image));
            cardGo.transform.SetParent(transform, false);
            cardGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.32f);
            UIFactory.Place((RectTransform)cardGo.transform, 0, 480, 940, 280);

            var cap = UIFactory.Label(cardGo.transform, "本局携带技能（4个）", 30, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)cap.transform, 0, 96, 880, 50);

            _loadoutLabel = UIFactory.Label(cardGo.transform, "", 30, TextAnchor.UpperCenter);
            UIFactory.Place((RectTransform)_loadoutLabel.transform, 0, -10, 880, 130);
            _loadoutLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _loadoutLabel.color = new Color(1f, 0.92f, 0.55f, 1f);

            _skillsButton = UIFactory.Button(transform, "选择 / 更换技能", 38);
            UIFactory.Place((RectTransform)_skillsButton.transform, 0, 262, 760, 96);
            _skillsButton.onClick.AddListener(OnSkillsClick);

            _matchButton = UIFactory.Button(transform, "快速匹配", 44);
            UIFactory.Place((RectTransform)_matchButton.transform, 0, 120, 760, 110);
            _matchButton.onClick.AddListener(OnMatchClick);

            _cancelButton = UIFactory.Button(transform, "匹配中… 点击取消", 40);
            UIFactory.Place((RectTransform)_cancelButton.transform, 0, 120, 760, 110);
            _cancelButton.onClick.AddListener(OnCancelClick);
            _cancelButton.gameObject.SetActive(false);

            var divider = UIFactory.Label(transform, "— 好友对战 —", 30, TextAnchor.MiddleCenter);
            UIFactory.Place((RectTransform)divider.transform, 0, -20, 760, 50);
            divider.color = new Color(1f, 1f, 1f, 0.6f);

            _createButton = UIFactory.Button(transform, "创建好友房", 40);
            UIFactory.Place((RectTransform)_createButton.transform, 0, -120, 760, 100);
            _createButton.onClick.AddListener(OnCreateClick);

            _codeInput = UIFactory.Input(transform, "输入5位房间码", 40);
            UIFactory.Place((RectTransform)_codeInput.transform, -150, -250, 540, 100);
            _codeInput.characterLimit = 5;
            _codeInput.contentType = InputField.ContentType.Standard;
            _codeInput.onValueChanged.AddListener(t =>
            {
                var upper = (t ?? "").ToUpperInvariant();
                if (upper != t) _codeInput.text = upper;
            });

            _joinButton = UIFactory.Button(transform, "加入", 38);
            UIFactory.Place((RectTransform)_joinButton.transform, 330, -250, 200, 100);
            _joinButton.onClick.AddListener(OnJoinClick);

            _toast = UIFactory.Toast(transform, out _toastBg, 32);
            var toastRt = (RectTransform)_toast.transform.parent;
            toastRt.SetAsLastSibling();
            UIFactory.Place(toastRt, 0, -400, 880, 80);
            _toast.color = new Color(1f, 0.85f, 0.3f, 1f);
            toastRt.gameObject.SetActive(false);
        }

        int[] CurrentLoadout()
        {
            var saved = PlayerProfile.LastSkillIds;
            if (saved != null && saved.Length == 4) return saved;
            return DefaultLoadout;
        }

        void RefreshLoadout()
        {
            var sb = new StringBuilder();
            foreach (var id in CurrentLoadout())
            {
                var spec = SkillCatalog.ById(id);
                if (sb.Length > 0) sb.Append("    ");
                sb.Append(spec != null ? spec.Name : $"#{id}");
            }
            _loadoutLabel.text = sb.ToString();
        }

        void OnSkillsClick()
        {
            var go = new GameObject("SkillSelect", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            UIFactory.Stretch((RectTransform)go.transform);
            var panel = go.AddComponent<SkillSelectPanel>();
            panel.Init(CurrentLoadout(), _ => RefreshLoadout());
        }

        void OnMatchClick()
        {
            _matching = true;
            SetBusyUi(true);
            _client.MatchQueue(new List<int>(CurrentLoadout()));
        }

        void OnCancelClick()
        {
            _matching = false;
            SetBusyUi(false);
            _client.MatchCancel(); // server never acks; UI reverts immediately
        }

        void OnCreateClick()
        {
            SetBusyUi(true);
            _client.RoomCreate(new List<int>(CurrentLoadout()));
        }

        void OnJoinClick()
        {
            var code = (_codeInput.text ?? "").Trim().ToUpperInvariant();
            if (code.Length != 5)
            {
                ShowToast("请输入5位房间码");
                return;
            }
            SetBusyUi(true);
            _client.RoomJoin(code, new List<int>(CurrentLoadout()));
        }

        void SetBusyUi(bool busy)
        {
            _matchButton.gameObject.SetActive(!busy);
            _cancelButton.gameObject.SetActive(busy && _matching);
            _createButton.interactable = !busy;
            _joinButton.interactable = !busy;
            _skillsButton.interactable = !busy;
            _codeInput.interactable = !busy;
        }

        void ResetBusy()
        {
            _matching = false;
            SetBusyUi(false);
        }

        void OnStateChanged(ConnState state)
        {
            switch (state)
            {
                case ConnState.Connected:
                    _status.text = $"已连接 · {PlayerProfile.Nickname}";
                    break;
                case ConnState.Reconnecting:
                    _status.text = "断线重连中…";
                    break;
                case ConnState.Disconnected:
                    _status.text = "连接已断开";
                    ResetBusy();
                    break;
            }
        }

        void OnError(ErrorMsg msg)
        {
            // already_busy arrives as a late reply to a stale request; the
            // player is already in a room/game handled elsewhere, so keep UI.
            if (msg.Code != "already_busy") ResetBusy();
            ShowToast(ErrorText(msg.Code));
        }

        static string ErrorText(string code)
        {
            switch (code)
            {
                case "bad_loadout": return "技能配置无效（需恰好4个）";
                case "room_not_found": return "房间不存在";
                case "room_full": return "房间已满";
                case "already_busy": return "你已在房间或匹配队列中";
                default: return "操作失败，请重试";
            }
        }

        void OnMatchWaiting(MatchWaitingMsg msg)
        {
            _matching = true;
            SetBusyUi(true);
            ShowToast("正在匹配对手…");
        }

        void OnMatchFound(MatchFoundMsg msg)
        {
            ResetBusy();
            AppController.Instance.ShowBattle(msg);
        }

        void ShowToast(string text)
        {
            _toast.text = text;
            _toast.transform.parent.gameObject.SetActive(true);
            _toastBg.color = new Color(0.1f, 0.08f, 0.02f, 0.85f);
            StopAllCoroutines();
            StartCoroutine(HideToast());
        }

        IEnumerator HideToast()
        {
            yield return new WaitForSeconds(1.6f);
            _toast.transform.parent.gameObject.SetActive(false);
        }
    }
}
