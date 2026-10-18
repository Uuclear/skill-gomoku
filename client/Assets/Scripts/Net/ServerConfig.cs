using UnityEngine;

namespace Gomoku.Net
{
    /// <summary>
    /// Default WebSocket URLs per environment. Android emulators reach the host
    /// machine through 10.0.2.2; physical devices need the LAN IP of the host.
    /// Override at runtime on the login panel; the chosen URL is persisted.
    /// </summary>
    public static class ServerConfig
    {
        public const string EditorUrl = "ws://127.0.0.1:8080/ws";
        public const string EmulatorUrl = "ws://10.0.2.2:8080/ws";
        // Set to the development machine's LAN IP before building a real APK.
        public const string DeviceUrl = "ws://192.168.1.100:8080/ws";

        public static string DefaultUrl
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return EmulatorUrl;
#else
                return EditorUrl;
#endif
            }
        }
    }
}
