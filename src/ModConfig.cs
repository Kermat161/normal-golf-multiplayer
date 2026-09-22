using System.IO;
using BepInEx;
using BepInEx.Configuration;
using NormalGolfMultiplayer.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NormalGolfMultiplayer
{
    internal static class ModConfig
    {
        public static readonly Color[] Palette =
        {
            new Color32(0xE8, 0x4A, 0x3C, 0xFF), // red
            new Color32(0xF2, 0x9A, 0x2E, 0xFF), // orange
            new Color32(0xF4, 0xD0, 0x3F, 0xFF), // yellow
            new Color32(0x4C, 0xB9, 0x4F, 0xFF), // green
            new Color32(0x2E, 0xB8, 0xB0, 0xFF), // teal
            new Color32(0x3A, 0x8D, 0xE8, 0xFF), // blue
            new Color32(0x7B, 0x5C, 0xE0, 0xFF), // purple
            new Color32(0xE4, 0x5B, 0xB5, 0xFF), // pink
            new Color32(0xF5, 0xF5, 0xF0, 0xFF), // white
            new Color32(0x33, 0x33, 0x3A, 0xFF), // charcoal
        };

        public static ConfigFile File;
        public static ConfigEntry<string> PlayerName;
        public static ConfigEntry<string> PlayerColor;
        public static ConfigEntry<int> HostPort;
        public static ConfigEntry<string> JoinAddress;
        public static ConfigEntry<int> JoinPort;
        public static ConfigEntry<string> Password;
        public static ConfigEntry<int> MaxPlayers;
        public static ConfigEntry<Key> MenuKey;
        public static ConfigEntry<Key> ChatKey;
        public static ConfigEntry<Key> ScoreboardKey;
        public static ConfigEntry<bool> ShowNameTags;
        public static ConfigEntry<bool> ShowBallLabels;
        public static ConfigEntry<bool> RemoteSounds;
        public static ConfigEntry<float> InterpolationDelay;

        public static void Init(ConfigFile pluginConfig)
        {
            // Test profiles (-ngmp-profile) get their own file so two local copies don't share a name/colour.
            File = string.IsNullOrEmpty(DevTools.Profile)
                ? pluginConfig
                : new ConfigFile(Path.Combine(Paths.ConfigPath, $"{Plugin.Guid}.{DevTools.Profile}.cfg"), true);

            PlayerName = File.Bind("Player", "Name", "", "Name shown to other players. Empty = your Steam name.");
            PlayerColor = File.Bind("Player", "Color", "", "Your avatar/trail colour as #RRGGBB. Empty = pick one at random.");
            HostPort = File.Bind("Network", "HostPort", Protocol.DefaultPort, "UDP port to listen on when hosting.");
            JoinAddress = File.Bind("Network", "JoinAddress", "127.0.0.1", "Last address you joined.");
            JoinPort = File.Bind("Network", "JoinPort", Protocol.DefaultPort, "Last port you joined.");
            Password = File.Bind("Network", "Password", "", "Optional password. When hosting, joiners must match it; when joining, it is sent to the host.");
            MaxPlayers = File.Bind("Network", "MaxPlayers", 8, new ConfigDescription("Maximum players in a hosted session (including you).", new AcceptableValueRange<int>(2, 32)));
            MenuKey = File.Bind("Controls", "MenuKey", Key.F8, "Opens the multiplayer menu.");
            ChatKey = File.Bind("Controls", "ChatKey", Key.T, "Opens the chat box while in a session.");
            ScoreboardKey = File.Bind("Controls", "ScoreboardKey", Key.F9, "Shows/hides the Front Nine scoreboard overlay.");
            ShowNameTags = File.Bind("Visuals", "ShowNameTags", true, "Show names above other players.");
            ShowBallLabels = File.Bind("Visuals", "ShowBallLabels", true, "Show the owner's name above other players' balls.");
            RemoteSounds = File.Bind("Audio", "RemoteSounds", true, "Play other players' club hits and hole-outs as positional sounds.");
            InterpolationDelay = File.Bind("Network", "InterpolationDelay", 0.1f, new ConfigDescription(
                "Seconds of buffering for smooth remote movement. Raise on bad connections.", new AcceptableValueRange<float>(0.03f, 0.5f)));

            if (!string.IsNullOrEmpty(DevTools.NameOverride))
                PlayerName.Value = DevTools.NameOverride;
            if (!ColorUtility.TryParseHtmlString(PlayerColor.Value, out _))
                PlayerColor.Value = "#" + ColorUtility.ToHtmlStringRGB(Palette[Random.Range(0, Palette.Length - 2)]);
        }

        public static Color GetColor()
        {
            return ColorUtility.TryParseHtmlString(PlayerColor.Value, out var c) ? c : Palette[5];
        }

        public static string ResolveName()
        {
            string n = PlayerName.Value?.Trim();
            if (!string.IsNullOrEmpty(n))
                return n;
            try
            {
                if (SteamManager.Initialized)
                    n = Steamworks.SteamFriends.GetPersonaName();
            }
            catch
            {
                // Steam unavailable; fall through to the generic name.
            }
            return string.IsNullOrEmpty(n) ? "Golfer" : n;
        }
    }
}
