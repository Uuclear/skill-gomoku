using System;
using UnityEngine;

namespace Gomoku.App
{
    /// <summary>
    /// Local player identity and preferences, persisted in PlayerPrefs.
    /// </summary>
    public static class PlayerProfile
    {
        const string KeyNickname = "gp.nickname";
        const string KeyDeviceId = "gp.deviceId";
        const string KeyServerUrl = "gp.serverUrl";
        const string KeyLastSkills = "gp.lastSkills"; // comma separated ids

        public static string Nickname
        {
            get => PlayerPrefs.GetString(KeyNickname, "");
            set => PlayerPrefs.SetString(KeyNickname, value ?? "");
        }

        public static string DeviceId
        {
            get
            {
                var id = PlayerPrefs.GetString(KeyDeviceId, "");
                if (string.IsNullOrEmpty(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(KeyDeviceId, id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }

        public static string ServerUrl
        {
            get => PlayerPrefs.GetString(KeyServerUrl, "");
            set => PlayerPrefs.SetString(KeyServerUrl, value ?? "");
        }

        /// <summary>Last chosen loadout (up to 4 skill ids); empty until skill select ships.</summary>
        public static int[] LastSkillIds
        {
            get
            {
                var raw = PlayerPrefs.GetString(KeyLastSkills, "");
                if (string.IsNullOrEmpty(raw)) return Array.Empty<int>();
                var parts = raw.Split(',');
                var ids = new System.Collections.Generic.List<int>(4);
                foreach (var p in parts)
                    if (int.TryParse(p, out var id)) ids.Add(id);
                return ids.ToArray();
            }
            set
            {
                var raw = value == null ? "" : string.Join(",", value);
                PlayerPrefs.SetString(KeyLastSkills, raw);
            }
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
