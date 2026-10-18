using System;
using System.Text;
using NativeWebSocket;

namespace Gomoku.Net
{
    public enum WsState { Disconnected, Connecting, Connected }

    /// <summary>
    /// Thin wrapper around NativeWebSocket: connect lifecycle, send text frames
    /// and surface raw text messages plus status changes. Reconnection policy
    /// lives in <see cref="GameClient"/>; this layer never retries by itself.
    ///
    /// IMPORTANT: NativeWebSocket invokes its callbacks on background threads.
    /// Consumers must marshal to the Unity main thread (see GameClient.Pump).
    /// DispatchMessageQueue() must be ticked every frame from the main thread.
    /// </summary>
    public sealed class WebSocketClient : IDisposable
    {
        WebSocket _ws;
        bool _disposed;

        public WsState State { get; private set; } = WsState.Disconnected;

        /// <summary>Raw JSON text frame received (background thread).</summary>
        public event Action<string> OnText;
        /// <summary>State transitioned (background thread).</summary>
        public event Action<WsState> OnState;

        public async void Connect(string url)
        {
            if (_disposed) return;
            CloseInternal();

            var ws = new WebSocket(url);
            ws.OnOpen += () =>
            {
                if (_ws != ws) return;
                State = WsState.Connected;
                OnState?.Invoke(State);
            };
            ws.OnMessage += bytes =>
            {
                if (_ws != ws) return;
                OnText?.Invoke(Encoding.UTF8.GetString(bytes));
            };
            ws.OnError += err =>
            {
                if (_ws != ws) return;
                UnityEngine.Debug.LogWarning($"[WS] error: {err}");
            };
            ws.OnClose += code =>
            {
                if (_ws != ws) return;
                UnityEngine.Debug.Log($"[WS] closed ({code})");
                State = WsState.Disconnected;
                OnState?.Invoke(State);
            };

            _ws = ws;
            State = WsState.Connecting;
            OnState?.Invoke(State);
            try
            {
                await ws.Connect();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[WS] connect failed: {e.Message}");
                if (_ws == ws)
                {
                    State = WsState.Disconnected;
                    OnState?.Invoke(State);
                }
            }
        }

        public async void SendText(string text)
        {
            var ws = _ws;
            if (ws != null && ws.State == WebSocketState.Open)
                await ws.SendText(text);
        }

        /// <summary>Call from MonoBehaviour.Update.</summary>
        public void Dispatch()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _ws?.DispatchMessageQueue();
#endif
        }

        /// <summary>Graceful close; Connect() may be called again afterwards.</summary>
        public void Close() => CloseInternal();

        public void Dispose()
        {
            _disposed = true;
            CloseInternal();
        }

        async void CloseInternal()
        {
            var ws = _ws;
            _ws = null;
            State = WsState.Disconnected;
            if (ws != null && ws.State == WebSocketState.Open)
            {
                try { await ws.Close(); } catch { /* best effort */ }
            }
        }
    }
}
