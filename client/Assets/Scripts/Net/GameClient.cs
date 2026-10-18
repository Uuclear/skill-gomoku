using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gomoku.Net
{
    public enum ConnState { Disconnected, Connecting, Connected, Reconnecting }

    /// <summary>
    /// High-level network API: typed requests and server event callbacks.
    /// Everything received on background threads is funnelled through a queue
    /// and re-raised on the Unity main thread in <see cref="Pump"/> (call it
    /// from a MonoBehaviour.Update).
    ///
    /// After a connection has succeeded once, an unexpected socket drop
    /// triggers exponential-backoff reconnect and a login replay so the server
    /// can reattach the device to its room/seat. A failure of the very first
    /// connect does NOT auto-retry; the caller surfaces it to the user.
    /// </summary>
    public sealed class GameClient
    {
        const double PingIntervalSec = 25.0;
        const double ReconnectBaseSec = 1.0;
        const double ReconnectMaxSec = 15.0;
        const int ReconnectMaxAttempts = 10;

        readonly WebSocketClient _ws = new WebSocketClient();
        readonly ConcurrentQueue<Action> _mainQueue = new ConcurrentQueue<Action>();

        long _seq;
        string _url;
        LoginReq _session;
        bool _manualClose;
        bool _hasConnectedOnce;
        double _lastPingSec;
        int _reconnectAttempts;

        // ---- connection lifecycle (main thread) ----
        public ConnState State { get; private set; } = ConnState.Disconnected;
        public event Action<ConnState> StateChanged;
        public event Action<string> ReconnectFailed;

        // ---- server pushes (main thread) ----
        public event Action<LoginOkMsg> LoginOk;
        public event Action<ErrorMsg> Error;
        public event Action<MatchWaitingMsg> MatchWaiting;
        public event Action<MatchFoundMsg> MatchFound;
        public event Action<RoomStateMsg> RoomState;
        public event Action<GameStartMsg> GameStart;
        public event Action<GameEventMsg> GameEvent;
        public event Action<GameOverMsg> GameOver;
        public event Action<EmoteMsg> Emote;
        public event Action<RematchRequestedMsg> RematchRequested;
        public event Action<OpponentLeftMsg> OpponentLeft;
        public event Action<TurnExpireWarnMsg> TurnExpireWarn;
        public event Action<PrivateSkillMsg> SkillPrivate;

        public GameClient()
        {
            _ws.OnState += HandleSocketState;
            _ws.OnText += HandleSocketText;
        }

        // ---------- public API (call from main thread) ----------

        public void Connect(string url, string nickname, string deviceId)
        {
            _url = url;
            _session = new LoginReq { Nickname = nickname, DeviceId = deviceId };
            _manualClose = false;
            _hasConnectedOnce = false;
            _reconnectAttempts = 0;
            SetState(ConnState.Connecting);
            _ws.Connect(url);
        }

        /// <summary>User-initiated disconnect; the client may Connect again later.</summary>
        public void Disconnect()
        {
            _manualClose = true;
            _reconnectAttempts = 0;
            SetState(ConnState.Disconnected);
            _ws.Close();
        }

        /// <summary>Permanent teardown on application quit.</summary>
        public void Dispose()
        {
            _manualClose = true;
            _ws.Dispose();
        }

        public void MatchQueue(IList<int> skillIds) => Send("match_queue", new SkillIdsReq { SkillIds = ToList(skillIds) });
        public void MatchCancel() => Send("match_cancel", null);
        public void RoomCreate(IList<int> skillIds) => Send("room_create", new SkillIdsReq { SkillIds = ToList(skillIds) });
        public void RoomJoin(string roomCode, IList<int> skillIds) =>
            Send("room_join", new RoomJoinReq { RoomCode = roomCode, SkillIds = ToList(skillIds) });
        public void Ready() => Send("ready", null);
        public void Start() => Send("start", null);
        public void Move(int x, int y) => Send("move", new MoveReq { X = x, Y = y });
        public void UseSkill(int skillId, List<Point> targets = null, int dir = 0) =>
            Send("use_skill", new UseSkillReq { SkillId = skillId, Targets = targets, Dir = dir });
        public void Surrender() => Send("surrender", null);
        public void SendEmote(int id) => Send("emote", new EmoteReq { Id = id });
        public void Rematch(bool accept) => Send("rematch", new RematchReq { Accept = accept });

        /// <summary>Drain queued messages/events onto the main thread. Tick every frame.</summary>
        public void Pump()
        {
            _ws.Dispatch();

            while (_mainQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogError($"[Net] handler error: {e}"); }
            }

            if (State != ConnState.Connected || _manualClose) return;
            double now = Time.realtimeSinceStartup;
            if (now - _lastPingSec >= PingIntervalSec)
            {
                _lastPingSec = now;
                Send("ping", null);
            }
        }

        // ---------- internals ----------

        void HandleSocketState(WsState wsState)
        {
            _mainQueue.Enqueue(() =>
            {
                switch (wsState)
                {
                    case WsState.Connected:
                        _reconnectAttempts = 0;
                        _hasConnectedOnce = true;
                        _lastPingSec = Time.realtimeSinceStartup;
                        SetState(ConnState.Connected);
                        // Replay login on every fresh socket (initial connect and reconnect);
                        // server replies login_ok and replays room/game state if attached.
                        Send("login", _session);
                        break;
                    case WsState.Disconnected:
                        if (!_manualClose && _hasConnectedOnce)
                            BeginReconnect();
                        else if (!_manualClose)
                            SetState(ConnState.Disconnected);
                        break;
                }
            });
        }

        void BeginReconnect()
        {
            if (State == ConnState.Reconnecting) return;
            if (_reconnectAttempts >= ReconnectMaxAttempts)
            {
                _manualClose = true;
                SetState(ConnState.Disconnected);
                ReconnectFailed?.Invoke("重连失败，请检查网络后重试");
                return;
            }

            _reconnectAttempts++;
            SetState(ConnState.Reconnecting);

            double delay = Math.Min(
                ReconnectMaxSec,
                ReconnectBaseSec * Math.Pow(2, _reconnectAttempts - 1));
            ScheduleReconnect((float)delay);
        }

        async void ScheduleReconnect(float delaySec)
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySec));
            _mainQueue.Enqueue(() =>
            {
                if (State != ConnState.Reconnecting || _manualClose) return;
                _ws.Connect(_url);
            });
        }

        void HandleSocketText(string json)
        {
            Envelope env;
            try { env = JsonConvert.DeserializeObject<Envelope>(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Net] bad frame: {e.Message}");
                return;
            }
            if (env == null || string.IsNullOrEmpty(env.Type)) return;
            _mainQueue.Enqueue(() => Dispatch(env));
        }

        void Dispatch(Envelope env)
        {
            switch (env.Type)
            {
                case "pong": break;
                case "login_ok": LoginOk?.Invoke(Parse<LoginOkMsg>(env)); break;
                case "error": Error?.Invoke(Parse<ErrorMsg>(env)); break;
                case "match_waiting": MatchWaiting?.Invoke(Parse<MatchWaitingMsg>(env)); break;
                case "match_found": MatchFound?.Invoke(Parse<MatchFoundMsg>(env)); break;
                case "room_state": RoomState?.Invoke(Parse<RoomStateMsg>(env)); break;
                case "game_start": GameStart?.Invoke(Parse<GameStartMsg>(env)); break;
                case "game_event": GameEvent?.Invoke(Parse<GameEventMsg>(env)); break;
                case "game_over": GameOver?.Invoke(Parse<GameOverMsg>(env)); break;
                case "emote": Emote?.Invoke(Parse<EmoteMsg>(env)); break;
                case "rematch_requested": RematchRequested?.Invoke(Parse<RematchRequestedMsg>(env)); break;
                case "opponent_left": OpponentLeft?.Invoke(Parse<OpponentLeftMsg>(env)); break;
                case "turn_expire_warn": TurnExpireWarn?.Invoke(Parse<TurnExpireWarnMsg>(env)); break;
                case "skill_private": SkillPrivate?.Invoke(Parse<PrivateSkillMsg>(env)); break;
                default: Debug.Log($"[Net] unhandled type: {env.Type}"); break;
            }
        }

        static T Parse<T>(Envelope env) where T : class
        {
            return env.Data == null ? null : env.Data.ToObject<T>();
        }

        long NextSeq() => ++_seq;

        void Send(string type, object payload)
        {
            var env = new Envelope { Type = type, Seq = NextSeq() };
            if (payload != null)
                env.Data = JObject.FromObject(payload);
            _ws.SendText(JsonConvert.SerializeObject(env));
        }

        static List<int> ToList(IList<int> ids)
        {
            var list = new List<int>(ids == null ? 0 : ids.Count);
            if (ids != null) list.AddRange(ids);
            return list;
        }

        void SetState(ConnState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
