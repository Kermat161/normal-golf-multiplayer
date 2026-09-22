using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using NormalGolfMultiplayer.Game;
using UnityEngine;

namespace NormalGolfMultiplayer.Net
{
    internal enum SessionMode
    {
        Offline,
        Hosting,
        Connecting,
        Connected,
    }

    /// <summary>
    /// Listen-server session over LiteNetLib (UDP). The host is player 1 and relays every
    /// client's messages to the other clients, so clients only ever talk to the host.
    /// </summary>
    internal class NetSession : MonoBehaviour
    {
        public static NetSession Instance { get; private set; }

        public SessionMode Mode { get; private set; }
        public byte LocalId { get; private set; }
        public string Status { get; private set; } = "Not connected";
        public string Endpoint { get; private set; } = "";
        public PlayerInfo LocalInfo { get; private set; } = new PlayerInfo();

        /// <summary>Everyone in the session, including us.</summary>
        public readonly Dictionary<byte, PlayerInfo> Players = new Dictionary<byte, PlayerInfo>();

        public event Action<PlayerInfo> PlayerJoined;
        public event Action<PlayerInfo> PlayerLeft;
        public event Action<PlayerInfo> PlayerInfoChanged;
        public event Action<byte, PlayerState> StateReceived;
        public event Action<byte, ShotEvent> ShotReceived;
        public event Action<byte, HoledEvent> HoledReceived;
        public event Action<byte, string> ChatReceived;
        public event Action<byte, ScoreCard> ScoreReceived;
        /// <summary>Raised with a human-readable reason when a session ends or a join fails.</summary>
        public event Action<string> SessionEnded;
        public event Action SessionStarted;

        public bool IsHost => Mode == SessionMode.Hosting;
        public bool InSession => Mode == SessionMode.Hosting || (Mode == SessionMode.Connected && LocalId != 0);
        public int PingMs => _server != null ? _server.RoundTripTime : 0;
        public float SessionTime => (float)(Time.realtimeSinceStartupAsDouble - _timeBase);

        private readonly NetDataWriter _w = new NetDataWriter();
        private readonly Dictionary<int, byte> _peerToPlayer = new Dictionary<int, byte>();
        private readonly Dictionary<byte, NetPeer> _playerToPeer = new Dictionary<byte, NetPeer>();
        private readonly Dictionary<byte, ChatLimiter> _chatLimits = new Dictionary<byte, ChatLimiter>();
        private EventBasedNetListener _listener;
        private NetManager _net;
        private NetPeer _server;
        private double _timeBase;
        private float _nextSend;
        private float _nextInfoCheck;
        private ushort _seq;
        private bool _prevRunInBackground;
        private bool _sessionBegan;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            Shutdown("Game closing");
        }

        private void OnApplicationQuit()
        {
            Shutdown("Game closing");
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Starts hosting. Returns null on success or an error message.</summary>
        public string Host(int port)
        {
            if (Mode != SessionMode.Offline)
                return "Already in a session";
            if (port < 1 || port > 65535)
                return "Port must be 1-65535";

            CreateManager();
            bool started = DevTools.LoopbackOnly
                ? _net.Start(System.Net.IPAddress.Loopback, System.Net.IPAddress.IPv6Loopback, port)
                : _net.Start(port);
            if (!started)
            {
                _net = null;
                return $"Could not listen on UDP port {port} (already in use?)";
            }

            Mode = SessionMode.Hosting;
            LocalId = Protocol.HostId;
            RefreshLocalInfo(includeName: true);
            Players.Clear();
            Players[LocalId] = LocalInfo;
            Endpoint = $"port {port}";
            Status = $"Hosting on UDP port {port}";
            OnSessionBegan();
            Plugin.Log.LogInfo(Status);
            return null;
        }

        /// <summary>Starts connecting to a host. Returns null if the attempt started, or an error message.</summary>
        public string Join(string address, int port)
        {
            if (Mode != SessionMode.Offline)
                return "Already in a session";
            address = address?.Trim();
            if (string.IsNullOrEmpty(address))
                return "Enter the host's IP address";
            if (port < 1 || port > 65535)
                return "Port must be 1-65535";

            CreateManager();
            if (!_net.Start())
            {
                _net = null;
                return "Could not open a UDP socket";
            }

            RefreshLocalInfo(includeName: true);
            var hello = new NetDataWriter();
            hello.Put(Protocol.Magic);
            hello.Put(Protocol.Version);
            hello.Put(ModConfig.Password.Value ?? "", 64);
            LocalInfo.Write(hello);

            _server = _net.Connect(address, port, hello);
            if (_server == null)
            {
                Shutdown(null);
                return $"Could not resolve '{address}'";
            }

            Mode = SessionMode.Connecting;
            LocalId = 0;
            Players.Clear();
            Endpoint = $"{address}:{port}";
            Status = $"Connecting to {Endpoint}...";
            _timeBase = Time.realtimeSinceStartupAsDouble;
            Plugin.Log.LogInfo(Status);
            return null;
        }

        public void Leave()
        {
            Shutdown("You left the session");
        }

        private void CreateManager()
        {
            _listener = new EventBasedNetListener();
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnReceive;
            _listener.NetworkErrorEvent += (ep, err) => Plugin.Log.LogWarning($"Network error from {ep}: {err}");

            _net = new NetManager(_listener)
            {
                AutoRecycle = true,
                IPv6Enabled = true,
                DisconnectTimeout = 15000, // tolerate long scene loads
                PingInterval = 1000,
                MaxConnectAttempts = 12,
                ReconnectDelay = 500,
            };
        }

        private void OnSessionBegan()
        {
            _timeBase = Time.realtimeSinceStartupAsDouble;
            // Keep sending/receiving when alt-tabbed, otherwise we'd freeze for everyone else.
            _prevRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            _sessionBegan = true;
            SessionStarted?.Invoke();
        }

        private void Shutdown(string reason)
        {
            bool wasActive = Mode != SessionMode.Offline;

            if (_net != null)
            {
                try
                {
                    _net.Stop(true);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Error stopping network: " + e.Message);
                }
            }
            _net = null;
            _server = null;
            _peerToPlayer.Clear();
            _playerToPeer.Clear();
            _chatLimits.Clear();

            foreach (var p in Players.Values.Where(p => p.Id != LocalId).ToList())
                PlayerLeft?.Invoke(p);
            Players.Clear();

            if (_sessionBegan)
                Application.runInBackground = _prevRunInBackground;
            _sessionBegan = false;

            Mode = SessionMode.Offline;
            LocalId = 0;
            if (reason != null)
                Status = reason;
            if (wasActive && reason != null)
            {
                Plugin.Log.LogInfo("Session ended: " + reason);
                SessionEnded?.Invoke(reason);
            }
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (_net == null)
                return;
            _net.PollEvents();
            if (_net == null || !InSession)
                return;

            float now = Time.unscaledTime;
            if (now >= _nextSend)
            {
                _nextSend = now + Protocol.SendInterval;
                SendLocalState();
            }
            if (now >= _nextInfoCheck)
            {
                _nextInfoCheck = now + 1f;
                CheckLocalInfoChanged();
            }
        }

        private void SendLocalState()
        {
            PlayerState s = LocalPlayer.Capture();
            s.Seq = ++_seq;
            s.Time = SessionTime;
            Begin(Msg.State, LocalId);
            s.Write(_w);
            SendFromLocal(DeliveryMethod.Unreliable);
        }

        private void CheckLocalInfoChanged()
        {
            var old = LocalInfo.Clone();
            RefreshLocalInfo(includeName: IsHost);
            if (old.Name == LocalInfo.Name && old.Color.Equals(LocalInfo.Color) && old.BallLook == LocalInfo.BallLook)
                return;
            Begin(Msg.PlayerInfo, LocalId);
            LocalInfo.Write(_w);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
        }

        /// <param name="includeName">
        /// False for joined clients mid-session: they keep the name the host assigned (which may carry a " (2)" suffix).
        /// </param>
        private void RefreshLocalInfo(bool includeName)
        {
            LocalInfo.Id = LocalId;
            if (includeName)
            {
                LocalInfo.Name = Protocol.Clean(ModConfig.ResolveName(), Protocol.MaxNameLength);
                if (LocalInfo.Name.Length == 0)
                    LocalInfo.Name = "Golfer";
            }
            LocalInfo.Color = ModConfig.GetColor();
            LocalInfo.BallLook = LocalPlayer.CurrentBallLook();
        }

        // ------------------------------------------------------------------ outgoing events

        public void SendShot(ShotEvent shot)
        {
            if (!InSession)
                return;
            Begin(Msg.Shot, LocalId);
            shot.Write(_w);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
        }

        public void SendHoled(HoledEvent holed)
        {
            if (!InSession)
                return;
            Begin(Msg.Holed, LocalId);
            holed.Write(_w);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
        }

        public void SendScore(ScoreCard card)
        {
            if (!InSession)
                return;
            Begin(Msg.Score, LocalId);
            card.Write(_w);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
        }

        public void SendChat(string text)
        {
            text = Protocol.Clean(text, Protocol.MaxChatLength);
            if (!InSession || text.Length == 0)
                return;
            Begin(Msg.Chat, LocalId);
            _w.Put(text, Protocol.MaxChatLength * 2);
            SendFromLocal(DeliveryMethod.ReliableOrdered);
            ChatReceived?.Invoke(LocalId, text);
        }

        private void Begin(Msg msg, byte playerId)
        {
            _w.Reset();
            _w.Put((byte)msg);
            _w.Put(playerId);
        }

        private void SendFromLocal(DeliveryMethod method)
        {
            if (IsHost)
                _net.SendToAll(_w, method);
            else
                _server?.Send(_w, method);
        }

        // ------------------------------------------------------------------ LiteNetLib callbacks

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (!IsHost)
            {
                request.RejectForce();
                return;
            }

            string error = null;
            PlayerInfo info = null;
            try
            {
                var r = request.Data;
                string magic = r.GetString(16);
                ushort version = r.GetUShort();
                string password = r.GetString(64);
                info = PlayerInfo.Read(r);

                if (magic != Protocol.Magic)
                    error = "Not a Normal Golf Multiplayer client";
                else if (version != Protocol.Version)
                    error = $"Version mismatch: host uses protocol v{Protocol.Version}, you have v{version}. Update the mod.";
                else if (!string.IsNullOrEmpty(ModConfig.Password.Value) && password != ModConfig.Password.Value)
                    error = "Wrong password";
                else if (_peerToPlayer.Count + 1 >= ModConfig.MaxPlayers.Value) // +1 = the host; counts joins still handshaking
                    error = "Session is full";
            }
            catch (Exception)
            {
                error = "Malformed join request";
            }

            byte id = error == null ? AllocateId() : (byte)0;
            if (error == null && id == 0)
                error = "Session is full";

            if (error != null)
            {
                Plugin.Log.LogInfo($"Rejected {request.RemoteEndPoint}: {error}");
                var w = new NetDataWriter();
                w.Put(error);
                request.Reject(w);
                return;
            }

            info.Id = id;
            info.Name = UniqueName(info.Name);
            NetPeer peer = request.Accept();
            if (peer == null)
                return;
            peer.Tag = info;
            // Reserve the id immediately so two simultaneous joins can't be handed the same one.
            _peerToPlayer[peer.Id] = id;
        }

        private void OnPeerConnected(NetPeer peer)
        {
            if (IsHost)
            {
                if (!(peer.Tag is PlayerInfo info))
                {
                    _net.DisconnectPeer(peer);
                    return;
                }

                _playerToPeer[info.Id] = peer;

                Begin(Msg.Welcome, Protocol.HostId);
                info.Write(_w);
                _w.Put((byte)Players.Count);
                foreach (var p in Players.Values)
                    p.Write(_w);
                peer.Send(_w, DeliveryMethod.ReliableOrdered);

                Players[info.Id] = info;

                Begin(Msg.PlayerJoined, info.Id);
                info.Write(_w);
                _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);

                Plugin.Log.LogInfo($"{info.Name} joined from {peer} as player {info.Id}");
                PlayerJoined?.Invoke(info);
            }
            else if (peer == _server)
            {
                Mode = SessionMode.Connected;
                Status = $"Connected to {Endpoint}, waiting for welcome...";
            }
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (IsHost)
            {
                if (!_peerToPlayer.TryGetValue(peer.Id, out byte id))
                    return;
                _peerToPlayer.Remove(peer.Id);
                _playerToPeer.Remove(id);
                _chatLimits.Remove(id);
                if (!Players.TryGetValue(id, out var player))
                    return; // disconnected before finishing the handshake
                Players.Remove(id);

                Begin(Msg.PlayerLeft, id);
                _net.SendToAll(_w, DeliveryMethod.ReliableOrdered);

                Plugin.Log.LogInfo($"{player.Name} left ({info.Reason})");
                PlayerLeft?.Invoke(player);
                return;
            }

            if (peer != _server)
                return;

            string reason;
            switch (info.Reason)
            {
                case DisconnectReason.ConnectionRejected:
                    reason = "Join refused: " + (ReadReason(info) ?? "rejected by host");
                    break;
                case DisconnectReason.RemoteConnectionClose:
                    reason = ReadReason(info) ?? "The host ended the session";
                    break;
                case DisconnectReason.ConnectionFailed:
                    reason = $"Could not reach {Endpoint}. Check the IP/port, that the host is hosting, and that UDP {Endpoint.Split(':').Last()} is forwarded/allowed.";
                    break;
                case DisconnectReason.Timeout:
                    reason = "Connection timed out";
                    break;
                case DisconnectReason.HostUnreachable:
                case DisconnectReason.NetworkUnreachable:
                    reason = "Host unreachable (network error)";
                    break;
                case DisconnectReason.UnknownHost:
                    reason = $"Unknown host '{Endpoint}'";
                    break;
                default:
                    reason = "Disconnected (" + info.Reason + ")";
                    break;
            }
            Shutdown(reason);
        }

        private static string ReadReason(DisconnectInfo info)
        {
            try
            {
                var data = info.AdditionalData;
                if (data != null && data.AvailableBytes > 0)
                    return data.GetString();
            }
            catch
            {
                // no readable reason attached
            }
            return null;
        }

        private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            try
            {
                var msg = (Msg)reader.GetByte();
                byte id = reader.GetByte();
                if (IsHost)
                    HandleOnHost(peer, msg, reader);
                else
                    HandleOnClient(msg, id, reader);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Bad packet from {peer}: {e.Message}");
            }
        }

        private void HandleOnHost(NetPeer peer, Msg msg, NetDataReader r)
        {
            if (!_peerToPlayer.TryGetValue(peer.Id, out byte from) || !Players.TryGetValue(from, out var player))
                return;

            switch (msg)
            {
                case Msg.State:
                {
                    var s = PlayerState.Read(r);
                    StateReceived?.Invoke(from, s);
                    Begin(Msg.State, from);
                    s.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.Unreliable, peer);
                    break;
                }
                case Msg.PlayerInfo:
                {
                    var info = PlayerInfo.Read(r);
                    // Name stays whatever the host assigned at join; only cosmetics can change.
                    player.Color = info.Color;
                    player.BallLook = info.BallLook;
                    Begin(Msg.PlayerInfo, from);
                    player.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    PlayerInfoChanged?.Invoke(player);
                    break;
                }
                case Msg.Shot:
                {
                    var shot = ShotEvent.Read(r);
                    ShotReceived?.Invoke(from, shot);
                    Begin(Msg.Shot, from);
                    shot.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    break;
                }
                case Msg.Holed:
                {
                    var holed = HoledEvent.Read(r);
                    HoledReceived?.Invoke(from, holed);
                    Begin(Msg.Holed, from);
                    holed.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    break;
                }
                case Msg.Score:
                {
                    var card = ScoreCard.Read(r);
                    ScoreReceived?.Invoke(from, card);
                    Begin(Msg.Score, from);
                    card.Write(_w);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    break;
                }
                case Msg.Chat:
                {
                    string text = Protocol.Clean(r.GetString(Protocol.MaxChatLength * 2), Protocol.MaxChatLength);
                    if (text.Length == 0)
                        break;
                    if (!_chatLimits.TryGetValue(from, out var limiter))
                        _chatLimits[from] = limiter = new ChatLimiter();
                    if (!limiter.Allow(Time.unscaledTime))
                        break;
                    ChatReceived?.Invoke(from, text);
                    Begin(Msg.Chat, from);
                    _w.Put(text, Protocol.MaxChatLength * 2);
                    _net.SendToAll(_w, DeliveryMethod.ReliableOrdered, peer);
                    break;
                }
            }
        }

        private void HandleOnClient(Msg msg, byte id, NetDataReader r)
        {
            switch (msg)
            {
                case Msg.Welcome:
                {
                    var me = PlayerInfo.Read(r);
                    LocalId = me.Id;
                    LocalInfo = me;
                    Players.Clear();
                    Players[LocalId] = LocalInfo;
                    int count = r.GetByte();
                    var others = new List<PlayerInfo>();
                    for (int i = 0; i < count; i++)
                    {
                        var p = PlayerInfo.Read(r);
                        if (p.Id == LocalId)
                            continue;
                        Players[p.Id] = p;
                        others.Add(p);
                    }
                    Status = $"Connected to {Endpoint}";
                    Plugin.Log.LogInfo($"Joined {Endpoint} as player {LocalId} '{LocalInfo.Name}' with {others.Count} other player(s)");
                    OnSessionBegan();
                    foreach (var p in others)
                        PlayerJoined?.Invoke(p);
                    break;
                }
                case Msg.PlayerJoined:
                {
                    var p = PlayerInfo.Read(r);
                    if (p.Id == LocalId)
                        break;
                    Players[p.Id] = p;
                    PlayerJoined?.Invoke(p);
                    break;
                }
                case Msg.PlayerLeft:
                    if (Players.TryGetValue(id, out var left) && id != LocalId)
                    {
                        Players.Remove(id);
                        PlayerLeft?.Invoke(left);
                    }
                    break;
                case Msg.PlayerInfo:
                {
                    var p = PlayerInfo.Read(r);
                    if (p.Id == LocalId)
                        break;
                    if (Players.TryGetValue(p.Id, out var existing))
                    {
                        existing.Name = p.Name;
                        existing.Color = p.Color;
                        existing.BallLook = p.BallLook;
                        PlayerInfoChanged?.Invoke(existing);
                    }
                    break;
                }
                case Msg.State:
                    if (id != LocalId && id != 0)
                        StateReceived?.Invoke(id, PlayerState.Read(r));
                    break;
                case Msg.Shot:
                    if (id != LocalId)
                        ShotReceived?.Invoke(id, ShotEvent.Read(r));
                    break;
                case Msg.Holed:
                    if (id != LocalId)
                        HoledReceived?.Invoke(id, HoledEvent.Read(r));
                    break;
                case Msg.Score:
                    if (id != LocalId)
                        ScoreReceived?.Invoke(id, ScoreCard.Read(r));
                    break;
                case Msg.Chat:
                    if (id != LocalId)
                        ChatReceived?.Invoke(id, Protocol.Clean(r.GetString(Protocol.MaxChatLength * 2), Protocol.MaxChatLength));
                    break;
            }
        }

        // ------------------------------------------------------------------ helpers

        private byte AllocateId()
        {
            for (int i = Protocol.HostId + 1; i < 255; i++)
            {
                byte id = (byte)i;
                if (!Players.ContainsKey(id) && !_peerToPlayer.ContainsValue(id))
                    return id;
            }
            return 0;
        }

        private string UniqueName(string name)
        {
            string candidate = name;
            for (int n = 2; Players.Values.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
                candidate = Protocol.Clean(name, Protocol.MaxNameLength - 4) + $" ({n})";
            return candidate;
        }

        public int GetPlayerPing(byte id)
        {
            if (id == LocalId)
                return 0;
            if (IsHost)
                return _playerToPeer.TryGetValue(id, out var peer) ? peer.RoundTripTime : 0;
            return PingMs; // clients only know their own latency to the host
        }

        private class ChatLimiter
        {
            private readonly Queue<float> _times = new Queue<float>();

            public bool Allow(float now)
            {
                while (_times.Count > 0 && now - _times.Peek() > 5f)
                    _times.Dequeue();
                if (_times.Count >= 6)
                    return false;
                _times.Enqueue(now);
                return true;
            }
        }
    }
}
