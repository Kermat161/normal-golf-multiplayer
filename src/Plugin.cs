using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using NormalGolfMultiplayer.Game;
using NormalGolfMultiplayer.Net;
using NormalGolfMultiplayer.Remote;
using NormalGolfMultiplayer.UI;
using UnityEngine;

namespace NormalGolfMultiplayer
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "normalgolf.multiplayer";
        public const string Name = "Normal Golf Multiplayer";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            DevTools.ParseCommandLine();
            ModConfig.Init(Config);

            _harmony = new Harmony(Guid);
            SaveSandbox.Apply(_harmony);
            GamePatches.Apply(_harmony);

            // BepInEx's plugin object can be destroyed by some games on scene load,
            // so all runtime behaviour lives on our own persistent GameObject.
            var host = new GameObject("NormalGolfMultiplayer");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<NetSession>();
            host.AddComponent<ScoreTracker>();
            host.AddComponent<RemoteWorld>();
            host.AddComponent<MultiplayerUI>();
            host.AddComponent<DevTools>();

            Log.LogInfo($"{Name} {Version} loaded (Unity {Application.unityVersion}, protocol v{Protocol.Version})");
        }
    }
}
