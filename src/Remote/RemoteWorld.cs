using System.Collections;
using System.Collections.Generic;
using NormalGolfMultiplayer.Game;
using NormalGolfMultiplayer.Net;
using NormalGolfMultiplayer.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace NormalGolfMultiplayer.Remote
{
    /// <summary>Owns every remote player: feeds them network data and keeps their avatars/balls on the course.</summary>
    internal class RemoteWorld : MonoBehaviour
    {
        public static RemoteWorld Instance { get; private set; }

        public readonly Dictionary<byte, RemotePlayer> Players = new Dictionary<byte, RemotePlayer>();

        private Transform _root;

        public static float Now => (float)Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            var s = NetSession.Instance;
            s.PlayerJoined += OnPlayerJoined;
            s.PlayerLeft += OnPlayerLeft;
            s.PlayerInfoChanged += OnPlayerInfoChanged;
            s.StateReceived += OnState;
            s.ShotReceived += OnShot;
            s.HoledReceived += OnHoled;
            s.ScoreReceived += OnScore;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void OnPlayerJoined(PlayerInfo info)
        {
            Players[info.Id] = new RemotePlayer { Info = info };
            MultiplayerUI.Toast($"{info.Name} joined", info.Color);
        }

        private void OnPlayerLeft(PlayerInfo info)
        {
            if (Players.TryGetValue(info.Id, out var p))
            {
                if (p.View != null)
                    Destroy(p.View.gameObject);
                Players.Remove(info.Id);
            }
            MultiplayerUI.Toast($"{info.Name} left", info.Color);
        }

        private void OnPlayerInfoChanged(PlayerInfo info)
        {
            if (Players.TryGetValue(info.Id, out var p))
                p.Info = info; // the view picks the change up next frame
        }

        private void OnScore(byte id, ScoreCard card)
        {
            if (Players.TryGetValue(id, out var p))
                p.Score = card;
        }

        private void OnState(byte id, PlayerState state)
        {
            if (Players.TryGetValue(id, out var p))
                p.Buffer.Add(state, Now);
        }

        private void OnShot(byte id, ShotEvent shot)
        {
            if (!Players.TryGetValue(id, out var p))
                return;
            // The ball's launch reaches us through the interpolation buffer, so delay the swing by the same amount.
            float delay = ModConfig.InterpolationDelay.Value;
            p.SwingStart = Now + delay;
            p.SwingClub = (Clubs)shot.Club;
            StartCoroutine(PlayLater(delay + 0.87f, () =>
            {
                if (p.View != null && p.HasPose)
                    Visuals.PlayAt(Visuals.HitClipFor((Clubs)shot.Club, (ShotType)shot.ShotType), p.Position + Vector3.up, 0.9f);
            }));
        }

        private void OnHoled(byte id, HoledEvent holed)
        {
            if (!Players.TryGetValue(id, out var p))
                return;
            MultiplayerUI.Toast($"{p.Info.Name} {holed.Describe()}", p.Info.Color);
            StartCoroutine(PlayLater(ModConfig.InterpolationDelay.Value, () =>
            {
                if (p.View != null && p.HasPose)
                    Visuals.PlayAt("ballInHole", p.BallPosition, 1f);
            }));
        }

        private static IEnumerator PlayLater(float seconds, System.Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            action();
        }

        private void LateUpdate()
        {
            // Views are ordinary scene objects: leaving the course destroys them, and they're rebuilt on return.
            if (Players.Count == 0 || !LocalPlayer.InWorld)
                return;

            if (_root == null)
            {
                Visuals.Prepare();
                _root = new GameObject("NGMP Remote Players").transform;
            }

            float now = Now;
            float delay = ModConfig.InterpolationDelay.Value;
            float dt = Time.unscaledDeltaTime; // remote players keep moving while our game is paused
            foreach (var p in Players.Values)
            {
                if (p.View == null)
                    p.View = RemotePlayerView.Create(p, _root);
                p.View.Tick(now, delay, dt);
            }
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (_root == null)
                return;
            foreach (var p in Players.Values)
            {
                if (p.View != null)
                    p.View.FaceCamera(cam);
            }
        }

        public static string Describe(RemotePlayer p)
        {
            if (!p.HasPose)
                return "In menus";
            if ((p.Flags & StateFlags.Golfing) != 0)
                return (p.Flags & StateFlags.BallMoving) != 0 ? "Ball in flight" : "Lining up a shot";
            return (p.Flags & StateFlags.PlayNine) != 0 ? "Walking (Play Nine)" : "Walking";
        }
    }
}
